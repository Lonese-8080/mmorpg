// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.IO.Compression;

namespace MMORPG.Framework.Logging;

/// <summary>
/// 文件日志输出
/// 
/// 特性：
/// - JSON 格式输出，便于日志分析
/// - 自动滚动（日文件或大小文件）
/// - 自动清理过期日志
/// - Gzip 压缩旧日志
/// 
/// 文件命名规则：
/// - mmorpg_20240115.json（按天）
/// - mmorpg_20240115_001.json（多文件时带序号）
/// - mmorpg_20240115.json.gz（压缩文件）
/// </summary>
public class FileSink : ILogSink
{
    #region 私有字段

    /// <summary>
    /// 日志目录
    /// </summary>
    private readonly string _directory;

    /// <summary>
    /// 单文件最大大小
    /// </summary>
    private readonly long _maxFileSize;

    /// <summary>
    /// 日志保留天数
    /// </summary>
    private readonly int _retentionDays;

    /// <summary>
    /// 当前写入文件路径
    /// </summary>
    private string _currentFile = string.Empty;

    /// <summary>
    /// 当前文件大小
    /// </summary>
    private long _currentFileSize;

    /// <summary>
    /// 文件写入流
    /// </summary>
    private StreamWriter? _writer;

    /// <summary>
    /// 写入锁
    /// </summary>
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// 是否已释放
    /// </summary>
    private bool _disposed;

    #endregion

    #region 构造函数

    /// <summary>
    /// 创建文件日志输出
    /// </summary>
    /// <param name="directory">日志目录</param>
    /// <param name="maxFileSize">单文件最大大小（字节）</param>
    /// <param name="retentionDays">保留天数</param>
    public FileSink(string directory, long maxFileSize, int retentionDays)
    {
        _directory = directory;
        _maxFileSize = maxFileSize;
        _retentionDays = retentionDays;

        // 确保目录存在
        Directory.CreateDirectory(_directory);

        // 打开今天的日志文件
        OpenNewFile();

        // 清理过期文件
        CleanupOldFiles();
    }

    #endregion

    #region ILogSink 实现

    /// <summary>
    /// 写入日志到文件
    /// </summary>
    /// <param name="entry">日志条目</param>
    public async Task WriteAsync(LogEntry entry)
    {
        await _writeLock.WaitAsync();
        try
        {
            // 格式化为 JSON
            var line = entry.ToJson();

            // 检查是否需要滚动文件
            if (_currentFileSize + line.Length > _maxFileSize)
            {
                await RotateFileAsync();
            }

            // 写入文件
            await _writer!.WriteLineAsync(line);
            await _writer.FlushAsync();
            _currentFileSize += line.Length + Environment.NewLine.Length;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 打开新的日志文件
    /// </summary>
    private void OpenNewFile()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        _currentFile = Path.Combine(_directory, $"mmorpg_{timestamp}.json");
        _writer = new StreamWriter(_currentFile, append: true)
        {
            AutoFlush = false  // 批量写入，提高性能
        };
        _currentFileSize = 0;
    }

    /// <summary>
    /// 滚动到新文件
    /// </summary>
    private async Task RotateFileAsync()
    {
        // 关闭当前文件
        await _writer!.DisposeAsync();
        _writer = null;

        // 压缩旧文件（异步进行）
        _ = Task.Run(() => CompressFile(_currentFile));

        // 打开新文件
        OpenNewFile();
    }

    /// <summary>
    /// 压缩日志文件
    /// </summary>
    /// <param name="file">文件路径</param>
    private void CompressFile(string file)
    {
        try
        {
            if (!File.Exists(file))
                return;

            var compressed = file + ".gz";

            using var inputStream = File.OpenRead(file);
            using var outputStream = File.Create(compressed);
            using var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal);

            inputStream.CopyTo(gzipStream);

            // 压缩成功后删除原文件
            File.Delete(file);

            Logger.Debug("FileSink", "日志文件已压缩: {0} -> {1}", file, compressed);
        }
        catch (Exception ex)
        {
            Logger.Error("FileSink", ex, "日志文件压缩失败: {0}", file);
        }
    }

    /// <summary>
    /// 清理过期日志文件
    /// </summary>
    private void CleanupOldFiles()
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-_retentionDays);

            var files = Directory.GetFiles(_directory, "mmorpg_*.json*");

            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);

                    // 检查最后修改时间
                    if (fileInfo.LastWriteTimeUtc < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // 忽略清理错误
                }
            }
        }
        catch
        {
            // 忽略清理错误
        }
    }

    #endregion

    #region IDisposable 实现

    /// <summary>
    /// 释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _writeLock.WaitAsync();
        try
        {
            // 关闭文件流
            if (_writer != null)
            {
                await _writer.FlushAsync();
                await _writer.DisposeAsync();
                _writer = null;
            }

            _disposed = true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    #endregion
}
