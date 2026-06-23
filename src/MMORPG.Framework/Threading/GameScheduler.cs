// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

// ====================================================================
// 游戏主循环调度器 - GameScheduler
//
// 这是整个游戏服务器的"心脏"。
// 它以固定的帧率运行，每帧执行所有游戏逻辑。
//
// 主循环示意图：
// ┌────────────────────────────────────────────────────────────┐
// │  主循环（单线程）                                            │
// │                                                               │
// │  ┌───────┐   ┌───────┐   ┌───────┐   ┌───────┐   ┌───────┐  │
// │  │ 帧开始 │ → │ 处理  │ → │ 执行  │ → │ 帧结束 │ → │ 等待  │  │
// │  │       │   │ 消息  │   │ 系统  │   │       │   │ 下一帧│  │
// │  └───────┘   └───────┘   └───────┘   └───────┘   └───────┘  │
// │       ↑                                                       │
// │       │ 每帧循环                                              │
// │       └───────────────────────────────────────────────────────┘
// │                                                               │
// │  帧率（可配置）：                                             │
// │  - 20Hz   → 每帧 50ms （省电，适合日常运营）                  │
// │  - 60Hz   → 每帧 16.67ms（标准，适合大多数游戏）              │
// │  - 144Hz  → 每帧 6.94ms（竞技模式，PVP）                      │
// │  - 240Hz  → 每帧 4.17ms（极致，ECS 性能要求高）               │
// └──────────────────────────────────────────────────────────────┘
//
// 为什么需要固定帧率？
// ┌─────────────────────────────────────────────────────────────┐
// │                                                               │
// │ 不固定帧率的问题：                                             │
// │  - CPU 快时：每秒更新 1000 次，玩家可能跳帧                  │
// │  - CPU 慢时：每秒更新 10 次，游戏卡顿                         │
// │  - 物理计算不准确（速度 = 距离 / 时间，不稳定）               │
// │  - 难以调试和重现问题                                         │
// │                                                               │
// │ 固定帧率的好处：                                               │
// │  - 所有玩家看到的游戏状态一致（同一时间同一步进）             │
// │  - 物理计算准确且可预测                                       │
// │  - 便于调试和问题定位                                         │
// │  - 可以控制 CPU 使用率                                       │
// │                                                               │
// └─────────────────────────────────────────────────────────────┘
// ====================================================================

using System.Diagnostics;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Threading;

/// <summary>
/// 游戏调度器配置
/// </summary>
public class GameSchedulerOptions
{
    /// <summary>
    /// 目标帧率（每秒帧数）
    /// 默认：20 Hz（省电且稳定）
    /// </summary>
    public int TargetFps { get; set; } = 20;

    /// <summary>
    /// 是否启用 FPS 统计
    /// 默认：启用
    /// </summary>
    public bool EnableFpsStats { get; set; } = true;

    /// <summary>
    /// 是否启用帧时间统计
    /// 默认：启用
    /// </summary>
    public bool EnableFrameTimeStats { get; set; } = true;

    /// <summary>
    /// ⚠️ 可配置化（#22 修复）：帧时间统计窗口大小
    /// 用于计算平均/最大/最小帧时间。
    /// 范围：1 - 10000，默认 100（最近 100 帧）。
    /// 调大：统计更平滑但占用更多内存（每 60 FPS 1000 帧 ≈ 16 秒历史）
    /// 调小：统计更灵敏但抖动更大
    /// </summary>
    public int FrameTimeWindowSize { get; set; } = 100;
}

/// <summary>
/// 帧事件参数
/// </summary>
public readonly struct FrameEventArgs
{
    /// <summary>
    /// 帧号（从 1 开始递增）
    /// </summary>
    public long FrameNumber { get; }

    /// <summary>
    /// 本帧 delta time（秒）
    /// 距离上一帧的时间差
    /// </summary>
    public float DeltaTime { get; }

    /// <summary>
    /// 本帧实际执行时间（毫秒）
    /// 用于性能监控
    /// </summary>
    public double FrameTimeMs { get; }

    public FrameEventArgs(long frameNumber, float deltaTime, double frameTimeMs)
    {
        FrameNumber = frameNumber;
        DeltaTime = deltaTime;
        FrameTimeMs = frameTimeMs;
    }
}

