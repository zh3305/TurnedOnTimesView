using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TurnedOnTimesView.Models;

namespace TurnedOnTimesView.Services;

/// <summary>
/// 事件映射服务，提供Windows事件日志的事件ID到关机类型的映射以及关机原因的中文描述
/// </summary>
public sealed class EventMappingService
{
    private readonly ILogger<EventMappingService> _logger;

    /// <summary>
    /// 事件ID到关机类型的映射表
    /// 基于Windows事件日志研究结果的完整映射
    /// </summary>
    private static readonly Dictionary<int, ShutdownType> EventIdToShutdownTypeMap = new()
    {
        // 系统启动事件
        { 6005, ShutdownType.Startup },    // EventLog: 事件日志服务已启动
        { 6009, ShutdownType.Startup },    // EventLog: Microsoft Windows 版本信息
        { 12, ShutdownType.Startup },      // Kernel-General: 操作系统启动

        // 正常关机事件
        { 6006, ShutdownType.Normal },     // EventLog: 事件日志服务已停止
        { 13, ShutdownType.Normal },       // Kernel-General: 操作系统正在关机

        // 用户发起的关机/重启事件
        { 1074, ShutdownType.UserInitiated },  // User32: 用户发起的系统关机
        { 1075, ShutdownType.UserInitiated },  // User32: 用户取消了关机

        // 系统发起的关机事件
        { 1076, ShutdownType.SystemInitiated }, // User32: 系统发起的关机
        { 1077, ShutdownType.SystemInitiated }, // User32: 用户未响应关机请求的原因

        // 意外关机事件
        { 6008, ShutdownType.Unexpected }, // EventLog: 系统意外关机
        { 6013, ShutdownType.Unexpected }, // EventLog: 系统启动时间

        // 强制关机/意外重启事件（内核电源事件）
        { 41, ShutdownType.Forced },       // Kernel-Power: 系统重新启动而没有完成关机
        { 109, ShutdownType.Forced },      // Kernel-Power: 内核电源管理器发起的关机

        // 系统睡眠事件
        { 42, ShutdownType.Sleep },        // Kernel-Power: 系统进入睡眠状态
        { 4, ShutdownType.Sleep },         // Power-Troubleshooter: 系统睡眠状态
        { 506, ShutdownType.Sleep },       // SystemSettings: 用户发起的睡眠

        // 系统休眠事件
        { 27, ShutdownType.Hibernate },    // Kernel-Power: 系统休眠

        // 从睡眠/休眠唤醒事件
        { 1, ShutdownType.WakeUp },        // Power-Troubleshooter: 系统唤醒
        { 107, ShutdownType.WakeUp },      // Kernel-Power: 系统已被内核电源管理器唤醒
        { 507, ShutdownType.WakeUp },      // SystemSettings: 系统从睡眠中唤醒

        // 重启相关事件
        { 1001, ShutdownType.Restart },    // BugCheck: 系统崩溃重启
        { 1003, ShutdownType.Restart },    // BugCheck: 蓝屏重启后的系统启动

        // Windows更新相关的关机/重启
        { 19, ShutdownType.SystemInitiated },    // Windows Update: 安装完成需要重启
        { 20, ShutdownType.SystemInitiated },    // Windows Update: 自动重启计划

        // 服务和应用程序发起的关机
        { 4609, ShutdownType.SystemInitiated },  // Windows: Windows 正在关闭
        { 7001, ShutdownType.SystemInitiated },  // Service Control Manager: 服务关机
        { 7034, ShutdownType.Unexpected },       // Service Control Manager: 服务意外终止

        // 电源按钮事件
        { 144, ShutdownType.UserInitiated },     // User32: 用户按下电源按钮
        { 145, ShutdownType.UserInitiated }      // User32: 用户会话锁定状态改变
    };

