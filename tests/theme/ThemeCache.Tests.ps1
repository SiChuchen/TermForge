$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot 'modules\theme.ps1')

try {
    Describe 'theme init cache' {
        It 'rejects empty init cache files' {
            $cacheFile = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
            Set-Content -Path $cacheFile -Value '' -Encoding UTF8

            try {
                Test-SccInitCacheUsable -CachePath $cacheFile | Should Be $false
            } finally {
                Remove-Item -Path $cacheFile -Force -ErrorAction SilentlyContinue
            }
        }

        It 'accepts init cache files that invoke the oh-my-posh init script' {
            $cacheFile = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
            Set-Content -Path $cacheFile -Value '$env:POSH_SESSION_ID = "test";& ''C:\Users\Test\.cache\oh-my-posh\init.123.ps1''' -Encoding UTF8

            try {
                Test-SccInitCacheUsable -CachePath $cacheFile | Should Be $true
            } finally {
                Remove-Item -Path $cacheFile -Force -ErrorAction SilentlyContinue
            }
        }

        It 'rejects stripped init cache files without a session id' {
            $cacheFile = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
            Set-Content -Path $cacheFile -Value '& ''C:\Users\Test\.cache\oh-my-posh\init.123.ps1''' -Encoding UTF8

            try {
                Test-SccInitCacheUsable -CachePath $cacheFile | Should Be $false
            } finally {
                Remove-Item -Path $cacheFile -Force -ErrorAction SilentlyContinue
            }
        }

        It 'uses the init cache only when the fingerprint matches' {
            $testDir = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
            New-Item -Path $testDir -ItemType Directory -Force | Out-Null
            $cacheFile = Join-Path $testDir '.init-cache.ps1'
            $fingerprintFile = Join-Path $testDir '.init-cache.fp'

            try {
                Set-Content -Path $cacheFile -Value '$env:POSH_SESSION_ID = "test";& ''C:\Users\Test\.cache\oh-my-posh\init.123.ps1''' -Encoding UTF8
                Set-Content -Path $fingerprintFile -Value 'theme|omp' -Encoding UTF8

                Test-SccInitCacheHit -CachePath $cacheFile -FingerprintPath $fingerprintFile -Fingerprint 'theme|omp' | Should Be $true
                Test-SccInitCacheHit -CachePath $cacheFile -FingerprintPath $fingerprintFile -Fingerprint 'theme|new-omp' | Should Be $false
            } finally {
                Remove-Item -Path $testDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'restores the matching oh-my-posh session cache before cache init reuse' {
            $testDir = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
            New-Item -Path $testDir -ItemType Directory -Force | Out-Null
            $backupFile = Join-Path $testDir '.init-cache.omp.cache'
            Set-Content -Path $backupFile -Value 'atomic-session-cache' -Encoding UTF8
            $content = '$env:POSH_SESSION_ID = "restore-test";& ''C:\Users\Test\.cache\oh-my-posh\init.123.ps1'''
            $sessionCache = Get-SccOhMyPoshSessionCachePath -SessionId 'restore-test'

            try {
                Set-Content -Path $sessionCache -Value 'stale-session-cache' -Encoding UTF8
                Restore-SccInitSessionCache -InitContent $content -BackupPath $backupFile | Should Be $true
                (Get-Content -Path $sessionCache -Raw).Trim() | Should Be 'atomic-session-cache'
            } finally {
                Remove-Item -Path $testDir -Recurse -Force -ErrorAction SilentlyContinue
                Remove-Item -Path $sessionCache -Force -ErrorAction SilentlyContinue
            }
        }

        It 'reports cache write failure without throwing' {
            $testDir = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
            New-Item -Path $testDir -ItemType Directory -Force | Out-Null
            $cacheFile = Join-Path $testDir '.init-cache.ps1'
            $fingerprintFile = Join-Path $testDir '.init-cache.fp'
            Set-Content -Path $cacheFile -Value 'locked' -Encoding UTF8
            $lock = [System.IO.File]::Open($cacheFile, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)

            try {
                Set-SccInitCacheContent -CachePath $cacheFile -FingerprintPath $fingerprintFile -Content '$env:POSH_SESSION_ID = "test";& ''C:\Users\Test\.cache\oh-my-posh\init.123.ps1''' -Fingerprint 'theme|omp' | Should Be $false
            } finally {
                $lock.Dispose()
                Remove-Item -Path $testDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
} finally {
    Remove-Item -LiteralPath (Join-Path $repoRoot 'themes\.init-cache.ps1'),(Join-Path $repoRoot 'themes\.init-cache.fp') -Force -ErrorAction SilentlyContinue
}
