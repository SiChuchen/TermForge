$ErrorActionPreference = 'Stop'

Describe 'setup.ps1 report modes' {
    It 'returns a JSON report without entering install when --json is used' {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $setupPath = Join-Path $repoRoot 'setup.ps1'

        $json = & pwsh -NoLogo -NoProfile -File $setupPath --json | Out-String
        $report = $json | ConvertFrom-Json

        $report.SchemaVersion | Should Be '2026-04-12'
        $report.Environment.IsWindows | Should BeOfType bool
        $report.Tools.Count | Should BeGreaterThan 0
        $report.InstallReadiness.CanContinue | Should BeOfType bool
    }

    It 'includes expanded tools and proxy environment in the JSON report' {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $setupPath = Join-Path $repoRoot 'setup.ps1'

        $json = & pwsh -NoLogo -NoProfile -File $setupPath --json | Out-String
        $report = $json | ConvertFrom-Json

        @('winget','pwsh','oh-my-posh','wt','clink','code','git','npm','pnpm','yarn','pip','uv','cargo','docker') |
            ForEach-Object {
                ($report.Tools | Where-Object Name -eq $_).Count | Should Be 1
            }

        $report.ProxyEnvironment.Enabled | Should BeOfType bool
        $report.ProxyEnvironment.Status | Should Match '^(PASS|WARN|FAIL)$'
    }

    It 'reuses the collected environment snapshot when building the structured report' {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $setupPath = Join-Path $repoRoot 'setup.ps1'
        $content = Get-Content -Path $setupPath -Raw
        $entrypointPattern = [regex]::Escape('$structuredReport = Get-SccSetupStructuredReport -EnvironmentReport $setupReport -InstallHostInfo $installHostInfo -SkipDependencyInstallFlag:$SkipDependencyInstall')

        $content | Should Match $entrypointPattern
        $content | Should Match 'function Get-SccSetupStructuredReport \{[\s\S]*\$environment = if \(\$null -ne \$EnvironmentReport\) \{ \$EnvironmentReport \} else \{ Get-SccSetupReport \}'
    }

    It 'treats recoverable required oh-my-posh misses as warnings instead of blockers' {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $setupPath = Join-Path $repoRoot 'setup.ps1'
        $content = Get-Content -Path $setupPath -Raw

        $content | Should Match '\$canAutoInstall = \([\s\S]*\$EnvironmentReport\.HasWinget[\s\S]*\$InstallHostInfo\.IsAvailable[\s\S]*\)'
        $content | Should Match 'required but missing; can be installed automatically'
        $content | Should Match 'RequiresDependencyInstall = @\([\s\S]*\$_.Required -and[\s\S]*\$_.Status -eq ''WARN'''
    }

    It 'blocks readiness when no PowerShell host can launch install.ps1' {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $setupPath = Join-Path $repoRoot 'setup.ps1'
        $content = Get-Content -Path $setupPath -Raw

        $content | Should Match 'function Get-SccSetupInstallHostInfo'
        $content | Should Match '未找到可用的 PowerShell 宿主，无法启动 install.ps1。'
        $content | Should Match 'CanContinue\s*=\s*\(\$blockingIssues\.Count -eq 0\)'
    }
}
