using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Serilog;
using TurnedOnTimesView.Models;
using TurnedOnTimesView.Services;

namespace TurnedOnTimesView.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly EventLogService _eventLogService;
        private ObservableCollection<BootSession> _bootSessions;
        private DateTime _startDate;
        private DateTime _endDate;
        private string _shutdownType;
        private bool _isLoading;
        private int _totalSessions;
        private int _normalShutdowns;
        private int _abnormalShutdowns;
        private int _sleepCycles;
        private string _totalRuntime;

        public MainViewModel()
        {
            _eventLogService = new EventLogService();
            _bootSessions = new ObservableCollection<BootSession>();
            Log.Debug("MainViewModel 初始化");
            _startDate = DateTime.Now.AddMonths(-1);
            _endDate = DateTime.Now;
            _shutdownType = "all";
            
            RefreshCommand = new RelayCommand(_ => LoadData());
            ExportCommand = new RelayCommand(_ => ExportData());
            
            // 初始加载数据
            LoadData();
        }

        public ObservableCollection<BootSession> BootSessions
        {
            get => _bootSessions;
            set => SetProperty(ref _bootSessions, value);
        }

        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                if (SetProperty(ref _startDate, value))
                {
                    LoadData();
                }
            }
        }

        public DateTime EndDate
        {
            get => _endDate;
            set
            {
                if (SetProperty(ref _endDate, value))
                {
                    LoadData();
                }
            }
        }

        public string ShutdownType
        {
            get => _shutdownType;
            set
            {
                if (SetProperty(ref _shutdownType, value))
                {
                    FilterData();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public int TotalSessions
        {
            get => _totalSessions;
            set => SetProperty(ref _totalSessions, value);
        }

        public int NormalShutdowns
        {
            get => _normalShutdowns;
            set => SetProperty(ref _normalShutdowns, value);
        }

        public int AbnormalShutdowns
        {
            get => _abnormalShutdowns;
            set => SetProperty(ref _abnormalShutdowns, value);
        }

        public int SleepCycles
        {
            get => _sleepCycles;
            set => SetProperty(ref _sleepCycles, value);
        }

        public string TotalRuntime
        {
            get => _totalRuntime;
            set => SetProperty(ref _totalRuntime, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }

        private void LoadData()
        {
            IsLoading = true;
            Log.Information("开始加载数据，时间范围: {StartDate} 至 {EndDate}", StartDate, EndDate);
            
            try
            {
                // 在后台线程中加载数据
                Task.Run(() =>
                {
                    var events = _eventLogService.GetSystemEvents(StartDate, EndDate);
                    var sessions = _eventLogService.ProcessEvents(events);

                    // 在UI线程中更新ObservableCollection
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        BootSessions.Clear();
                        // 按启动时间降序排序
                        var sortedSessions = sessions
                            .OrderByDescending(s => s.StartupTime)
                            .ToList();

                        foreach (var session in sortedSessions)
                        {
                            BootSessions.Add(session);
                        }

                        UpdateStatistics();
                        IsLoading = false;
                        Log.Information("数据加载完成，共加载 {Count} 条记录", BootSessions.Count);
                    });
                });
            }
            catch (Exception ex)
            {
                // 处理异常
                Log.Error(ex, "加载数据时出错");
                IsLoading = false;
            }
        }

        private void FilterData()
        {
            if (ShutdownType == "all")
            {
                // 不需要筛选，显示所有数据
                return;
            }

            var filteredSessions = BootSessions.Where(s => 
                (ShutdownType == "normal" && s.ShutdownType.Contains("正常")) ||
                (ShutdownType == "abnormal" && s.ShutdownType.Contains("意外")) ||
                (ShutdownType == "sleep" && s.ShutdownType.Contains("睡眠"))
            ).ToList();

            // 更新统计信息
            UpdateStatistics(filteredSessions);
        }

        private void UpdateStatistics(IEnumerable<BootSession>? sessions = null)
        {
            sessions ??= BootSessions;

            TotalSessions = sessions.Count();
            NormalShutdowns = sessions.Count(s => s.ShutdownType.Contains("正常"));
            AbnormalShutdowns = sessions.Count(s => s.ShutdownType.Contains("意外"));
            SleepCycles = sessions.Count(s => s.ShutdownType.Contains("睡眠"));

            // 计算总运行时间
            TimeSpan totalDuration = TimeSpan.Zero;
            foreach (var session in sessions)
            {
                if (session.Duration.HasValue)
                {
                    totalDuration += session.Duration.Value;
                }
            }

            // 格式化总运行时间
            if (totalDuration.TotalDays >= 1)
            {
                TotalRuntime = $"{(int)totalDuration.TotalDays}天 {totalDuration.Hours}小时";
            }
            else
            {
                TotalRuntime = $"{(int)totalDuration.TotalHours}小时";
            }
        }

        private void ExportData()
        {
            // 导出功能实现
            // 这里可以添加导出到CSV、HTML或XML的代码
            Console.WriteLine("导出功能开发中...");
        }
    }
}