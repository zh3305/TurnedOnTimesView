using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TurnedOnTimesView.Models;

namespace TurnedOnTimesView.Services;

/// <summary>
/// Windows事件日志服务实现，使用System.Diagnostics.Eventing.Reader进行高性能事件查询
/// </summary>
public sealed class EventLogService : IEventLogService, IDisposable
{
    private readonly ILogger<EventLogService> _logger;
    private readonly IMemoryCache _cache;
    private readonly ArrayPool<SystemEvent> _eventPool;
    private readonly ConcurrentDictionary<string, EventLogReader> _readerCache;
    
    // 关键事件ID常量
    private static readonly int[] TargetEventIds = { 6005, 6006, 1074, 6008, 41, 42, 1 };
    
    // 性能优化常量
    private static readonly int MaxConcurrentReaders = Math.Min(Environment.ProcessorCount, 4); // 限制最大并发数
    private const int EventBatchSize = 1000; // 增加批处理大小
    private const int CacheExpirationMinutes = 10; // 增加缓存时间
    
    // 用于提取关机原因的正则表达式
    private static readonly Regex ShutdownReasonRegex = new(
        @"关机原因:\s*(.+?)(?:\r\n|\n|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    // 用于提取进程信息的正则表达式
    private static readonly Regex ProcessInfoRegex = new(
        @"进程\s+(.+?)\s+(?:\(PID\s+\d+\))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public EventLogService(ILogger<EventLogService> logger, IMemoryCache cache)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _eventPool = ArrayPool<SystemEvent>.Shared;
        _readerCache = new ConcurrentDictionary<string, EventLogReader>();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SystemEvent>> GetSystemEventsAsync(
        DateTime startDate, 
        DateTime endDate, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("开始获取系统事件，时间范围: {StartDate} - {EndDate}", 
            startDate.ToString("yyyy-MM-dd HH:mm:ss"), 
            endDate.ToString("yyyy-MM-dd HH:mm:ss"));

        if (!CanAccessEventLog())
        {
            _logger.LogError("没有访问事件日志的权限");
            throw new UnauthorizedAccessException("需要管理员权限才能访问Windows事件日志");
        }

        var events = new List<SystemEvent>();
        var eventIdFilter = string.Join(" or ", TargetEventIds.Select(id => $"EventID={id}"));
        
        // 检查缓存
        var cacheKey = $"events_{startDate:yyyyMMddHHmmss}_{endDate:yyyyMMddHHmmss}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SystemEvent>? cachedEvents))
        {
            _logger.LogDebug("从缓存中获取事件数据，共 {Count} 个事件", cachedEvents!.Count);
            return cachedEvents;
        }

        // 构建优化的XPath查询
        var xpath = BuildOptimizedXPath(eventIdFilter, startDate, endDate);

        try
        {
            events = await ProcessEventsWithChannelAsync(xpath, cancellationToken);
            
            // 缓存结果（指定缓存大小）
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheExpirationMinutes),
                Size = Math.Max(1, events.Count / 10) // 根据事件数量动态计算大小
            };
            _cache.Set(cacheKey, events.AsReadOnly(), cacheOptions);
            
            _logger.LogInformation("事件查询完成，共获取 {Count} 个有效事件", events.Count);
        }
        catch (EventLogException ex)
        {
            _logger.LogError(ex, "访问事件日志时发生错误");
            throw new InvalidOperationException("无法访问Windows事件日志", ex);
        }
        catch (SecurityException ex)
        {
            _logger.LogError(ex, "访问事件日志权限不足");
            throw new UnauthorizedAccessException("需要管理员权限才能访问Windows事件日志", ex);
        }

        // 按时间排序并返回只读列表
        var sortedEvents = events.OrderBy(e => e.TimeGenerated).ToList();
        
        _logger.LogInformation("成功获取并排序了 {Count} 个系统事件", sortedEvents.Count);
        return sortedEvents.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SystemEvent>> GetRecentSystemEventsAsync(
        int days = 30, 
        CancellationToken cancellationToken = default)
    {
        var endDate = DateTime.Now;
        var startDate = endDate.AddDays(-days);
        
        return await GetSystemEventsAsync(startDate, endDate, cancellationToken);
    }

    /// <inheritdoc />
    public bool CanAccessEventLog()
    {
        try
        {
            // 尝试创建一个简单的查询来测试权限
            var query = new EventLogQuery("System", PathType.LogName, "*[System[EventID=6005]]");
            using var reader = new EventLogReader(query);
            
            // 尝试读取一个事件（不关心结果）
            using var testEvent = reader.ReadEvent();
            
            return true;
        }
        catch (Exception ex) when (ex is EventLogException or SecurityException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "检测到无法访问事件日志");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<long> GetEventCountAsync(DateTime startDate, DateTime endDate)
    {
        // 检查缓存
        var cacheKey = $"count_{startDate:yyyyMMddHHmmss}_{endDate:yyyyMMddHHmmss}";
        if (_cache.TryGetValue(cacheKey, out long cachedCount))
        {
            return cachedCount;
        }

        var eventIdFilter = string.Join(" or ", TargetEventIds.Select(id => $"EventID={id}"));
        var xpath = BuildOptimizedXPath(eventIdFilter, startDate, endDate);

        var count = await Task.Run(() =>
        {
            try
            {
                var query = new EventLogQuery("System", PathType.LogName, xpath);
                using var reader = new EventLogReader(query);
                
                long eventCount = 0;
                const int batchSize = 1000;
                var batch = 0;
                
                while (reader.ReadEvent() is EventRecord eventRecord)
                {
                    using (eventRecord)
                    {
                        eventCount++;
                        
                        // 定期检查取消请求
                        if (++batch % batchSize == 0)
                        {
                            // 可以在这里添加进度报告
                        }
                    }
                }
                
                return eventCount;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取事件计数时发生错误");
                return 0L;
            }
        });

        // 缓存结果（指定缓存大小）
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheExpirationMinutes),
            Size = 1 // 为计数结果指定大小
        };
        _cache.Set(cacheKey, count, cacheOptions);
        return count;
    }

