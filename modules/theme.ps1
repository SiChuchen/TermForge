$commonPath = Join-Path $PSScriptRoot "common.ps1"
if (Test-Path $commonPath) {
    . $commonPath
}

Initialize-SccHelpRegistry
Register-SccHelp -ModuleName "theme" -HelpText "posh  - 查看主题模块状态`nposhl - 列出当前已安装主题`nposht <名称> - 临时测试主题`nposhs <名称> - 永久保存并应用主题"
Register-SccCommandHelp -CommandName "posh" -ModuleName "theme" -HelpText "用法: posh [-Help]`n作用: 显示当前主题、主题目录、启用状态和配置文件位置。"
Register-SccCommandHelp -CommandName "poshl" -ModuleName "theme" -HelpText "用法: poshl [-Help]`n作用: 列出主题目录中的所有 `.omp.json` 主题名称。"
Register-SccCommandHelp -CommandName "posht" -ModuleName "theme" -HelpText "用法: posht <名称> [-Help]`n作用: 临时切换主题，只对当前会话生效，不写入配置文件。"
Register-SccCommandHelp -CommandName "poshs" -ModuleName "theme" -HelpText "用法: poshs <名称> [-Help]`n作用: 永久切换主题，并把结果写回 scc.config.json。"

$config = Get-SccConfig
$script:ThemeEnabled = [bool]$config.theme.enabled
$script:ThemeDir = $config.theme.themeDir
$script:DefaultThemeName = $config.theme.defaultTheme
$script:ThemeCommandPath = $config.theme.commandPath
$script:CurrentThemeName = if (
    $config.theme.activeTheme -is [string] -and
    -not [string]::IsNullOrWhiteSpace($config.theme.activeTheme)
) {
    $config.theme.activeTheme
} else {
    $config.theme.defaultTheme
}
$script:ThemeStatus = "未初始化"
$script:ThemeLastError = $null

function Set-SccThemeRuntimeState {
    $config = Get-SccConfig
    $script:ThemeEnabled = [bool]$config.theme.enabled
    $script:ThemeDir = $config.theme.themeDir
    $script:DefaultThemeName = $config.theme.defaultTheme
    $script:ThemeCommandPath = $config.theme.commandPath
    $script:CurrentThemeName = if (
        $config.theme.activeTheme -is [string] -and
        -not [string]::IsNullOrWhiteSpace($config.theme.activeTheme)
    ) {
        $config.theme.activeTheme
    } else {
        $config.theme.defaultTheme
    }
}

function Ensure-SccThemeDirectory {
    if ([string]::IsNullOrWhiteSpace($script:ThemeDir)) {
        $script:ThemeStatus = "未配置主题目录，请检查 $(Get-SccConfigPath)"
        $script:ThemeLastError = $null
        return $false
    }

    if (-not (Test-Path $script:ThemeDir)) {
        try {
            New-Item -Path $script:ThemeDir -ItemType Directory -Force | Out-Null
        } catch {
            $script:ThemeStatus = "无法创建主题目录: $($_.Exception.Message)"
            $script:ThemeLastError = $null
            return $false
        }
    }

    return $true
}

function Get-SccThemeFilePath {
    param([Parameter(Mandatory)][string]$ThemeName)

    $requestedThemePath = Join-Path $script:ThemeDir "$ThemeName.omp.json"
    if (Test-Path $requestedThemePath) {
        return $requestedThemePath
    }

    $legacyAliases = @{
        "windows-terminal" = "termforge"
        "termforge"        = "windows-terminal"
    }

    if ($legacyAliases.ContainsKey($ThemeName)) {
        $aliasPath = Join-Path $script:ThemeDir "$($legacyAliases[$ThemeName]).omp.json"
        if (Test-Path $aliasPath) {
            return $aliasPath
        }
    }

    return $requestedThemePath
}

function Get-SccThemeActiveFilePath {
    return (Join-Path $script:ThemeDir "active.omp.json")
}

function Get-SccThemeCommand {
    return (Get-Command oh-my-posh -ErrorAction SilentlyContinue)
}

