$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot 'modules\common.ps1')

Describe 'shared environment facts' {
    It 'returns a combined environment facts object with host, tools, proxy, and install host' {
        $facts = Get-SccEnvironmentFacts

        $facts.Host | Should BeOfType psobject
        $facts.Tools.Count | Should BeGreaterThan 0
        $facts.ProxyEnvironment.Enabled | Should BeOfType bool
        $facts.InstallHost.IsAvailable | Should BeOfType bool
    }

    It 'does not recommend full install mode when the install host is unavailable' {
        $environmentFacts = [pscustomobject][ordered]@{
            Host = [pscustomobject][ordered]@{
                IsWindows = $true
                OsVersion = '10.0.22631'
                PowerShellEdition = 'Core'
                PowerShellVersion = '7.5.0'
                LocalAppData = 'C:\Users\Test\AppData\Local'
                DocumentsPath = 'C:\Users\Test\Documents'
                CanWriteLocalAppData = $true
            }
            Tools = @(
                [pscustomobject][ordered]@{ Name = 'winget'; Detected = $true; CommandPath = 'C:\Program Files\WindowsApps\winget.exe'; Required = $false; CanAutoInstall = $false; Status = 'PASS'; Message = 'winget.exe' }
                [pscustomobject][ordered]@{ Name = 'oh-my-posh'; Detected = $true; CommandPath = 'C:\Tools\oh-my-posh.exe'; Required = $true; CanAutoInstall = $false; Status = 'PASS'; Message = 'oh-my-posh.exe' }
                [pscustomobject][ordered]@{ Name = 'wt'; Detected = $true; CommandPath = 'C:\Tools\wt.exe'; Required = $false; CanAutoInstall = $false; Status = 'PASS'; Message = 'wt.exe' }
                [pscustomobject][ordered]@{ Name = 'clink'; Detected = $true; CommandPath = 'C:\Tools\clink.exe'; Required = $false; CanAutoInstall = $false; Status = 'PASS'; Message = 'clink.exe' }
            )
            ProxyEnvironment = [pscustomobject][ordered]@{
                Enabled = $false
                HttpProxy = ''
                HttpsProxy = ''
                NoProxy = ''
                Source = 'none'
                Status = 'PASS'
            }
            InstallHost = [pscustomobject][ordered]@{
                IsAvailable = $false
                ExecutablePath = $null
                HostKind = 'unknown'
                Status = 'FAIL'
                Message = 'host unavailable'
            }
        }

        $report = Get-SccSetupEnvironmentReport -EnvironmentFacts $environmentFacts

        $report.InstallReadiness.CanContinue | Should Be $false
        $report.InstallReadiness.RecommendedInstallMode | Should Not Be 'full'
    }

    It 'does not throw when Windows PowerShell environment variables are absent' {
        $originalSystemRoot = $env:SystemRoot
        $originalWindir = $env:WINDIR

        try {
            $env:SystemRoot = ''
            $env:WINDIR = ''

            $threw = $false
            try {
                $null = Get-SccSetupWindowsPowerShellPath
            } catch {
                $threw = $true
            }

            $threw | Should Be $false
        } finally {
            $env:SystemRoot = $originalSystemRoot
            $env:WINDIR = $originalWindir
        }
    }

    It 'checks writability without creating directories or probe files' {
        $path = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())

        Mock New-Item { throw 'should not create directories' }
        Mock Set-Content { throw 'should not write probe files' }
        Mock Remove-Item { throw 'should not remove probe files' }

        try {
            $result = Test-SccWritablePath -Path $path

            $result | Should Be $false
            Test-Path $path | Should Be $false
            Assert-MockCalled New-Item -Times 0 -Exactly
            Assert-MockCalled Set-Content -Times 0 -Exactly
            Assert-MockCalled Remove-Item -Times 0 -Exactly
        } finally {
            if (Test-Path $path) {
                Remove-Item -Path $path -Recurse -Force
            }
        }
    }

    It 'returns true for an existing writable temp directory under the current user context' {
        $path = [System.IO.Path]::GetTempPath()

        Test-Path $path | Should Be $true
        Test-SccWritablePath -Path $path | Should Be $true
    }
}