    /// <summary>
    /// 将EventRecord转换为SystemEvent
    /// </summary>
    private SystemEvent? ConvertToSystemEvent(EventRecord eventRecord)
    {
        try
        {
            var message = eventRecord.FormatDescription() ?? string.Empty;
            var shutdownReason = ExtractShutdownReason(message);
            var processInfo = ExtractProcessInfo(message);

            return new SystemEvent
            {
                EventId = eventRecord.Id,
                TimeGenerated = eventRecord.TimeCreated ?? DateTime.Now,
                Source = eventRecord.ProviderName ?? "Unknown",
                Message = message,
                Level = eventRecord.Level?.ToString() ?? "Information",
                RecordId = eventRecord.RecordId ?? 0,
                UserSid = eventRecord.UserId?.ToString(),
                ProcessInfo = processInfo,
                ShutdownReason = shutdownReason
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "转换事件记录时发生错误，EventId: {EventId}, RecordId: {RecordId}", 
                eventRecord.Id, eventRecord.RecordId);
            return null;
        }
    }

    /// <summary>
    /// 从事件消息中提取关机原因
    /// </summary>
    private static string ExtractShutdownReason(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        var match = ShutdownReasonRegex.Match(message);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    /// <summary>
    /// 从事件消息中提取进程信息
    /// </summary>
    private static string ExtractProcessInfo(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        var match = ProcessInfoRegex.Match(message);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    /// <summary>
    /// 构建优化的XPath查询
    /// </summary>
    private static string BuildOptimizedXPath(string eventIdFilter, DateTime startDate, DateTime endDate)
    {
        return $@"*[System[
            (Provider[@Name='Microsoft-Windows-Kernel-General'] or 
             Provider[@Name='Microsoft-Windows-Kernel-Power'] or 
             Provider[@Name='Microsoft-Windows-Winlogon'] or 
             Provider[@Name='EventLog'] or 
             Provider[@Name='User32']) and 
            ({eventIdFilter}) and 
            TimeCreated[@SystemTime>='{startDate:yyyy-MM-ddTHH:mm:ss.fffZ}' and 
                        @SystemTime<='{endDate:yyyy-MM-ddTHH:mm:ss.fffZ}']
        ]]";
    }

    /// <summary>
    /// 使用Channel进行高性能事件处理
    /// </summary>
    private async Task<List<SystemEvent>> ProcessEventsWithChannelAsync(string xpath, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<EventRecord>();
        var writer = channel.Writer;
        var reader = channel.Reader;
        var events = new ConcurrentBag<SystemEvent>();

        // 生产者任务：读取事件记录
        var producerTask = Task.Run(async () =>
        {
            try
            {
                var query = new EventLogQuery("System", PathType.LogName, xpath);
                using var eventReader = new EventLogReader(query);
                
                EventRecord? eventRecord;
                while ((eventRecord = eventReader.ReadEvent()) != null)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                        
                    await writer.WriteAsync(eventRecord, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取事件记录时发生错误");
            }
            finally
            {
                writer.Complete();
            }
        }, cancellationToken);

        // 消费者任务：并行处理事件记录
        var consumerTasks = Enumerable.Range(0, Math.Min(MaxConcurrentReaders, Environment.ProcessorCount))
            .Select(_ => Task.Run(async () =>
            {
                var processedCount = 0;
                await foreach (var eventRecord in reader.ReadAllAsync(cancellationToken))
                {
                    try
                    {
                        var systemEvent = ConvertToSystemEvent(eventRecord);
                        if (systemEvent != null)
                        {
                            events.Add(systemEvent);
                        }
                        
                        processedCount++;
                        if (processedCount % EventBatchSize == 0)
                        {
                            _logger.LogDebug("线程已处理 {Count} 个事件记录", processedCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "处理事件记录时发生错误，记录ID: {RecordId}", eventRecord.RecordId);
                    }
                    finally
                    {
                        eventRecord.Dispose();
                    }
                }
            }, cancellationToken))
            .ToArray();

        // 等待所有任务完成
        await Task.WhenAll(new[] { producerTask }.Concat(consumerTasks));
        
        return events.ToList();
    }

    public void Dispose()
    {
        try
        {
            // 清理缓存的读取器
            foreach (var reader in _readerCache.Values)
            {
                reader?.Dispose();
            }
            _readerCache.Clear();
            
            // 释放缓存
            _cache?.Dispose();
            
            _logger.LogDebug("EventLogService 已释放所有资源");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "释放EventLogService资源时发生错误");
        }
    }
}