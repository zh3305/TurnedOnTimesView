using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System;
using System.IO;

namespace TurnedOnTimesView.Infrastructure.Logging;

/// <summary>
/// 日志配置扩展方法
/// </summary>
public static class LoggingExtensions
{
    /// <summary>
    /// 配置 Serilog 日志系统
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置对象</param>
    /// <param name="isDevelopment">是否为开发环境</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddSerilogLogging(
        this IServiceCollection services, 
        IConfiguration configuration,
        bool isDevelopment = false)
    {
        // 确保日志目录存在
        var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logDirectory);

        // 配置 Serilog
        var loggerConfiguration = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.WithProperty("Application", "TurnedOnTimesView")
            .Enrich.WithProperty("Version", typeof(LoggingExtensions).Assembly.GetName().Version?.ToString() ?? "1.0.0")
            .Enrich.WithProperty("MachineName", Environment.MachineName)
            .Enrich.WithProperty("UserName", Environment.UserName);

        // 开发环境额外配置
        if (isDevelopment)
        {
            loggerConfiguration
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .MinimumLevel.Override("System", LogEventLevel.Warning);
        }

        // 创建日志器
        var logger = loggerConfiguration.CreateLogger();

        // 设置全局日志器
        Log.Logger = logger;

        // 注册到服务容器
        services.AddSingleton<Serilog.ILogger>(logger);
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(logger, dispose: true);
        });

        return services;
    }

    /// <summary>
    /// 配置主机使用 Serilog
    /// </summary>
    /// <param name="builder">主机构建器</param>
    /// <returns>主机构建器</returns>
    public static IHostBuilder UseSerilogLogging(this IHostBuilder builder)
    {
        return builder.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "TurnedOnTimesView")
                .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
                .WriteTo.Console(outputTemplate: 
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    path: Path.Combine("logs", "app-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: 
                        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}");
        });
    }

    /// <summary>
    /// 记录应用程序启动信息
    /// </summary>
    /// <param name="logger">日志器</param>
    /// <param name="appName">应用程序名称</param>
    public static void LogApplicationStart(this Microsoft.Extensions.Logging.ILogger logger, string appName)
    {
        logger.LogInformation("========================================");
        logger.LogInformation("{AppName} 应用程序启动", appName);
        logger.LogInformation("版本: {Version}", typeof(LoggingExtensions).Assembly.GetName().Version?.ToString() ?? "1.0.0");
        logger.LogInformation("环境: {Environment}", Environment.OSVersion);
        logger.LogInformation("用户: {User}@{Machine}", Environment.UserName, Environment.MachineName);
        logger.LogInformation("工作目录: {WorkingDirectory}", Directory.GetCurrentDirectory());
        logger.LogInformation("========================================");
    }

    /// <summary>
    /// 记录应用程序关闭信息
    /// </summary>
    /// <param name="logger">日志器</param>
    /// <param name="appName">应用程序名称</param>
    public static void LogApplicationShutdown(this Microsoft.Extensions.Logging.ILogger logger, string appName)
    {
        logger.LogInformation("========================================");
        logger.LogInformation("{AppName} 应用程序正在关闭", appName);
        logger.LogInformation("运行时长: {Uptime}", DateTime.Now - Process.GetCurrentProcess().StartTime);
        logger.LogInformation("========================================");
    }
}

/// <summary>
/// 性能监控日志扩展
/// </summary>
public static class PerformanceLoggingExtensions
{
    /// <summary>
    /// 记录操作执行时间
    /// </summary>
    /// <param name="logger">日志器</param>
    /// <param name="operationName">操作名称</param>
    /// <returns>性能计时器</returns>
    public static IDisposable LogOperationTime(this Microsoft.Extensions.Logging.ILogger logger, string operationName)
    {
        return new OperationTimer(logger, operationName);
    }

    private sealed class OperationTimer : IDisposable
    {
        private readonly Microsoft.Extensions.Logging.ILogger _logger;
        private readonly string _operationName;
        private readonly System.Diagnostics.Stopwatch _stopwatch;

        public OperationTimer(Microsoft.Extensions.Logging.ILogger logger, string operationName)
        {
            _logger = logger;
            _operationName = operationName;
            _stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            _logger.LogDebug("开始执行操作: {OperationName}", _operationName);
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            var elapsed = _stopwatch.Elapsed;
            
            if (elapsed.TotalMilliseconds > 1000)
            {
                _logger.LogInformation("操作 {OperationName} 完成，耗时: {Duration:F2}s", 
                    _operationName, elapsed.TotalSeconds);
            }
            else
            {
                _logger.LogDebug("操作 {OperationName} 完成，耗时: {Duration}ms", 
                    _operationName, elapsed.TotalMilliseconds);
            }
        }
    }
}

/// <summary>
/// 需要System.Diagnostics.Process
/// </summary>
file static class Process
{
    public static System.Diagnostics.Process GetCurrentProcess() => System.Diagnostics.Process.GetCurrentProcess();
}