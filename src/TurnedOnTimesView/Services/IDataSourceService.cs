using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TurnedOnTimesView.Models;

namespace TurnedOnTimesView.Services;

/// <summary>
/// 数据源管理服务接口
/// </summary>
public interface IDataSourceService
{
    /// <summary>
    /// 当前活动的数据源
    /// </summary>
    DataSourceConfiguration? CurrentDataSource { get; }
    
    /// <summary>
    /// 数据源变更事件
    /// </summary>
    event EventHandler<DataSourceChangedEventArgs>? DataSourceChanged;
    
    /// <summary>
    /// 创建本机系统日志数据源配置
    /// </summary>
    /// <returns>本机系统数据源配置</returns>
    LocalSystemDataSource CreateLocalSystemDataSource();
    
    /// <summary>
    /// 创建外部.evtx文件数据源配置
    /// </summary>
    /// <param name="filePath">.evtx文件路径</param>
    /// <param name="displayName">显示名称（可选）</param>
    /// <param name="description">描述（可选）</param>
    /// <returns>外部文件数据源配置</returns>
    Task<ExternalEvtxDataSource> CreateExternalEvtxDataSourceAsync(
        string filePath, 
        string? displayName = null, 
        string? description = null);
    
    /// <summary>
    /// 设置当前数据源
    /// </summary>
    /// <param name="dataSource">数据源配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设置是否成功</returns>
    Task<bool> SetCurrentDataSourceAsync(DataSourceConfiguration dataSource, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 验证数据源配置
    /// </summary>
    /// <param name="dataSource">数据源配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验证结果</returns>
    Task<DataSourceValidationResult> ValidateDataSourceAsync(DataSourceConfiguration dataSource, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取支持的数据源类型列表
    /// </summary>
    /// <returns>支持的数据源类型</returns>
    IReadOnlyList<DataSourceType> GetSupportedDataSourceTypes();
    
    /// <summary>
    /// 获取用户偏好设置
    /// </summary>
    /// <returns>用户偏好设置</returns>
    Task<UserPreferences> GetUserPreferencesAsync();
    
    /// <summary>
    /// 保存用户偏好设置
    /// </summary>
    /// <param name="preferences">用户偏好设置</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SaveUserPreferencesAsync(UserPreferences preferences, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 添加最近使用的文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task AddRecentFileAsync(string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取最近使用的文件列表
    /// </summary>
    /// <returns>最近使用的文件路径列表</returns>
    Task<IReadOnlyList<string>> GetRecentFilesAsync();
}

/// <summary>
/// 数据源变更事件参数
/// </summary>
public sealed class DataSourceChangedEventArgs : EventArgs
{
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="oldDataSource">旧数据源</param>
    /// <param name="newDataSource">新数据源</param>
    public DataSourceChangedEventArgs(DataSourceConfiguration? oldDataSource, DataSourceConfiguration? newDataSource)
    {
        OldDataSource = oldDataSource;
        NewDataSource = newDataSource;
    }
    
    /// <summary>
    /// 旧数据源
    /// </summary>
    public DataSourceConfiguration? OldDataSource { get; }
    
    /// <summary>
    /// 新数据源
    /// </summary>
    public DataSourceConfiguration? NewDataSource { get; }
}

/// <summary>
/// 数据源验证结果
/// </summary>
public sealed record DataSourceValidationResult
{
    /// <summary>
    /// 是否验证成功
    /// </summary>
    public bool IsValid { get; init; }
    
    /// <summary>
    /// 错误消息
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;
    
    /// <summary>
    /// 警告消息列表
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
    
    /// <summary>
    /// 数据源性能信息
    /// </summary>
    public DataSourcePerformanceInfo? PerformanceInfo { get; init; }
    
    /// <summary>
    /// 创建成功的验证结果
    /// </summary>
    public static DataSourceValidationResult Success(DataSourcePerformanceInfo? performanceInfo = null)
        => new() { IsValid = true, PerformanceInfo = performanceInfo };
    
    /// <summary>
    /// 创建带警告的验证结果
    /// </summary>
    public static DataSourceValidationResult WithWarnings(IReadOnlyList<string> warnings, DataSourcePerformanceInfo? performanceInfo = null)
        => new() { IsValid = true, Warnings = warnings, PerformanceInfo = performanceInfo };
    
    /// <summary>
    /// 创建失败的验证结果
    /// </summary>
    public static DataSourceValidationResult Failure(string errorMessage)
        => new() { IsValid = false, ErrorMessage = errorMessage };
}

/// <summary>
/// 数据源性能信息
/// </summary>
public sealed record DataSourcePerformanceInfo
{
    /// <summary>
    /// 估算的访问时间（毫秒）
    /// </summary>
    public long EstimatedAccessTimeMs { get; init; }
    
    /// <summary>
    /// 估算的事件数量
    /// </summary>
    public long EstimatedEventCount { get; init; }
    
    /// <summary>
    /// 支持的时间范围
    /// </summary>
    public DateTimeRange? SupportedTimeRange { get; init; }
    
    /// <summary>
    /// 数据源大小（字节）
    /// </summary>
    public long DataSizeBytes { get; init; }
    
    /// <summary>
    /// 是否支持并行访问
    /// </summary>
    public bool SupportsParallelAccess { get; init; }
    
    /// <summary>
    /// 推荐的批处理大小
    /// </summary>
    public int RecommendedBatchSize { get; init; } = 1000;
}