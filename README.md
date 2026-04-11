# TermForge

`TermForge` 是一套面向 Windows shell 的受管终端运行时。它的目标不是替代 `Windows Terminal` 本体，而是把 PowerShell、VS Code PowerShell、CMD/Clink 这一层的启动入口、主题、代理和诊断能力整理成一个可安装、可回滚、可扩展的项目。

默认管理 CLI 仍然叫 `scc`，并保留 `wtctl` 作为固定恢复入口。

## 适用场景

- 想把 PowerShell profile 从“个人脚本目录”升级成可安装项目
- 需要统一管理 PowerShell、VS Code PowerShell 和 CMD 的启动体验
- 希望主题、代理、字体和诊断能力都由仓库管理，而不是散落在本机各处
- 需要一个可以安装、回滚、验证并持续演进的 Windows 终端基线

## 当前能力

- 交互式安装器：`install.cmd` / `install.ps1`
- 卸载与回滚：`uninstall.ps1`
- 受管 profile 注入，不覆盖用户整个 profile
- 动态主命令，默认 `scc`
- 固定恢复入口 `wtctl`
- `proxy` / `theme` 两个基础模块
- `scc doctor` 诊断输出与 `verify.ps1` smoke test
- CMD + Clink + Oh My Posh 集成
- Nerd Font 安装与 Windows Terminal / VS Code 字体写入
- 本地回环默认代理绕过：`127.0.0.1,localhost,::1`

## Quick Start

在仓库根目录执行：

```powershell
.\install.cmd
```

或：

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

安装完成后，新开的终端会话里可以直接验证：

```powershell
scc doctor
wtctl doctor
```

## 运行时模型

TermForge 的运行时分成三层：

1. 用户真实 profile
2. 受管 profile 入口
3. `bootstrap.ps1` + `modules/*`

这样做的目的很明确：

- 安装与卸载不需要覆盖整份用户 profile
- profile 注入可以回滚
- 模块、帮助、配置和诊断都能统一由仓库维护
- `verify.ps1` 可以在干净上下文里稳定复测

默认安装目录是：

```text
%LOCALAPPDATA%\TermForge
```

如果本机已经存在旧版 `windows-terminal` 安装目录，安装器和卸载器会优先兼容已有路径与旧的受管 block 标记，避免升级时留下重复注入。

## 核心命令

```powershell
scc list
scc doctor
scc doctor fancy
scc doctor json
scc help proxy
proxy
proxy bypass add 127.0.0.1 localhost host.docker.internal
posh
poshl
posht <theme>
poshs <theme>
```

说明：

- `scc` 是默认主命令，可在安装时修改
- `wtctl` 始终保留为恢复入口
- `proxy bypass add` 会把目标追加到 `proxy.noProxy`
- 当 `proxy.enabled = true` 时，新增绕过项会立即同步到当前会话的 `no_proxy/NO_PROXY`

## 配置模型

运行时配置文件是 `scc.config.json`。安装后典型结构如下：

```json
{
  "install": {
    "root": "C:\\Users\\you\\AppData\\Local\\TermForge",
    "addToPath": true,
    "managedProfiles": {
      "powershell": "C:\\Users\\you\\Documents\\PowerShell\\Microsoft.PowerShell_profile.ps1",
      "vscode": "C:\\Users\\you\\Documents\\PowerShell\\Microsoft.VSCode_profile.ps1",
      "cmd": "clink"
    }
  },
  "cli": {
    "commandName": "scc"
  },
  "cmd": {
    "enabled": true,
    "clinkPath": "C:\\Users\\you\\AppData\\Local\\Programs\\clink\\clink.exe",
    "scriptsPath": "C:\\Users\\you\\AppData\\Local\\TermForge\\clink"
  },
  "font": {
    "face": "MesloLGM Nerd Font",
    "size": 12
  },
  "proxy": {
    "enabled": false,
    "http": "",
    "https": "",
    "noProxy": "127.0.0.1,localhost,::1"
  },
  "theme": {
    "enabled": true,
    "themeDir": "C:\\Users\\you\\AppData\\Local\\TermForge\\themes",
    "defaultTheme": "termforge",
    "activeTheme": "termforge",
    "commandPath": ""
  }
}
```

## 仓库结构

- `install.ps1` / `install.cmd`: 交互式安装入口
- `uninstall.ps1`: 卸载与回滚
- `launcher.ps1`: `scc.cmd` / `wtctl.cmd` 的统一入口
- `bootstrap.ps1`: 统一启动入口
- `Microsoft.PowerShell_profile.ps1`: 受管 PowerShell profile 入口
- `Microsoft.VSCode_profile.ps1`: 受管 VS Code PowerShell profile 入口
- `modules/common.ps1`: 配置、JSON、诊断、通用函数
- `modules/manager.ps1`: 主命令绑定与模块管理
- `modules/proxy.ps1`: 代理模块
- `modules/theme.ps1`: Oh My Posh 主题模块
- `themes/termforge.omp.json`: 内置默认主题
- `verify.ps1`: 仓库级 smoke test

## 验证

开发态验证：

```powershell
pwsh -NoProfile -File .\verify.ps1
```

运行态验证：

```powershell
scc doctor
scc doctor fancy
scc doctor verbose
scc doctor json
```

说明：

- 仓库根目录运行自检时，如果本地缺少 `scc.config.json` / `module_state.json`，脚本会自动生成
- 这些本地运行时文件，以及 `themes/active.omp.json`、zip 打包产物，都不进入 git

## 当前边界

- 目前还没有进入 Windows Terminal pane/layout 编排层
- 代理模块已经支持 `proxy bypass add`，但还没有完整的交互式代理编辑器
- 安装器当前优先面向 Windows + winget 场景
- 经典 Console Host 不会强推注册表字体配置，当前优先配置 Windows Terminal / VS Code
