@echo off
chcp 65001 >nul
setlocal

set "ROOT=%~dp0"

echo ========================================
echo     WorldCup AI Company stop
echo ========================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%stop.ps1"

echo.
pause

