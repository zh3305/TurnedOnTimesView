using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TurnedOnTimesView.Infrastructure.Exceptions;
using TurnedOnTimesView.Infrastructure.Resilience;
using TurnedOnTimesView.Models;

namespace TurnedOnTimesView.Services;

/// <summary>
/// Windows事件日志服务实现，使用System.Diagnostics.Eventing.Reader进行高性能事件查询
/// </summary>
public sealed class EventLogService : IEventLogService, IDisposable
{
    private readonly ILogger<EventLogService> _logger;
    private readonly IMemoryCache _cache;
    private readonly EventMappingService _eventMappingService;
    private readonly ArrayPool<SystemEvent> _eventPool;
    private readonly ConcurrentDictionary<string, EventLogReader> _readerCache;
    private readonly RetryPolicy _fileRetryPolicy;
    private readonly RetryPolicy _systemRetryPolicy;
    
    // 关键事件ID常量 - 扩展支持更多事件类型
    private static readonly int[] TargetEventIds = { 
        // 系统启动事件
        6005, 6009, 12,
        // 正常关机事件  
        6006, 13,
        // 用户发起的关机/重启事件
        1074, 1075,
        // 系统发起的关机事件
        1076, 1077,
        // 意外关机事件
        6008, 6013,
        // 强制关机/意外重启事件
        41, 109,
        // 系统睡眠事件
        42, 4, 506,
        // 系统休眠事件
        27,
        // 唤醒事件
        1, 107, 507,
        // 重启相关事件
        1001, 1003,
        // Windows更新相关
        19, 20,
        // 服务和应用程序发起
        4609, 7001, 7034,
        // 电源按钮事件
        144, 145
    };
    
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

