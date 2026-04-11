$BootstrapPath = Join-Path $PSScriptRoot "bootstrap.ps1"

if (Test-Path $BootstrapPath) {
    . $BootstrapPath
} else {
    Write-Host "[SCC] 未找到 bootstrap.ps1: $BootstrapPath" -ForegroundColor Yellow
}
