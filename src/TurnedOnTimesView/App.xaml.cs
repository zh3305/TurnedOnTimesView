using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Windows;
using TurnedOnTimesView.Infrastructure.Configuration;
using TurnedOnTimesView.Infrastructure.DependencyInjection;
using TurnedOnTimesView.Infrastructure.Logging;
using TurnedOnTimesView.ViewModels;
using TurnedOnTimesView.Views;

namespace TurnedOnTimesView;

/// <summary>
/// WPF应用程序入口点，配置依赖注入和服务容器
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <summary>
    /// 应用程序启动时的配置
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            // 构建主机和服务容器
            _host = CreateHost();

            // 启动主机
            await _host.StartAsync();

            // 验证服务
            var serviceValidation = _host.Services.ValidateServices();
            if (!serviceValidation.IsValid)
            {
                var logger = _host.Services.GetRequiredService<ILogger<App>>();
                logger.LogError("服务验证失败:{NewLine}{Errors}", 
                    Environment.NewLine, serviceValidation.GetErrorSummary());

                // 显示错误对话框
                ShowServiceValidationErrors(serviceValidation.Errors);
                
                // 如果有致命错误，考虑退出应用程序
                if (HasCriticalErrors(serviceValidation.Errors))
                {
                    Current.Shutdown(1);
                    return;
                }
            }

            // 记录应用启动
            var startupLogger = _host.Services.GetRequiredService<ILogger<App>>();
            startupLogger.LogApplicationStart("Windows开关机记录分析工具");

            // 创建并显示主窗口
            var mainWindow = CreateMainWindow();
            mainWindow.Show();

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            // 处理启动时的严重错误
            HandleStartupError(ex);
            Current.Shutdown(1);
        }
    }

    /// <summary>
    /// 应用程序退出时的清理
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host != null)
            {
                var logger = _host.Services.GetService<ILogger<App>>();
                logger?.LogApplicationShutdown("Windows开关机记录分析工具");

                await _host.StopAsync(TimeSpan.FromSeconds(5));
                _host.Dispose();
            }
        }
        catch (Exception ex)
        {
            // 记录关闭时的错误，但不阻止应用程序退出
            var logger = _host?.Services.GetService<ILogger<App>>();
            logger?.LogError(ex, "应用程序关闭时发生错误");
        }
        finally
        {
            base.OnExit(e);
        }
    }

    /// <summary>
    /// 创建主机和配置服务
    /// </summary>
    private IHost CreateHost()
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                // 配置文件优先级：appsettings.json -> appsettings.{Environment}.json -> 用户设置
                config.SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                      .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                      .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", 
                                  optional: true, reloadOnChange: true)
                      .AddEnvironmentVariables()
                      .AddCommandLine(Environment.GetCommandLineArgs());
            })
            .UseSerilogLogging()
            .ConfigureServices((context, services) =>
            {
                // 注册应用程序服务
                var isDevelopment = context.HostingEnvironment.IsDevelopment();
                services.AddTurnedOnTimesViewServices(context.Configuration, isDevelopment);
            })
            .UseConsoleLifetime(); // 使用控制台生命周期管理

        return builder.Build();
    }

    /// <summary>
    /// 创建主窗口
    /// </summary>
    private MainWindow CreateMainWindow()
    {
        var serviceProvider = _host!.Services;
        var mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();
        
        return new MainWindow
        {
            DataContext = mainViewModel
        };
    }

    /// <summary>
    /// 显示服务验证错误
    /// </summary>
    private static void ShowServiceValidationErrors(IReadOnlyList<string> errors)
    {
        var errorMessage = string.Join(Environment.NewLine + "• ", errors);
        var fullMessage = $"应用程序初始化时发现以下问题:{Environment.NewLine}• {errorMessage}" +
                         $"{Environment.NewLine}{Environment.NewLine}应用程序可能无法正常工作。";

        MessageBox.Show(
            fullMessage,
            "初始化警告",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <summary>
    /// 检查是否有致命错误
    /// </summary>
    private static bool HasCriticalErrors(IReadOnlyList<string> errors)
    {
        return errors.Any(error => 
            error.Contains("创建失败") || 
            error.Contains("配置无效") ||
            error.Contains("AppSettings 验证失败"));
    }

    /// <summary>
    /// 处理启动错误
    /// </summary>
    private static void HandleStartupError(Exception exception)
    {
        var errorMessage = $"应用程序启动时发生严重错误:{Environment.NewLine}{exception.Message}";
        
        if (exception.InnerException != null)
        {
            errorMessage += $"{Environment.NewLine}详细信息: {exception.InnerException.Message}";
        }

        // 尝试记录到文件
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "startup-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 启动错误:{Environment.NewLine}{exception}");
        }
        catch
        {
            // 忽略日志写入错误
        }

        MessageBox.Show(
            errorMessage,
            "启动错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}