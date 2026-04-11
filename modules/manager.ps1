$commonPath = Join-Path $PSScriptRoot "common.ps1"
if (Test-Path $commonPath) {
    . $commonPath
}

Initialize-SccHelpRegistry

function Show-SccManagerHelp {
    $commandName = Get-SccPrimaryCommandName
    $fallbackCommandName = Get-SccFallbackCommandName

    Write-Host "=== 系统模块管理器 ===" -ForegroundColor Cyan
    Write-Host "用法: $commandName <命令> [模块名|命令名|模式]"
    Write-Host "命令:"
    Write-Host "  list | status        - 查看模块状态及可用子命令"
    Write-Host "  doctor [模式]        - 执行当前会话诊断；模式: default | fancy | verbose | json"
    Write-Host "  enable <模块名>      - 启用已存在的模块"
    Write-Host "  disable <模块名>     - 禁用指定模块"
    Write-Host "  reload               - 重新加载当前 PowerShell profile"
    Write-Host "  help [名称]          - 查看主命令、模块或命令帮助"
    Write-Host "示例:"
    Write-Host "  $commandName doctor fancy"
    Write-Host "  $commandName doctor json"
    Write-Host "  $commandName help theme"
    Write-Host "  $commandName help posh"
    if ($fallbackCommandName -ne $commandName) {
        Write-Host "恢复入口: $fallbackCommandName" -ForegroundColor DarkGray
    }
}

function Show-SccHelpEntry {
    param([string]$Name)

    Initialize-SccHelpRegistry
    $managerCommandNames = @(Get-SccManagerCommandNames)

    if ([string]::IsNullOrWhiteSpace($Name) -or $managerCommandNames -contains $Name) {
        Show-SccManagerHelp
        return
    }

    if ($global:SccCommandHelp.ContainsKey($Name)) {
        [void](Show-SccCommandHelp -CommandName $Name)
        return
    }

    if ($global:SccHelp.ContainsKey($Name) -or (Get-SccModuleCommandHelpEntries -ModuleName $Name).Count -gt 0) {
        [void](Show-SccModuleHelp -ModuleName $Name)
        return
    }

    Write-Host "[SCC] 未找到 '$Name' 的帮助信息。" -ForegroundColor Yellow
}

function Show-SccModuleList {
    param(
        [Parameter(Mandatory)]$State,
        [Parameter(Mandatory)][string[]]$AvailableModules
    )

    $moduleNames = @($AvailableModules + $State.PSObject.Properties.Name) | Sort-Object -Unique
    if ($moduleNames.Count -eq 0) {
        Write-Host "未发现任何模块。" -ForegroundColor Yellow
        return
    }

    Write-Host "=== 模块状态与可用命令 ===" -ForegroundColor Cyan
    foreach ($moduleName in $moduleNames) {
        $exists = $AvailableModules -contains $moduleName
        $isEnabled = $false

        if ($State.PSObject.Properties.Match($moduleName).Count -gt 0) {
            $isEnabled = [bool]$State.$moduleName
        }

        if (-not $exists) {
            Write-Host "[缺失] $moduleName" -ForegroundColor Red
            continue
        }

        if ($isEnabled) {
            Write-Host "[启用] $moduleName" -ForegroundColor Green
        } else {
            Write-Host "[禁用] $moduleName" -ForegroundColor DarkGray
        }

        if ($global:SccHelp.ContainsKey($moduleName)) {
            $global:SccHelp[$moduleName] -split "`n" | ForEach-Object {
                Write-Host "       $_" -ForegroundColor DarkGray
            }
        }
    }
}

function Set-SccModuleState {
    param(
        [Parameter(Mandatory)][string]$ModuleName,
        [Parameter(Mandatory)][bool]$Enabled
    )

    $availableModules = Get-SccAvailableModuleNames
    if ($availableModules -notcontains $ModuleName) {
        throw "模块 '$ModuleName' 不存在，请先在 modules 目录中创建对应脚本。"
    }

    $state = Ensure-SccStateFile
    if ($state.PSObject.Properties.Match($ModuleName).Count -eq 0) {
        $state | Add-Member -NotePropertyName $ModuleName -NotePropertyValue $Enabled
    } else {
        $state.$ModuleName = $Enabled
    }

    Write-SccJsonFile -Path (Get-SccStatePath) -Value $state
}

