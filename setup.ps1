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

. (Join-Path $PSScriptRoot 'modules\common.ps1')

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

$environmentFacts = Get-SccEnvironmentFacts -SkipDependencyInstallFlag:$SkipDependencyInstall
$structuredReport = Get-SccSetupEnvironmentReport -EnvironmentFacts $environmentFacts -SkipDependencyInstallFlag:$SkipDependencyInstall

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

$hostExecutable = $environmentFacts.InstallHost.ExecutablePath
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
