# 比较脚本 - 输出前10条记录用于对比
Write-Host "=== 运行我们的应用程序获取前10条记录 ==="

# 切换到应用目录
Set-Location "H:\Code_Poject\TurnedOnTimesView\src\TurnedOnTimesView"

# 运行应用程序，捕获输出
$output = & dotnet run --configuration Release 2>$null

# 查找会话记录数量
$sessionCount = ($output | Select-String "成功加载.*个会话记录" | ForEach-Object { $_.Matches[0].Value })
Write-Host "会话记录数量: $sessionCount"

Write-Host ""
Write-Host "=== 原始TurnedOnTimesView数据前10条 ==="
Write-Host "1. 2025-09-06 9:46:57"
Write-Host "2. 2025-09-04 7:38:12 - 2025-09-06 2:02:29 (关闭电源)"
Write-Host "3. 2025-09-03 7:49:19 - 2025-09-04 3:54:40 (Sleep)"
Write-Host "4. 2025-09-02 9:44:32 - 2025-09-03 2:02:48 (Sleep)"
Write-Host "5. 2025-09-02 4:20:13 - 2025-09-02 4:20:33 (Sleep)"
Write-Host "6. 2025-09-02 0:59:45 - 2025-09-02 4:14:28 (Sleep)"
Write-Host "7. 2025-09-01 17:44:09 - 2025-09-02 0:58:29 (Unexpected Shutdown)"
Write-Host "8. 2025-09-01 9:17:24 - (无结束时间)"
Write-Host "9. 2025-09-01 2:42:21 - 2025-09-01 8:25:14 (Sleep)"
Write-Host "10. 2025-08-31 11:20:00 - 2025-09-01 2:40:57 (重启)"