function Get-SccClinkExecutable {
    $config = Get-SccConfig
    if (
        (Test-SccPropertyObject -Value $config.cmd) -and
        $config.cmd.clinkPath -is [string] -and
        -not [string]::IsNullOrWhiteSpace($config.cmd.clinkPath) -and
        (Test-Path $config.cmd.clinkPath)
    ) {
        return $config.cmd.clinkPath
    }

    $clinkCommand = Get-Command clink -ErrorAction SilentlyContinue
    if ($null -ne $clinkCommand) {
        return $clinkCommand.Source
    }

    return $null
}

function Sync-SccThemeActiveFile {
    param([Parameter(Mandatory)][string]$ThemeName)

    if (-not (Ensure-SccThemeDirectory)) {
        return $false
    }

    $sourceThemePath = Get-SccThemeFilePath -ThemeName $ThemeName
    if (-not (Test-Path $sourceThemePath)) {
        $script:ThemeStatus = "主题文件不存在: $sourceThemePath"
        $script:ThemeLastError = $null
        return $false
    }

    try {
        Copy-Item -Path $sourceThemePath -Destination (Get-SccThemeActiveFilePath) -Force
        return $true
    } catch {
        $script:ThemeStatus = "无法写入 active.omp.json: $($_.Exception.Message)"
        $script:ThemeLastError = $null
        return $false
    }
}

function Sync-SccCmdThemeIntegration {
    param([Parameter(Mandatory)][string]$ThemeName)

    $config = Get-SccConfig
    if (-not (Test-SccPropertyObject -Value $config.cmd) -or -not [bool]$config.cmd.enabled) {
        return
    }

    $clinkExecutable = Get-SccClinkExecutable
    if ([string]::IsNullOrWhiteSpace($clinkExecutable)) {
        return
    }

    if (-not (Sync-SccThemeActiveFile -ThemeName $ThemeName)) {
        return
    }

    $scriptsPath = $config.cmd.scriptsPath
    if ([string]::IsNullOrWhiteSpace($scriptsPath) -or -not (Test-Path $scriptsPath)) {
        return
    }

    try {
        & $clinkExecutable installscripts $scriptsPath | Out-Null
    } catch {
        # Leave PowerShell theme activation unaffected if Clink sync fails.
    }
}

function Get-SccThemeExecutable {
    if (-not [string]::IsNullOrWhiteSpace($script:ThemeCommandPath)) {
        if (Test-Path $script:ThemeCommandPath) {
            return $script:ThemeCommandPath
        }

        $script:ThemeStatus = "配置的 oh-my-posh 路径不存在: $script:ThemeCommandPath"
        $script:ThemeLastError = $null
        return $null
    }

    if ($null -eq (Get-SccThemeCommand)) {
        return $null
    }

    return "oh-my-posh"
}

function Get-SccSelectedThemeName {
    if ([string]::IsNullOrWhiteSpace($script:CurrentThemeName)) {
        return $script:DefaultThemeName
    }

    return $script:CurrentThemeName
}

function Save-SccSelectedThemeName {
    param([Parameter(Mandatory)][string]$ThemeName)

    $config = Get-SccConfig
    $config.theme.activeTheme = $ThemeName
    Save-SccConfig -Config $config
    [void](Sync-SccThemeActiveFile -ThemeName $ThemeName)
    Sync-SccCmdThemeIntegration -ThemeName $ThemeName
    Set-SccThemeRuntimeState
}

