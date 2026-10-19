@echo off
chcp 65001 >nul
cd /d "%~dp0"
Installer.exe %*
echo.
pause
