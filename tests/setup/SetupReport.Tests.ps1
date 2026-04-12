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

}
