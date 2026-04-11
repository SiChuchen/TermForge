$commonPath = Join-Path $PSScriptRoot "common.ps1"
if (Test-Path $commonPath) {
    . $commonPath
}

Initialize-SccHelpRegistry
Register-SccHelp -ModuleName "proxy" -HelpText "proxy - 查看当前终端代理配置与环境变量状态`nproxy scan --json - 输出 env 目标的结构化代理状态`nproxy plan/apply/rollback ... --json - 调用 .NET 控制面执行 env 代理工作流`nproxy bypass add <host> [更多 host] - 追加 NO_PROXY 绕过项并写回配置"
Register-SccCommandHelp -CommandName "proxy" -ModuleName "proxy" -HelpText "用法: proxy [-Help]`n      proxy scan --json`n      proxy plan --mode <enable|disable> --targets env ... --json`n      proxy apply --plan-id <id> --json`n      proxy rollback --change-id <id> --json`n      proxy bypass add <host> [更多 host]`n作用: 显示代理模块的启用状态；调用 .NET 控制面执行 env 目标的 scan/plan/apply/rollback；或把主机名/IP 追加到 noProxy 配置。`n说明: bypass add 支持逗号分隔；请传 localhost、127.0.0.1、host.docker.internal 这类主机/IP，不要带 http://。"

$config = Get-SccConfig
$script:ProxyEnabled = [bool]$config.proxy.enabled
$script:HttpProxy = if ($config.proxy.http -is [string]) { $config.proxy.http.Trim() } else { "" }
$script:HttpsProxy = if ($config.proxy.https -is [string]) { $config.proxy.https.Trim() } else { "" }
$script:NoProxy = if ($config.proxy.noProxy -is [string]) { $config.proxy.noProxy.Trim() } else { "" }

function Get-SccProxyEntries {
    param([string[]]$Values)

    $entries = foreach ($value in $Values) {
        if ($value -isnot [string]) {
            continue
        }

        foreach ($entry in ($value -split ',')) {
            $trimmedEntry = $entry.Trim()
            if ([string]::IsNullOrWhiteSpace($trimmedEntry)) {
                continue
            }

            if ($trimmedEntry -match '^[A-Za-z][A-Za-z0-9+.-]*://') {
                throw "NO_PROXY 条目只接受主机名或 IP，不要带协议: $trimmedEntry"
            }

            $trimmedEntry
        }
    }

    return @($entries | Select-Object -Unique)
}

function Set-SccProxyEnvironment {
    param(
        [Parameter(Mandatory)][bool]$Enabled,
        [string]$HttpProxy,
        [string]$HttpsProxy,
        [string]$NoProxy
    )

    if (-not $Enabled) {
        Remove-Item Env:\http_proxy -ErrorAction SilentlyContinue
        Remove-Item Env:\HTTP_PROXY -ErrorAction SilentlyContinue
        Remove-Item Env:\https_proxy -ErrorAction SilentlyContinue
        Remove-Item Env:\HTTPS_PROXY -ErrorAction SilentlyContinue
        Remove-Item Env:\no_proxy -ErrorAction SilentlyContinue
        Remove-Item Env:\NO_PROXY -ErrorAction SilentlyContinue
        return
    }

    $resolvedHttpsProxy = if ([string]::IsNullOrWhiteSpace($HttpsProxy)) {
        $HttpProxy
    } else {
        $HttpsProxy
    }

    if ([string]::IsNullOrWhiteSpace($HttpProxy)) {
        Remove-Item Env:\http_proxy -ErrorAction SilentlyContinue
        Remove-Item Env:\HTTP_PROXY -ErrorAction SilentlyContinue
    } else {
        $env:http_proxy = $HttpProxy
        $env:HTTP_PROXY = $HttpProxy
    }

    if ([string]::IsNullOrWhiteSpace($resolvedHttpsProxy)) {
        Remove-Item Env:\https_proxy -ErrorAction SilentlyContinue
        Remove-Item Env:\HTTPS_PROXY -ErrorAction SilentlyContinue
    } else {
        $env:https_proxy = $resolvedHttpsProxy
        $env:HTTPS_PROXY = $resolvedHttpsProxy
    }

    if ([string]::IsNullOrWhiteSpace($NoProxy)) {
        Remove-Item Env:\no_proxy -ErrorAction SilentlyContinue
        Remove-Item Env:\NO_PROXY -ErrorAction SilentlyContinue
    } else {
        $env:no_proxy = $NoProxy
        $env:NO_PROXY = $NoProxy
    }
}

function Save-SccProxyConfig {
    $config = Get-SccConfig
    $config.proxy.enabled = [bool]$script:ProxyEnabled
    $config.proxy.http = $script:HttpProxy
    $config.proxy.https = $script:HttpsProxy
    $config.proxy.noProxy = $script:NoProxy
    Save-SccConfig -Config $config
}

