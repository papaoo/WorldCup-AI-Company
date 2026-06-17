param(
    [int]$BackendPort = 4050,
    [int]$FrontendPort = 5174,
    [switch]$EnableDevEndpoints
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$frontend = Join-Path $root "Frontend\worldcup-ui"
$backendUrl = "http://localhost:$BackendPort"
$frontendUrl = "http://127.0.0.1:$FrontendPort"

function Test-PortAvailable([int]$port) {
    $listeners = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    return -not $listeners
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "    WorldCup AI Company startup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$dotnetVersion = dotnet --version
Write-Host "[OK] .NET SDK: $dotnetVersion" -ForegroundColor Green

$nodeVersion = node --version
Write-Host "[OK] Node.js: $nodeVersion" -ForegroundColor Green

if (-not (Test-Path (Join-Path $frontend "package.json"))) {
    throw "Frontend project was not found: $frontend"
}

if (-not (Test-PortAvailable $BackendPort)) {
    throw "Backend port $BackendPort is already in use. Run stop.ps1 first or pass -BackendPort."
}

if (-not (Test-PortAvailable $FrontendPort)) {
    throw "Frontend port $FrontendPort is already in use. Run stop.ps1 first or pass -FrontendPort."
}

if (-not (Test-Path (Join-Path $frontend "node_modules"))) {
    Write-Host "[INFO] Installing frontend dependencies..." -ForegroundColor Yellow
    Push-Location $frontend
    npm install
    Pop-Location
}

$devEndpoints = if ($EnableDevEndpoints) { "1" } else { "0" }

Write-Host "[INFO] Backend dir: $root" -ForegroundColor Yellow
Write-Host "[INFO] Frontend dir: $frontend" -ForegroundColor Yellow
Write-Host "[INFO] Backend URL: $backendUrl/" -ForegroundColor Yellow
Write-Host "[INFO] Frontend URL: $frontendUrl/" -ForegroundColor Yellow
Write-Host "[INFO] Dev endpoints: $(if ($EnableDevEndpoints) { 'enabled' } else { 'disabled' })" -ForegroundColor Yellow
Write-Host ""

$backendCommand = @"
`$env:WORLDCUP_URLS='$backendUrl/';
`$env:WORLDCUP_ENABLE_DEV_ENDPOINTS='$devEndpoints';
cd '$root';
dotnet run --urls '$backendUrl'
"@
Start-Process -FilePath "powershell" -ArgumentList "-NoExit", "-ExecutionPolicy", "Bypass", "-Command", $backendCommand -WindowStyle Normal

Start-Sleep -Seconds 3

$frontendCommand = @"
cd '$frontend';
npm run dev -- --host 127.0.0.1 --port $FrontendPort
"@
Start-Process -FilePath "powershell" -ArgumentList "-NoExit", "-ExecutionPolicy", "Bypass", "-Command", $frontendCommand -WindowStyle Normal

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "    Started" -ForegroundColor Green
Write-Host "    Backend: $backendUrl/" -ForegroundColor White
Write-Host "    Frontend: $frontendUrl/" -ForegroundColor White
Write-Host "========================================" -ForegroundColor Cyan

