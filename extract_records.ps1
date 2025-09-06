# 提取我们应用程序的启动记录数据
$outputPath = "H:\Code_Poject\TurnedOnTimesView"
Set-Location $outputPath

# 构建并运行应用程序，输出到文件
Write-Host "开始构建应用程序..."
dotnet build src/TurnedOnTimesView/TurnedOnTimesView.csproj --configuration Release --verbosity quiet

if ($LASTEXITCODE -eq 0) {
    Write-Host "构建成功，正在提取记录数据..."
    
    # 创建一个临时的控制台版本来输出数据
    $tempConsole = @"
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurnedOnTimesView.Services;
using TurnedOnTimesView.Core;
using TurnedOnTimesView.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

class Program
{
    static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.Configure<AppSettings>(context.Configuration.GetSection("AppSettings"));
                services.AddSingleton<IEventLogService, EventLogService>();
                services.AddSingleton<ISessionAnalyzer, SessionAnalyzer>();
                services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Error));
            })
            .Build();

        var eventLogService = host.Services.GetRequiredService<IEventLogService>();
        var sessionAnalyzer = host.Services.GetRequiredService<ISessionAnalyzer>();
        var appSettings = host.Services.GetRequiredService<IOptions<AppSettings>>().Value;

        var dateRange = appSettings.GetDefaultDateRange();
        var events = await eventLogService.GetSystemEventsAsync(dateRange.Start, dateRange.End);
        var sessions = await sessionAnalyzer.AnalyzeSessionsAsync(events);

        var sortedSessions = sessions.OrderByDescending(s => s.StartTime).ToList();

        Console.WriteLine("序号,启动时间,关机时间,持续时间,关机原因,关机类型");
        for (int i = 0; i < Math.Min(10, sortedSessions.Count); i++)
        {
            var session = sortedSessions[i];
            Console.WriteLine($"{i+1},{session.StartTime:yyyy-MM-dd HH:mm:ss},{session.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""},{session.Duration},{session.ShutdownReason},{session.ShutdownType}");
        }
    }
}
"@
    
    $tempConsole | Out-File -FilePath "temp_console.cs" -Encoding UTF8
    
    # 编译临时控制台程序
    dotnet new console -n TempConsole -f net9.0 --force
    Copy-Item "temp_console.cs" "TempConsole/Program.cs" -Force
    
    # 添加必要的包引用
    Set-Location TempConsole
    dotnet add reference "../src/TurnedOnTimesView/TurnedOnTimesView.csproj"
    
    # 运行并输出到文件
    dotnet run > "../our_records.csv" 2>&1
    
    Set-Location ..
    Remove-Item -Recurse -Force TempConsole
    Remove-Item temp_console.cs
    
    Write-Host "记录数据已提取到 our_records.csv"
} else {
    Write-Host "构建失败，请检查项目配置"
}