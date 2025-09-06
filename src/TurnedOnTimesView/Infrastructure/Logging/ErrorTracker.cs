using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurnedOnTimesView.Infrastructure.Exceptions;

namespace TurnedOnTimesView.Infrastructure.Logging;

/// <summary>
/// 错误跟踪记录
/// </summary>
public class ErrorRecord
{
    /// <summary>
    /// 错误ID
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// 错误发生时间
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;
    
    /// <summary>
    /// 错误代码
    /// </summary>
    public string ErrorCode { get; init; } = string.Empty;
    
    /// <summary>
    /// 用户消息
    /// </summary>
    public string UserMessage { get; init; } = string.Empty;
    
    /// <summary>
    /// 技术消息
    /// </summary>
    public string TechnicalMessage { get; init; } = string.Empty;
    
    /// <summary>
    /// 错误严重性
    /// </summary>
    public ErrorSeverity Severity { get; init; }
    
    /// <summary>
    /// 错误上下文
    /// </summary>
    public string Context { get; init; } = string.Empty;
    
    /// <summary>
    /// 操作类型
    /// </summary>
    public string OperationType { get; init; } = string.Empty;
    
    /// <summary>
    /// 用户ID
    /// </summary>
    public string? UserId { get; init; }
    
    /// <summary>
    /// 会话ID
    /// </summary>
    public string? SessionId { get; init; }
    
    /// <summary>
    /// 错误详细信息
    /// </summary>
    public Dictionary<string, object> Details { get; init; } = new();
    
    /// <summary>
    /// 异常堆栈跟踪
    /// </summary>
    public string? StackTrace { get; init; }
    
    /// <summary>
    /// 是否已解决
    /// </summary>
    public bool IsResolved { get; set; }
    
    /// <summary>
    /// 解决时间
    /// </summary>
    public DateTime? ResolvedAt { get; set; }
    
    /// <summary>
    /// 重试次数
    /// </summary>
    public int RetryCount { get; set; }
}

/// <summary>
/// 错误统计信息
/// </summary>
public class ErrorStatistics
{
    /// <summary>
    /// 总错误数
    /// </summary>
    public int TotalErrors { get; set; }
    
    /// <summary>
    /// 按严重性分组的错误数
    /// </summary>
    public Dictionary<ErrorSeverity, int> ErrorsBySeverity { get; set; } = new();
    
    /// <summary>
    /// 按错误代码分组的错误数
    /// </summary>
    public Dictionary<string, int> ErrorsByCode { get; set; } = new();
    
    /// <summary>
    /// 按操作类型分组的错误数
    /// </summary>
    public Dictionary<string, int> ErrorsByOperation { get; set; } = new();
    
    /// <summary>
    /// 最常见的错误
    /// </summary>
    public List<(string ErrorCode, int Count)> MostCommonErrors { get; set; } = new();
    
    /// <summary>
    /// 错误趋势（每小时）
    /// </summary>
    public Dictionary<DateTime, int> HourlyTrend { get; set; } = new();
    
    /// <summary>
    /// 平均解决时间（分钟）
    /// </summary>
    public double AverageResolutionTimeMinutes { get; set; }
    
    /// <summary>
    /// 未解决错误数
    /// </summary>
    public int UnresolvedErrors { get; set; }
}

/// <summary>
/// 错误跟踪器接口
/// </summary>
public interface IErrorTracker
{
    /// <summary>
    /// 记录错误
    /// </summary>
    /// <param name="exception">异常信息</param>
    /// <param name="context">错误上下文</param>
    /// <param name="operationType">操作类型</param>
    /// <param name="userId">用户ID</param>
    /// <param name="sessionId">会话ID</param>
    /// <returns>错误记录ID</returns>
    string TrackError(
        Exception exception, 
        string context = "", 
        string operationType = "",
        string? userId = null,
        string? sessionId = null);
    
    /// <summary>
    /// 记录应用程序异常
    /// </summary>
    /// <param name="appException">应用程序异常</param>
    /// <param name="context">错误上下文</param>
    /// <param name="operationType">操作类型</param>
    /// <param name="userId">用户ID</param>
    /// <param name="sessionId">会话ID</param>
    /// <returns>错误记录ID</returns>
    string TrackError(
        TurnedOnTimesViewException appException,
        string context = "",
        string operationType = "",
        string? userId = null,
        string? sessionId = null);
    
    /// <summary>
    /// 标记错误为已解决
    /// </summary>
    /// <param name="errorId">错误ID</param>
    void ResolveError(string errorId);
    
    /// <summary>
    /// 增加重试次数
    /// </summary>
    /// <param name="errorId">错误ID</param>
    void IncrementRetryCount(string errorId);
    
    /// <summary>
    /// 获取错误记录
    /// </summary>
    /// <param name="errorId">错误ID</param>
    /// <returns>错误记录</returns>
    ErrorRecord? GetError(string errorId);
    
