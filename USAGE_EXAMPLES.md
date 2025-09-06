# TurnedOnTimesView 多数据源使用示例

## ViewModel集成示例

### 基础集成

```csharp
public class MainViewModel : ViewModelBase
{
    private readonly IDataSourceService _dataSourceService;
    private readonly IEventLogService _eventLogService;
    private readonly ISessionAnalyzer _sessionAnalyzer;
    private readonly ILogger<MainViewModel> _logger;

    // 数据源相关属性
    public DataSourceConfiguration? CurrentDataSource => _dataSourceService.CurrentDataSource;
    public ObservableCollection<string> RecentFiles { get; } = new();
    public IReadOnlyList<DataSourceType> SupportedDataSourceTypes { get; }
    
    // UI绑定属性
    private bool _isLocalSystemSelected = true;
    public bool IsLocalSystemSelected
    {
        get => _isLocalSystemSelected;
        set => SetProperty(ref _isLocalSystemSelected, value);
    }
    
    private string? _selectedEvtxFile;
    public string? SelectedEvtxFile
    {
        get => _selectedEvtxFile;
        set => SetProperty(ref _selectedEvtxFile, value);
    }
    
    // 命令
    public ICommand SelectLocalSystemCommand { get; }
    public ICommand SelectEvtxFileCommand { get; }
    public ICommand BrowseEvtxFileCommand { get; }
    public ICommand LoadDataCommand { get; }

    public MainViewModel(
        IDataSourceService dataSourceService,
        IEventLogService eventLogService,
        ISessionAnalyzer sessionAnalyzer,
        ILogger<MainViewModel> logger)
    {
        _dataSourceService = dataSourceService;
        _eventLogService = eventLogService;
        _sessionAnalyzer = sessionAnalyzer;
        _logger = logger;
        
        SupportedDataSourceTypes = _dataSourceService.GetSupportedDataSourceTypes();
        
        // 初始化命令
        SelectLocalSystemCommand = new RelayCommand(async () => await SelectLocalSystemAsync());
        SelectEvtxFileCommand = new RelayCommand<string>(async file => await SelectEvtxFileAsync(file));
        BrowseEvtxFileCommand = new RelayCommand(async () => await BrowseEvtxFileAsync());
        LoadDataCommand = new RelayCommand(async () => await LoadDataAsync());
        
        // 监听数据源变更
        _dataSourceService.DataSourceChanged += OnDataSourceChanged;
        
        // 初始化
        _ = InitializeAsync();
    }
    
    private async Task InitializeAsync()
    {
        try
        {
            // 加载最近使用的文件
            var recentFiles = await _dataSourceService.GetRecentFilesAsync();
            RecentFiles.Clear();
            foreach (var file in recentFiles)
            {
                RecentFiles.Add(file);
            }
            
            // 设置默认数据源
            var preferences = await _dataSourceService.GetUserPreferencesAsync();
            if (preferences.DefaultDataSourceType == DataSourceType.LocalSystem)
            {
                await SelectLocalSystemAsync();
            }
            
            _logger.LogInformation("ViewModel初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ViewModel初始化失败");
            ShowError("初始化失败", ex.Message);
        }
    }
}
```

### 数据源切换实现

