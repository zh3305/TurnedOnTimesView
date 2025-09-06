using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace TurnedOnTimesView.Infrastructure.Exceptions;

/// <summary>
/// 应用程序异常基类
/// </summary>
public abstract class TurnedOnTimesViewException : Exception
{
    /// <summary>
    /// 错误代码
    /// </summary>
    public string ErrorCode { get; }
    
    /// <summary>
    /// 用户友好的错误消息
    /// </summary>
    public string UserMessage { get; }
    
    /// <summary>
    /// 错误详细信息
    /// </summary>
    public Dictionary<string, object> Details { get; }
    
    /// <summary>
    /// 是否可以重试
    /// </summary>
    public virtual bool IsRetryable { get; protected set; }
    
    /// <summary>
    /// 错误严重性级别
    /// </summary>
    public ErrorSeverity Severity { get; protected set; }

    protected TurnedOnTimesViewException(
        string errorCode, 
        string userMessage, 
        string technicalMessage = "", 
        Exception? innerException = null)
        : base(string.IsNullOrEmpty(technicalMessage) ? userMessage : technicalMessage, innerException)
    {
        ErrorCode = errorCode;
        UserMessage = userMessage;
        Details = new Dictionary<string, object>();
        IsRetryable = false;
        Severity = ErrorSeverity.Error;
    }

    protected TurnedOnTimesViewException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        ErrorCode = info.GetString(nameof(ErrorCode)) ?? string.Empty;
        UserMessage = info.GetString(nameof(UserMessage)) ?? string.Empty;
        Details = (Dictionary<string, object>)(info.GetValue(nameof(Details), typeof(Dictionary<string, object>)) ?? new Dictionary<string, object>());
        IsRetryable = info.GetBoolean(nameof(IsRetryable));
        Severity = (ErrorSeverity)info.GetInt32(nameof(Severity));
    }

    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(ErrorCode), ErrorCode);
        info.AddValue(nameof(UserMessage), UserMessage);
        info.AddValue(nameof(Details), Details);
        info.AddValue(nameof(IsRetryable), IsRetryable);
        info.AddValue(nameof(Severity), Severity);
    }

    /// <summary>
    /// 添加错误详细信息
    /// </summary>
    public TurnedOnTimesViewException WithDetail(string key, object value)
    {
        Details[key] = value;
        return this;
    }
}

/// <summary>
/// 错误严重性级别
/// </summary>
public enum ErrorSeverity
{
    /// <summary>
    /// 信息性错误
    /// </summary>
    Info = 0,
    
    /// <summary>
    /// 警告
    /// </summary>
    Warning = 1,
    
    /// <summary>
    /// 错误
    /// </summary>
    Error = 2,
    
    /// <summary>
    /// 严重错误
    /// </summary>
    Critical = 3
}

/// <summary>
/// 数据源相关异常
/// </summary>
public class DataSourceException : TurnedOnTimesViewException
{
    public DataSourceException(string errorCode, string userMessage, string technicalMessage = "", Exception? innerException = null)
        : base(errorCode, userMessage, technicalMessage, innerException)
    {
    }

    protected DataSourceException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}

/// <summary>
/// 文件访问异常
/// </summary>
public class FileAccessException : DataSourceException
{
    public string FilePath { get; }

    public FileAccessException(string filePath, string userMessage, string technicalMessage = "", Exception? innerException = null)
        : base("FILE_ACCESS_ERROR", userMessage, technicalMessage, innerException)
    {
        FilePath = filePath;
        IsRetryable = true;
        WithDetail("FilePath", filePath);
    }

    protected FileAccessException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        FilePath = info.GetString(nameof(FilePath)) ?? string.Empty;
    }

    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(FilePath), FilePath);
    }
}

/// <summary>
/// 文件格式异常
/// </summary>
public class FileFormatException : DataSourceException
{
    public string FilePath { get; }
    public string ExpectedFormat { get; }

    public FileFormatException(string filePath, string expectedFormat, string userMessage, Exception? innerException = null)
        : base("FILE_FORMAT_ERROR", userMessage, $"文件格式不正确: {filePath}, 期望格式: {expectedFormat}", innerException)
    {
        FilePath = filePath;
        ExpectedFormat = expectedFormat;
        IsRetryable = false;
        WithDetail("FilePath", filePath).WithDetail("ExpectedFormat", expectedFormat);
    }

    protected FileFormatException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        FilePath = info.GetString(nameof(FilePath)) ?? string.Empty;
        ExpectedFormat = info.GetString(nameof(ExpectedFormat)) ?? string.Empty;
    }

    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(FilePath), FilePath);
        info.AddValue(nameof(ExpectedFormat), ExpectedFormat);
    }
}

