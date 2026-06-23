// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Text;
using MMORPG.Framework.Logging;
using MMORPG.Framework.Security;

namespace MMORPG.Framework.Observability;

/// <summary>
/// 崩溃异常收集器
/// 
/// 功能：
/// 1. 订阅 AppDomain.CurrentDomain.UnhandledException 事件
/// 2. 订阅 TaskScheduler.UnobservedTaskException 事件
/// 3. 对所有未处理的异常生成崩溃报告文件
/// 4. 将异常信息也记录到 Logger
/// 
/// 崩溃报告内容：
/// - 时间戳（UTC 和本地时间）
/// - 服务状态（ServiceState）
/// - 异常类型、Message、StackTrace
/// - 内部异常链
/// - 当前线程信息
/// - 进程信息（内存占用、GC 状态）
/// 
/// 文件命名：logs/crashes/crash-yyyyMMdd-HHmmss.ffffff.log
/// （使用毫秒/微秒确保唯一性）
/// </summary>
public static class CrashReporting
{
    /// <summary>是否已启用</summary>
    private static bool _enabled;

    /// <summary>启用状态锁</summary>
    private static readonly object _lock = new();

    /// <summary>崩溃文件目录（默认为 logs/crashes）</summary>
    private static string _crashDir = "logs/crashes";

    /// <summary>已记录的崩溃次数</summary>
    private static long _crashCount;

    /// <summary>
    /// 启用崩溃收集
    /// </summary>
    /// <param name="crashDirectory">崩溃报告目录（默认 logs/crashes）</param>
    public static void Enable(string? crashDirectory = null)
    {
        lock (_lock)
        {
            if (_enabled) return;

            if (!string.IsNullOrWhiteSpace(crashDirectory))
                _crashDir = crashDirectory;

            try
            {
                Directory.CreateDirectory(_crashDir);
            }
            catch (Exception ex)
            {
                Logger.Error("Crash", ex, "创建崩溃目录失败：{0}", _crashDir);
            }

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            _enabled = true;
            Logger.Info("Crash", "崩溃异常收集已启用，目录：{0}", _crashDir);
        }
    }

    /// <summary>
    /// 禁用崩溃收集
    /// </summary>
    public static void Disable()
    {
        lock (_lock)
        {
            if (!_enabled) return;

            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

            _enabled = false;
            Logger.Info("Crash", "崩溃异常收集已禁用");
        }
    }

    /// <summary>
    /// 手动报告一个异常（便于业务层在 try/catch 中主动上报）
    /// </summary>
    /// <param name="ex">异常对象</param>
    /// <param name="extraMessage">附加说明（例如出现的模块名）</param>
    public static void ReportException(Exception? ex, string? extraMessage = null)
    {
        if (ex == null) return;

        try
        {
            Interlocked.Increment(ref _crashCount);
            Logger.Error("Crash", ex, "崩溃异常报告：{0}", extraMessage ?? ex.Message);
            WriteCrashFile(ex, extraMessage);
        }
        catch (Exception fileEx)
        {
            Logger.Error("Crash", fileEx, "写入崩溃报告文件失败");
        }
    }

    /// <summary>
    /// 当前是否已启用
    /// </summary>
    public static bool IsEnabled
    {
        get { lock (_lock) return _enabled; }
    }

    /// <summary>
    /// 已记录的崩溃次数
    /// </summary>
    public static long CrashCount => Interlocked.Read(ref _crashCount);

    /// <summary>
    /// 当前崩溃报告目录
    /// </summary>
    public static string CrashDirectory => _crashDir;

    #region 私有方法

    /// <summary>
    /// 处理 AppDomain 未处理异常
    /// </summary>
    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        if (ex == null) return;

        Logger.Error("Crash", ex, "检测到未处理异常，IsTerminating={0}", e.IsTerminating);

