$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
$testPath = Join-Path $repoRoot 'tests\\setup\\SetupReport.Tests.ps1'

Invoke-Pester -Script $testPath
