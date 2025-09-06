using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace TurnedOnTimesView.Infrastructure.Exceptions;

/// <summary>
/// 错误消息映射服务
/// </summary>
public static class ErrorMessages
{
    /// <summary>
    /// 错误代码到用户友好消息的映射
    /// </summary>
    private static readonly Dictionary<string, string> ErrorCodeToUserMessage = new()
    {
        // 文件访问相关错误
        ["FILE_ACCESS_ERROR"] = "无法访问文件，请检查文件路径和权限设置",
        ["FILE_NOT_FOUND"] = "找不到指定的文件，请确认文件路径是否正确",
        ["FILE_PERMISSION_DENIED"] = "没有访问文件的权限，请检查文件权限设置",
        ["FILE_IN_USE"] = "文件正在被其他程序使用，请稍后再试",
        ["FILE_FORMAT_ERROR"] = "文件格式不正确或已损坏，请选择有效的.evtx文件",
        ["FILE_TOO_LARGE"] = "文件过大，可能导致系统性能问题",
        
        // 网络相关错误
        ["NETWORK_ERROR"] = "网络连接出现问题，请检查网络设置",
        ["NETWORK_TIMEOUT"] = "网络连接超时，请稍后重试",
        ["NETWORK_UNAVAILABLE"] = "网络服务不可用",
        
        // 权限相关错误
        ["INSUFFICIENT_PERMISSION"] = "权限不足，请以管理员身份运行程序",
        ["ADMIN_RIGHTS_REQUIRED"] = "需要管理员权限才能访问系统事件日志",
        ["EVENTLOG_ACCESS_DENIED"] = "无法访问Windows事件日志，请确保具有相应权限",
        
        // 系统服务相关错误
        ["EVENTLOG_SERVICE_UNAVAILABLE"] = "Windows事件日志服务不可用",
        ["SYSTEM_SERVICE_ERROR"] = "系统服务出现错误",
        ["SERVICE_TIMEOUT"] = "系统服务响应超时",
        
        // 资源相关错误
        ["INSUFFICIENT_MEMORY"] = "内存不足，无法完成操作",
        ["INSUFFICIENT_DISK_SPACE"] = "磁盘空间不足",
        ["RESOURCE_BUSY"] = "系统资源繁忙，请稍后再试",
        
        // 数据验证错误
        ["DATA_VALIDATION_ERROR"] = "数据验证失败，请检查输入参数",
        ["INVALID_DATE_RANGE"] = "日期范围无效，请确保开始日期早于结束日期",
        ["INVALID_FILE_PATH"] = "文件路径无效",
        ["UNSUPPORTED_FILE_FORMAT"] = "不支持的文件格式，仅支持.evtx文件",
        
        // 操作相关错误
        ["OPERATION_CANCELLED"] = "操作已取消",
        ["OPERATION_TIMEOUT"] = "操作超时",
        ["OPERATION_FAILED"] = "操作执行失败",
        
        // 配置相关错误
        ["CONFIGURATION_ERROR"] = "配置错误",
        ["INVALID_CONFIGURATION"] = "配置参数无效"
    };

    /// <summary>
    /// 错误代码到解决方案的映射
    /// </summary>
    private static readonly Dictionary<string, List<string>> ErrorCodeToSolutions = new()
    {
        ["FILE_ACCESS_ERROR"] = new()
        {
            "检查文件是否存在",
            "确保文件没有被其他程序占用",
            "检查文件读取权限",
            "尝试以管理员身份运行程序"
        },
        
        ["FILE_NOT_FOUND"] = new()
        {
            "确认文件路径拼写正确",
            "检查文件是否被移动或删除",
            "重新浏览并选择文件"
        },
        
        ["FILE_FORMAT_ERROR"] = new()
        {
            "确保选择的是.evtx格式的事件日志文件",
            "检查文件是否损坏",
            "尝试从其他来源获取有效的事件日志文件"
        },
        
        ["INSUFFICIENT_PERMISSION"] = new()
        {
            "右键点击程序图标，选择"以管理员身份运行"",
            "确保当前用户具有管理员权限",
            "联系系统管理员获取必要的权限"
        },
        
        ["EVENTLOG_SERVICE_UNAVAILABLE"] = new()
        {
            "检查Windows事件日志服务是否正在运行",
            "尝试重启Windows事件日志服务",
            "重启计算机以恢复系统服务"
        },
        
        ["INSUFFICIENT_MEMORY"] = new()
        {
            "关闭其他不必要的程序释放内存",
            "减少查询的日期范围",
            "升级系统内存"
        },
        
        ["NETWORK_ERROR"] = new()
        {
            "检查网络连接是否正常",
            "检查防火墙设置",
            "稍后重试操作"
        },
        
        ["INVALID_DATE_RANGE"] = new()
        {
            "确保开始日期早于结束日期",
            "检查日期格式是否正确",
            "使用"重置日期范围"按钮恢复默认设置"
        }
    };

    /// <summary>
    /// 获取用户友好的错误消息
    /// </summary>
    /// <param name="errorCode">错误代码</param>
    /// <param name="fallbackMessage">备用消息</param>
    /// <returns>用户友好的错误消息</returns>
    public static string GetUserMessage(string errorCode, string fallbackMessage = "")
    {
        if (ErrorCodeToUserMessage.TryGetValue(errorCode, out var userMessage))
        {
            return userMessage;
        }
        
        return string.IsNullOrEmpty(fallbackMessage) ? "发生了未知错误" : fallbackMessage;
    }

    /// <summary>
    /// 获取错误解决方案建议
    /// </summary>
    /// <param name="errorCode">错误代码</param>
    /// <returns>解决方案建议列表</returns>
    public static List<string> GetSolutions(string errorCode)
    {
        if (ErrorCodeToSolutions.TryGetValue(errorCode, out var solutions))
        {
            return new List<string>(solutions);
        }
        
        return new List<string> { "请联系技术支持获取帮助" };
    }

