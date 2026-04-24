# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Agent Usage

If you are an AI agent performing tasks for the user (not editing this codebase), see **SKILL.md** for the complete agent-facing command reference. It covers installation, runtime commands (`--json` output), proxy workflow, diagnostics, and invocation patterns.

## Project Overview

TermForge is a managed Windows shell runtime that turns PowerShell/CMD profile management into an installable, rollbackable project. It ships as a dual-stack system: **PowerShell** handles Windows host integration, installation, profile injection, and human-readable commands; **.NET** provides a portable control core for machine-readable JSON contracts (`status --json`, `doctor json`, `proxy scan/plan/apply/rollback --json`).

The .NET proxy workflow supports both standalone **env** and **git** targets, as well as **composite** (`env+git`) orchestration with compensation rollback on partial failure.

## Build & Test Commands

### .NET (dotnet CLI)

```bash
dotnet build TermForge.sln                          # Build everything
dotnet test TermForge.sln                           # Run all xunit tests (30 tests)
dotnet test tests/TermForge.Core.Tests              # Run core tests only
dotnet run --project src/TermForge.Cli -- status --json   # Run CLI directly
```

Target framework: **net8.0** (set in `Directory.Build.props`).

### PowerShell

```powershell
pwsh -NoProfile -File .\verify.ps1                  # Smoke test (dev-mode)
pwsh -NoProfile -File .\tests\run-contracts.ps1     # Contract tests
pwsh -NoProfile -File .\tests\setup\run-setup-tests.ps1  # Setup report tests
```

PowerShell tests live in `tests/contracts/*.Tests.ps1` and `tests/setup/*.Tests.ps1`. Each file is self-contained and runnable with `pwsh -NoProfile -File <path>`.

## Architecture

### Runtime Model (3 layers)

