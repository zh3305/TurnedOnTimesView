using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;
using TurnedOnTimesView.Core;
using TurnedOnTimesView.Infrastructure.Configuration;
using TurnedOnTimesView.Infrastructure.Logging;
using TurnedOnTimesView.Models;
using TurnedOnTimesView.Services;

namespace TurnedOnTimesView.ViewModels;

/// <summary>
/// 主窗口的视图模型，实现MVVM模式的核心逻辑
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IEventLogService _eventLogService;
    private readonly ISessionAnalyzer _sessionAnalyzer;
    private readonly ILogger<MainViewModel> _logger;
    private readonly AppSettings _appSettings;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _loadingSemaphore;
    
    // 性能优化字段
    private readonly ConcurrentQueue<SessionRecord> _sessionQueue;
    private Timer? _progressUpdateTimer;
    private volatile bool _isLoadingCancelled;
    private const int MaxConcurrentSessions = 10000; // 最大会话记录数
    private const int ProgressUpdateIntervalMs = 100; // 进度更新间隔

    // 可观察属性
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "准备就绪";

    [ObservableProperty]
    private DateTime _startDate;

    [ObservableProperty]
    private DateTime _endDate;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _totalSessions;

    [ObservableProperty]
    private int _normalShutdowns;

    [ObservableProperty]
    private int _abnormalShutdowns;

    [ObservableProperty]
    private int _systemRestarts;

    [ObservableProperty]
    private int _sleepEvents;

    [ObservableProperty]
    private double _averageSessionTime;

    [ObservableProperty]
    private string _loadingProgress = string.Empty;

    // 集合和视图
    public ObservableCollection<SessionRecord> Sessions { get; }
    public ICollectionView SessionsView { get; }

    public MainViewModel(
        IEventLogService eventLogService,
        ISessionAnalyzer sessionAnalyzer,
        ILogger<MainViewModel> logger,
        IOptions<AppSettings> appSettings)
    {
        _eventLogService = eventLogService ?? throw new ArgumentNullException(nameof(eventLogService));
        _sessionAnalyzer = sessionAnalyzer ?? throw new ArgumentNullException(nameof(sessionAnalyzer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));

        _cancellationTokenSource = new CancellationTokenSource();
        _dispatcher = Dispatcher.CurrentDispatcher;
        _loadingSemaphore = new SemaphoreSlim(1, 1);
        _sessionQueue = new ConcurrentQueue<SessionRecord>();

        // 初始化集合
        Sessions = new ObservableCollection<SessionRecord>();
        SessionsView = CollectionViewSource.GetDefaultView(Sessions);
        
        // 配置过滤和排序
        SessionsView.Filter = FilterSessions;
        SessionsView.SortDescriptions.Add(new SortDescription(nameof(SessionRecord.LastEventTime), ListSortDirection.Descending));

        // 初始化日期范围
        InitializeDateRange();

        // 订阅属性变化事件
        PropertyChanged += OnPropertyChanged;

        _logger.LogInformation("MainViewModel 已初始化");
    }

    /// <summary>
    /// 初始化日期范围
    /// </summary>
    private void InitializeDateRange()
    {
        EndDate = DateTime.Now;
        StartDate = _appSettings.GetDefaultStartDate();
        
        _logger.LogDebug("初始化日期范围: {StartDate} - {EndDate}", StartDate, EndDate);
    }

    /// <summary>
    /// 属性变化处理
    /// </summary>
    private async void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SearchText):
                SessionsView.Refresh();
                break;

            case nameof(StartDate):
            case nameof(EndDate):
                if (EndDate < StartDate)
                {
                    StatusMessage = "结束日期不能早于开始日期";
                    return;
                }
                await LoadSessionsAsync();
                break;
        }
    }

    /// <summary>
    /// 会话过滤器
    /// </summary>
    private bool FilterSessions(object item)
    {
        if (item is not SessionRecord session)
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var searchTerm = SearchText.ToLowerInvariant();
        return session.ShutdownReason.ToLowerInvariant().Contains(searchTerm) ||
               session.Process.ToLowerInvariant().Contains(searchTerm) ||
               session.Type.ToString().ToLowerInvariant().Contains(searchTerm);
    }

    /// <summary>
    /// 加载会话数据命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoadSessions))]
    private async Task LoadSessionsAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        StatusMessage = "正在加载系统事件...";
        LoadingProgress = "0%";

        if (!await _loadingSemaphore.WaitAsync(100))
        {
            StatusMessage = "正在执行其他操作，请稍后再试";
            return;
        }

        try
        {
            using var _ = _logger.LogOperationTime("加载会话数据");
            _isLoadingCancelled = false;

            // 检查权限
            if (!_eventLogService.CanAccessEventLog())
            {
                StatusMessage = "无法访问事件日志，需要管理员权限";
                _logger.LogWarning("缺少事件日志访问权限");
                return;
            }

            // 启动进度更新定时器
            StartProgressTimer();

            // 异步获取事件总数（用于进度显示）
            var totalEventsTask = _eventLogService.GetEventCountAsync(StartDate, EndDate);
            var totalEvents = await totalEventsTask;
            
            if (_isLoadingCancelled) return;
            
            _logger.LogDebug("预计需要处理 {TotalEvents} 个事件", totalEvents);
            StatusMessage = $"正在读取事件日志... (预计 {totalEvents} 个事件)";

            // 异步获取系统事件
            var eventsTask = _eventLogService.GetSystemEventsAsync(
                StartDate, EndDate, _cancellationTokenSource.Token);
            var events = await eventsTask;

            if (_isLoadingCancelled) return;

            await UpdateUIAsync(() => 
            {
                LoadingProgress = "50%";
                StatusMessage = $"正在分析 {events.Count} 个事件...";
            });

            // 异步分析会话
            var sessionsTask = _sessionAnalyzer.AnalyzeSessionsAsync(
                events, _cancellationTokenSource.Token);
            var sessions = await sessionsTask;

            if (_isLoadingCancelled) return;

            await UpdateUIAsync(() => 
            {
                LoadingProgress = "75%";
                StatusMessage = "正在更新界面...";
            });

            // 异步更新UI（分批处理避免阻塞）
            await UpdateSessionsAsync(sessions);
            
            if (_isLoadingCancelled) return;

            // 更新统计信息
            UpdateStatistics(sessions);

            await UpdateUIAsync(() => 
            {
                LoadingProgress = "100%";
                StatusMessage = $"加载完成，共 {sessions.Count} 个会话记录";
            });

            _logger.LogInformation("成功加载 {SessionCount} 个会话记录", sessions.Count);
        }
        catch (OperationCanceledException)
        {
            _isLoadingCancelled = true;
            await UpdateUIAsync(() => StatusMessage = "操作已取消");
            _logger.LogInformation("会话加载操作被取消");
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusMessage = "权限不足，请以管理员身份运行";
            _logger.LogError(ex, "访问事件日志权限不足");
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
            _logger.LogError(ex, "加载会话数据时发生错误");
        }
        finally
        {
            StopProgressTimer();
            await UpdateUIAsync(() => 
            {
                IsLoading = false;
                LoadingProgress = string.Empty;
            });
            _loadingSemaphore.Release();
        }
    }

    private bool CanLoadSessions() => !IsLoading;

    /// <summary>
    /// 刷新数据命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        _logger.LogInformation("用户请求刷新数据");
        await LoadSessionsAsync();
    }

    private bool CanRefresh() => !IsLoading;

    /// <summary>
    /// 取消操作命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task Cancel()
    {
        _isLoadingCancelled = true;
        _cancellationTokenSource.Cancel();
        await UpdateUIAsync(() => StatusMessage = "正在取消操作...");
        _logger.LogInformation("用户取消了当前操作");
    }

    private bool CanCancel() => IsLoading;

    /// <summary>
    /// 清除搜索命令
    /// </summary>
    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        _logger.LogDebug("清除搜索条件");
    }

    /// <summary>
    /// 重置日期范围命令
    /// </summary>
    [RelayCommand]
    private void ResetDateRange()
    {
        InitializeDateRange();
        _logger.LogDebug("重置日期范围为默认值");
    }

    /// <summary>
    /// 导出数据命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        try
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出会话记录",
                Filter = "CSV文件 (*.csv)|*.csv|Excel文件 (*.xlsx)|*.xlsx|JSON文件 (*.json)|*.json",
                DefaultExt = "csv",
                FileName = $"系统会话记录_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (saveFileDialog.ShowDialog() != true)
                return;

            IsLoading = true;
            StatusMessage = "正在导出数据...";

            var filePath = saveFileDialog.FileName;
            var extension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();

            await Task.Run(() =>
            {
                switch (extension)
                {
                    case ".csv":
                        ExportToCsv(filePath);
                        break;
                    case ".json":
                        ExportToJson(filePath);
                        break;
                    case ".xlsx":
                        ExportToExcel(filePath);
                        break;
                    default:
                        throw new NotSupportedException($"不支持的文件格式: {extension}");
                }
            });

            StatusMessage = $"数据已成功导出至: {filePath}";
            _logger.LogInformation("数据已导出至文件: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出失败: {ex.Message}";
            _logger.LogError(ex, "导出数据时发生错误");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanExport() => !IsLoading && Sessions.Count > 0;

    /// <summary>
    /// 导出为CSV格式
    /// </summary>
    private void ExportToCsv(string filePath)
    {
        using var writer = new System.IO.StreamWriter(filePath, false, System.Text.Encoding.UTF8);
        
        // 写入标题行
        writer.WriteLine("启动时间,关机时间,持续时间,最后事件时间,关机类型,状态,关机原因,进程");
        
        // 写入数据行
        foreach (var session in Sessions)
        {
            writer.WriteLine($"\"{session.StartTime:yyyy-MM-dd HH:mm:ss}\"," +
                           $"\"{session.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""}\"," +
                           $"\"{session.FormattedDuration}\"," +
                           $"\"{session.LastEventTime:yyyy-MM-dd HH:mm:ss}\"," +
                           $"\"{GetShutdownTypeText(session.Type)}\"," +
                           $"\"{session.StatusText}\"," +
                           $"\"{session.ShutdownReason}\"," +
                           $"\"{session.Process}\"");
        }
    }

    /// <summary>
    /// 导出为JSON格式
    /// </summary>
    private void ExportToJson(string filePath)
    {
        var exportData = Sessions.Select(s => new
        {
            StartTime = s.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
            EndTime = s.EndTime?.ToString("yyyy-MM-dd HH:mm:ss"),
            Duration = s.FormattedDuration,
            LastEventTime = s.LastEventTime.ToString("yyyy-MM-dd HH:mm:ss"),
            Type = GetShutdownTypeText(s.Type),
            Status = s.StatusText,
            ShutdownReason = s.ShutdownReason,
            Process = s.Process
        }).ToList();

        var json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        System.IO.File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
    }

    /// <summary>
    /// 导出为Excel格式（简单实现）
    /// </summary>
    private void ExportToExcel(string filePath)
    {
        // 简单的XML格式Excel文件
        var xml = new System.Text.StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\"?>");
        xml.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\">");
        xml.AppendLine("<Worksheet ss:Name=\"系统会话记录\">");
        xml.AppendLine("<Table>");
        
        // 标题行
        xml.AppendLine("<Row>");
        xml.AppendLine("<Cell><Data ss:Type=\"String\">启动时间</Data></Cell>");
        xml.AppendLine("<Cell><Data ss:Type=\"String\">关机时间</Data></Cell>");
        xml.AppendLine("<Cell><Data ss:Type=\"String\">持续时间</Data></Cell>");
        xml.AppendLine("<Cell><Data ss:Type=\"String\">最后事件时间</Data></Cell>");
        xml.AppendLine("<Cell><Data ss:Type=\"String\">关机类型</Data></Cell>");
        xml.AppendLine("<Cell><Data ss:Type=\"String\">状态</Data></Cell>");
        xml.AppendLine("<Cell><Data ss:Type=\"String\">关机原因</Data></Cell>");
        xml.AppendLine("<Cell><Data ss:Type=\"String\">进程</Data></Cell>");
        xml.AppendLine("</Row>");
        
        // 数据行
        foreach (var session in Sessions)
        {
            xml.AppendLine("<Row>");
            xml.AppendLine($"<Cell><Data ss:Type=\"String\">{session.StartTime:yyyy-MM-dd HH:mm:ss}</Data></Cell>");
            xml.AppendLine($"<Cell><Data ss:Type=\"String\">{session.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""}</Data></Cell>");
            xml.AppendLine($"<Cell><Data ss:Type=\"String\">{session.FormattedDuration}</Data></Cell>");
            xml.AppendLine($"<Cell><Data ss:Type=\"String\">{session.LastEventTime:yyyy-MM-dd HH:mm:ss}</Data></Cell>");
            xml.AppendLine($"<Cell><Data ss:Type=\"String\">{GetShutdownTypeText(session.Type)}</Data></Cell>");
            xml.AppendLine($"<Cell><Data ss:Type=\"String\">{session.StatusText}</Data></Cell>");
            xml.AppendLine($"<Cell><Data ss:Type=\"String\">{System.Security.SecurityElement.Escape(session.ShutdownReason)}</Data></Cell>");
            xml.AppendLine($"<Cell><Data ss:Type=\"String\">{session.Process}</Data></Cell>");
            xml.AppendLine("</Row>");
        }
        
        xml.AppendLine("</Table>");
        xml.AppendLine("</Worksheet>");
        xml.AppendLine("</Workbook>");
        
        System.IO.File.WriteAllText(filePath, xml.ToString(), System.Text.Encoding.UTF8);
    }

    /// <summary>
    /// 获取关机类型的中文文本
    /// </summary>
    private static string GetShutdownTypeText(ShutdownType type)
    {
        return type switch
        {
            ShutdownType.Normal => "正常关机",
            ShutdownType.Abnormal => "异常关机",
            ShutdownType.Restart => "系统重启",
            ShutdownType.Sleep => "系统睡眠",
            ShutdownType.Unknown => "未知",
            _ => type.ToString()
        };
    }

    /// <summary>
    /// 更新统计信息
    /// </summary>
    private void UpdateStatistics(IReadOnlyList<SessionRecord> sessions)
    {
        TotalSessions = sessions.Count;
        NormalShutdowns = sessions.Count(s => s.Type == ShutdownType.Normal);
        AbnormalShutdowns = sessions.Count(s => s.Type == ShutdownType.Abnormal);
        SystemRestarts = sessions.Count(s => s.Type == ShutdownType.Restart);
        SleepEvents = sessions.Count(s => s.Type == ShutdownType.Sleep);

        // 计算平均会话时间（只计算已结束的会话）
        var completedSessions = sessions.Where(s => s.EndTime.HasValue).ToList();
        if (completedSessions.Count > 0)
        {
            AverageSessionTime = completedSessions.Average(s => s.Duration.TotalHours);
        }
        else
        {
            AverageSessionTime = 0;
        }

        _logger.LogDebug("统计信息已更新: 总计={TotalSessions}, 正常关机={NormalShutdowns}, 异常关机={AbnormalShutdowns}",
            TotalSessions, NormalShutdowns, AbnormalShutdowns);
    }

    /// <summary>
    /// 初始化数据（在窗口加载时调用）
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.LogInformation("开始初始化数据");
        await LoadSessionsAsync();
    }

    /// <summary>
    /// 异步更新UI（确保在UI线程执行）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task UpdateUIAsync(Action updateAction)
    {
        if (_dispatcher.CheckAccess())
        {
            updateAction();
        }
        else
        {
            await _dispatcher.InvokeAsync(updateAction, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// 异步更新会话集合（分批处理提高响应性）
    /// </summary>
    private async Task UpdateSessionsAsync(IReadOnlyList<SessionRecord> sessions)
    {
        await UpdateUIAsync(() => Sessions.Clear());
        
        const int batchSize = 50; // 分批大小
        var sessionsToAdd = sessions.Take(_appSettings.MaxRecordsToLoad).ToArray();
        
        for (int i = 0; i < sessionsToAdd.Length; i += batchSize)
        {
            if (_isLoadingCancelled) break;
            
            var batch = sessionsToAdd.Skip(i).Take(batchSize);
            await UpdateUIAsync(() => 
            {
                foreach (var session in batch)
                {
                    Sessions.Add(session);
                }
            });
            
            // 短暂延迟让UI有机会响应
            if (i + batchSize < sessionsToAdd.Length)
            {
                await Task.Delay(1, _cancellationTokenSource.Token);
            }
        }
    }

    /// <summary>
    /// 启动进度更新定时器
    /// </summary>
    private void StartProgressTimer()
    {
        _progressUpdateTimer?.Dispose();
        _progressUpdateTimer = new Timer(UpdateProgressCallback, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
    }

    /// <summary>
    /// 停止进度更新定时器
    /// </summary>
    private void StopProgressTimer()
    {
        _progressUpdateTimer?.Dispose();
        _progressUpdateTimer = null;
    }

    /// <summary>
    /// 进度更新回调
    /// </summary>
    private void UpdateProgressCallback(object? state)
    {
        if (!IsLoading || _isLoadingCancelled) return;
        
        // 可以在这里添加更详细的进度逻辑
        // 比如从事件服务获取实时进度信息
    }

    public void Dispose()
    {
        try
        {
            _isLoadingCancelled = true;
            _cancellationTokenSource?.Cancel();
            
            StopProgressTimer();
            _cancellationTokenSource?.Dispose();
            _loadingSemaphore?.Dispose();
            
            _logger.LogDebug("MainViewModel 已释放所有资源");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "释放MainViewModel资源时发生错误");
        }
    }
}