function Add-SccProxyBypassEntries {
    param([string[]]$Entries)

    $incomingEntries = @(Get-SccProxyEntries -Values $Entries)
    if ($incomingEntries.Count -eq 0) {
        throw "请提供至少一个主机名或 IP，例如: proxy bypass add 127.0.0.1 localhost"
    }

    $existingEntries = @(Get-SccProxyEntries -Values @($script:NoProxy))
    $mergedEntries = @($existingEntries + $incomingEntries | Select-Object -Unique)
    $script:NoProxy = $mergedEntries -join ","
    Save-SccProxyConfig
    Set-SccProxyEnvironment -Enabled $script:ProxyEnabled -HttpProxy $script:HttpProxy -HttpsProxy $script:HttpsProxy -NoProxy $script:NoProxy

    $addedEntries = @($mergedEntries | Where-Object { $existingEntries -notcontains $_ })
    if ($addedEntries.Count -eq 0) {
        Write-Host "NO_PROXY 未变化，目标项已存在: $($incomingEntries -join ', ')" -ForegroundColor Yellow
        return
    }

    if ($script:ProxyEnabled) {
        Write-Host "已添加 NO_PROXY 绕过并应用到当前会话: $($addedEntries -join ', ')" -ForegroundColor Green
    } else {
        Write-Host "已写入 proxy.noProxy: $($addedEntries -join ', ')" -ForegroundColor Green
        Write-Host "当前 proxy.enabled=false；启用代理后这些绕过项会随环境变量一起生效。" -ForegroundColor Yellow
    }
}

Set-SccProxyEnvironment -Enabled $script:ProxyEnabled -HttpProxy $script:HttpProxy -HttpsProxy $script:HttpsProxy -NoProxy $script:NoProxy

function Show-SccProxyStatus {
    $resolvedHttpsProxy = if ([string]::IsNullOrWhiteSpace($script:HttpsProxy)) {
        $script:HttpProxy
    } else {
        $script:HttpsProxy
    }

    Write-Host "=== 代理模块 (Proxy) 当前状态 ===" -ForegroundColor Cyan
    Write-Host "已启用     : $script:ProxyEnabled"
    Write-Host "HTTP       : $(if ([string]::IsNullOrWhiteSpace($script:HttpProxy)) { '(未配置)' } else { $script:HttpProxy })"
    Write-Host "HTTPS      : $(if ([string]::IsNullOrWhiteSpace($resolvedHttpsProxy)) { '(未配置)' } else { $resolvedHttpsProxy })"
    Write-Host "NO_PROXY   : $(if ([string]::IsNullOrWhiteSpace($script:NoProxy)) { '(未配置)' } else { $script:NoProxy })"
    Write-Host "env:http   : $(if ([string]::IsNullOrWhiteSpace($env:http_proxy)) { '(未设置)' } else { $env:http_proxy })"
    Write-Host "env:https  : $(if ([string]::IsNullOrWhiteSpace($env:https_proxy)) { '(未设置)' } else { $env:https_proxy })"
    Write-Host "env:no_prx : $(if ([string]::IsNullOrWhiteSpace($env:no_proxy)) { '(未设置)' } else { $env:no_proxy })"
    Write-Host "配置文件   : $(Get-SccConfigPath)"
}

function proxy {
    param(
        [string]$Action,
        [string]$SubAction,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments,
        [switch]$Help
    )

    if ($Help -or $Action -eq "help") {
        [void](Show-SccCommandHelp -CommandName "proxy")
        return
    }

    $normalizedAction = if ([string]::IsNullOrWhiteSpace($Action)) {
        ""
    } else {
        $Action.ToLowerInvariant()
    }

    if ($normalizedAction -eq "bypass") {
        $normalizedSubAction = if ([string]::IsNullOrWhiteSpace($SubAction)) {
            ""
        } else {
            $SubAction.ToLowerInvariant()
        }

        switch ($normalizedSubAction) {
            "add" {
                try {
                    Add-SccProxyBypassEntries -Entries $Arguments
                } catch {
                    Write-Host "[Proxy] $($_.Exception.Message)" -ForegroundColor Red
                }
                return
            }
            default {
                Write-Host "[Proxy] 仅支持: proxy bypass add <host>" -ForegroundColor Yellow
                return
            }
        }
    }

    if ($normalizedAction -in @('scan','plan','apply','rollback')) {
        $bridgeArgs = @('proxy', $normalizedAction)
        if (-not [string]::IsNullOrWhiteSpace($SubAction)) {
            $bridgeArgs += $SubAction
        }
        if ($Arguments.Count -gt 0) {
            $bridgeArgs += $Arguments
        }
        Invoke-SccDotNetCli @bridgeArgs
        return
    }

    Show-SccProxyStatus
}