    /// <summary>
    /// 获取最近的错误记录
    /// </summary>
    /// <param name="count">数量</param>
    /// <param name="severity">严重性过滤（可选）</param>
    /// <returns>错误记录列表</returns>
    IReadOnlyList<ErrorRecord> GetRecentErrors(int count = 100, ErrorSeverity? severity = null);
    
    /// <summary>
    /// 获取错误统计信息
    /// </summary>
    /// <param name="fromDate">开始日期（可选）</param>
    /// <param name="toDate">结束日期（可选）</param>
    /// <returns>错误统计信息</returns>
    ErrorStatistics GetStatistics(DateTime? fromDate = null, DateTime? toDate = null);
    
    /// <summary>
    /// 清理过期的错误记录
    /// </summary>
    /// <param name="olderThan">保留时间</param>
    /// <returns>清理的记录数</returns>
    int CleanupOldRecords(TimeSpan olderThan);
}

/// <summary>
/// 内存错误跟踪器实现
/// </summary>
public class InMemoryErrorTracker : IErrorTracker, IHostedService, IDisposable
{
    private readonly ILogger<InMemoryErrorTracker> _logger;
    private readonly ConcurrentDictionary<string, ErrorRecord> _errorRecords = new();
    private readonly Timer? _cleanupTimer;
    private readonly object _lockObject = new();
    
    // 配置参数
    private const int MaxRecords = 10000;
    private const int CleanupIntervalMinutes = 60;
    private const int DefaultRetentionDays = 7;

    public InMemoryErrorTracker(ILogger<InMemoryErrorTracker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // 设置定期清理定时器
        _cleanupTimer = new Timer(PerformCleanup, null, 
            TimeSpan.FromMinutes(CleanupIntervalMinutes), 
            TimeSpan.FromMinutes(CleanupIntervalMinutes));
    }

    /// <inheritdoc />
    public string TrackError(Exception exception, string context = "", string operationType = "", string? userId = null, string? sessionId = null)
    {
        if (exception is TurnedOnTimesViewException appException)
        {
            return TrackError(appException, context, operationType, userId, sessionId);
        }
        
        var translatedError = ErrorMessages.TranslateException(exception, context);
        return TrackError(translatedError, context, operationType, userId, sessionId);
    }

    /// <inheritdoc />
    public string TrackError(TurnedOnTimesViewException appException, string context = "", string operationType = "", string? userId = null, string? sessionId = null)
    {
        var errorRecord = new ErrorRecord
        {
            ErrorCode = appException.ErrorCode,
            UserMessage = appException.UserMessage,
            TechnicalMessage = appException.Message,
            Severity = appException.Severity,
            Context = context,
            OperationType = operationType,
            UserId = userId,
            SessionId = sessionId,
            Details = new Dictionary<string, object>(appException.Details),
            StackTrace = appException.StackTrace
        };
        
        // 如果超过最大记录数，先清理一些旧记录
        if (_errorRecords.Count >= MaxRecords)
        {
            CleanupOldRecords(TimeSpan.FromDays(1)); // 清理1天以上的记录
        }
        
        _errorRecords.TryAdd(errorRecord.Id, errorRecord);
        
        _logger.LogError("错误已记录: {ErrorId} - {ErrorCode} - {UserMessage}", 
            errorRecord.Id, errorRecord.ErrorCode, errorRecord.UserMessage);
        
        return errorRecord.Id;
    }

    /// <inheritdoc />
    public void ResolveError(string errorId)
    {
        if (_errorRecords.TryGetValue(errorId, out var errorRecord))
        {
            errorRecord.IsResolved = true;
            errorRecord.ResolvedAt = DateTime.Now;
            
            _logger.LogInformation("错误已解决: {ErrorId} - {ErrorCode}", errorId, errorRecord.ErrorCode);
        }
    }

    /// <inheritdoc />
    public void IncrementRetryCount(string errorId)
    {
        if (_errorRecords.TryGetValue(errorId, out var errorRecord))
        {
            errorRecord.RetryCount++;
            
            _logger.LogDebug("错误重试次数增加: {ErrorId} - 重试次数: {RetryCount}", 
                errorId, errorRecord.RetryCount);
        }
    }

    /// <inheritdoc />
    public ErrorRecord? GetError(string errorId)
    {
        _errorRecords.TryGetValue(errorId, out var errorRecord);
        return errorRecord;
    }

    /// <inheritdoc />
    public IReadOnlyList<ErrorRecord> GetRecentErrors(int count = 100, ErrorSeverity? severity = null)
    {
        var query = _errorRecords.Values.AsEnumerable();
        
        if (severity.HasValue)
        {
            query = query.Where(e => e.Severity == severity.Value);
        }
        
        return query
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToList();
    }