/// <summary>
/// 游戏主循环调度器
///
/// 职责：
/// 1️⃣ 管理固定帧率的游戏循环
/// 2️⃣ 协调所有系统按顺序执行
/// 3️⃣ 提供帧开始/结束事件，业务层可以挂接逻辑
/// 4️⃣ 统计和监控性能（FPS、帧时间等）
///
/// 使用示例：
/// <code>
/// var scheduler = new GameScheduler(60);  // 60 FPS
///
/// // 注册每帧逻辑（业务层自己的系统）
/// scheduler.OnFrameStart += args =>
/// {
///     // 处理网络消息
///     ProcessNetworkMessages();
/// };
///
/// scheduler.OnFrameEnd += args =>
/// {
///     // 处理发送队列
///     ProcessSendQueue();
/// };
///
/// // 启动主循环（阻塞当前线程）
/// scheduler.Start();
/// </code>
/// </summary>
public class GameScheduler
{
    #region 字段

    /// <summary>
    /// 每帧目标时间（毫秒）
    /// 例如 60 FPS → 1000 / 60 = 16.67 ms
    /// </summary>
    private readonly double _frameIntervalMs;

    /// <summary>
    /// 配置
    /// </summary>
    private readonly GameSchedulerOptions _options;

    /// <summary>
    /// 是否正在运行
    /// volatile 保证多线程读取正确
    /// </summary>
    private volatile bool _isRunning;

    /// <summary>
    /// 启动锁（用于保护 Start/Stop 的状态变更）
    /// </summary>
    private readonly object _startLock = new();

    /// <summary>
    /// 帧计数器
    /// </summary>
    private long _frameCount;

    /// <summary>
    /// 上一帧结束时间（用于计算 delta time）
    /// </summary>
    private long _lastFrameTicks;

    /// <summary>
    /// 服务器启动时间（UTC）
    /// </summary>
    private DateTime _startTime;

    /// <summary>
    /// 高精度计时器（Stopwatch）
    /// 用于精确的帧时间计算
    /// </summary>
    private readonly Stopwatch _stopwatch;

    #region FPS 统计

    /// <summary>
    /// FPS 计数器（每秒重置一次）
    /// </summary>
    private int _fpsCounter;

    /// <summary>
    /// 上次 FPS 统计时间
    /// </summary>
    private long _fpsLastUpdateTicks;

    /// <summary>
    /// 当前 FPS（每秒更新一次）
    /// </summary>
    private double _currentFps;

    /// <summary>
    /// 帧时间统计窗口（最近 N 帧，N 可通过 <see cref="GameSchedulerOptions.FrameTimeWindowSize"/> 配置）
    /// 用于计算平均/最大/最小帧时间
    /// </summary>
    private readonly double[] _frameTimeWindow;
    private int _frameTimeWindowIndex;
    private int _frameTimeWindowCount;

    #endregion

    #endregion

    #region 事件

    /// <summary>
    /// 帧开始事件
    /// 在处理消息和执行系统之前触发
    /// 适合：读取输入、同步状态
    /// </summary>
    public event Action<FrameEventArgs>? OnFrameStart;

    /// <summary>
    /// 主逻辑事件
    /// 帧开始之后，帧结束之前
    /// 适合：执行 ECS 系统、AI、物理
    /// </summary>
    public event Action<FrameEventArgs>? OnUpdate;

    /// <summary>
    /// 帧结束事件
    /// 所有逻辑执行完毕后触发
    /// 适合：发送消息、持久化数据、统计
    /// </summary>
    public event Action<FrameEventArgs>? OnFrameEnd;

    #endregion

    #region 属性

    /// <summary>
    /// 目标帧率
    /// </summary>
    public int TargetFps => _options.TargetFps;

    /// <summary>
    /// 当前实际 FPS（每秒更新）
    /// </summary>
    public double CurrentFps => _currentFps;

    /// <summary>
    /// 当前帧号
    /// </summary>
    public long FrameNumber => _frameCount;

