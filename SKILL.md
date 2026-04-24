---
name: termforge
description: Install, configure, diagnose, and manage TermForge — a managed Windows shell runtime for PowerShell/CMD profiles.
version: 0.9.0
---

# TermForge Skill for AI Agents

This skill teaches agents how to operate TermForge programmatically via its CLI commands. All commands support `--json` for machine-readable output using a consistent `CommandEnvelope` schema.

## Quick Start for Agents

### Step 0: Discover TermForge State

Before using any commands, an agent MUST determine whether TermForge is installed and find its root:

```bash
# Method 1: Check if the primary command is available
pwsh -NoProfile -Command "Get-Command termforge -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source"

# Method 2: Check fallback command
pwsh -NoProfile -Command "Get-Command wtctl -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source"

# Method 3: Check common install locations
pwsh -NoProfile -Command "Test-Path (Join-Path $env:LOCALAPPDATA 'TermForge\scc.config.json')"
```

If none of these return a result, TermForge is not installed. Proceed to Installation.

### Step 1: Choose Invocation Method

TermForge commands only exist in a PowerShell session that has loaded the TermForge profile. Agents should use one of:

**Option A — Direct profile dot-source (preferred):**
```bash
pwsh -NoProfile -Command ". (Join-Path $env:LOCALAPPDATA 'TermForge\Microsoft.PowerShell_profile.ps1'); termforge list --json"
```

**Option B — Using the installed .cmd launcher:**
```bash
# If install root is in PATH (common after install)
termforge list --json

# If not in PATH, use full path
pwsh -NoProfile -Command "& (Join-Path $env:LOCALAPPDATA 'TermForge\termforge.cmd') list --json"
```

**Option C — Bootstrap + command:**
```bash
pwsh -NoProfile -Command ". (Join-Path $env:LOCALAPPDATA 'TermForge\bootstrap.ps1'); termforge list --json"
```

**Rule:** Always use `pwsh -NoProfile` to avoid loading the user's full profile (which may be slow or have side effects), then dot-source only the TermForge entry point.

---

## Command Envelope Schema

Every `--json` response follows this structure:

```json
{
  "SchemaVersion": "2026-04-11",
  "Command": "<command-name>",
  "Status": "PASS|WARN|FAIL",
  "GeneratedAt": "2026-04-16T12:00:00Z",
  "Warnings": [],
  "Errors": [],
  "Payload": { ... }
}
```

- `Status`: `PASS` = success, `WARN` = success with caveats, `FAIL` = error
- `Errors`: non-empty array when `Status` is `FAIL`
- `Payload`: command-specific data

**Exit codes**: 0 = success, non-zero = error

---

## Installation

Run from the TermForge source directory.

```bash
# Non-interactive install with sensible defaults (recommended for agents)
pwsh -NoProfile -File install.ps1 -NonInteractive `
    -ManagePowerShellProfile `
    -AddToPath `
    -SkipVerification

# Non-interactive with all options
pwsh -NoProfile -File install.ps1 -NonInteractive `
    -InstallRoot "C:\Users\$env:USERNAME\AppData\Local\TermForge" `
    -CommandName "termforge" `
    -AddToPath `
    -ManagePowerShellProfile `
    -ManageVsCodeProfile `
    -ThemeName "termforge" `
    -ConfigureFonts `
    -FontFace "MesloLGM Nerd Font" `
    -FontSize 12

# With proxy
pwsh -NoProfile -File install.ps1 -NonInteractive `
    -ManagePowerShellProfile -AddToPath `
    -ConfigureProxy `
    -HttpProxy "http://proxy.corp:8080" `
    -HttpsProxy "http://proxy.corp:8443"

# Minimal install (skip dependency auto-install, skip verification)
pwsh -NoProfile -File install.ps1 -NonInteractive `
    -ManagePowerShellProfile `
    -SkipDependencyInstall -SkipVerification
```

