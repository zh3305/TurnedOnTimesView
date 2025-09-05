using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using TurnedOnTimesView.Models;

namespace TurnedOnTimesView.Services
{
    public class EventLogService
    {
        // 检索所有相关的系统事件
        public List<EventRecord> GetSystemEvents(DateTime startDate, DateTime endDate)
        {
            List<EventRecord> events = new List<EventRecord>();

            // 构建XPath查询，筛选关键事件ID
            string query = $@"*[System[TimeCreated[@SystemTime>='{startDate:yyyy-MM-ddTHH:mm:ss.000Z}' and @SystemTime<='{endDate:yyyy-MM-ddTHH:mm:ss.999Z}'] and (EventID=6005 or EventID=6006 or EventID=6008 or EventID=6009 or EventID=1074 or EventID=41 or EventID=42 or EventID=1)]]";

            try
            {
                EventLogQuery eventsQuery = new EventLogQuery("System", PathType.LogName, query);
                using (EventLogReader logReader = new EventLogReader(eventsQuery))
                {
                    for (EventRecord eventRecord = logReader.ReadEvent(); eventRecord != null; eventRecord = logReader.ReadEvent())
                    {
                        events.Add(eventRecord);
                    }
                }
            }
            catch (EventLogNotFoundException ex)
            {
                // 处理日志未找到的异常
                Console.WriteLine($"事件日志未找到: {ex.Message}");
            }
            catch (Exception ex)
            {
                // 处理其他异常
                Console.WriteLine($"读取事件日志时出错: {ex.Message}");
            }

            // 按时间戳升序排序
            return events.OrderBy(e => e.TimeCreated).ToList();
        }

        // 解析事件记录，生成会话列表
        public List<BootSession> ProcessEvents(List<EventRecord> events)
        {
            List<BootSession> sessions = new List<BootSession>();
            BootSession? currentSession = null;

            foreach (var eventRecord in events)
            {
                int eventId = eventRecord.Id;
                DateTime eventTime = eventRecord.TimeCreated ?? DateTime.Now;

                switch (eventId)
                {
                    case 6005: // 系统启动
                    case 6009: // 系统启动（详细信息）
                        // 如果已有活动会话，说明上一个会话是非正常结束
                        if (currentSession != null)
                        {
                            // 标记为意外关机
                            currentSession.ShutdownTime = eventTime;
                            currentSession.ShutdownType = "意外关机";
                            currentSession.ShutdownReason = "系统未正常关闭";
                            currentSession.Duration = currentSession.ShutdownTime.Value - currentSession.StartupTime;
                            currentSession.LastEventTime = eventTime; // 更新最后事件时间
                            sessions.Add(currentSession);
                        }

                        // 创建新会话
                        currentSession = new BootSession
                        {
                            StartupTime = eventTime,
                            LastEventTime = eventTime // 初始化最后事件时间
                        };
                        break;

                    case 6006: // 正常关机
                        if (currentSession != null)
                        {
                            currentSession.ShutdownTime = eventTime;
                            currentSession.ShutdownType = "正常关机";
                            currentSession.ShutdownReason = "事件日志服务已停止";
                            currentSession.Duration = eventTime - currentSession.StartupTime;
                            currentSession.LastEventTime = eventTime; // 更新最后事件时间
                            sessions.Add(currentSession);
                            currentSession = null;
                        }
                        break;

                    case 1074: // 用户或应用程序发起的关机/重启
                        if (currentSession != null)
                        {
                            currentSession.ShutdownTime = eventTime;
                            
                            // 解析关机原因和进程
                            string reason = "用户发起关机";
                            string process = "未知";
                            
                            try
                            {
                                if (eventRecord.Properties.Count >= 4)
                                {
                                    reason = eventRecord.Properties[2].Value?.ToString() ?? "未知原因";
                                    process = eventRecord.Properties[3].Value?.ToString() ?? "未知进程";
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"解析事件属性时出错: {ex.Message}");
                            }

                            currentSession.ShutdownType = "正常关机";
                            currentSession.ShutdownReason = reason;
                            currentSession.ShutdownProcess = process;
                            currentSession.Duration = eventTime - currentSession.StartupTime;
                            currentSession.LastEventTime = eventTime; // 更新最后事件时间
                            sessions.Add(currentSession);
                            currentSession = null;
                        }
                        break;

                    case 6008: // 非正常关机
                        // 这个事件通常在系统启动时记录，表明上一次关机是非正常的
                        // 如果当前没有活动会话，可能是因为我们刚开始处理事件
                        // 如果有活动会话，可以更新其关机类型
                        if (currentSession != null)
                        {
                            currentSession.ShutdownType = "意外关机";
                            currentSession.ShutdownReason = "上次系统关闭不正常";
                            currentSession.LastEventTime = eventTime; // 更新最后事件时间
                        }
                        break;

                    case 41: // 系统意外重启
                        if (currentSession != null)
                        {
                            currentSession.ShutdownType = "意外关机";
                            currentSession.ShutdownReason = "系统未干净关机就重启";
                            currentSession.LastEventTime = eventTime; // 更新最后事件时间
                        }
                        break;

                    case 42: // 进入睡眠状态
                        if (currentSession != null)
                        {
                            currentSession.ShutdownTime = eventTime;
                            currentSession.ShutdownType = "睡眠";
                            currentSession.ShutdownReason = "系统进入睡眠状态";
                            currentSession.Duration = eventTime - currentSession.StartupTime;
                            currentSession.LastEventTime = eventTime; // 更新最后事件时间
                            sessions.Add(currentSession);
                            currentSession = null;
                        }
                        break;

                    case 1: // 从睡眠中唤醒
                        // 这里不创建新会话，等待6005启动事件
                        break;

                    default:
                        // 对于其他事件，如果有活动会话，更新最后事件时间
                        if (currentSession != null)
                        {
                            currentSession.LastEventTime = eventTime;
                        }
                        break;
                }
            }

            // 处理最后一个可能未关闭的会话
            if (currentSession != null)
            {
                currentSession.ShutdownTime = DateTime.Now;
                currentSession.ShutdownType = "当前会话";
                currentSession.ShutdownReason = "系统仍在运行";
                currentSession.Duration = DateTime.Now - currentSession.StartupTime;
                sessions.Add(currentSession);
            }

            return sessions;
        }
    }
}