    /// <summary>
    /// 是否正在运行
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// 服务器启动时间
    /// </summary>
    public DateTime StartTime => _startTime;

    /// <summary>
    /// 运行时间（秒）
    /// </summary>
    public double UptimeSeconds => _stopwatch.Elapsed.TotalSeconds;

    #endregion

    #region 构造函数

    /// <summary>
    /// 创建游戏调度器（使用默认配置）
    /// </summary>
    /// <param name="targetFps">目标帧率，默认 20</param>
    public GameScheduler(int targetFps = 20)
        : this(new GameSchedulerOptions { TargetFps = targetFps })
    {
    }

    /// <summary>
    /// 创建游戏调度器（使用自定义配置）
    /// </summary>
    /// <param name="options">配置选项</param>
    public GameScheduler(GameSchedulerOptions options)
    {
        // 校验帧率范围
        if (options.TargetFps < 1)
            options.TargetFps = 1;
        if (options.TargetFps > 1000)
            options.TargetFps = 1000;

        // 校验窗口大小（#22 修复）
        if (options.FrameTimeWindowSize < 1)
            options.FrameTimeWindowSize = 1;
        if (options.FrameTimeWindowSize > 10000)
            options.FrameTimeWindowSize = 10000;

        _options = options;
        _frameIntervalMs = 1000.0 / options.TargetFps;
        _stopwatch = new Stopwatch();
        _frameTimeWindow = new double[options.FrameTimeWindowSize];

        Logger.Info("Network",
            "游戏调度器初始化: 目标帧率={0} FPS, 每帧预算={1:F2} ms, 帧时间窗口={2}",
            options.TargetFps,
            _frameIntervalMs,
            options.FrameTimeWindowSize);
    }

    #endregion

    #region 公共方法 - 启动/停止

    /// <summary>
    /// 启动游戏主循环
    ///
    /// 注意：此方法会阻塞当前线程！
    /// 应该在专用线程中运行，或者直接在 Main 方法中调用。
    ///
    /// 使用示例：
    /// <code>
    /// // 在主线程中运行（最简单方式）
    /// scheduler.Start();
    ///
    /// // 或者在新线程中运行
    /// Task.Run(() => scheduler.Start());
    /// </code>
    /// </summary>
    public void Start()
    {
        lock (_startLock)
        {
            if (_isRunning)
            {
                Logger.Warning("Network", "游戏调度器已在运行，忽略重复启动");
                return;
            }

            _isRunning = true;
            _startTime = DateTime.UtcNow;
            _stopwatch.Start();

            // 初始化第一帧
            _lastFrameTicks = _stopwatch.ElapsedTicks;
            _fpsLastUpdateTicks = _lastFrameTicks;
            _frameCount = 0;
        }

        Logger.Info("Network", "游戏主循环启动，目标帧率: {0} FPS", _options.TargetFps);

        try
        {
            // 主循环
            while (_isRunning)
            {
                Tick();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Network", ex, "游戏主循环异常，服务器将停止");
            _isRunning = false;
        }

        _stopwatch.Stop();
        Logger.Info("Network", "游戏主循环已停止，共执行 {0} 帧，运行时间 {1:F2} 秒",
            _frameCount, UptimeSeconds);
    }

    /// <summary>
    /// 请求停止游戏主循环
    ///
    /// 注意：这是一个异步请求，当前帧执行完毕后才会停止
    /// 通常用于优雅关闭服务器
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        Logger.Info("Network", "游戏调度器收到停止请求，将在下一帧后结束");
    }

    #endregion

    #region 私有方法 - 主循环

