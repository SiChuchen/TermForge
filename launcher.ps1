[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [object[]]$RemainingArgs
)

$ErrorActionPreference = "Stop"
$script:SccRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$bootstrapPath = Join-Path $script:SccRoot "bootstrap.ps1"

if (-not (Test-Path $bootstrapPath)) {
    throw "未找到 bootstrap.ps1: $bootstrapPath"
}

. $bootstrapPath

if (-not (Get-Command Invoke-SccManagerCommand -ErrorAction SilentlyContinue)) {
    throw "未加载 Invoke-SccManagerCommand。"
}

Invoke-SccManagerCommand @RemainingArgs
