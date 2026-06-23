// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

namespace MMORPG.Framework.Network;

/// <summary>
/// 消息 ID 定义
///
/// 消息 ID 分配规则：
/// - 框架层消息：0x0000 - 0x0FFF
/// - 游戏业务消息：0x1000 - 0x1FFF
/// - 扩展区间：0x2000 - 0xFFFF
///
/// 消息 ID 与 Protobuf 消息类型的映射：
/// - C2S_Login (0x00000001) -> MMORPG.Framework.Network.Protobuf.C2S_Login
/// - S2C_LoginResult (0x00000002) -> MMORPG.Framework.Network.Protobuf.S2C_LoginResult
/// - ...
/// </summary>
public static class MessageIds
{
    // ============================================================
    // 框架层消息（已实现）
    // ============================================================

    // 登录 (0x0001 - 0x001F)
    public const uint C2S_Login = 0x00000001;
    public const uint S2C_LoginResult = 0x00000002;

    // 心跳 (0x0020 - 0x002F)
    public const uint C2S_Heartbeat = 0x00000003;
    public const uint S2C_Heartbeat = 0x00000004;

    // 进入世界 (0x0030 - 0x003F)
    public const uint C2S_EnterWorld = 0x00000005;
    public const uint S2C_EnterWorld = 0x00000006;

    // 服务器通知 (0x0100 - 0x01FF)
    public const uint S2C_ServerNotice = 0x00000100;
    public const uint S2C_Error = 0x00000101;

    // 移动 (0x1000 - 0x10FF)
    public const uint C2S_PlayerMove = 0x00001001;
    public const uint S2C_PlayerPosition = 0x00001002;

    // ============================================================
    // 游戏业务消息（规划中）
    // ============================================================

    // 战斗 (0x1100 - 0x11FF)
    public const uint C2S_UseSkill = 0x00001100;
    public const uint S2C_SkillEffect = 0x00001101;
    public const uint C2S_NormalAttack = 0x00001102;
    public const uint S2C_DamageNotify = 0x00001103;
    public const uint S2C_EntityDead = 0x00001104;

    // 物品 (0x1200 - 0x12FF)
    public const uint C2S_PickupItem = 0x00001200;
    public const uint S2C_PickupResult = 0x00001201;
    public const uint S2C_ItemDrop = 0x00001202;

    // 聊天 (0x1300 - 0x13FF)
    public const uint C2S_Chat = 0x00001300;
    public const uint S2C_ChatNotify = 0x00001301;

    // 社交 (0x1400 - 0x14FF)
    public const uint C2S_AddFriend = 0x00001400;
    public const uint S2C_AddFriendResult = 0x00001401;
    public const uint S2C_FriendListUpdate = 0x00001402;

    // 公会 (0x1500 - 0x15FF)
    public const uint C2S_CreateGuild = 0x00001500;
    public const uint S2C_CreateGuildResult = 0x00001501;

    // ============================================================
    // 辅助方法
    // ============================================================

    /// <summary>
    /// 判断是否为框架层消息
    /// </summary>
    /// <param name="messageId">消息 ID</param>
    /// <returns>true 表示框架层消息</returns>
    public static bool IsFrameworkMessage(uint messageId)
    {
        return messageId < 0x00001000;
    }

    /// <summary>
    /// 判断是否为游戏业务消息
    /// </summary>
    /// <param name="messageId">消息 ID</param>
    /// <returns>true 表示游戏业务消息</returns>
    public static bool IsGameMessage(uint messageId)
    {
        return messageId >= 0x00001000 && messageId < 0x00010000;
    }

    /// <summary>
    /// 获取消息 ID 的描述名称
    /// </summary>
    /// <param name="messageId">消息 ID</param>
    /// <returns>消息名称，如 "C2S_Login"</returns>
    public static string GetDescription(uint messageId)
    {
        return messageId switch
        {
            C2S_Login => "C2S_Login",
            S2C_LoginResult => "S2C_LoginResult",
            C2S_Heartbeat => "C2S_Heartbeat",
            S2C_Heartbeat => "S2C_Heartbeat",
            C2S_EnterWorld => "C2S_EnterWorld",
            S2C_EnterWorld => "S2C_EnterWorld",
            S2C_ServerNotice => "S2C_ServerNotice",
            S2C_Error => "S2C_Error",
            C2S_PlayerMove => "C2S_PlayerMove",
            S2C_PlayerPosition => "S2C_PlayerPosition",
            C2S_UseSkill => "C2S_UseSkill",
            S2C_SkillEffect => "S2C_SkillEffect",
            C2S_NormalAttack => "C2S_NormalAttack",
            S2C_DamageNotify => "S2C_DamageNotify",
            S2C_EntityDead => "S2C_EntityDead",
            C2S_PickupItem => "C2S_PickupItem",
            S2C_PickupResult => "S2C_PickupResult",
            S2C_ItemDrop => "S2C_ItemDrop",
            C2S_Chat => "C2S_Chat",
            S2C_ChatNotify => "S2C_ChatNotify",
            C2S_AddFriend => "C2S_AddFriend",
            S2C_AddFriendResult => "S2C_AddFriendResult",
            S2C_FriendListUpdate => "S2C_FriendListUpdate",
            C2S_CreateGuild => "C2S_CreateGuild",
            S2C_CreateGuildResult => "S2C_CreateGuildResult",
            _ => $"Unknown(0x{messageId:X8})"
        };
    }
}