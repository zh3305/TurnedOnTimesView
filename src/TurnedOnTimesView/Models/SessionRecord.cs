using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace TurnedOnTimesView.Models;

/// <summary>
/// 表示一个系统会话记录，包含开机到关机的完整信息
/// 优化了内存使用和性能
/// </summary>
[DataContract]
public sealed class SessionRecord : INotifyPropertyChanged
{
    private DateTime _startTime;
    private DateTime? _endTime;
    private TimeSpan _duration;
    private string? _shutdownReason;
    private ShutdownType _type;
    private string? _process;
    private DateTime _lastEventTime;
    
    // 缓存字段以提高性能
    private string? _formattedDurationCache;
    private string? _statusTextCache;
    private bool _isDurationCacheValid;
    private bool _isStatusCacheValid;

    /// <summary>
    /// 系统启动时间
    /// </summary>
    [DataMember]
    public DateTime StartTime
    {
        get => _startTime;
        set => SetProperty(ref _startTime, value);
    }

    /// <summary>
    /// 系统关机时间（可能为空，表示仍在运行）
    /// </summary>
    [DataMember]
    public DateTime? EndTime
    {
        get => _endTime;
        set
        {
            if (SetProperty(ref _endTime, value))
            {
                InvalidateCache();
                UpdateDuration();
            }
        }
    }

    /// <summary>
    /// 会话持续时间
    /// </summary>
    [DataMember]
    public TimeSpan Duration
    {
        get => _duration;
        private set
        {
            if (SetProperty(ref _duration, value))
            {
                _isDurationCacheValid = false;
            }
        }
    }

    /// <summary>
    /// 关机原因描述
    /// </summary>
    [DataMember]
    public string ShutdownReason
    {
        get => _shutdownReason ?? string.Empty;
        set => SetProperty(ref _shutdownReason, string.IsNullOrEmpty(value) ? null : value);
    }

    /// <summary>
    /// 关机类型
    /// </summary>
    [DataMember]
    public ShutdownType Type
    {
        get => _type;
        set
        {
            if (SetProperty(ref _type, value))
            {
                _isStatusCacheValid = false;
            }
        }
    }

    /// <summary>
    /// 执行关机的进程名称
    /// </summary>
    [DataMember]
    public string Process
    {
        get => _process ?? string.Empty;
        set => SetProperty(ref _process, string.IsNullOrEmpty(value) ? null : value);
    }

    /// <summary>
    /// 最后事件发生的时间戳（新增字段）
    /// 用于记录该会话中最新的事件时间，有助于数据排序和分析
    /// </summary>
    [DataMember]
    public DateTime LastEventTime
    {
        get => _lastEventTime;
        set => SetProperty(ref _lastEventTime, value);
    }

    /// <summary>
    /// 获取格式化的持续时间字符串（使用缓存优化）
    /// </summary>
    public string FormattedDuration
    {
        get
        {
            if (!_isDurationCacheValid || _formattedDurationCache == null)
            {
                _formattedDurationCache = Duration.TotalDays >= 1 
                    ? $"{Duration.Days}天 {Duration.Hours:D2}:{Duration.Minutes:D2}:{Duration.Seconds:D2}"
                    : $"{Duration.Hours:D2}:{Duration.Minutes:D2}:{Duration.Seconds:D2}";
                _isDurationCacheValid = true;
            }
            return _formattedDurationCache;
        }
    }

    /// <summary>
    /// 是否为当前会话（未结束）
    /// </summary>
    public bool IsCurrentSession => !EndTime.HasValue;

    /// <summary>
    /// 获取状态描述文本（使用缓存优化）
    /// </summary>
    public string StatusText
    {
        get
        {
            if (!_isStatusCacheValid || _statusTextCache == null)
            {
                _statusTextCache = IsCurrentSession ? "运行中" : "已结束";
                _isStatusCacheValid = true;
            }
            return _statusTextCache;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 更新持续时间
    /// </summary>
    private void UpdateDuration()
    {
        var endTime = EndTime ?? DateTime.Now;
        Duration = endTime - StartTime;
    }

    /// <summary>
    /// 失效缓存
    /// </summary>
    private void InvalidateCache()
    {
        _isDurationCacheValid = false;
        _isStatusCacheValid = false;
        _formattedDurationCache = null;
        _statusTextCache = null;
    }

    /// <summary>
    /// 属性更改通知辅助方法（优化版）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// 触发属性更改事件（优化版）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 重写 ToString 方法，提供有意义的字符串表示
    /// </summary>
    public override string ToString()
    {
        var status = IsCurrentSession ? "运行中" : $"已结束于 {EndTime:yyyy-MM-dd HH:mm:ss}";
        return $"会话: {StartTime:yyyy-MM-dd HH:mm:ss} - {status} ({Type})";
    }

    /// <summary>
    /// 创建当前运行会话的实例
    /// </summary>
    public static SessionRecord CreateCurrentSession(DateTime startTime, DateTime lastEventTime)
    {
        return new SessionRecord
        {
            StartTime = startTime,
            LastEventTime = lastEventTime,
            Type = ShutdownType.Unknown,
            ShutdownReason = "系统正在运行",
            Process = "N/A"
        };
    }

    /// <summary>
    /// 创建已完成会话的实例
    /// </summary>
    public static SessionRecord CreateCompletedSession(
        DateTime startTime, 
        DateTime endTime, 
        DateTime lastEventTime,
        ShutdownType type, 
        string shutdownReason, 
        string process)
    {
        return new SessionRecord
        {
            StartTime = startTime,
            EndTime = endTime,
            LastEventTime = lastEventTime,
            Type = type,
            ShutdownReason = shutdownReason,
            Process = process
        };
    }
}