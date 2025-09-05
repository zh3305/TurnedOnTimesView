using System;

namespace TurnedOnTimesView.Models
{
    public class BootSession
    {
        public DateTime StartupTime { get; set; }
        public DateTime? ShutdownTime { get; set; }
        public TimeSpan? Duration { get; set; }
        public string? ShutdownReason { get; set; } = string.Empty;
        public string? ShutdownType { get; set; } = string.Empty;
        public string? ShutdownProcess { get; set; } = string.Empty;
        public string? ShutdownCode { get; set; } = string.Empty;
        
        // 新增属性：最后事件时间
        public DateTime LastEventTime { get; set; }
    }
}