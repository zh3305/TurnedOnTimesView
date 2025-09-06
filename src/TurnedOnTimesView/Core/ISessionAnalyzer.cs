using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TurnedOnTimesView.Models;

namespace TurnedOnTimesView.Core;

/// <summary>
/// 会话分析器接口，负责将系统事件转换为会话记录
/// </summary>
public interface ISessionAnalyzer
{
    /// <summary>
    /// 异步分析系统事件并生成会话记录
    /// </summary>
    /// <param name="events">系统事件列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>会话记录列表</returns>
    Task<IReadOnlyList<SessionRecord>> AnalyzeSessionsAsync(
        IReadOnlyList<SystemEvent> events, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 分析单个事件序列并返回可能的会话
    /// </summary>
    /// <param name="events">按时间排序的事件列表</param>
    /// <returns>会话记录列表</returns>
    IReadOnlyList<SessionRecord> AnalyzeEventSequence(IReadOnlyList<SystemEvent> events);

    /// <summary>
    /// 验证事件序列的完整性
    /// </summary>
    /// <param name="events">事件列表</param>
    /// <returns>验证结果，包含错误信息</returns>
    SessionAnalysisResult ValidateEventSequence(IReadOnlyList<SystemEvent> events);
}

/// <summary>
/// 会话分析结果
/// </summary>
public sealed record SessionAnalysisResult
{
    /// <summary>
    /// 是否分析成功
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// 错误或警告信息
    /// </summary>
    public IReadOnlyList<string> Messages { get; init; } = [];

    /// <summary>
    /// 未配对的启动事件数量
    /// </summary>
    public int UnpairedStartupEvents { get; init; }

    /// <summary>
    /// 未配对的关机事件数量
    /// </summary>
    public int UnpairedShutdownEvents { get; init; }

    /// <summary>
    /// 异常事件数量
    /// </summary>
    public int AbnormalEvents { get; init; }

    /// <summary>
    /// 创建成功的分析结果
    /// </summary>
    public static SessionAnalysisResult Success() => new() { IsValid = true };

    /// <summary>
    /// 创建包含警告的分析结果
    /// </summary>
    public static SessionAnalysisResult WithWarnings(params string[] warnings) => new()
    {
        IsValid = true,
        Messages = warnings.ToArray()
    };

    /// <summary>
    /// 创建失败的分析结果
    /// </summary>
    public static SessionAnalysisResult Failure(params string[] errors) => new()
    {
        IsValid = false,
        Messages = errors.ToArray()
    };
}