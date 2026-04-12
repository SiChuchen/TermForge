[CmdletBinding()]
param(
    [string]$InstallRoot,
    [switch]$SkipDependencyInstall,
    [switch]$SkipVerification,
    [switch]$Json,
    [switch]$Report
)

$ErrorActionPreference = "Stop"

if ($Json -and $Report) {
    throw 'setup.ps1 不支持同时使用 --json 和 --report。'
}

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

function Get-SccSetupInstallHostInfo {
    $windowsPowerShellPath = Get-SccSetupWindowsPowerShellPath
    if (-not [string]::IsNullOrWhiteSpace($windowsPowerShellPath)) {
        return [pscustomobject]@{
            HostExecutable = $windowsPowerShellPath
            Source         = 'windows-powershell'
            IsAvailable    = $true
        }
    }

    $pwshPath = Get-SccSetupCommandSource -CommandName "pwsh"
    if (-not [string]::IsNullOrWhiteSpace($pwshPath)) {
        return [pscustomobject]@{
            HostExecutable = $pwshPath
            Source         = 'pwsh'
            IsAvailable    = $true
        }
    }

    return [pscustomobject]@{
        HostExecutable = $null
        Source         = 'none'
        IsAvailable    = $false
    }
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
        [Parameter(Mandatory)]$InstallHostInfo,
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
        } elseif (-not $InstallHostInfo.IsAvailable) {
            $issues += "当前缺少 oh-my-posh，且未找到可用的 PowerShell 宿主来启动 install.ps1。"
        }
    }

    if (-not $InstallHostInfo.IsAvailable) {
        $issues += "未找到可用的 PowerShell 宿主，无法启动 install.ps1。"
    }

    return @($issues)
}

function New-SccSetupToolResult {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][bool]$Detected,
        [string]$CommandPath,
        [Parameter(Mandatory)][bool]$Required,
        [Parameter(Mandatory)][ValidateSet('PASS','WARN','FAIL')][string]$Status,
        [Parameter(Mandatory)][string]$Message
    )

    [pscustomobject][ordered]@{
        Name        = $Name
        Detected    = $Detected
        CommandPath = $CommandPath
        Required    = $Required
        Status      = $Status
        Message     = $Message
    }
}

function Get-SccSetupProxyEnvironment {
    $http = if ($env:http_proxy) { $env:http_proxy } elseif ($env:HTTP_PROXY) { $env:HTTP_PROXY } else { '' }
    $https = if ($env:https_proxy) { $env:https_proxy } elseif ($env:HTTPS_PROXY) { $env:HTTPS_PROXY } else { '' }
    $noProxy = if ($env:no_proxy) { $env:no_proxy } elseif ($env:NO_PROXY) { $env:NO_PROXY } else { '' }
    $enabled = (-not [string]::IsNullOrWhiteSpace($http)) -or (-not [string]::IsNullOrWhiteSpace($https))

    [pscustomobject][ordered]@{
        Enabled    = $enabled
        HttpProxy  = $http
        HttpsProxy = $https
        NoProxy    = $noProxy
        Source     = if ($enabled) { 'process' } else { 'none' }
        Status     = if ($enabled) { 'WARN' } else { 'PASS' }
    }
}

