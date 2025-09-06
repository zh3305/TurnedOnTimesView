using System;
using System.ComponentModel.DataAnnotations;
using System.IO;

namespace TurnedOnTimesView.Models;

/// <summary>
/// 数据源类型枚举
/// </summary>
public enum DataSourceType
{
    /// <summary>
    /// 本机系统日志
    /// </summary>
    LocalSystem = 0,
    
    /// <summary>
    /// 外部.evtx文件
    /// </summary>
    ExternalEvtxFile = 1
}

/// <summary>
/// 数据源配置基类
/// </summary>
public abstract record DataSourceConfiguration
{
    /// <summary>
    /// 数据源类型
    /// </summary>
    public abstract DataSourceType Type { get; }
    
    /// <summary>
    /// 数据源显示名称
    /// </summary>
    [Required]
    public required string DisplayName { get; init; }
    
    /// <summary>
    /// 数据源描述
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// 验证配置是否有效
    /// </summary>
    public abstract ValidationResult Validate();
}

/// <summary>
/// 本机系统日志数据源配置
/// </summary>
public sealed record LocalSystemDataSource : DataSourceConfiguration
{
    public override DataSourceType Type => DataSourceType.LocalSystem;
    
    /// <summary>
    /// 是否需要管理员权限检查
    /// </summary>
    public bool RequireAdminCheck { get; init; } = true;
    
    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
            return new ValidationResult("显示名称不能为空");
            
        return ValidationResult.Success!;
    }
}

/// <summary>
/// 外部.evtx文件数据源配置
/// </summary>
public sealed record ExternalEvtxDataSource : DataSourceConfiguration
{
    public override DataSourceType Type => DataSourceType.ExternalEvtxFile;
    
    /// <summary>
    /// .evtx文件路径
    /// </summary>
    [Required]
    public required string FilePath { get; init; }
    
    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long FileSize { get; init; }
    
    /// <summary>
    /// 文件最后修改时间
    /// </summary>
    public DateTime LastModified { get; init; }
    
    /// <summary>
    /// 是否为只读文件
    /// </summary>
    public bool IsReadOnly { get; init; }
    
    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
            return new ValidationResult("显示名称不能为空");
            
        if (string.IsNullOrWhiteSpace(FilePath))
            return new ValidationResult("文件路径不能为空");
            
        if (!File.Exists(FilePath))
            return new ValidationResult($"文件不存在: {FilePath}");
            
        var extension = Path.GetExtension(FilePath);
        if (!string.Equals(extension, ".evtx", StringComparison.OrdinalIgnoreCase))
            return new ValidationResult($"不支持的文件格式: {extension}，仅支持.evtx文件");
            
        return ValidationResult.Success!;
    }
}

/// <summary>
/// 文件验证结果
/// </summary>
public sealed record FileValidationResult
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
    /// 文件基本信息
    /// </summary>
    public FileInfo? FileInfo { get; init; }
    
    /// <summary>
    /// 估算的事件数量
    /// </summary>
    public long EstimatedEventCount { get; init; }
    
    /// <summary>
    /// 支持的事件时间范围
    /// </summary>
    public DateTimeRange? SupportedTimeRange { get; init; }
    
    /// <summary>
    /// 创建成功的验证结果
    /// </summary>
    public static FileValidationResult Success(FileInfo fileInfo, long eventCount = 0, DateTimeRange? timeRange = null)
        => new() { IsValid = true, FileInfo = fileInfo, EstimatedEventCount = eventCount, SupportedTimeRange = timeRange };
    
    /// <summary>
    /// 创建失败的验证结果
    /// </summary>
    public static FileValidationResult Failure(string errorMessage)
        => new() { IsValid = false, ErrorMessage = errorMessage };
}

/// <summary>
/// 时间范围记录
/// </summary>
public sealed record DateTimeRange(DateTime Start, DateTime End)
{
    /// <summary>
    /// 时间跨度
    /// </summary>
    public TimeSpan Duration => End - Start;
    
    /// <summary>
    /// 检查指定时间是否在范围内
    /// </summary>
    public bool Contains(DateTime dateTime) => dateTime >= Start && dateTime <= End;
    
    /// <summary>
    /// 与另一个时间范围的交集
    /// </summary>
    public DateTimeRange? Intersect(DateTimeRange other)
    {
        var start = Start > other.Start ? Start : other.Start;
        var end = End < other.End ? End : other.End;
        
        return start <= end ? new DateTimeRange(start, end) : null;
    }
}

/// <summary>
/// 用户偏好设置
/// </summary>
public sealed record UserPreferences
{
    /// <summary>
    /// 默认数据源类型
    /// </summary>
    public DataSourceType DefaultDataSourceType { get; init; } = DataSourceType.LocalSystem;
    
    /// <summary>
    /// 最近使用的.evtx文件路径列表
    /// </summary>
    public IReadOnlyList<string> RecentEvtxFiles { get; init; } = [];
    
    /// <summary>
    /// 最大保存的最近文件数量
    /// </summary>
    public int MaxRecentFiles { get; init; } = 10;
    
    /// <summary>
    /// 是否启用自动缓存
    /// </summary>
    public bool EnableAutoCache { get; init; } = true;
    
    /// <summary>
    /// 缓存过期时间（分钟）
    /// </summary>
    public int CacheExpirationMinutes { get; init; } = 15;
    
    /// <summary>
    /// 默认查询时间范围（天数）
    /// </summary>
    public int DefaultQueryDays { get; init; } = 30;
}