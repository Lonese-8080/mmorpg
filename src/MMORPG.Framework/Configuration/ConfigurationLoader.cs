// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

// ====================================================================
// 配置加载器 - ConfigurationLoader
//
// 负责从多种来源加载配置：
// 1. JSON 文件（主要配置）
// 2. 环境变量（覆盖配置，适合容器化部署）
// 3. 命令行参数（快速调试）
//
// 为什么不用 .NET 的 IConfiguration / Microsoft.Extensions.Configuration？
// - 游戏服务器需要保持轻量，不依赖 ASP.NET Core 的配置体系
// - 我们的配置模型很简单，手动解析更可控
// - 避免引入额外的 NuGet 包依赖
//
// 加载优先级（后面的会覆盖前面的）：
//   JSON 文件 → 环境变量 → 命令行参数
// ====================================================================

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Configuration;

/// <summary>
/// 配置加载器
///
/// 负责从 JSON 文件 / 环境变量 / 命令行参数加载配置。
/// 使用 System.Text.Json 进行 JSON 反序列化。
///
/// 使用示例：
/// <code>
/// // 方式1：从 JSON 文件加载
/// var config = ConfigurationLoader.Load&lt;ServerConfig&gt;("config/server.json");
///
/// // 方式2：从 JSON + 环境变量（环境变量覆盖 JSON 中的值）
/// var config = ConfigurationLoader.Load&lt;ServerConfig&gt;(
///     "config/server.json",
///     enableEnvironmentVariables: true);
///
/// // 方式3：获取 JSON 配置中的某个子节点
/// var dbConfig = ConfigurationLoader.LoadSection&lt;DatabaseSettings&gt;(
///     "config/server.json", "Database");
/// </code>
/// </summary>
public static class ConfigurationLoader
{
    #region 缓存

    /// <summary>
    /// 已加载的配置缓存（文件路径 → 配置对象）
    /// 避免重复反序列化，提高性能
    /// </summary>
    private static readonly ConcurrentDictionary<string, object> _cache = new();

    /// <summary>
    /// 已注册的文件监听器（文件路径 → 监听器信息）
    /// </summary>
    private static readonly ConcurrentDictionary<string, FileWatcherEntry> _fileWatchers = new();

    /// <summary>
    /// 配置变更事件：当被监听的配置文件发生变化并重新加载后触发
    /// </summary>
    public static event EventHandler<ConfigChangedEventArgs>? ConfigChanged;

    /// <summary>
    /// 文件监听器入口：记录最后一次修改时间和轮询间隔
    /// </summary>
    private class FileWatcherEntry
    {
        public string FilePath { get; set; } = string.Empty;
        public int IntervalMs { get; set; }
        public long LastWriteTimeUtcTicks { get; set; }
        public Type ConfigType { get; set; } = typeof(object);
        public bool Enabled { get; set; }
        public Timer? Timer { get; set; }
        public object? CurrentConfig { get; set; }
    }

    #endregion

    #region JSON 反序列化选项