    /// <summary>
    /// 单帧逻辑
    ///
    /// 执行流程：
    /// 1. 计算 delta time（距离上一帧的时间）
    /// 2. 触发 OnFrameStart 事件
    /// 3. 触发 OnUpdate 事件
    /// 4. 触发 OnFrameEnd 事件
    /// 5. 帧率控制（Sleep 等待下一帧）
    /// 6. 更新 FPS 统计
    ///
    /// ⚠️ 异常处理策略：
    /// - 事件处理异常（非致命）会被捕获并记录日志，主循环继续运行
    /// - 致命异常（OOM/StackOverflow）会向上传播，导致主循环停止
    /// - 设计意图：业务层事件异常不应影响框架稳定性
    /// </summary>
    private void Tick()
    {
        // ----------------------------------------
        // 1. 计算时间
        // ----------------------------------------
        var nowTicks = _stopwatch.ElapsedTicks;
        var elapsedTicks = nowTicks - _lastFrameTicks;
        _lastFrameTicks = nowTicks;

        // 转换为秒和毫秒
        var deltaTime = (float)elapsedTicks / Stopwatch.Frequency;
        var elapsedMs = elapsedTicks * 1000.0 / Stopwatch.Frequency;

        // 第一帧的 delta time 可能异常大，使用默认值
        if (_frameCount == 0 || deltaTime > 1.0f)
            deltaTime = (float)_frameIntervalMs / 1000.0f;

        _frameCount++;

        var frameArgs = new FrameEventArgs(_frameCount, deltaTime, elapsedMs);

        // ----------------------------------------
        // 2. 帧开始事件
        // ----------------------------------------
        try
        {
            OnFrameStart?.Invoke(frameArgs);
        }
        catch (Exception ex) when (!IsFatalException(ex))
        {
            // 捕获非致命异常，记录日志，主循环继续
            Logger.Error("Network", ex, "OnFrameStart 事件处理异常，第 {0} 帧", _frameCount);
        }

        // ----------------------------------------
        // 3. 主逻辑事件
        // ----------------------------------------
        try
        {
            OnUpdate?.Invoke(frameArgs);
        }
        catch (Exception ex) when (!IsFatalException(ex))
        {
            Logger.Error("Network", ex, "OnUpdate 事件处理异常，第 {0} 帧", _frameCount);
        }

        // ----------------------------------------
        // 4. 帧结束事件
        // ----------------------------------------
        try
        {
            OnFrameEnd?.Invoke(frameArgs);
        }
        catch (Exception ex) when (!IsFatalException(ex))
        {
            Logger.Error("Network", ex, "OnFrameEnd 事件处理异常，第 {0} 帧", _frameCount);
        }

        // ----------------------------------------
        // 5. 帧率控制
        // ----------------------------------------
        FrameRateControl(nowTicks);

        // ----------------------------------------
        // 6. FPS 统计
        // ----------------------------------------
        UpdateFpsStats(nowTicks, elapsedMs);
    }

    /// <summary>
    /// 帧率控制
    ///
    /// 混合睡眠策略（平衡精度与 CPU 占用）：
    /// - 剩余时间 大于 30ms：使用 Thread.Sleep（让出 CPU，精度较差）
    /// - 剩余时间 2-30ms：交替 Sleep(0) 与 SpinWait（精确又不占用过多 CPU）
    /// - 剩余时间 小于等于 2ms：纯 SpinWait（高精度自旋）
    /// </summary>
    private void FrameRateControl(long currentTicks)
    {
        // 计算本帧已用时
        var frameElapsedMs = (currentTicks - _lastFrameTicks) * 1000.0 / Stopwatch.Frequency;
        var sleepMs = _frameIntervalMs - frameElapsedMs;

        if (sleepMs > 0)
        {
            // 目标时间点（绝对 ticks）
            var targetTicks = _lastFrameTicks +
                              (long)(_frameIntervalMs * Stopwatch.Frequency / 1000.0);

            // 混合睡眠循环
            while (true)
            {
                var remainingTicks = targetTicks - _stopwatch.ElapsedTicks;
                if (remainingTicks <= 0)
                    break;

                var remainingMs = remainingTicks * 1000.0 / Stopwatch.Frequency;

                if (remainingMs > 30)
                {
                    // 充裕时间：深度睡眠，让出 CPU
                    // 多睡一些，留最后约 10ms 给精确控制
                    Thread.Sleep((int)(remainingMs - 10));
                }
                else if (remainingMs > 5)
                {
                    // 中等时间：Sleep(0) 让出时间片，避免全核占用
                    // Sleep(0) 只让出给同优先级线程，通常很快返回
                    Thread.Sleep(0);
                }
                else if (remainingMs > 1)
                {
                    // 1-5ms：混合策略，每次自旋一小段后让出
                    Thread.SpinWait(100);
                    Thread.Sleep(0);
                }
                else
                {
                    // 最后 1ms 内：纯自旋保证精度
                    Thread.SpinWait(10);
                }
            }
        }
        else if (sleepMs < -_frameIntervalMs * 2)
        {
            // 帧超时超过 2 倍，记录警告
            if (_options.EnableFrameTimeStats && _frameCount % 60 == 0)
            {
                Logger.Warning("Network",
                    "帧超时警告: 第 {0} 帧，执行时间 {1:F2} ms (目标 {2:F2} ms)，超出 {3:F2} ms",
                    _frameCount,
                    frameElapsedMs,
                    _frameIntervalMs,
                    -sleepMs);
            }
        }
    }

