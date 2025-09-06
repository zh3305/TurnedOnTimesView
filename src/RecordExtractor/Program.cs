using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TurnedOnTimesView.Core;
using TurnedOnTimesView.Infrastructure.Configuration;
using TurnedOnTimesView.Services;

class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            var host = CreateHost();
            await host.StartAsync();

            var eventLogService = host.Services.GetRequiredService<IEventLogService>();
            var sessionAnalyzer = host.Services.GetRequiredService<ISessionAnalyzer>();
            var appSettings = host.Services.GetRequiredService<IOptions<AppSettings>>().Value;

            var startDate = appSettings.GetDefaultStartDate();
            var endDate = DateTime.Now;
            var events = await eventLogService.GetSystemEventsAsync(startDate, endDate);
            var sessions = await sessionAnalyzer.AnalyzeSessionsAsync(events);

            var sortedSessions = sessions.OrderByDescending(s => s.StartTime).ToList();

            Console.WriteLine("优化后应用程序的前10条启动记录:");
            Console.WriteLine("序号,启动时间,关机时间,持续时间,关机原因,关机类型");
            
            for (int i = 0; i < Math.Min(10, sortedSessions.Count); i++)
            {
                var session = sortedSessions[i];
                var endTimeStr = session.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "运行中";
                var durationStr = session.Duration.ToString(@"hh\:mm\:ss");
                var shutdownReason = string.IsNullOrEmpty(session.ShutdownReason) ? "无" : session.ShutdownReason;
                
                Console.WriteLine($"{i+1},{session.StartTime:yyyy-MM-dd HH:mm:ss},{endTimeStr},{durationStr},{shutdownReason},{session.Type}");
            }

            await host.StopAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"错误: {ex.Message}");
            Console.WriteLine($"详细信息: {ex}");
        }
    }

    private static IHost CreateHost()
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "TurnedOnTimesView");
                config.SetBasePath(basePath)
                      .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                services.Configure<AppSettings>(context.Configuration.GetSection("AppSettings"));
                services.AddMemoryCache();
                services.AddSingleton<EventMappingService>();
                services.AddSingleton<IEventLogService, EventLogService>();
                services.AddSingleton<ISessionAnalyzer, SessionAnalyzer>();
                services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Error));
            })
            .UseConsoleLifetime();

        return builder.Build();
    }
}