@echo off
chcp 65001 >nul
setlocal

set "ROOT=%~dp0"

echo ========================================
echo     WorldCup AI Company startup
echo ========================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%start.ps1"

echo.
pause

