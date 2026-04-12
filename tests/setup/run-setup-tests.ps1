$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
$testPath = Join-Path $repoRoot 'tests\setup\SetupReport.Tests.ps1'

$result = Invoke-Pester -Script $testPath -PassThru
if ($result.FailedCount -gt 0) {
    exit 1
}
