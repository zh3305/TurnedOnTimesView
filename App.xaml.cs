using System.Windows;
using Serilog;
using TurnedOnTimesView.Services;

namespace TurnedOnTimesView
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // 配置日志
            Log.Logger = LoggerConfig.ConfigureLogger();
            Log.Information("应用程序启动");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("应用程序退出");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}