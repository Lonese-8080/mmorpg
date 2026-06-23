# MMORPG 服务端框架 —— ECS 架构详细设计

> **文档编号**: ECS-001  
> **版本**: v1.0.0  
> **作者**: 与伙伴共同编写  
> **最后更新**: 2026-06-21

---

## ⚠️ 注意：规划文档（尚未实现）

本文档描述的 ECS 架构为**设计稿**，相关代码尚未实现。

**实际已实现的模块：**
- `MMORPG.Framework.Network` — IOCP 网络层（已实现）
- `MMORPG.Framework.Logging` — 结构化日志（已实现）
- `MMORPG.Framework.Threading` — 主循环调度器、消息队列、雪花算法（已实现）
- `MMORPG.Framework.Configuration` — 配置类（已实现）

**待开发模块：**
- `MMORPG.Core.ECS` — 本文件规划的内容
- 数据持久化层
- 场景系统 / AOI / 九宫格
- 登录认证系统

> **设计稿 — 待实现**：以下内容为架构设计与规划，供未来开发参考，当前代码库中不存在对应实现。

---

## 目录

1. [设计背景](#1-设计背景)
2. [核心概念](#2-核心概念)
3. [Archetype 模式详解](#3-archetype-模式详解)
4. [数据结构设计](#4-数据结构设计)
5. [系统设计](#5-系统设计)
6. [查询优化](#6-查询优化)
7. [并发与线程安全](#7-并发与线程安全)
8. [组件设计规范](#8-组件设计规范)
9. [使用示例](#9-使用示例)

---

## 1. 设计背景

### 1.1 什么是 ECS？

```
ECS = Entity Component System（实体组件系统）

三个核心概念：

┌─────────────────────────────────────────────────────────────────┐
│                                                                  │
│  Entity（实体）                                                  │
│  ┌─────────────────────────────────────────────────────────┐     │
│  │                                                          │     │
│  │   一个"身份标识"，本身不包含任何数据或逻辑              │     │
│  │                                                          │     │
│  │   例：玩家#12345、NPC#67890、怪物#11111                 │     │
│  │                                                          │     │
│  └─────────────────────────────────────────────────────────┘     │
│                                                                  │
│  Component（组件）                                                │
│  ┌─────────────────────────────────────────────────────────┐     │
│  │                                                          │     │
│  │   纯粹的"数据容器"，没有逻辑方法                        │     │
│  │                                                          │     │
│  │   例：                                                   │     │
│  │   - PositionComponent { X, Y, Z }                      │     │
│  │   - HealthComponent { CurrentHp, MaxHp }               │     │
│  │   - NameComponent { Name }                             │     │
│  │                                                          │     │
│  └─────────────────────────────────────────────────────────┘     │
│                                                                  │
│  System（系统）                                                   │
│  ┌─────────────────────────────────────────────────────────┐     │
│  │                                                          │     │
│  │   处理"一类逻辑"的机器，只关心特定组件组合               │     │
│  │                                                          │     │
│  │   例：                                                   │     │
│  │   - MovementSystem：处理所有有 Position 的实体          │     │
│  │   - BattleSystem：处理所有有 Health 的实体             │     │
│  │   - AISystem：处理所有有 AI 状态的实体                  │     │
│  │                                                          │     │
│  └─────────────────────────────────────────────────────────┘     │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 1.2 为什么不用传统 OOP？

```
传统 OOP 方式的问题：

┌─────────────────────────────────────────────────────────────────┐
│                    Player : MonoBehaviour                       │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │                                                           │  │
│  │   // 数据                                                  │  │
│  │   position: Vector3                                        │  │
│  │   health: int                                              │  │
│  │   attack: int                                             │  │
│  │   defense: int                                            │  │
│  │   skills: Skill[]                                         │  │
│  │   equipment: Equipment[]                                   │  │
│  │   inventory: Inventory                                    │  │
│  │   buffs: Buff[]                                           │  │
│  │   ...（可能有几十上百个字段）                               │  │
│  │                                                           │  │
│  │   // 逻辑                                                  │  │
│  │   MoveTo(target) { ... }                                 │  │
│  │   TakeDamage(amount) { ... }                             │  │
│  │   UseSkill(skillId) { ... }                              │  │
│  │   AddBuff(buff) { ... }                                  │  │
│  │                                                           │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘

问题1：数据和方法混在一起
   - 每次修改都要找这个类
   - 容易违反单一职责

问题2：难以共享逻辑
   - NPC 和 Player 有相同的移动逻辑
   - 但它们是不同的类
   - 只能继承或多态，不好维护

问题3：内存不连续
   - Player 对象在内存各处散落
   - CPU 读取数据要跳来跳去
   - 缓存命中率低，性能差

问题4：难以并行
   - 多个系统要访问同一个对象
   - 需要加锁或其他同步机制
```

### 1.3 ECS 的优势

```
ECS 方式的优势：

┌─────────────────────────────────────────────────────────────────┐
│                                                                  │
│  1. 数据和逻辑分离                                                │
│  ┌───────────────────────┐                                       │
│  │  Component（数据）     │   ┌───────────────────────┐          │
│  │  - Position           │   │  System（逻辑）        │          │
│  │  - Health             │   │  - MovementSystem     │          │
│  │  - Attack             │   │  - BattleSystem       │          │
│  │  - Defense            │   │  - AISystem           │          │
│  │  - Name               │   │  - BuffSystem         │          │
│  └───────────────────────┘   └───────────────────────┘          │
│                                                                  │
│  2. 组件自由组合                                                  │
│                                                                  │
│  玩家实体 = Position + Health + Attack + Defense + Name + ...   │
│  怪物实体 = Position + Health + Attack + AIState               │
│  树实体   = Position + Name                                     │
│                                                                  │
│  3. 内存连续                                                     │
│                                                                  │
│  同一个 Archetype 下的所有实体，相同组件在内存中是连续排列的：      │
│                                                                  │
│  ┌──────────┬──────────┬──────────┬──────────┬──────────┐       │
│  │ Position │ Position │ Position │ Position │ Position │       │
│  │ 玩家#1  │ 玩家#2  │ 玩家#3  │ 怪物#1  │ 怪物#2  │       │
│  └──────────┴──────────┴──────────┴──────────┴──────────┘       │
│  ┌──────────┬──────────┬──────────┬──────────┬──────────┐       │
│  │ Health   │ Health   │ Health   │ Health   │ Health   │       │
│  │ 玩家#1  │ 玩家#2  │ 玩家#3  │ 怪物#1  │ 怪物#2  │       │
│  └──────────┴──────────┴──────────┴──────────┴──────────┘       │
│           ▲ 连续内存，CPU 一次读取一大片，缓存命中率高！           │
│                                                                  │
│  4. 批量处理                                                     │
│                                                                  │
│  MovementSystem 遍历所有有 Position 的实体：                      │
│  for (每个实体)                                                  │
│  {                                                               │
│      // CPU 会把这几个实体的数据一起加载到缓存                     │
│      position.X += velocity.X * deltaTime;                       │
│      position.Y += velocity.Y * deltaTime;                       │
│      position.Z += velocity.Z * deltaTime;                       │
│  }                                                               │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. 核心概念

### 2.1 Entity（实体）

```csharp
/// <summary>
/// 实体 - ECS 中的"身份标识"
/// 
/// 特点：
/// 1. 只是一个 64 位整数 ID
/// 2. 本身不包含任何数据或逻辑
/// 3. 通过 ID 在 World 中查找和管理
/// 
/// ID 格式：
/// ┌─────────────────────────────────────────────────────────────────┐
/// │  63-48 位 (16位) │  47-32 位 (16位) │  31-0 位 (32位)           │
/// ├──────────────────┼──────────────────┼──────────────────────────┤
/// │    版本号         │   Archetype ID   │   在 Archetype 中的索引   │
/// └──────────────────┴──────────────────┴──────────────────────────┘
/// 
/// 设计理由：
/// - 索引：快速定位数据在内存中的位置
/// - Archetype ID：快速判断实体属于哪个原型
/// - 版本号：处理"删除后复用 ID"的情况（类似对象池）
/// 
/// 版本号的作用：
/// 假设实体#12345 被删除
/// 如果直接复用，新实体可能读到旧的脏数据
/// 如果新版本号 = 2，主线程会发现版本不匹配，拒绝访问
/// </summary>
public readonly struct Entity : IEquatable<Entity>, IComparable<Entity>
{
    // ============================================================
    // 字段
    // ============================================================
    
    /// <summary>
    /// 实体 ID（64位整数）
    /// 
    /// 位布局：
    /// - 高 16 位：版本号
    /// - 中 16 位：Archetype ID
    /// - 低 32 位：在 Archetype 中的索引
    /// </summary>
    private readonly ulong _id;
    
    // ============================================================
    // 属性
    // ============================================================
    
    /// <summary>
    /// 版本号
    /// 
    /// 每次实体被销毁并重建时递增
    /// 用于检测"悬挂指针"问题
    /// </summary>
    public int Version => (int)(_id >> 48);
    
    /// <summary>
    /// Archetype ID
    /// 
    /// 标识这个实体属于哪个原型
    /// 用于快速查找实体所在的 Archetype
    /// </summary>
    public ushort ArchetypeId => (ushort)(_id >> 32);
    
    /// <summary>
    /// 在 Archetype 中的索引
    /// 
    /// 索引指向实体数据在 ComponentArrays 中的位置
    /// </summary>
    public uint Index => (uint)_id;
    
    // ============================================================
    // 构造函数
    // ============================================================
    
    /// <summary>
    /// 内部构造函数
    /// 
    /// 外部不应该直接创建 Entity
    /// 应该通过 World.CreateEntity() 创建
    /// </summary>
    internal Entity(ushort archetypeId, uint index, int version)
    {
        _id = ((ulong)version << 48) | ((ulong)archetypeId << 32) | index;
    }
    
    // ============================================================
    // 方法
    // ============================================================
    
    /// <summary>
    /// 判断两个实体是否相等
    /// 
    /// 注意：只比较 ID，不比较版本号
    /// 这样可以用同一个 Entity 对象多次访问
    /// </summary>
    public bool Equals(Entity other)
    {
        return _id == other._id;
    }
    
    public override bool Equals(object obj)
    {
        return obj is Entity other && Equals(other);
    }
    
    public override int GetHashCode()
    {
        return _id.GetHashCode();
    }
    
    public int CompareTo(Entity other)
    {
        return _id.CompareTo(other._id);
    }
    
    public static bool operator ==(Entity left, Entity right)
    {
        return left.Equals(right);
    }
    
    public static bool operator !=(Entity left, Entity right)
    {
        return !left.Equals(right);
    }
    
    /// <summary>
    /// 空实体（无效）
    /// </summary>
    public static readonly Entity Null = default;
    
    /// <summary>
    /// 判断是否是空实体
    /// </summary>
    public bool IsNull => _id == 0;
    
    public override string ToString()
    {
        return $"Entity[{Version}:A{ArchetypeId}:I{Index}]";
    }
}

/// <summary>
/// 空实体的便捷常量
/// </summary>
public static class EntityExtensions
{
    /// <summary>
    /// 判断实体是否有效
    /// 
    /// 使用示例：
    /// ```csharp
    /// if (entity.IsValid())
    /// {
    ///     // 安全访问
    /// }
    /// ```
    /// </summary>
    public static bool IsValid(this Entity entity)
    {
        return !entity.IsNull;
    }
}
```

### 2.2 Component（组件）

```csharp
/// <summary>
/// 组件 - ECS 中的纯数据容器
/// 
/// 设计原则：
/// 1. 必须是 struct（值类型），不能是 class
///    └─ 值类型在数组中连续存储
///    └─ 引用类型会造成对象头和 GC 开销
/// 
/// 2. 不能有方法（可以有属性）
///    └─ 纯数据，避免混入逻辑
/// 
/// 3. 不能有构造函数参数
///    └─ 保证可以无参构造
///    └─ 内存布局简单
/// 
/// 4. 避免 GC 分配
///    └─ 不要在组件中使用 string、数组、List 等引用类型
///    └─ 使用 Span<T> 或固定大小的数组
/// 
/// 组件类型注册：
/// 
/// 每个组件类型都需要注册一个类型 ID
/// 用于在 Archetype 中标识组件类型
/// </summary>
public interface IComponent
{
    /// <summary>
    /// 组件类型 ID
    /// </summary>
    static abstract int ComponentTypeId { get; }
}

/// <summary>
/// 组件类型 ID 分配器
/// 
/// 自动为每个组件分配唯一 ID
/// </summary>
public static class ComponentTypeId
{
    private static int _nextId = 0;
    
    /// <summary>
    /// 获取下一个可用的组件类型 ID
    /// </summary>
    public static int Allocate()
    {
        return Interlocked.Increment(ref _nextId);
    }
}

/// <summary>
/// 组件特性
/// 
/// 用于自动注册组件类型
/// 
/// 使用示例：
/// ```csharp
/// [Component]
/// public struct PositionComponent : IComponent
/// {
///     public static int ComponentTypeId { get; } = ComponentTypeId.Allocate();
///     public float X, Y, Z;
/// }
/// ```
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public class ComponentAttribute : Attribute { }

// ============================================================
// 内置组件示例
// ============================================================

/// <summary>
/// 位置组件 - 存储实体的世界坐标
/// 
/// 使用场景：所有需要知道"在哪里"的实体
/// - 玩家
/// - NPC
/// - 怪物
/// - 掉落物品
/// - 特效
/// </summary>
[Component]
public struct PositionComponent : IComponent
{
    /// <summary>
    /// 组件类型 ID
    /// </summary>
    public static int ComponentTypeId { get; } = ComponentTypeId.Allocate();
    
    /// <summary>
    /// X 坐标（米）
    /// </summary>
    public float X;
    
    /// <summary>
    /// Y 坐标（米）
    /// </summary>
    public float Y;
    
    /// <summary>
    /// Z 坐标（高度，米）
    /// </summary>
    public float Z;
    
    /// <summary>
    /// 便捷构造函数
    /// </summary>
    public PositionComponent(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
    
    /// <summary>
    /// 从另一个向量复制
    /// </summary>
    public PositionComponent(Vector3 vector)
    {
        X = vector.X;
        Y = vector.Y;
        Z = vector.Z;
    }
    
    /// <summary>
    /// 距离另一个位置的平方（避免开方运算）
    /// </summary>
    public float SqrDistanceTo(PositionComponent other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return dx * dx + dy * dy + dz * dz;
    }
    
    /// <summary>
    /// 距离另一个位置
    /// </summary>
    public float DistanceTo(PositionComponent other)
    {
        return MathF.Sqrt(SqrDistanceTo(other));
    }
}

/// <summary>
/// 速度组件 - 存储实体的移动速度
/// 
/// 使用场景：所有会移动的实体
/// </summary>
[Component]
public struct VelocityComponent : IComponent
{
    public static int ComponentTypeId { get; } = ComponentTypeId.Allocate();
    
    /// <summary>
    /// X 轴速度（米/秒）
    /// </summary>
    public float X;
    
    /// <summary>
    /// Y 轴速度（米/秒）
    /// </summary>
    public float Y;
    
    /// <summary>
    /// Z 轴速度（米/秒）
    /// </summary>
    public float Z;
    
    /// <summary>
    /// 速度大小（标量）
    /// </summary>
    public float Speed;
}

/// <summary>
/// 生命值组件 - 存储实体的生命状态
/// 
/// 使用场景：所有可以被攻击/杀死的实体
/// - 玩家
/// - 怪物
/// - NPC
/// </summary>
[Component]
public struct HealthComponent : IComponent
{
    public static int ComponentTypeId { get; } = ComponentTypeId.Allocate();
    
    /// <summary>
    /// 当前生命值
    /// </summary>
    public int CurrentHp;
    
    /// <summary>
    /// 最大生命值
    /// </summary>
    public int MaxHp;
    
    /// <summary>
    /// 生命百分比（0-100）
    /// </summary>
    public float Percent => MaxHp > 0 ? (float)CurrentHp / MaxHp * 100f : 0f;
    
    /// <summary>
    /// 是否已经死亡
    /// </summary>
    public bool IsDead => CurrentHp <= 0;
    
    /// <summary>
    /// 受伤
    /// </summary>
    public void TakeDamage(int damage)
    {
        CurrentHp = Math.Max(0, CurrentHp - damage);
    }
    
    /// <summary>
    /// 治疗
    /// </summary>
    public void Heal(int amount)
    {
        CurrentHp = Math.Min(MaxHp, CurrentHp + amount);
    }
}

/// <summary>
/// 攻击力组件 - 存储实体的攻击属性
/// 
/// 使用场景：所有能造成伤害的实体
/// </summary>
[Component]
public struct AttackComponent : IComponent
{
    public static int ComponentTypeId { get; } = ComponentTypeId.Allocate();
    
    /// <summary>
    /// 基础攻击力
    /// </summary>
    public int BaseAttack;
    
    /// <summary>
    /// 暴击率（0-100）
    /// </summary>
    public float CritRate;
    
    /// <summary>
    /// 暴击伤害倍率
    /// </summary>
    public float CritDamage;
}

/// <summary>
/// 名称组件 - 存储实体的显示名称
/// 
/// 注意：字符串会造成 GC
/// 建议尽量少用，或使用 StringPool
/// </summary>
[Component]
public struct NameComponent : IComponent
{
    public static int ComponentTypeId { get; } = ComponentTypeId.Allocate();
    
    /// <summary>
    /// 名称
    /// 
    /// 警告：string 会造成 GC 压力
    /// 建议使用 StringId 或字符串池
    /// </summary>
    public string Name;
}

/// <summary>
/// 玩家控制组件 - 标记由玩家控制的实体
/// 
/// 使用场景：玩家角色
/// </summary>
[Component]
public struct PlayerControlledComponent : IComponent
{
    public static int ComponentTypeId { get; } = ComponentTypeId.Allocate();
    
    /// <summary>
    /// 玩家 ID（从数据库来的）
    /// </summary>
    public long PlayerId;
    
    /// <summary>
    /// 连接的 Session ID
    /// </summary>
    public long SessionId;
}

/// <summary>
/// AI 状态组件 - 存储实体的 AI 状态
/// 
/// 使用场景：怪物、NPC
/// </summary>
[Component]
public struct AIStateComponent : IComponent
{
    public static int ComponentTypeId { get; } = ComponentTypeId.Allocate();
    
    /// <summary>
    /// AI 状态枚举
    /// </summary>
    public AIState State;
    
    /// <summary>
    /// 状态持续时间
    /// </summary>
    public float StateDuration;
    
    /// <summary>
    /// 目标实体 ID
    /// </summary>
    public Entity Target;
}

/// <summary>
/// AI 状态枚举
/// </summary>
public enum AIState
{
    /// <summary>空闲</summary>
    Idle = 0,
    
    /// <summary>巡逻</summary>
    Patrol = 1,
    
    /// <summary>追击</summary>
    Chase = 2,
    
    /// <summary>攻击</summary>
    Attack = 3,
    
    /// <summary>返回</summary>
    Return = 4,
    
    /// <summary>死亡</summary>
    Dead = 5
}
```

---

## 3. Archetype 模式详解

### 3.1 什么是 Archetype？

```
Archetype（原型）= 相同组件组合的实体集合

示例：

Archetype A = {Position, Health, Name}
┌──────────┬──────────┬──────────┐
│ Position │  Health  │   Name   │
├──────────┼──────────┼──────────┤
│ P1       │  H1      │  "玩家A" │
├──────────┼──────────┼──────────┤
│ P2       │  H2      │  "玩家B" │
├──────────┼──────────┼──────────┤
│ P3       │  H3      │  "玩家C" │
└──────────┴──────────┴──────────┘

Archetype B = {Position, Health, Attack, AIState}
┌──────────┬──────────┬──────────┬──────────┐
│ Position │  Health  │  Attack  │ AIState  │
├──────────┼──────────┼──────────┼──────────┤
│ P4       │  H4      │  A1      │  AI1     │
├──────────┼──────────┼──────────┼──────────┤
│ P5       │  H5      │  A2      │  AI2     │
└──────────┴──────────┴──────────┴──────────┘

Archetype C = {Position, Name}
┌──────────┬──────────┐
│ Position │   Name   │
├──────────┼──────────┤
│ P6       │  "树#1" │
└──────────┴──────────┘

内存布局优势：

Archetype A 的 Position 数组：
[ P1 ][ P2 ][ P3 ][    ][    ][    ]  ← 连续内存！

CPU 读取 P1, P2, P3 时：
- 读取 P1 时，CPU 会把 P1 附近的内存也加载到缓存
- 读取 P2, P3 时，直接从缓存拿，不需要去内存读
- 性能提升 10-100 倍！
```

### 3.2 Archetype 数据结构

```csharp
/// <summary>
/// 原型 - 相同组件组合的实体集合
/// 
/// 核心思想：
/// 相同组件组合的实体，数据在内存中连续存储
/// 
/// 数据结构：
/// ┌─────────────────────────────────────────────────────────────┐
/// │                    Archetype                                │
/// │  ┌─────────────────────────────────────────────────────┐   │
/// │  │  ComponentTypes: int[]  ← 这个原型有哪些组件        │   │
/// │  │  例如：[Position, Health, Attack]                   │   │
/// │  └─────────────────────────────────────────────────────┘   │
/// │  ┌─────────────────────────────────────────────────────┐   │
/// │  │  ComponentArrays: object[]  ← 每种组件的数据数组    │   │
/// │  │  ComponentArrays[0]: PositionComponent[]           │   │
/// │  │  ComponentArrays[1]: HealthComponent[]              │   │
/// │  │  ComponentArrays[2]: AttackComponent[]             │   │
/// │  └─────────────────────────────────────────────────────┘   │
/// │  ┌─────────────────────────────────────────────────────┐   │
/// │  │  Entities: Entity[]  ← 这个原型下的所有实体         │   │
/// │  │  Entities[0]: Entity (玩家#1)                      │   │
/// │  │  Entities[1]: Entity (玩家#2)                      │   │
/// │  └─────────────────────────────────────────────────────┘   │
/// │  ┌─────────────────────────────────────────────────────┐   │
/// │  │  Count: int  ← 当前实体的数量                        │   │
/// │  └─────────────────────────────────────────────────────┘   │
/// └─────────────────────────────────────────────────────────────┘
/// 
/// 内存布局（以 Archetype A = {Position, Health} 为例）：
/// 
/// 索引:    0         1         2         3         4
///       ┌─────────┬─────────┬─────────┬─────────┬─────────┐
/// Pos   │ 玩家#1  │ 玩家#2  │ 玩家#3  │  (空)   │  (空)   │
///       └─────────┴─────────┴─────────┴─────────┴─────────┘
///       ┌─────────┬─────────┬─────────┬─────────┬─────────┐
/// Hlth  │ HP1     │ HP2     │ HP3     │  (空)   │  (空)   │
///       └─────────┴─────────┴─────────┴─────────┴─────────┘
///       ┌─────────┬─────────┬─────────┬─────────┬─────────┐
/// Ent   │ Ent#1   │ Ent#2   │ Ent#3   │  (空)   │  (空)   │
///       └─────────┴─────────┴─────────┴─────────┴─────────┘
/// 
/// 关键特性：
/// 1. 数组按需扩容（类似 List）
/// 2. 删除实体时用"末尾交换"策略
/// 3. 添加组件时可能需要迁移到新的 Archetype
/// </summary>
public sealed class Archetype
{
    // ============================================================
    // 字段
    // ============================================================
    
    /// <summary>
    /// 原型 ID（唯一标识）
    /// </summary>
    public readonly ushort Id;
    
    /// <summary>
    /// 组件类型 ID 数组（排序过，用于比较）
    /// 
    /// 例如：[PositionComponent.ComponentTypeId, HealthComponent.ComponentTypeId]
    /// </summary>
    public readonly int[] ComponentTypes;
    
    /// <summary>
    /// 组件数据数组（object[] 是为了通用性）
    /// 
    /// ComponentArrays[i] 对应 ComponentTypes[i]
    /// 每个数组长度相同，用 Count 控制有效数据
    /// </summary>
    internal object[] ComponentArrays;
    
    /// <summary>
    /// 实体数组
    /// </summary>
    internal Entity[] Entities;
    
    /// <summary>
    /// 当前实体数量
    /// </summary>
    internal int Count;
    
    /// <summary>
    /// 容量（数组大小）
    /// </summary>
    internal int Capacity;
    
    // ============================================================
    // 构造函数
    // ============================================================
    
    /// <summary>
    /// 创建新原型
    /// </summary>
    public Archetype(ushort id, int[] componentTypes)
    {
        Id = id;
        ComponentTypes = componentTypes;
        Count = 0;
        Capacity = 4;  // 初始容量
        
        // 为每种组件类型创建数组
        ComponentArrays = new object[componentTypes.Length];
        for (int i = 0; i < componentTypes.Length; i++)
        {
            ComponentArrays[i] = CreateArray(componentTypes[i], Capacity);
        }
        
        Entities = new Entity[Capacity];
    }
    
    // ============================================================
    // 方法
    // ============================================================
    
    /// <summary>
    /// 添加实体到原型
    /// 
    /// 执行步骤：
    /// 1. 检查容量，不够就扩容
    /// 2. 在末尾添加
    /// 3. 复制组件数据
    /// </summary>
    internal void Add(Entity entity, ref byte componentsData)
    {
        // 扩容检查
        if (Count >= Capacity)
        {
            Grow();
        }
        
        // 添加到末尾
        var index = Count;
        Entities[index] = entity;
        Count++;
        
        // 从 componentsData 中复制组件数据
        // 具体实现见 ArchetypeBuilder
    }
    
    /// <summary>
    /// 移除实体
    /// 
    /// 使用末尾交换策略：
    /// 假设要删除索引 2 的实体：
    /// 
    /// 之前：
    /// 索引:  0    1    2    3    4
    ///      [A]  [B]  [C]  [D]  [E]
    /// 
    /// 交换后（把 E 移到 2）：
    /// 索引:  0    1    2    3    4
    ///      [A]  [B]  [E]  [D]  [_]  ← C 被删除
    /// 
    /// 优点：不需要移动多个数组元素
    /// 缺点：实体顺序变了
    /// </summary>
    internal void Remove(int index, out Entity removedEntity)
    {
        removedEntity = Entities[index];
        Count--;
        
        // 末尾交换
        if (index < Count)
        {
            // 交换实体
            Entities[index] = Entities[Count];
            
            // 交换所有组件数组
            for (int i = 0; i < ComponentArrays.Length; i++)
            {
                SwapComponentAt(ComponentArrays[i], index, Count);
            }
        }
        
        // 清空末尾
        Entities[Count] = Entity.Null;
    }
    
    /// <summary>
    /// 获取组件数组（泛型版本）
    /// 
    /// 使用示例：
    /// ```csharp
    /// var positions = archetype.GetArray<PositionComponent>();
    /// ref var pos0 = ref positions[0];
    /// ```
    /// </summary>
    public T[] GetArray<T>() where T : struct
    {
        var typeId = ComponentType<T>.Id;
        var index = Array.BinarySearch(ComponentTypes, typeId);
        return (T[])ComponentArrays[index];
    }
    
    // ============================================================
    // 私有方法
    // ============================================================
    
    /// <summary>
    /// 扩容
    /// </summary>
    private void Grow()
    {
        var newCapacity = Capacity * 2;
        
        // 扩容所有数组
        Entities = ResizeArray(Entities, newCapacity);
        
        for (int i = 0; i < ComponentArrays.Length; i++)
        {
            ComponentArrays[i] = ResizeArray(ComponentArrays[i], newCapacity);
        }
        
        Capacity = newCapacity;
    }
}
```

### 3.3 World（世界）

```csharp
/// <summary>
/// 世界 - ECS 的总管理器
/// 
/// 职责：
/// 1. 管理所有 Archetype
/// 2. 创建/销毁实体
/// 3. 添加/移除组件
/// 4. 查询实体
/// 5. 执行系统
/// 
/// 线程安全说明：
/// - 所有操作在主线程执行
/// - 不需要任何锁
/// 
/// 使用示例：
/// ```csharp
/// var world = new World();
/// 
/// // 创建实体
/// var player = world.CreateEntity();
/// world.AddComponent(player, new PositionComponent { X = 0, Y = 0, Z = 0 });
/// world.AddComponent(player, new HealthComponent { CurrentHp = 100, MaxHp = 100 });
/// 
/// // 查询
/// foreach (ref var pos in world.Query<PositionComponent>())
/// {
///     Console.WriteLine($"位置: {pos.X}, {pos.Y}");
/// }
/// 
/// // 删除实体
/// world.DestroyEntity(player);
/// ```
/// </summary>
public sealed class World
{
    // ============================================================
    // 字段
    // ============================================================
    
    /// <summary>
    /// 所有原型
    /// </summary>
    private Archetype[] _archetypes = new Archetype[256];
    
    /// <summary>
    /// 原型数量
    /// </summary>
    private int _archetypeCount = 0;
    
    /// <summary>
    /// 下一个 Archetype ID
    /// </summary>
    private ushort _nextArchetypeId = 0;
    
    /// <summary>
    /// Archetype ID 到 Archetype 的映射
    /// </summary>
    private Dictionary<ushort, Archetype> _archetypeMap = new();
    
    /// <summary>
    /// 组件类型组合到 Archetype ID 的映射
    /// 用于查找现有的 Archetype
    /// </summary>
    private Dictionary<int[], ushort> _componentSignatureMap = new(ArrayComparer.Instance);
    
    /// <summary>
    /// 所有系统
    /// </summary>
    private List<ISystem> _systems = new();
    
    /// <summary>
    /// 实体数量统计
    /// </summary>
    private int _entityCount = 0;
    
    // ============================================================
    // 属性
    // ============================================================
    
    /// <summary>
    /// 获取所有原型（只读）
    /// </summary>
    public ReadOnlySpan<Archetype> Archetypes => 
        new ReadOnlySpan<Archetype>(_archetypes, 0, _archetypeCount);
    
    /// <summary>
    /// 实体总数
    /// </summary>
    public int EntityCount => _entityCount;
    
    /// <summary>
    /// 获取所有系统
    /// </summary>
    public IReadOnlyList<ISystem> Systems => _systems;
    
    // ============================================================
    // 实体管理
    // ============================================================
    
    /// <summary>
    /// 创建实体
    /// 
    /// 返回值：
    /// - 返回新创建的 Entity
    /// - 实体没有任何组件
    /// 
    /// 使用示例：
    /// ```csharp
    /// var entity = world.CreateEntity();
    /// ```
    /// </summary>
    public Entity CreateEntity()
    {
        // 没有组件的实体，使用特殊的"空原型"
        return CreateEntityInArchetype(GetOrCreateEmptyArchetype());
    }
    
    /// <summary>
    /// 创建带组件的实体
    /// 
    /// 使用示例：
    /// ```csharp
    /// var entity = world.CreateEntity(
    ///     new PositionComponent { X = 0, Y = 0, Z = 0 },
    ///     new HealthComponent { CurrentHp = 100, MaxHp = 100 },
    ///     new NameComponent { Name = "玩家" }
    /// );
    /// ```
    /// </summary>
    public Entity CreateEntity<T1>(T1 component1) 
        where T1 : struct
    {
        var archetype = GetOrCreateArchetype(typeof(T1));
        var entity = CreateEntityInArchetype(archetype);
        SetComponent(entity, component1);
        return entity;
    }
    
    public Entity CreateEntity<T1, T2>(T1 component1, T2 component2)
        where T1 : struct where T2 : struct
    {
        var archetype = GetOrCreateArchetype(typeof(T1), typeof(T2));
        var entity = CreateEntityInArchetype(archetype);
        SetComponent(entity, component1);
        SetComponent(entity, component2);
        return entity;
    }
    
    public Entity CreateEntity<T1, T2, T3>(T1 component1, T2 component2, T3 component3)
        where T1 : struct where T2 : struct where T3 : struct
    {
        var archetype = GetOrCreateArchetype(typeof(T1), typeof(T2), typeof(T3));
        var entity = CreateEntityInArchetype(archetype);
        SetComponent(entity, component1);
        SetComponent(entity, component2);
        SetComponent(entity, component3);
        return entity;
    }
    
    /// <summary>
    /// 销毁实体
    /// 
    /// 执行步骤：
    /// 1. 找到实体所在的 Archetype
    /// 2. 从 Archetype 中移除
    /// 3. 更新实体计数
    /// </summary>
    public void DestroyEntity(Entity entity)
    {
        var archetypeId = entity.ArchetypeId;
        var index = (int)entity.Index;
        
        var archetype = _archetypeMap[archetypeId];
        archetype.Remove(index, out _);
        _entityCount--;
    }
    
    /// <summary>
    /// 添加组件
    /// 
    /// 注意：添加组件可能导致实体迁移到新的 Archetype
    /// 
    /// 迁移过程：
    /// 1. 在新的 Archetype 中创建
    /// 2. 复制原有组件数据
    /// 3. 从旧 Archetype 中删除
    /// </summary>
    public void AddComponent<T>(Entity entity, T component) where T : struct
    {
        var oldArchetypeId = entity.ArchetypeId;
        var oldArchetype = _archetypeMap[oldArchetypeId];
        var oldIndex = (int)entity.Index;
        
        // 获取新组件的 Archetype
        var newComponentTypes = AddComponentType(oldArchetype.ComponentTypes, typeof(T));
        var newArchetype = GetOrCreateArchetype(newComponentTypes);
        
        // 如果组件类型没变，直接设置
        if (oldArchetype.ComponentTypes.Length == newComponentTypes.Length)
        {
            SetComponentInternal(entity, component);
            return;
        }
        
        // 否则需要迁移
        MigrateEntity(entity, oldArchetype, oldIndex, newArchetype);
        SetComponentInternal(entity, component);
    }
    
    /// <summary>
    /// 移除组件
    /// </summary>
    public void RemoveComponent<T>(Entity entity) where T : struct
    {
        var oldArchetypeId = entity.ArchetypeId;
        var oldArchetype = _archetypeMap[oldArchetypeId];
        var oldIndex = (int)entity.Index;
        
        // 获取移除后的 Archetype
        var newComponentTypes = RemoveComponentType(oldArchetype.ComponentTypes, typeof(T));
        var newArchetype = GetOrCreateArchetype(newComponentTypes);
        
        // 迁移
        if (oldArchetype.ComponentTypes.Length != newComponentTypes.Length)
        {
            MigrateEntity(entity, oldArchetype, oldIndex, newArchetype);
        }
    }
    
    /// <summary>
    /// 获取组件
    /// </summary>
    public ref T GetComponent<T>(Entity entity) where T : struct
    {
        var archetypeId = entity.ArchetypeId;
        var index = (int)entity.Index;
        var archetype = _archetypeMap[archetypeId];
        var componentArray = archetype.GetArray<T>();
        return ref componentArray[index];
    }
    
    /// <summary>
    /// 尝试获取组件
    /// </summary>
    public bool TryGetComponent<T>(Entity entity, out T component) where T : struct
    {
        var archetypeId = entity.ArchetypeId;
        if (!_archetypeMap.TryGetValue(archetypeId, out var archetype))
        {
            component = default;
            return false;
        }
        
        var typeId = ComponentType<T>.Id;
        var index = Array.BinarySearch(archetype.ComponentTypes, typeId);
        if (index < 0)
        {
            component = default;
            return false;
        }
        
        var componentArray = archetype.GetArray<T>();
        component = componentArray[(int)entity.Index];
        return true;
    }
    
    // ============================================================
    // 查询
    // ============================================================
    
    /// <summary>
    /// 查询拥有特定组件的实体
    /// 
    /// 使用示例：
    /// ```csharp
    /// // 查询所有有 Position 的实体
    /// foreach (ref var pos in world.Query<PositionComponent>())
    /// {
    ///     pos.X += 1;
    /// }
    /// 
    /// // 查询有 Position + Health 的实体
    /// foreach (ref var (pos, health) in 
    ///     world.Query<PositionComponent, HealthComponent>())
    /// {
    ///     Console.WriteLine($"HP: {health.CurrentHp}");
    /// }
    /// ```
    /// </summary>
    public QueryEnumerator<T1> Query<T1>() where T1 : struct
    {
        var archetype = GetOrCreateArchetype(typeof(T1));
        var array = archetype.GetArray<T1>();
        return new QueryEnumerator<T1>(archetype, array);
    }
    
    public QueryEnumerator<T1, T2> Query<T1, T2>() 
        where T1 : struct where T2 : struct
    {
        var archetype = GetOrCreateArchetype(typeof(T1), typeof(T2));
        var array1 = archetype.GetArray<T1>();
        var array2 = archetype.GetArray<T2>();
        return new QueryEnumerator<T1, T2>(archetype, array1, array2);
    }
    
    // ============================================================
    // 系统管理
    // ============================================================
    
    /// <summary>
    /// 添加系统
    /// </summary>
    public void AddSystem<T>(T system) where T : ISystem
    {
        system.OnCreate(this);
        _systems.Add(system);
    }
    
    /// <summary>
    /// 更新所有系统
    /// 
    /// 在主循环的每一帧调用
    /// </summary>
    public void Update(float deltaTime)
    {
        foreach (var system in _systems)
        {
            system.Update(this, deltaTime);
        }
    }
    
    // ============================================================
    // 私有方法
    // ============================================================
    
    /// <summary>
    /// 在指定 Archetype 中创建实体
    /// </summary>
    private Entity CreateEntityInArchetype(Archetype archetype)
    {
        var index = archetype.Count;
        var entity = new Entity(archetype.Id, (uint)index, 1);
        archetype.Entities[index] = entity;
        archetype.Count++;
        _entityCount++;
        return entity;
    }
    
    /// <summary>
    /// 获取或创建空原型
    /// </summary>
    private Archetype GetOrCreateEmptyArchetype()
    {
        return GetOrCreateArchetype(Array.Empty<Type>());
    }
    
    /// <summary>
    /// 获取或创建原型
    /// </summary>
    private Archetype GetOrCreateArchetype(params Type[] types)
    {
        var componentIds = new int[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            componentIds[i] = ComponentType.GetId(types[i]);
        }
        Array.Sort(componentIds);
        return GetOrCreateArchetype(componentIds);
    }
    
    /// <summary>
    /// 根据组件 ID 数组获取或创建原型
    /// </summary>
    private Archetype GetOrCreateArchetype(int[] componentIds)
    {
        // 检查是否已存在
        if (_componentSignatureMap.TryGetValue(componentIds, out var archetypeId))
        {
            return _archetypeMap[archetypeId];
        }
        
        // 创建新原型
        var id = _nextArchetypeId++;
        var archetype = new Archetype(id, componentIds);
        
        // 添加到映射
        _archetypeMap[id] = archetype;
        
        // 扩展数组
        if (_archetypeCount >= _archetypes.Length)
        {
            Array.Resize(ref _archetypes, _archetypes.Length * 2);
        }
        _archetypes[_archetypeCount++] = archetype;
        
        // 记录组件组合
        _componentSignatureMap[componentIds] = id;
        
        return archetype;
    }
    
    /// <summary>
    /// 实体迁移到新 Archetype
    /// </summary>
    private void MigrateEntity(Entity entity, Archetype oldArch, int oldIndex, Archetype newArch)
    {
        var newIndex = newArch.Count;
        newArch.Entities[newIndex] = new Entity(newArch.Id, (uint)newIndex, entity.Version);
        newArch.Count++;
        
        // 复制组件数据
        for (int i = 0; i < oldArch.ComponentTypes.Length; i++)
        {
            var componentTypeId = oldArch.ComponentTypes[i];
            var newIndex2 = Array.BinarySearch(newArch.ComponentTypes, componentTypeId);
            if (newIndex2 >= 0)
            {
                CopyComponent(oldArch.ComponentArrays[i], oldIndex, 
                    newArch.ComponentArrays[newIndex2], newIndex);
            }
        }
        
        // 从旧 Archetype 中移除
        oldArch.Remove(oldIndex, out _);
    }
}
```

---

## 4. 系统设计

### 4.1 ISystem 接口

```csharp
/// <summary>
/// 系统接口
/// 
/// 所有系统都要实现这个接口
/// 系统负责处理特定组件组合的逻辑
/// </summary>
public interface ISystem
{
    /// <summary>
    /// 系统创建时调用
    /// 
    /// 使用场景：
    /// - 初始化系统所需的资源
    /// - 订阅事件
    /// - 注册查询
    /// </summary>
    void OnCreate(World world);
    
    /// <summary>
    /// 每帧更新
    /// 
    /// 重要：所有游戏逻辑都在这里执行
    /// </summary>
    void Update(World world, float deltaTime);
    
    /// <summary>
    /// 系统销毁时调用
    /// 
    /// 使用场景：
    /// - 释放资源
    /// - 取消事件订阅
    /// </summary>
    void OnDestroy(World world);
}

/// <summary>
/// 系统基类
/// 
/// 提供一些常用功能
/// </summary>
public abstract class SystemBase : ISystem
{
    protected World World { get; private set; }
    
    public virtual void OnCreate(World world)
    {
        World = world;
    }
    
    public abstract void Update(World world, float deltaTime);
    
    public virtual void OnDestroy(World world) { }
}
```

### 4.2 示例系统

```csharp
/// <summary>
/// 移动系统
/// 
/// 职责：
/// - 更新所有有 Position + Velocity 的实体
/// - 根据速度计算新位置
/// 
/// 关注的组件：
/// - PositionComponent
/// - VelocityComponent
/// </summary>
public class MovementSystem : SystemBase
{
    /// <summary>
    /// 每帧更新
    /// 
    /// 逻辑：
    /// 1. 遍历所有有 Position + Velocity 的实体
    /// 2. 根据速度更新位置
    /// 3. 如果超出地图边界，进行处理
    /// </summary>
    public override void Update(World world, float deltaTime)
    {
        // 查询有 Position + Velocity 的实体
        foreach (ref var data in world.Query<PositionComponent, VelocityComponent>())
        {
            ref var position = ref data.Get1();
            ref var velocity = ref data.Get2();
            
            // 更新位置
            position.X += velocity.X * velocity.Speed * deltaTime;
            position.Y += velocity.Y * velocity.Speed * deltaTime;
            position.Z += velocity.Z * velocity.Speed * deltaTime;
            
            // 边界检测（简化版）
            position.X = Math.Clamp(position.X, -1000, 1000);
            position.Y = Math.Clamp(position.Y, -1000, 1000);
        }
    }
}

/// <summary>
/// 战斗系统
/// 
/// 职责：
/// - 处理所有有 Health 的实体
/// - 检查死亡状态
/// - 触发死亡事件
/// </summary>
public class BattleSystem : SystemBase
{
    /// <summary>
    /// 死亡事件
    /// </summary>
    public event Action<Entity> OnEntityDead;
    
    public override void Update(World world, float deltaTime)
    {
        // 查询所有有 Health 的实体
        foreach (ref var data in world.Query<HealthComponent>())
        {
            ref var health = ref data.Get1();
            
            // 检查死亡
            if (health.IsDead)
            {
                // 触发死亡事件
                OnEntityDead?.Invoke(data.Entity);
            }
        }
    }
}

/// <summary>
/// AI 系统
/// 
/// 职责：
/// - 处理所有有 AIState 的实体
/// - 根据状态执行不同的 AI 行为
/// 
/// 注意：这是简化版的 AI 系统
/// 实际项目可能需要更复杂的行为树
/// </summary>
public class AISystem : SystemBase
{
    /// <summary>
    /// 感知范围（米）
    /// </summary>
    private const float SenseRange = 30f;
    
    /// <summary>
    /// 攻击范围（米）
    /// </summary>
    private const float AttackRange = 2f;
    
    public override void Update(World world, float deltaTime)
    {
        // 查询所有有 AI 的实体
        foreach (ref var data in world.Query<AIStateComponent, PositionComponent>())
        {
            ref var ai = ref data.Get1();
            ref var position = ref data.Get2();
            
            // 更新状态持续时间
            ai.StateDuration -= deltaTime;
            
            // 根据状态执行行为
            switch (ai.State)
            {
                case AIState.Idle:
                    UpdateIdle(world, data.Entity, ref ai, ref position, deltaTime);
                    break;
                    
                case AIState.Patrol:
                    UpdatePatrol(world, data.Entity, ref ai, ref position, deltaTime);
                    break;
                    
                case AIState.Chase:
                    UpdateChase(world, data.Entity, ref ai, ref position, deltaTime);
                    break;
                    
                case AIState.Attack:
                    UpdateAttack(world, data.Entity, ref ai, ref position, deltaTime);
                    break;
            }
        }
    }
    
    private void UpdateIdle(World world, Entity entity, 
        ref AIStateComponent ai, ref PositionComponent position, float deltaTime)
    {
        // 空闲超过 3 秒，转为巡逻
        if (ai.StateDuration <= 0)
        {
            ai.State = AIState.Patrol;
            ai.StateDuration = 5f;
        }
    }
    
    private void UpdatePatrol(World world, Entity entity,
        ref AIStateComponent ai, ref PositionComponent position, float deltaTime)
    {
        // 简化：只移动一点
        position.X += deltaTime * 2;
        
        // 巡逻结束，回到空闲
        if (ai.StateDuration <= 0)
        {
            ai.State = AIState.Idle;
            ai.StateDuration = 3f;
        }
        
        // 检测到玩家，转为追击
        if (HasPlayerNearby(world, position, SenseRange))
        {
            ai.State = AIState.Chase;
            ai.StateDuration = 10f;
        }
    }
    
    private void UpdateChase(World world, Entity entity,
        ref AIStateComponent ai, ref PositionComponent position, float deltaTime)
    {
        // 简化：朝目标方向移动
        if (ai.Target.IsValid())
        {
            ref var targetPos = ref world.GetComponent<PositionComponent>(ai.Target);
            var dir = new Vector3(
                targetPos.X - position.X,
                targetPos.Y - position.Y,
                targetPos.Z - position.Z
            ).Normalized();
            
            position.X += dir.X * deltaTime * 5;
            position.Y += dir.Y * deltaTime * 5;
            position.Z += dir.Z * deltaTime * 5;
            
            // 进入攻击范围，转为攻击
            if (position.DistanceTo(targetPos) <= AttackRange)
            {
                ai.State = AIState.Attack;
                ai.StateDuration = 1f;  // 每秒攻击一次
            }
        }
        else
        {
            // 目标丢失，回到空闲
            ai.State = AIState.Idle;
            ai.StateDuration = 3f;
        }
        
        // 追击超时，回到空闲
        if (ai.StateDuration <= 0)
        {
            ai.State = AIState.Idle;
            ai.StateDuration = 3f;
        }
    }
    
    private void UpdateAttack(World world, Entity entity,
        ref AIStateComponent ai, ref PositionComponent position, float deltaTime)
    {
        // 攻击逻辑
        if (ai.StateDuration <= 0 && ai.Target.IsValid())
        {
            // 造成伤害
            ref var targetHealth = ref world.GetComponent<HealthComponent>(ai.Target);
            ref var attack = ref world.GetComponent<AttackComponent>(entity);
            
            targetHealth.TakeDamage(attack.BaseAttack);
            
            // 重新计算攻击间隔
            ai.StateDuration = 1f;
        }
        
        // 攻击结束，判断下一步
        if (ai.StateDuration <= 0)
        {
            // 如果目标死亡或超出范围，回到空闲
            if (!ai.Target.IsValid())
            {
                ai.State = AIState.Idle;
                ai.StateDuration = 3f;
            }
            else
            {
                ref var targetPos = ref world.GetComponent<PositionComponent>(ai.Target);
                if (position.DistanceTo(targetPos) > AttackRange)
                {
                    ai.State = AIState.Chase;
                    ai.StateDuration = 10f;
                }
            }
        }
    }
    
    /// <summary>
    /// 检查附近是否有玩家
    /// </summary>
    private bool HasPlayerNearby(World world, PositionComponent position, float range)
    {
        foreach (ref var playerData in world.Query<PositionComponent, PlayerControlledComponent>())
        {
            ref var playerPos = ref playerData.Get1();
            if (position.DistanceTo(playerPos) <= range)
            {
                return true;
            }
        }
        return false;
    }
}
```

---

## 5. 查询优化

### 5.1 QueryEnumerator

```csharp
/// <summary>
/// 查询枚举器 - 用于遍历实体组合
/// 
/// 使用foreach循环，内部是值类型的ref遍历
/// 性能最优
/// </summary>
public ref struct QueryEnumerator<T1> 
    where T1 : struct
{
    private readonly Archetype _archetype;
    private readonly T1[] _array1;
    private int _index;
    
    public QueryEnumerator(Archetype archetype, T1[] array1)
    {
        _archetype = archetype;
        _array1 = array1;
        _index = -1;
    }
    
    /// <summary>
    /// 获取当前查询到的数据
    /// </summary>
    public ref T1 Current => ref _array1[_index];
    
    /// <summary>
    /// 获取当前实体
    /// </summary>
    public Entity Entity => _archetype.Entities[_index];
    
    public bool MoveNext()
    {
        _index++;
        return _index < _archetype.Count;
    }
    
    public QueryEnumerator<T1> GetEnumerator() => this;
}

/// <summary>
/// 双组件查询枚举器
/// </summary>
public ref struct QueryEnumerator<T1, T2> 
    where T1 : struct where T2 : struct
{
    private readonly Archetype _archetype;
    private readonly T1[] _array1;
    private readonly T2[] _array2;
    private int _index;
    
    public QueryEnumerator(Archetype archetype, T1[] array1, T2[] array2)
    {
        _archetype = archetype;
        _array1 = array1;
        _array2 = array2;
        _index = -1;
    }
    
    public (ref T1, ref T2) Current => (ref _array1[_index], ref _array2[_index]);
    public Entity Entity => _archetype.Entities[_index];
    
    public bool MoveNext()
    {
        _index++;
        return _index < _archetype.Count;
    }
    
    public QueryEnumerator<T1, T2> GetEnumerator() => this;
}

/// <summary>
/// 带数据的查询结果（用于事件）
/// </summary>
public readonly ref struct QueryData<T1>
    where T1 : struct
{
    private readonly Archetype _archetype;
    private readonly T1[] _array1;
    private readonly int _index;
    
    internal QueryData(Archetype archetype, T1[] array1, int index)
    {
        _archetype = archetype;
        _array1 = array1;
        _index = index;
    }
    
    public ref T1 Get1() => ref _array1[_index];
    public Entity Entity => _archetype.Entities[_index];
}
```

---

## 6. 使用示例

### 6.1 完整示例

```csharp
/// <summary>
/// 游戏世界初始化示例
/// </summary>
public class GameWorld
{
    private World _world;
    private MovementSystem _movementSystem;
    private BattleSystem _battleSystem;
    private AISystem _aiSystem;
    
    public void Initialize()
    {
        // 创建世界
        _world = new World();
        
        // 添加系统（按执行顺序）
        _movementSystem = new MovementSystem();
        _battleSystem = new BattleSystem();
        _aiSystem = new AISystem();
        
        _world.AddSystem(_movementSystem);
        _world.AddSystem(_aiSystem);
        _world.AddSystem(_battleSystem);
        
        // 订阅事件
        _battleSystem.OnEntityDead += OnEntityDead;
        
        // 创建一些测试实体
        CreateTestEntities();
    }
    
    /// <summary>
    /// 创建测试实体
    /// </summary>
    private void CreateTestEntities()
    {
        // 创建玩家
        var player = _world.CreateEntity(
            new PositionComponent { X = 0, Y = 0, Z = 0 },
            new VelocityComponent { X = 1, Y = 0, Z = 0, Speed = 5 },
            new HealthComponent { CurrentHp = 100, MaxHp = 100 },
            new AttackComponent { BaseAttack = 20, CritRate = 10, CritDamage = 150 },
            new PlayerControlledComponent { PlayerId = 1, SessionId = 100 },
            new NameComponent { Name = "测试玩家" }
        );
        
        // 创建怪物
        for (int i = 0; i < 10; i++)
        {
            _world.CreateEntity(
                new PositionComponent { X = i * 5, Y = 0, Z = 0 },
                new VelocityComponent { X = 1, Y = 0, Z = 0, Speed = 2 },
                new HealthComponent { CurrentHp = 50, MaxHp = 50 },
                new AttackComponent { BaseAttack = 10, CritRate = 5, CritDamage = 120 },
                new AIStateComponent { State = AIState.Idle, StateDuration = 3 }
            );
        }
        
        // 创建一棵树（只有位置和名称）
        _world.CreateEntity(
            new PositionComponent { X = 10, Y = 0, Z = 10 },
            new NameComponent { Name = "古树" }
        );
        
        Logger.Info($"创建了 {_world.EntityCount} 个实体");
    }
    
    /// <summary>
    /// 每帧更新
    /// </summary>
    public void Update(float deltaTime)
    {
        _world.Update(deltaTime);
    }
    
    /// <summary>
    /// 实体死亡处理
    /// </summary>
    private void OnEntityDead(Entity entity)
    {
        Logger.Info($"实体死亡: {entity}");
        
        // 检查是否有玩家控制组件
        if (_world.TryGetComponent<PlayerControlledComponent>(entity, out var player))
        {
            Logger.Warning($"玩家 {player.PlayerId} 死亡！");
            // 复活逻辑...
        }
        else
        {
            // 怪物死亡，移除
            _world.DestroyEntity(entity);
        }
    }
}
```

---

## 相关文档

| 文档 | 说明 |
|------|------|
| [ARCHITECTURE.md](./ARCHITECTURE.md) | 整体架构设计 |
| [THREADING_MODEL.md](./THREADING_MODEL.md) | 线程模型设计 |
| [DEVELOPER_GUIDE.md](./DEVELOPER_GUIDE.md) | 开发者指南 |

---

## 修改历史

| 版本 | 日期 | 修改内容 | 作者 |
|------|------|---------|------|
| v1.0.0 | 2024-01-15 | 初始版本 | 与伙伴共同编写 |

---

> **提示**：ECS 是游戏逻辑的核心。建议结合框架代码一起学习。
