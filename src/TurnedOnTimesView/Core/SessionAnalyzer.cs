using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TurnedOnTimesView.Infrastructure.Logging;
using TurnedOnTimesView.Models;

namespace TurnedOnTimesView.Core;

/// <summary>
/// 会话分析器实现，使用优化的状态机算法进行事件配对和会话识别
/// </summary>
public sealed class SessionAnalyzer : ISessionAnalyzer
{
    private readonly ILogger<SessionAnalyzer> _logger;
    private readonly ArrayPool<SystemEvent> _eventPool = ArrayPool<SystemEvent>.Shared;
    
    // 性能优化常量
    private const int MaxParallelSessions = 4;
    private const int SessionBatchSize = 100;

    public SessionAnalyzer(ILogger<SessionAnalyzer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionRecord>> AnalyzeSessionsAsync(
        IReadOnlyList<SystemEvent> events, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("开始分析 {EventCount} 个系统事件", events.Count);

        using var _ = _logger.LogOperationTime("会话分析");

        if (events.Count == 0)
        {
            _logger.LogWarning("没有事件数据可供分析");
            return [];
        }

        // 验证事件序列
        var validationResult = ValidateEventSequence(events);
        if (!validationResult.IsValid)
        {
            _logger.LogError("事件序列验证失败: {Messages}", string.Join(", ", validationResult.Messages));
        }
        else if (validationResult.Messages.Count > 0)
        {
            _logger.LogWarning("事件序列存在警告: {Messages}", string.Join(", ", validationResult.Messages));
        }

        // 在后台线程执行分析，使用优化算法
        var sessions = await AnalyzeEventSequenceOptimizedAsync(events, cancellationToken);

        _logger.LogInformation("会话分析完成，生成 {SessionCount} 个会话记录", sessions.Count);
        
        return sessions;
    }

    /// <summary>
    /// 优化的异步事件序列分析
    /// </summary>
    private async Task<IReadOnlyList<SessionRecord>> AnalyzeEventSequenceOptimizedAsync(
        IReadOnlyList<SystemEvent> events, 
        CancellationToken cancellationToken)
    {
        if (events.Count == 0)
            return [];

        return await Task.Run(() =>
        {
            // 使用预排序的事件数据（EventLogService已经排序）
            var sortedEvents = events.Count > 100 ? 
                events.OrderBy(e => e.TimeGenerated).ToArray() : // 大量数据时重新排序
                events.ToArray(); // 小量数据直接使用

            var sessions = new ConcurrentBag<SessionRecord>();
            var sessionState = new SessionAnalysisState();

            _logger.LogDebug("开始优化处理 {EventCount} 个事件", sortedEvents.Length);

            // 始终使用顺序处理确保会话配对正确性
            // 并行处理会破坏事件的时间顺序，导致启动-关机事件配对错误
            ProcessEventsSequentially(sortedEvents, sessionState, sessions);

            // 处理最后的未完成会话
            FinalizePendingSessions(sessionState, sessions);

            var result = sessions.OrderBy(s => s.StartTime).ToList();
            _logger.LogDebug("事件处理完成，生成 {SessionCount} 个会话", result.Count);
            
            return result.AsReadOnly();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public IReadOnlyList<SessionRecord> AnalyzeEventSequence(IReadOnlyList<SystemEvent> events)
    {
        if (events.Count == 0)
            return [];

        var sortedEvents = events.OrderBy(e => e.TimeGenerated).ToArray();
        var sessions = new ConcurrentBag<SessionRecord>();
        var sessionState = new SessionAnalysisState();

        _logger.LogDebug("开始处理 {EventCount} 个排序后的事件", sortedEvents.Length);

        ProcessEventsSequentially(sortedEvents, sessionState, sessions);
        FinalizePendingSessions(sessionState, sessions);

        var result = sessions.OrderBy(s => s.StartTime).ToList();
        _logger.LogDebug("事件处理完成，生成 {SessionCount} 个会话", result.Count);
        
        return result.AsReadOnly();
    }

    /// <inheritdoc />
    public SessionAnalysisResult ValidateEventSequence(IReadOnlyList<SystemEvent> events)
    {
        if (events.Count == 0)
            return SessionAnalysisResult.Success();

        var messages = new List<string>();
        var startupCount = 0;
        var shutdownCount = 0;
        var abnormalCount = 0;

        // 优化的事件统计
        Span<int> eventCounts = stackalloc int[3]; // [startup, shutdown, abnormal]
        
        foreach (var evt in events)
        {
            switch (evt.EventId)
            {
                case 6005: // 系统启动
                    eventCounts[0]++;
                    break;
                case 6006 or 1074: // 正常关机
                    eventCounts[1]++;
                    break;
                case 6008 or 41: // 异常关机
                    eventCounts[2]++;
                    break;
            }
        }
        
        startupCount = eventCounts[0];
        shutdownCount = eventCounts[1];
        abnormalCount = eventCounts[2];

        // 检查事件序列的合理性
        if (startupCount == 0 && shutdownCount > 0)
        {
            messages.Add($"发现 {shutdownCount} 个关机事件但没有启动事件");
        }

        if (Math.Abs(startupCount - shutdownCount) > 1)
        {
            messages.Add($"启动事件 ({startupCount}) 和关机事件 ({shutdownCount}) 数量差异较大");
        }

        if (abnormalCount > 0)
        {
            messages.Add($"发现 {abnormalCount} 个异常事件");
        }

        // 优化的时间序列检查（仅在发现问题时执行）
        if (events.Count > 1)
        {
            var hasTimeIssues = false;
            var prevTime = events[0].TimeGenerated;
            
            for (int i = 1; i < events.Count; i++)
            {
                if (events[i].TimeGenerated <= prevTime)
                {
                    hasTimeIssues = true;
                    break;
                }
                prevTime = events[i].TimeGenerated;
            }
            
            if (hasTimeIssues)
            {
                messages.Add("发现时间戳异常的事件");
            }
        }

        return messages.Count == 0 
            ? SessionAnalysisResult.Success() 
            : SessionAnalysisResult.WithWarnings([.. messages]);
    }

    /// <summary>
    /// 并行处理事件序列（适用于大量数据）
    /// </summary>
    private void ProcessEventsInParallel(
        SystemEvent[] sortedEvents, 
        ConcurrentBag<SessionRecord> sessions, 
        CancellationToken cancellationToken)
    {
        // 将数组分割成批次进行并行处理
        var batchSize = Math.Max(SessionBatchSize, sortedEvents.Length / MaxParallelSessions);
        var batches = new List<SystemEvent[]>();
        
        for (int i = 0; i < sortedEvents.Length; i += batchSize)
        {
            var size = Math.Min(batchSize, sortedEvents.Length - i);
            var batch = new SystemEvent[size];
            Array.Copy(sortedEvents, i, batch, 0, size);
            batches.Add(batch);
        }

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxParallelSessions,
            CancellationToken = cancellationToken
        };

        Parallel.ForEach(batches, parallelOptions, eventBatch =>
        {
            var localState = new SessionAnalysisState();
            var localSessions = new List<SessionRecord>();
            
            foreach (var evt in eventBatch)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                    
                try
                {
                    ProcessEventOptimized(evt, localState, localSessions);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "并行处理事件时发生错误: {Event}", evt);
                }
            }
            
            // 将本地结果添加到全局集合
            foreach (var session in localSessions)
            {
                sessions.Add(session);
            }
        });
    }

    /// <summary>
    /// 单线程处理事件序列
    /// </summary>
    private void ProcessEventsSequentially(
        SystemEvent[] sortedEvents, 
        SessionAnalysisState state, 
        ConcurrentBag<SessionRecord> sessions)
    {
        var sessionList = new List<SessionRecord>();
        
        foreach (var evt in sortedEvents)
        {
            try
            {
                ProcessEventOptimized(evt, state, sessionList);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "处理事件时发生错误: {Event}", evt);
            }
        }
        
        foreach (var session in sessionList)
        {
            sessions.Add(session);
        }
    }

