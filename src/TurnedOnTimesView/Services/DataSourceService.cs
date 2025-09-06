using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TurnedOnTimesView.Infrastructure.Configuration;
using TurnedOnTimesView.Infrastructure.Exceptions;
using TurnedOnTimesView.Infrastructure.Resilience;
using TurnedOnTimesView.Models;

namespace TurnedOnTimesView.Services;

/// <summary>
/// 数据源管理服务实现
/// </summary>
public sealed class DataSourceService : IDataSourceService, IDisposable
{
    private readonly ILogger<DataSourceService> _logger;
    private readonly IEventLogService _eventLogService;
    private readonly AppSettings _appSettings;
    private readonly string _userPreferencesPath;
    private readonly RetryPolicy _retryPolicy;
    
    private DataSourceConfiguration? _currentDataSource;
    private UserPreferences? _cachedPreferences;
    private readonly object _lock = new();
    
    /// <summary>
    /// 支持的数据源类型
    /// </summary>
    private static readonly IReadOnlyList<DataSourceType> SupportedTypes = new[]
    {
        DataSourceType.LocalSystem,
        DataSourceType.ExternalEvtxFile
    };
    
    public DataSourceService(
        ILogger<DataSourceService> logger,
        IEventLogService eventLogService,
        IOptions<AppSettings> appSettings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventLogService = eventLogService ?? throw new ArgumentNullException(nameof(eventLogService));
        _appSettings = appSettings.Value ?? throw new ArgumentNullException(nameof(appSettings));
        
        // 初始化重试策略
        _retryPolicy = RetryPolicies.FileOperations(_logger);
        
        try
        {
            // 用户偏好设置文件路径
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appDataPath, "TurnedOnTimesView");
            
            // 确保目录存在
            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
                _logger.LogDebug("创建应用数据目录: {AppFolder}", appFolder);
            }
            
            _userPreferencesPath = Path.Combine(appFolder, "user-preferences.json");
            