    /// <summary>
    /// JSON 反序列化默认选项
    ///
    /// 设计要点：
    /// - 不区分大小写：支持 "port"、"Port"、"PORT" 都能映射到 Port 属性
    /// - 允许注释：允许 JSON 文件中有 // 注释，便于写文档
    /// - 允许尾随逗号：JSON 数组/对象末尾多一个逗号也能解析
    /// </summary>
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // 支持 enum 字符串：{"Type":"MySql"} 也能解析
        Converters = { new JsonStringEnumConverter() }
    };

    #endregion

    #region 公共方法 - 加载

    /// <summary>
    /// 从 JSON 文件加载配置（最常用方式）
    /// </summary>
    /// <typeparam name="T">配置类型（如 ServerConfig）</typeparam>
    /// <param name="jsonFilePath">JSON 文件路径</param>
    /// <param name="enableEnvironmentVariables">是否允许环境变量覆盖（默认 false）</param>
    /// <returns>加载好的配置对象</returns>
    /// <exception cref="FileNotFoundException">找不到 JSON 文件</exception>
    /// <exception cref="InvalidOperationException">JSON 解析失败</exception>
    public static T Load<T>(string jsonFilePath, bool enableEnvironmentVariables = false)
        where T : class, new()
    {
        // 1. 从 JSON 文件加载基础配置
        var config = LoadFromJson<T>(jsonFilePath);

        // 2. 如果启用了环境变量覆盖，按属性名匹配
        if (enableEnvironmentVariables)
        {
            ApplyEnvironmentVariables(config);
        }

        // 3. 解密敏感字段（以 "ENC[v1]:" 开头）
        DecryptSensitiveFields(config);

        return config;
    }

    /// <summary>
    /// 从 JSON 文件加载配置的某个子节点
    ///
    /// 当配置文件结构复杂，只需要某一部分时使用。
    /// 例如 ServerConfig 中有 Database 子节点，可直接用此方法加载 DatabaseSettings
    /// </summary>
    /// <typeparam name="T">子配置类型（如 DatabaseSettings）</typeparam>
    /// <param name="jsonFilePath">JSON 文件路径</param>
    /// <param name="sectionName">子节点名称（不区分大小写）</param>
    /// <returns>加载好的子配置对象</returns>
    public static T? LoadSection<T>(string jsonFilePath, string sectionName)
        where T : class, new()
    {
        if (!File.Exists(jsonFilePath))
            return null;

        // ⚠️ 使用 ReadFileWithRetry 而非 File.ReadAllText
        // 防止文件正在被写入时读到半份内容
        var json = ReadFileWithRetry(jsonFilePath);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty(sectionName, out var section))
        {
            return section.Deserialize<T>(_jsonOptions);
        }

        return null;
    }

    /// <summary>
    /// 强制重新加载（忽略缓存）
    /// 用于配置热更新场景
    /// </summary>
    public static T Reload<T>(string jsonFilePath, bool enableEnvironmentVariables = false)
        where T : class, new()
    {
        _cache.TryRemove(jsonFilePath, out _);
        return Load<T>(jsonFilePath, enableEnvironmentVariables);
    }

    /// <summary>
    /// 清空缓存
    /// </summary>
    public static void ClearCache()
    {
        _cache.Clear();
    }

    /// <summary>
    /// 启用指定配置文件的热更新监听
    /// 
    /// 监听文件的 LastWriteTimeUtc，当检测到变化时自动重新加载
    /// 并触发 ConfigChanged 事件。默认间隔 1000ms 检查一次。
    /// </summary>
    /// <typeparam name="T">配置类型</typeparam>
    /// <param name="jsonFilePath">JSON 文件路径</param>
    /// <param name="intervalMs">检查间隔（毫秒），默认 1000</param>
    /// <param name="enableEnvironmentVariables">加载时是否应用环境变量</param>
    public static void EnableFileWatcher<T>(string jsonFilePath, int intervalMs = 1000, bool enableEnvironmentVariables = false)
        where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(jsonFilePath))
        {
            Logger.Warning("Config", "EnableFileWatcher 文件路径为空");
            return;
        }

        if (!File.Exists(jsonFilePath))
        {
            Logger.Warning("Config", "EnableFileWatcher 文件不存在：{0}", jsonFilePath);
            return;
        }

        // 获取当前配置（如未加载则加载）
        var currentConfig = Load<T>(jsonFilePath, enableEnvironmentVariables);

        // 使用 FileInfo 强制读取初始时间戳，避免文件系统缓存
        var initialFileInfo = new FileInfo(jsonFilePath);
        initialFileInfo.Refresh();

        var entry = new FileWatcherEntry
        {
            FilePath = Path.GetFullPath(jsonFilePath),
            IntervalMs = Math.Max(100, intervalMs),
            LastWriteTimeUtcTicks = initialFileInfo.LastWriteTimeUtc.Ticks,
            ConfigType = typeof(T),
            Enabled = true,
            CurrentConfig = currentConfig
        };

        // 设置定时器，定期检查
        entry.Timer = new Timer(_ =>
        {
            if (!entry.Enabled) return;

            try
            {
                // 使用 FileInfo 强制刷新元数据，避免文件系统缓存
                var fileInfo = new FileInfo(entry.FilePath);
                if (!fileInfo.Exists) return;

                fileInfo.Refresh();
                var current = fileInfo.LastWriteTimeUtc.Ticks;

                if (current > entry.LastWriteTimeUtcTicks)
                {
                    // 文件发生变化，重新加载
                    var oldSnapshot = entry.CurrentConfig;
                    var newConfig = Reload<T>(entry.FilePath, enableEnvironmentVariables);
                    entry.CurrentConfig = newConfig;
                    entry.LastWriteTimeUtcTicks = current;

                    // 触发变更事件
                    try
                    {
                        ConfigChanged?.Invoke(null, new ConfigChangedEventArgs(
                            entry.FilePath,
                            entry.ConfigType,
                            oldSnapshot,
                            newConfig));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Config", ex, "ConfigChanged 事件处理器异常，文件：{0}", entry.FilePath);
                    }

                    Logger.Info("Config", "配置已热更新：{0}", entry.FilePath);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Config", ex, "配置热更新检查失败，文件：{0}", entry.FilePath);
            }
        }, null, entry.IntervalMs, entry.IntervalMs);

        _fileWatchers.TryAdd(entry.FilePath, entry);
        Logger.Info("Config", "配置热更新已启用：{0}（间隔 {1}ms）", jsonFilePath, entry.IntervalMs);
    }

    /// <summary>
    /// 禁用指定配置文件的热更新监听
    /// </summary>
    /// <param name="jsonFilePath">JSON 文件路径</param>
    public static void DisableFileWatcher(string jsonFilePath)
    {
        if (string.IsNullOrWhiteSpace(jsonFilePath))
            return;

        var fullPath = Path.GetFullPath(jsonFilePath);
        if (_fileWatchers.TryRemove(fullPath, out var entry))
        {
            entry.Enabled = false;
            entry.Timer?.Dispose();
            Logger.Info("Config", "配置热更新已禁用：{0}", fullPath);
        }
    }

    /// <summary>
    /// 禁用所有配置文件的热更新监听
    /// </summary>
    public static void DisableAllFileWatchers()
    {
        var paths = _fileWatchers.Keys.ToList();
        foreach (var p in paths)
            DisableFileWatcher(p);
    }

    #endregion

    #region 私有方法 - JSON 加载

    /// <summary>
    /// 从 JSON 文件反序列化到对象
    /// 带缓存：同一路径多次调用只会反序列化一次
    ///
    /// ⚠️ 并发安全：
    /// - 如果文件正在被其他程序写入，File.ReadAllText 可能读到半份内容
    /// - 使用重试机制：最多重试 3 次，每次间隔 50ms
    /// - 如果仍然失败，抛出最后一次的异常
    /// </summary>
    private static T LoadFromJson<T>(string jsonFilePath)
        where T : class, new()
    {
        // 检查缓存
        if (_cache.TryGetValue(jsonFilePath, out var cached) && cached is T typed)
        {
            return typed;
        }

        // 文件必须存在
        if (!File.Exists(jsonFilePath))
        {
            throw new FileNotFoundException($"找不到配置文件: {jsonFilePath}", jsonFilePath);
        }

        // 带重试的文件读取（防止读到半份内容）
        var json = ReadFileWithRetry(jsonFilePath);

        try
        {
            var config = JsonSerializer.Deserialize<T>(json, _jsonOptions);

            if (config == null)
                throw new InvalidOperationException($"配置文件 {jsonFilePath} 反序列化结果为空");

            // 加入缓存
            _cache.TryAdd(jsonFilePath, config);
            return config;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"配置文件 {jsonFilePath} 格式错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 带重试机制的文件读取
    ///
    /// ⚠️ 并发安全说明：
    /// - 文件可能被外部程序（如编辑器）正在写入
    /// - File.ReadAllText 不会等文件解锁，可能读到半份内容
    /// - 使用 FileShare.ReadWrite + 重试机制，最大限度避免读到不完整内容
    /// - 如果重试 3 次后仍然失败，抛出异常
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="maxRetries">最大重试次数，默认 3</param>
    /// <param name="delayMs">每次重试间隔（毫秒），默认 50</param>
    /// <returns>文件内容</returns>
    private static string ReadFileWithRetry(string filePath, int maxRetries = 3, int delayMs = 50)
    {
        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // 使用 FileStream + FileShare.ReadWrite，允许其他进程同时读取
                // 这样如果文件正在被写入，我们能检测到并重试
                using var fileStream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                using var reader = new StreamReader(fileStream);
                return reader.ReadToEnd();
            }
            catch (IOException ex)
            {
                // 文件被占用或读取错误
                lastException = ex;

                if (attempt < maxRetries)
                {
                    Logger.Warning("Config", "读取配置文件失败（第 {0}/{1} 次），{2}ms 后重试: {3}",
                        attempt, maxRetries, delayMs, ex.Message);
                    Thread.Sleep(delayMs);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                // 权限错误，不需要重试
                throw new InvalidOperationException(
                    $"读取配置文件 {filePath} 权限不足: {ex.Message}", ex);
            }
        }

        // 重试次数用尽，抛出最后一次的异常
        throw new InvalidOperationException(
            $"读取配置文件 {filePath} 失败（重试 {maxRetries} 次后仍然失败）: {lastException?.Message}",
            lastException);
    }

    #endregion

    #region 私有方法 - 环境变量覆盖

    /// <summary>
    /// 用环境变量覆盖配置对象的属性值
    ///
    /// 规则：
    /// - 环境变量名 = 类名_属性名（例如 ServerConfig_Port = 8080）
    /// - 不区分大小写
    /// - 支持的类型：string、int、long、double、float、bool、enum
    /// - 如果环境变量不存在，保持原值（不覆盖）
    /// </summary>
    private static void ApplyEnvironmentVariables(object config)
    {
        if (config == null)
            return;

        var type = config.GetType();
        var typeName = type.Name;

        foreach (var prop in type.GetProperties())
        {
            if (!prop.CanWrite)
                continue;

            // 支持两种命名方式：ClassName_PropertyName 或 PropertyName
            var envKey1 = $"{typeName}_{prop.Name}";
            var envKey2 = prop.Name;

            var value = Environment.GetEnvironmentVariable(envKey1)
                        ?? Environment.GetEnvironmentVariable(envKey2);

            if (string.IsNullOrEmpty(value))
                continue;

            try
            {
                var converted = Convert.ChangeType(value, prop.PropertyType);
                prop.SetValue(config, converted);
            }
            catch
            {
                // 转换失败就跳过（忽略错误的环境变量）
            }
        }
    }

    #endregion

    #region 私有方法 - 敏感字段解密

    /// <summary>
    /// 配置中需要解密的敏感字段名（不区分大小写）
    /// 
    /// 通过反射遍历，匹配这些属性名的 string 字段，
    /// 若值以 "ENC[v1]:" 开头则尝试解密。
    /// </summary>
    private static readonly string[] SensitiveFieldNames =
    {
        "Password", "CertificatePassword", "ConnectionString", "Secret", "Token", "ApiKey"
    };

    /// <summary>
    /// 递归遍历配置对象，对所有以 "ENC[v1]:" 开头的字符串属性执行解密
    /// 
    /// 解密失败时保留原密文，并在日志中记录警告（不抛异常，避免启动失败）
    /// </summary>
    private static void DecryptSensitiveFields(object config)
    {
        if (config == null) return;
        try
        {
            DecryptRecursive(config, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }
        catch (Exception ex)
        {
            Logger.Warning("Config", "敏感字段解密过程异常: {0}", ex.Message);
        }
    }

    private static void DecryptRecursive(object obj, HashSet<object> visited)
    {
        if (obj == null) return;
        if (!visited.Add(obj)) return; // 防止循环引用
        var type = obj.GetType();
        if (type.IsPrimitive || type == typeof(string) || type.IsEnum) return;
        if (type.Namespace?.StartsWith("System.", StringComparison.Ordinal) == true) return;

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (prop.GetIndexParameters().Length > 0) continue;

            try
            {
                if (prop.PropertyType == typeof(string) && IsSensitiveField(prop.Name))
                {
                    var raw = prop.GetValue(obj) as string;
                    if (!string.IsNullOrEmpty(raw) && ConfigEncryptor.IsEncrypted(raw))
                    {
                        if (ConfigEncryptor.TryDecrypt(raw, out var plain))
                        {
                            prop.SetValue(obj, plain);
                        }
                    }
                }
                else if (!prop.PropertyType.IsPrimitive
                    && prop.PropertyType != typeof(string)
                    && !prop.PropertyType.IsEnum
                    && !typeof(System.Collections.IEnumerable).IsAssignableFrom(prop.PropertyType))
                {
                    var child = prop.GetValue(obj);
                    if (child != null)
                    {
                        DecryptRecursive(child, visited);
                    }
                }
            }
            catch
            {
                // 忽略单个属性的处理异常
            }
        }
    }

    private static bool IsSensitiveField(string name)
    {
        foreach (var s in SensitiveFieldNames)
        {
            if (string.Equals(s, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    #endregion
}
