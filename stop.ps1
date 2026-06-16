$ErrorActionPreference = "SilentlyContinue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "    PiPiClaw.Team 项目停止脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

foreach ($port in @(4050, 5173, 5174)) {
    Write-Host "[信息] 检查端口 $port ..." -ForegroundColor Yellow
    $connections = Get-NetTCPConnection -LocalPort $port | Where-Object { $_.State -eq "Listen" }
    foreach ($connection in $connections) {
        Stop-Process -Id $connection.OwningProcess -Force
        Write-Host "[成功] 已停止端口 $port 进程 PID: $($connection.OwningProcess)" -ForegroundColor Green
    }
}

Get-Process -Name "PiPiClaw.Team" | ForEach-Object {
    Stop-Process -Id $_.Id -Force
    Write-Host "[成功] 已停止 PiPiClaw.Team.exe PID: $($_.Id)" -ForegroundColor Green
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "    停止操作完成" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
