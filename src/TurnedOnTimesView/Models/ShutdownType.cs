using System.ComponentModel;

namespace TurnedOnTimesView.Models;

/// <summary>
/// 表示系统关机类型的枚举
/// </summary>
public enum ShutdownType
{
    /// <summary>
    /// 正常关机
    /// </summary>
    [Description("正常关机")]
    Normal,

    /// <summary>
    /// 异常关机/意外断电
    /// </summary>
    [Description("异常关机")]
    Abnormal,

    /// <summary>
    /// 系统重启
    /// </summary>
    [Description("系统重启")]
    Restart,

    /// <summary>
    /// 系统睡眠
    /// </summary>
    [Description("系统睡眠")]
    Sleep,

    /// <summary>
    /// 未知类型
    /// </summary>
    [Description("未知")]
    Unknown
}