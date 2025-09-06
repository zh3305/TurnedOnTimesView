using System.Windows;
using TurnedOnTimesView.ViewModels;

namespace TurnedOnTimesView.Views;

/// <summary>
/// MainWindow.xaml 的交互逻辑
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        // 订阅窗口加载事件
        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
    }

    /// <summary>
    /// 窗口加载完成时初始化数据
    /// </summary>
    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    /// <summary>
    /// 窗口关闭时清理资源
    /// </summary>
    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Dispose();
        }
    }
}