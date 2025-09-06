# Windows事件映射表和枚举定义实现说明

## 概述

基于Windows事件日志信息，我已经创建了详细的映射表和枚举定义，包含改进的ShutdownType枚举、事件ID映射表、关机原因中文描述映射等完整的C#实现。

## 主要改进内容

### 1. 改进的ShutdownType枚举

**文件位置**: `H:\Code_Poject\TurnedOnTimesView\src\TurnedOnTimesView\Models\ShutdownType.cs`

```csharp
public enum ShutdownType
{
    [Description("正常关机")]
    Normal,           // 正常关机
    
    [Description("强制关机")]
    Forced,           // 强制关机
    
    [Description("系统重启")]
    Restart,          // 系统重启
    
    [Description("系统睡眠")]
    Sleep,            // 系统睡眠
    
    [Description("系统休眠")]
    Hibernate,        // 系统休眠
    
    [Description("意外关机")]
    Unexpected,       // 意外关机/断电
    
    [Description("用户发起")]
    UserInitiated,    // 用户发起的关机/重启
    
    [Description("系统发起")]
    SystemInitiated,  // 系统发起的关机/重启
    
    [Description("系统启动")]
    Startup,          // 系统启动
    
    [Description("系统唤醒")]
    WakeUp,           // 从睡眠/休眠唤醒
    
    [Description("未知")]
    Unknown           // 未知类型
}
```

### 2. EventMappingService服务类

**文件位置**: `H:\Code_Poject\TurnedOnTimesView\src\TurnedOnTimesView\Services\EventMappingService.cs`

该服务提供了完整的Windows事件日志映射功能：

#### 事件ID到关机类型映射表
```csharp
private static readonly Dictionary<int, ShutdownType> EventIdToShutdownTypeMap = new()
{
    { 6005, ShutdownType.Startup },          // 系统启动事件
    { 6006, ShutdownType.Normal },           // 正常关机事件
    { 1074, ShutdownType.UserInitiated },    // 用户发起的关机/重启事件
    { 6008, ShutdownType.Unexpected },       // 意外关机事件
    { 41, ShutdownType.Forced },             // 强制关机/意外重启事件
    { 42, ShutdownType.Sleep },              // 系统睡眠事件
    { 1, ShutdownType.WakeUp },              // 从睡眠/休眠唤醒事件
    { 107, ShutdownType.Hibernate },         // 系统休眠事件
    { 1076, ShutdownType.SystemInitiated }   // 系统发起的关机事件
};
```

#### 关机原因代码中文描述映射表
```csharp
private static readonly Dictionary<string, string> ShutdownReasonDescriptionMap = new()
{
    // 意外关机原因
    { "0x80000000", "意外关机" },
    { "0x80020000", "意外关机 (电源故障)" },
    { "0x80040000", "意外关机 (蓝屏死机)" },
    
    // 计划内关机原因  
    { "0x84000000", "计划内关机" },
    { "0x84020000", "计划内关机 (硬件维护)" },
    { "0x84040000", "计划内关机 (软件维护)" },
    
    // 用户发起的关机
    { "0x500000ff", "用户手动关机" },
    { "0x500000fe", "用户注销" },
    { "0x50000000", "用户关机" },
    
    // 系统发起的关机
    { "0x80000001", "系统自动关机" },
    { "0x80000002", "系统重启" },
    { "0x80000012", "系统蓝屏重启" },
    
    // 其他常见原因
    { "0x85000000", "电源按钮关机" },
    { "0x85010000", "睡眠按钮" }
    // ... 更多映射
};
```

### 3. 增强的SystemEvent模型

**文件位置**: `H:\Code_Poject\TurnedOnTimesView\src\TurnedOnTimesView\Models\SystemEvent.cs`

新增的属性：
- `ShutdownType ShutdownType` - 关机类型（基于事件ID映射）
- `string ShutdownReasonDescription` - 关机原因的中文描述  
- `string DetailedDescription` - 详细的事件描述

