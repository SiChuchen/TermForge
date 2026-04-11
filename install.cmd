@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
set "WT_INSTALL_EXIT=%ERRORLEVEL%"
if not "%WT_INSTALL_EXIT%"=="0" (
    echo.
    echo [TermForge] 安装失败，错误码 %WT_INSTALL_EXIT%。
    pause
)
exit /b %WT_INSTALL_EXIT%
