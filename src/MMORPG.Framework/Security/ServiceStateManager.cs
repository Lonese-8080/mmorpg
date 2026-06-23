// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using MMORPG.Framework.Logging;
using MMORPG.Framework.Network;

namespace MMORPG.Framework.Security;

/// <summary>
/// 服务状态枚举
/// 
/// 设计意图：
/// - Stopped: 服务未启动
/// - Starting: 正在启动（初始化资源）
/// - Running: 正常运行（处理所有消息）
/// - Pausing: 正在暂停
/// - Paused: 暂停（只处理系统消息/心跳）
/// - Stopping: 正在停止（释放资源）
/// </summary>
public enum ServiceState
{
    Stopped = 0,
    Starting = 1,
    Running = 2,
    Pausing = 3,
    Paused = 4,
    Stopping = 5
}

/// <summary>
/// 服务状态变更事件参数
/// </summary>
public class ServiceStateChangedEventArgs : EventArgs
{
    /// <summary>变更前状态</summary>
    public ServiceState OldState { get; }
    /// <summary>变更后状态</summary>
    public ServiceState NewState { get; }
    /// <summary>变更时间（UTC）</summary>
    public DateTime ChangedAtUtc { get; }

    public ServiceStateChangedEventArgs(ServiceState oldState, ServiceState newState)
    {
        OldState = oldState;
        NewState = newState;
        ChangedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>
/// 服务状态管理器（单例）
/// 
/// 管理服务的生命周期状态，提供：
/// 1. 状态流转（Start/Pause/Resume/Stop）
/// 2. 状态变更事件订阅
/// 3. 当前状态查询
/// 4. 消息路由辅助：是否应处理某类消息
/// 
/// 设计要点：
/// - 使用 SemaphoreSlim(1, 1) 确保同一时刻只有一个状态变更操作
/// - 状态流转有严格的合法性验证
/// - 状态变更都有日志记录
/// </summary>
public class ServiceStateManager
{
    /// <summary>单例实例</summary>
    public static ServiceStateManager Instance { get; } = new();

    /// <summary>状态变更锁</summary>
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    /// <summary>当前状态</summary>
    private ServiceState _currentState = ServiceState.Stopped;

    /// <summary>
    /// 状态变更事件
    /// 
    /// 每当服务状态从 OldState 变成 NewState 时触发
    /// </summary>
    public event EventHandler<ServiceStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 私有构造函数（保证单例）
    /// </summary>
    private ServiceStateManager()
    {
    }

    /// <summary>
    /// 当前状态（线程安全读取）
    /// </summary>
    public ServiceState CurrentState
    {
        get
        {
            _stateLock.Wait();
            try
            {
                return _currentState;
            }
            finally
            {
                _stateLock.Release();
            }
        }
    }

    /// <summary>
    /// 是否处于正常运行状态（Running）
    /// </summary>
    public bool IsRunning => CurrentState == ServiceState.Running;

    /// <summary>
    /// 是否应该正常路由消息（Running 或 Resuming 过程中）
    /// Paused 状态下只能通过系统消息/心跳
    /// </summary>
    public bool AcceptsNormalMessages
    {
        get
        {
            var s = CurrentState;
            return s == ServiceState.Running;
        }
    }

    /// <summary>
    /// 启动服务：Stopped → Starting → Running
    /// </summary>
    public async Task StartAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (_currentState != ServiceState.Stopped)
            {
                Logger.Warning("Service", "Start 被忽略：当前状态 = {0}", _currentState);
                return;
            }

            await ChangeStateInternal(ServiceState.Starting);
            await Task.Delay(50); // 模拟初始化（可移除，只是为了显示状态流转）
            await ChangeStateInternal(ServiceState.Running);
            Logger.Info("Service", "服务已启动，状态 = Running");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// 暂停服务：Running → Pausing → Paused
    /// </summary>
    public async Task PauseAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (_currentState != ServiceState.Running)
            {
                Logger.Warning("Service", "Pause 被忽略：当前状态 = {0}", _currentState);
                return;
            }

            await ChangeStateInternal(ServiceState.Pausing);
            await Task.Delay(50); // 模拟准备
            await ChangeStateInternal(ServiceState.Paused);
            Logger.Info("Service", "服务已暂停，状态 = Paused（仅处理系统消息）");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// 恢复服务：Paused → Running
    /// </summary>
    public async Task ResumeAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (_currentState != ServiceState.Paused)
            {
                Logger.Warning("Service", "Resume 被忽略：当前状态 = {0}", _currentState);
                return;
            }

            await ChangeStateInternal(ServiceState.Running);
            Logger.Info("Service", "服务已恢复，状态 = Running");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// 停止服务：Running/Paused → Stopping → Stopped
    /// </summary>
    public async Task StopAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            var s = _currentState;
            if (s == ServiceState.Stopped || s == ServiceState.Stopping)
            {
                return;
            }

            await ChangeStateInternal(ServiceState.Stopping);
            await Task.Delay(50);
            await ChangeStateInternal(ServiceState.Stopped);
            Logger.Info("Service", "服务已停止，状态 = Stopped");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// 重置状态（主要用于测试场景）
    /// </summary>
    public void Reset()
    {
        _stateLock.Wait();
        try
        {
            _currentState = ServiceState.Stopped;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// 内部状态变更（不做锁保护，由调用方保证）
    /// </summary>
    private async Task ChangeStateInternal(ServiceState newState)
    {
        var oldState = _currentState;
        if (oldState == newState) return;

        _currentState = newState;

        // 触发事件
        try
        {
            StateChanged?.Invoke(this, new ServiceStateChangedEventArgs(oldState, newState));
        }
        catch (Exception ex)
        {
            Logger.Error("Service", ex, "StateChanged 事件处理器异常：{0} → {1}", oldState, newState);
        }

        await Task.CompletedTask;
    }
}