function Get-SccSetupToolReport {
    param(
        [Parameter(Mandatory)]$EnvironmentReport,
        [Parameter(Mandatory)]$InstallHostInfo,
        [Parameter(Mandatory)][bool]$SkipDependencyInstallFlag
    )

    $toolSpecs = @(
        @{ Name = 'winget'; Required = $false }
        @{ Name = 'pwsh'; Required = $false }
        @{ Name = 'oh-my-posh'; Required = $true }
        @{ Name = 'wt'; Required = $false }
        @{ Name = 'clink'; Required = $false }
        @{ Name = 'code'; Required = $false }
        @{ Name = 'git'; Required = $false }
        @{ Name = 'npm'; Required = $false }
        @{ Name = 'pnpm'; Required = $false }
        @{ Name = 'yarn'; Required = $false }
        @{ Name = 'pip'; Required = $false }
        @{ Name = 'uv'; Required = $false }
        @{ Name = 'cargo'; Required = $false }
        @{ Name = 'docker'; Required = $false }
    )

    foreach ($toolSpec in $toolSpecs) {
        $path = Get-SccSetupCommandSource -CommandName $toolSpec.Name
        $detected = -not [string]::IsNullOrWhiteSpace($path)
        $canAutoInstall = (
            $toolSpec.Required -and
            -not $detected -and
            -not $SkipDependencyInstallFlag -and
            $EnvironmentReport.HasWinget -and
            $InstallHostInfo.IsAvailable
        )

        $status = if ($detected) {
            'PASS'
        } elseif ($toolSpec.Required -and -not $canAutoInstall) {
            'FAIL'
        } else {
            'WARN'
        }

        $message = if ($detected) {
            $path
        } elseif ($toolSpec.Required -and $SkipDependencyInstallFlag) {
            'required but missing; dependency install is skipped'
        } elseif ($toolSpec.Required -and -not $EnvironmentReport.HasWinget) {
            'required but missing; automatic install is unavailable'
        } elseif ($toolSpec.Required -and -not $InstallHostInfo.IsAvailable) {
            'required but missing; install host is unavailable'
        } elseif ($toolSpec.Required) {
            'required but missing; can be installed automatically'
        } else {
            'optional but missing'
        }

        New-SccSetupToolResult `
            -Name $toolSpec.Name `
            -Detected $detected `
            -CommandPath $path `
            -Required $toolSpec.Required `
            -Status $status `
            -Message $message
    }
}

function Get-SccSetupStructuredReport {
    param(
        $EnvironmentReport,
        $InstallHostInfo,
        [bool]$SkipDependencyInstallFlag = $SkipDependencyInstall
    )

    $environment = if ($null -ne $EnvironmentReport) { $EnvironmentReport } else { Get-SccSetupReport }
    $resolvedInstallHostInfo = if ($null -ne $InstallHostInfo) { $InstallHostInfo } else { Get-SccSetupInstallHostInfo }
    $tools = @(
        Get-SccSetupToolReport `
            -EnvironmentReport $environment `
            -InstallHostInfo $resolvedInstallHostInfo `
            -SkipDependencyInstallFlag:$SkipDependencyInstallFlag
    )
    $proxyEnvironment = Get-SccSetupProxyEnvironment
    $blockingIssues = @(
        Get-SccSetupBlockingIssues `
            -Report $environment `
            -InstallHostInfo $resolvedInstallHostInfo `
            -SkipDependencyInstallFlag:$SkipDependencyInstallFlag
    )
    $warnings = @()

    $warnings += @(
        $tools |
            Where-Object { $_.Status -eq 'WARN' } |
            ForEach-Object { '{0}: {1}' -f $_.Name, $_.Message }
    )

    if ($proxyEnvironment.Status -eq 'WARN') {
        $warnings += 'Proxy environment variables are enabled for this process.'
    }

    [pscustomobject][ordered]@{
        SchemaVersion    = '2026-04-12'
        GeneratedAt      = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        OverallStatus    = if ($blockingIssues.Count -gt 0) { 'FAIL' } elseif ($warnings.Count -gt 0) { 'WARN' } else { 'PASS' }
        BlockingIssues   = @($blockingIssues)
        Warnings         = @($warnings)
        Environment      = [pscustomobject][ordered]@{
            IsWindows           = $environment.IsWindows
            OsVersion           = $environment.OsVersionText
            PowerShellEdition   = $environment.PowerShellEdition
            PowerShellVersion   = $environment.PowerShellVersion
            LocalAppData        = $environment.LocalAppData
            DocumentsPath       = $environment.DocumentsPath
            CanWriteLocalAppData = $environment.CanWriteLocalAppData
        }
        Tools            = @($tools)
        ProxyEnvironment = $proxyEnvironment
        InstallReadiness = [pscustomobject][ordered]@{
            CanContinue               = ($blockingIssues.Count -eq 0)
            RequiresDependencyInstall = @(
                $tools |
                    Where-Object {
                        $_.Required -and
                        -not $_.Detected -and
                        $_.Status -eq 'WARN'
                    }
            ).Count -gt 0
            BlockingIssueCount        = $blockingIssues.Count
            WarningCount              = $warnings.Count
            RecommendedInstallMode    = if (-not $environment.HasWinget -and -not $environment.HasOhMyPosh) { 'manual-deps-required' } elseif (-not $environment.HasWindowsTerminal) { 'without-terminal' } elseif (-not $environment.HasClink) { 'without-cmd' } else { 'full' }
        }
    }
}

