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
    /// 强制关机
    /// </summary>
    [Description("强制关机")]
    Forced,

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
    /// 系统休眠
    /// </summary>
    [Description("系统休眠")]
    Hibernate,

    /// <summary>
    /// 意外关机/断电
    /// </summary>
    [Description("意外关机")]
    Unexpected,

    /// <summary>
    /// 用户发起的关机/重启
    /// </summary>
    [Description("用户发起")]
    UserInitiated,

    /// <summary>
    /// 系统发起的关机/重启
    /// </summary>
    [Description("系统发起")]
    SystemInitiated,

    /// <summary>
    /// 系统启动
    /// </summary>
    [Description("系统启动")]
    Startup,

    /// <summary>
    /// 从睡眠/休眠唤醒
    /// </summary>
    [Description("系统唤醒")]
    WakeUp,

    /// <summary>
    /// 未知类型
    /// </summary>
    [Description("未知")]
    Unknown
}