    /// <summary>
    /// 优化的单个事件处理
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessEventOptimized(SystemEvent evt, SessionAnalysisState state, List<SessionRecord> sessions)
    {
        _logger.LogTrace("处理事件: {EventId} at {Time} - {Description}", 
            evt.EventId, evt.TimeGenerated, evt.EventTypeDescription);

        switch (evt.EventId)
        {
            case 6005: // 系统启动
                HandleStartupEvent(evt, state, sessions);
                break;

            case 6006: // 正常关机
            case 1074: // 用户关机
                HandleShutdownEvent(evt, state, sessions);
                break;

            case 6008: // 意外关机
            case 41:   // 意外重启
                HandleAbnormalShutdownEvent(evt, state, sessions);
                break;

            case 42:   // 系统睡眠
                HandleSleepEvent(evt, state, sessions);
                break;

            case 1:    // 系统唤醒
                HandleWakeupEvent(evt, state, sessions);
                break;

            default:
                _logger.LogTrace("忽略未知事件ID: {EventId}", evt.EventId);
                break;
        }

        // 更新最后事件时间
        state.LastEventTime = evt.TimeGenerated;
    }

    /// <summary>
    /// 处理启动事件
    /// </summary>
    private void HandleStartupEvent(SystemEvent evt, SessionAnalysisState state, List<SessionRecord> sessions)
    {
        // 如果有未完成的会话，先完成它（可能是异常关机）
        if (state.CurrentSession != null)
        {
            FinalizeSession(state.CurrentSession, evt.TimeGenerated, ShutdownType.Unexpected, 
                "系统异常关机（检测到新的启动事件）", "Unknown", sessions);
        }

        // 开始新会话
        state.CurrentSession = new SessionRecord
        {
            StartTime = evt.TimeGenerated,
            LastEventTime = evt.TimeGenerated,
            Type = ShutdownType.Unknown
        };

        _logger.LogDebug("开始新会话: {StartTime}", evt.TimeGenerated);
    }