    /// <summary>
    /// 关机原因代码到中文描述的映射表
    /// 基于Windows关机原因码的完整映射
    /// </summary>
    private static readonly Dictionary<string, string> ShutdownReasonDescriptionMap = new()
    {
        // 意外关机原因 (0x8000xxxx)
        { "0x80000000", "意外关机 (未指定)" },
        { "0x80000001", "硬件故障 (主板)" },
        { "0x80000002", "硬件故障 (内存)" },
        { "0x80000003", "硬件故障 (处理器)" },
        { "0x80000004", "硬件故障 (网络)" },
        { "0x80000005", "其他硬件故障" },
        { "0x80000006", "其他驱动程序问题" },
        { "0x80000007", "其他系统故障" },
        { "0x80000008", "WMI 问题" },
        { "0x80000012", "系统蓝屏崩溃" },
        { "0x80020000", "电源故障 (断电)" },
        { "0x80040000", "蓝屏死机 (BSOD)" },
        { "0x80080000", "系统挂起无响应" },

        // 计划内关机原因 (0x8400xxxx)
        { "0x84000000", "计划内关机 (其他)" },
        { "0x84000001", "计划内关机 (硬件维护)" },
        { "0x84000002", "计划内关机 (操作系统)" },
        { "0x84000003", "计划内关机 (应用程序)" },
        { "0x84000004", "计划内关机 (系统故障)" },
        { "0x84000005", "计划内关机 (电源故障)" },
        { "0x84020000", "计划内关机 (硬件维护)" },
        { "0x84040000", "计划内关机 (软件维护)" },
        { "0x84080000", "计划内关机 (安装)" },

        // 用户发起的关机 (0x500xxxxx)
        { "0x50000000", "用户关机" },
        { "0x500000ff", "用户手动关机 (开始菜单)" },
        { "0x500000fe", "用户注销" },
        { "0x50010000", "用户重启" },
        { "0x50020000", "用户关机 (电源按钮)" },
        { "0x50040000", "用户关机 (Alt+F4)" },
        { "0x50080000", "用户关机 (命令行)" },

        // 系统发起的关机 (注意：0x80000001和0x80000002已在意外关机中定义)
        { "0x80000010", "系统自动重启" },
        { "0x80000011", "系统关机 (热关机)" },
        { "0x80000013", "系统关机 (快速关机)" },

        // 应用程序发起的关机 (0x400xxxxx)
        { "0x40000000", "应用程序关机" },
        { "0x40010000", "应用程序重启" },
        { "0x40020000", "应用程序安装" },
        { "0x40030000", "应用程序卸载" },
        { "0x40040000", "应用程序重新配置" },
        { "0x40050000", "应用程序维护" },
        { "0x40060000", "应用程序不稳定" },

        // 电源相关 (0x850xxxxx)
        { "0x85000000", "电源按钮关机" },
        { "0x85010000", "睡眠按钮" },
        { "0x85020000", "电源管理器" },
        { "0x85040000", "UPS 电池不足" },

        // Windows Update 相关
        { "0x80020001", "Windows 更新重启" },
        { "0x80020002", "关键更新安装" },
        { "0x80020003", "安全更新重启" },
        { "0x80020004", "驱动程序更新重启" },

        // 服务相关
        { "0x80030001", "服务控制管理器关机" },
        { "0x80030002", "服务故障恢复" },
        { "0x80030003", "关键服务失败" },

        // 虚拟化相关 (针对虚拟机)
        { "0x80050001", "虚拟机管理器重启" },
        { "0x80050002", "容器服务重启" },
        { "0x80050003", "Hyper-V 关机" },

        // 特殊关机原因
        { "0x00000000", "正常关机" },
        { "0x00000001", "其他 (计划内)" },
        { "0x00000002", "其他 (计划外)" },

        // 常见文本描述
        { "No Reason", "未指定关机原因" },
        { "Unknown", "未知关机原因" },
        { "Unplanned", "计划外关机" },
        { "Planned", "计划内关机" },
        { "User Defined", "用户自定义" },
        { "Major Application", "主要应用程序" },
        { "Major Hardware", "主要硬件" },
        { "Major Operating System", "主要操作系统" },
        { "Major Power", "主要电源" },
        { "Major Software", "主要软件" },
        { "Major System", "主要系统" },
        { "Minor Blue Screen", "蓝屏" },
        { "Minor Hung", "系统挂起" },
        { "Minor Installation", "安装" },
        { "Minor Maintenance", "维护" },
        { "Minor Upgrade", "升级" },
        { "", "未知原因" }
    };

