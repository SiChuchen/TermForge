# TermForge

`TermForge` 是一套面向 Windows shell 的受管终端运行时。它的目标不是替代 `Windows Terminal` 本体，而是把 PowerShell、VS Code PowerShell、CMD/Clink 这一层的启动入口、主题、代理和诊断能力整理成一个可安装、可回滚、可扩展的项目。

默认主命令是 `termforge`，但安装向导允许用户自定义；`wtctl` 始终保留为固定恢复入口。

## 适用场景

- 想把 PowerShell profile 从“个人脚本目录”升级成可安装项目
- 需要统一管理 PowerShell、VS Code PowerShell 和 CMD 的启动体验
- 希望主题、代理、字体和诊断能力都由仓库管理，而不是散落在本机各处
- 需要一个可以安装、回滚、验证并持续演进的 Windows 终端基线

## 当前能力

- 交互式安装器：`install.cmd` / `install.ps1`
- 预检入口：`setup.ps1`
- 卸载与回滚：`uninstall.ps1`
- 受管 profile 注入，不覆盖用户整个 profile
- 动态主命令，默认 `termforge`
- 固定恢复入口 `wtctl`
- `proxy` / `theme` 两个基础模块
- `termforge doctor` 诊断输出与 `verify.ps1` smoke test
- `.NET` 控制面 CLI，当前承接 `status --json`、`doctor --json` 与 env 目标 `proxy scan/plan/apply/rollback`
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
powershell -ExecutionPolicy Bypass -File .\setup.ps1
```

安装完成后，新开的终端会话里可以直接验证：

```powershell
termforge doctor
wtctl doctor
```

## 运行时模型

TermForge 的运行时分成三层：

1. 用户真实 profile
2. 受管 profile 入口
3. `bootstrap.ps1` + `modules/*`

在当前 phase1 中，还额外引入了一层结构化控制面：

4. `.NET CLI` (`src/TermForge.Cli`)

PowerShell 继续保留为安装器、profile 注入和命令入口；`status --json`、`doctor json` 与 env 目标代理工作流则转发到 `.NET` 控制面，输出稳定的 JSON envelope，供 agent 或后续 MCP 封装调用。
`doctor` / `doctor fancy` / `doctor verbose` 仍由 PowerShell 负责渲染。

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

## 安装向导行为

安装脚本会按步骤询问真实需求，而不是默认把所有组件都装上：

`setup.ps1` 现在同时承担“预检入口”和“环境报告入口”：

- `./setup.ps1`：输出预检摘要，并在可继续时进入安装向导
- `./setup.ps1 --json`：输出结构化环境报告，不进入安装向导
- `./setup.ps1 --report`：输出完整文本环境报告，不进入安装向导

安装脚本会按步骤询问真实需求，而不是默认把所有组件都装上：

1. `setup.ps1` 先做环境预检或环境报告
2. 选择安装目录和主命令名
3. 选择要集成的宿主：PowerShell、VS Code PowerShell、CMD
4. 选择是否会在 Windows Terminal 中使用
5. 处理依赖：`oh-my-posh` 为必需项；`Windows Terminal` 只在你选择使用它时才参与安装
6. 配置主题、字体和代理
7. 部署运行时并执行 smoke test

说明：

- 默认主命令是 `termforge`，你可以在安装时换成别的名字
- `wtctl` 不随用户改名，用来保留恢复入口
- 如果你只在 VS Code 里使用，可以关闭 Windows Terminal 和 CMD 相关选项
- 代理默认关闭，只有在你确认需要时才会要求填写地址
- `setup.ps1` 会先检查 Windows 版本、`LOCALAPPDATA` 可写性，以及扩展工具扫描结果：`winget`、`pwsh`、`oh-my-posh`、`wt`、`clink`、`VS Code`、`git`、`npm`、`pnpm`、`yarn`、`pip`、`uv`、`cargo`、`docker`
- `setup.ps1` 的环境报告会显示当前代理环境变量可见性，包括 `HTTP_PROXY` / `HTTPS_PROXY` / `NO_PROXY`
- 如果缺少 `oh-my-posh` 且又无法自动安装，预检会直接阻断，而不是让用户走到一半才失败

## 兼容性结论

如果用户是在一台“几乎没有额外工具”的 Windows 10/11 机器上安装，是否能直接用，取决于两件事：

- 是否至少满足 Windows 10 / 11 基线
- 是否能拿到 `oh-my-posh`

当前策略是：

- 纯净 Windows 11 且带 `winget` 的环境，通常可以直接从 `install.cmd` 开始安装
- Windows 10/11 上如果没有 `winget`，但已经手动装过 `oh-my-posh`，也可以继续
- 如果既没有 `winget`，也没有 `oh-my-posh`，安装器会在预检阶段直接停止，并告诉用户为什么不能继续

这意味着我们不需要维护多套“Windows 10 安装器 / Windows 11 安装器 / VS Code 安装器”，而是采用：

- 一层预检入口：`setup.ps1`
- 一层主安装向导：`install.ps1`

这种结构更容易维护，也更容易兼容不同宿主场景。

## 核心命令

```powershell
termforge list
termforge doctor
termforge doctor fancy
termforge doctor json
termforge status --json
proxy scan --json
proxy plan --mode enable --targets env --http http://127.0.0.1:7890 --https http://127.0.0.1:7890 --no-proxy 127.0.0.1,localhost,::1 --json
proxy apply --plan-id <id> --json
proxy rollback --change-id <id> --json
termforge help proxy
proxy
proxy bypass add 127.0.0.1 localhost host.docker.internal
posh
poshl
posht <theme>
poshs <theme>
```

说明：

- `termforge` 是默认主命令，可在安装时修改
- `wtctl` 始终保留为恢复入口
- `termforge status --json` 现在通过 `.NET` 控制面输出机器可读状态
- `termforge doctor json` 现在通过 `.NET` 控制面输出机器可读诊断结果
- `proxy scan/plan/apply/rollback --json` 当前只支持 `env` 目标
- `proxy bypass add` 会把目标追加到 `proxy.noProxy`
- 当 `proxy.enabled = true` 时，新增绕过项会立即同步到当前会话的 `no_proxy/NO_PROXY`

## 代理配置

代理默认关闭。安装向导只有在你选择启用代理时，才会继续询问以下字段：

- `HTTP` 代理地址，例如 `http://127.0.0.1:7890`
- `HTTPS` 代理地址，留空时复用 `HTTP`
- `NO_PROXY`，默认值是 `127.0.0.1,localhost,::1`

推荐做法：

- 如果你不确定是否需要代理，就保持关闭
- 如果你需要公司代理或本地代理，再在安装时启用
- 本地服务、Docker 桥接地址、局域网直连目标，可以用 `proxy bypass add <host>` 追加到 `NO_PROXY`

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
    "commandName": "termforge"
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
- `setup.ps1`: 环境预检与安装分发入口
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
termforge doctor
termforge doctor fancy
termforge doctor verbose
termforge doctor json
```

说明：

- 仓库根目录运行自检时，如果本地缺少 `scc.config.json` / `module_state.json`，脚本会自动生成
- 这些本地运行时文件，以及 `themes/active.omp.json`、zip 打包产物，都不进入 git

## 当前边界

- 目前还没有进入 Windows Terminal pane/layout 编排层
- 代理模块已经支持 `proxy bypass add`，但还没有完整的交互式代理编辑器
- 安装器当前优先面向 Windows + winget 场景
- 经典 Console Host 不会强推注册表字体配置，当前优先配置 Windows Terminal / VS Code
