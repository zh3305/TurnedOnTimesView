using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TurnedOnTimesView.Infrastructure.Configuration;

/// <summary>
/// 应用程序配置选项
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// 配置节点名称
    /// </summary>
    public const string SectionName = "App";

    /// <summary>
    /// 默认查询天数
    /// </summary>
    [Range(1, 365, ErrorMessage = "默认查询天数必须在1-365之间")]
    public int DefaultDateRange { get; init; } = 150;

    /// <summary>
    /// 最大记录加载数量
    /// </summary>
    [Range(100, 100000, ErrorMessage = "最大记录数必须在100-100000之间")]
    public int MaxRecordsToLoad { get; init; } = 10000;

    /// <summary>
    /// 刷新间隔（分钟）
    /// </summary>
    [Range(1, 60, ErrorMessage = "刷新间隔必须在1-60分钟之间")]
    public int RefreshIntervalMinutes { get; init; } = 5;

    /// <summary>
    /// 支持的导出格式
    /// </summary>
    public IReadOnlyList<string> ExportFormats { get; init; } = new[] { "CSV", "Excel", "HTML" };

    /// <summary>
    /// 是否启用自动刷新
    /// </summary>
    public bool EnableAutoRefresh { get; init; } = true;

    /// <summary>
    /// 是否在启动时显示权限警告
    /// </summary>
    public bool ShowPermissionWarning { get; init; } = true;

    /// <summary>
    /// 数据缓存时间（分钟）
    /// </summary>
    [Range(0, 30, ErrorMessage = "缓存时间必须在0-30分钟之间")]
    public int CacheTimeoutMinutes { get; init; } = 5;

    /// <summary>
    /// 验证配置的有效性
    /// </summary>
    /// <returns>验证结果</returns>
    public ValidationResult Validate()
    {
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(this);

        if (!Validator.TryValidateObject(this, validationContext, validationResults, true))
        {
            var errors = string.Join(", ", validationResults.Select(r => r.ErrorMessage));
            return new ValidationResult($"配置验证失败: {errors}");
        }

        // 自定义验证逻辑
        if (ExportFormats.Count == 0)
        {
            return new ValidationResult("至少需要配置一种导出格式");
        }

        var validFormats = new[] { "CSV", "Excel", "HTML" };
        if (ExportFormats.Any(format => !validFormats.Contains(format, StringComparer.OrdinalIgnoreCase)))
        {
            return new ValidationResult($"无效的导出格式，支持的格式: {string.Join(", ", validFormats)}");
        }

        return ValidationResult.Success!;
    }

    /// <summary>
    /// 获取刷新间隔的 TimeSpan
    /// </summary>
    public TimeSpan RefreshInterval => TimeSpan.FromMinutes(RefreshIntervalMinutes);

    /// <summary>
    /// 获取缓存超时的 TimeSpan
    /// </summary>
    public TimeSpan CacheTimeout => TimeSpan.FromMinutes(CacheTimeoutMinutes);

    /// <summary>
    /// 获取默认查询的开始日期
    /// </summary>
    public DateTime GetDefaultStartDate() => DateTime.Now.AddDays(-DefaultDateRange);

    public override string ToString()
    {
        return $"AppSettings {{ DefaultDateRange={DefaultDateRange}, MaxRecords={MaxRecordsToLoad}, RefreshInterval={RefreshIntervalMinutes}min }}";
    }
}