Non-interactive output is a `CommandEnvelope` JSON with `Command: "install"` and `Payload` containing the install summary. Check `Status` for success.

**All install parameters:**

| Parameter                | Type     | Default                          | Description                              |
|--------------------------|----------|----------------------------------|------------------------------------------|
| `-InstallRoot`           | string   | `$env:LOCALAPPDATA\TermForge`    | Installation target directory            |
| `-CommandName`           | string   | `termforge`                      | Primary CLI command name                 |
| `-AddToPath`             | switch   | false                            | Add install root to user PATH            |
| `-ManagePowerShellProfile` | switch | false                            | Inject into PowerShell profile           |
| `-ManageVsCodeProfile`   | switch   | false                            | Inject into VS Code PowerShell profile   |
| `-EnableCmdHost`         | switch   | false                            | Enable CMD host with Clink               |
| `-ConfigureProxy`        | switch   | false                            | Enable proxy configuration               |
| `-HttpProxy`             | string   | `""`                             | HTTP proxy URL                           |
| `-HttpsProxy`            | string   | `""`                             | HTTPS proxy URL (falls back to HTTP)     |
| `-NoProxy`               | string   | `127.0.0.1,localhost,::1`        | Comma-separated bypass hosts             |
| `-ThemeName`             | string   | `termforge`                      | Default Oh My Posh theme name            |
| `-FontFace`              | string   | `MesloLGM Nerd Font`             | Nerd Font face name                      |
| `-FontSize`              | int      | `12`                             | Font size                                |
| `-ConfigureFonts`        | switch   | false                            | Auto-install Nerd Font                   |
| `-SkipDependencyInstall` | switch   | false                            | Don't auto-install missing tools         |
| `-SkipVerification`      | switch   | false                            | Skip post-install smoke test             |
| `-NonInteractive`        | switch   | false                            | Use all defaults / provided params       |

---

## Runtime Commands

All examples assume TermForge is loaded. Replace the invocation wrapper per Step 1 if calling from outside a TermForge session.

### Check Module Status

```bash
# JSON — agent should use this
termforge list --json
```

Payload: array of module objects:
```json
[
  { "Name": "proxy", "Exists": true, "Enabled": true, "Status": "enabled" },
  { "Name": "theme", "Exists": true, "Enabled": true, "Status": "enabled" }
]
```

### System Diagnostics

```bash
termforge doctor --json
```

Returns overall health. Check `Payload.OverallStatus` (PASS/WARN/FAIL) and `Payload.Results` for individual checks.

### Enable / Disable Modules

```bash
termforge enable proxy --json
termforge disable theme --json
```

Payload: `{ "Module": "proxy", "Enabled": true }`

After enable/disable, reload: `termforge reload --json`

### Self-Update

```bash
termforge update --json
```

Payload on success:
```json
{
  "PreviousVersion": "0.8.0",
  "NewVersion": "0.9.0",
  "UpdatedFiles": ["bootstrap.ps1", "modules/", "src/", ...]
}
```

If already up to date: `UpdatedFiles` is empty, `Status` is still `PASS`.

### Reload Profile

```bash
termforge reload --json
```

---

## Proxy Module

Manages HTTP/HTTPS proxy across multiple targets.

```bash
proxy scan --json                              # scan all targets
proxy scan --targets env --json                # scan specific target
proxy plan --mode enable --targets env --http http://proxy:8080 --json
proxy plan --mode enable --targets composite --http http://proxy:8080 --json
proxy apply --plan-id <id> --json              # apply planned change
proxy rollback --change-id <id> --json         # rollback applied change
proxy bypass add 192.168.1.0/24 internal.corp  # add NO_PROXY entries
```

### Proxy Targets

| Target      | Scope                                           |
|-------------|-------------------------------------------------|
| `env`       | Process environment variables + scc.config.json |
| `git`       | `git config --global` (http.proxy, https.proxy) |
| `npm`       | `.npmrc` file                                   |
| `pip`       | `pip.ini` file                                  |
| `composite` | All targets with compensation rollback          |