    /// <summary>
    /// Windows关机原因类型的详细描述映射
    /// </summary>
    private static readonly Dictionary<string, string> DetailedShutdownReasonMap = new()
    {
        // 主要原因 - 用户相关
        { "SHTDN_REASON_MAJOR_APPLICATION", "应用程序" },
        { "SHTDN_REASON_MAJOR_HARDWARE", "硬件" },
        { "SHTDN_REASON_MAJOR_OPERATINGSYSTEM", "操作系统" },
        { "SHTDN_REASON_MAJOR_OTHER", "其他" },
        { "SHTDN_REASON_MAJOR_POWER", "电源" },
        { "SHTDN_REASON_MAJOR_SOFTWARE", "软件" },
        { "SHTDN_REASON_MAJOR_SYSTEM", "系统" },

        // 次要原因
        { "SHTDN_REASON_MINOR_BLUESCREEN", "蓝屏死机" },
        { "SHTDN_REASON_MINOR_CORDUNPLUGGED", "电源线拔出" },
        { "SHTDN_REASON_MINOR_DISK", "磁盘错误" },
        { "SHTDN_REASON_MINOR_ENVIRONMENT", "环境问题" },
        { "SHTDN_REASON_MINOR_HARDWARE_DRIVER", "硬件驱动程序" },
        { "SHTDN_REASON_MINOR_HOTFIX", "热修复" },
        { "SHTDN_REASON_MINOR_HOTFIX_UNINSTALL", "热修复卸载" },
        { "SHTDN_REASON_MINOR_HUNG", "系统挂起" },
        { "SHTDN_REASON_MINOR_INSTALLATION", "安装" },
        { "SHTDN_REASON_MINOR_MAINTENANCE", "维护" },
        { "SHTDN_REASON_MINOR_MMC", "MMC问题" },
        { "SHTDN_REASON_MINOR_NETWORK_CONNECTIVITY", "网络连接问题" },
        { "SHTDN_REASON_MINOR_NETWORKCARD", "网络适配器" },
        { "SHTDN_REASON_MINOR_OTHER", "其他问题" },
        { "SHTDN_REASON_MINOR_OTHERDRIVER", "其他驱动程序" },
        { "SHTDN_REASON_MINOR_POWER_SUPPLY", "电源供应问题" },
        { "SHTDN_REASON_MINOR_PROCESSOR", "处理器问题" },
        { "SHTDN_REASON_MINOR_RECONFIG", "重新配置" },
        { "SHTDN_REASON_MINOR_SECURITY", "安全问题" },
        { "SHTDN_REASON_MINOR_SECURITYFIX", "安全修复" },
        { "SHTDN_REASON_MINOR_SECURITYFIX_UNINSTALL", "安全修复卸载" },
        { "SHTDN_REASON_MINOR_SERVICEPACK", "服务包" },
        { "SHTDN_REASON_MINOR_SERVICEPACK_UNINSTALL", "服务包卸载" },
        { "SHTDN_REASON_MINOR_TERMSRV", "终端服务" },
        { "SHTDN_REASON_MINOR_UNSTABLE", "系统不稳定" },
        { "SHTDN_REASON_MINOR_UPGRADE", "系统升级" },
        { "SHTDN_REASON_MINOR_WMI", "WMI问题" },

        // 标志
        { "SHTDN_REASON_FLAG_USER_DEFINED", "用户自定义" },
        { "SHTDN_REASON_FLAG_PLANNED", "计划内" }
    };

