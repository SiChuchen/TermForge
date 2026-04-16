@echo off
chcp 65001 >nul 2>&1
setlocal
set "TF_PWSH=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%TF_PWSH%" (
    "%TF_PWSH%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" %*
) else (
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" %*
)
set "WT_INSTALL_EXIT=%ERRORLEVEL%"
if not "%WT_INSTALL_EXIT%"=="0" (
    echo.
    echo [TermForge] 安装失败，错误码 %WT_INSTALL_EXIT%。
    pause
)
exit /b %WT_INSTALL_EXIT%
