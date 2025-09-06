using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using TurnedOnTimesView.Core;
using TurnedOnTimesView.Infrastructure.Configuration;
using TurnedOnTimesView.Infrastructure.Logging;
using TurnedOnTimesView.Services;
using TurnedOnTimesView.ViewModels;

namespace TurnedOnTimesView.Infrastructure.DependencyInjection;

/// <summary>
/// 服务注册扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册应用程序核心服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置对象</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册配置
        services.Configure<AppSettings>(configuration.GetSection(AppSettings.SectionName));
        
        // 验证配置
        services.AddSingleton<IValidateOptions<AppSettings>, ValidateAppSettings>();

        // 注册内存缓存
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 100;
            options.CompactionPercentage = 0.25;
        });

        // 注册核心服务
        services.AddSingleton<EventMappingService>();
        services.AddScoped<IEventLogService, EventLogService>();
        services.AddScoped<ISessionAnalyzer, SessionAnalyzer>();
        services.AddSingleton<IDataSourceService, DataSourceService>();
        
        // 注册错误跟踪服务
        services.AddSingleton<IErrorTracker, InMemoryErrorTracker>();

        // 注册 ViewModels
        services.AddTransient<MainViewModel>();

        return services;
    }

    /// <summary>
    /// 注册日志服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置对象</param>
    /// <param name="isDevelopment">是否为开发环境</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddLoggingServices(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment = false)
    {
        services.AddSerilogLogging(configuration, isDevelopment);
        return services;
    }

    /// <summary>
    /// 注册所有应用服务的便捷方法
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置对象</param>
    /// <param name="isDevelopment">是否为开发环境</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddTurnedOnTimesViewServices(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment = false)
    {
        return services
            .AddLoggingServices(configuration, isDevelopment)
            .AddApplicationServices(configuration);
    }
}

/// <summary>
/// AppSettings 配置验证器
/// </summary>
internal sealed class ValidateAppSettings : IValidateOptions<AppSettings>
{
    public ValidateOptionsResult Validate(string? name, AppSettings options)
    {
        var validationResult = options.Validate();
        
        if (validationResult == ValidationResult.Success)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(validationResult.ErrorMessage ?? "配置验证失败");
    }
}

/// <summary>
/// 服务提供者扩展方法
/// </summary>
public static class ServiceProviderExtensions
{
    /// <summary>
    /// 验证所有注册的服务是否可以正常创建
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    /// <returns>验证结果</returns>
    public static ServiceValidationResult ValidateServices(this IServiceProvider serviceProvider)
    {
        var errors = new List<string>();

        // 测试核心服务
        try
        {
            var eventLogService = serviceProvider.GetRequiredService<IEventLogService>();
            if (!eventLogService.CanAccessEventLog())
            {
                errors.Add("无法访问Windows事件日志，可能需要管理员权限");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"EventLogService 创建失败: {ex.Message}");
        }

        try
        {
            serviceProvider.GetRequiredService<ISessionAnalyzer>();
        }
        catch (Exception ex)
        {
            errors.Add($"SessionAnalyzer 创建失败: {ex.Message}");
        }

        try
        {
            serviceProvider.GetRequiredService<IDataSourceService>();
        }
        catch (Exception ex)
        {
            errors.Add($"DataSourceService 创建失败: {ex.Message}");
        }

        try
        {
            serviceProvider.GetRequiredService<MainViewModel>();
        }
        catch (Exception ex)
        {
            errors.Add($"MainViewModel 创建失败: {ex.Message}");
        }

        // 验证配置
        try
        {
            var appSettings = serviceProvider.GetRequiredService<IOptions<AppSettings>>().Value;
            var configValidation = appSettings.Validate();
            if (configValidation != ValidationResult.Success)
            {
                errors.Add($"应用配置无效: {configValidation.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"AppSettings 验证失败: {ex.Message}");
        }

        return new ServiceValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.AsReadOnly()
        };
    }
}

/// <summary>
/// 服务验证结果
/// </summary>
public sealed record ServiceValidationResult
{
    /// <summary>
    /// 是否验证成功
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// 错误信息列表
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// 获取错误摘要
    /// </summary>
    public string GetErrorSummary() => string.Join(Environment.NewLine, Errors);
}