    /// <summary>
    /// 将系统异常转换为应用程序异常
    /// </summary>
    /// <param name="exception">系统异常</param>
    /// <param name="context">错误上下文（可选）</param>
    /// <returns>应用程序异常</returns>
    public static TurnedOnTimesViewException TranslateException(Exception exception, string context = "")
    {
        return exception switch
        {
            FileNotFoundException ex => new FileAccessException(
                ex.FileName ?? context,
                GetUserMessage("FILE_NOT_FOUND"),
                ex.Message,
                ex),
                
            DirectoryNotFoundException ex => new FileAccessException(
                context,
                GetUserMessage("FILE_NOT_FOUND"),
                ex.Message,
                ex),
                
            UnauthorizedAccessException ex => new InsufficientPermissionException(
                "文件访问权限",
                GetUserMessage("FILE_PERMISSION_DENIED"),
                ex),
                
            SecurityException ex => new InsufficientPermissionException(
                "系统安全权限",
                GetUserMessage("INSUFFICIENT_PERMISSION"),
                ex),
                
            IOException ex when ex.Message.Contains("being used by another process") => new FileAccessException(
                context,
                GetUserMessage("FILE_IN_USE"),
                ex.Message,
                ex),
                
            IOException ex => new FileAccessException(
                context,
                GetUserMessage("FILE_ACCESS_ERROR"),
                ex.Message,
                ex),
                
            System.Diagnostics.Eventing.Reader.EventLogException ex => new EventLogServiceException(
                "EVENTLOG_ERROR",
                GetUserMessage("EVENTLOG_SERVICE_UNAVAILABLE"),
                ex.Message,
                ex),
                
            OutOfMemoryException ex => new InsufficientResourceException(
                "内存",
                0,
                0,
                GetUserMessage("INSUFFICIENT_MEMORY")),
                
            TimeoutException ex => new EventLogServiceException(
                "OPERATION_TIMEOUT",
                GetUserMessage("OPERATION_TIMEOUT"),
                ex.Message,
                ex),
                
            System.OperationCanceledException ex => new OperationCancelledException(
                context,
                GetUserMessage("OPERATION_CANCELLED")),
                
            ArgumentException ex => new DataValidationException(
                ex.Message,
                GetUserMessage("DATA_VALIDATION_ERROR")),
                
            ArgumentNullException ex => new DataValidationException(
                $"参数 '{ex.ParamName}' 不能为空",
                GetUserMessage("DATA_VALIDATION_ERROR")),
                
            NotSupportedException ex => new DataValidationException(
                ex.Message,
                GetUserMessage("UNSUPPORTED_FILE_FORMAT")),
                
            InvalidOperationException ex => new EventLogServiceException(
                "INVALID_OPERATION",
                "当前操作无效或不被支持",
                ex.Message,
                ex),
                
            // 网络相关异常
            System.Net.NetworkInformation.NetworkInformationException ex => new NetworkException(
                GetUserMessage("NETWORK_ERROR"),
                ex.Message,
                ex),
                
            System.Net.Sockets.SocketException ex => new NetworkException(
                GetUserMessage("NETWORK_ERROR"),
                ex.Message,
                ex),
                
            // 默认处理
            _ => new TurnedOnTimesViewException(
                "UNKNOWN_ERROR",
                $"操作失败: {exception.Message}",
                exception.Message,
                exception)
            {
                Severity = ErrorSeverity.Error
            }
        };
    }

    /// <summary>
    /// 根据文件大小判断并创建相应的异常
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="fileSize">文件大小（字节）</param>
    /// <returns>如果文件过大则返回异常，否则返回null</returns>
    public static TurnedOnTimesViewException? CheckFileSize(string filePath, long fileSize)
    {
        const long MaxRecommendedSize = 500 * 1024 * 1024; // 500MB
        const long MaxAllowedSize = 2L * 1024 * 1024 * 1024; // 2GB
        
        if (fileSize > MaxAllowedSize)
        {
            return new FileAccessException(
                filePath,
                $"文件过大 ({fileSize / (1024 * 1024):F1}MB)，超过最大允许大小 ({MaxAllowedSize / (1024 * 1024):F1}MB)",
                $"文件大小超出限制: {fileSize} 字节")
            {
                Severity = ErrorSeverity.Error
            };
        }
        
        if (fileSize > MaxRecommendedSize)
        {
            return new FileAccessException(
                filePath,
                $"文件较大 ({fileSize / (1024 * 1024):F1}MB)，处理可能较慢",
                $"文件大小超出推荐值: {fileSize} 字节")
            {
                Severity = ErrorSeverity.Warning
            };
        }
        
        return null;
    }

    /// <summary>
    /// 验证日期范围
    /// </summary>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <returns>如果日期范围无效则返回异常，否则返回null</returns>
    public static DataValidationException? ValidateDateRange(DateTime startDate, DateTime endDate)
    {
        var errors = new List<string>();
        
        if (startDate > endDate)
        {
            errors.Add("开始日期不能晚于结束日期");
        }
        
        if (endDate > DateTime.Now.AddDays(1))
        {
            errors.Add("结束日期不能超过明天");
        }
        
        if (startDate < DateTime.Now.AddYears(-10))
        {
            errors.Add("开始日期不能超过10年前");
        }
        
        var timeSpan = endDate - startDate;
        if (timeSpan.TotalDays > 365)
        {
            errors.Add("查询时间范围不能超过365天");
        }
        
        if (errors.Count > 0)
        {
            return new DataValidationException(errors, GetUserMessage("INVALID_DATE_RANGE"));
        }
        
        return null;
    }
}