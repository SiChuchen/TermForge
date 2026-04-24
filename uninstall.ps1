[CmdletBinding()]
param(
    [string]$InstallRoot = $(
        if (Test-Path (Join-Path $env:LOCALAPPDATA "TermForge")) {
            Join-Path $env:LOCALAPPDATA "TermForge"
        } elseif (Test-Path (Join-Path $env:LOCALAPPDATA "windows-terminal")) {
            Join-Path $env:LOCALAPPDATA "windows-terminal"
        } else {
            Join-Path $env:LOCALAPPDATA "TermForge"
        }
    ),
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$installHelpersPath = Join-Path $PSScriptRoot "modules\install-helpers.ps1"
if (Test-Path $installHelpersPath) {
    . $installHelpersPath
} else {
    throw "未找到 install-helpers.ps1: $installHelpersPath"
}

function Write-SccUninstallStep {
    param([Parameter(Mandatory)][string]$Message)

    Write-Host "[uninstall] $Message" -ForegroundColor Cyan
}

function Read-SccUninstallBool {
    param(
        [Parameter(Mandatory)][string]$Prompt,
        [bool]$Default = $false
    )

    $defaultToken = if ($Default) { "Y/n" } else { "y/N" }
    while ($true) {
        $value = Read-Host "$Prompt [$defaultToken]"
        if ([string]::IsNullOrWhiteSpace($value)) {
            return $Default
        }

        switch ($value.Trim().ToLowerInvariant()) {
            "y" { return $true }
            "yes" { return $true }
            "n" { return $false }
            "no" { return $false }
            default { Write-Host "请输入 y 或 n。" -ForegroundColor Yellow }
        }
    }
}

function Remove-SccManagedProfileBlock {
    param([Parameter(Mandatory)][string]$ProfilePath)

    if (-not (Test-Path $ProfilePath)) {
        return
    }

    $existingContent = Get-Content -Path $ProfilePath -Raw
    $cleanContent = Remove-SccManagedBlock -Content $existingContent
    Set-Content -Path $ProfilePath -Value $cleanContent -Encoding UTF8
}

function Remove-SccUserPathEntry {
    param([Parameter(Mandatory)][string]$InstallRoot)

    $currentPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $parts = @($currentPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -ne $InstallRoot })
    [Environment]::SetEnvironmentVariable("Path", ($parts -join ';'), "User")
}

function Get-SccInstallConfig {
    param([Parameter(Mandatory)][string]$InstallRoot)

    $configPath = Join-Path $InstallRoot "scc.config.json"
    if (-not (Test-Path $configPath)) {
        return $null
    }

    return (Get-Content -Path $configPath -Raw | ConvertFrom-Json)
}

$resolvedInstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)
if (-not (Test-Path $resolvedInstallRoot)) {
    Write-Host "未找到安装目录: $resolvedInstallRoot" -ForegroundColor Yellow
    exit 0
}

$shouldRemove = $Force
if (-not $Force) {
    $shouldRemove = Read-SccUninstallBool -Prompt "确认卸载 $resolvedInstallRoot 并移除受管集成吗" -Default $false
}

if (-not $shouldRemove) {
    Write-Host "已取消卸载。" -ForegroundColor Yellow
    exit 0
}

$config = Get-SccInstallConfig -InstallRoot $resolvedInstallRoot
if ($null -ne $config) {
    foreach ($profilePath in @($config.install.managedProfiles.powershell, $config.install.managedProfiles.vscode)) {
        if ($profilePath -is [string] -and -not [string]::IsNullOrWhiteSpace($profilePath)) {
            Remove-SccManagedProfileBlock -ProfilePath $profilePath
            Write-SccUninstallStep "已清理 profile 注入: $profilePath"
        }
    }

    if ($config.install.addToPath -eq $true) {
        Remove-SccUserPathEntry -InstallRoot $resolvedInstallRoot
        Write-SccUninstallStep "已从用户 PATH 移除安装目录。"
    }

    if ($config.cmd.enabled -eq $true -and $config.cmd.scriptsPath -is [string] -and -not [string]::IsNullOrWhiteSpace($config.cmd.scriptsPath)) {
        $clinkExecutable = if (
            $config.cmd.clinkPath -is [string] -and
            -not [string]::IsNullOrWhiteSpace($config.cmd.clinkPath) -and
            (Test-Path $config.cmd.clinkPath)
        ) {
            $config.cmd.clinkPath
        } else {
            Find-SccClinkExecutable
        }

        if (-not [string]::IsNullOrWhiteSpace($clinkExecutable)) {
            try {
                & $clinkExecutable uninstallscripts $config.cmd.scriptsPath | Out-Null
                Write-SccUninstallStep "已移除 Clink script 注册: $($config.cmd.scriptsPath)"
            } catch {
                Write-Host "[uninstall] Clink script 注销失败: $($_.Exception.Message)" -ForegroundColor Yellow
            }
        }
    }
}

Remove-Item -Path $resolvedInstallRoot -Recurse -Force
Write-SccUninstallStep "已删除安装目录: $resolvedInstallRoot"
Write-Host "卸载完成。" -ForegroundColor Green