---

## Theme Module

```bash
posh          # view current theme
poshl         # list installed themes
posht <name>  # preview (current session only)
poshs <name>  # save permanently
```

---

## Agent Workflow Recipes

### Recipe 1: Fresh Install + Verify
```
1. pwsh -NoProfile -File install.ps1 -NonInteractive -ManagePowerShellProfile -AddToPath -SkipVerification
2. Parse JSON → check Status == "PASS"
3. pwsh -NoProfile -Command ". (Join-Path $env:LOCALAPPDATA 'TermForge\Microsoft.PowerShell_profile.ps1'); termforge doctor --json"
4. Parse JSON → check Payload.OverallStatus != "FAIL"
```

### Recipe 2: Check and Update
```
1. pwsh -NoProfile -Command ". (Join-Path $env:LOCALAPPDATA 'TermForge\Microsoft.PowerShell_profile.ps1'); termforge update --json"
2. Parse JSON → if UpdatedFiles is non-empty, update happened
3. If updated: termforge reload --json
```

### Recipe 3: Configure Proxy
```
1. proxy scan --json → read current state
2. proxy plan --mode enable --targets env --http <url> --json → get plan-id
3. proxy apply --plan-id <id> --json → apply
4. On failure: proxy rollback --change-id <id> --json
```

### Recipe 4: Diagnose Broken Install
```
1. termforge doctor --json
2. Check Payload.OverallStatus
3. Iterate Payload.Results → find entries with Status != "PASS"
4. Fix issues based on Name and Message fields
5. Re-run doctor to confirm
```

---

## Configuration Files

| File                     | Purpose                          | Preserved on update |
|--------------------------|----------------------------------|--------------------|
| `scc.config.json`        | Runtime config                   | Yes (version updated) |
| `module_state.json`      | Module enable/disable flags      | Yes                |
| `state/`                 | Plan store, operation ledger     | Yes                |
| `themes/active.omp.json` | Active theme override            | Yes                |

## Important Notes

- PowerShell 5.1 (`powershell.exe`) and PowerShell 7 (`pwsh`) are both supported
- `wtctl` always exists as a recovery fallback if the primary command is broken
- `oh-my-posh` is required; installer auto-installs via winget
- Proxy defaults to **off**; `noProxy` always includes `127.0.0.1,localhost,::1`
- Human-readable messages are in Chinese; JSON keys are in English
- `.NET CLI` handles `status --json` and `doctor --json`; all other `--json` is native PowerShell

## Hard Requirements & Limitations

An agent **MUST** verify these conditions before attempting to use TermForge commands. If any condition fails, the agent should report the limitation to the user and not attempt to proceed.

| Requirement | Why | Fallback |
|---|---|---|
| **Windows 10 or later** | TermForge only supports Windows 10/11 | None — Linux/macOS are not supported |
| **PowerShell available** (`powershell.exe` or `pwsh`) | All TermForge commands run inside PowerShell | None — PowerShell is the runtime |
| **Agent can invoke `pwsh -NoProfile -Command "..."`** | Commands only exist in a PowerShell session with TermForge loaded | Use `powershell.exe` if `pwsh` is unavailable |
| **TermForge installed** (for runtime commands) | `termforge`/`wtctl` only exist after install | Run `install.ps1 -NonInteractive` first |
| **`oh-my-posh` installed** (required dependency) | Theme engine is mandatory for TermForge to function | Installer auto-installs via winget; use `-SkipDependencyInstall` only if pre-installed |

**Key limitation:** TermForge commands are PowerShell functions, not standalone executables. They only exist after `bootstrap.ps1` loads into the session. An agent **cannot** simply run `termforge list --json` in a bare shell — it must first load the TermForge profile entry point. See "Step 1: Choose Invocation Method" above for the correct calling pattern.