    /// <summary>
    /// 处理正常关机事件
    /// </summary>
    private void HandleShutdownEvent(SystemEvent evt, SessionAnalysisState state, List<SessionRecord> sessions)
    {
        if (state.CurrentSession == null)
        {
            // 没有对应的启动事件，跳过这个关机事件
            // 这通常是由于系统日志不完整或事件丢失导致的
            _logger.LogWarning("跳过关机事件，没有对应的启动事件: {EventTime}", evt.TimeGenerated);
            return;
        }

        FinalizeSession(state.CurrentSession, evt.TimeGenerated, ShutdownType.Normal, 
            evt.ShutdownReason, evt.ProcessInfo, sessions);

        state.CurrentSession = null;
    }

    /// <summary>
    /// 处理异常关机事件
    /// </summary>
    private void HandleAbnormalShutdownEvent(SystemEvent evt, SessionAnalysisState state, List<SessionRecord> sessions)
    {
        if (state.CurrentSession == null)
        {
            // 异常关机通常表示上一次会话异常结束，创建估算会话
            var estimatedStartTime = evt.TimeGenerated.AddHours(-2); // 估算启动时间
            state.CurrentSession = new SessionRecord
            {
                StartTime = estimatedStartTime,
                LastEventTime = estimatedStartTime,
                Type = ShutdownType.Unknown
            };
        }

        var shutdownType = evt.EventId == 41 ? ShutdownType.Restart : ShutdownType.Unexpected;
        FinalizeSession(state.CurrentSession, evt.TimeGenerated, shutdownType, 
            evt.ShutdownReason, evt.ProcessInfo, sessions);

        state.CurrentSession = null;
    }

