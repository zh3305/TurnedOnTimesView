using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TurnedOnTimesView.Infrastructure.Exceptions;

namespace TurnedOnTimesView.Infrastructure.Resilience;

/// <summary>
/// 重试策略配置
/// </summary>
public class RetryOptions
{
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;
    
    /// <summary>
    /// 基础延迟时间（毫秒）
    /// </summary>
    public int BaseDelayMs { get; set; } = 1000;
    
    /// <summary>
    /// 延迟增长因子
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;
    
    /// <summary>
    /// 最大延迟时间（毫秒）
    /// </summary>
    public int MaxDelayMs { get; set; } = 30000;
    
    /// <summary>
    /// 是否启用抖动
    /// </summary>
    public bool EnableJitter { get; set; } = true;
    
    /// <summary>
    /// 重试前的回调
    /// </summary>
    public Action<int, Exception>? OnRetrying { get; set; }
}

/// <summary>
/// 操作进度信息
/// </summary>
public class OperationProgress
{
    /// <summary>
    /// 当前步骤
    /// </summary>
    public string CurrentStep { get; set; } = string.Empty;
    
    /// <summary>
    /// 进度百分比 (0-100)
    /// </summary>
    public int PercentComplete { get; set; }
    
    /// <summary>
    /// 已处理项目数
    /// </summary>
    public long ItemsProcessed { get; set; }
    
    /// <summary>
    /// 总项目数
    /// </summary>
    public long TotalItems { get; set; }
    
    /// <summary>
    /// 是否可以取消
    /// </summary>
    public bool IsCancellable { get; set; } = true;
    
    /// <summary>
    /// 附加信息
    /// </summary>
    public string AdditionalInfo { get; set; } = string.Empty;
    
    /// <summary>
    /// 估算剩余时间
    /// </summary>
    public TimeSpan? EstimatedRemainingTime { get; set; }
}

/// <summary>
/// 重试策略执行器
/// </summary>
public class RetryPolicy
{
    private readonly RetryOptions _options;
    private readonly ILogger? _logger;
    private readonly Random _random;

    public RetryPolicy(RetryOptions options, ILogger? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _random = new Random();
    }

    /// <summary>
    /// 执行带重试的异步操作
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="operation">要执行的操作</param>
    /// <param name="shouldRetry">判断是否应该重试的函数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<Exception, bool>? shouldRetry = null,
        CancellationToken cancellationToken = default)
    {
        Exception? lastException = null;
        var attempt = 0;

        while (attempt <= _options.MaxRetries)
        {
            try
            {
                _logger?.LogDebug("执行操作，尝试次数: {Attempt}/{MaxRetries}", attempt + 1, _options.MaxRetries + 1);
                
                return await operation(cancellationToken);
            }
            catch (Exception ex) when (attempt < _options.MaxRetries)
            {
                lastException = ex;
                
                // 检查是否应该重试
                if (!ShouldRetry(ex, shouldRetry))
                {
                    _logger?.LogWarning("异常不适合重试: {ExceptionType} - {Message}", ex.GetType().Name, ex.Message);
                    throw;
                }

                attempt++;
                var delay = CalculateDelay(attempt);
                
                _logger?.LogWarning("操作失败，将在 {Delay}ms 后重试 (尝试 {Attempt}/{MaxRetries}): {Exception}",
                    delay, attempt + 1, _options.MaxRetries + 1, ex.Message);
                
                _options.OnRetrying?.Invoke(attempt, ex);
                
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _logger?.LogInformation("重试被取消");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "操作执行失败，不再重试");
                throw;
            }
        }

        // 如果到了这里，说明所有重试都失败了
        _logger?.LogError("操作失败，已达到最大重试次数 {MaxRetries}", _options.MaxRetries);
        throw lastException ?? new InvalidOperationException("操作失败且未捕获到异常信息");
    }

    /// <summary>
    /// 执行带进度报告的异步操作
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="operation">要执行的操作</param>
    /// <param name="progressCallback">进度回调</param>
    /// <param name="shouldRetry">判断是否应该重试的函数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public async Task<T> ExecuteWithProgressAsync<T>(
        Func<IProgress<OperationProgress>, CancellationToken, Task<T>> operation,
        IProgress<OperationProgress>? progressCallback = null,
        Func<Exception, bool>? shouldRetry = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        Exception? lastException = null;

        while (attempt <= _options.MaxRetries)
        {
            try
            {
                progressCallback?.Report(new OperationProgress
                {
                    CurrentStep = attempt == 0 ? "开始执行操作" : $"重试操作 (第 {attempt} 次)",
                    PercentComplete = 0
                });

                return await operation(progressCallback, cancellationToken);
            }
            catch (Exception ex) when (attempt < _options.MaxRetries)
            {
                lastException = ex;
                
                if (!ShouldRetry(ex, shouldRetry))
                {
                    throw;
                }

                attempt++;
                var delay = CalculateDelay(attempt);
                
                progressCallback?.Report(new OperationProgress
                {
                    CurrentStep = $"操作失败，{delay}ms 后重试",
                    PercentComplete = 0,
                    AdditionalInfo = $"错误: {ex.Message}"
                });
                
                _options.OnRetrying?.Invoke(attempt, ex);
                
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    progressCallback?.Report(new OperationProgress
                    {
                        CurrentStep = "操作已取消",
                        PercentComplete = 0
                    });
                    throw;
                }
            }
            catch (Exception ex)
            {
                progressCallback?.Report(new OperationProgress
                {
                    CurrentStep = "操作失败",
                    PercentComplete = 0,
                    AdditionalInfo = $"错误: {ex.Message}"
                });
                throw;
            }
        }

        progressCallback?.Report(new OperationProgress
        {
            CurrentStep = "操作失败，已达到最大重试次数",
            PercentComplete = 0,
            AdditionalInfo = $"最后错误: {lastException?.Message}"
        });

        throw lastException ?? new InvalidOperationException("操作失败且未捕获到异常信息");
    }

    /// <summary>
    /// 判断异常是否应该重试
    /// </summary>
    private bool ShouldRetry(Exception exception, Func<Exception, bool>? shouldRetryFunc)
    {
        // 用户自定义的重试判断
        if (shouldRetryFunc != null)
        {
            return shouldRetryFunc(exception);
        }

        // 默认重试规则
        return exception switch
        {
            TurnedOnTimesViewException appEx => appEx.IsRetryable,
            TimeoutException => true,
            IOException => true,
            System.Net.Sockets.SocketException => true,
            System.Net.NetworkInformation.NetworkInformationException => true,
            System.Diagnostics.Eventing.Reader.EventLogException => true,
            OperationCanceledException => false,
            ArgumentException => false,
            ArgumentNullException => false,
            NotSupportedException => false,
            UnauthorizedAccessException => false,
            SecurityException => false,
            _ => false
        };
    }

    /// <summary>
    /// 计算延迟时间
    /// </summary>
    private int CalculateDelay(int attempt)
    {
        // 指数退避算法
        var delay = (int)(_options.BaseDelayMs * Math.Pow(_options.BackoffMultiplier, attempt - 1));
        
        // 限制最大延迟
        delay = Math.Min(delay, _options.MaxDelayMs);
        
        // 添加抖动减少雷鸣群效应
        if (_options.EnableJitter)
        {
            var jitter = _random.Next(0, (int)(delay * 0.1)); // 10%的随机抖动
            delay += jitter;
        }
        
        return delay;
    }
}

