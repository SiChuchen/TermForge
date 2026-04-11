[CmdletBinding()]
param(
    [string]$InstallRoot = $(
        if (Test-Path (Join-Path $env:LOCALAPPDATA "TermForge")) {
            Join-Path $env:LOCALAPPDATA "TermForge"
        } elseif (Test-Path (Join-Path $env:LOCALAPPDATA "windows-terminal")) {
            Join-Path $env:LOCALAPPDATA "windows-terminal"
        } else {
            Join-Path $env:LOCALAPPDATA "TermForge"
        }
    ),
    [switch]$SkipDependencyInstall,
    [switch]$SkipVerification
)

$ErrorActionPreference = "Stop"
$script:ManagedBlockMarkers = @(
    [pscustomobject]@{
        Start = "# >>> TermForge managed >>>"
        End   = "# <<< TermForge managed <<<"
    }
    [pscustomobject]@{
        Start = "# >>> windows-terminal managed >>>"
        End   = "# <<< windows-terminal managed <<<"
    }
)
$script:ManagedBlockStart = $script:ManagedBlockMarkers[0].Start
$script:ManagedBlockEnd = $script:ManagedBlockMarkers[0].End

Add-Type -AssemblyName Newtonsoft.Json

function Write-SccInstallStep {
    param([Parameter(Mandatory)][string]$Message)

    Write-Host "[install] $Message" -ForegroundColor Cyan
}

function Write-SccInstallWarn {
    param([Parameter(Mandatory)][string]$Message)

    Write-Host "[install] $Message" -ForegroundColor Yellow
}

function Read-SccInstallText {
    param(
        [Parameter(Mandatory)][string]$Prompt,
        [string]$Default = ""
    )

    $suffix = if ([string]::IsNullOrWhiteSpace($Default)) { "" } else { " [$Default]" }
    $value = Read-Host "$Prompt$suffix"
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }

    return $value.Trim()
}

function Read-SccInstallBool {
    param(
        [Parameter(Mandatory)][string]$Prompt,
        [bool]$Default = $true
    )

    $defaultToken = if ($Default) { "Y/n" } else { "y/N" }
    while ($true) {
        $value = Read-Host "$Prompt [$defaultToken]"
        if ([string]::IsNullOrWhiteSpace($value)) {
            return $Default
        }

        switch ($value.Trim().ToLowerInvariant()) {
            "y" { return $true }
            "yes" { return $true }
            "n" { return $false }
            "no" { return $false }
            default { Write-SccInstallWarn "请输入 y 或 n。" }
        }
    }
}

function Read-SccInstallInt {
    param(
        [Parameter(Mandatory)][string]$Prompt,
        [int]$Default
    )

    while ($true) {
        $value = Read-SccInstallText -Prompt $Prompt -Default $Default
        $parsed = 0
        if ([int]::TryParse($value, [ref]$parsed)) {
            return $parsed
        }

        Write-SccInstallWarn "请输入整数。"
    }
}

function Test-SccInstallCommandName {
    param([string]$Name)

    return -not [string]::IsNullOrWhiteSpace($Name) -and $Name -match '^[A-Za-z][A-Za-z0-9_-]*$'
}

function Read-SccInstallCommandName {
    param([string]$Default = "scc")

    while ($true) {
        $value = Read-SccInstallText -Prompt "主命令名" -Default $Default
        if (Test-SccInstallCommandName -Name $value) {
            return $value
        }

        Write-SccInstallWarn "命令名只能包含字母、数字、下划线和横线，且必须以字母开头。"
    }
}

function Get-SccManagedProfileTargets {
    $documentsPath = [Environment]::GetFolderPath("MyDocuments")

    return @(
        [pscustomobject]@{
            Name      = "PowerShell"
            Key       = "powershell"
            Path      = (Join-Path $documentsPath "PowerShell\Microsoft.PowerShell_profile.ps1")
            EntryFile = "Microsoft.PowerShell_profile.ps1"
        }
        [pscustomobject]@{
            Name      = "VSCode"
            Key       = "vscode"
            Path      = (Join-Path $documentsPath "PowerShell\Microsoft.VSCode_profile.ps1")
            EntryFile = "Microsoft.VSCode_profile.ps1"
        }
    )
}

function Ensure-SccDependency {
    param(
        [Parameter(Mandatory)][string]$CommandName,
        [Parameter(Mandatory)][string]$WingetId,
        [Parameter(Mandatory)][string]$FriendlyName
    )

    if (Get-Command $CommandName -ErrorAction SilentlyContinue) {
        Write-SccInstallStep "$FriendlyName 已存在。"
        return $true
    }

    if ($SkipDependencyInstall) {
        Write-SccInstallWarn "未检测到 $FriendlyName，且已跳过依赖安装。"
        return $false
    }

    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-SccInstallWarn "未检测到 winget，无法自动安装 $FriendlyName。"
        return $false
    }

    Write-SccInstallStep "正在通过 winget 安装 $FriendlyName ..."
    & winget install --id $WingetId --exact --source winget --accept-source-agreements --accept-package-agreements

    if (Get-Command $CommandName -ErrorAction SilentlyContinue) {
        Write-SccInstallStep "$FriendlyName 安装完成。"
        return $true
    }

    Write-SccInstallWarn "$FriendlyName 安装命令已执行，但当前会话尚未检测到 $CommandName。安装完成后请重新打开终端。"
    return $false
}

function Find-SccClinkExecutable {
    $clinkCommand = Get-Command clink -ErrorAction SilentlyContinue
    if ($null -ne $clinkCommand) {
        return $clinkCommand.Source
    }

    $searchRoots = @(
        (Join-Path $env:LOCALAPPDATA "Programs")
        $env:ProgramFiles
        ${env:ProgramFiles(x86)}
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) }

    foreach ($root in $searchRoots) {
        $candidate = Get-ChildItem -Path $root -Recurse -Filter clink*.exe -ErrorAction SilentlyContinue |
            Sort-Object FullName |
            Select-Object -First 1
        if ($null -ne $candidate) {
            return $candidate.FullName
        }
    }

    return $null
}

function Get-SccOhMyPoshExecutable {
    $command = Get-Command oh-my-posh -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return $null
    }

    return $command.Source
}

function Copy-SccRuntimeFile {
    param(
        [Parameter(Mandatory)][string]$SourceRoot,
        [Parameter(Mandatory)][string]$InstallRoot,
        [Parameter(Mandatory)][string]$RelativePath
    )

    $sourcePath = Join-Path $SourceRoot $RelativePath
    $targetPath = Join-Path $InstallRoot $RelativePath
    if ([System.IO.Path]::GetFullPath($sourcePath) -eq [System.IO.Path]::GetFullPath($targetPath)) {
        return
    }

    $targetDirectory = Split-Path -Parent $targetPath
    if (-not (Test-Path $targetDirectory)) {
        New-Item -Path $targetDirectory -ItemType Directory -Force | Out-Null
    }

    Copy-Item -Path $sourcePath -Destination $targetPath -Force
}

function Remove-SccManagedBlock {
    param([string]$Content)

    if ([string]::IsNullOrWhiteSpace($Content)) {
        return ""
    }

    $result = $Content
    foreach ($marker in $script:ManagedBlockMarkers) {
        $pattern = "(?ms)^\Q$($marker.Start)\E.*?^\Q$($marker.End)\E\r?\n?"
        $result = [regex]::Replace($result, $pattern, "")
    }
    return $result.TrimEnd("`r", "`n")
}

function Get-SccManagedProfileBlock {
    param([Parameter(Mandatory)][string]$EntryScriptPath)

    return @"
$script:ManagedBlockStart
`$SccManagedProfile = '$EntryScriptPath'
if (Test-Path `$SccManagedProfile) {
    . `$SccManagedProfile
} else {
    Write-Host "[TermForge] 未找到受管 profile: `$SccManagedProfile" -ForegroundColor Yellow
}
$script:ManagedBlockEnd
"@
}

function Set-SccManagedProfile {
    param(
        [Parameter(Mandatory)][string]$ProfilePath,
        [Parameter(Mandatory)][string]$EntryScriptPath
    )

    $profileDirectory = Split-Path -Parent $ProfilePath
    if (-not (Test-Path $profileDirectory)) {
        New-Item -Path $profileDirectory -ItemType Directory -Force | Out-Null
    }

    $existingContent = if (Test-Path $ProfilePath) {
        Get-Content -Path $ProfilePath -Raw
    } else {
        ""
    }

    $cleanContent = Remove-SccManagedBlock -Content $existingContent
    $managedBlock = Get-SccManagedProfileBlock -EntryScriptPath $EntryScriptPath

    $newContent = if ([string]::IsNullOrWhiteSpace($cleanContent)) {
        $managedBlock
    } else {
        "$cleanContent`r`n`r`n$managedBlock"
    }

    Set-Content -Path $ProfilePath -Value $newContent -Encoding UTF8
}

function Read-SccJsonObject {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$DefaultJson = "{}"
    )

    $content = if (Test-Path $Path) {
        Get-Content -Path $Path -Raw
    } else {
        $DefaultJson
    }

    if ([string]::IsNullOrWhiteSpace($content)) {
        $content = $DefaultJson
    }

    $reader = [Newtonsoft.Json.JsonTextReader]::new([System.IO.StringReader]::new($content))
    $reader.DateParseHandling = [Newtonsoft.Json.DateParseHandling]::None

    try {
        $token = [Newtonsoft.Json.Linq.JToken]::ReadFrom($reader)
    } finally {
        $reader.Close()
    }

    if ($token -is [Newtonsoft.Json.Linq.JObject]) {
        return [Newtonsoft.Json.Linq.JObject]$token
    }

    return [Newtonsoft.Json.Linq.JObject]::new()
}

function Set-SccJsonObjectValue {
    param(
        [Parameter(Mandatory)][Newtonsoft.Json.Linq.JObject]$Root,
        [Parameter(Mandatory)][string[]]$PathSegments,
        [Parameter(Mandatory)]$Value
    )

    $current = $Root
    for ($index = 0; $index -lt ($PathSegments.Count - 1); $index++) {
        $segment = $PathSegments[$index]
        $next = $current[$segment]
        if ($null -eq $next -or $next.Type -ne [Newtonsoft.Json.Linq.JTokenType]::Object) {
            $nextObject = [Newtonsoft.Json.Linq.JObject]::new()
            $current[$segment] = $nextObject
            $current = $nextObject
        } else {
            $current = [Newtonsoft.Json.Linq.JObject]$next
        }
    }

    $current[$PathSegments[-1]] = [Newtonsoft.Json.Linq.JToken]::FromObject($Value)
}

function Save-SccJsonObject {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][Newtonsoft.Json.Linq.JObject]$Root
    )

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path $directory)) {
        New-Item -Path $directory -ItemType Directory -Force | Out-Null
    }

    $Root.ToString([Newtonsoft.Json.Formatting]::Indented) | Set-Content -Path $Path -Encoding UTF8
}

function Find-SccWindowsTerminalSettingsPath {
    $paths = @(
        (Join-Path $env:LOCALAPPDATA "Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json")
        (Join-Path $env:LOCALAPPDATA "Packages\Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe\LocalState\settings.json")
        (Join-Path $env:LOCALAPPDATA "Packages\Microsoft.WindowsTerminalCanary_8wekyb3d8bbwe\LocalState\settings.json")
        (Join-Path $env:LOCALAPPDATA "Microsoft\Windows Terminal\settings.json")
    )

    foreach ($path in $paths) {
        if (Test-Path $path) {
            return $path
        }
    }

    return $paths[0]
}

function Get-SccVsCodeSettingsPaths {
    return @(
        (Join-Path $env:APPDATA "Code\User\settings.json")
        (Join-Path $env:APPDATA "Code - Insiders\User\settings.json")
    )
}

function Set-SccWindowsTerminalFont {
    param(
        [Parameter(Mandatory)][string]$FontFace,
        [Parameter(Mandatory)][int]$FontSize
    )

    $settingsPath = Find-SccWindowsTerminalSettingsPath
    $settingsRoot = Read-SccJsonObject -Path $settingsPath
    Set-SccJsonObjectValue -Root $settingsRoot -PathSegments @("profiles", "defaults", "font", "face") -Value $FontFace
    Set-SccJsonObjectValue -Root $settingsRoot -PathSegments @("profiles", "defaults", "font", "size") -Value $FontSize
    Set-SccJsonObjectValue -Root $settingsRoot -PathSegments @("profiles", "defaults", "fontFace") -Value $FontFace
    Set-SccJsonObjectValue -Root $settingsRoot -PathSegments @("profiles", "defaults", "fontSize") -Value $FontSize
    Save-SccJsonObject -Path $settingsPath -Root $settingsRoot
    Write-SccInstallStep "已配置 Windows Terminal 字体: $FontFace ($FontSize)"
    return $settingsPath
}

function Set-SccVsCodeTerminalFont {
    param(
        [Parameter(Mandatory)][string]$FontFace,
        [Parameter(Mandatory)][int]$FontSize
    )

    $updatedPaths = @()
    foreach ($settingsPath in Get-SccVsCodeSettingsPaths) {
        $settingsRoot = Read-SccJsonObject -Path $settingsPath
        Set-SccJsonObjectValue -Root $settingsRoot -PathSegments @("terminal.integrated.fontFamily") -Value $FontFace
        Set-SccJsonObjectValue -Root $settingsRoot -PathSegments @("terminal.integrated.fontSize") -Value $FontSize
        Save-SccJsonObject -Path $settingsPath -Root $settingsRoot
        $updatedPaths += $settingsPath
    }

    return @($updatedPaths)
}

function Install-SccNerdFont {
    param([Parameter(Mandatory)][string]$FontToken)

    $ohMyPoshExecutable = Get-SccOhMyPoshExecutable
    if ([string]::IsNullOrWhiteSpace($ohMyPoshExecutable)) {
        Write-SccInstallWarn "未检测到 oh-my-posh，无法自动安装 Nerd Font。"
        return $false
    }

    try {
        & $ohMyPoshExecutable font install $FontToken
        Write-SccInstallStep "Nerd Font 安装完成: $FontToken"
        return $true
    } catch {
        Write-SccInstallWarn "Nerd Font 安装失败: $($_.Exception.Message)"
        return $false
    }
}

function Export-SccTheme {
    param(
        [Parameter(Mandatory)][string]$ThemeName,
        [Parameter(Mandatory)][string]$ThemeDirectory,
        [Parameter(Mandatory)][string]$SourceRoot
    )

    $bundledThemePath = Join-Path $SourceRoot "themes\termforge.omp.json"
    $targetThemePath = Join-Path $ThemeDirectory "$ThemeName.omp.json"
    $activeThemePath = Join-Path $ThemeDirectory "active.omp.json"

    if (-not (Test-Path $ThemeDirectory)) {
        New-Item -Path $ThemeDirectory -ItemType Directory -Force | Out-Null
    }

    if ($ThemeName -in @("termforge", "windows-terminal")) {
        Copy-Item -Path $bundledThemePath -Destination (Join-Path $ThemeDirectory "termforge.omp.json") -Force
        Copy-Item -Path $bundledThemePath -Destination $targetThemePath -Force
        Copy-Item -Path $bundledThemePath -Destination $activeThemePath -Force
        return "termforge"
    }

    $ohMyPoshExecutable = Get-SccOhMyPoshExecutable
    if (-not [string]::IsNullOrWhiteSpace($ohMyPoshExecutable)) {
        try {
            & $ohMyPoshExecutable config export --config $ThemeName --output $targetThemePath
            if (Test-Path $targetThemePath) {
                Copy-Item -Path $targetThemePath -Destination $activeThemePath -Force
                return $ThemeName
            }
        } catch {
            Write-SccInstallWarn "导出主题 '$ThemeName' 失败，将回退到 termforge。"
        }
    }

    Copy-Item -Path $bundledThemePath -Destination (Join-Path $ThemeDirectory "termforge.omp.json") -Force
    Copy-Item -Path $bundledThemePath -Destination $activeThemePath -Force
    return "termforge"
}

function New-SccClinkScript {
    param(
        [Parameter(Mandatory)][string]$ScriptsPath,
        [Parameter(Mandatory)][string]$OhMyPoshExecutable,
        [Parameter(Mandatory)][string]$ThemePath
    )

    if (-not (Test-Path $ScriptsPath)) {
        New-Item -Path $ScriptsPath -ItemType Directory -Force | Out-Null
    }

    $scriptPath = Join-Path $ScriptsPath "oh-my-posh.lua"
    $lua = @"
local handle = io.popen([=["$OhMyPoshExecutable" init cmd --config "$ThemePath"]=])
if handle then
    local init = handle:read("*a")
    handle:close()
    if init and #init > 0 then
        load(init)()
    end
end
"@

    Set-Content -Path $scriptPath -Value $lua -Encoding UTF8
    return $scriptPath
}

function Configure-SccCmdHost {
    param(
        [Parameter(Mandatory)][bool]$EnableCmdHost,
        [Parameter(Mandatory)][string]$InstallRoot,
        [Parameter(Mandatory)][string]$ThemePath
    )

    if (-not $EnableCmdHost) {
        return [pscustomobject]@{
            Enabled     = $false
            ClinkPath   = ""
            ScriptsPath = (Join-Path $InstallRoot "clink")
        }
    }

    $clinkExecutable = Find-SccClinkExecutable
    if ([string]::IsNullOrWhiteSpace($clinkExecutable)) {
        [void](Ensure-SccDependency -CommandName "clink" -WingetId "chrisant996.Clink" -FriendlyName "Clink")
        $clinkExecutable = Find-SccClinkExecutable
    }

    if ([string]::IsNullOrWhiteSpace($clinkExecutable)) {
        Write-SccInstallWarn "未检测到 Clink，CMD 提示符集成将跳过。"
        return [pscustomobject]@{
            Enabled     = $false
            ClinkPath   = ""
            ScriptsPath = (Join-Path $InstallRoot "clink")
        }
    }

    $ohMyPoshExecutable = Get-SccOhMyPoshExecutable
    if ([string]::IsNullOrWhiteSpace($ohMyPoshExecutable)) {
        Write-SccInstallWarn "未检测到 oh-my-posh，CMD 提示符集成将跳过。"
        return [pscustomobject]@{
            Enabled     = $false
            ClinkPath   = $clinkExecutable
            ScriptsPath = (Join-Path $InstallRoot "clink")
        }
    }

    $scriptsPath = Join-Path $InstallRoot "clink"
    [void](New-SccClinkScript -ScriptsPath $scriptsPath -OhMyPoshExecutable $ohMyPoshExecutable -ThemePath $ThemePath)

    try {
        & $clinkExecutable installscripts $scriptsPath | Out-Null
        & $clinkExecutable autorun install | Out-Null
        Write-SccInstallStep "已配置 CMD/Clink + Oh My Posh 提示符。"
    } catch {
        Write-SccInstallWarn "Clink 集成失败: $($_.Exception.Message)"
        return [pscustomobject]@{
            Enabled     = $false
            ClinkPath   = $clinkExecutable
            ScriptsPath = $scriptsPath
        }
    }

    return [pscustomobject]@{
        Enabled     = $true
        ClinkPath   = $clinkExecutable
        ScriptsPath = $scriptsPath
    }
}