            _logger.LogDebug("数据源服务初始化完成，用户偏好文件路径: {PreferencesPath}", _userPreferencesPath);
        }
        catch (Exception ex)
        {
            var appException = ErrorMessages.TranslateException(ex, "初始化数据源服务");
            _logger.LogError(ex, "初始化数据源服务失败: {Error}", appException.UserMessage);
            throw appException;
        }
    }
    
    /// <inheritdoc />
    public DataSourceConfiguration? CurrentDataSource
    {
        get
        {
            lock (_lock)
            {
                return _currentDataSource;
            }
        }
    }
    
    /// <inheritdoc />
    public event EventHandler<DataSourceChangedEventArgs>? DataSourceChanged;
    
    /// <inheritdoc />
    public LocalSystemDataSource CreateLocalSystemDataSource()
    {
        _logger.LogDebug("创建本机系统日志数据源");
        
        return new LocalSystemDataSource
        {
            DisplayName = "本机系统日志",
            Description = "从本机Windows事件日志中读取系统事件",
            RequireAdminCheck = true
        };
    }
    
    /// <inheritdoc />
    public async Task<ExternalEvtxDataSource> CreateExternalEvtxDataSourceAsync(
        string filePath, 
        string? displayName = null, 
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new DataValidationException("文件路径不能为空", ErrorMessages.GetUserMessage("INVALID_FILE_PATH"));
        }
        
        _logger.LogDebug("创建外部.evtx文件数据源: {FilePath}", filePath);
        
        try
        {
            return await _retryPolicy.ExecuteAsync(async _ =>
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    throw new FileAccessException(filePath, 
                        ErrorMessages.GetUserMessage("FILE_NOT_FOUND"), 
                        $"文件不存在: {filePath}");
                }
                
                // 检查文件扩展名
                if (!string.Equals(fileInfo.Extension, ".evtx", StringComparison.OrdinalIgnoreCase))
                {
                    throw new FileFormatException(filePath, ".evtx", 
                        ErrorMessages.GetUserMessage("UNSUPPORTED_FILE_FORMAT"));
                }
                
                var finalDisplayName = displayName ?? Path.GetFileNameWithoutExtension(filePath);
                var finalDescription = description ?? $"外部.evtx文件: {Path.GetFileName(filePath)}";
                
                // 在后台获取文件详细信息
                var (fileSize, lastModified, isReadOnly) = await Task.Run(() =>
                {
                    try
                    {
                        fileInfo.Refresh(); // 刷新文件信息
                        return (fileInfo.Length, fileInfo.LastWriteTime, fileInfo.IsReadOnly);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "获取文件详细信息失败: {FilePath}", filePath);
                        return (0L, DateTime.MinValue, false);
                    }
                });
                
                // 检查文件大小
                var sizeCheck = ErrorMessages.CheckFileSize(filePath, fileSize);
                if (sizeCheck != null && sizeCheck.Severity == ErrorSeverity.Error)
                {
                    throw sizeCheck;
                }
                
                var dataSource = new ExternalEvtxDataSource
                {
                    DisplayName = finalDisplayName,
                    Description = finalDescription,
                    FilePath = filePath,
                    FileSize = fileSize,
                    LastModified = lastModified,
                    IsReadOnly = isReadOnly
                };
                
                _logger.LogInformation("成功创建外部.evtx文件数据源: {DisplayName}, 大小: {FileSize:N0} 字节", 
                    finalDisplayName, fileSize);
                    
                return dataSource;
            });
        }
        catch (TurnedOnTimesViewException)
        {
            throw; // 重新抛出应用程序异常
        }
        catch (Exception ex)
        {
            var appException = ErrorMessages.TranslateException(ex, filePath);
            _logger.LogError(ex, "创建外部.evtx文件数据源失败: {FilePath} - {Error}", filePath, appException.UserMessage);
            throw appException;
        }
    }
    
    /// <inheritdoc />
    public async Task<bool> SetCurrentDataSourceAsync(DataSourceConfiguration dataSource, CancellationToken cancellationToken = default)
    {
        if (dataSource == null)
        {
            throw new DataValidationException("数据源配置不能为空", ErrorMessages.GetUserMessage("DATA_VALIDATION_ERROR"));
        }
        
        _logger.LogInformation("切换数据源: {Type} - {DisplayName}", dataSource.Type, dataSource.DisplayName);
        
        try
        {
            // 验证数据源
            var validationResult = await ValidateDataSourceAsync(dataSource, cancellationToken);
            if (!validationResult.IsValid)
            {
                var errorMsg = $"数据源验证失败: {validationResult.ErrorMessage}";
                _logger.LogError(errorMsg);
                throw new DataSourceException("DATA_SOURCE_VALIDATION_FAILED", 
                    validationResult.ErrorMessage, errorMsg);
            }
            
            if (validationResult.Warnings.Count > 0)
            {
                _logger.LogWarning("数据源存在警告: {Warnings}", string.Join(", ", validationResult.Warnings));
            }
            
            DataSourceConfiguration? oldDataSource;
            lock (_lock)
            {
                oldDataSource = _currentDataSource;
                _currentDataSource = dataSource;
            }
            
            // 触发数据源变更事件
            try
            {
                DataSourceChanged?.Invoke(this, new DataSourceChangedEventArgs(oldDataSource, dataSource));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发数据源变更事件时发生错误");
                // 不抛出异常，但记录错误
            }
            
            // 如果是外部文件，添加到最近使用的文件列表
            if (dataSource is ExternalEvtxDataSource evtxDataSource)
            {
                try
                {
                    await AddRecentFileAsync(evtxDataSource.FilePath, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "添加最近使用文件失败: {FilePath}", evtxDataSource.FilePath);
                    // 不抛出异常，这不是致命错误
                }
            }
            
            _logger.LogInformation("数据源切换成功: {DisplayName}", dataSource.DisplayName);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("设置数据源操作被取消");
            throw new OperationCancelledException("设置数据源", "操作被用户取消");
        }
        catch (TurnedOnTimesViewException)
        {
            throw; // 重新抛出应用程序异常
        }
        catch (Exception ex)
        {
            var appException = ErrorMessages.TranslateException(ex, "设置数据源");
            _logger.LogError(ex, "设置数据源失败: {Error}", appException.UserMessage);
            throw appException;
        }
    }
    
    /// <inheritdoc />
    public async Task<DataSourceValidationResult> ValidateDataSourceAsync(DataSourceConfiguration dataSource, CancellationToken cancellationToken = default)
    {
        if (dataSource == null)
            throw new ArgumentNullException(nameof(dataSource));
        
        _logger.LogDebug("验证数据源: {Type} - {DisplayName}", dataSource.Type, dataSource.DisplayName);
        
        // 基础验证
        var basicValidation = dataSource.Validate();
        if (basicValidation != System.ComponentModel.DataAnnotations.ValidationResult.Success)
        {
            return DataSourceValidationResult.Failure(basicValidation.ErrorMessage ?? "数据源配置无效");
        }
        
        var warnings = new List<string>();
        DataSourcePerformanceInfo? performanceInfo = null;
        
        try
        {
            switch (dataSource)
            {
                case LocalSystemDataSource localSource:
                    performanceInfo = await ValidateLocalSystemDataSourceAsync(localSource, warnings, cancellationToken);
                    break;
                    
                case ExternalEvtxDataSource evtxSource:
                    performanceInfo = await ValidateExternalEvtxDataSourceAsync(evtxSource, warnings, cancellationToken);
                    break;
                    
                default:
                    return DataSourceValidationResult.Failure($"不支持的数据源类型: {dataSource.Type}");
            }
            
            return warnings.Count > 0
                ? DataSourceValidationResult.WithWarnings(warnings, performanceInfo)
                : DataSourceValidationResult.Success(performanceInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证数据源时发生错误: {DataSource}", dataSource);
            return DataSourceValidationResult.Failure($"验证数据源时发生错误: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 验证本机系统日志数据源
    /// </summary>
    private async Task<DataSourcePerformanceInfo> ValidateLocalSystemDataSourceAsync(
        LocalSystemDataSource dataSource, 
        List<string> warnings, 
        CancellationToken cancellationToken)
    {
        // 检查权限
        if (dataSource.RequireAdminCheck && !_eventLogService.CanAccessEventLog())
        {
            warnings.Add("无法访问Windows事件日志，可能需要管理员权限");
        }
        
        // 估算性能信息
        var testStartDate = DateTime.Now.AddDays(-7);
        var testEndDate = DateTime.Now;
        
        var estimatedCount = await _eventLogService.GetEventCountAsync(testStartDate, testEndDate);
        
        return new DataSourcePerformanceInfo
        {
            EstimatedAccessTimeMs = 500, // 本机访问相对较快
            EstimatedEventCount = estimatedCount * 4, // 按周估算月度数据
            SupportedTimeRange = new DateTimeRange(DateTime.Now.AddYears(-1), DateTime.Now),
            DataSizeBytes = -1, // 本机日志无法准确估算大小
            SupportsParallelAccess = true,
            RecommendedBatchSize = 1000
        };
    }
    
    /// <summary>
    /// 验证外部.evtx文件数据源
    /// </summary>
    private async Task<DataSourcePerformanceInfo> ValidateExternalEvtxDataSourceAsync(
        ExternalEvtxDataSource dataSource, 
        List<string> warnings, 
        CancellationToken cancellationToken)
    {
        // 验证文件
        var fileValidation = await _eventLogService.ValidateEvtxFileAsync(dataSource.FilePath, cancellationToken);
        if (!fileValidation.IsValid)
        {
            throw new InvalidOperationException(fileValidation.ErrorMessage);
        }
        
        // 检查文件大小和性能影响
        if (dataSource.FileSize > 100 * 1024 * 1024) // 100MB
        {
            warnings.Add($"文件较大 ({dataSource.FileSize / (1024 * 1024):F1}MB)，读取可能较慢");
        }
        
        if (dataSource.IsReadOnly)
        {
            warnings.Add("文件为只读，无法修改");
        }
        
        // 估算访问时间（基于文件大小）
        var estimatedAccessTimeMs = Math.Max(1000, dataSource.FileSize / (1024 * 1024) * 100);
        
        return new DataSourcePerformanceInfo
        {
            EstimatedAccessTimeMs = estimatedAccessTimeMs,
            EstimatedEventCount = fileValidation.EstimatedEventCount,
            SupportedTimeRange = fileValidation.SupportedTimeRange,
            DataSizeBytes = dataSource.FileSize,
            SupportsParallelAccess = false, // 文件访问通常不支持并行
            RecommendedBatchSize = dataSource.FileSize > 50 * 1024 * 1024 ? 500 : 1000
        };
    }
    
    /// <inheritdoc />
    public IReadOnlyList<DataSourceType> GetSupportedDataSourceTypes()
    {
        return SupportedTypes;
    }
    
    /// <inheritdoc />
    public async Task<UserPreferences> GetUserPreferencesAsync()
    {
        if (_cachedPreferences != null)
            return _cachedPreferences;
        
        try
        {
            return await _retryPolicy.ExecuteAsync(async _ =>
            {
                if (!File.Exists(_userPreferencesPath))
                {
                    _logger.LogDebug("用户偏好文件不存在，使用默认设置");
                    _cachedPreferences = new UserPreferences();
                    return _cachedPreferences;
                }
                
                var json = await File.ReadAllTextAsync(_userPreferencesPath);
                
                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.LogWarning("用户偏好文件为空，使用默认设置");
                    _cachedPreferences = new UserPreferences();
                    return _cachedPreferences;
                }
                
                try
                {
                    var preferences = JsonSerializer.Deserialize<UserPreferences>(json) ?? new UserPreferences();
                    _cachedPreferences = preferences;
                    _logger.LogDebug("成功加载用户偏好设置");
                    return preferences;
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "用户偏好文件格式错误，使用默认设置");
                    
                    // 备份损坏的配置文件
                    var backupPath = _userPreferencesPath + $".backup.{DateTime.Now:yyyyMMdd_HHmmss}";
                    try
                    {
                        File.Copy(_userPreferencesPath, backupPath);
                        _logger.LogInformation("已备份损坏的配置文件至: {BackupPath}", backupPath);
                    }
                    catch (Exception backupEx)
                    {
                        _logger.LogWarning(backupEx, "备份配置文件失败");
                    }
                    
                    _cachedPreferences = new UserPreferences();
                    return _cachedPreferences;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载用户偏好设置失败，使用默认设置");
            _cachedPreferences = new UserPreferences();
            return _cachedPreferences;
        }
    }
    
    /// <inheritdoc />
    public async Task SaveUserPreferencesAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        if (preferences == null)
        {
            throw new DataValidationException("用户偏好设置不能为空", ErrorMessages.GetUserMessage("DATA_VALIDATION_ERROR"));
        }
        
        try
        {
            await _retryPolicy.ExecuteAsync(async ct =>
            {
                var json = JsonSerializer.Serialize(preferences, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                
                // 先写入临时文件，然后移动到目标文件（原子操作）
                var tempPath = _userPreferencesPath + ".tmp";
                
                await File.WriteAllTextAsync(tempPath, json, ct);
                
                // 如果原文件存在，先备份
                if (File.Exists(_userPreferencesPath))
                {
                    var backupPath = _userPreferencesPath + ".bak";
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                    File.Move(_userPreferencesPath, backupPath);
                }
                
                // 移动临时文件到目标位置
                File.Move(tempPath, _userPreferencesPath);
                
                _cachedPreferences = preferences;
                
                _logger.LogDebug("用户偏好设置保存成功");
                
                return true;
            }, 
            exception => exception is not OperationCanceledException, // 不重试取消操作
            cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("保存用户偏好设置被取消");
            throw new OperationCancelledException("保存用户偏好设置", "操作被用户取消");
        }
        catch (TurnedOnTimesViewException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var appException = ErrorMessages.TranslateException(ex, "保存用户偏好设置");
            _logger.LogError(ex, "保存用户偏好设置失败: {Error}", appException.UserMessage);
            throw appException;
        }
    }
    
    /// <inheritdoc />
    public async Task AddRecentFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;
        
        try
        {
            var preferences = await GetUserPreferencesAsync();
            var recentFiles = preferences.RecentEvtxFiles.ToList();
            
            // 移除已存在的路径
            recentFiles.Remove(filePath);
            
            // 添加到列表开头
            recentFiles.Insert(0, filePath);
            
            // 限制最大数量
            if (recentFiles.Count > preferences.MaxRecentFiles)
            {
                recentFiles = recentFiles.Take(preferences.MaxRecentFiles).ToList();
            }
            
            var updatedPreferences = preferences with { RecentEvtxFiles = recentFiles };
            await SaveUserPreferencesAsync(updatedPreferences, cancellationToken);
            
            _logger.LogDebug("添加最近使用文件: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "添加最近使用文件失败: {FilePath}", filePath);
            throw;
        }
    }
    
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRecentFilesAsync()
    {
        try
        {
            var preferences = await GetUserPreferencesAsync();
            
            // 过滤掉不存在的文件
            var existingFiles = preferences.RecentEvtxFiles
                .Where(File.Exists)
                .ToList();
            
            // 如果过滤后的列表与原列表不同，更新偏好设置
            if (existingFiles.Count != preferences.RecentEvtxFiles.Count)
            {
                var updatedPreferences = preferences with { RecentEvtxFiles = existingFiles };
                await SaveUserPreferencesAsync(updatedPreferences);
                _logger.LogDebug("清理了 {Count} 个不存在的最近文件", 
                    preferences.RecentEvtxFiles.Count - existingFiles.Count);
            }
            
            return existingFiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取最近使用文件列表失败");
            return [];
        }
    }
    
    public void Dispose()
    {
        try
        {
            lock (_lock)
            {
                _currentDataSource = null;
                _cachedPreferences = null;
            }
            
            _logger.LogDebug("数据源服务已释放资源");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "释放数据源服务资源时发生错误");
        }
    }
}