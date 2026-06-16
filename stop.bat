@echo off
chcp 65001 >nul
setlocal

echo ========================================
echo     PiPiClaw.Team 项目停止脚本
echo ========================================
echo.

for %%p in (4050 5173 5174) do (
    echo [信息] 检查端口 %%p ...
    for /f "tokens=5" %%a in ('netstat -ano ^| findstr ":%%p" ^| findstr "LISTENING"') do (
        taskkill /F /PID %%a >nul 2>&1
        if not errorlevel 1 echo [成功] 已停止端口 %%p 进程 PID: %%a
    )
)

for /f "tokens=2" %%a in ('tasklist /fi "imagename eq PiPiClaw.Team.exe" /fo list ^| findstr "PID"') do (
    taskkill /F /PID %%a >nul 2>&1
    if not errorlevel 1 echo [成功] 已停止 PiPiClaw.Team.exe PID: %%a
)

echo.
echo ========================================
echo     停止操作完成
echo ========================================
echo.
pause
