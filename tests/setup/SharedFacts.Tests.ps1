$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot 'modules\common.ps1')

Describe 'shared environment facts' {
    It 'returns a combined environment facts object with host, tools, proxy, and install host' {
        $facts = Get-SccEnvironmentFacts

        $facts.Host | Should BeOfType psobject
        $facts.Tools.Count | Should BeGreaterThan 0
        $facts.ProxyEnvironment.Enabled | Should BeOfType bool
        $facts.InstallHost.IsAvailable | Should BeOfType bool
    }
}
