using System;
using Microsoft.Extensions.Logging;
using TurnedOnTimesView.Models;
using TurnedOnTimesView.Services;

namespace TurnedOnTimesView.Examples;

/// <summary>
/// EventMappingService使用示例
/// </summary>
public static class EventMappingExample
{
    /// <summary>
    /// 演示如何使用EventMappingService进行事件映射和关机原因解析
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public static void DemonstrateEventMapping(ILogger<EventMappingService> logger)
    {
        // 创建映射服务实例
        var eventMappingService = new EventMappingService(logger);

        Console.WriteLine("=== Windows事件日志映射服务演示 ===\n");

        // 1. 演示事件ID到关机类型的映射
        Console.WriteLine("1. 事件ID到关机类型映射:");
        var eventIds = new[] { 6005, 6006, 1074, 6008, 41, 42, 1 };
        
        foreach (var eventId in eventIds)
        {
            var shutdownType = eventMappingService.GetShutdownType(eventId);
            var description = eventMappingService.GetShutdownTypeDescription(shutdownType);
            
            Console.WriteLine($"   事件ID {eventId:D4} -> {shutdownType} ({description})");
        }

        Console.WriteLine();

        // 2. 演示关机原因的中文描述映射
        Console.WriteLine("2. 关机原因中文描述:");
        var shutdownReasons = new[]
        {
            "0x80000000",
            "0x500000ff", 
            "0x80000012",
            "No Reason",
            "SHTDN_REASON_MAJOR_POWER",
            "未知原因代码",
            ""
        };

        foreach (var reason in shutdownReasons)
        {
            var description = eventMappingService.GetShutdownReasonDescription(reason);
            Console.WriteLine($"   '{reason}' -> {description}");
        }

        Console.WriteLine();

        // 3. 演示详细事件描述
        Console.WriteLine("3. 详细事件描述:");
        var testEvents = new[]
        {
            new { EventId = 1074, Message = "用户 COMPUTER\\User 发起了计算机的关机请求。进程 shutdown.exe (PID 1234)。" },
            new { EventId = 6008, Message = "系统在 2024-01-15 14:30:00 意外关机。" },
            new { EventId = 41, Message = "系统在未完成关机的情况下重新启动。BugcheckCode 0x0000007F" }
        };

        foreach (var evt in testEvents)
        {
            var detailedDescription = eventMappingService.GetDetailedEventDescription(evt.EventId, evt.Message);
            Console.WriteLine($"   事件ID {evt.EventId}: {detailedDescription}");
        }

        Console.WriteLine();

        // 4. 演示支持的事件ID检查
        Console.WriteLine("4. 支持的事件ID:");
        var supportedIds = eventMappingService.GetSupportedEventIds();
        Console.WriteLine($"   支持的事件ID ({supportedIds.Length}个): {string.Join(", ", supportedIds)}");
        
        Console.WriteLine();
        
        // 测试未知事件ID
        var unknownEventId = 9999;
        var isSupported = eventMappingService.IsSupportedEventId(unknownEventId);
        Console.WriteLine($"   事件ID {unknownEventId} 是否支持: {isSupported}");

        Console.WriteLine();

        // 5. 演示完整的SystemEvent对象创建
        Console.WriteLine("5. 完整的SystemEvent对象示例:");
        var sampleEvent = new SystemEvent
        {
            EventId = 1074,
            TimeGenerated = DateTime.Now.AddHours(-1),
            Source = "User32",
            Message = "用户 COMPUTER\\Administrator 发起了计算机的重新启动。进程 shutdown.exe (PID 2468)。关机原因: 0x500000ff",
            Level = "Information",
            RecordId = 12345,
            UserSid = "S-1-5-21-1234567890-1234567890-1234567890-500",
            ProcessInfo = "shutdown.exe",
            ShutdownReason = "0x500000ff",
            
            // 使用映射服务填充的字段
            ShutdownType = eventMappingService.GetShutdownType(1074),
            ShutdownReasonDescription = eventMappingService.GetShutdownReasonDescription("0x500000ff"),
            DetailedDescription = eventMappingService.GetDetailedEventDescription(1074, "用户 COMPUTER\\Administrator 发起了计算机的重新启动。进程 shutdown.exe (PID 2468)。")
        };

        Console.WriteLine($"   事件ID: {sampleEvent.EventId}");
        Console.WriteLine($"   时间: {sampleEvent.TimeGenerated:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"   关机类型: {sampleEvent.ShutdownType}");
        Console.WriteLine($"   类型描述: {eventMappingService.GetShutdownTypeDescription(sampleEvent.ShutdownType)}");
        Console.WriteLine($"   关机原因: {sampleEvent.ShutdownReason}");
        Console.WriteLine($"   原因描述: {sampleEvent.ShutdownReasonDescription}");
        Console.WriteLine($"   详细描述: {sampleEvent.DetailedDescription}");
        Console.WriteLine($"   事件描述: {sampleEvent.EventTypeDescription}");

        // 6. 演示新的布尔属性
        Console.WriteLine();
        Console.WriteLine("6. 事件类型判断:");
        Console.WriteLine($"   是否启动事件: {sampleEvent.IsStartupEvent}");
        Console.WriteLine($"   是否关机事件: {sampleEvent.IsShutdownEvent}");
        Console.WriteLine($"   是否异常关机: {sampleEvent.IsAbnormalShutdownEvent}");
        Console.WriteLine($"   是否睡眠事件: {sampleEvent.IsSleepEvent}");
        Console.WriteLine($"   是否休眠事件: {sampleEvent.IsHibernateEvent}");
        Console.WriteLine($"   是否唤醒事件: {sampleEvent.IsWakeupEvent}");
        Console.WriteLine($"   是否重启事件: {sampleEvent.IsRestartEvent}");

        Console.WriteLine("\n=== 演示结束 ===");
    }
}