改进的布尔属性：
- `IsStartupEvent` - 基于ShutdownType判断
- `IsShutdownEvent` - 包含Normal、UserInitiated、SystemInitiated
- `IsAbnormalShutdownEvent` - 包含Unexpected、Forced
- `IsSleepEvent` - 睡眠事件
- `IsHibernateEvent` - 休眠事件（新增）
- `IsWakeupEvent` - 唤醒事件
- `IsRestartEvent` - 重启事件（新增）

### 4. 更新的EventLogService

**文件位置**: `H:\Code_Poject\TurnedOnTimesView\src\TurnedOnTimesView\Services\EventLogService.cs`

主要改进：
- 集成EventMappingService依赖注入
- ConvertToSystemEvent方法使用映射服务自动填充新属性
- 支持详细的关机原因解析和中文描述

### 5. 依赖注入配置

**文件位置**: `H:\Code_Poject\TurnedOnTimesView\src\TurnedOnTimesView\Infrastructure\DependencyInjection\ServiceCollectionExtensions.cs`

新增服务注册：
```csharp
services.AddSingleton<EventMappingService>();
```

## 核心功能API

### EventMappingService主要方法

1. **GetShutdownType(int eventId)** - 根据事件ID获取关机类型
2. **GetShutdownTypeDescription(ShutdownType shutdownType)** - 获取关机类型中文描述
3. **GetShutdownReasonDescription(string rawReason)** - 解析关机原因的中文描述
4. **GetDetailedEventDescription(int eventId, string message)** - 获取详细事件描述
5. **GetSupportedEventIds()** - 获取所有支持的事件ID
6. **IsSupportedEventId(int eventId)** - 检查事件ID是否受支持

### 使用示例

```csharp
// 依赖注入获取服务
var eventMappingService = serviceProvider.GetRequiredService<EventMappingService>();

// 根据事件ID获取关机类型
var shutdownType = eventMappingService.GetShutdownType(1074); // UserInitiated

// 获取关机原因中文描述
var reasonDescription = eventMappingService.GetShutdownReasonDescription("0x500000ff"); // "用户手动关机"

// 创建完整的SystemEvent对象（在EventLogService中自动完成）
var systemEvent = new SystemEvent
{
    EventId = 1074,
    // ... 其他基本属性
    ShutdownType = eventMappingService.GetShutdownType(1074),
    ShutdownReasonDescription = eventMappingService.GetShutdownReasonDescription(rawReason),
    DetailedDescription = eventMappingService.GetDetailedEventDescription(1074, message)
};
```

## 关键改进特性

### 1. 智能关机原因解析
- 支持十六进制关机代码解析
- 支持Windows常量名解析
- 模糊匹配常见关键字
- 多级映射策略确保高准确率

### 2. 详细事件描述
- 从事件消息中提取用户名、进程名等关键信息
- 根据不同事件ID提供定制化描述
- 支持时间、错误代码等特殊信息提取

### 3. 向后兼容性
- 保持原有API接口不变
- 新属性采用合理默认值
- 渐进式增强现有功能

### 4. 高性能设计
- 静态映射表避免重复计算
- 单例服务减少内存占用
- 编译时正则表达式优化

## 示例程序

**文件位置**: `H:\Code_Poject\TurnedOnTimesView\src\TurnedOnTimesView\Examples\EventMappingExample.cs`

提供了完整的使用演示，包括：
- 事件ID映射演示
- 关机原因解析演示
- 详细描述生成演示
- 完整SystemEvent对象创建示例

## 构建和测试

项目已通过编译测试，所有新功能都已集成到现有架构中：

```bash
dotnet build src/TurnedOnTimesView/TurnedOnTimesView.csproj
# 构建成功，0个错误
```

## 总结

此次实现提供了：
1. ✅ 完整的ShutdownType枚举扩展（11种类型）
2. ✅ 详细的事件ID到关机类型映射表（9个关键事件ID）
3. ✅ 丰富的关机原因中文描述映射（30+种原因代码）
4. ✅ 增强的SystemEvent模型和布尔属性
5. ✅ 集成的EventMappingService服务
6. ✅ 完整的依赖注入配置
7. ✅ 详细的使用示例和文档

所有代码都是可直接在.NET项目中使用的生产级实现，具有良好的性能、可维护性和扩展性。