function Invoke-SccManagerCommand {
    param(
        [ValidateSet('list','status','doctor','enable','disable','help','reload')][string]$Action = 'help',
        [string]$Module
    )

    $commandName = Get-SccPrimaryCommandName

    switch ($Action) {
        'help' {
            Show-SccHelpEntry -Name $Module
        }
        'list' {
            try {
                Show-SccModuleList -State (Ensure-SccStateFile) -AvailableModules (Get-SccAvailableModuleNames)
            } catch {
                Write-Host "[SCC] 无法读取模块状态: $($_.Exception.Message)" -ForegroundColor Red
            }
        }
        'status' {
            try {
                Show-SccModuleList -State (Ensure-SccStateFile) -AvailableModules (Get-SccAvailableModuleNames)
            } catch {
                Write-Host "[SCC] 无法读取模块状态: $($_.Exception.Message)" -ForegroundColor Red
            }
        }
        'doctor' {
            $doctorMode = if ([string]::IsNullOrWhiteSpace($Module)) {
                "default"
            } else {
                $Module.ToLowerInvariant()
            }

            switch ($doctorMode) {
                "default" {
                    [void](Show-SccDoctor -Mode "default")
                }
                "verbose" {
                    [void](Show-SccDoctor -Mode "verbose")
                }
                "fancy" {
                    [void](Show-SccDoctor -Mode "fancy")
                }
                "json" {
                    Get-SccDoctorReport | ConvertTo-Json -Depth 8
                }
                default {
                    Write-Host "[SCC] 不支持的 doctor 模式: $doctorMode。可选: default, fancy, verbose, json" -ForegroundColor Red
                }
            }
        }
        'enable' {
            if (-not $Module) {
                Write-Host "请指定模块名。" -ForegroundColor Red
                return
            }

            try {
                Set-SccModuleState -ModuleName $Module -Enabled $true
                Write-Host "模块 '$Module' 已启用。运行 '$commandName reload' 或 '. `$PROFILE`' 生效。" -ForegroundColor Green
            } catch {
                Write-Host "[SCC] 启用失败: $($_.Exception.Message)" -ForegroundColor Red
            }
        }
        'disable' {
            if (-not $Module) {
                Write-Host "请指定模块名。" -ForegroundColor Red
                return
            }

            try {
                Set-SccModuleState -ModuleName $Module -Enabled $false
                Write-Host "模块 '$Module' 已禁用。运行 '$commandName reload' 或 '. `$PROFILE`' 生效。" -ForegroundColor Yellow
            } catch {
                Write-Host "[SCC] 禁用失败: $($_.Exception.Message)" -ForegroundColor Red
            }
        }
        'reload' {
            if (-not (Test-Path $PROFILE)) {
                Write-Host "[SCC] 当前 PROFILE 不存在: $PROFILE" -ForegroundColor Red
                return
            }

            try {
                . $PROFILE
                Write-Host "已重新加载: $PROFILE" -ForegroundColor Green
            } catch {
                Write-Host "[SCC] 重新加载失败: $($_.Exception.Message)" -ForegroundColor Red
            }
        }
    }
}

function Register-SccManagerCommands {
    $commandNames = @(Get-SccManagerCommandNames)
    $previousCommandNames = @()
    if ($global:SccManagerCommandNames -is [System.Array]) {
        $previousCommandNames = @($global:SccManagerCommandNames)
    }

    foreach ($oldCommandName in $previousCommandNames) {
        if ($commandNames -notcontains $oldCommandName -and (Test-Path "Function:\global:$oldCommandName")) {
            Remove-Item -Path "Function:\global:$oldCommandName" -ErrorAction SilentlyContinue
        }
    }

    $wrapper = {
        param([Parameter(ValueFromRemainingArguments = $true)][object[]]$RemainingArgs)

        Invoke-SccManagerCommand @RemainingArgs
    }

    foreach ($commandName in $commandNames) {
        if (Test-Path "Function:\global:$commandName") {
            Set-Item -Path "Function:\global:$commandName" -Value $wrapper
        } else {
            New-Item -Path "Function:\global:$commandName" -Value $wrapper | Out-Null
        }
    }

    $global:SccManagerCommandNames = $commandNames
}
