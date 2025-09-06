using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurnedOnTimesView.Infrastructure.Exceptions;
using TurnedOnTimesView.Infrastructure.Logging;
using TurnedOnTimesView.Infrastructure.Resilience;
using TurnedOnTimesView.Services;
using TurnedOnTimesView.Views;

namespace TurnedOnTimesView.Examples;

/// <summary>
/// 异常处理系统使用示例
/// </summary>
public static class ExceptionHandlingExample
{
    /// <summary>
    /// 基本异常处理示例
    /// </summary>
    public static async Task BasicExceptionHandlingExample()
    {
        // 1. 创建自定义异常
        var fileException = new FileAccessException(
            @"C:\temp\missing.evtx",
            "无法访问指定的文件，请检查文件路径和权限",
            "文件不存在或访问被拒绝");

        // 2. 添加详细信息
        fileException.WithDetail("FileSize", 0)
                    .WithDetail("LastAttempt", DateTime.Now);

        // 3. 获取用户友好的解决方案
        var solutions = ErrorMessages.GetSolutions(fileException.ErrorCode);
        Console.WriteLine($"建议的解决方案数量: {solutions.Count}");

        // 4. 异常翻译
        try
        {
            throw new UnauthorizedAccessException("Access denied");
        }
        catch (Exception ex)
        {
            var translatedEx = ErrorMessages.TranslateException(ex, "访问系统文件");
            Console.WriteLine($"翻译后的用户消息: {translatedEx.UserMessage}");
        }
    }