function Invoke-SccThemeActivation {
    param(
        [Parameter(Mandatory)][string]$ThemeName,
        [switch]$Persist,
        [switch]$Quiet
    )

    if (-not $script:ThemeEnabled) {
        $script:ThemeStatus = "主题模块已关闭。"
        $script:ThemeLastError = $null
        if (-not $Quiet) {
            Write-Host "[Theme] $script:ThemeStatus" -ForegroundColor Yellow
        }
        return $false
    }

    if (-not (Ensure-SccThemeDirectory)) {
        if (-not $Quiet) {
            Write-Host "[Theme] $script:ThemeStatus" -ForegroundColor Yellow
        }
        return $false
    }

    $themePath = Get-SccThemeFilePath -ThemeName $ThemeName
    if (-not (Test-Path $themePath)) {
        $script:ThemeStatus = "主题文件不存在: $themePath"
        $script:ThemeLastError = $null
        if (-not $Quiet) {
            Write-Host "[Theme] $script:ThemeStatus" -ForegroundColor Yellow
        }
        return $false
    }

    $themeExecutable = Get-SccThemeExecutable
    if ([string]::IsNullOrWhiteSpace($themeExecutable)) {
        if ([string]::IsNullOrWhiteSpace($script:ThemeStatus)) {
            $script:ThemeStatus = "未找到 oh-my-posh，请先安装或修复 PATH。"
            $script:ThemeLastError = $null
        }
        if (-not $Quiet) {
            Write-Host "[Theme] $script:ThemeStatus" -ForegroundColor Yellow
        }
        return $false
    }

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Stop'

    try {
        & $themeExecutable init pwsh --config $themePath | Invoke-Expression

        if ($Persist) {
            Save-SccSelectedThemeName -ThemeName $ThemeName
        } else {
            $script:CurrentThemeName = $ThemeName
        }

        $script:ThemeStatus = "当前主题: $ThemeName"
        $script:ThemeLastError = $null
        return $true
    } catch {
        $script:ThemeStatus = "oh-my-posh 当前环境不可用，已跳过主题加载。"
        $script:ThemeLastError = $_.Exception.Message
        if (-not $Quiet) {
            Write-Host "[Theme] $script:ThemeStatus" -ForegroundColor Yellow
        }
        return $false
    } finally {
        $ErrorActionPreference = $previousPreference
    }
}

if ($script:ThemeEnabled) {
    [void](Sync-SccThemeActiveFile -ThemeName (Get-SccSelectedThemeName))
    Sync-SccCmdThemeIntegration -ThemeName (Get-SccSelectedThemeName)
    [void](Invoke-SccThemeActivation -ThemeName (Get-SccSelectedThemeName) -Quiet)
} else {
    $script:ThemeStatus = "主题模块已关闭。"
    $script:ThemeLastError = $null
}

function posh {
    param(
        [string]$Action,
        [switch]$Help
    )

    if ($Help -or $Action -eq "help") {
        [void](Show-SccModuleHelp -ModuleName "theme")
        return
    }

    $currentTheme = if ([string]::IsNullOrWhiteSpace($script:CurrentThemeName)) {
        "未加载"
    } else {
        $script:CurrentThemeName
    }

    Write-Host "=== 主题模块 (Oh My Posh) ===" -ForegroundColor Cyan
    Write-Host "已启用     : $script:ThemeEnabled"
    Write-Host "当前主题   : $currentTheme"
    Write-Host "默认主题   : $script:DefaultThemeName"
    Write-Host "主题目录   : $script:ThemeDir"
    Write-Host "运行状态   : $script:ThemeStatus"
    if (-not [string]::IsNullOrWhiteSpace($script:ThemeLastError)) {
        Write-Host "详细错误   : $script:ThemeLastError"
    }
    Write-Host "配置文件   : $(Get-SccConfigPath)"
}

function posht {
    param(
        [string]$name,
        [switch]$Help
    )

    if ($Help -or $name -eq "help") {
        [void](Show-SccCommandHelp -CommandName "posht")
        return
    }

    if ([string]::IsNullOrWhiteSpace($name)) {
        Write-Host "请指定主题名。" -ForegroundColor Red
        return
    }

    if (Invoke-SccThemeActivation -ThemeName $name) {
        Write-Host "已临时切换至: $name" -ForegroundColor Cyan
    }
}

function poshs {
    param(
        [string]$name,
        [switch]$Help
    )

    if ($Help -or $name -eq "help") {
        [void](Show-SccCommandHelp -CommandName "poshs")
        return
    }

    if ([string]::IsNullOrWhiteSpace($name)) {
        Write-Host "请指定主题名。" -ForegroundColor Red
        return
    }

    if (Invoke-SccThemeActivation -ThemeName $name -Persist) {
        Write-Host "已永久写入配置: $name" -ForegroundColor Green
    }
}

function poshl {
    param(
        [string]$Action,
        [switch]$Help
    )

    if ($Help -or $Action -eq "help") {
        [void](Show-SccCommandHelp -CommandName "poshl")
        return
    }

    if (-not (Ensure-SccThemeDirectory)) {
        Write-Host "[Theme] $script:ThemeStatus" -ForegroundColor Yellow
        return
    }

    Get-ChildItem -Path $script:ThemeDir -Filter *.omp.json -File | ForEach-Object {
        $_.BaseName -replace '\.omp$', ''
    }
}