```csharp
public partial class MainViewModel
{
    private async Task SelectLocalSystemAsync()
    {
        try
        {
            SetBusy(true, "切换到本机系统日志...");
            
            var dataSource = _dataSourceService.CreateLocalSystemDataSource();
            var success = await _dataSourceService.SetCurrentDataSourceAsync(dataSource);
            
            if (success)
            {
                IsLocalSystemSelected = true;
                SelectedEvtxFile = null;
                ShowSuccess("已切换到本机系统日志");
                
                // 自动加载数据
                await LoadDataAsync();
            }
            else
            {
                ShowError("切换失败", "无法切换到本机系统日志，请检查权限");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换到本机系统日志失败");
            ShowError("切换失败", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }
    
    private async Task SelectEvtxFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            ShowWarning("文件不存在", $"指定的文件不存在: {filePath}");
            return;
        }
        
        try
        {
            SetBusy(true, "切换到外部文件...");
            
            // 创建数据源配置
            var dataSource = await _dataSourceService.CreateExternalEvtxDataSourceAsync(
                filePath, 
                Path.GetFileNameWithoutExtension(filePath));
            
            // 验证数据源
            var validationResult = await _dataSourceService.ValidateDataSourceAsync(dataSource);
            if (!validationResult.IsValid)
            {
                ShowError("文件验证失败", validationResult.ErrorMessage);
                return;
            }
            
            // 显示警告（如果有）
            if (validationResult.Warnings.Count > 0)
            {
                var warningMessage = string.Join("\n", validationResult.Warnings);
                var continueResult = ShowWarningWithChoice("注意", 
                    $"发现以下警告:\n{warningMessage}\n\n是否继续？");
                if (!continueResult) return;
            }
            
            // 设置数据源
            var success = await _dataSourceService.SetCurrentDataSourceAsync(dataSource);
            if (success)
            {
                IsLocalSystemSelected = false;
                SelectedEvtxFile = filePath;
                
                // 显示文件信息
                var perfInfo = validationResult.PerformanceInfo;
                var message = $"已切换到外部文件\n文件: {Path.GetFileName(filePath)}";
                if (perfInfo != null)
                {
                    message += $"\n估算事件数: {perfInfo.EstimatedEventCount:N0}";
                    message += $"\n文件大小: {perfInfo.DataSizeBytes / (1024 * 1024):F1} MB";
                }
                ShowSuccess(message);
                
                // 自动加载数据
                await LoadDataAsync();
            }
            else
            {
                ShowError("切换失败", "无法切换到指定的.evtx文件");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换到外部文件失败: {FilePath}", filePath);
            ShowError("切换失败", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }
    
    private async Task BrowseEvtxFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Windows事件日志文件 (*.evtx)|*.evtx|所有文件 (*.*)|*.*",
            Title = "选择.evtx文件",
            CheckFileExists = true,
            Multiselect = false
        };
        
        if (dialog.ShowDialog() == true)
        {
            await SelectEvtxFileAsync(dialog.FileName);
        }
    }
}
```

### 数据加载和分析

```csharp
public partial class MainViewModel
{
    private async Task LoadDataAsync()
    {
        if (_dataSourceService.CurrentDataSource == null)
        {
            ShowWarning("请先选择数据源");
            return;
        }
        
        try
        {
            SetBusy(true, "加载数据...");
            
            var startDate = StartDate ?? DateTime.Now.AddDays(-30);
            var endDate = EndDate ?? DateTime.Now;
            
            // 获取事件计数（用于进度显示）
            var totalCount = await _eventLogService.GetEventCountAsync(
                _dataSourceService.CurrentDataSource, 
                startDate, 
                endDate);
            
            if (totalCount == 0)
            {
                ShowInfo("指定时间范围内没有找到相关事件");
                Events.Clear();
                Sessions.Clear();
                return;
            }
            
            UpdateStatus($"找到 {totalCount:N0} 个事件，开始加载...");
            
            // 获取系统事件
            var events = await _eventLogService.GetSystemEventsAsync(
                _dataSourceService.CurrentDataSource,
                startDate,
                endDate);
            
            UpdateStatus($"加载完成，共 {events.Count:N0} 个事件，开始分析会话...");
            
            // 分析会话
            var sessions = await _sessionAnalyzer.AnalyzeSessionsAsync(events);
            
            // 更新UI
            Application.Current.Dispatcher.Invoke(() =>
            {
                Events.Clear();
                foreach (var evt in events)
                {
                    Events.Add(evt);
                }
                
                Sessions.Clear();
                foreach (var session in sessions)
                {
                    Sessions.Add(session);
                }
            });
            
            UpdateStatus($"分析完成，共生成 {sessions.Count} 个会话记录");
            ShowSuccess($"成功加载 {events.Count:N0} 个事件，分析生成 {sessions.Count} 个会话");
            
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("操作已取消");
            ShowInfo("数据加载已取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载数据失败");
            ShowError("加载失败", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }
    
    private void OnDataSourceChanged(object? sender, DataSourceChangedEventArgs e)
    {
        // 在UI线程上更新
        Application.Current.Dispatcher.Invoke(() =>
        {
            OnPropertyChanged(nameof(CurrentDataSource));
            
            // 清除旧数据
            Events.Clear();
            Sessions.Clear();
            
            // 更新UI状态
            var message = e.NewDataSource switch
            {
                LocalSystemDataSource => "已切换到本机系统日志",
                ExternalEvtxDataSource evtx => $"已切换到文件: {Path.GetFileName(evtx.FilePath)}",
                _ => "数据源已更改"
            };
            
            UpdateStatus(message);
        });
    }
}
```

