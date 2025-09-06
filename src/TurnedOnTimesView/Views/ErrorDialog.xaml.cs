using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using TurnedOnTimesView.Infrastructure.Exceptions;

namespace TurnedOnTimesView.Views;

/// <summary>
/// 错误对话框的交互逻辑
/// </summary>
public partial class ErrorDialog : Window
{
    private readonly ILogger? _logger;
    private TurnedOnTimesViewException? _exception;
    private Func<Task>? _retryAction;
    private TaskCompletionSource<bool>? _retryTaskSource;

    /// <summary>
    /// 用户是否选择重试
    /// </summary>
    public bool ShouldRetry { get; private set; }

    /// <summary>
    /// 自动重试间隔（毫秒）
    /// </summary>
    public int RetryIntervalMs { get; private set; } = 3000;

    /// <summary>
    /// 是否启用自动重试
    /// </summary>
    public bool AutoRetryEnabled { get; private set; }

    public ErrorDialog(ILogger? logger = null)
    {
        InitializeComponent();
        _logger = logger;
        
        // 设置窗口图标和样式
        SetupWindowAppearance();
    }

    /// <summary>
    /// 显示异常信息
    /// </summary>
    /// <param name="exception">要显示的异常</param>
    /// <param name="retryAction">重试操作（可选）</param>
    public void ShowError(TurnedOnTimesViewException exception, Func<Task>? retryAction = null)
    {
        _exception = exception ?? throw new ArgumentNullException(nameof(exception));
        _retryAction = retryAction;
        
        // 设置窗口标题
        Title = GetWindowTitle(exception.Severity);
        
        // 设置错误信息
        ErrorTitleText.Text = GetErrorTitle(exception);
        ErrorCodeText.Text = $"错误代码: {exception.ErrorCode}";
        ErrorMessageText.Text = exception.UserMessage;
        
        // 设置解决方案
        var solutions = ErrorMessages.GetSolutions(exception.ErrorCode);
        if (solutions.Any())
        {
            SolutionsList.ItemsSource = solutions;
            SolutionsSection.Visibility = Visibility.Visible;
        }
        else
        {
            SolutionsSection.Visibility = Visibility.Collapsed;
        }
        
        // 设置技术详情
        SetTechnicalDetails(exception);
        
        // 设置重试选项
        SetupRetryOptions(exception, retryAction != null);
        
        _logger?.LogDebug("显示错误对话框: {ErrorCode} - {UserMessage}", exception.ErrorCode, exception.UserMessage);
    }

    /// <summary>
    /// 显示一般异常信息
    /// </summary>
    /// <param name="exception">系统异常</param>
    /// <param name="context">错误上下文</param>
    /// <param name="retryAction">重试操作（可选）</param>
    public void ShowError(Exception exception, string context = "", Func<Task>? retryAction = null)
    {
        var appException = ErrorMessages.TranslateException(exception, context);
        ShowError(appException, retryAction);
    }

    /// <summary>
    /// 异步显示错误对话框并等待用户响应
    /// </summary>
    public async Task<bool> ShowErrorAsync(TurnedOnTimesViewException exception, Func<Task>? retryAction = null)
    {
        ShowError(exception, retryAction);
        
        _retryTaskSource = new TaskCompletionSource<bool>();
        
        Show();
        
        return await _retryTaskSource.Task;
    }

    private void SetupWindowAppearance()
    {
        // 设置窗口居中
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        
        // 如果没有拥有者，居中显示在屏幕上
        if (Owner == null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private string GetWindowTitle(ErrorSeverity severity)
    {
        return severity switch
        {
            ErrorSeverity.Critical => "严重错误",
            ErrorSeverity.Error => "错误",
            ErrorSeverity.Warning => "警告",
            ErrorSeverity.Info => "信息",
            _ => "错误提示"
        };
    }

    private string GetErrorTitle(TurnedOnTimesViewException exception)
    {
        return exception.Severity switch
        {
            ErrorSeverity.Critical => "系统发生严重错误",
            ErrorSeverity.Error => "操作执行失败",
            ErrorSeverity.Warning => "操作完成但存在问题",
            ErrorSeverity.Info => "操作信息",
            _ => "发生错误"
        };
    }

    private void SetTechnicalDetails(TurnedOnTimesViewException exception)
    {
        var details = new List<string>
        {
            $"错误类型: {exception.GetType().Name}",
            $"错误消息: {exception.Message}"
        };

        if (exception.Details.Any())
        {
            details.Add("错误详情:");
            foreach (var detail in exception.Details)
            {
                details.Add($"  {detail.Key}: {detail.Value}");
            }
        }

        if (exception.InnerException != null)
        {
            details.Add($"内部异常: {exception.InnerException.GetType().Name}");
            details.Add($"内部异常消息: {exception.InnerException.Message}");
        }

        details.Add($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        TechnicalDetailsText.Text = string.Join("\n", details);
    }

    private void SetupRetryOptions(TurnedOnTimesViewException exception, bool hasRetryAction)
    {
        if (exception.IsRetryable && hasRetryAction)
        {
            RetrySection.Visibility = Visibility.Visible;
            RetryButton.Visibility = Visibility.Visible;
        }
        else
        {
            RetrySection.Visibility = Visibility.Collapsed;
            RetryButton.Visibility = Visibility.Collapsed;
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var errorInfo = new List<string>
            {
                $"错误标题: {ErrorTitleText.Text}",
                $"错误代码: {_exception?.ErrorCode}",
                $"用户消息: {ErrorMessageText.Text}",
                "",
                "技术详情:",
                TechnicalDetailsText.Text
            };

            var solutions = SolutionsList.ItemsSource as IEnumerable<string>;
            if (solutions?.Any() == true)
            {
                errorInfo.Add("");
                errorInfo.Add("解决方案:");
                errorInfo.AddRange(solutions.Select(s => $"• {s}"));
            }

            Clipboard.SetText(string.Join("\n", errorInfo));
            
            // 临时改变按钮文本提示复制成功
            var originalContent = CopyButton.Content;
            CopyButton.Content = "已复制";
            CopyButton.IsEnabled = false;
            
            _ = Task.Delay(2000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    CopyButton.Content = originalContent;
                    CopyButton.IsEnabled = true;
                });
            });

            _logger?.LogDebug("错误信息已复制到剪贴板");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "复制错误信息到剪贴板失败");
        }
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        ShouldRetry = true;
        AutoRetryEnabled = AutoRetryCheckBox.IsChecked == true;
        
