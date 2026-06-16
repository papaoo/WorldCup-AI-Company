$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$frontend = Join-Path $root "Frontend\worldcup-ui"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "    PiPiClaw.Team 项目启动脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$dotnetVersion = dotnet --version
Write-Host "[成功] 已检测到 .NET SDK: $dotnetVersion" -ForegroundColor Green

$nodeVersion = node --version
Write-Host "[成功] 已检测到 Node.js: $nodeVersion" -ForegroundColor Green

if (-not (Test-Path (Join-Path $frontend "package.json"))) {
    throw "未找到前端项目: $frontend"
}

Write-Host ""
Write-Host "[信息] 后端工作目录: $root" -ForegroundColor Yellow
Write-Host "[信息] 前端工作目录: $frontend" -ForegroundColor Yellow
Write-Host ""

Start-Process -FilePath "dotnet" -ArgumentList "run" -WorkingDirectory $root -WindowStyle Normal

Write-Host "[信息] 等待后端启动..." -ForegroundColor Yellow
Start-Sleep -Seconds 3

if (-not (Test-Path (Join-Path $frontend "node_modules"))) {
    Write-Host "[信息] 首次运行，正在安装前端依赖..." -ForegroundColor Yellow
    Push-Location $frontend
    npm install
    Pop-Location
}

Start-Process -FilePath "npm" -ArgumentList "run", "dev" -WorkingDirectory $frontend -WindowStyle Normal

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "    项目启动完成" -ForegroundColor Green
Write-Host "    后端: http://localhost:4050/" -ForegroundColor White
Write-Host "    前端: http://127.0.0.1:5174/" -ForegroundColor White
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "可以关闭此窗口，后端和前端会在新窗口继续运行。"