/// <summary>
/// 重试策略构建器
/// </summary>
public class RetryPolicyBuilder
{
    private readonly RetryOptions _options = new();
    private ILogger? _logger;

    /// <summary>
    /// 设置最大重试次数
    /// </summary>
    public RetryPolicyBuilder WithMaxRetries(int maxRetries)
    {
        _options.MaxRetries = maxRetries;
        return this;
    }

    /// <summary>
    /// 设置基础延迟时间
    /// </summary>
    public RetryPolicyBuilder WithBaseDelay(int baseDelayMs)
    {
        _options.BaseDelayMs = baseDelayMs;
        return this;
    }

    /// <summary>
    /// 设置指数退避参数
    /// </summary>
    public RetryPolicyBuilder WithExponentialBackoff(double multiplier = 2.0, int maxDelayMs = 30000)
    {
        _options.BackoffMultiplier = multiplier;
        _options.MaxDelayMs = maxDelayMs;
        return this;
    }

    /// <summary>
    /// 启用抖动
    /// </summary>
    public RetryPolicyBuilder WithJitter(bool enable = true)
    {
        _options.EnableJitter = enable;
        return this;
    }

    /// <summary>
    /// 设置重试回调
    /// </summary>
    public RetryPolicyBuilder OnRetrying(Action<int, Exception> callback)
    {
        _options.OnRetrying = callback;
        return this;
    }

    /// <summary>
    /// 设置日志记录器
    /// </summary>
    public RetryPolicyBuilder WithLogger(ILogger logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// 构建重试策略
    /// </summary>
    public RetryPolicy Build()
    {
        return new RetryPolicy(_options, _logger);
    }
}

/// <summary>
/// 预定义的重试策略
/// </summary>
public static class RetryPolicies
{
    /// <summary>
    /// 用于文件操作的重试策略
    /// </summary>
    public static RetryPolicy FileOperations(ILogger? logger = null) =>
        new RetryPolicyBuilder()
            .WithMaxRetries(3)
            .WithBaseDelay(1000)
            .WithExponentialBackoff(1.5, 10000)
            .WithJitter()
            .WithLogger(logger)
            .Build();

    /// <summary>
    /// 用于网络操作的重试策略
    /// </summary>
    public static RetryPolicy NetworkOperations(ILogger? logger = null) =>
        new RetryPolicyBuilder()
            .WithMaxRetries(5)
            .WithBaseDelay(2000)
            .WithExponentialBackoff(2.0, 30000)
            .WithJitter()
            .WithLogger(logger)
            .Build();

    /// <summary>
    /// 用于系统服务的重试策略
    /// </summary>
    public static RetryPolicy SystemServices(ILogger? logger = null) =>
        new RetryPolicyBuilder()
            .WithMaxRetries(3)
            .WithBaseDelay(500)
            .WithExponentialBackoff(1.5, 5000)
            .WithJitter()
            .WithLogger(logger)
            .Build();

    /// <summary>
    /// 快速重试策略（用于轻量级操作）
    /// </summary>
    public static RetryPolicy FastRetry(ILogger? logger = null) =>
        new RetryPolicyBuilder()
            .WithMaxRetries(2)
            .WithBaseDelay(100)
            .WithExponentialBackoff(2.0, 1000)
            .WithLogger(logger)
            .Build();
}