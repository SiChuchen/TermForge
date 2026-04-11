[CmdletBinding()]
param(
    [string]$InstallRoot,
    [switch]$SkipDependencyInstall,
    [switch]$SkipVerification
)

$ErrorActionPreference = "Stop"

function Get-SccSetupCommandSource {
    param([Parameter(Mandatory)][string]$CommandName)

    $command = Get-Command $CommandName -ErrorAction SilentlyContinue
    if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        return $command.Source
    }

    try {
        $candidate = & where.exe $CommandName 2>$null |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) } |
            Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            return $candidate
        }
    } catch {
    }

    return $null
}

function Get-SccSetupWindowsPowerShellPath {
    $candidates = @(
        (Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe")
        (Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe")
        "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return (Get-SccSetupCommandSource -CommandName "powershell.exe")
}

function Test-SccWritablePath {
    param([Parameter(Mandatory)][string]$Path)

    try {
        if (-not (Test-Path $Path)) {
            New-Item -Path $Path -ItemType Directory -Force | Out-Null
        }

        $probePath = Join-Path $Path ".termforge-write-probe"
        Set-Content -Path $probePath -Value "ok" -Encoding ASCII
        Remove-Item -Path $probePath -Force
        return $true
    } catch {
        return $false
    }
}

function Get-SccSetupReport {
    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $documentsPath = [Environment]::GetFolderPath("MyDocuments")
    $osVersion = [Environment]::OSVersion.Version
    $isWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)

    return [pscustomobject]@{
        IsWindows          = $isWindowsPlatform
        OsVersion          = $osVersion
        OsVersionText      = $osVersion.ToString()
        IsSupportedWindows = $isWindowsPlatform -and $osVersion.Major -ge 10
        PowerShellEdition  = $PSVersionTable.PSEdition
        PowerShellVersion  = $PSVersionTable.PSVersion.ToString()
        LocalAppData       = $localAppData
        DocumentsPath      = $documentsPath
        CanWriteLocalAppData = (
            -not [string]::IsNullOrWhiteSpace($localAppData) -and
            (Test-SccWritablePath -Path $localAppData)
        )
        HasWinget          = $null -ne (Get-SccSetupCommandSource -CommandName "winget")
        HasPwsh            = $null -ne (Get-SccSetupCommandSource -CommandName "pwsh")
        HasOhMyPosh        = $null -ne (Get-SccSetupCommandSource -CommandName "oh-my-posh")
        HasWindowsTerminal = $null -ne (Get-SccSetupCommandSource -CommandName "wt")
        HasClink           = $null -ne (Get-SccSetupCommandSource -CommandName "clink")
        HasVSCode          = $null -ne (Get-SccSetupCommandSource -CommandName "code")
    }
}

function Show-SccSetupSummary {
    param([Parameter(Mandatory)]$Report)

    Write-Host ""
    Write-Host "TermForge Setup" -ForegroundColor Cyan
    Write-Host "系统        : Windows $($Report.OsVersionText)"
    Write-Host "PowerShell  : $($Report.PowerShellEdition) $($Report.PowerShellVersion)"
    Write-Host "winget      : $($Report.HasWinget)"
    Write-Host "pwsh        : $($Report.HasPwsh)"
    Write-Host "oh-my-posh  : $($Report.HasOhMyPosh)"
    Write-Host "wt          : $($Report.HasWindowsTerminal)"
    Write-Host "clink       : $($Report.HasClink)"
    Write-Host "VS Code     : $($Report.HasVSCode)"
    Write-Host "LOCALAPPDATA: $($Report.LocalAppData)"
    Write-Host ""
}

function Get-SccSetupBlockingIssues {
    param(
        [Parameter(Mandatory)]$Report,
        [Parameter(Mandatory)][bool]$SkipDependencyInstallFlag
    )

    $issues = @()

    if (-not $Report.IsWindows) {
        $issues += "当前系统不是 Windows，TermForge 目前只支持 Windows 10 / 11。"
    } elseif (-not $Report.IsSupportedWindows) {
        $issues += "当前系统版本低于 Windows 10，TermForge 不支持该环境。"
    }

    if (-not $Report.CanWriteLocalAppData) {
        $issues += "无法写入 LOCALAPPDATA，安装器无法创建默认安装目录。"
    }

    if (-not $Report.HasOhMyPosh) {
        if ($SkipDependencyInstallFlag) {
            $issues += "当前缺少 oh-my-posh，且你要求跳过依赖安装。TermForge 无法继续。"
        } elseif (-not $Report.HasWinget) {
            $issues += "当前缺少 oh-my-posh，且未检测到 winget，安装器无法自动补齐必需依赖。"
        }
    }

    return @($issues)
}

function Show-SccSetupWarnings {
    param(
        [Parameter(Mandatory)]$Report,
        [Parameter(Mandatory)][bool]$SkipDependencyInstallFlag
    )

    if (-not $Report.HasWinget) {
        Write-Host "[setup] 未检测到 winget；只能依赖本机已存在的组件，无法自动安装可选依赖。" -ForegroundColor Yellow
    }

    if (-not $Report.HasPwsh) {
        Write-Host "[setup] 未检测到 PowerShell 7；如果后续选择 PowerShell / VS Code 集成，建议允许安装器补齐 pwsh。" -ForegroundColor Yellow
    }

    if (-not $Report.HasWindowsTerminal) {
        Write-Host "[setup] 未检测到 Windows Terminal；这不是阻塞项，如果你只在 VS Code 或 PowerShell 中使用，可以在向导里关闭它。" -ForegroundColor Yellow
    }

    if (-not $Report.HasVSCode) {
        Write-Host "[setup] 未检测到 VS Code；后续向导里可以直接关闭 VS Code 集成。" -ForegroundColor Yellow
    }

    if (-not $Report.HasClink) {
        Write-Host "[setup] 未检测到 Clink；如果你不需要 CMD 集成，可以在向导里关闭它。" -ForegroundColor Yellow
    }

    if ($SkipDependencyInstallFlag) {
        Write-Host "[setup] 已启用 SkipDependencyInstall；缺失组件不会自动安装。" -ForegroundColor Yellow
    }
}

function Read-SccSetupContinue {
    while ($true) {
        $value = Read-Host "继续进入安装向导吗 [Y/n]"
        if ([string]::IsNullOrWhiteSpace($value)) {
            return $true
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

$report = Get-SccSetupReport
Show-SccSetupSummary -Report $report

$blockingIssues = @(Get-SccSetupBlockingIssues -Report $report -SkipDependencyInstallFlag:$SkipDependencyInstall)
if ($blockingIssues.Count -gt 0) {
    foreach ($issue in $blockingIssues) {
        Write-Host "[setup] $issue" -ForegroundColor Red
    }
    exit 1
}

Show-SccSetupWarnings -Report $report -SkipDependencyInstallFlag:$SkipDependencyInstall

Write-Host ""
Write-Host "接下来会进入交互式安装向导，由你选择主命令名、宿主集成、代理与依赖策略。" -ForegroundColor DarkGray
if (-not (Read-SccSetupContinue)) {
    Write-Host "已取消安装。" -ForegroundColor Yellow
    exit 0
}

$installScript = Join-Path $PSScriptRoot "install.ps1"
if (-not (Test-Path $installScript)) {
    throw "未找到 install.ps1: $installScript"
}

$hostExecutable = Get-SccSetupWindowsPowerShellPath
if ([string]::IsNullOrWhiteSpace($hostExecutable)) {
    $hostExecutable = Get-SccSetupCommandSource -CommandName "pwsh"
}
if ([string]::IsNullOrWhiteSpace($hostExecutable)) {
    throw "未找到可用的 PowerShell 宿主，无法启动 install.ps1。"
}

$arguments = @(
    "-NoLogo"
    "-NoProfile"
    "-ExecutionPolicy"
    "Bypass"
    "-File"
    $installScript
)

if (-not [string]::IsNullOrWhiteSpace($InstallRoot)) {
    $arguments += @("-InstallRoot", $InstallRoot)
}
if ($SkipDependencyInstall) {
    $arguments += "-SkipDependencyInstall"
}
if ($SkipVerification) {
    $arguments += "-SkipVerification"
}

& $hostExecutable @arguments
exit $LASTEXITCODE
