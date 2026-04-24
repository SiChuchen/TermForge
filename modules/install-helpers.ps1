# Shared helpers for install.ps1 and uninstall.ps1.
# This file is intentionally self-contained — it does NOT depend on
# bootstrap.ps1 or $script:SccRoot, so it can be dot-sourced during
# fresh install when the TermForge runtime is not yet deployed.

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

function Remove-SccManagedBlock {
    param([string]$Content)

    if ([string]::IsNullOrWhiteSpace($Content)) {
        return ""
    }

    $result = $Content
    foreach ($marker in $script:ManagedBlockMarkers) {
        $escapedStart = [regex]::Escape($marker.Start)
        $escapedEnd = [regex]::Escape($marker.End)
        $pattern = "(?ms)^$escapedStart.*?^$escapedEnd\r?\n?"
        $result = [regex]::Replace($result, $pattern, "")
    }
    return $result.TrimEnd("`r", "`n")
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