    /// <inheritdoc />
    public ErrorStatistics GetStatistics(DateTime? fromDate = null, DateTime? toDate = null)
    {
        fromDate ??= DateTime.Now.AddDays(-7); // 默认最近7天
        toDate ??= DateTime.Now;
        
        var filteredErrors = _errorRecords.Values
            .Where(e => e.Timestamp >= fromDate && e.Timestamp <= toDate)
            .ToList();
        
        var stats = new ErrorStatistics
        {
            TotalErrors = filteredErrors.Count,
            UnresolvedErrors = filteredErrors.Count(e => !e.IsResolved)
        };
        
        // 按严重性分组
        stats.ErrorsBySeverity = filteredErrors
            .GroupBy(e => e.Severity)
            .ToDictionary(g => g.Key, g => g.Count());
        
        // 按错误代码分组
        stats.ErrorsByCode = filteredErrors
            .GroupBy(e => e.ErrorCode)
            .ToDictionary(g => g.Key, g => g.Count());
        
        // 按操作类型分组
        stats.ErrorsByOperation = filteredErrors
            .Where(e => !string.IsNullOrEmpty(e.OperationType))
            .GroupBy(e => e.OperationType)
            .ToDictionary(g => g.Key, g => g.Count());
        
        // 最常见的错误（前10）
        stats.MostCommonErrors = stats.ErrorsByCode
            .OrderByDescending(kvp => kvp.Value)
            .Take(10)
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();
        
        // 每小时错误趋势
        var hourlyGroups = filteredErrors
            .GroupBy(e => new DateTime(e.Timestamp.Year, e.Timestamp.Month, e.Timestamp.Day, e.Timestamp.Hour, 0, 0))
            .ToDictionary(g => g.Key, g => g.Count());
        
        stats.HourlyTrend = hourlyGroups;
        
        // 平均解决时间
        var resolvedErrors = filteredErrors.Where(e => e.IsResolved && e.ResolvedAt.HasValue).ToList();
        if (resolvedErrors.Any())
        {
            stats.AverageResolutionTimeMinutes = resolvedErrors
                .Average(e => (e.ResolvedAt!.Value - e.Timestamp).TotalMinutes);
        }
        
        return stats;
    }

    /// <inheritdoc />
    public int CleanupOldRecords(TimeSpan olderThan)
    {
        lock (_lockObject)
        {
            var cutoffTime = DateTime.Now - olderThan;
            var keysToRemove = _errorRecords
                .Where(kvp => kvp.Value.Timestamp < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();
            
            var removedCount = 0;
            foreach (var key in keysToRemove)
            {
                if (_errorRecords.TryRemove(key, out _))
                {
                    removedCount++;
                }
            }
            
            if (removedCount > 0)
            {
                _logger.LogInformation("清理了 {Count} 个过期错误记录", removedCount);
            }
            
            return removedCount;
        }
    }
    
    /// <summary>
    /// 定期清理过期记录
    /// </summary>
    private void PerformCleanup(object? state)
    {
        try
        {
            CleanupOldRecords(TimeSpan.FromDays(DefaultRetentionDays));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "定期清理错误记录时发生异常");
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("错误跟踪器服务已启动");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("错误跟踪器服务正在停止");
        _cleanupTimer?.Dispose();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 错误跟踪器扩展方法
/// </summary>
public static class ErrorTrackerExtensions
{
    /// <summary>
    /// 记录并跟踪异常
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="errorTracker">错误跟踪器</param>
    /// <param name="exception">异常</param>
    /// <param name="context">上下文</param>
    /// <param name="operationType">操作类型</param>
    /// <returns>错误ID</returns>
    public static string LogAndTrackError(
        this ILogger logger,
        IErrorTracker errorTracker,
        Exception exception,
        string context = "",
        string operationType = "")
    {
        // 记录详细日志
        logger.LogError(exception, "发生错误 - 上下文: {Context}, 操作: {Operation}", context, operationType);
        
        // 跟踪错误
        return errorTracker.TrackError(exception, context, operationType);
    }
    
    /// <summary>
    /// 记录并跟踪应用程序异常
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="errorTracker">错误跟踪器</param>
    /// <param name="appException">应用程序异常</param>
    /// <param name="context">上下文</param>
    /// <param name="operationType">操作类型</param>
    /// <returns>错误ID</returns>
    public static string LogAndTrackError(
        this ILogger logger,
        IErrorTracker errorTracker,
        TurnedOnTimesViewException appException,
        string context = "",
        string operationType = "")
    {
        // 记录结构化日志
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["ErrorCode"] = appException.ErrorCode,
            ["Severity"] = appException.Severity,
            ["Context"] = context,
            ["Operation"] = operationType,
            ["Details"] = appException.Details
        }))
        {
            logger.LogError(appException, "应用程序错误 - {ErrorCode}: {UserMessage}", 
                appException.ErrorCode, appException.UserMessage);
        }
        
        // 跟踪错误
        return errorTracker.TrackError(appException, context, operationType);
    }
}