using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TurnedOnTimesView.Models;

namespace TurnedOnTimesView.Services;

/// <summary>
/// 事件日志服务接口，负责从Windows事件日志中读取系统启动和关机事件
/// </summary>
public interface IEventLogService
{
    /// <summary>
    /// 异步获取指定时间范围内的系统事件
    /// </summary>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>系统事件列表</returns>
    Task<IReadOnlyList<SystemEvent>> GetSystemEventsAsync(
        DateTime startDate, 
        DateTime endDate, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步获取最近N天的系统事件
    /// </summary>
    /// <param name="days">天数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>系统事件列表</returns>
    Task<IReadOnlyList<SystemEvent>> GetRecentSystemEventsAsync(
        int days = 30, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查是否有访问事件日志的权限
    /// </summary>
    /// <returns>如果有权限返回 true，否则返回 false</returns>
    bool CanAccessEventLog();

    /// <summary>
    /// 获取系统日志的总条目数（用于进度显示）
    /// </summary>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <returns>日志条目总数</returns>
    Task<long> GetEventCountAsync(DateTime startDate, DateTime endDate);
}