### 用户偏好管理

```csharp
public partial class MainViewModel
{
    private async Task SavePreferencesAsync()
    {
        try
        {
            var preferences = await _dataSourceService.GetUserPreferencesAsync();
            var updatedPreferences = preferences with
            {
                DefaultDataSourceType = IsLocalSystemSelected ? 
                    DataSourceType.LocalSystem : 
                    DataSourceType.ExternalEvtxFile,
                DefaultQueryDays = QueryDays,
                EnableAutoCache = EnableAutoCache
            };
            
            await _dataSourceService.SaveUserPreferencesAsync(updatedPreferences);
            _logger.LogDebug("用户偏好设置已保存");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存用户偏好设置失败");
        }
    }
    
    protected override async void OnClosing(CancelEventArgs e)
    {
        try
        {
            // 保存用户偏好
            await SavePreferencesAsync();
            
            // 清理资源
            _dataSourceService.DataSourceChanged -= OnDataSourceChanged;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭时清理资源失败");
        }
        
        base.OnClosing(e);
    }
}
```

## XAML绑定示例

### 数据源选择界面

```xml
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="*"/>
    </Grid.RowDefinitions>
    
    <!-- 数据源选择 -->
    <GroupBox Header="数据源选择" Grid.Row="0" Margin="5">
        <StackPanel>
            <RadioButton Content="本机系统日志" 
                        IsChecked="{Binding IsLocalSystemSelected}"
                        Command="{Binding SelectLocalSystemCommand}"
                        Margin="5"/>
            
            <RadioButton Content="外部.evtx文件" 
                        IsChecked="{Binding IsLocalSystemSelected, Converter={StaticResource InverseBooleanConverter}}"
                        Margin="5"/>
            
            <StackPanel Orientation="Horizontal" 
                       IsEnabled="{Binding IsLocalSystemSelected, Converter={StaticResource InverseBooleanConverter}}"
                       Margin="20,5">
                <TextBox Text="{Binding SelectedEvtxFile}" 
                        Width="300" 
                        IsReadOnly="True"
                        Margin="0,0,10,0"/>
                <Button Content="浏览..." 
                       Command="{Binding BrowseEvtxFileCommand}"
                       Width="60"/>
            </StackPanel>
        </StackPanel>
    </GroupBox>
    
    <!-- 最近使用的文件 -->
    <GroupBox Header="最近使用的文件" Grid.Row="1" Margin="5"
              Visibility="{Binding RecentFiles.Count, Converter={StaticResource CountToVisibilityConverter}}">
        <ItemsControl ItemsSource="{Binding RecentFiles}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Button Content="{Binding Converter={StaticResource FilePathToNameConverter}}"
                           Command="{Binding DataContext.SelectEvtxFileCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                           CommandParameter="{Binding}"
                           HorizontalAlignment="Left"
                           Margin="2"
                           Style="{StaticResource LinkButtonStyle}"/>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </GroupBox>
    
    <!-- 主要内容区域 -->
    <TabControl Grid.Row="2" Margin="5">
        <TabItem Header="会话记录">
            <DataGrid ItemsSource="{Binding Sessions}"
                     AutoGenerateColumns="False"
                     IsReadOnly="True">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="开始时间" 
                                      Binding="{Binding StartTime, StringFormat=yyyy-MM-dd HH:mm:ss}"/>
                    <DataGridTextColumn Header="结束时间" 
                                      Binding="{Binding EndTime, StringFormat=yyyy-MM-dd HH:mm:ss}"/>
                    <DataGridTextColumn Header="持续时间" 
                                      Binding="{Binding Duration, Converter={StaticResource DurationConverter}}"/>
                    <DataGridTextColumn Header="类型" 
                                      Binding="{Binding Type, Converter={StaticResource ShutdownTypeConverter}}"/>
                    <DataGridTextColumn Header="关机原因" 
                                      Binding="{Binding ShutdownReason}"/>
                </DataGrid.Columns>
            </DataGrid>
        </TabItem>
        
        <TabItem Header="原始事件">
            <DataGrid ItemsSource="{Binding Events}"
                     AutoGenerateColumns="False"
                     IsReadOnly="True">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="时间" 
                                      Binding="{Binding TimeGenerated, StringFormat=yyyy-MM-dd HH:mm:ss}"/>
                    <DataGridTextColumn Header="事件ID" 
                                      Binding="{Binding EventId}"/>
                    <DataGridTextColumn Header="类型" 
                                      Binding="{Binding ShutdownType, Converter={StaticResource ShutdownTypeConverter}}"/>
                    <DataGridTextColumn Header="描述" 
                                      Binding="{Binding DetailedDescription}"/>
                </DataGrid.Columns>
            </DataGrid>
        </TabItem>
    </TabControl>
</Grid>
```

