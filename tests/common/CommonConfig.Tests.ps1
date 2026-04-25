$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot 'modules\common.ps1')

Describe 'common config helpers' {
    It 'does not treat scalar strings as property objects' {
        Test-SccPropertyObject -Value 'C:\Users\Test\AppData\Local\TermForge' | Should Be $false
    }

    It 'treats PSCustomObject values as property objects' {
        Test-SccPropertyObject -Value ([pscustomobject]@{ name = 'value' }) | Should Be $true
    }
}
