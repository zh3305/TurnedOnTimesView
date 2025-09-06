# TurnedOnTimesView - 项目上下文文档

## 🎯 项目概述
基于 .NET 9 + WPF 的 Windows 开关机记录分析工具，使用 MVVM 模式，分析 Windows 事件日志并显示系统开关机历史记录。

## 🔧 核心功能需求

### 主要功能
1. **事件日志分析** - 读取 Windows 系统事件日志
2. **会话记录展示** - 显示启动/关机时间、持续时间等
3. **新增字段** - **最后事件时间字段**（重要新功能）
4. **多维度筛选** - 按日期范围、关机类型筛选
5. **统计信息** - 总会话数、正常关机、异常关机、睡眠次数
6. **数据导出** - 支持 CSV、Excel、HTML 格式
7. **排序功能** - 支持各列排序

### 界面要求
- **严格参照** `dz.html` 模板设计
- 现代化 WPF 界面
- 渐变色头部 (#667eea 到 #764ba2)
- 卡片式布局设计
- 响应式统计面板

## 🏗️ 技术架构

### 框架和版本
- **.NET 9** (最新版本)
- **WPF** (Windows Presentation Foundation)
- **MVVM模式** 使用 CommunityToolkit.Mvvm

### 核心技术栈
- **事件日志**: System.Diagnostics.EventLog
- **MVVM框架**: CommunityToolkit.Mvvm
- **日志系统**: Microsoft.Extensions.Logging + Serilog
- **依赖注入**: Microsoft.Extensions.DependencyInjection
- **数据导出**: EPPlus (Excel) + CsvHelper (CSV)

### 关键事件ID
- **6005**: 系统启动
- **6006**: 系统关机
- **6008**: 意外关机/重启
- **42**: 系统睡眠
- **1**: 系统唤醒

## 📂 项目结构

```
src/TurnedOnTimesView/
├── ViewModels/          # MVVM - 视图模型层
├── Views/               # MVVM - 视图层
├── Models/              # 数据模型和DTO
├── Services/            # 业务服务层
├── Core/                # 核心业务逻辑
├── Infrastructure/      # 基础设施（日志、DI、配置）
├── Converters/          # WPF值转换器
├── Commands/            # WPF命令
└── Resources/           # 样式和模板
```

## 💾 数据模型

### SessionRecord（核心实体）
```csharp
public class SessionRecord
{
    public DateTime StartTime { get; set; }        // 启动时间
    public DateTime? EndTime { get; set; }         // 关机时间
    public TimeSpan Duration { get; set; }         // 持续时间
    public string ShutdownReason { get; set; }     // 关机原因
    public ShutdownType Type { get; set; }         // 关机类型
    public string Process { get; set; }            // 关机进程
    public DateTime LastEventTime { get; set; }    // 【新增】最后事件时间
}
```

### ShutdownType（枚举）
```csharp
public enum ShutdownType
{
    Normal,      // 正常关机
    Abnormal,    // 异常关机  
    Sleep,       // 睡眠
    Restart      // 重启
}
```

## 🔍 关键实现点

### 1. 事件日志读取逻辑
- 访问 "System" 事件日志
- 按时间范围过滤事件
- 解析事件详情提取关机原因

### 2. 会话计算算法
- 配对启动/关机事件形成会话
- 处理未配对事件（系统异常情况）
- **计算最后事件时间**（新功能重点）

### 3. MVVM绑定
- MainViewModel 管理主界面状态
- 使用 ObservableCollection 绑定数据表格
- 命令绑定处理用户交互

### 4. 日志记录
- 结构化日志记录操作过程
- 文件和控制台双输出
- 分级日志便于调试

## 🎨 UI设计规范

### 颜色方案
- **主色调**: #667eea (蓝紫色)
- **辅助色**: #764ba2 (深紫色) 
- **背景色**: #f5f5f5 (浅灰)
- **成功色**: #48bb78 (绿色)
- **警告色**: #ed8936 (橙色)
- **错误色**: #f56565 (红色)

### 布局要求
- 头部渐变背景
- 统计卡片网格布局
- 数据表格响应式设计
- 筛选控件水平布局

## 🛠️ 开发优先级

### Phase 1 (优先级最高)
1. 基础 MVVM 架构搭建
2. 事件日志读取核心功能
3. SessionRecord 数据模型
4. **最后事件时间字段实现**

### Phase 2 (第二优先级)  
1. WPF 主界面实现
2. 数据绑定和显示
3. 基础筛选功能
4. 统计信息计算

### Phase 3 (第三优先级)
1. 数据导出功能
2. 高级筛选和排序
3. 日志和错误处理
4. 单元测试完善

## 🔧 配置说明

### appsettings.json 关键配置
- **DefaultDateRange**: 默认查询天数 (30天)
- **MaxRecordsToLoad**: 最大记录数 (10000)
- **RefreshIntervalMinutes**: 刷新间隔 (5分钟)

## 🧪 测试策略

### 单元测试重点
- EventLogService 事件读取逻辑
- SessionAnalyzer 会话计算算法
- ViewModels 绑定逻辑

### 集成测试重点
- 完整数据流测试
- 界面交互测试

## 🚨 注意事项

1. **管理员权限** - 读取事件日志需要管理员权限
2. **性能优化** - 大量事件记录的分页处理
3. **异常处理** - 事件日志访问异常的优雅处理
4. **时区处理** - 确保时间显示的一致性
5. **内存管理** - 大数据集的内存优化

## 📋 后续 Agent 工作重点

### csharp-pro agent 任务清单
1. **优先实现事件日志读取核心服务**
2. **重点开发最后事件时间功能**
3. **搭建完整 MVVM 架构**
4. **实现 WPF 界面，严格按照 HTML 模板设计**
5. **集成日志系统和依赖注入**
6. **开发数据导出功能**
7. **编写单元测试**

### 关键交付物
- 可运行的 WPF 应用程序
- 完整的事件日志分析功能
- 符合设计规范的用户界面
- 单元测试覆盖核心逻辑
- 完整的错误处理和日志记录

---
*文档版本: 1.0 | 创建时间: 2024-09-06 | 上下文管理器: Claude*