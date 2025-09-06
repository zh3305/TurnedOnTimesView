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
    /// 是否为启动事件
    /// </summary>
    public bool IsStartupEvent => EventId == 6005;

    /// <summary>
    /// 是否为关机事件
    /// </summary>
    public bool IsShutdownEvent => EventId is 6006 or 1074;

    /// <summary>
    /// 是否为异常关机事件
    /// </summary>
    public bool IsAbnormalShutdownEvent => EventId is 6008 or 41;

    /// <summary>
    /// 是否为睡眠事件
    /// </summary>
    public bool IsSleepEvent => EventId == 42;

    /// <summary>
    /// 是否为唤醒事件
    /// </summary>
    public bool IsWakeupEvent => EventId == 1;

    /// <summary>
    /// 获取事件类型描述
    /// </summary>
    public string EventTypeDescription => EventId switch
    {
        6005 => "系统启动",
        6006 => "系统关机",
        1074 => "用户关机",
        6008 => "意外关机",
        41 => "意外重启",
        42 => "系统睡眠",
        1 => "系统唤醒",
        _ => "未知事件"
    };

    public override string ToString()
    {
        return $"[{EventId}] {TimeGenerated:yyyy-MM-dd HH:mm:ss} - {EventTypeDescription}";
    }
}