    /// <summary>
    /// 处理睡眠事件
    /// </summary>
    private void HandleSleepEvent(SystemEvent evt, SessionAnalysisState state, List<SessionRecord> sessions)
    {
        if (state.CurrentSession != null)
        {
            // 结束当前会话并标记为睡眠类型
            FinalizeSession(state.CurrentSession, evt.TimeGenerated, ShutdownType.Sleep, 
                "进入睡眠状态", "System", sessions);
                
            state.InSleepMode = true;
            state.CurrentSession = null; // 清除当前会话，等待唤醒事件
            
            _logger.LogDebug("系统进入睡眠模式: {SleepTime}", evt.TimeGenerated);
        }
    }

    /// <summary>
    /// 处理唤醒事件
    /// </summary>
    private void HandleWakeupEvent(SystemEvent evt, SessionAnalysisState state, List<SessionRecord> sessions)
    {
        if (state.InSleepMode)
        {
            // 从睡眠中唤醒，开始新会话
            state.CurrentSession = new SessionRecord
            {
                StartTime = evt.TimeGenerated,
                LastEventTime = evt.TimeGenerated,
                Type = ShutdownType.Unknown
            };
            
            state.InSleepMode = false;
            
            _logger.LogDebug("系统从睡眠模式唤醒: {WakeupTime}", evt.TimeGenerated);
        }
        else if (state.CurrentSession == null)
        {
            // 唤醒事件但没有当前会话，可能系统重启了
            state.CurrentSession = new SessionRecord
            {
                StartTime = evt.TimeGenerated,
                LastEventTime = evt.TimeGenerated,
                Type = ShutdownType.Unknown
            };
            
            _logger.LogDebug("检测到唤醒事件，创建新会话: {WakeupTime}", evt.TimeGenerated);
        }
    }

    /// <summary>
    /// 完成会话记录
    /// </summary>
    private void FinalizeSession(SessionRecord session, DateTime endTime, ShutdownType type, 
        string shutdownReason, string process, List<SessionRecord> sessions)
    {
        session.EndTime = endTime;
        session.Type = type;
        session.ShutdownReason = shutdownReason;
        session.Process = process;
        session.LastEventTime = endTime; // 确保最后事件时间是最新的

        sessions.Add(session);
        
        _logger.LogDebug("完成会话: {StartTime} - {EndTime}, 类型: {Type}, 持续时间: {Duration}", 
            session.StartTime, session.EndTime, session.Type, session.Duration);
    }

    /// <summary>
    /// 处理未完成的会话
    /// </summary>
    private void FinalizePendingSessions(SessionAnalysisState state, ConcurrentBag<SessionRecord> sessions)
    {
        if (state.CurrentSession != null)
        {
            // 当前会话仍在运行
            state.CurrentSession.ShutdownReason = "系统正在运行";
            state.CurrentSession.Process = "N/A";
            
            sessions.Add(state.CurrentSession);
            
            _logger.LogDebug("添加当前运行中的会话: {StartTime}", state.CurrentSession.StartTime);
        }
    }
    
    /// <summary>
    /// 处理未完成的会话（单线程版本）
    /// </summary>
    private void FinalizePendingSessions(SessionAnalysisState state, List<SessionRecord> sessions)
    {
        if (state.CurrentSession != null)
        {
            // 当前会话仍在运行
            state.CurrentSession.ShutdownReason = "系统正在运行";
            state.CurrentSession.Process = "N/A";
            
            sessions.Add(state.CurrentSession);
            
            _logger.LogDebug("添加当前运行中的会话: {StartTime}", state.CurrentSession.StartTime);
        }
    }

    /// <summary>
    /// 会话分析状态
    /// </summary>
    private sealed class SessionAnalysisState
    {
        public SessionRecord? CurrentSession { get; set; }
        public bool InSleepMode { get; set; }
        public DateTime LastEventTime { get; set; }
    }
}