    /// <summary>
    /// 重试机制示例
    /// </summary>
    public static async Task RetryPolicyExample()
    {
        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("RetryExample");
        
        // 1. 使用预定义的重试策略
        var fileRetryPolicy = RetryPolicies.FileOperations(logger);
        
        // 2. 执行可能失败的操作
        try
        {
            var result = await fileRetryPolicy.ExecuteAsync(async ct =>
            {
                // 模拟一个可能失败的文件操作
                await Task.Delay(100, ct);
                
                // 第一次和第二次调用会失败
                if (DateTime.Now.Millisecond % 3 != 0)
                {
                    throw new IOException("文件被锁定");
                }
                
                return "操作成功";
            });
            
            Console.WriteLine($"重试操作结果: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"重试失败: {ex.Message}");
        }
        
        // 3. 带进度报告的重试
        var progress = new Progress<OperationProgress>(p =>
        {
            Console.WriteLine($"进度: {p.CurrentStep} - {p.PercentComplete}%");
        });
        
        try
        {
            await fileRetryPolicy.ExecuteWithProgressAsync(async (progressReporter, ct) =>
            {
                progressReporter?.Report(new OperationProgress 
                { 
                    CurrentStep = "开始长时间操作",
                    PercentComplete = 0 
                });
                
                await Task.Delay(1000, ct);
                
                progressReporter?.Report(new OperationProgress 
                { 
                    CurrentStep = "操作完成",
                    PercentComplete = 100 
                });
                
                return "带进度的操作完成";
            }, progress);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"带进度的重试失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 错误跟踪器示例
    /// </summary>
    public static async Task ErrorTrackerExample()
    {
        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<InMemoryErrorTracker>();
        var errorTracker = new InMemoryErrorTracker(logger);
        
        // 1. 跟踪不同类型的错误
        var error1 = new FileAccessException("/path/to/file", "文件访问被拒绝");
        var errorId1 = errorTracker.TrackError(error1, "用户操作", "打开文件");
        
        var error2 = new InsufficientPermissionException("管理员权限", "需要管理员权限");
        var errorId2 = errorTracker.TrackError(error2, "系统检查", "权限验证");
        
        // 2. 增加重试次数
        errorTracker.IncrementRetryCount(errorId1);
        errorTracker.IncrementRetryCount(errorId1);
        
        // 3. 解决错误
        errorTracker.ResolveError(errorId2);
        
        // 4. 获取错误统计信息
        var stats = errorTracker.GetStatistics();
        Console.WriteLine($"总错误数: {stats.TotalErrors}");
        Console.WriteLine($"未解决错误数: {stats.UnresolvedErrors}");
        Console.WriteLine($"最常见的错误: {string.Join(", ", stats.MostCommonErrors.Select(e => $"{e.ErrorCode}({e.Count})"))}");
        
        // 5. 获取最近的错误
        var recentErrors = errorTracker.GetRecentErrors(5, ErrorSeverity.Error);
        Console.WriteLine($"最近的严重错误数: {recentErrors.Count}");
    }

    /// <summary>
    /// UI错误处理示例
    /// </summary>
    public static async Task UIErrorHandlingExample()
    {
        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("UIExample");
        
        // 1. 创建一个需要用户干预的错误
        var networkError = new NetworkException(
            "网络连接失败，无法访问远程服务器",
            "连接超时: 服务器未响应");
        
        // 2. 在实际应用中，这会显示错误对话框
        // 这里我们模拟用户的选择
        Console.WriteLine("模拟显示错误对话框...");
        Console.WriteLine($"错误消息: {networkError.UserMessage}");
        Console.WriteLine($"建议的解决方案:");
        
        var solutions = ErrorMessages.GetSolutions(networkError.ErrorCode);
        foreach (var solution in solutions)
        {
            Console.WriteLine($"  • {solution}");
        }
        
        // 3. 模拟用户选择重试
        var retryAction = async () =>
        {
            Console.WriteLine("执行重试操作...");
            await Task.Delay(1000);
            Console.WriteLine("重试成功!");
        };
        
        Console.WriteLine("用户选择重试...");
        await retryAction();
    }

    /// <summary>
    /// 服务集成示例
    /// </summary>
    public static async Task ServiceIntegrationExample()
    {
        // 这个示例展示如何在实际的服务中集成异常处理
        
        var services = new ServiceCollection();
        
        // 注册服务
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IErrorTracker, InMemoryErrorTracker>();
        services.AddHostedService<InMemoryErrorTracker>(provider => 
            (InMemoryErrorTracker)provider.GetRequiredService<IErrorTracker>());
        
        var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("ExceptionHandlingExample");
        var errorTracker = serviceProvider.GetRequiredService<IErrorTracker>();
        
        // 模拟服务操作
        try
        {
            await SimulateServiceOperation();
        }
        catch (Exception ex)
        {
            // 使用扩展方法记录和跟踪错误
            var errorId = logger.LogAndTrackError(errorTracker, ex, "服务操作示例", "模拟操作");
            
            Console.WriteLine($"错误已记录和跟踪，ID: {errorId}");
            
            // 获取错误详情
            var errorRecord = errorTracker.GetError(errorId);
            if (errorRecord != null)
            {
                Console.WriteLine($"错误时间: {errorRecord.Timestamp}");
                Console.WriteLine($"错误严重性: {errorRecord.Severity}");
                Console.WriteLine($"操作上下文: {errorRecord.Context}");
            }
        }
        
        // 显示最终统计
        var finalStats = errorTracker.GetStatistics();
        Console.WriteLine($"\n最终统计:");
        Console.WriteLine($"总错误数: {finalStats.TotalErrors}");
        Console.WriteLine($"按严重性分组: {string.Join(", ", finalStats.ErrorsBySeverity.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
    }
    
    /// <summary>
    /// 模拟可能失败的服务操作
    /// </summary>
    private static async Task SimulateServiceOperation()
    {
        await Task.Delay(100);
        
        // 随机选择一种异常类型
        var random = new Random();
        var exceptionType = random.Next(1, 5);
        
        switch (exceptionType)
        {
            case 1:
                throw new FileAccessException(
                    @"C:\nonexistent\file.evtx", 
                    "无法访问指定的事件日志文件");
                    
            case 2:
                throw new InsufficientPermissionException(
                    "事件日志访问权限", 
                    "访问Windows事件日志需要管理员权限");
                    
            case 3:
                throw new NetworkException(
                    "网络连接失败，请检查网络设置");
                    
            case 4:
                throw new InsufficientResourceException(
                    "内存", 1024 * 1024, 512 * 1024, 
                    "系统内存不足，无法完成操作");
                    
            default:
                throw new DataValidationException(
                    "输入的日期范围无效", 
                    "开始日期必须早于结束日期");
        }
    }

    /// <summary>
    /// 运行所有示例
    /// </summary>
    public static async Task RunAllExamples()
    {
        Console.WriteLine("=== 异常处理系统示例 ===\n");
        
        Console.WriteLine("1. 基本异常处理示例");
        await BasicExceptionHandlingExample();
        Console.WriteLine();
        
        Console.WriteLine("2. 重试机制示例");
        await RetryPolicyExample();
        Console.WriteLine();
        
        Console.WriteLine("3. 错误跟踪器示例");
        await ErrorTrackerExample();
        Console.WriteLine();
        
        Console.WriteLine("4. UI错误处理示例");
        await UIErrorHandlingExample();
        Console.WriteLine();
        
        Console.WriteLine("5. 服务集成示例");
        await ServiceIntegrationExample();
        Console.WriteLine();
        
        Console.WriteLine("=== 所有示例运行完成 ===");
    }
}

/// <summary>
/// 异常处理最佳实践指南
/// </summary>
public static class ExceptionHandlingBestPractices
{
    /// <summary>
    /// 1. 异常分类和使用指南
    /// </summary>
    public static void ExceptionClassificationGuide()
    {
        /*
        异常分类使用指南:

        1. FileAccessException - 文件访问相关错误
           - 文件不存在
           - 权限不足
           - 文件被锁定
           - 网络驱动器问题

        2. FileFormatException - 文件格式错误
           - 损坏的.evtx文件
           - 不支持的文件格式
           - 版本不兼容

        3. InsufficientPermissionException - 权限不足
           - 需要管理员权限
           - 事件日志访问权限
           - 系统服务权限

        4. EventLogServiceException - 事件日志服务错误
           - 服务不可用
           - 查询超时
           - 系统资源问题

        5. NetworkException - 网络相关错误
           - 连接超时
           - DNS解析失败
           - 代理配置问题

        6. DataValidationException - 数据验证错误
           - 参数验证失败
           - 日期范围无效
           - 配置错误

        7. InsufficientResourceException - 资源不足
           - 内存不足
           - 磁盘空间不足
           - CPU资源不足
        */
    }

    /// <summary>
    /// 2. 重试策略选择指南
    /// </summary>
    public static void RetryPolicyGuide()
    {
        /*
        重试策略选择指南:

        1. RetryPolicies.FileOperations
           - 适用于: 文件读写操作
           - 重试次数: 3次
           - 延迟策略: 指数退避(1s, 1.5s, 2.25s)
           - 适用错误: IOException, FileAccessException

        2. RetryPolicies.NetworkOperations  
           - 适用于: 网络请求
           - 重试次数: 5次
           - 延迟策略: 指数退避(2s, 4s, 8s, 16s, 30s)
           - 适用错误: NetworkException, TimeoutException

        3. RetryPolicies.SystemServices
           - 适用于: 系统服务调用
           - 重试次数: 3次
           - 延迟策略: 快速重试(0.5s, 0.75s, 1.125s)
           - 适用错误: EventLogException, ServiceException

        4. RetryPolicies.FastRetry
           - 适用于: 轻量级操作
           - 重试次数: 2次
           - 延迟策略: 快速(100ms, 200ms)
           - 适用错误: 临时性错误

        自定义重试策略:
        var customPolicy = new RetryPolicyBuilder()
            .WithMaxRetries(5)
            .WithBaseDelay(1000)
            .WithExponentialBackoff(2.0, 30000)
            .WithJitter()
            .OnRetrying((attempt, ex) => Console.WriteLine($"重试第{attempt}次: {ex.Message}"))
            .Build();
        */
    }

    /// <summary>
    /// 3. 错误消息本地化指南
    /// </summary>
    public static void ErrorMessageLocalizationGuide()
    {
        /*
        错误消息本地化指南:

        1. 错误码命名规范:
           - 使用大写字母和下划线
           - 按功能模块分组
           - 示例: FILE_ACCESS_ERROR, NETWORK_TIMEOUT

        2. 用户消息编写原则:
           - 使用用户友好的语言
           - 避免技术术语
           - 提供明确的指导

        3. 解决方案建议:
           - 提供具体的操作步骤
           - 按优先级排序
           - 包含备选方案

        4. 添加新的错误消息:
           // 在ErrorMessages.cs中添加
           ["NEW_ERROR_CODE"] = "用户友好的错误消息",

           // 添加对应的解决方案
           ["NEW_ERROR_CODE"] = new()
           {
               "第一步解决方案",
               "第二步解决方案",
               "备选解决方案"
           }
        */
    }

    /// <summary>
    /// 4. 错误跟踪最佳实践
    /// </summary>
    public static void ErrorTrackingBestPractices()
    {
        /*
        错误跟踪最佳实践:

        1. 结构化日志记录:
           using (logger.BeginScope(new Dictionary<string, object>
           {
               ["UserId"] = currentUserId,
               ["Operation"] = "LoadSessions",
               ["DataSource"] = dataSource.Type
           }))
           {
               logger.LogError(ex, "操作失败: {ErrorMessage}", ex.Message);
           }

        2. 错误关联:
           var errorId = errorTracker.TrackError(exception, context, operationType, userId, sessionId);
           
           // 后续可以通过errorId关联相关操作
           errorTracker.IncrementRetryCount(errorId);
           errorTracker.ResolveError(errorId);

        3. 错误统计分析:
           var stats = errorTracker.GetStatistics(DateTime.Today.AddDays(-7), DateTime.Today);
           
           // 分析错误趋势
           foreach (var hourlyError in stats.HourlyTrend)
           {
               if (hourlyError.Value > threshold)
               {
                   // 触发告警
               }
           }

        4. 错误清理策略:
           // 定期清理旧错误记录
           var cleanedCount = errorTracker.CleanupOldRecords(TimeSpan.FromDays(30));
           logger.LogInformation("清理了 {Count} 个过期错误记录", cleanedCount);
        */
    }

    /// <summary>
    /// 5. UI错误处理指南
    /// </summary>
    public static void UIErrorHandlingGuide()
    {
        /*
        UI错误处理指南:

        1. 错误对话框显示时机:
           - 用户操作导致的错误
           - 需要用户干预的错误
           - 系统级别的严重错误

        2. 错误消息层次:
           - 标题: 简短的错误描述
           - 消息: 用户友好的详细说明
           - 解决方案: 具体的操作建议
           - 技术详情: 可展开的技术信息

        3. 重试机制集成:
           var shouldRetry = await ErrorDialogService.ShowErrorAsync(
               parentWindow, 
               exception, 
               retryAction, 
               logger);

        4. 渐进式错误处理:
           try
           {
               await riskyOperation();
           }
           catch (TurnedOnTimesViewException appEx)
           {
               // 显示用户友好的错误对话框
               await ShowErrorDialogAsync(appEx, retryAction);
           }
           catch (Exception ex)
           {
               // 翻译并显示系统异常
               var translated = ErrorMessages.TranslateException(ex);
               await ShowErrorDialogAsync(translated);
           }

        5. 状态消息降级:
           - 如果错误对话框显示失败，降级到状态栏消息
           - 提供基本的错误信息
           - 确保用户知道操作失败
        */
    }
}