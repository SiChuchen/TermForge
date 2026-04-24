$commonPath = Join-Path $PSScriptRoot "common.ps1"
if (Test-Path $commonPath) {
    . $commonPath
}

Initialize-SccHelpRegistry
Register-SccHelp -ModuleName "theme" -HelpText "posh  - 查看主题模块状态`nposhl - 列出当前已安装主题`nposhl --available - 列出 oh-my-posh 远程可用主题`nposht <名称> - 临时测试主题（本地没有会自动下载）`nposhs <名称> - 永久保存并应用主题（本地没有会自动下载）"
Register-SccCommandHelp -CommandName "posh" -ModuleName "theme" -HelpText "用法: posh [-Help]`n作用: 显示当前主题、主题目录、启用状态和配置文件位置。"
Register-SccCommandHelp -CommandName "poshl" -ModuleName "theme" -HelpText "用法: poshl [-Help] [--available]`n作用: 列出主题目录中的所有 `.omp.json` 主题名称。`n       --available 列出 oh-my-posh 远程仓库中的全部可用主题名称。"
Register-SccCommandHelp -CommandName "posht" -ModuleName "theme" -HelpText "用法: posht <名称> [-Help]`n作用: 临时切换主题，只对当前会话生效，不写入配置文件。本地没有的主题会自动从远程下载。"
Register-SccCommandHelp -CommandName "poshs" -ModuleName "theme" -HelpText "用法: poshs <名称> [-Help]`n作用: 永久切换主题，并把结果写回 scc.config.json。本地没有的主题会自动从远程下载。"

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

function Get-SccRemoteThemeList {
    $cacheFile = Join-Path $script:ThemeDir ".remote-themes-cache.json"
    $cacheTtl = [TimeSpan]::FromHours(24)

    if ((Test-Path $cacheFile)) {
        try {
            $cacheAge = (Get-Date) - (Get-Item $cacheFile).LastWriteTime
            if ($cacheAge -lt $cacheTtl) {
                $cached = Get-Content -Path $cacheFile -Raw | ConvertFrom-Json
                if ($cached -is [array] -and $cached.Count -gt 0) {
                    return @($cached)
                }
            }
        } catch {
            # Cache corrupt, re-fetch
        }
    }

    $apiUrl = "https://api.github.com/repos/JanDeDobbeleer/oh-my-posh/contents/themes"
    $previousProtocol = [Net.ServicePointManager]::SecurityProtocol
    $previousProgress = $ProgressPreference
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $ProgressPreference = 'SilentlyContinue'

        $proxyUrl = ""
        $config = Get-SccConfig
        if ($config.proxy -and [bool]$config.proxy.enabled) {
            $proxyUrl = if (-not [string]::IsNullOrWhiteSpace($config.proxy.https)) { $config.proxy.https } elseif (-not [string]::IsNullOrWhiteSpace($config.proxy.http)) { $config.proxy.http } else { "" }
        }

        $params = @{
            Uri             = $apiUrl
            UseBasicParsing = $true
            TimeoutSec      = 30
            Headers         = @{ "Accept" = "application/vnd.github.v3+json" }
        }
        if (-not [string]::IsNullOrWhiteSpace($proxyUrl)) {
            $params.Proxy = $proxyUrl
            $params.ProxyUseDefaultCredentials = $true
        }

        $response = Invoke-RestMethod @params
        $themeNames = @($response | Where-Object { $_.name -match '\.omp\.(json|yaml)$' } | ForEach-Object { $_.name -replace '\.omp\.(json|yaml)$', '' } | Sort-Object)

        if ($themeNames.Count -gt 0) {
            Ensure-SccThemeDirectory | Out-Null
            $themeNames | ConvertTo-Json | Set-Content -Path $cacheFile -Encoding UTF8
        }

        return $themeNames
    } catch {
        # Network failed, return stale cache if available
        if ((Test-Path $cacheFile)) {
            try {
                $cached = Get-Content -Path $cacheFile -Raw | ConvertFrom-Json
                if ($cached -is [array]) {
                    Write-Host "[Theme] 远程获取失败，使用本地缓存。" -ForegroundColor Yellow
                    return @($cached)
                }
            } catch { }
        }
        Write-Host "[Theme] 无法获取远程主题列表: $($_.Exception.Message)" -ForegroundColor Red
        return @()
    } finally {
        [Net.ServicePointManager]::SecurityProtocol = $previousProtocol
        $ProgressPreference = $previousProgress
    }
}

