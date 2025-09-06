using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TurnedOnTimesView.Models;

namespace TurnedOnTimesView.Services;

/// <summary>
/// 事件日志服务接口，支持从多种数据源读取Windows事件日志
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

    /// <summary>
    /// 从.evtx文件异步获取指定时间范围内的系统事件
    /// </summary>
    /// <param name="evtxFilePath">.evtx文件路径</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>系统事件列表</returns>
    Task<IReadOnlyList<SystemEvent>> GetSystemEventsFromFileAsync(
        string evtxFilePath,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证.evtx文件是否可以正常读取
    /// </summary>
    /// <param name="evtxFilePath">.evtx文件路径</param>
    /// <returns>如果文件有效且可读取返回 true，否则返回 false</returns>
    bool CanReadEvtxFile(string evtxFilePath);
    
    /// <summary>
    /// 异步验证.evtx文件并获取详细信息
    /// </summary>
    /// <param name="evtxFilePath">.evtx文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文件验证结果</returns>
    Task<FileValidationResult> ValidateEvtxFileAsync(string evtxFilePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 使用指定数据源配置获取系统事件
    /// </summary>
    /// <param name="dataSource">数据源配置</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>系统事件列表</returns>
    Task<IReadOnlyList<SystemEvent>> GetSystemEventsAsync(
        DataSourceConfiguration dataSource,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取数据源的事件计数
    /// </summary>
    /// <param name="dataSource">数据源配置</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>事件计数</returns>
    Task<long> GetEventCountAsync(
        DataSourceConfiguration dataSource,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default);
}