### 状态和进度显示

```xml
<!-- 状态栏 -->
<StatusBar Grid.Row="3">
    <StatusBarItem>
        <TextBlock Text="{Binding CurrentDataSource.DisplayName}" 
                  FontWeight="Bold"/>
    </StatusBarItem>
    <Separator/>
    <StatusBarItem>
        <TextBlock Text="{Binding StatusMessage}"/>
    </StatusBarItem>
    <StatusBarItem HorizontalAlignment="Right">
        <StackPanel Orientation="Horizontal">
            <ProgressBar Width="100" Height="16" 
                        IsIndeterminate="{Binding IsBusy}"
                        Visibility="{Binding IsBusy, Converter={StaticResource BooleanToVisibilityConverter}}"/>
            <TextBlock Text="{Binding BusyMessage}" 
                      Margin="5,0,0,0"
                      Visibility="{Binding IsBusy, Converter={StaticResource BooleanToVisibilityConverter}}"/>
        </StackPanel>
    </StatusBarItem>
</StatusBar>
```

## 错误处理和用户反馈

### 友好的错误处理

```csharp
private void ShowError(string title, string message)
{
    Application.Current.Dispatcher.Invoke(() =>
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    });
}

private void ShowWarning(string title, string message = "")
{
    Application.Current.Dispatcher.Invoke(() =>
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    });
}

private bool ShowWarningWithChoice(string title, string message)
{
    var result = MessageBoxResult.None;
    Application.Current.Dispatcher.Invoke(() =>
    {
        result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
    });
    return result == MessageBoxResult.Yes;
}

private void ShowSuccess(string message)
{
    Application.Current.Dispatcher.Invoke(() =>
    {
        // 可以使用通知气泡或状态栏显示成功消息
        StatusMessage = message;
        
        // 5秒后清除消息
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (s, e) => { StatusMessage = "就绪"; timer.Stop(); };
        timer.Start();
    });
}
```

这些示例展示了如何在实际应用中使用新的多数据源架构，包括完整的用户交互、错误处理和UI绑定。