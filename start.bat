@echo off
chcp 65001 >nul
setlocal

set "ROOT=%~dp0"
set "FRONTEND=%ROOT%Frontend\worldcup-ui"

echo ========================================
echo     PiPiClaw.Team 项目启动脚本
echo ========================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [错误] 未找到 .NET SDK，请先安装 .NET。
    pause
    exit /b 1
)
echo [成功] 已检测到 .NET SDK

where node >nul 2>&1
if errorlevel 1 (
    echo [错误] 未找到 Node.js，请先安装 Node.js。
    pause
    exit /b 1
)
echo [成功] 已检测到 Node.js

if not exist "%FRONTEND%\package.json" (
    echo [错误] 未找到前端项目: %FRONTEND%
    pause
    exit /b 1
)

echo.
echo [信息] 后端工作目录: %ROOT%
echo [信息] 前端工作目录: %FRONTEND%
echo.

start "PiPiClaw.Team 后端 4050" cmd /k "cd /d "%ROOT%" && echo 启动后端服务 http://localhost:4050/ && dotnet run"

timeout /t 3 /nobreak >nul

if not exist "%FRONTEND%\node_modules" (
    echo [信息] 首次运行，正在安装前端依赖...
    pushd "%FRONTEND%"
    call npm install
    popd
)

start "PiPiClaw.Team 前端 5174" cmd /k "cd /d "%FRONTEND%" && echo 启动前端服务 http://127.0.0.1:5174/ && npm run dev"

echo.
echo ========================================
echo     项目启动完成
echo     后端: http://localhost:4050/
echo     前端: http://127.0.0.1:5174/
echo ========================================
echo.
echo 可以关闭此窗口，后端和前端会在新窗口继续运行。
pause >nul
