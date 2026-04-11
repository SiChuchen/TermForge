# DESIGN

## 目标

`TermForge` 的第一阶段目标不是做 pane/layout 编排，而是先把运行时基础设施项目化：

- 支持交互式安装
- 支持受管 profile 注入与回滚
- 支持 cmd-first 用户从 `install.cmd` 一键进入安装流程
- 支持可配置主命令
- 支持默认可用的主题体验
- 支持字体自动安装与宿主终端字体写入
- 避免代理这类有副作用的配置默认生效

## 运行时模型

运行时由三层组成：

1. 用户真实 profile
2. 受管 profile 入口
3. `bootstrap.ps1` + `modules/*`

用户真实 profile 只包含一段带标记的注入 block，用于 dot-source 受管目录中的 profile 入口。受管 profile 入口再统一转发到 `bootstrap.ps1`。

这样做的原因：

- 安装和卸载不需要覆盖用户整个 profile
- 仓库内可以保留稳定的受管 profile 模板
- `verify.ps1` 可以直接对受管 profile 做稳定 smoke test，而不受用户自定义 profile 污染

CMD 宿主不走 PowerShell profile 注入，而是通过：

1. `install.cmd`
2. 生成的 `<主命令>.cmd` / `wtctl.cmd` 包装器
3. `launcher.ps1`
4. Clink script 注入

来完成命令入口和提示符初始化。

## 安装入口模型

安装流程分成两层：

1. `setup.ps1`
2. `install.ps1`

其中：

- `setup.ps1` 负责环境预检、阻塞项判断和安装分发
- `install.ps1` 负责真正的交互式安装流程
- `install.cmd` 只是双击友好的外壳，默认调用 `setup.ps1`

这样设计的原因：

- 不需要维护多套几乎重复的安装脚本
- 可以在“纯净 Windows 10 / 11”环境里先明确告诉用户缺什么
- 可以把“阻塞项”和“可选项”分开处理，避免用户走到中途才失败

## 控制面模型

当前 phase1 额外引入 `.NET` 控制核心：

1. `TermForge.Contracts`
2. `TermForge.Core`
3. `TermForge.Platform`
4. `TermForge.Platform.Windows`
5. `TermForge.Cli`

职责边界：

- PowerShell 继续负责安装、profile 注入、模块加载和交互式入口
- `.NET` 负责结构化 `status` 输出和 env 目标代理工作流
- PowerShell 通过桥接把 `status --json` 与 `proxy scan/plan/apply/rollback --json` 转发到 `.NET CLI`

这样可以在不重写安装器和运行时入口的前提下，先把 agent 需要的机器可读契约稳定下来。

## 主命令模型

内部管理函数固定为 `Invoke-SccManagerCommand`，外部暴露的主命令来自 `cli.commandName`。

当前策略：

- 默认主命令为 `termforge`
- 固定保留 `wtctl` 作为恢复入口
- 启动时动态把主命令和 `wtctl` 绑定到同一套管理函数

保留恢复入口的原因是避免用户把主命令改坏后失去修复路径。

## 配置模型

配置文件是 `scc.config.json`。

当前关键字段：

- `install.root`
- `install.addToPath`
- `install.managedProfiles.powershell`
- `install.managedProfiles.vscode`
- `install.managedProfiles.cmd`
- `cli.commandName`
- `cmd.enabled`
- `cmd.clinkPath`
- `cmd.scriptsPath`
- `font.face`
- `font.size`
- `proxy.enabled`
- `proxy.http`
- `proxy.https`
- `proxy.noProxy`
- `theme.enabled`
- `theme.themeDir`
- `theme.defaultTheme`
- `theme.activeTheme`
- `theme.commandPath`

设计原则：

- 默认主命令应该可自定义，但默认值必须稳定且有产品语义，因此选 `termforge`
- 代理默认关闭
- 代理的默认绕过列表至少应包含 `127.0.0.1,localhost,::1`，避免本地回环流量被错误送进代理
- 主题默认开启
- 默认主题必须随仓库提供，不能依赖首次启动在线下载
- 默认字体配置必须能自动写入支持的宿主
- 所有用户可见入口都由配置驱动，而不是硬编码在脚本里

## 模块默认行为

当前模块状态文件默认只启用：

- `theme`
- `proxy`

这里的“启用模块”只表示命令入口可用，不代表一定产生副作用。`proxy` 模块虽然默认启用，但只有在 `proxy.enabled = true` 时才会向环境变量写入代理。

`proxy` 模块还提供 `proxy bypass add <host>`，用于把本地服务、容器桥接地址或其他直连目标追加到 `proxy.noProxy` 并写回配置；若代理当前已启用，则会同步刷新当前会话的 `no_proxy/NO_PROXY`。

在当前 phase1 中，`proxy` 模块的结构化工作流由 `.NET CLI` 接管：

- `proxy scan --json`
- `proxy plan --mode <enable|disable> --targets env ... --json`
- `proxy apply --plan-id <id> --json`
- `proxy rollback --change-id <id> --json`

当前仍只支持 `env` 目标；`git/npm/pip` 等应用级适配器留在后续阶段。

## 主题策略

仓库内提供内置主题 `themes/termforge.omp.json`，保证离线和首次安装即可用。
安装时会额外生成 `themes/active.omp.json` 作为稳定入口，供 PowerShell 和 CMD/Clink 共用。

安装器允许用户输入主题名：

- 如果主题名是 `termforge`，直接复制内置主题
- 如果是其他主题名，尝试通过 `oh-my-posh config export` 导出到本地
- 导出失败时回退到内置主题

这样可以同时满足：

- 首次安装可用
- 用户可以一键选择常见主题
- 运行时始终依赖本地主题文件，而不是在线下载

## 安装器策略