function Import-SccRemoteTheme {
    param([Parameter(Mandatory)][string]$ThemeName)

    $localPath = Join-Path $script:ThemeDir "$ThemeName.omp.json"
    if (Test-Path $localPath) {
        return $localPath
    }

    if (-not (Ensure-SccThemeDirectory)) {
        return $null
    }

    $remoteUrl = "https://raw.githubusercontent.com/JanDeDobbeleer/oh-my-posh/main/themes/$ThemeName.omp.json"
    $previousProtocol = [Net.ServicePointManager]::SecurityProtocol
    $previousProgress = $ProgressPreference
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $ProgressPreference = 'SilentlyContinue'

        $proxyUrl = ""
        $config = Get-SccConfig
        if ($config.proxy -and [bool]$config.proxy.enabled) {
            $proxyUrl = if (-not [string]::IsNullOrWhiteSpace($config.proxy.https)) { $config.proxy.https } elseif (-not [string]::IsNullOrWhiteSpace($config.proxy.http)) { $config.proxy.http } else { "" }
        }

        $params = @{
            Uri             = $remoteUrl
            OutFile         = $localPath
            UseBasicParsing = $true
            TimeoutSec      = 30
        }
        if (-not [string]::IsNullOrWhiteSpace($proxyUrl)) {
            $params.Proxy = $proxyUrl
            $params.ProxyUseDefaultCredentials = $true
        }

        Invoke-WebRequest @params

        if (Test-Path $localPath) {
            # Validate the downloaded content is valid JSON (not a 404 HTML page)
            try {
                $null = Get-Content -Path $localPath -Raw | ConvertFrom-Json -ErrorAction Stop
            } catch {
                Remove-Item -Path $localPath -Force -ErrorAction SilentlyContinue
                Write-Host "[Theme] 下载内容不是有效的 JSON: $ThemeName" -ForegroundColor Red
                return $null
            }
            Write-Host "[Theme] 已自动下载主题: $ThemeName" -ForegroundColor DarkGray
            return $localPath
        }

        Write-Host "[Theme] 下载主题失败: $ThemeName" -ForegroundColor Red
        return $null
    } catch {
        Write-Host "[Theme] 下载主题失败: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    } finally {
        [Net.ServicePointManager]::SecurityProtocol = $previousProtocol
        $ProgressPreference = $previousProgress
    }
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

function Get-SccInitCacheFingerprint {
    param([Parameter(Mandatory)][string]$ThemePath, [string]$OmpExecutable)

    $themeMtime = ""
    if (Test-Path $ThemePath) {
        $themeMtime = (Get-Item $ThemePath).LastWriteTimeUtc.Ticks.ToString()
    }

    $ompMtime = ""
    $ompCmd = Get-Command $OmpExecutable -ErrorAction SilentlyContinue
    if ($null -ne $ompCmd -and -not [string]::IsNullOrWhiteSpace($ompCmd.Source)) {
        if (Test-Path $ompCmd.Source) {
            $ompMtime = (Get-Item $ompCmd.Source).LastWriteTimeUtc.Ticks.ToString()
        }
    }

    return "$themeMtime|$ompMtime"
}

function Get-SccInitCachePath {
    return (Join-Path $script:ThemeDir ".init-cache.ps1")
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
        $importedPath = Import-SccRemoteTheme -ThemeName $ThemeName
        if ($null -eq $importedPath) {
            $script:ThemeStatus = "主题文件不存在且下载失败: $ThemeName"
            $script:ThemeLastError = $null
            if (-not $Quiet) {
                Write-Host "[Theme] $script:ThemeStatus" -ForegroundColor Yellow
            }
            return $false
        }
        $themePath = $importedPath
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
        $cacheFile = Get-SccInitCachePath
        $fingerprint = Get-SccInitCacheFingerprint -ThemePath $themePath -OmpExecutable $themeExecutable
        $fingerprintFile = Join-Path $script:ThemeDir ".init-cache.fp"

        $cacheHit = $false
        if ((Test-Path $cacheFile) -and (Test-Path $fingerprintFile)) {
            $savedFp = (Get-Content -Path $fingerprintFile -Raw).Trim()
            if ($savedFp -eq $fingerprint) {
                $cacheHit = $true
            }
        }

        if ($cacheHit) {
            $env:POSH_SESSION_ID = [guid]::NewGuid().ToString()
            . $cacheFile
        } else {
            $initOutput = & $themeExecutable init pwsh --config $themePath 2>$null
            # Strip the fixed POSH_SESSION_ID line so we regenerate it on cache hit
            $sanitized = ($initOutput -split "`n" | Where-Object { $_ -notmatch '^\$env:POSH_SESSION_ID\s*=' }) -join "`n"
            $sanitized | Set-Content -Path $cacheFile -Encoding UTF8
            $fingerprint | Set-Content -Path $fingerprintFile -Encoding UTF8
            Invoke-Expression $initOutput
        }

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
        [switch]$Help,
        [switch]$Available
    )

    if ($Help -or $Action -eq "help") {
        [void](Show-SccCommandHelp -CommandName "poshl")
        return
    }

    if ($Available -or $Action -eq "--available") {
        $remoteThemes = Get-SccRemoteThemeList
        if ($remoteThemes.Count -eq 0) {
            return
        }
        foreach ($name in $remoteThemes) {
            Write-Output $name
        }
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
