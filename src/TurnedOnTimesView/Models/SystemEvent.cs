using System;

namespace TurnedOnTimesView.Models;

/// <summary>
/// 表示系统事件的数据模型
/// </summary>
public sealed class SystemEvent
{
    /// <summary>
    /// 事件ID
    /// </summary>
    public int EventId { get; init; }

    /// <summary>
    /// 事件发生时间
    /// </summary>
    public DateTime TimeGenerated { get; init; }

    /// <summary>
    /// 事件源
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// 事件消息内容
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 事件级别（信息、警告、错误等）
    /// </summary>
    public string Level { get; init; } = string.Empty;

    /// <summary>
    /// 事件记录ID
    /// </summary>
    public long RecordId { get; init; }

    /// <summary>
    /// 用户SID（如果可用）
    /// </summary>
    public string? UserSid { get; init; }

    /// <summary>
    /// 进程信息（从消息中提取）
    /// </summary>
    public string ProcessInfo { get; init; } = string.Empty;

    /// <summary>
    /// 关机原因（从消息中提取）
    /// </summary>
    public string ShutdownReason { get; init; } = string.Empty;

    /// <summary>
    /// 关机类型（基于事件ID映射）
    /// </summary>
    public ShutdownType ShutdownType { get; init; } = ShutdownType.Unknown;

    /// <summary>
    /// 关机原因的中文描述
    /// </summary>
    public string ShutdownReasonDescription { get; init; } = string.Empty;

    /// <summary>
    /// 详细的事件描述
    /// </summary>
    public string DetailedDescription { get; init; } = string.Empty;

    /// <summary>
    /// 是否为启动事件
    /// </summary>
    public bool IsStartupEvent => ShutdownType == ShutdownType.Startup;

    /// <summary>
    /// 是否为关机事件
    /// </summary>
    public bool IsShutdownEvent => ShutdownType is ShutdownType.Normal or ShutdownType.UserInitiated or ShutdownType.SystemInitiated;

    /// <summary>
    /// 是否为异常关机事件
    /// </summary>
    public bool IsAbnormalShutdownEvent => ShutdownType is ShutdownType.Unexpected or ShutdownType.Forced;

    /// <summary>
    /// 是否为睡眠事件
    /// </summary>
    public bool IsSleepEvent => ShutdownType == ShutdownType.Sleep;

    /// <summary>
    /// 是否为休眠事件
    /// </summary>
    public bool IsHibernateEvent => ShutdownType == ShutdownType.Hibernate;

    /// <summary>
    /// 是否为唤醒事件
    /// </summary>
    public bool IsWakeupEvent => ShutdownType == ShutdownType.WakeUp;

    /// <summary>
    /// 是否为重启事件
    /// </summary>
    public bool IsRestartEvent => ShutdownType == ShutdownType.Restart;

    /// <summary>
    /// 获取事件类型描述（使用详细描述或默认描述）
    /// </summary>
    public string EventTypeDescription => !string.IsNullOrEmpty(DetailedDescription) 
        ? DetailedDescription 
        : ShutdownType.ToString();

    public override string ToString()
    {
        return $"[{EventId}] {TimeGenerated:yyyy-MM-dd HH:mm:ss} - {EventTypeDescription}";
    }
}