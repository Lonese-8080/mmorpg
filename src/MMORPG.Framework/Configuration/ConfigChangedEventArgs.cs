// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

namespace MMORPG.Framework.Configuration;

/// <summary>
/// 配置变更事件参数
/// 
/// 包含变更前的旧配置（可能为 null）和变更后的新配置，
/// 业务层可以订阅此事件来应用配置变更
///（例如调整日志级别、修改监听端口等）。
/// </summary>
public class ConfigChangedEventArgs : EventArgs
{
    /// <summary>配置文件完整路径</summary>
    public string FilePath { get; }

    /// <summary>配置类型（例如 typeof(ServerConfig)）</summary>
    public Type ConfigType { get; }

    /// <summary>旧配置对象（首次加载时可能为 null）</summary>
    public object? OldConfig { get; }

    /// <summary>新配置对象</summary>
    public object? NewConfig { get; }

    /// <summary>变更时间（UTC）</summary>
    public DateTime ChangedAtUtc { get; }

    public ConfigChangedEventArgs(string filePath, Type configType, object? oldConfig, object? newConfig)
    {
        FilePath = filePath;
        ConfigType = configType;
        OldConfig = oldConfig;
        NewConfig = newConfig;
        ChangedAtUtc = DateTime.UtcNow;
    }
}