1. **User's real profile** — contains only a tagged injection block that dot-sources the managed profile
2. **Managed profile entry** — `Microsoft.PowerShell_profile.ps1` / `Microsoft.VSCode_profile.ps1`, forwards to `bootstrap.ps1`
3. **bootstrap.ps1 + modules/** — loads `common.ps1`, `manager.ps1`, then enabled modules from `module_state.json`

This separation means install/uninstall only touches the injection block, never the user's full profile.

### PowerShell Module System

- `modules/common.ps1` — config I/O, JSON file store with mutex locking and corruption recovery, help registry, diagnostics (`doctor`), command envelope factory (`New-SccCommandEnvelope`), shared environment facts
- `modules/manager.ps1` — binds the configurable primary command (default: `termforge`) and fallback (`wtctl`) to `Invoke-SccManagerCommand`
- `modules/proxy.ps1` — proxy module, bridges `scan/plan/apply/rollback --json` to .NET CLI
- `modules/theme.ps1` — Oh My Posh theme management
- New modules go in `modules/`, dot-source `common.ps1`, register help via `Register-SccHelp` / `Register-SccCommandHelp`
- Module enable/disable state lives in `module_state.json`; `proxy` and `theme` are enabled by default

### .NET Solution Structure

```
src/TermForge.Contracts/   — Pure record types + ContractSchema version
  CommandEnvelope.cs        — Generic JSON envelope (Command, Status, Warnings, Errors, Payload)
  ProxyContracts.cs         — ProxyConfigSnapshot, ProxyPlanPayload, ProxyApplyPayload, etc.
  GitProxyContracts.cs      — GitProxySnapshot, GitProxyPlan, GitProxyPlanAction
  CompositeProxyContracts.cs — CompositeProxyPlan, CompositeTargetPlan, CompositeProxyChange
  UnifiedStoreContracts.cs  — PlanRecord, ChangeRecord (polymorphic store records)
  EnvironmentFactsContracts.cs — Shared environment fact models (host, tools, proxy, install)
  DoctorContracts.cs        — Doctor report structure
  StatusContracts.cs        — Status payload structure

src/TermForge.Core/        — Service logic + interfaces
  Interfaces/               — IClock, IConfigStore, IPlanStore, IOperationLedger
  Services/StatusService.cs
  Services/DoctorService.cs
  Services/ProxyWorkflowService.cs  — Handles env, git, and composite proxy workflows

src/TermForge.Platform/    — Platform abstractions
  IPlatformEnvironmentAdapter.cs
  IGitProxyAdapter.cs       — 6-method adapter: IsAvailable, ReadCurrent, Plan*, Apply, Verify, Rollback

src/TermForge.Platform.Windows/ — Windows implementations
  JsonConfigStore.cs        — File-backed config with schema validation and auto-defaults
  JsonPlanStore.cs          — Unified plan store (PlanRecord)
  JsonOperationLedger.cs    — Unified change ledger (ChangeRecord)
  WindowsEnvironmentAdapter.cs
  WindowsGitProxyAdapter.cs — Real git.exe integration (config --global)

src/TermForge.Cli/         — CLI entry point + CommandDispatcher
  Program.cs                — Top-level statement, JSON serialization, error envelope
  CommandDispatcher.cs      — Routes args to services; bridges PowerShell for shared environment facts
  AppPaths.cs               — Repo root discovery (walks up to TermForge.sln or setup.ps1)

tests/TermForge.Core.Tests/ — xunit tests (30 total)
  StatusServiceTests.cs, DoctorServiceTests.cs
  ProxyWorkflowServiceTests.cs, GitProxyAdapterTests.cs, UnifiedStoreTests.cs
```

**Dependency flow:** `Cli → Core + Platform.Windows`, `Core → Contracts`, `Platform.Windows → Core + Platform`, `Platform → Contracts`. Tests only reference `Core`.

### Proxy Workflow Architecture

`ProxyWorkflowService` handles three target types through a unified plan/apply/rollback model:

- **env** — reads/writes process environment variables + `scc.config.json` via `IPlatformEnvironmentAdapter`
- **git** — reads/writes `git config --global` keys (`http.proxy`, `https.proxy`, `http.noProxy`) via `IGitProxyAdapter`
- **composite** — orchestrates env+git in apply order [`env`, `git`] with compensation rollback on partial failure

Plans and changes are stored as polymorphic `PlanRecord` / `ChangeRecord` with `PayloadType` discriminators (`proxy-plan`, `git-proxy-plan`, `composite-proxy-plan`, etc.). The unified store uses `UnifiedStoreValueReader` to deserialize typed payloads from `JsonElement`.

### Bridge between PowerShell and .NET

`modules/common.ps1` defines `Invoke-SccDotNetCli` which calls `dotnet run --project src/TermForge.Cli` with forwarded arguments. PowerShell delegates `status --json`, `doctor json`, and proxy `--json` commands to the .NET CLI while keeping human-readable modes (`doctor default/fancy/verbose`) in PowerShell.

The CLI itself bridges back to PowerShell for shared environment facts — `CommandDispatcher` spawns a pwsh process to collect host facts, tool detections, and proxy state from PowerShell's `Get-SccEnvironmentFacts`.

### Configuration

- `scc.config.json` — runtime config (install root, command name, proxy, theme, font, CMD settings)
- `module_state.json` — module enable/disable flags
- `state/` — runtime persistence (unified plan store, operation ledger)
- All three are auto-created with defaults if missing; gitignored

### JSON Contract Schema

Both PowerShell and .NET produce `CommandEnvelope<T>` output. The envelope has: `Command`, `Status` (PASS/WARN/FAIL), `GeneratedAt`, `Warnings`, `Errors`, `Payload`. Contract schema version is `"2026-04-11"` in `CommandEnvelope` and `"2026-04-13"` in unified store records.

### Key Design Decisions

- Proxy defaults to **off**; `noProxy` always includes `127.0.0.1,localhost,::1`
- Primary command is configurable at install time; `wtctl` is always kept as a recovery fallback
- JSON file writes use mutex-locked atomic writes with corruption recovery
- The .NET side only supports `--json` output — human-readable rendering stays in PowerShell
- Composite proxy apply uses ordered execution with per-target compensation rollback on failure
- Git adapter only manages `http.proxy`, `https.proxy`, `http.noProxy` — rejects unknown keys
- `PlanRecord`/`ChangeRecord` are polymorphic; `PayloadType` field determines deserialization target

## Conventions

- Internal function names use the `Scc` prefix (historical) — e.g., `Get-SccConfig`, `Invoke-SccDotNetCli`
- Config keys use the same names across PowerShell and .NET (`proxy.enabled`, `proxy.http`, etc.)
- Module files in `modules/` are dot-sourced; `common.ps1` and `manager.ps1` are infrastructure, not business modules
- The project uses Chinese for user-facing messages in PowerShell scripts
- `oh-my-posh` is a required dependency; `Windows Terminal`, `Clink`, and `VS Code` are optional based on user scenario
- Git proxy adapter test harness uses reflection to instantiate `WindowsGitProxyAdapter` with fake delegates (avoids test project referencing `Platform.Windows` directly)