        try
        {
            Interlocked.Increment(ref _crashCount);
            WriteCrashFile(ex, $"UnhandledException | IsTerminating={e.IsTerminating}");
        }
        catch (Exception fileEx)
        {
            Logger.Error("Crash", fileEx, "写入崩溃报告失败");
        }
    }

    /// <summary>
    /// 处理 TaskScheduler 未观察到的异常
    /// </summary>
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logger.Error("Crash", e.Exception, "检测到未观察到的任务异常");

        try
        {
            Interlocked.Increment(ref _crashCount);
            WriteCrashFile(e.Exception, "UnobservedTaskException");
            e.SetObserved();
        }
        catch (Exception fileEx)
        {
            Logger.Error("Crash", fileEx, "写入崩溃报告失败");
        }
    }

    /// <summary>
    /// 写入崩溃文件
    /// </summary>
    private static void WriteCrashFile(Exception ex, string? extraMessage)
    {
        try
        {
            Directory.CreateDirectory(_crashDir);

            var now = DateTime.Now;
            var fileName = $"crash-{now:yyyyMMdd-HHmmss}.{now.Millisecond:D3}{Guid.NewGuid().ToString("N").Substring(0, 4)}.log";
            var filePath = Path.Combine(_crashDir, fileName);

            var sb = new StringBuilder();
            sb.AppendLine("================================================");
            sb.AppendLine("  MMORPG 服务器崩溃报告");
            sb.AppendLine("================================================");
            sb.AppendLine($"[时间] UTC     : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fffffff}");
            sb.AppendLine($"[时间] 本地   : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fffffff}");
            sb.AppendLine($"[服务] 状态   : {ServiceStateManager.Instance.CurrentState}");
            sb.AppendLine($"[线程] 当前ID : {Environment.CurrentManagedThreadId}");
            sb.AppendLine($"[进程] 名称   : {Process.GetCurrentProcess().ProcessName}");
            sb.AppendLine($"[进程] PID     : {Process.GetCurrentProcess().Id}");
            sb.AppendLine($"[内存] 工作集 : {Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024:F2} MB");
            sb.AppendLine($"[GC]   第0代  : {GC.CollectionCount(0)} 次");
            sb.AppendLine($"[GC]   第1代  : {GC.CollectionCount(1)} 次");
            sb.AppendLine($"[GC]   第2代  : {GC.CollectionCount(2)} 次");
            sb.AppendLine($"[运行] 时间   : {(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalHours:F2} 小时");
            sb.AppendLine($"[崩溃] 计数   : {_crashCount}");
            sb.AppendLine();
            sb.AppendLine($"[附加信息]     : {extraMessage ?? "(无)"}");
            sb.AppendLine();
            sb.AppendLine("================================================ 异常信息 ============================================");
            sb.AppendLine($"异常类型       : {ex.GetType().FullName}");
            sb.AppendLine($"异常消息       : {ex.Message}");
            sb.AppendLine($"HResult        : {ex.HResult}");
            sb.AppendLine();
            sb.AppendLine("=========================== StackTrace ===========================");
            sb.AppendLine(ex.StackTrace ?? "(无)");
            sb.AppendLine();

            var inner = ex.InnerException;
            var level = 1;
            while (inner != null)
            {
                sb.AppendLine($"=========================== InnerException #{level} ===========================");
                sb.AppendLine($"  类型           : {inner.GetType().FullName}");
                sb.AppendLine($"  消息           : {inner.Message}");
                sb.AppendLine($"  StackTrace     : {inner.StackTrace}");
                sb.AppendLine();
                inner = inner.InnerException;
                level++;
            }

            sb.AppendLine("=============================================== 文件结束 ===========================================");
            File.WriteAllText(filePath, sb.ToString());
            Logger.Info("Crash", "崩溃报告已保存：{0}", filePath);
        }
        catch (Exception outer)
        {
            Logger.Error("Crash", outer, "写入崩溃文件时发生异常（忽略）");
        }
    }

    #endregion
}
