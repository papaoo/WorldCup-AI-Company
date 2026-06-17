param(
    [int[]]$Ports = @(4050, 5174)
)

$ErrorActionPreference = "SilentlyContinue"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "    WorldCup AI Company stop" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

foreach ($port in $Ports) {
    Write-Host "[INFO] Checking port $port ..." -ForegroundColor Yellow
    $connections = Get-NetTCPConnection -LocalPort $port -State Listen
    foreach ($connection in $connections) {
        $process = Get-Process -Id $connection.OwningProcess
        if ($process) {
            Stop-Process -Id $process.Id -Force
            Write-Host "[OK] Stopped port $port process PID: $($process.Id) $($process.ProcessName)" -ForegroundColor Green
        }
    }
}

Get-Process -Name "PiPiClaw.Team" | Where-Object {
    $_.Path -like "$root*"
} | ForEach-Object {
    Stop-Process -Id $_.Id -Force
    Write-Host "[OK] Stopped PiPiClaw.Team.exe PID: $($_.Id)" -ForegroundColor Green
}

Get-Process -Name "dotnet" | ForEach-Object {
    $commandLine = (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)").CommandLine
    if ($commandLine -and $commandLine.Contains($root)) {
        Stop-Process -Id $_.Id -Force
        Write-Host "[OK] Stopped project dotnet process PID: $($_.Id)" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "    Stopped" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan

