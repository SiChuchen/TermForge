function Get-SccRootPath {
    if ($script:SccRoot -and (Test-Path $script:SccRoot)) {
        return $script:SccRoot
    }

    if ($PSScriptRoot) {
        return (Split-Path -Parent $PSScriptRoot)
    }

    if ($PROFILE) {
        return (Split-Path -Parent $PROFILE)
    }

    return (Get-Location).Path
}

function Get-SccModulesPath {
    return (Join-Path (Get-SccRootPath) "modules")
}

function Get-SccStatePath {
    return (Join-Path (Get-SccRootPath) "module_state.json")
}

function Get-SccConfigPath {
    return (Join-Path (Get-SccRootPath) "scc.config.json")
}

function Get-SccDotNetCliProjectPath {
    return (Join-Path (Get-SccRootPath) "src\TermForge.Cli\TermForge.Cli.csproj")
}

function Invoke-SccDotNetCli {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    $projectPath = Get-SccDotNetCliProjectPath
    if (-not (Test-Path $projectPath)) {
        throw "缺少 .NET CLI 项目: $projectPath"
    }

    & dotnet run --project $projectPath -- @Arguments
}

function Get-SccDefaultProfileEntries {
    $rootPath = Get-SccRootPath

    return @(
        [pscustomobject]@{
            Name = "PowerShell"
            Path = Join-Path $rootPath "Microsoft.PowerShell_profile.ps1"
        }
        [pscustomobject]@{
            Name = "VSCode"
            Path = Join-Path $rootPath "Microsoft.VSCode_profile.ps1"
        }
    )
}

function Get-SccProfileEntries {
    return @(Get-SccDefaultProfileEntries)
}

function Initialize-SccHelpRegistry {
    if (-not ($global:SccHelp -is [hashtable])) {
        $global:SccHelp = @{}
    }

    if (-not ($global:SccCommandHelp -is [hashtable])) {
        $global:SccCommandHelp = @{}
    }
}

function Register-SccHelp {
    param(
        [Parameter(Mandatory)][string]$ModuleName,
        [Parameter(Mandatory)][string]$HelpText
    )

    Initialize-SccHelpRegistry
    $global:SccHelp[$ModuleName] = $HelpText
}

function Register-SccCommandHelp {
    param(
        [Parameter(Mandatory)][string]$CommandName,
        [Parameter(Mandatory)][string]$HelpText,
        [string]$ModuleName = ""
    )

    Initialize-SccHelpRegistry
    $global:SccCommandHelp[$CommandName] = [pscustomobject]@{
        CommandName = $CommandName
        ModuleName  = $ModuleName
        HelpText    = $HelpText
    }
}

function Show-SccCommandHelp {
    param([Parameter(Mandatory)][string]$CommandName)

    Initialize-SccHelpRegistry
    if (-not $global:SccCommandHelp.ContainsKey($CommandName)) {
        Write-Host "[SCC] 未注册命令帮助: $CommandName" -ForegroundColor Yellow
        return $false
    }

    $entry = $global:SccCommandHelp[$CommandName]
    Write-Host "=== 命令帮助: $($entry.CommandName) ===" -ForegroundColor Cyan
    if (-not [string]::IsNullOrWhiteSpace($entry.ModuleName)) {
        Write-Host "所属模块   : $($entry.ModuleName)"
    }
    $entry.HelpText -split "`n" | ForEach-Object {
        Write-Host $_
    }

    return $true
}

function Get-SccModuleCommandHelpEntries {
    param([Parameter(Mandatory)][string]$ModuleName)

    Initialize-SccHelpRegistry
    return @(
        $global:SccCommandHelp.Values |
            Where-Object { $_.ModuleName -eq $ModuleName } |
            Sort-Object CommandName
    )
}

function Show-SccModuleHelp {
    param([Parameter(Mandatory)][string]$ModuleName)

    Initialize-SccHelpRegistry
    $hasModuleHelp = $global:SccHelp.ContainsKey($ModuleName)
    $commandEntries = Get-SccModuleCommandHelpEntries -ModuleName $ModuleName

    if (-not $hasModuleHelp -and $commandEntries.Count -eq 0) {
        Write-Host "[SCC] 未注册模块帮助: $ModuleName" -ForegroundColor Yellow
        return $false
    }

    Write-Host "=== 模块帮助: $ModuleName ===" -ForegroundColor Cyan
    if ($hasModuleHelp) {
        $global:SccHelp[$ModuleName] -split "`n" | ForEach-Object {
            Write-Host $_
        }
    }

    if ($commandEntries.Count -gt 0) {
        Write-Host "子命令:" -ForegroundColor Cyan
        foreach ($entry in $commandEntries) {
            Write-Host "  $($entry.CommandName)" -ForegroundColor White
            $entry.HelpText -split "`n" | ForEach-Object {
                Write-Host "    $_" -ForegroundColor DarkGray
            }
        }
    }

    return $true
}

function Get-SccDefaultConfig {
    $rootPath = Get-SccRootPath
    $profileEntries = @(Get-SccDefaultProfileEntries)
    $defaultPowerShellProfile = @($profileEntries | Where-Object { $_.Name -eq "PowerShell" } | Select-Object -First 1)[0]
    $defaultVsCodeProfile = @($profileEntries | Where-Object { $_.Name -eq "VSCode" } | Select-Object -First 1)[0]

    return [pscustomobject][ordered]@{
        install = [pscustomobject][ordered]@{
            root = $rootPath
            addToPath = $true
            managedProfiles = [pscustomobject][ordered]@{
                powershell = if ($defaultPowerShellProfile) { $defaultPowerShellProfile.Path } else { "" }
                vscode     = if ($defaultVsCodeProfile) { $defaultVsCodeProfile.Path } else { "" }
                cmd        = ""
            }
        }
        cli = [pscustomobject][ordered]@{
            commandName = "termforge"
        }
        cmd = [pscustomobject][ordered]@{
            enabled     = $false
            clinkPath   = ""
            scriptsPath = (Join-Path $rootPath "clink")
        }
        font = [pscustomobject][ordered]@{
            face = "MesloLGM Nerd Font"
            size = 12
        }
        proxy = [pscustomobject][ordered]@{
            enabled = $false
            http    = ""
            https   = ""
            noProxy = "127.0.0.1,localhost,::1"
            targets = [pscustomobject][ordered]@{
                env = $true
                git = $false
                npm = $false
                pip = $false
            }
        }
        theme = [pscustomobject][ordered]@{
            enabled      = $true
            themeDir     = (Join-Path $rootPath "themes")
            defaultTheme = "termforge"
            activeTheme  = "termforge"
            commandPath  = ""
        }
    }
}

function Test-SccPropertyObject {
    param($Value)

    return $null -ne $Value -and (
        $Value -is [pscustomobject] -or
        $Value -is [System.Collections.IDictionary]
    )
}

function ConvertTo-SccObject {
    param($Value)

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [string] -or $Value -is [System.ValueType]) {
        return $Value
    }

    if ($Value -is [System.Collections.IDictionary]) {
        $converted = [ordered]@{}
        foreach ($key in $Value.Keys) {
            $converted[$key] = ConvertTo-SccObject -Value $Value[$key]
        }
        return [pscustomobject]$converted
    }

    if ($Value -is [pscustomobject]) {
        $converted = [ordered]@{}
        foreach ($property in $Value.PSObject.Properties) {
            $converted[$property.Name] = ConvertTo-SccObject -Value $property.Value
        }
        return [pscustomobject]$converted
    }

    if ($Value -is [System.Array]) {
        return @($Value | ForEach-Object {
            ConvertTo-SccObject -Value $_
        })
    }

    return $Value
}

function Test-SccBooleanValue {
    param($Value)

    return $Value -is [bool]
}

function Test-SccCommandName {
    param([string]$Name)

    return -not [string]::IsNullOrWhiteSpace($Name) -and $Name -match '^[A-Za-z][A-Za-z0-9_-]*$'
}

function Normalize-SccCommandName {
    param(
        [string]$Name,
        [string]$Default = "scc"
    )

    if (Test-SccCommandName -Name $Name) {
        return $Name
    }

    return $Default
}

function Get-SccFallbackCommandName {
    return "wtctl"
}

function Get-SccPrimaryCommandName {
    param($Config = $null)

    if ($null -eq $Config) {
        try {
            $Config = Get-SccConfig
        } catch {
            $Config = $null
        }
    }

    $configuredCommandName = $null
    if (
        (Test-SccPropertyObject -Value $Config) -and
        (Test-SccPropertyObject -Value $Config.cli) -and
        $Config.cli.commandName -is [string]
    ) {
        $configuredCommandName = $Config.cli.commandName
    }

    return (Normalize-SccCommandName -Name $configuredCommandName -Default "scc")
}

function Get-SccManagerCommandNames {
    param($Config = $null)

    $primaryCommandName = Get-SccPrimaryCommandName -Config $Config
    return @(
        $primaryCommandName
        Get-SccFallbackCommandName
    ) | Where-Object { Test-SccCommandName -Name $_ } | Sort-Object -Unique
}

function Merge-SccDefaultProperties {
    param(
        [Parameter(Mandatory)]$Target,
        [Parameter(Mandatory)]$Defaults
    )

    $changed = $false
    foreach ($defaultProperty in $Defaults.PSObject.Properties) {
        $name = $defaultProperty.Name
        $defaultValue = ConvertTo-SccObject -Value $defaultProperty.Value
        $targetProperty = $Target.PSObject.Properties[$name]

        if ($null -eq $targetProperty) {
            $Target | Add-Member -NotePropertyName $name -NotePropertyValue $defaultValue
            $changed = $true
            continue
        }

        if ($null -eq $targetProperty.Value) {
            $Target.$name = $defaultValue
            $changed = $true
            continue
        }

        if (-not (Test-SccPropertyObject -Value $defaultValue) -and (Test-SccPropertyObject -Value $targetProperty.Value)) {
            $Target.$name = $defaultValue
            $changed = $true
            continue
        }

        if (Test-SccPropertyObject -Value $defaultValue) {
            if (-not (Test-SccPropertyObject -Value $targetProperty.Value)) {
                $Target.$name = $defaultValue
                $changed = $true
                continue
            }

            if (Merge-SccDefaultProperties -Target $targetProperty.Value -Defaults $defaultValue) {
                $changed = $true
            }
        }
    }

    return $changed
}

function Read-SccJsonDocuments {
    param([Parameter(Mandatory)][string]$Content)

    $documents = @()
    $depth = 0
    $inString = $false
    $escape = $false
    $start = -1

    for ($i = 0; $i -lt $Content.Length; $i++) {
        $c = $Content[$i]
        if ($escape) {
            $escape = $false
            continue
        }
        if ($inString -and $c -eq '\') {
            $escape = $true
            continue
        }
        if ($c -eq '"') {
            $inString = -not $inString
            continue
        }
        if ($inString) { continue }

        if ($c -eq '{' -or $c -eq '[') {
            if ($depth -eq 0) { $start = $i }
            $depth++
        } elseif ($c -eq '}' -or $c -eq ']') {
            $depth--
            if ($depth -eq 0 -and $start -ge 0) {
                $documents += ,$Content.Substring($start, $i - $start + 1).Trim()
                $start = -1
            }
        }
    }

    return @($documents)
}

function Read-SccJsonFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        $Default = $null
    )

    if (-not (Test-Path $Path)) {
        return (ConvertTo-SccObject -Value $Default)
    }

    try {
        $content = Get-Content -Path $Path -Raw -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($content)) {
            return (ConvertTo-SccObject -Value $Default)
        }

        return ($content | ConvertFrom-Json -ErrorAction Stop)
    } catch {
        $primaryError = $_.Exception.Message

        try {
            $rawDocuments = Read-SccJsonDocuments -Content $content
        } catch {
            throw "无法读取 JSON 文件 '$Path'：$primaryError"
        }

        if ($rawDocuments.Count -le 1) {
            throw "无法读取 JSON 文件 '$Path'：$primaryError"
        }

        $parsedDocs = @()
        foreach ($raw in $rawDocuments) {
            $parsedDocs += ,($raw | ConvertFrom-Json -ErrorAction Stop)
        }

        $firstNormalized = $parsedDocs[0] | ConvertTo-Json -Depth 20 -Compress
        foreach ($doc in $parsedDocs | Select-Object -Skip 1) {
            if (($doc | ConvertTo-Json -Depth 20 -Compress) -ne $firstNormalized) {
                throw "无法读取 JSON 文件 '$Path'：文件包含多个顶层 JSON 文档，且内容不一致，请手动修复。"
            }
        }

        $normalizedContent = $parsedDocs[0] | ConvertTo-Json -Depth 20
        Set-Content -Path $Path -Value $normalizedContent -Encoding UTF8
        return ($normalizedContent | ConvertFrom-Json -ErrorAction Stop)
    }
}

function Write-SccJsonFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$Value
    )

    $Value | ConvertTo-Json -Depth 8 | Set-Content -Path $Path -Encoding UTF8
}

function Get-SccConfig {
    $configPath = Get-SccConfigPath
    $defaultConfig = ConvertTo-SccObject -Value (Get-SccDefaultConfig)

    if (-not (Test-Path $configPath)) {
        Write-SccJsonFile -Path $configPath -Value $defaultConfig
    }

    $config = Read-SccJsonFile -Path $configPath -Default $defaultConfig
    $needsWrite = $false

    if (-not (Test-SccPropertyObject -Value $config)) {
        $config = ConvertTo-SccObject -Value $defaultConfig
        $needsWrite = $true
    }

    if (Merge-SccDefaultProperties -Target $config -Defaults $defaultConfig) {
        $needsWrite = $true
    }

    if ($needsWrite) {
        Write-SccJsonFile -Path $configPath -Value $config
    }

    return $config
}

function Save-SccConfig {
    param([Parameter(Mandatory)]$Config)

    Write-SccJsonFile -Path (Get-SccConfigPath) -Value $Config
}

function Get-SccSetupCommandSource {
    param([Parameter(Mandatory)][string]$CommandName)

    $command = Get-Command $CommandName -ErrorAction SilentlyContinue
    if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        return $command.Source
    }

    try {
        $candidate = & where.exe $CommandName 2>$null |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) } |
            Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            return $candidate
        }
    } catch {
    }

    return $null
}

function Get-SccSetupWindowsPowerShellPath {
    $candidates = @()
    foreach ($windowsRoot in @($env:SystemRoot, $env:WINDIR)) {
        if (-not [string]::IsNullOrWhiteSpace($windowsRoot)) {
            $candidates += Join-Path $windowsRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
        }
    }
    $candidates += "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return (Get-SccSetupCommandSource -CommandName "powershell.exe")
}

function Test-SccWritablePath {
    param([Parameter(Mandatory)][string]$Path)

    try {
        if ([string]::IsNullOrWhiteSpace($Path)) {
            return $false
        }

        $item = Get-Item -LiteralPath $Path -ErrorAction Stop
        if (-not $item.PSIsContainer) {
            return $false
        }

        if ($item.Attributes -band [System.IO.FileAttributes]::ReadOnly) {
            return $false
        }

        $isWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
        if (-not $isWindowsPlatform) {
            return $true
        }

        $writeLikeRights = `
            [System.Security.AccessControl.FileSystemRights]::Write `
            -bor [System.Security.AccessControl.FileSystemRights]::Modify `
            -bor [System.Security.AccessControl.FileSystemRights]::CreateFiles `
            -bor [System.Security.AccessControl.FileSystemRights]::CreateDirectories `
            -bor [System.Security.AccessControl.FileSystemRights]::FullControl
        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $acl = Get-Acl -LiteralPath $item.FullName -ErrorAction Stop
        $allowWrite = $false

        foreach ($accessRule in $acl.Access) {
            $accessSid = $accessRule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier])
            $isCurrentIdentity = $identity.User -and $accessSid -eq $identity.User
            $isCurrentGroup = $identity.Groups -and ($identity.Groups -contains $accessSid)

            if (-not ($isCurrentIdentity -or $isCurrentGroup)) {
                continue
            }

            if (($accessRule.FileSystemRights -band $writeLikeRights) -eq 0) {
                continue
            }

            if ($accessRule.AccessControlType -eq [System.Security.AccessControl.AccessControlType]::Deny) {
                return $false
            }

            if ($accessRule.AccessControlType -eq [System.Security.AccessControl.AccessControlType]::Allow) {
                $allowWrite = $true
            }
        }

        return $allowWrite
    } catch {
        return $false
    }
}

function Get-SccHostFacts {
    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $documentsPath = [Environment]::GetFolderPath('MyDocuments')
    $osVersion = [Environment]::OSVersion.Version
    $isWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)

    [pscustomobject][ordered]@{
        IsWindows = $isWindowsPlatform
        OsVersion = $osVersion.ToString()
        PowerShellEdition = $PSVersionTable.PSEdition
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
        LocalAppData = $localAppData
        DocumentsPath = $documentsPath
        CanWriteLocalAppData = (
            -not [string]::IsNullOrWhiteSpace($localAppData) -and
            (Test-SccWritablePath -Path $localAppData)
        )
    }
}

function Get-SccInstallHostFacts {
    $hostExecutable = Get-SccSetupWindowsPowerShellPath
    if ([string]::IsNullOrWhiteSpace($hostExecutable)) {
        $hostExecutable = Get-SccSetupCommandSource -CommandName 'pwsh'
    }

    [pscustomobject][ordered]@{
        IsAvailable = -not [string]::IsNullOrWhiteSpace($hostExecutable)
        ExecutablePath = $hostExecutable
        HostKind = if ($hostExecutable -match 'powershell\.exe$') { 'windows-powershell' } elseif ($hostExecutable -match 'pwsh') { 'pwsh' } else { 'unknown' }
        Status = if ([string]::IsNullOrWhiteSpace($hostExecutable)) { 'FAIL' } else { 'PASS' }
        Message = if ([string]::IsNullOrWhiteSpace($hostExecutable)) { '未找到可用的 PowerShell 宿主，无法启动 install.ps1。' } else { $hostExecutable }
    }
}

function Get-SccToolFacts {
    param(
        [Parameter(Mandatory)]$HostFacts,
        [Parameter(Mandatory)]$InstallHostFacts,
        [Parameter(Mandatory)][bool]$SkipDependencyInstallFlag
    )

    $toolSpecs = @(
        @{ Name = 'winget'; Required = $false }
        @{ Name = 'pwsh'; Required = $false }
        @{ Name = 'oh-my-posh'; Required = $true }
        @{ Name = 'wt'; Required = $false }
        @{ Name = 'clink'; Required = $false }
        @{ Name = 'code'; Required = $false }
        @{ Name = 'git'; Required = $false }
        @{ Name = 'npm'; Required = $false }
        @{ Name = 'pnpm'; Required = $false }
        @{ Name = 'yarn'; Required = $false }
        @{ Name = 'pip'; Required = $false }
        @{ Name = 'uv'; Required = $false }
        @{ Name = 'cargo'; Required = $false }
        @{ Name = 'docker'; Required = $false }
    )

    $toolPaths = @{}
    foreach ($toolSpec in $toolSpecs) {
        $toolPaths[$toolSpec.Name] = Get-SccSetupCommandSource -CommandName $toolSpec.Name
    }

    $wingetDetected = -not [string]::IsNullOrWhiteSpace($toolPaths['winget'])

    foreach ($toolSpec in $toolSpecs) {
        $path = $toolPaths[$toolSpec.Name]
        $detected = -not [string]::IsNullOrWhiteSpace($path)
        $canAutoInstall = (
            $toolSpec.Required -and
            -not $detected -and
            -not $SkipDependencyInstallFlag -and
            ($toolSpec.Name -eq 'oh-my-posh') -and
            $wingetDetected -and
            $InstallHostFacts.IsAvailable
        )
        $status = if ($detected) { 'PASS' } elseif ($canAutoInstall) { 'WARN' } elseif ($toolSpec.Required) { 'FAIL' } else { 'WARN' }
        $message = if ($detected) {
            $path
        } elseif ($toolSpec.Required -and $SkipDependencyInstallFlag) {
            'required but missing; dependency install is skipped'
        } elseif ($toolSpec.Required -and -not $wingetDetected) {
            'required but missing; automatic install is unavailable'
        } elseif ($toolSpec.Required -and -not $InstallHostFacts.IsAvailable) {
            'required but missing; install host is unavailable'
        } elseif ($canAutoInstall) {
            'required but missing; can be installed automatically'
        } elseif ($toolSpec.Required) {
            'required but missing'
        } else {
            'optional but missing'
        }

        [pscustomobject][ordered]@{
            Name = $toolSpec.Name
            Detected = $detected
            CommandPath = $path
            Required = $toolSpec.Required
            CanAutoInstall = $canAutoInstall
            Status = $status
            Message = $message
        }
    }
}

function Get-SccProxyEnvironmentFacts {
    $http = if ($env:http_proxy) { $env:http_proxy } elseif ($env:HTTP_PROXY) { $env:HTTP_PROXY } else { '' }
    $https = if ($env:https_proxy) { $env:https_proxy } elseif ($env:HTTPS_PROXY) { $env:HTTPS_PROXY } else { '' }
    $noProxy = if ($env:no_proxy) { $env:no_proxy } elseif ($env:NO_PROXY) { $env:NO_PROXY } else { '' }
    $enabled = -not [string]::IsNullOrWhiteSpace($http) -or -not [string]::IsNullOrWhiteSpace($https)

    [pscustomobject][ordered]@{
        Enabled = $enabled
        HttpProxy = $http
        HttpsProxy = $https
        NoProxy = $noProxy
        Source = if ($enabled) { 'process' } else { 'none' }
        Status = if ($enabled) { 'WARN' } else { 'PASS' }
    }
}

function Get-SccEnvironmentFacts {
    param([bool]$SkipDependencyInstallFlag = $false)

    $hostFacts = Get-SccHostFacts
    $installHostFacts = Get-SccInstallHostFacts
    $toolFacts = @(Get-SccToolFacts -HostFacts $hostFacts -InstallHostFacts $installHostFacts -SkipDependencyInstallFlag:$SkipDependencyInstallFlag)
    $proxyFacts = Get-SccProxyEnvironmentFacts

    [pscustomobject][ordered]@{
        Host = $hostFacts
        Tools = $toolFacts
        ProxyEnvironment = $proxyFacts
        InstallHost = $installHostFacts
    }
}

function Get-SccSetupBlockingIssues {
    param(
        [Parameter(Mandatory)]$EnvironmentFacts,
        [Parameter(Mandatory)][bool]$SkipDependencyInstallFlag
    )

    $issues = @()
    $hostFacts = $EnvironmentFacts.Host
    $installHostFacts = $EnvironmentFacts.InstallHost
    $toolFacts = @($EnvironmentFacts.Tools)
    $ohMyPoshFacts = @($toolFacts | Where-Object Name -eq 'oh-my-posh')[0]
    $wingetFacts = @($toolFacts | Where-Object Name -eq 'winget')[0]
    $osVersion = [version]'0.0'

    if (-not [string]::IsNullOrWhiteSpace($hostFacts.OsVersion)) {
        try {
            $osVersion = [version]$hostFacts.OsVersion
        } catch {
            $osVersion = [version]'0.0'
        }
    }

    $isSupportedWindows = $hostFacts.IsWindows -and $osVersion.Major -ge 10

    if (-not $hostFacts.IsWindows) {
        $issues += "当前系统不是 Windows，TermForge 目前只支持 Windows 10 / 11。"
    } elseif (-not $isSupportedWindows) {
        $issues += "当前系统版本低于 Windows 10，TermForge 不支持该环境。"
    }

    if (-not $hostFacts.CanWriteLocalAppData) {
        $issues += "无法写入 LOCALAPPDATA，安装器无法创建默认安装目录。"
    }

    if ($null -ne $ohMyPoshFacts -and -not $ohMyPoshFacts.Detected) {
        if ($SkipDependencyInstallFlag) {
            $issues += "当前缺少 oh-my-posh，且你要求跳过依赖安装。TermForge 无法继续。"
        } elseif ($null -eq $wingetFacts -or -not $wingetFacts.Detected) {
            $issues += "当前缺少 oh-my-posh，且未检测到 winget，安装器无法自动补齐必需依赖。"
        } elseif (-not $installHostFacts.IsAvailable) {
            $issues += "当前缺少 oh-my-posh，且未找到可用的 PowerShell 宿主来启动 install.ps1。"
        }
    }

    if (-not $installHostFacts.IsAvailable) {
        $issues += "未找到可用的 PowerShell 宿主，无法启动 install.ps1。"
    }

    return @($issues)
}

function Get-SccSetupEnvironmentReport {
    param(
        [Parameter(Mandatory)]$EnvironmentFacts,
        [bool]$SkipDependencyInstallFlag = $false
    )

    $tools = @($EnvironmentFacts.Tools)
    $proxyEnvironment = $EnvironmentFacts.ProxyEnvironment
    $blockingIssues = @(
        Get-SccSetupBlockingIssues `
            -EnvironmentFacts $EnvironmentFacts `
            -SkipDependencyInstallFlag:$SkipDependencyInstallFlag
    )
    $warnings = @(
        $tools |
            Where-Object { $_.Status -eq 'WARN' } |
            ForEach-Object { '{0}: {1}' -f $_.Name, $_.Message }
    )

    if ($proxyEnvironment.Status -eq 'WARN') {
        $warnings += 'Proxy environment variables are enabled for this process.'
    }

    $wingetFacts = @($tools | Where-Object Name -eq 'winget')[0]
    $ohMyPoshFacts = @($tools | Where-Object Name -eq 'oh-my-posh')[0]
    $windowsTerminalFacts = @($tools | Where-Object Name -eq 'wt')[0]
    $clinkFacts = @($tools | Where-Object Name -eq 'clink')[0]

    [pscustomobject][ordered]@{
        SchemaVersion    = '2026-04-12'
        GeneratedAt      = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        OverallStatus    = if ($blockingIssues.Count -gt 0) { 'FAIL' } elseif ($warnings.Count -gt 0) { 'WARN' } else { 'PASS' }
        BlockingIssues   = @($blockingIssues)
        Warnings         = @($warnings)
        Environment      = [pscustomobject][ordered]@{
            IsWindows            = $EnvironmentFacts.Host.IsWindows
            OsVersion            = $EnvironmentFacts.Host.OsVersion
            PowerShellEdition    = $EnvironmentFacts.Host.PowerShellEdition
            PowerShellVersion    = $EnvironmentFacts.Host.PowerShellVersion
            LocalAppData         = $EnvironmentFacts.Host.LocalAppData
            DocumentsPath        = $EnvironmentFacts.Host.DocumentsPath
            CanWriteLocalAppData = $EnvironmentFacts.Host.CanWriteLocalAppData
        }
        Tools            = @($tools)
        ProxyEnvironment = $proxyEnvironment
        InstallReadiness = [pscustomobject][ordered]@{
            CanContinue               = ($blockingIssues.Count -eq 0)
            RequiresDependencyInstall = @(
                $tools |
                    Where-Object {
                        $_.Required -and
                        -not $_.Detected -and
                        $_.Status -eq 'WARN'
                    }
            ).Count -gt 0
            BlockingIssueCount        = $blockingIssues.Count
            WarningCount              = $warnings.Count
            RecommendedInstallMode    = if (-not $EnvironmentFacts.InstallHost.IsAvailable) {
                'manual-deps-required'
            } elseif (($null -eq $wingetFacts -or -not $wingetFacts.Detected) -and ($null -eq $ohMyPoshFacts -or -not $ohMyPoshFacts.Detected)) {
                'manual-deps-required'
            } elseif ($null -eq $windowsTerminalFacts -or -not $windowsTerminalFacts.Detected) {
                'without-terminal'
            } elseif ($null -eq $clinkFacts -or -not $clinkFacts.Detected) {
                'without-cmd'
            } else {
                'full'
            }
        }
    }
}

function Get-SccStatusReport {
    $config = Get-SccConfig
    $rootPath = Get-SccRootPath
    $primaryCommand = Get-SccPrimaryCommandName -Config $config
    $state = Ensure-SccStateFile
    $enabledModules = @(Get-SccEnabledModuleNames -State $state)

    [pscustomobject][ordered]@{
        RootPath           = $rootPath
        PrimaryCommand     = $primaryCommand
        EnabledModules     = @($enabledModules)
        ConfigPath         = Get-SccConfigPath
        ModuleStatePath    = Get-SccStatePath
        RuntimeStatePath   = Join-Path $rootPath "state"
        Proxy              = [pscustomobject][ordered]@{
            Enabled = [bool]$config.proxy.enabled
            Http    = $config.proxy.http
            Https   = $config.proxy.https
            NoProxy = $config.proxy.noProxy
            Targets = [pscustomobject][ordered]@{
                env = [bool]$(if ($config.proxy.targets) { $config.proxy.targets.env } else { $true })
                git = [bool]$(if ($config.proxy.targets) { $config.proxy.targets.git } else { $false })
                npm = [bool]$(if ($config.proxy.targets) { $config.proxy.targets.npm } else { $false })
                pip = [bool]$(if ($config.proxy.targets) { $config.proxy.targets.pip } else { $false })
            }
        }
    }
}

function Get-SccModuleScriptPaths {
    $modulesPath = Get-SccModulesPath
    if (-not (Test-Path $modulesPath)) {
        return @()
    }

    return @(Get-ChildItem -Path $modulesPath -Filter *.ps1 -File |
        Where-Object { $_.Name -notin @("common.ps1", "manager.ps1") } |
        Sort-Object Name)
}

function Get-SccAvailableModuleNames {
    return @(Get-SccModuleScriptPaths | ForEach-Object {
        [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
    })
}

function Get-SccEnabledModuleNames {
    param($State = $null)

    if ($null -eq $State) {
        $State = Ensure-SccStateFile
    }

    return @(Get-SccAvailableModuleNames | Where-Object {
        $moduleName = $_
        $State.PSObject.Properties.Match($moduleName).Count -gt 0 -and [bool]$State.$moduleName
    })
}

function Ensure-SccStateFile {
    $statePath = Get-SccStatePath
    $moduleNames = Get-SccAvailableModuleNames
    $stateMap = [ordered]@{}
    $needsWrite = $false
    $defaultStateMap = [ordered]@{}

    foreach ($moduleName in $moduleNames) {
        $defaultStateMap[$moduleName] = $false
    }

    foreach ($enabledByDefault in @("proxy", "theme")) {
        if ($defaultStateMap.Contains($enabledByDefault)) {
            $defaultStateMap[$enabledByDefault] = $true
        }
    }

    if (Test-Path $statePath) {
        $state = Read-SccJsonFile -Path $statePath
        foreach ($property in $state.PSObject.Properties) {
            $stateMap[$property.Name] = [bool]$property.Value
        }

        foreach ($moduleName in $defaultStateMap.Keys) {
            if (-not $stateMap.Contains($moduleName)) {
                $stateMap[$moduleName] = [bool]$defaultStateMap[$moduleName]
                $needsWrite = $true
            }
        }
    } else {
        foreach ($moduleName in $defaultStateMap.Keys) {
            $stateMap[$moduleName] = [bool]$defaultStateMap[$moduleName]
        }
        $needsWrite = $true
    }

    if ($needsWrite) {
        Write-SccJsonFile -Path $statePath -Value ([pscustomobject]$stateMap)
    }

    return ([pscustomobject]$stateMap)
}

function New-SccDiagnosticResult {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][ValidateSet("PASS","WARN","FAIL")][string]$Status,
        [Parameter(Mandatory)][string]$Message
    )

    return [pscustomobject]@{
        Name    = $Name
        Status  = $Status
        Message = $Message
    }
}

function Get-SccDoctorResults {
    $results = @()
    $rootPath = Get-SccRootPath

    $results += New-SccDiagnosticResult -Name "Root" -Status "PASS" -Message "工作区: $rootPath"

    foreach ($profile in Get-SccProfileEntries) {
        if (Test-Path $profile.Path) {
            $results += New-SccDiagnosticResult -Name "Profile/$($profile.Name)" -Status "PASS" -Message $profile.Path
        } else {
            $results += New-SccDiagnosticResult -Name "Profile/$($profile.Name)" -Status "FAIL" -Message "缺少 profile: $($profile.Path)"
        }
    }

    $bootstrapPath = Join-Path $rootPath "bootstrap.ps1"
    if (Test-Path $bootstrapPath) {
        $results += New-SccDiagnosticResult -Name "Bootstrap" -Status "PASS" -Message $bootstrapPath
    } else {
        $results += New-SccDiagnosticResult -Name "Bootstrap" -Status "FAIL" -Message "缺少 bootstrap.ps1"
    }

    try {
        $config = Get-SccConfig
        $configIssues = @()
        if (-not (Test-SccPropertyObject -Value $config.install)) {
            $configIssues += "缺少 install 配置段"
        } elseif (-not (Test-SccPropertyObject -Value $config.install.managedProfiles)) {
            $configIssues += "缺少 install.managedProfiles 配置段"
        } else {
            if ($config.install.addToPath -isnot [bool]) {
                $configIssues += "install.addToPath 必须是布尔值"
            }
        }

        if (-not (Test-SccPropertyObject -Value $config.cli)) {
            $configIssues += "缺少 cli 配置段"
        } else {
            if ($config.cli.commandName -isnot [string]) {
                $configIssues += "cli.commandName 必须是字符串"
            } elseif (-not (Test-SccCommandName -Name $config.cli.commandName)) {
                $configIssues += "cli.commandName 格式非法"
            }
        }

        if (-not (Test-SccPropertyObject -Value $config.cmd)) {
            $configIssues += "缺少 cmd 配置段"
        } else {
            if (-not (Test-SccBooleanValue -Value $config.cmd.enabled)) {
                $configIssues += "cmd.enabled 必须是布尔值"
            }
            if ($config.cmd.clinkPath -isnot [string]) {
                $configIssues += "cmd.clinkPath 必须是字符串"
            }
            if ($config.cmd.scriptsPath -isnot [string]) {
                $configIssues += "cmd.scriptsPath 必须是字符串"
            }
        }

        if (-not (Test-SccPropertyObject -Value $config.font)) {
            $configIssues += "缺少 font 配置段"
        } else {
            if ($config.font.face -isnot [string]) {
                $configIssues += "font.face 必须是字符串"
            }
            if ($config.font.size -isnot [int] -and $config.font.size -isnot [long]) {
                $configIssues += "font.size 必须是整数"
            }
        }

        if (-not (Test-SccPropertyObject -Value $config.theme)) {
            $configIssues += "缺少 theme 配置段"
        } else {
            if (-not (Test-SccBooleanValue -Value $config.theme.enabled)) {
                $configIssues += "theme.enabled 必须是布尔值"
            }
            if ($config.theme.themeDir -isnot [string]) {
                $configIssues += "theme.themeDir 必须是字符串"
            }
            if ($config.theme.defaultTheme -isnot [string]) {
                $configIssues += "theme.defaultTheme 必须是字符串"
            }
            if ($config.theme.activeTheme -isnot [string]) {
                $configIssues += "theme.activeTheme 必须是字符串"
            }
            if ($config.theme.commandPath -isnot [string]) {
                $configIssues += "theme.commandPath 必须是字符串"
            }
        }

        if (-not (Test-SccPropertyObject -Value $config.proxy)) {
            $configIssues += "缺少 proxy 配置段"
        } else {
            if (-not (Test-SccBooleanValue -Value $config.proxy.enabled)) {
                $configIssues += "proxy.enabled 必须是布尔值"
            }
            if ($config.proxy.http -isnot [string]) {
                $configIssues += "proxy.http 必须是字符串"
            }
            if ($config.proxy.https -isnot [string]) {
                $configIssues += "proxy.https 必须是字符串"
            }
            if ($config.proxy.noProxy -isnot [string]) {
                $configIssues += "proxy.noProxy 必须是字符串"
            }
            if ((Test-SccPropertyObject -Value $config.proxy.targets)) {
                foreach ($targetName in @("env", "git", "npm", "pip")) {
                    if ($config.proxy.targets.PSObject.Properties.Match($targetName).Count -gt 0 -and $config.proxy.targets.$targetName -isnot [bool]) {
                        $configIssues += "proxy.targets.$targetName must be a boolean"
                    }
                }
            }
        }

        if ($configIssues.Count -gt 0) {
            $results += New-SccDiagnosticResult -Name "Config" -Status "FAIL" -Message ($configIssues -join "；")
        } else {
            $results += New-SccDiagnosticResult -Name "Config" -Status "PASS" -Message "配置可读: $(Get-SccConfigPath)"
        }
    } catch {
        $results += New-SccDiagnosticResult -Name "Config" -Status "FAIL" -Message $_.Exception.Message
    }

    try {
        $state = Ensure-SccStateFile
        $enabledModules = Get-SccEnabledModuleNames -State $state
        $moduleSummary = if ($enabledModules.Count -gt 0) { $enabledModules -join ', ' } else { '无' }
        $results += New-SccDiagnosticResult -Name "State" -Status "PASS" -Message "已启用模块: $moduleSummary"
    } catch {
        $results += New-SccDiagnosticResult -Name "State" -Status "FAIL" -Message $_.Exception.Message
        $state = $null
        $enabledModules = @()
    }

    $availableModules = Get-SccAvailableModuleNames
    if ($availableModules.Count -gt 0) {
        $results += New-SccDiagnosticResult -Name "Modules" -Status "PASS" -Message "已发现模块: $($availableModules -join ', ')"
    } else {
        $results += New-SccDiagnosticResult -Name "Modules" -Status "WARN" -Message "modules 目录下未发现业务模块。"
    }

    $primaryCommandName = if ($null -ne $config) {
        Get-SccPrimaryCommandName -Config $config
    } else {
        "scc"
    }
    foreach ($commandName in Get-SccManagerCommandNames -Config $config) {
        if (Get-Command $commandName -ErrorAction SilentlyContinue) {
            $results += New-SccDiagnosticResult -Name "Command/$commandName" -Status "PASS" -Message "$commandName 已加载"
        } else {
            $results += New-SccDiagnosticResult -Name "Command/$commandName" -Status "FAIL" -Message "$commandName 命令未加载"
        }
    }

    foreach ($moduleName in $enabledModules) {
        $modulePath = Join-Path (Get-SccModulesPath) "$moduleName.ps1"
        if (Test-Path $modulePath) {
            $results += New-SccDiagnosticResult -Name "Module/$moduleName" -Status "PASS" -Message $modulePath
        } else {
            $results += New-SccDiagnosticResult -Name "Module/$moduleName" -Status "FAIL" -Message "缺少模块脚本: $modulePath"
            continue
        }

        $commandEntries = Get-SccModuleCommandHelpEntries -ModuleName $moduleName
        if ($commandEntries.Count -eq 0) {
            $results += New-SccDiagnosticResult -Name "Help/$moduleName" -Status "WARN" -Message "模块已启用，但未注册命令帮助。"
        } else {
            $results += New-SccDiagnosticResult -Name "Help/$moduleName" -Status "PASS" -Message "已注册 $($commandEntries.Count) 个命令帮助"
        }

        foreach ($entry in $commandEntries) {
            if (Get-Command $entry.CommandName -ErrorAction SilentlyContinue) {
                $results += New-SccDiagnosticResult -Name "Command/$($entry.CommandName)" -Status "PASS" -Message "命令已加载"
            } else {
                $results += New-SccDiagnosticResult -Name "Command/$($entry.CommandName)" -Status "FAIL" -Message "命令帮助已注册，但命令未加载"
            }
        }
    }

    $verifyPath = Join-Path $rootPath "verify.ps1"
    if (Test-Path $verifyPath) {
        $results += New-SccDiagnosticResult -Name "VerifyScript" -Status "PASS" -Message $verifyPath
    } else {
        $results += New-SccDiagnosticResult -Name "VerifyScript" -Status "WARN" -Message "缺少 verify.ps1，本地 smoke test 不可用"
    }

    return $results
}

function Get-SccDoctorAggregateStatus {
    param([object[]]$SectionResults)

    if (@($SectionResults | Where-Object { $_.Status -eq "FAIL" }).Count -gt 0) {
        return "FAIL"
    }

    if (@($SectionResults | Where-Object { $_.Status -eq "WARN" }).Count -gt 0) {
        return "WARN"
    }

    return "PASS"
}

function Get-SccDoctorResultByName {
    param(
        [Parameter(Mandatory)][object[]]$Results,
        [Parameter(Mandatory)][string]$Name
    )

    return @($Results | Where-Object { $_.Name -eq $Name } | Select-Object -First 1)[0]
}

function ConvertTo-SccDoctorDisplayPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$RootPath = (Get-SccRootPath)
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $Path
    }

    try {
        $fullPath = [System.IO.Path]::GetFullPath($Path)
    } catch {
        return $Path
    }

    try {
        $rootFullPath = [System.IO.Path]::GetFullPath($RootPath)
    } catch {
        $rootFullPath = $RootPath
    }

    $rootRelative = [System.IO.Path]::GetRelativePath($rootFullPath, $fullPath).Replace('/', '\')
    if ($rootRelative -ne "." -and -not $rootRelative.StartsWith("..")) {
        return ".\$rootRelative"
    }

    $homePath = [System.IO.Path]::GetFullPath($HOME)
    $homeRelative = [System.IO.Path]::GetRelativePath($homePath, $fullPath).Replace('/', '\')
    if ($homeRelative -eq ".") {
        return "~"
    }

    if (-not $homeRelative.StartsWith("..")) {
        return "~\$homeRelative"
    }

    return $fullPath
}

function Get-SccDoctorReport {
    $results = @(Get-SccDoctorResults)
    $failures = @($results | Where-Object { $_.Status -eq "FAIL" })
    $warnings = @($results | Where-Object { $_.Status -eq "WARN" })
    $failCount = $failures.Count
    $warnCount = $warnings.Count
    $overallStatus = if ($failCount -gt 0) {
        "FAIL"
    } elseif ($warnCount -gt 0) {
        "WARN"
    } else {
        "PASS"
    }

    $overallText = switch ($overallStatus) {
        "FAIL" { "存在失败" }
        "WARN" { "存在警告" }
        default { "通过" }
    }

    $rootResult = Get-SccDoctorResultByName -Results $results -Name "Root"
    $bootstrapResult = Get-SccDoctorResultByName -Results $results -Name "Bootstrap"
    $configResult = Get-SccDoctorResultByName -Results $results -Name "Config"
    $stateResult = Get-SccDoctorResultByName -Results $results -Name "State"
    $modulesResult = Get-SccDoctorResultByName -Results $results -Name "Modules"
    $verifyResult = Get-SccDoctorResultByName -Results $results -Name "VerifyScript"
    $profileResults = @($results | Where-Object { $_.Name -like "Profile/*" } | Sort-Object Name)
    $moduleNames = @(
        $results |
            Where-Object { $_.Name -like "Module/*" } |
            ForEach-Object { $_.Name.Substring("Module/".Length) } |
            Sort-Object
    )
    $config = $null
    try {
        $config = Get-SccConfig
    } catch {
        $config = $null
    }
    $managerCommandNames = @(Get-SccManagerCommandNames -Config $config)
    $primaryCommandName = Get-SccPrimaryCommandName -Config $config

    $profiles = foreach ($profileResult in $profileResults) {
        [pscustomobject][ordered]@{
            Name    = $profileResult.Name.Substring("Profile/".Length)
            Status  = $profileResult.Status
            Message = $profileResult.Message
            Path    = if ($profileResult.Status -eq "PASS") { $profileResult.Message } else { $null }
        }
    }

    $modules = foreach ($moduleName in $moduleNames) {
        $moduleResult = Get-SccDoctorResultByName -Results $results -Name "Module/$moduleName"
        $helpResult = Get-SccDoctorResultByName -Results $results -Name "Help/$moduleName"
        $commandEntries = @(Get-SccModuleCommandHelpEntries -ModuleName $moduleName)
        $commandResults = @($commandEntries | ForEach-Object {
            $commandResult = Get-SccDoctorResultByName -Results $results -Name "Command/$($_.CommandName)"
            if ($null -ne $commandResult) {
                [pscustomobject][ordered]@{
                    Name    = $_.CommandName
                    Status  = $commandResult.Status
                    Message = $commandResult.Message
                }
            }
        } | Where-Object { $null -ne $_ })
        $sectionResults = @($moduleResult, $helpResult) + $commandResults | Where-Object { $null -ne $_ }

        [pscustomobject][ordered]@{
            Name           = $moduleName
            Status         = Get-SccDoctorAggregateStatus -SectionResults $sectionResults
            Message        = if ($moduleResult) { $moduleResult.Message } else { "未找到模块脚本结果" }
            Path           = if ($moduleResult -and $moduleResult.Status -eq "PASS") { $moduleResult.Message } else { $null }
            HelpStatus     = if ($helpResult) { $helpResult.Status } else { "WARN" }
            HelpMessage    = if ($helpResult) { $helpResult.Message } else { "模块已启用，但未注册命令帮助。" }
            Commands       = @($commandEntries.CommandName)
            CommandResults = @($commandResults)
        }
    }

    $tools = @()
    if ($bootstrapResult) {
        $tools += [pscustomobject][ordered]@{
            Name    = "bootstrap"
            Status  = $bootstrapResult.Status
            Message = $bootstrapResult.Message
            Path    = if ($bootstrapResult.Status -eq "PASS") { $bootstrapResult.Message } else { $null }
        }
    }
    if ($configResult) {
        $tools += [pscustomobject][ordered]@{
            Name    = "config"
            Status  = $configResult.Status
            Message = $configResult.Message -replace '^配置可读:\s*', ''
            Path    = if ($configResult.Status -eq "PASS") { $configResult.Message -replace '^配置可读:\s*', '' } else { $null }
        }
    }
    foreach ($commandName in $managerCommandNames) {
        $commandResult = Get-SccDoctorResultByName -Results $results -Name "Command/$commandName"
        if ($commandResult) {
            $tools += [pscustomobject][ordered]@{
                Name    = if ($commandName -eq $primaryCommandName) { "command:$commandName" } else { "fallback:$commandName" }
                Status  = $commandResult.Status
                Message = $commandResult.Message
                Path    = $null
            }
        }
    }
    if ($verifyResult) {
        $tools += [pscustomobject][ordered]@{
            Name    = "verify.ps1"
            Status  = $verifyResult.Status
            Message = $verifyResult.Message
            Path    = if ($verifyResult.Status -eq "PASS") { $verifyResult.Message } else { $null }
        }
    }

    $verifyPath = Join-Path (Get-SccRootPath) "verify.ps1"
    $reportRootPath = if ($rootResult) {
        $rootResult.Message -replace '^工作区:\s*', ''
    } else {
        Get-SccRootPath
    }

    return [pscustomobject][ordered]@{
        GeneratedAt        = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
        OverallStatus      = $overallStatus
        OverallText        = $overallText
        FailCount          = $failCount
        WarnCount          = $warnCount
        RootPath           = $reportRootPath
        PrimaryCommandName = $primaryCommandName
        EnabledModulesText = if ($stateResult) { $stateResult.Message -replace '^已启用模块:\s*', '' } else { "未知" }
        DiscoveredText     = if ($modulesResult) { $modulesResult.Message -replace '^已发现模块:\s*', '' } else { "未知" }
        Profiles           = @($profiles)
        Modules            = @($modules)
        Tools              = @($tools)
        Issues             = @($results | Where-Object { $_.Status -ne "PASS" } | Sort-Object Name)
        SuggestedSmokeTest = if (Test-Path $verifyPath) {
            "pwsh -NoProfile -File $(ConvertTo-SccDoctorDisplayPath -Path $verifyPath -RootPath $reportRootPath)"
        } else {
            $null
        }
        Results            = @($results)
    }
}

function Show-SccDoctor {
    param(
        [ValidateSet("default", "verbose", "fancy")]
        [string]$Mode = "default"
    )

    $report = Get-SccDoctorReport
    $useFancy = $Mode -eq "fancy"
    $showVerbose = $Mode -eq "verbose"
    $psStyleVariable = Get-Variable -Name PSStyle -ErrorAction SilentlyContinue
    $supportsAnsi = $null -ne $psStyleVariable -and $psStyleVariable.Value.OutputRendering -ne "PlainText"
    $styles = if ($supportsAnsi) {
        @{
            Reset   = $PSStyle.Reset
            Title   = "$($PSStyle.Bold)$($PSStyle.Foreground.BrightCyan)"
            Section = "$($PSStyle.Bold)$($PSStyle.Foreground.Cyan)"
            Key     = $PSStyle.Foreground.Cyan
            Value   = $PSStyle.Foreground.White
            Muted   = $PSStyle.Foreground.BrightBlack
            Accent  = $PSStyle.Foreground.BrightWhite
            PASS    = $PSStyle.Foreground.BrightGreen
            WARN    = $PSStyle.Foreground.BrightYellow
            FAIL    = $PSStyle.Foreground.BrightRed
        }
    } else {
        @{
            Reset   = ""
            Title   = ""
            Section = ""
            Key     = ""
            Value   = ""
            Muted   = ""
            Accent  = ""
            PASS    = ""
            WARN    = ""
            FAIL    = ""
        }
    }
    $renderWidth = 100
    try {
        $rawWidth = [int]$Host.UI.RawUI.WindowSize.Width
        if ($rawWidth -gt 0) {
            $renderWidth = [Math]::Max(60, $rawWidth)
        }
    } catch {
        $renderWidth = 100
    }
    $isCompact = $renderWidth -lt 96
    $keyWidth = if ($isCompact) { 8 } else { 10 }
    $statusKeyWidth = if ($isCompact) { 10 } else { 12 }
    $sectionRuleWidth = [Math]::Max(0, [Math]::Min(36, $renderWidth - 18))

    function Format-SccDoctorText {
        param(
            [string]$Text,
            [string]$StyleName
        )

        if ([string]::IsNullOrEmpty($styles[$StyleName])) {
            return $Text
        }

        return "{0}{1}{2}" -f $styles[$StyleName], $Text, $styles.Reset
    }

    function Get-SccDoctorCompactPath {
        param(
            [string]$DisplayPath,
            [int]$MaxWidth
        )

        if ([string]::IsNullOrWhiteSpace($DisplayPath) -or $DisplayPath.Length -le $MaxWidth) {
            return $DisplayPath
        }

        $leaf = Split-Path $DisplayPath -Leaf
        if (-not [string]::IsNullOrWhiteSpace($leaf) -and $leaf.Length -ge $MaxWidth) {
            return $leaf
        }

        $segments = $DisplayPath -split '[\\/]'
        if ($segments.Count -ge 2) {
            $candidate = "{0}\...\{1}" -f $segments[0], $leaf
            if ($candidate.Length -le $MaxWidth) {
                return $candidate
            }
        }

        $tail = "...\{0}" -f $leaf
        if ($tail.Length -le $MaxWidth) {
            return $tail
        }

        return $leaf
    }

    function Get-SccDoctorRawValue {
        param(
            [string]$Value,
            [switch]$PathValue,
            [int]$MaxWidth = 0
        )

        if ([string]::IsNullOrWhiteSpace($Value)) {
            return ""
        }

        $rawValue = if ($PathValue) {
            ConvertTo-SccDoctorDisplayPath -Path $Value -RootPath $report.RootPath
        } else {
            $Value
        }

        if ($PathValue -and $MaxWidth -gt 0) {
            return (Get-SccDoctorCompactPath -DisplayPath $rawValue -MaxWidth $MaxWidth)
        }

        return $rawValue
    }

    function Split-SccDoctorText {
        param(
            [string]$Text,
            [int]$MaxWidth
        )

        if ([string]::IsNullOrWhiteSpace($Text) -or $Text.Length -le $MaxWidth) {
            return @($Text)
        }

        $lines = @()
        $remaining = $Text.Trim()
        while ($remaining.Length -gt $MaxWidth) {
            $chunk = $remaining.Substring(0, $MaxWidth)
            $breakAt = $chunk.LastIndexOf(' ')
            if ($breakAt -lt [Math]::Floor($MaxWidth / 2)) {
                $breakAt = $chunk.LastIndexOf([char]',')
            }
            if ($breakAt -lt [Math]::Floor($MaxWidth / 2)) {
                $breakAt = $MaxWidth
            }

            $slice = $remaining.Substring(0, [Math]::Min($breakAt, $remaining.Length))
            $line = $slice.Trim()
            if ([string]::IsNullOrWhiteSpace($line)) {
                $line = $slice
            }

            $lines += $line
            $remaining = $remaining.Substring([Math]::Min($breakAt, $remaining.Length)).TrimStart(' ', ',')
        }

        if (-not [string]::IsNullOrWhiteSpace($remaining)) {
            $lines += $remaining
        }

        return @($lines)
    }

    function Format-SccDoctorRenderedValue {
        param(
            [string]$Value,
            [switch]$PathValue,
            [switch]$CompactStyle
        )

        if (-not $PathValue) {
            return (Format-SccDoctorText -Text $Value -StyleName "Value")
        }

        if (-not $supportsAnsi -or $CompactStyle) {
            if ($supportsAnsi) {
                return (Format-SccDoctorText -Text $Value -StyleName "Accent")
            }
            return $Value
        }

        $leaf = Split-Path $Value -Leaf
        $parent = Split-Path $Value -Parent
        if ([string]::IsNullOrWhiteSpace($leaf) -or $leaf -eq $Value) {
            return (Format-SccDoctorText -Text $Value -StyleName "Accent")
        }

        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq ".") {
            return (Format-SccDoctorText -Text $Value -StyleName "Accent")
        }

        return "{0}\{1}" -f (Format-SccDoctorText -Text $parent -StyleName "Muted"), (Format-SccDoctorText -Text $leaf -StyleName "Accent")
    }

    function Get-SccDoctorStatusToken {
        param([string]$Status)

        if ($useFancy) {
            $token = switch ($Status) {
                "PASS" { "✔" }
                "WARN" { "⚠" }
                default { "✖" }
            }
            return $token
        }

        return $Status
    }

    function Write-SccDoctorHeader {
        $summaryToken = Get-SccDoctorStatusToken -Status $report.OverallStatus

        Write-Host ""
        Write-Host (Format-SccDoctorText -Text "TermForge Doctor" -StyleName "Title")
        if ($useFancy) {
            Write-Host (Format-SccDoctorText -Text ("─" * [Math]::Max(18, [Math]::Min(48, $renderWidth - 4))) -StyleName "Muted")
        }

        if ($isCompact) {
            Write-Host ("{0} {1}" -f `
                (Format-SccDoctorText -Text $summaryToken -StyleName $report.OverallStatus), `
                (Format-SccDoctorText -Text $report.OverallText -StyleName $report.OverallStatus))
            Write-Host ("{0} {1}  {2} {3}" -f `
                (Format-SccDoctorText -Text "fail" -StyleName "Muted"), `
                (Format-SccDoctorText -Text $report.FailCount -StyleName "Value"), `
                (Format-SccDoctorText -Text "warn" -StyleName "Muted"), `
                (Format-SccDoctorText -Text $report.WarnCount -StyleName "Value"))
            return
        }

        Write-Host ("{0} {1}  fail {2}  warn {3}" -f `
            (Format-SccDoctorText -Text $summaryToken -StyleName $report.OverallStatus), `
            (Format-SccDoctorText -Text $report.OverallText -StyleName $report.OverallStatus), `
            (Format-SccDoctorText -Text $report.FailCount -StyleName "Value"), `
            (Format-SccDoctorText -Text $report.WarnCount -StyleName "Value"))
    }

    function Write-SccDoctorSection {
        param([string]$Title)

        Write-Host ""
        if ($useFancy) {
            if ($isCompact) {
                Write-Host ("{0} {1}" -f (Format-SccDoctorText -Text "■" -StyleName "Section"), (Format-SccDoctorText -Text $Title -StyleName "Section"))
            } else {
                Write-Host ("{0} {1} {2}" -f `
                    (Format-SccDoctorText -Text "■" -StyleName "Section"), `
                    (Format-SccDoctorText -Text $Title -StyleName "Section"), `
                    (Format-SccDoctorText -Text ("─" * $sectionRuleWidth) -StyleName "Muted"))
            }
        } else {
            Write-Host (Format-SccDoctorText -Text $Title -StyleName "Section")
        }
    }

    function Write-SccDoctorValueLines {
        param(
            [string[]]$Lines,
            [int]$Indent,
            [switch]$PathValue,
            [switch]$CompactStyle
        )

        foreach ($line in $Lines) {
            Write-Host ("{0}{1}" -f `
                (" " * $Indent), `
                (Format-SccDoctorRenderedValue -Value $line -PathValue:$PathValue -CompactStyle:$CompactStyle))
        }
    }

    function Write-SccDoctorTextLine {
        param(
            [string]$Label,
            [string]$Value,
            [int]$Indent = 2,
            [switch]$PathValue
        )

        $labelText = "{0,-$keyWidth}" -f $Label
        $oneLineWidth = [Math]::Max(20, $renderWidth - ($Indent + $keyWidth + 4))
        $rawValue = Get-SccDoctorRawValue -Value $Value -PathValue:$PathValue -MaxWidth $oneLineWidth
        $shouldStack = $isCompact -or $rawValue.Length -gt $oneLineWidth

        if (-not $shouldStack) {
            Write-Host ("{0}{1} {2} {3}" -f `
                (" " * $Indent), `
                (Format-SccDoctorText -Text $labelText -StyleName "Key"), `
                (Format-SccDoctorText -Text ":" -StyleName "Muted"), `
                (Format-SccDoctorRenderedValue -Value $rawValue -PathValue:$PathValue))
            return
        }

        Write-Host ("{0}{1}" -f (" " * $Indent), (Format-SccDoctorText -Text $Label -StyleName "Key"))
        $valueLines = if ($PathValue) {
            @($rawValue)
        } else {
            Split-SccDoctorText -Text $rawValue -MaxWidth ([Math]::Max(20, $renderWidth - ($Indent + 3)))
        }
        Write-SccDoctorValueLines -Lines $valueLines -Indent ($Indent + 2) -PathValue:$PathValue -CompactStyle
    }

    function Write-SccDoctorStatusLine {
        param(
            [string]$Status,
            [string]$Label,
            [string]$Value,
            [int]$Indent = 2,
            [switch]$PathValue
        )

        $tokenText = if ($useFancy) {
            "{0,-2}" -f (Get-SccDoctorStatusToken -Status $Status)
        } else {
            "{0,-4}" -f (Get-SccDoctorStatusToken -Status $Status)
        }
        $labelText = "{0,-$statusKeyWidth}" -f $Label
        $oneLineWidth = [Math]::Max(18, $renderWidth - ($Indent + $tokenText.Length + $statusKeyWidth + 4))
        $rawValue = Get-SccDoctorRawValue -Value $Value -PathValue:$PathValue -MaxWidth $oneLineWidth
        $shouldStack = $isCompact -or $rawValue.Length -gt $oneLineWidth

        if (-not $shouldStack) {
            Write-Host ("{0}{1} {2} {3} {4}" -f `
                (" " * $Indent), `
                (Format-SccDoctorText -Text $tokenText -StyleName $Status), `
                (Format-SccDoctorText -Text $labelText -StyleName "Key"), `
                (Format-SccDoctorText -Text ":" -StyleName "Muted"), `
                (Format-SccDoctorRenderedValue -Value $rawValue -PathValue:$PathValue))
            return
        }

        Write-Host ("{0}{1} {2}" -f `
            (" " * $Indent), `
            (Format-SccDoctorText -Text $tokenText.Trim() -StyleName $Status), `
            (Format-SccDoctorText -Text $Label -StyleName "Key"))
        $valueLines = if ($PathValue) {
            @($rawValue)
        } else {
            Split-SccDoctorText -Text $rawValue -MaxWidth ([Math]::Max(18, $renderWidth - ($Indent + 5)))
        }
        Write-SccDoctorValueLines -Lines $valueLines -Indent ($Indent + 4) -PathValue:$PathValue -CompactStyle
    }

    Write-SccDoctorHeader

    Write-SccDoctorSection -Title "Overview"
    Write-SccDoctorStatusLine -Status $report.OverallStatus -Label "状态" -Value $report.OverallText
    Write-SccDoctorTextLine -Label "工作区" -Value $report.RootPath -PathValue
    Write-SccDoctorTextLine -Label "启用模块" -Value $report.EnabledModulesText
    Write-SccDoctorTextLine -Label "发现模块" -Value $report.DiscoveredText

    if ($report.Profiles.Count -gt 0) {
        Write-SccDoctorSection -Title "Profiles"
        foreach ($profile in $report.Profiles) {
            Write-SccDoctorStatusLine -Status $profile.Status -Label $profile.Name -Value $(if ($profile.Path) { $profile.Path } else { $profile.Message }) -PathValue:($null -ne $profile.Path)
        }
    }

    if ($report.Modules.Count -gt 0) {
        Write-SccDoctorSection -Title "Modules"
        foreach ($module in $report.Modules) {
            Write-SccDoctorStatusLine -Status $module.Status -Label $module.Name -Value $(if ($module.Path) { $module.Path } else { $module.Message }) -PathValue:($null -ne $module.Path)
            Write-SccDoctorTextLine -Label "帮助" -Value $module.HelpMessage -Indent 5
            Write-SccDoctorTextLine -Label "命令" -Value $(if ($module.Commands.Count -gt 0) { $module.Commands -join ", " } else { "无" }) -Indent 5
            if ($showVerbose -and $module.CommandResults.Count -gt 0) {
                foreach ($commandResult in $module.CommandResults) {
                    Write-SccDoctorStatusLine -Status $commandResult.Status -Label $commandResult.Name -Value $commandResult.Message -Indent 5
                }
            }
            Write-Host ""
        }
    }

    Write-SccDoctorSection -Title "Tools"
    foreach ($tool in $report.Tools) {
        Write-SccDoctorStatusLine -Status $tool.Status -Label $tool.Name -Value $(if ($tool.Path) { $tool.Path } else { $tool.Message }) -PathValue:($null -ne $tool.Path)
    }
    if ($report.SuggestedSmokeTest) {
        Write-SccDoctorTextLine -Label "smoke test" -Value $report.SuggestedSmokeTest
    }

    # Proxy Targets section
    try {
        $statusOutput = Invoke-SccDotNetCli status --json 2>$null
        $statusJson = $statusOutput | ConvertFrom-Json
        $targetStates = @($statusJson.Payload.Proxy.TargetStates)
        if ($targetStates.Count -gt 0) {
            Write-SccDoctorSection -Title "Proxy Targets"
            foreach ($ts in $targetStates) {
                $tsStatus = if (-not $ts.Available) { "WARN" } elseif ($ts.Enabled) { "PASS" } else { "WARN" }
                $tsMessage = if (-not $ts.Available) { "未检测到" } elseif ($ts.Enabled) { "http=$($ts.Http)" } else { "代理未启用" }
                Write-SccDoctorStatusLine -Status $tsStatus -Label $ts.Target -Value $tsMessage
            }
        }
    } catch {
        # Skip if .NET CLI unavailable
    }

    if ($report.Issues.Count -gt 0) {
        Write-SccDoctorSection -Title "Issues"
        foreach ($issue in $report.Issues) {
            Write-SccDoctorStatusLine -Status $issue.Status -Label $issue.Name -Value $issue.Message
        }
    } elseif (-not $showVerbose) {
        Write-SccDoctorSection -Title "Next"
        Write-SccDoctorTextLine -Label "建议" -Value "当前状态正常；修改 profile 或模块后，运行 verify.ps1 做完整 smoke test。"
    }

    return ($report.FailCount -eq 0)
}