`install.ps1` 负责：

1. 交互式收集安装目录、主命令名和宿主使用场景
2. 让用户明确选择是否托管 PowerShell、VS Code PowerShell、CMD
3. 让用户明确选择是否真的需要 Windows Terminal 场景
4. 将 `oh-my-posh` 视为必需依赖，缺失时自动安装或明确失败退出
5. 按场景决定是否提示安装 `pwsh`、`Windows Terminal`、`Clink`
6. 明确提示代理默认关闭，只在用户确认需要时才收集 HTTP / HTTPS / NO_PROXY
7. 安装 Nerd Font，并按已选宿主写入 Windows Terminal / VS Code 字体设置
8. 将运行时复制到受管目录
9. 生成 `<主命令>.cmd` / `wtctl.cmd` 包装器，并可选加入用户 PATH
10. 写入 `scc.config.json` 和 `module_state.json`
11. 给用户真实 profile 插入受管 block
12. 配置 Clink script 和 autorun
13. 运行 `verify.ps1` 做安装后 smoke test

`setup.ps1` 负责：

1. 检测是否为 Windows 10 / 11
2. 检测 `LOCALAPPDATA` 是否可写
3. 检测 `winget`、`pwsh`、`oh-my-posh`、`Windows Terminal`、`Clink`、`VS Code`
4. 在缺少必需依赖且无法自动补齐时提前阻断
5. 输出环境摘要与风险提示
6. 再转入 `install.ps1`

`uninstall.ps1` 负责：

1. 清理 profile 注入 block
2. 从用户 PATH 移除安装目录
3. 注销 Clink scripts
4. 删除受管目录

## 当前限制

- 目前只支持 `proxy bypass add` 追加绕过项，还没有完整的代理开关/删除/重置交互式编辑器
- 还没有宿主终端 pane/layout 抽象层
- 安装器目前优先面向 Windows + winget 场景
- 经典 Console Host 的字体不会直接写注册表强推，当前优先配置 Windows Terminal / VS Code；CMD 本身通过 Clink + 已安装 Nerd Font 获得正确显示

## 变更历史

### 2026-04-11 - 品牌重命名为 TermForge

**变更内容**: 项目对外名称从 `windows-terminal` 调整为 `TermForge`，同步更新 README、安装器标题、默认安装目录、默认主题名和受管 block 标记，并保留对旧安装目录、旧主题名和旧标记的兼容。

**变更理由**: 原名称过于接近 Microsoft `Windows Terminal` 本体，难以区分“宿主终端”和“运行时/安装框架”这两个概念。`TermForge` 更准确表达项目定位。

**影响范围**: `install.ps1`、`install.cmd`、`uninstall.ps1`、`modules/common.ps1`、`modules/theme.ps1`、`verify.ps1`、README、DESIGN、MODULE_GUIDE、主题文件。

### 2026-04-11 - 安装向导按用户场景裁剪依赖与配置

**变更内容**: 安装器改为按宿主场景逐步询问用户需求；默认主命令改为 `termforge` 但允许自定义；`oh-my-posh` 改为必需依赖；`Windows Terminal` 改为仅在用户选择该宿主时才参与安装；代理默认关闭，但在启用时给出清晰的 HTTP/HTTPS/NO_PROXY 配置说明。

**变更理由**: 终端宿主和网络环境差异很大，安装器不能假设所有用户都需要同一组组件。向导必须按实际使用场景裁剪依赖和配置，才能保证兼容性和可理解性。

**影响范围**: `install.ps1`、`modules/common.ps1`、README、DESIGN、MODULE_GUIDE、VM 测试说明。

### 2026-04-11 - 增加安装预检入口 setup.ps1

**变更内容**: 新增 `setup.ps1` 作为环境预检入口，由 `install.cmd` 默认调用；预检会先检查 Windows 版本、写权限和关键依赖，再决定是否放行到 `install.ps1`。

**变更理由**: 纯净 Windows 10 / 11 环境的差异主要在依赖是否存在、是否可自动安装，而不是需要多套完全不同的安装器。增加一层预检，比维护多套安装脚本更稳。

**影响范围**: `setup.ps1`、`install.cmd`、`install.ps1`、README、DESIGN。

### 2026-04-11 - 增加 proxy bypass add 与回环默认绕过

**变更内容**: `proxy` 模块新增 `proxy bypass add <host>` 命令，允许把目标追加到 `proxy.noProxy` 并在代理已启用时立即刷新当前会话；同时把默认 `noProxy` 调整为 `127.0.0.1,localhost,::1`，并同步到安装器与文档。

**变更理由**: 代理启用后，本地回环服务如果没有被明确加入 `NO_PROXY`，很容易被错误送进代理，导致 `localhost` / `127.0.0.1` 端口无法直连。

**影响范围**: `modules/proxy.ps1`、`modules/common.ps1`、`install.ps1`、README、DESIGN、MODULE_GUIDE。

### 2026-04-08 - 项目化安装、cmd 宿主与字体基线

**变更内容**: 新增交互式安装器/卸载器，加入受管 profile 注入、动态主命令、CMD/Clink 集成、字体自动配置、内置默认主题与共享 active 主题文件，并重构配置模型。

**变更理由**: 原项目仍然是“个人 PowerShell 目录”的组织方式，无法作为真正的开源安装项目使用，也无法覆盖只有 cmd 的用户。

**影响范围**: `bootstrap.ps1`、profile 入口、`launcher.ps1`、`modules/common.ps1`、`modules/manager.ps1`、`modules/proxy.ps1`、`modules/theme.ps1`、`verify.ps1`、README、DESIGN 与安装脚本。

**决策依据**: 优先把多宿主的一键安装、命令入口和字体/主题体验做稳定，再继续向 Windows Terminal 编排层推进。
