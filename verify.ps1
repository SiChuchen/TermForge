$ErrorActionPreference = "Stop"
$script:SccRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$commonPath = Join-Path $script:SccRoot "modules\common.ps1"

if (-not (Test-Path $commonPath)) {
    Write-Host "[VERIFY] 缺少公共模块: $commonPath" -ForegroundColor Red
    exit 1
}

. $commonPath

function New-SccVerifyResult {
    param(
        [Parameter(Mandatory)][string]$ProfileName,
        [Parameter(Mandatory)][ValidateSet("PASS","FAIL")][string]$Status,
        [Parameter(Mandatory)][string]$Message
    )

    return [pscustomobject]@{
        Profile = $ProfileName
        Status  = $Status
        Message = $Message
    }
}

function Invoke-SccProfileSmokeTest {
    param(
        [Parameter(Mandatory)][string]$ProfileName,
        [Parameter(Mandatory)][string]$ProfilePath
    )

    if (-not (Test-Path $ProfilePath)) {
        return New-SccVerifyResult -ProfileName $ProfileName -Status "FAIL" -Message "缺少 profile: $ProfilePath"
    }

    try {
        $summary = & {
            param($ProfileUnderTest)

            $ErrorActionPreference = "Stop"
            $InformationPreference = "SilentlyContinue"
            $PROFILE = $ProfileUnderTest

            . $ProfileUnderTest

            $config = Get-SccConfig
            $commandName = Get-SccPrimaryCommandName -Config $config
            $null = Get-Command $commandName -ErrorAction Stop
            $state = Ensure-SccStateFile

            & $commandName help $commandName *> $null
            & $commandName doctor *> $null
            & $commandName doctor fancy *> $null
            & $commandName doctor verbose *> $null

            $statusJson = & $commandName status --json | Out-String
            $status = $statusJson | ConvertFrom-Json
            if ($status.SchemaVersion -ne "2026-04-11") {
                throw "$commandName status --json 未返回 .NET 控制面契约。"
            }

            $doctorJson = & $commandName doctor json | Out-String
            $doctor = $doctorJson | ConvertFrom-Json
            if ($doctor.SchemaVersion -ne "2026-04-11" -or $doctor.Command -ne "doctor" -or [string]::IsNullOrWhiteSpace($doctor.Payload.OverallStatus)) {
                throw "$commandName doctor json 未返回 .NET 控制面 doctor 契约。"
            }
            if ($doctor.Payload.PrimaryCommandName -ne $status.Payload.PrimaryCommand) {
                throw 'doctor json 与 status --json 的主命令名不一致。'
            }

            $enabledModules = Get-SccEnabledModuleNames -State $state
            $verifiedCommands = @()

            foreach ($moduleName in $enabledModules) {
                & $commandName help $moduleName *> $null

                foreach ($entry in Get-SccModuleCommandHelpEntries -ModuleName $moduleName) {
                    if (-not (Get-Command $entry.CommandName -ErrorAction SilentlyContinue)) {
                        throw "命令未加载: $($entry.CommandName)"
                    }

                    & $entry.CommandName help *> $null
                    if ($entry.CommandName -eq "proxy") {
                        $scanJson = & $entry.CommandName scan --json | Out-String
                        $scanReport = $scanJson | ConvertFrom-Json
                        if ($scanReport.SchemaVersion -ne "2026-04-11" -or $scanReport.Command -ne "proxy.scan") {
                            throw "proxy scan --json 未返回 .NET 控制面代理契约。"
                        }
                        & $entry.CommandName bypass add 127.0.0.1 *> $null
                    }
                    $verifiedCommands += $entry.CommandName
                }
            }

            return [pscustomobject]@{
                CommandName      = $commandName
                EnabledModules   = @($enabledModules)
                VerifiedCommands = @($verifiedCommands | Sort-Object -Unique)
            }
        } $ProfilePath

        $moduleText = if ($summary.EnabledModules.Count -gt 0) {
            $summary.EnabledModules -join ", "
        } else {
            "无启用模块"
        }

        $commandText = if ($summary.VerifiedCommands.Count -gt 0) {
            $summary.VerifiedCommands -join ", "
        } else {
            "无子命令"
        }

        return New-SccVerifyResult -ProfileName $ProfileName -Status "PASS" -Message "主命令: $($summary.CommandName) | 模块: $moduleText | 已验证命令: $commandText"
    } catch {
        return New-SccVerifyResult -ProfileName $ProfileName -Status "FAIL" -Message $_.Exception.Message
    }
}

$results = foreach ($profile in Get-SccProfileEntries) {
    Invoke-SccProfileSmokeTest -ProfileName $profile.Name -ProfilePath $profile.Path
}

Write-Host "=== TermForge Verify ===" -ForegroundColor Cyan
foreach ($result in $results) {
    $color = if ($result.Status -eq "PASS") { "Green" } else { "Red" }
    $statusLabel = "[{0}]" -f $result.Status
    Write-Host $statusLabel -ForegroundColor $color -NoNewline
    Write-Host " $($result.Profile) - $($result.Message)"
}

$failCount = @($results | Where-Object { $_.Status -eq "FAIL" }).Count
if ($failCount -gt 0) {
    Write-Host "校验失败: $failCount 个 profile 未通过。" -ForegroundColor Red
    exit 1
}

Write-Host "校验通过: 所有 profile smoke test 均通过。" -ForegroundColor Green