function New-SccCommandLauncher {
    param(
        [Parameter(Mandatory)][string]$InstallRoot,
        [Parameter(Mandatory)][string]$CommandName
    )

    $launcherPath = Join-Path $InstallRoot "$CommandName.cmd"
    $content = @"
@echo off
setlocal
set "WT_LAUNCHER=%~dp0launcher.ps1"
where pwsh >nul 2>nul
if %errorlevel%==0 (
    pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%WT_LAUNCHER%" %*
) else (
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%WT_LAUNCHER%" %*
)
exit /b %errorlevel%
"@

    Set-Content -Path $launcherPath -Value $content -Encoding ASCII
}

function Add-SccUserPathEntry {
    param([Parameter(Mandatory)][string]$InstallRoot)

    $currentPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $parts = @($currentPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($parts -contains $InstallRoot) {
        return
    }

    $newParts = @($parts + $InstallRoot) | Select-Object -Unique
    [Environment]::SetEnvironmentVariable("Path", ($newParts -join ';'), "User")
    Write-SccInstallStep "已将安装目录加入用户 PATH。"
}

function New-SccInstallConfig {
    param(
        [Parameter(Mandatory)][string]$InstallRoot,
        [Parameter(Mandatory)][string]$CommandName,
        [Parameter(Mandatory)][bool]$AddToPath,
        [Parameter(Mandatory)][bool]$ProxyEnabled,
        [string]$HttpProxy,
        [string]$HttpsProxy,
        [string]$NoProxy,
        [Parameter(Mandatory)][string]$ThemeName,
        [Parameter(Mandatory)][string]$FontFace,
        [Parameter(Mandatory)][int]$FontSize,
        [Parameter(Mandatory)][hashtable]$ManagedProfiles,
        [Parameter(Mandatory)]$CmdHost
    )

    return [pscustomobject][ordered]@{
        install = [pscustomobject][ordered]@{
            root = $InstallRoot
            addToPath = $AddToPath
            managedProfiles = [pscustomobject][ordered]@{
                powershell = $ManagedProfiles["powershell"]
                vscode     = $ManagedProfiles["vscode"]
                cmd        = if ($CmdHost.Enabled) { "clink" } else { "" }
            }
        }
        cli = [pscustomobject][ordered]@{
            commandName = $CommandName
        }
        cmd = [pscustomobject][ordered]@{
            enabled     = [bool]$CmdHost.Enabled
            clinkPath   = $CmdHost.ClinkPath
            scriptsPath = $CmdHost.ScriptsPath
        }
        font = [pscustomobject][ordered]@{
            face = $FontFace
            size = $FontSize
        }
        proxy = [pscustomobject][ordered]@{
            enabled = $ProxyEnabled
            http    = $HttpProxy
            https   = $HttpsProxy
            noProxy = $NoProxy
        }
        theme = [pscustomobject][ordered]@{
            enabled      = $true
            themeDir     = (Join-Path $InstallRoot "themes")
            defaultTheme = $ThemeName
            activeTheme  = $ThemeName
            commandPath  = ""
        }
    }
}

function New-SccModuleState {
    return [pscustomobject][ordered]@{
        theme = $true
        proxy = $true
    }
}

$sourceRoot = $PSScriptRoot
$installRoot = [System.IO.Path]::GetFullPath($InstallRoot)

Write-Host ""
Write-Host "TermForge interactive installer" -ForegroundColor Cyan
Write-Host "Source : $sourceRoot"
Write-Host "Target : $installRoot"
Write-Host ""

$installRoot = [System.IO.Path]::GetFullPath((Read-SccInstallText -Prompt "安装目录" -Default $installRoot))
$commandName = Read-SccInstallCommandName -Default "scc"
$addToPath = Read-SccInstallBool -Prompt "是否将安装目录加入用户 PATH（便于从 cmd / PowerShell 直接执行命令）" -Default $true
$managePowerShellProfile = Read-SccInstallBool -Prompt "是否托管 PowerShell profile" -Default $true
$manageVsCodeProfile = Read-SccInstallBool -Prompt "是否托管 VS Code PowerShell profile" -Default $true
$enableCmdHost = Read-SccInstallBool -Prompt "是否为 CMD 配置 Clink + Oh My Posh 提示符" -Default $true
$themeChoice = Read-SccInstallText -Prompt "默认主题名" -Default "termforge"
$configureProxy = Read-SccInstallBool -Prompt "是否启用代理" -Default $false
$httpProxy = ""
$httpsProxy = ""
$noProxy = "127.0.0.1,localhost,::1"

if ($configureProxy) {
    $httpProxy = Read-SccInstallText -Prompt "HTTP 代理地址" -Default ""
    $httpsProxy = Read-SccInstallText -Prompt "HTTPS 代理地址(留空则复用 HTTP)" -Default ""
    $noProxy = Read-SccInstallText -Prompt "NO_PROXY(可选，逗号分隔)" -Default "127.0.0.1,localhost,::1"
}

$configureFonts = Read-SccInstallBool -Prompt "是否自动安装 Nerd Font 并配置终端字体" -Default $true
$fontFace = Read-SccInstallText -Prompt "字体名称" -Default "MesloLGM Nerd Font"
$fontSize = Read-SccInstallInt -Prompt "字体大小" -Default 12

$shouldInstallPowerShell = Read-SccInstallBool -Prompt "若缺少 pwsh，是否自动安装 PowerShell 7" -Default $true
$shouldInstallOhMyPosh = Read-SccInstallBool -Prompt "若缺少 oh-my-posh，是否自动安装" -Default $true
$shouldInstallWindowsTerminal = Read-SccInstallBool -Prompt "若缺少 Windows Terminal，是否自动安装" -Default $true

if ($shouldInstallPowerShell) {
    [void](Ensure-SccDependency -CommandName "pwsh" -WingetId "Microsoft.PowerShell" -FriendlyName "PowerShell 7")
}

if ($shouldInstallOhMyPosh) {
    [void](Ensure-SccDependency -CommandName "oh-my-posh" -WingetId "JanDeDobbeleer.OhMyPosh" -FriendlyName "Oh My Posh")
}

if ($shouldInstallWindowsTerminal) {
    [void](Ensure-SccDependency -CommandName "wt" -WingetId "Microsoft.WindowsTerminal" -FriendlyName "Windows Terminal")
}

Write-SccInstallStep "正在部署运行时文件 ..."
if (-not (Test-Path $installRoot)) {
    New-Item -Path $installRoot -ItemType Directory -Force | Out-Null
}

foreach ($relativePath in @(
    "bootstrap.ps1",
    "launcher.ps1",
    "Microsoft.PowerShell_profile.ps1",
    "Microsoft.VSCode_profile.ps1",
    "verify.ps1",
    "README.md",
    "DESIGN.md",
    "MODULE_GUIDE.md",
    "powershell.config.json",
    "install.ps1",
    "install.cmd",
    "uninstall.ps1"
)) {
    Copy-SccRuntimeFile -SourceRoot $sourceRoot -InstallRoot $installRoot -RelativePath $relativePath
}

foreach ($directoryName in @("modules", "themes")) {
    $sourceDirectory = Join-Path $sourceRoot $directoryName
    $targetDirectory = Join-Path $installRoot $directoryName
    if ([System.IO.Path]::GetFullPath($sourceDirectory) -eq [System.IO.Path]::GetFullPath($targetDirectory)) {
        continue
    }
    if (Test-Path $targetDirectory) {
        Remove-Item -Path $targetDirectory -Recurse -Force
    }
    Copy-Item -Path $sourceDirectory -Destination $targetDirectory -Recurse -Force
}

$themeDirectory = Join-Path $installRoot "themes"
$effectiveThemeName = Export-SccTheme -ThemeName $themeChoice -ThemeDirectory $themeDirectory -SourceRoot $sourceRoot
$activeThemePath = Join-Path $themeDirectory "active.omp.json"

if ($configureFonts) {
    [void](Install-SccNerdFont -FontToken "meslo")
    $windowsTerminalSettings = Set-SccWindowsTerminalFont -FontFace $fontFace -FontSize $fontSize
    $vsCodeSettings = Set-SccVsCodeTerminalFont -FontFace $fontFace -FontSize $fontSize
    Write-SccInstallStep "Windows Terminal 设置文件: $windowsTerminalSettings"
    foreach ($path in $vsCodeSettings) {
        Write-SccInstallStep "VS Code 设置文件: $path"
    }
}

$managedProfiles = @{
    powershell = ""
    vscode     = ""
}

foreach ($profileTarget in Get-SccManagedProfileTargets) {
    $shouldManage = switch ($profileTarget.Key) {
        "powershell" { $managePowerShellProfile }
        "vscode" { $manageVsCodeProfile }
        default { $false }
    }

    if (-not $shouldManage) {
        continue
    }

    $managedProfiles[$profileTarget.Key] = $profileTarget.Path
    $entryScriptPath = Join-Path $installRoot $profileTarget.EntryFile
    Set-SccManagedProfile -ProfilePath $profileTarget.Path -EntryScriptPath $entryScriptPath
    Write-SccInstallStep "已写入受管 profile: $($profileTarget.Path)"
}

$cmdHost = Configure-SccCmdHost -EnableCmdHost $enableCmdHost -InstallRoot $installRoot -ThemePath $activeThemePath

foreach ($launcherName in @($commandName, "wtctl") | Select-Object -Unique) {
    New-SccCommandLauncher -InstallRoot $installRoot -CommandName $launcherName
}

if ($addToPath) {
    Add-SccUserPathEntry -InstallRoot $installRoot
}

$config = New-SccInstallConfig `
    -InstallRoot $installRoot `
    -CommandName $commandName `
    -AddToPath $addToPath `
    -ProxyEnabled $configureProxy `
    -HttpProxy $httpProxy `
    -HttpsProxy $httpsProxy `
    -NoProxy $noProxy `
    -ThemeName $effectiveThemeName `
    -FontFace $fontFace `
    -FontSize $fontSize `
    -ManagedProfiles $managedProfiles `
    -CmdHost $cmdHost

$config | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $installRoot "scc.config.json") -Encoding UTF8
(New-SccModuleState) | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $installRoot "module_state.json") -Encoding UTF8

Write-Host ""
Write-Host "Install Summary" -ForegroundColor Cyan
Write-Host "Root         : $installRoot"
Write-Host "Command      : $commandName"
Write-Host "Fallback     : wtctl"
Write-Host "Theme        : $effectiveThemeName"
Write-Host "CMD Host     : $($cmdHost.Enabled)"
Write-Host "Font         : $fontFace ($fontSize)"
Write-Host "Proxy Enabled: $configureProxy"
Write-Host ""

if (-not $SkipVerification -and (Get-Command pwsh -ErrorAction SilentlyContinue)) {
    Write-SccInstallStep "运行安装后的 smoke test ..."
    & pwsh -NoLogo -NoProfile -File (Join-Path $installRoot "verify.ps1")
} elseif (-not $SkipVerification) {
    Write-SccInstallWarn "当前会话未检测到 pwsh，已跳过 smoke test。"
}

Write-Host ""
Write-Host "安装完成。请打开新的终端会话后使用 '$commandName doctor' 或 'wtctl doctor' 验证环境。" -ForegroundColor Green