/// <summary>
/// 权限不足异常
/// </summary>
public class InsufficientPermissionException : TurnedOnTimesViewException
{
    public string RequiredPermission { get; }

    public InsufficientPermissionException(string requiredPermission, string userMessage, Exception? innerException = null)
        : base("INSUFFICIENT_PERMISSION", userMessage, $"权限不足，需要: {requiredPermission}", innerException)
    {
        RequiredPermission = requiredPermission;
        IsRetryable = false;
        Severity = ErrorSeverity.Warning;
        WithDetail("RequiredPermission", requiredPermission);
    }

    protected InsufficientPermissionException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        RequiredPermission = info.GetString(nameof(RequiredPermission)) ?? string.Empty;
    }

    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(RequiredPermission), RequiredPermission);
    }
}

/// <summary>
/// 事件日志服务异常
/// </summary>
public class EventLogServiceException : TurnedOnTimesViewException
{
    public EventLogServiceException(string errorCode, string userMessage, string technicalMessage = "", Exception? innerException = null)
        : base(errorCode, userMessage, technicalMessage, innerException)
    {
        // 网络和系统相关错误通常可以重试
        IsRetryable = errorCode.Contains("NETWORK") || errorCode.Contains("TIMEOUT") || errorCode.Contains("SERVICE_UNAVAILABLE");
    }

    protected EventLogServiceException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}

/// <summary>
/// 网络相关异常
/// </summary>
public class NetworkException : TurnedOnTimesViewException
{
    public NetworkException(string userMessage, string technicalMessage = "", Exception? innerException = null)
        : base("NETWORK_ERROR", userMessage, technicalMessage, innerException)
    {
        IsRetryable = true;
        Severity = ErrorSeverity.Warning;
    }

    protected NetworkException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}

/// <summary>
/// 操作取消异常
/// </summary>
public class OperationCancelledException : TurnedOnTimesViewException
{
    public string OperationName { get; }

    public OperationCancelledException(string operationName, string userMessage = "")
        : base("OPERATION_CANCELLED", string.IsNullOrEmpty(userMessage) ? "操作已取消" : userMessage, $"操作被取消: {operationName}")
    {
        OperationName = operationName;
        IsRetryable = false;
        Severity = ErrorSeverity.Info;
        WithDetail("OperationName", operationName);
    }

    protected OperationCancelledException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        OperationName = info.GetString(nameof(OperationName)) ?? string.Empty;
    }

    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(OperationName), OperationName);
    }
}

/// <summary>
/// 资源不足异常
/// </summary>
public class InsufficientResourceException : TurnedOnTimesViewException
{
    public string ResourceType { get; }
    public long RequiredAmount { get; }
    public long AvailableAmount { get; }

    public InsufficientResourceException(string resourceType, long requiredAmount, long availableAmount, string userMessage)
        : base("INSUFFICIENT_RESOURCE", userMessage, $"资源不足: {resourceType}, 需要: {requiredAmount}, 可用: {availableAmount}")
    {
        ResourceType = resourceType;
        RequiredAmount = requiredAmount;
        AvailableAmount = availableAmount;
        IsRetryable = false;
        Severity = ErrorSeverity.Error;
        
        WithDetail("ResourceType", resourceType)
            .WithDetail("RequiredAmount", requiredAmount)
            .WithDetail("AvailableAmount", availableAmount);
    }

    protected InsufficientResourceException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        ResourceType = info.GetString(nameof(ResourceType)) ?? string.Empty;
        RequiredAmount = info.GetInt64(nameof(RequiredAmount));
        AvailableAmount = info.GetInt64(nameof(AvailableAmount));
    }

    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(ResourceType), ResourceType);
        info.AddValue(nameof(RequiredAmount), RequiredAmount);
        info.AddValue(nameof(AvailableAmount), AvailableAmount);
    }
}

/// <summary>
/// 数据验证异常
/// </summary>
public class DataValidationException : TurnedOnTimesViewException
{
    public List<string> ValidationErrors { get; }

    public DataValidationException(List<string> validationErrors, string userMessage)
        : base("DATA_VALIDATION_ERROR", userMessage, $"数据验证失败: {string.Join(", ", validationErrors)}")
    {
        ValidationErrors = validationErrors;
        IsRetryable = false;
        Severity = ErrorSeverity.Warning;
        WithDetail("ValidationErrors", validationErrors);
    }

    public DataValidationException(string validationError, string userMessage)
        : this(new List<string> { validationError }, userMessage)
    {
    }

    protected DataValidationException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        ValidationErrors = (List<string>)(info.GetValue(nameof(ValidationErrors), typeof(List<string>)) ?? new List<string>());
    }

    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(ValidationErrors), ValidationErrors);
    }
}