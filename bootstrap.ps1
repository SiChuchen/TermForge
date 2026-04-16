$script:SccRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$commonPath = Join-Path $script:SccRoot "modules\common.ps1"

if (-not (Test-Path $commonPath)) {
    Write-Host "[SCC] 缺少公共模块: $commonPath" -ForegroundColor Red
    return
}

. $commonPath
Initialize-SccHelpRegistry

$managerPath = Join-Path (Get-SccModulesPath) "manager.ps1"
if (Test-Path $managerPath) {
    try {
        . $managerPath
        if (Get-Command Register-SccManagerCommands -ErrorAction SilentlyContinue) {
            Register-SccManagerCommands
        }
    } catch {
        Write-Host "[SCC] 模块 'manager' 加载失败: $($_.Exception.Message)" -ForegroundColor Yellow
    }
} else {
    Write-Host "[SCC] 未找到模块管理器: $managerPath" -ForegroundColor Red
}

try {
    $moduleState = Ensure-SccStateFile
} catch {
    Write-Host "[SCC] 模块状态文件初始化失败: $($_.Exception.Message)" -ForegroundColor Red
    return
}

foreach ($moduleName in Get-SccAvailableModuleNames) {
    $isEnabled = $false
    if ($moduleState.PSObject.Properties.Match($moduleName).Count -gt 0) {
        $isEnabled = [bool]$moduleState.$moduleName
    }

    if (-not $isEnabled) {
        continue
    }

    $modulePath = Join-Path (Get-SccModulesPath) "$moduleName.ps1"
    try {
        . $modulePath
    } catch {
        Write-Host "[SCC] 模块 '$moduleName' 加载失败: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}