function Show-SccSetupReport {
    param([Parameter(Mandatory)]$StructuredReport)

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('')
    $lines.Add('TermForge Setup Report')
    $lines.Add(('Overall status  : {0}' -f $StructuredReport.OverallStatus))
    $lines.Add(('OS              : {0}' -f $StructuredReport.Environment.OsVersion))
    $lines.Add(('PowerShell      : {0} {1}' -f $StructuredReport.Environment.PowerShellEdition, $StructuredReport.Environment.PowerShellVersion))
    $lines.Add(('LOCALAPPDATA    : {0}' -f $StructuredReport.Environment.LocalAppData))
    $lines.Add(('Writable        : {0}' -f $StructuredReport.Environment.CanWriteLocalAppData))
    $lines.Add('')
    $lines.Add('Tools')

    foreach ($tool in $StructuredReport.Tools) {
        $lines.Add(('  {0,-12} {1,-4} {2}' -f $tool.Name, $tool.Status, $tool.Message))
    }

    $lines.Add('')
    $lines.Add('Proxy environment')
    $lines.Add(('  Enabled : {0}' -f $StructuredReport.ProxyEnvironment.Enabled))
    $lines.Add(('  HTTP    : {0}' -f $StructuredReport.ProxyEnvironment.HttpProxy))
    $lines.Add(('  HTTPS   : {0}' -f $StructuredReport.ProxyEnvironment.HttpsProxy))
    $lines.Add(('  NO_PROXY: {0}' -f $StructuredReport.ProxyEnvironment.NoProxy))
    $lines.Add('')
    $lines.Add('Install readiness')
    $lines.Add(('  CanContinue              : {0}' -f $StructuredReport.InstallReadiness.CanContinue))
    $lines.Add(('  RequiresDependencyInstall: {0}' -f $StructuredReport.InstallReadiness.RequiresDependencyInstall))
    $lines.Add(('  RecommendedInstallMode   : {0}' -f $StructuredReport.InstallReadiness.RecommendedInstallMode))

    if ($StructuredReport.BlockingIssues.Count -gt 0) {
        $lines.Add('')
        $lines.Add('Blocking issues')
        foreach ($issue in $StructuredReport.BlockingIssues) {
            $lines.Add(('  - {0}' -f $issue))
        }
    }

    if ($StructuredReport.Warnings.Count -gt 0) {
        $lines.Add('')
        $lines.Add('Warnings')
        foreach ($warning in $StructuredReport.Warnings) {
            $lines.Add(('  - {0}' -f $warning))
        }
    }

    Write-Output $lines
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

$setupReport = Get-SccSetupReport
$installHostInfo = Get-SccSetupInstallHostInfo
$structuredReport = Get-SccSetupStructuredReport -EnvironmentReport $setupReport -InstallHostInfo $installHostInfo -SkipDependencyInstallFlag:$SkipDependencyInstall

if ($Json) {
    $structuredReport | ConvertTo-Json -Depth 8
    exit 0
}

if ($Report) {
    Show-SccSetupReport -StructuredReport $structuredReport
    exit 0
}

Show-SccSetupReport -StructuredReport $structuredReport

if ($structuredReport.InstallReadiness.BlockingIssueCount -gt 0) {
    exit 1
}

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

$hostExecutable = $installHostInfo.HostExecutable
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