        if (RetryIntervalComboBox.SelectedItem is ComboBoxItem selectedItem && 
            selectedItem.Tag is string tagValue && 
            int.TryParse(tagValue, out int interval))
        {
            RetryIntervalMs = interval;
        }

        _logger?.LogInformation("用户选择重试操作，自动重试: {AutoRetry}, 间隔: {Interval}ms", 
            AutoRetryEnabled, RetryIntervalMs);

        if (_retryAction != null)
        {
            try
            {
                // 禁用重试按钮防止重复点击
                RetryButton.IsEnabled = false;
                RetryButton.Content = "重试中...";
                
                await _retryAction();
                
                // 如果重试成功，关闭对话框
                Close();
                _retryTaskSource?.SetResult(true);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "重试操作失败");
                
                // 重新显示错误（可能是新的错误）
                if (ex is TurnedOnTimesViewException appEx)
                {
                    ShowError(appEx, _retryAction);
                }
                else
                {
                    ShowError(ErrorMessages.TranslateException(ex), _retryAction);
                }
            }
            finally
            {
                RetryButton.IsEnabled = true;
                RetryButton.Content = "重试";
            }
        }
        else
        {
            Close();
            _retryTaskSource?.SetResult(true);
        }
    }

    private void OKButton_Click(object sender, RoutedEventArgs e)
    {
        ShouldRetry = false;
        _logger?.LogDebug("用户确认错误对话框");
        Close();
        _retryTaskSource?.SetResult(false);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _retryTaskSource?.TrySetResult(ShouldRetry);
    }
}

/// <summary>
/// 错误对话框服务
/// </summary>
public static class ErrorDialogService
{
    /// <summary>
    /// 显示错误对话框
    /// </summary>
    /// <param name="owner">父窗口</param>
    /// <param name="exception">要显示的异常</param>
    /// <param name="retryAction">重试操作（可选）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <returns>用户是否选择重试</returns>
    public static bool ShowError(
        Window? owner, 
        TurnedOnTimesViewException exception, 
        Func<Task>? retryAction = null,
        ILogger? logger = null)
    {
        var dialog = new ErrorDialog(logger)
        {
            Owner = owner
        };
        
        dialog.ShowError(exception, retryAction);
        dialog.ShowDialog();
        
        return dialog.ShouldRetry;
    }

    /// <summary>
    /// 显示系统异常的错误对话框
    /// </summary>
    /// <param name="owner">父窗口</param>
    /// <param name="exception">系统异常</param>
    /// <param name="context">错误上下文</param>
    /// <param name="retryAction">重试操作（可选）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <returns>用户是否选择重试</returns>
    public static bool ShowError(
        Window? owner, 
        Exception exception, 
        string context = "",
        Func<Task>? retryAction = null,
        ILogger? logger = null)
    {
        var appException = ErrorMessages.TranslateException(exception, context);
        return ShowError(owner, appException, retryAction, logger);
    }

    /// <summary>
    /// 异步显示错误对话框
    /// </summary>
    /// <param name="owner">父窗口</param>
    /// <param name="exception">要显示的异常</param>
    /// <param name="retryAction">重试操作（可选）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <returns>用户是否选择重试</returns>
    public static async Task<bool> ShowErrorAsync(
        Window? owner, 
        TurnedOnTimesViewException exception, 
        Func<Task>? retryAction = null,
        ILogger? logger = null)
    {
        var dialog = new ErrorDialog(logger)
        {
            Owner = owner
        };
        
        return await dialog.ShowErrorAsync(exception, retryAction);
    }
}