    /// <summary>
    /// 用于提取关机原因代码的正则表达式
    /// </summary>
    private static readonly Regex ReasonCodeRegex = new(
        @"0x[0-9a-fA-F]{8}|SHTDN_REASON_[A-Z_]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public EventMappingService(ILogger<EventMappingService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 根据事件ID获取关机类型
    /// </summary>
    /// <param name="eventId">Windows事件日志的事件ID</param>
    /// <returns>对应的关机类型</returns>
    public ShutdownType GetShutdownType(int eventId)
    {
        if (EventIdToShutdownTypeMap.TryGetValue(eventId, out var shutdownType))
        {
            return shutdownType;
        }

        _logger.LogWarning("未知的事件ID: {EventId}", eventId);
        return ShutdownType.Unknown;
    }

    /// <summary>
    /// 获取关机类型的中文描述
    /// </summary>
    /// <param name="shutdownType">关机类型</param>
    /// <returns>中文描述</returns>
    public string GetShutdownTypeDescription(ShutdownType shutdownType)
    {
        var field = shutdownType.GetType().GetField(shutdownType.ToString());
        if (field?.GetCustomAttribute<DescriptionAttribute>() is DescriptionAttribute attribute)
        {
            return attribute.Description;
        }

        return shutdownType.ToString();
    }

    /// <summary>
    /// 解析并获取关机原因的中文描述
    /// </summary>
    /// <param name="rawReason">原始关机原因字符串</param>
    /// <returns>中文描述</returns>
    public string GetShutdownReasonDescription(string rawReason)
    {
        if (string.IsNullOrWhiteSpace(rawReason))
        {
            return "未指定原因";
        }

        // 首先尝试直接匹配
        if (ShutdownReasonDescriptionMap.TryGetValue(rawReason, out var directMatch))
        {
            return directMatch;
        }

        // 尝试提取十六进制代码
        var matches = ReasonCodeRegex.Matches(rawReason);
        if (matches.Count > 0)
        {
            var descriptions = new List<string>();

            foreach (Match match in matches)
            {
                var reasonCode = match.Value;
                
                if (ShutdownReasonDescriptionMap.TryGetValue(reasonCode, out var codeDescription))
                {
                    descriptions.Add(codeDescription);
                }
                else if (DetailedShutdownReasonMap.TryGetValue(reasonCode, out var detailedDescription))
                {
                    descriptions.Add(detailedDescription);
                }
            }

            if (descriptions.Count > 0)
            {
                return string.Join(" - ", descriptions);
            }
        }

        // 尝试模糊匹配
        var fuzzyMatch = GetFuzzyReasonDescription(rawReason);
        if (!string.IsNullOrEmpty(fuzzyMatch))
        {
            return fuzzyMatch;
        }

        _logger.LogDebug("无法解析关机原因: {RawReason}", rawReason);
        return $"未知原因 ({rawReason})";
    }

    /// <summary>
    /// 根据事件ID和消息内容获取详细的事件描述
    /// </summary>
    /// <param name="eventId">事件ID</param>
    /// <param name="message">事件消息</param>
    /// <returns>详细描述</returns>
    public string GetDetailedEventDescription(int eventId, string message)
    {
        var shutdownType = GetShutdownType(eventId);
        var baseDescription = GetShutdownTypeDescription(shutdownType);

        // 从消息中提取额外信息
        var additionalInfo = ExtractAdditionalInfo(eventId, message);

        return string.IsNullOrEmpty(additionalInfo) 
            ? baseDescription 
            : $"{baseDescription} ({additionalInfo})";
    }

    /// <summary>
    /// 获取所有支持的事件ID
    /// </summary>
    /// <returns>支持的事件ID数组</returns>
    public int[] GetSupportedEventIds()
    {
        return EventIdToShutdownTypeMap.Keys.ToArray();
    }

    /// <summary>
    /// 检查事件ID是否受支持
    /// </summary>
    /// <param name="eventId">事件ID</param>
    /// <returns>是否受支持</returns>
    public bool IsSupportedEventId(int eventId)
    {
        return EventIdToShutdownTypeMap.ContainsKey(eventId);
    }

    /// <summary>
    /// 模糊匹配关机原因描述
    /// </summary>
    /// <param name="rawReason">原始关机原因</param>
    /// <returns>匹配的描述</returns>
    private static string GetFuzzyReasonDescription(string rawReason)
    {
        var lowerReason = rawReason.ToLowerInvariant();

        // 检查常见关键字
        if (lowerReason.Contains("power") || lowerReason.Contains("电源"))
            return "电源相关问题";
        
        if (lowerReason.Contains("user") || lowerReason.Contains("用户"))
            return "用户操作";
        
        if (lowerReason.Contains("system") || lowerReason.Contains("系统"))
            return "系统操作";
        
        if (lowerReason.Contains("application") || lowerReason.Contains("应用"))
            return "应用程序触发";
        
        if (lowerReason.Contains("hardware") || lowerReason.Contains("硬件"))
            return "硬件问题";
        
        if (lowerReason.Contains("software") || lowerReason.Contains("软件"))
            return "软件问题";
        
        if (lowerReason.Contains("bluescreen") || lowerReason.Contains("蓝屏"))
            return "蓝屏死机";
        
        if (lowerReason.Contains("unexpected") || lowerReason.Contains("意外"))
            return "意外关机";

        return string.Empty;
    }

    /// <summary>
    /// 从事件消息中提取额外信息
    /// </summary>
    /// <param name="eventId">事件ID</param>
    /// <param name="message">事件消息</param>
    /// <returns>额外信息</returns>
    private static string ExtractAdditionalInfo(int eventId, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        return eventId switch
        {
            1074 => ExtractUserInfo(message),
            6008 => ExtractUnexpectedShutdownInfo(message),
            41 => ExtractKernelPowerInfo(message),
            _ => string.Empty
        };
    }

    /// <summary>
    /// 从用户关机事件中提取用户信息
    /// </summary>
    /// <param name="message">事件消息</param>
    /// <returns>用户信息</returns>
    private static string ExtractUserInfo(string message)
    {
        // 匹配用户名和进程名
        var userMatch = Regex.Match(message, @"用户\s+([^\s]+)");
        var processMatch = Regex.Match(message, @"进程\s+([^\s]+)");

        var parts = new List<string>();
        if (userMatch.Success)
            parts.Add($"用户: {userMatch.Groups[1].Value}");
        if (processMatch.Success)
            parts.Add($"进程: {processMatch.Groups[1].Value}");

        return string.Join(", ", parts);
    }

    /// <summary>
    /// 从意外关机事件中提取信息
    /// </summary>
    /// <param name="message">事件消息</param>
    /// <returns>意外关机信息</returns>
    private static string ExtractUnexpectedShutdownInfo(string message)
    {
        // 提取可能的时间信息或其他详细信息
        if (message.Contains("时间"))
        {
            var timeMatch = Regex.Match(message, @"(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})");
            if (timeMatch.Success)
                return $"时间: {timeMatch.Groups[1].Value}";
        }

        return "系统未正常关机";
    }

    /// <summary>
    /// 从内核电源事件中提取信息
    /// </summary>
    /// <param name="message">事件消息</param>
    /// <returns>内核电源信息</returns>
    private static string ExtractKernelPowerInfo(string message)
    {
        // 提取电源状态相关信息
        if (message.Contains("BugcheckCode"))
        {
            var bugCheckMatch = Regex.Match(message, @"BugcheckCode\s+(\w+)");
            if (bugCheckMatch.Success)
                return $"错误代码: {bugCheckMatch.Groups[1].Value}";
        }

        return "内核电源事件";
    }
}