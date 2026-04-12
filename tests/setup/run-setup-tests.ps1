$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
$testPath = Join-Path $repoRoot 'tests\setup\SetupReport.Tests.ps1'
$sharedFactsPath = Join-Path $repoRoot 'tests\setup\SharedFacts.Tests.ps1'

$invokePester = Get-Command Invoke-Pester -ErrorAction Stop
$scriptParameter = $invokePester.Parameters['Script']

if ($null -ne $scriptParameter -and $scriptParameter.ParameterType -eq [string[]]) {
    $result = Invoke-Pester -Script @($testPath, $sharedFactsPath) -PassThru
} else {
    $result = @(
        Invoke-Pester -Script $testPath -PassThru
        Invoke-Pester -Script $sharedFactsPath -PassThru
    )
}

if (($result | Measure-Object -Property FailedCount -Sum).Sum -gt 0) {
    exit 1
}

if ($result.FailedCount -gt 0) {
    exit 1
}