    public EventLogService(
        ILogger<EventLogService> logger, 
        IMemoryCache cache,
        EventMappingService eventMappingService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _eventMappingService = eventMappingService ?? throw new ArgumentNullException(nameof(eventMappingService));
        _eventPool = ArrayPool<SystemEvent>.Shared;
        _readerCache = new ConcurrentDictionary<string, EventLogReader>();
        
        // 初始化重试策略
        _fileRetryPolicy = RetryPolicies.FileOperations(_logger);
        _systemRetryPolicy = RetryPolicies.SystemServices(_logger);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SystemEvent>> GetSystemEventsAsync(
        DateTime startDate, 
        DateTime endDate, 
        CancellationToken cancellationToken = default)
    {
        // 验证输入参数
        var dateValidation = ErrorMessages.ValidateDateRange(startDate, endDate);
        if (dateValidation != null)
        {
            _logger.LogWarning("日期范围验证失败: {Errors}", string.Join(", ", dateValidation.ValidationErrors));
            throw dateValidation;
        }
        
        _logger.LogInformation("开始获取系统事件，时间范围: {StartDate} - {EndDate}", 
            startDate.ToString("yyyy-MM-dd HH:mm:ss"), 
            endDate.ToString("yyyy-MM-dd HH:mm:ss"));

        if (!CanAccessEventLog())
        {
            _logger.LogError("没有访问事件日志的权限");
            throw new InsufficientPermissionException(
                "管理员权限", 
                ErrorMessages.GetUserMessage("ADMIN_RIGHTS_REQUIRED"));
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
            events = await _systemRetryPolicy.ExecuteAsync(async ct =>
            {
                return await ProcessEventsWithChannelAsync(xpath, ct);
            }, 
            exception => exception is EventLogException or TimeoutException,
            cancellationToken);
            
            // 缓存结果（指定缓存大小）
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheExpirationMinutes),
                Size = Math.Max(1, events.Count / 10) // 根据事件数量动态计算大小
            };
            _cache.Set(cacheKey, events.AsReadOnly(), cacheOptions);
            
            _logger.LogInformation("事件查询完成，共获取 {Count} 个有效事件", events.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("获取系统事件操作被取消");
            throw new OperationCancelledException("获取系统事件", "操作被用户取消");
        }
        catch (EventLogException ex)
        {
            _logger.LogError(ex, "访问事件日志时发生错误");
            throw new EventLogServiceException(
                "EVENTLOG_ACCESS_ERROR", 
                ErrorMessages.GetUserMessage("EVENTLOG_SERVICE_UNAVAILABLE"),
                ex.Message, ex);
        }
        catch (SecurityException ex)
        {
            _logger.LogError(ex, "访问事件日志权限不足");
            throw new InsufficientPermissionException(
                "事件日志访问权限",
                ErrorMessages.GetUserMessage("EVENTLOG_ACCESS_DENIED"), ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "访问事件日志权限不足");
            throw new InsufficientPermissionException(
                "管理员权限",
                ErrorMessages.GetUserMessage("ADMIN_RIGHTS_REQUIRED"), ex);
        }
        catch (TurnedOnTimesViewException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var appException = ErrorMessages.TranslateException(ex, "获取系统事件");
            _logger.LogError(ex, "获取系统事件失败: {Error}", appException.UserMessage);
            throw appException;
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
                // 如果xpath为null，使用无过滤的查询
                var query = string.IsNullOrEmpty(xpath) 
                    ? new EventLogQuery("System", PathType.LogName)
                    : new EventLogQuery("System", PathType.LogName, xpath);
                using var reader = new EventLogReader(query);
                
                long eventCount = 0;
                const int batchSize = 1000;
                var batch = 0;
                
                while (reader.ReadEvent() is EventRecord eventRecord)
                {
                    using (eventRecord)
                    {
                        // 只计算我们感兴趣的事件ID
                        if (TargetEventIds.Contains(eventRecord.Id))
                        {
                            eventCount++;
                        }
                        
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
            var eventId = eventRecord.Id;
            
            // 只处理我们感兴趣的事件ID
            if (!TargetEventIds.Contains(eventId))
            {
                return null;
            }
            
            var message = eventRecord.FormatDescription() ?? string.Empty;
            var shutdownReason = ExtractShutdownReason(message);
            var processInfo = ExtractProcessInfo(message);

            // 使用映射服务获取关机类型和描述
            var shutdownType = _eventMappingService.GetShutdownType(eventId);
            var shutdownReasonDescription = _eventMappingService.GetShutdownReasonDescription(shutdownReason);
            var detailedDescription = _eventMappingService.GetDetailedEventDescription(eventId, message);

            return new SystemEvent
            {
                EventId = eventId,
                TimeGenerated = eventRecord.TimeCreated ?? DateTime.Now,
                Source = eventRecord.ProviderName ?? "Unknown",
                Message = message,
                Level = eventRecord.Level?.ToString() ?? "Information",
                RecordId = eventRecord.RecordId ?? 0,
                UserSid = eventRecord.UserId?.ToString(),
                ProcessInfo = processInfo,
                ShutdownReason = shutdownReason,
                ShutdownType = shutdownType,
                ShutdownReasonDescription = shutdownReasonDescription,
                DetailedDescription = detailedDescription
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
        // 不使用XPath，返回null表示使用无过滤的查询
        return null;
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
                // 如果xpath为null，使用无过滤的查询
                var query = string.IsNullOrEmpty(xpath) 
                    ? new EventLogQuery("System", PathType.LogName)
                    : new EventLogQuery("System", PathType.LogName, xpath);
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

    /// <summary>
    /// 从.evtx文件异步获取指定时间范围内的系统事件
    /// </summary>
    public async Task<IReadOnlyList<SystemEvent>> GetSystemEventsFromFileAsync(
        string evtxFilePath, 
        DateTime startDate, 
        DateTime endDate, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(evtxFilePath))
        {
            throw new DataValidationException("文件路径不能为空", ErrorMessages.GetUserMessage("INVALID_FILE_PATH"));
        }

        if (!System.IO.File.Exists(evtxFilePath))
        {
            throw new FileAccessException(evtxFilePath, 
                ErrorMessages.GetUserMessage("FILE_NOT_FOUND"),
                $"找不到文件: {evtxFilePath}");
        }
        
        // 验证日期范围
        var dateValidation = ErrorMessages.ValidateDateRange(startDate, endDate);
        if (dateValidation != null)
        {
            _logger.LogWarning("日期范围验证失败: {Errors}", string.Join(", ", dateValidation.ValidationErrors));
            throw dateValidation;
        }
        
        // 检查文件大小
        var fileInfo = new FileInfo(evtxFilePath);
        var sizeCheck = ErrorMessages.CheckFileSize(evtxFilePath, fileInfo.Length);
        if (sizeCheck != null && sizeCheck.Severity == ErrorSeverity.Error)
        {
            throw sizeCheck;
        }

        _logger.LogInformation("开始从.evtx文件读取事件: {FilePath}, 时间范围: {StartDate} - {EndDate}", 
            evtxFilePath, startDate, endDate);

        var events = new ConcurrentBag<SystemEvent>();
        
        try
        {
            return await _fileRetryPolicy.ExecuteWithProgressAsync(async (progress, ct) =>
            {
                // 构建XPath查询以筛选目标事件ID和时间范围
                var eventIdFilter = string.Join(" or ", TargetEventIds.Select(id => $"EventID={id}"));
                var timeCreatedFilter = $"TimeCreated[@SystemTime>='{startDate:yyyy-MM-ddTHH:mm:ss.000Z}' and @SystemTime<='{endDate:yyyy-MM-ddTHH:mm:ss.000Z}']";
                var query = $"*[System[({eventIdFilter}) and {timeCreatedFilter}]]";
                
                _logger.LogDebug("使用XPath查询: {Query}", query);
                
                progress?.Report(new OperationProgress
                {
                    CurrentStep = "初始化文件读取器",
                    PercentComplete = 0
                });

                return await Task.Run(() =>
                {
                    try
                    {
                        using var reader = new EventLogReader(evtxFilePath, PathType.FilePath);
                        
                        EventRecord? eventRecord;
                        var eventCount = 0;
                        var totalProcessed = 0;
                        
                        progress?.Report(new OperationProgress
                        {
                            CurrentStep = "读取事件记录",
                            PercentComplete = 5
                        });
                        
                        while ((eventRecord = reader.ReadEvent()) != null)
                        {
                            ct.ThrowIfCancellationRequested();
                            
                            using (eventRecord)
                            {
                                totalProcessed++;
                                
                                try
                                {
                                    // 手动过滤事件ID和时间范围
                                    if (!TargetEventIds.Contains(eventRecord.Id))
                                        continue;
                                        
                                    var eventTime = eventRecord.TimeCreated?.ToLocalTime();
                                    if (!eventTime.HasValue || eventTime < startDate || eventTime > endDate)
                                        continue;

                                    var systemEvent = _eventMappingService.MapEventRecord(eventRecord);
                                    if (systemEvent != null)
                                    {
                                        events.Add(systemEvent);
                                        eventCount++;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "处理事件记录时发生错误: EventID={EventId}, Time={Time}",
                                        eventRecord.Id, eventRecord.TimeCreated);
                                }
                                
                                // 每处理500个事件更新一次进度
                                if (totalProcessed % 500 == 0)
                                {
                                    progress?.Report(new OperationProgress
                                    {
                                        CurrentStep = $"已处理 {totalProcessed} 个记录，找到 {eventCount} 个目标事件",
                                        ItemsProcessed = totalProcessed,
                                        AdditionalInfo = $"文件: {Path.GetFileName(evtxFilePath)}"
                                    });
                                }
                            }
                        }
                        
                        progress?.Report(new OperationProgress
                        {
                            CurrentStep = "读取完成",
                            PercentComplete = 90,
                            ItemsProcessed = totalProcessed,
                            TotalItems = totalProcessed
                        });
                        
                        _logger.LogInformation("从.evtx文件读取完成，共处理 {TotalProcessed} 个记录，找到 {EventCount} 个目标事件", 
                            totalProcessed, eventCount);
                        
                        return events.OrderBy(e => e.TimeGenerated).ToList();
                    }
                    catch (EventLogException ex)
                    {
                        throw new FileFormatException(evtxFilePath, ".evtx", 
                            ErrorMessages.GetUserMessage("FILE_FORMAT_ERROR"), ex);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        throw new FileAccessException(evtxFilePath, 
                            ErrorMessages.GetUserMessage("FILE_PERMISSION_DENIED"),
                            ex.Message, ex);
                    }
                }, ct);
            }, 
            null, // 使用默认进度回调
            exception => exception is not (OperationCanceledException or ArgumentException or NotSupportedException),
            cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("从.evtx文件读取事件被取消: {FilePath}", evtxFilePath);
            throw new OperationCancelledException("读取.evtx文件", "操作被用户取消");
        }
        catch (TurnedOnTimesViewException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var appException = ErrorMessages.TranslateException(ex, evtxFilePath);
            _logger.LogError(ex, "从.evtx文件读取事件失败: {FilePath} - {Error}", 
                evtxFilePath, appException.UserMessage);
            throw appException;
        }

        var result = events.OrderBy(e => e.TimeGenerated).ToList();
        _logger.LogInformation("成功从.evtx文件获取 {EventCount} 个系统事件", result.Count);
        
        return result;
    }

    /// <summary>
    /// 验证.evtx文件是否可以正常读取
    /// </summary>
    public bool CanReadEvtxFile(string evtxFilePath)
    {
        if (string.IsNullOrWhiteSpace(evtxFilePath))
            return false;

        try
        {
            if (!System.IO.File.Exists(evtxFilePath))
                return false;

            // 尝试创建EventLogReader来测试文件是否可读
            using var reader = new EventLogReader(evtxFilePath, PathType.FilePath);
            
            // 尝试读取一个事件来验证文件格式
            using var testEvent = reader.ReadEvent();
            
            _logger.LogDebug("成功验证.evtx文件: {FilePath}", evtxFilePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "验证.evtx文件时发生错误: {FilePath}", evtxFilePath);
            return false;
        }
    }
    
    /// <inheritdoc />
    public async Task<FileValidationResult> ValidateEvtxFileAsync(string evtxFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(evtxFilePath))
            return FileValidationResult.Failure("文件路径不能为空");
            
        try
        {
            var fileInfo = new FileInfo(evtxFilePath);
            if (!fileInfo.Exists)
                return FileValidationResult.Failure($"文件不存在: {evtxFilePath}");
                
            // 检查文件扩展名
            if (!string.Equals(fileInfo.Extension, ".evtx", StringComparison.OrdinalIgnoreCase))
                return FileValidationResult.Failure($"不支持的文件格式: {fileInfo.Extension}，仅支持.evtx文件");
            
            _logger.LogDebug("开始详细验证.evtx文件: {FilePath}", evtxFilePath);
            
            return await Task.Run(() =>
            {
                try
                {
                    using var reader = new EventLogReader(evtxFilePath, PathType.FilePath);
                    
                    // 尝试读取第一个和最后一个事件来确定时间范围
                    DateTime? firstEventTime = null;
                    DateTime? lastEventTime = null;
                    long eventCount = 0;
                    
                    // 读取前100个事件来获取基本信息
                    const int sampleSize = 100;
                    var sampleCount = 0;
                    
                    while (sampleCount < sampleSize && reader.ReadEvent() is EventRecord eventRecord)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        using (eventRecord)
                        {
                            var eventTime = eventRecord.TimeCreated?.ToLocalTime();
                            if (eventTime.HasValue)
                            {
                                firstEventTime ??= eventTime;
                                lastEventTime = eventTime; // 持续更新最后时间
                            }
                            
                            // 只计算我们感兴趣的事件
                            if (TargetEventIds.Contains(eventRecord.Id))
                            {
                                eventCount++;
                            }
                            
                            sampleCount++;
                        }
                    }
                    
                    // 基于样本估算总事件数
                    var estimatedTotalEvents = sampleCount > 0 ? (eventCount * fileInfo.Length) / (sampleCount * 1000) : 0;
                    
                    DateTimeRange? timeRange = null;
                    if (firstEventTime.HasValue && lastEventTime.HasValue)
                    {
                        timeRange = new DateTimeRange(firstEventTime.Value, lastEventTime.Value);
                    }
                    
                    _logger.LogDebug("文件验证完成: {FilePath}, 估算事件数: {EventCount}, 时间范围: {TimeRange}", 
                        evtxFilePath, estimatedTotalEvents, timeRange);
                    
                    return FileValidationResult.Success(fileInfo, estimatedTotalEvents, timeRange);
                }
                catch (EventLogException ex)
                {
                    _logger.LogError(ex, "验证.evtx文件时发生事件日志错误: {FilePath}", evtxFilePath);
                    return FileValidationResult.Failure($"无效的.evtx文件格式: {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogError(ex, "验证.evtx文件时权限不足: {FilePath}", evtxFilePath);
                    return FileValidationResult.Failure($"访问文件权限不足: {ex.Message}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "验证.evtx文件时发生未知错误: {FilePath}", evtxFilePath);
                    return FileValidationResult.Failure($"验证文件时发生错误: {ex.Message}");
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("文件验证被取消: {FilePath}", evtxFilePath);
            return FileValidationResult.Failure("操作被取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文件验证预处理失败: {FilePath}", evtxFilePath);
            return FileValidationResult.Failure($"文件验证失败: {ex.Message}");
        }
    }
    
    /// <inheritdoc />
    public async Task<IReadOnlyList<SystemEvent>> GetSystemEventsAsync(
        DataSourceConfiguration dataSource,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        if (dataSource == null)
            throw new ArgumentNullException(nameof(dataSource));
            
        _logger.LogInformation("从数据源获取系统事件: {Type} - {DisplayName}, 时间范围: {StartDate} - {EndDate}",
            dataSource.Type, dataSource.DisplayName, startDate, endDate);
        
        return dataSource switch
        {
            LocalSystemDataSource => await GetSystemEventsAsync(startDate, endDate, cancellationToken),
            ExternalEvtxDataSource evtxSource => await GetSystemEventsFromFileAsync(
                evtxSource.FilePath, startDate, endDate, cancellationToken),
            _ => throw new NotSupportedException($"不支持的数据源类型: {dataSource.Type}")
        };
    }
    
    /// <inheritdoc />
    public async Task<long> GetEventCountAsync(
        DataSourceConfiguration dataSource,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        if (dataSource == null)
            throw new ArgumentNullException(nameof(dataSource));
            
        _logger.LogDebug("获取数据源事件计数: {Type} - {DisplayName}", dataSource.Type, dataSource.DisplayName);
        
        return dataSource switch
        {
            LocalSystemDataSource => await GetEventCountAsync(startDate, endDate),
            ExternalEvtxDataSource evtxSource => await GetEventCountFromFileAsync(
                evtxSource.FilePath, startDate, endDate, cancellationToken),
            _ => throw new NotSupportedException($"不支持的数据源类型: {dataSource.Type}")
        };
    }
    
    /// <summary>
    /// 从.evtx文件获取事件计数
    /// </summary>
    private async Task<long> GetEventCountFromFileAsync(
        string evtxFilePath, 
        DateTime startDate, 
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(evtxFilePath))
            return 0;
        
        // 检查缓存
        var cacheKey = $"file_count_{Path.GetFileName(evtxFilePath)}_{startDate:yyyyMMddHHmmss}_{endDate:yyyyMMddHHmmss}";
        if (_cache.TryGetValue(cacheKey, out long cachedCount))
        {
            return cachedCount;
        }
        
        var count = await Task.Run(() =>
        {
            try
            {
                using var reader = new EventLogReader(evtxFilePath, PathType.FilePath);
                
                long eventCount = 0;
                const int batchSize = 1000;
                
                while (reader.ReadEvent() is EventRecord eventRecord)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    using (eventRecord)
                    {
                        // 过滤事件ID和时间范围
                        if (!TargetEventIds.Contains(eventRecord.Id))
                            continue;
                            
                        var eventTime = eventRecord.TimeCreated?.ToLocalTime();
                        if (!eventTime.HasValue || eventTime < startDate || eventTime > endDate)
                            continue;
                        
                        eventCount++;
                        
                        // 定期检查取消请求
                        if (eventCount % batchSize == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                }
                
                return eventCount;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("文件事件计数被取消: {FilePath}", evtxFilePath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取文件事件计数时发生错误: {FilePath}", evtxFilePath);
                return 0L;
            }
        }, cancellationToken);
        
        // 缓存结果
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheExpirationMinutes),
            Size = 1
        };
        _cache.Set(cacheKey, count, cacheOptions);
        
        return count;
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