    /// <summary>
    /// 更新 FPS 统计（每秒更新一次）
    /// </summary>
    private void UpdateFpsStats(long currentTicks, double frameElapsedMs)
    {
        if (!_options.EnableFpsStats)
            return;

        _fpsCounter++;

        // 记录帧时间到统计窗口
        _frameTimeWindow[_frameTimeWindowIndex] = frameElapsedMs;
        _frameTimeWindowIndex = (_frameTimeWindowIndex + 1) % _frameTimeWindow.Length;
        if (_frameTimeWindowCount < _frameTimeWindow.Length)
            _frameTimeWindowCount++;

        // 每秒更新一次 FPS
        var elapsedSinceUpdate = (currentTicks - _fpsLastUpdateTicks) * 1000.0 / Stopwatch.Frequency;

        if (elapsedSinceUpdate >= 1000.0)
        {
            _currentFps = _fpsCounter * 1000.0 / elapsedSinceUpdate;
            _fpsCounter = 0;
            _fpsLastUpdateTicks = currentTicks;

            // 每 10 秒输出一次统计信息（约每 10 秒一次）
            if (_frameCount % ((long)_options.TargetFps * 10) == 0)
            {
                Logger.Info("Network",
                    "性能统计: FPS={0:F1} (目标 {1})，帧时间={2:F2} ms，已运行 {3:F0} 秒",
                    _currentFps,
                    _options.TargetFps,
                    GetAverageFrameTimeMs(),
                    UptimeSeconds);
            }
        }
    }

    /// <summary>
    /// 获取平均帧时间（从最近 100 帧计算）
    /// </summary>
    private double GetAverageFrameTimeMs()
    {
        if (_frameTimeWindowCount == 0)
            return 0;

        double sum = 0;
        for (var i = 0; i < _frameTimeWindowCount; i++)
            sum += _frameTimeWindow[i];

        return sum / _frameTimeWindowCount;
    }

    /// <summary>
    /// 判断异常是否为致命异常（不应被捕获）
    ///
    /// 致命异常包括：
    /// - OutOfMemoryException：内存耗尽，无法继续运行
    /// - StackOverflowException：栈溢出，无法恢复
    /// - ThreadAbortException：线程被终止
    /// - AccessViolationException：内存访问违规
    /// </summary>
    private static bool IsFatalException(Exception ex)
    {
        return ex is OutOfMemoryException ||
               ex is StackOverflowException ||
               ex is ThreadAbortException ||
               ex is AccessViolationException;
    }

    #endregion
}

/// <summary>
/// 调度器扩展方法
/// 提供更方便的 API 给业务层使用
/// </summary>
public static class GameSchedulerExtensions
{
    /// <summary>
    /// 在新线程中启动调度器
    /// 适用于：主程序需要继续执行其他任务
    /// </summary>
    /// <param name="scheduler">调度器实例</param>
    /// <returns>Task（等待调度器运行完成）</returns>
    public static Task StartAsync(this GameScheduler scheduler)
    {
        return Task.Run(() => scheduler.Start());
    }

    /// <summary>
    /// 注册一个每帧执行的定时任务
    /// </summary>
    /// <param name="scheduler">调度器</param>
    /// <param name="action">要执行的动作</param>
    public static void RegisterUpdate(this GameScheduler scheduler, Action<FrameEventArgs> action)
    {
        scheduler.OnUpdate += action;
    }
}
