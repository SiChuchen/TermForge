# SCC 模块开发规范 (MODULE_GUIDE)

本目录是 `TermForge` 的 PowerShell 运行时工作区。默认管理 CLI 仍然叫 `scc`，目标是让 profile、模块状态和业务模块都通过统一的 bootstrap 启动，并且在单模块加载时也能正常工作。

## 1. 目录结构

```text
PowerShell/
  ├── bootstrap.ps1                     (共享启动入口：负责初始化与按状态加载模块)
  ├── Microsoft.PowerShell_profile.ps1  (PowerShell 主 profile，仅负责转发到 bootstrap)
  ├── Microsoft.VSCode_profile.ps1      (VSCode 终端 profile，仅负责转发到 bootstrap)
  ├── module_state.json                 (模块启停状态)
  ├── scc.config.json                   (模块运行配置：安装根、主命令、代理、主题等)
  ├── verify.ps1                        (本地 smoke test：校验 profile、help 和子命令入口)
  ├── MODULE_GUIDE.md                   (开发指南)
  └── modules/
      ├── common.ps1                    (公共函数：路径、JSON、帮助注册、模块发现)
      ├── manager.ps1                   (核心主控模块：提供可配置主命令，默认 scc)
      ├── proxy.ps1                     (业务模块示例：代理配置)
      ├── theme.ps1                     (业务模块示例：主题控制)
      └── 你的新模块.ps1                (后续新增模块)
```

## 2. 启动约定

1. 所有 profile 只负责 dot-source `bootstrap.ps1`。
2. `bootstrap.ps1` 会先加载 `modules/common.ps1` 和 `modules/manager.ps1`，再读取 `module_state.json` 按状态加载业务模块。
3. 模块加载失败时必须输出明确的模块名和失败原因，不能用笼统提示吞掉异常。

## 3. 新模块约定

1. 新模块放在 `modules/` 下，文件名即模块名，例如 `example.ps1`。
2. 模块内部先 dot-source `common.ps1`，再调用 `Initialize-SccHelpRegistry` 和 `Register-SccHelp` 注册模块帮助。
3. 如果模块暴露了独立命令，例如 `posh`、`posht`，应额外调用 `Register-SccCommandHelp` 注册命令级帮助，并在函数里支持 `-Help`。
4. 模块如果依赖外部程序、目录或配置文件，必须先校验依赖，再决定是否降级或跳过加载。
5. 模块配置应优先写入 `scc.config.json`，避免把路径、端口和命令名硬编码在脚本里。
6. `cli.commandName` 是用户可见的主命令；内部始终保留 `wtctl` 作为恢复入口。
7. `theme.commandPath` 可选；留空时按命令名 `oh-my-posh` 调用，只有在自动化环境里需要绕过 `WindowsApps` 别名时才填写真实可执行路径。

## 4. 管理命令

`<主命令> list`
查看模块状态和可用子命令。

`<主命令> doctor [default|fancy|verbose|json]`
执行当前会话诊断。默认模式兼顾兼容性和可读性；`fancy` 使用更强的配色和 Unicode 符号；`verbose` 会展开命令级检查；`json` 适合脚本消费。默认和 `fancy` 会根据终端宽度自动切换为更紧凑的堆叠布局。

`<主命令> enable <模块名>`
启用已存在的模块并写回 `module_state.json`。

`<主命令> disable <模块名>`
禁用指定模块并写回 `module_state.json`。

`<主命令> reload`
重新加载当前 profile，便于立即验证配置变更。

`<主命令> help <模块名|命令名>`
查看模块级或命令级帮助，例如 `scc help theme`、`scc help posht`；如果用户把主命令改名，也可以始终使用 `wtctl help ...`。

`proxy`
查看当前代理配置、`NO_PROXY` 与当前终端环境变量。

`proxy bypass add <主机名/IP>`
把目标追加到 `proxy.noProxy` 并写回配置；如果当前代理已启用，还会立即同步到当前会话，例如 `proxy bypass add 127.0.0.1 localhost host.docker.internal`。参数也支持逗号分隔，但不要带 `http://`。

`posh help`
显示 `theme` 模块下所有子命令的具体用法。

`pwsh -NoProfile -File .\verify.ps1`
在无 profile 污染的上下文里对两个 profile 执行 smoke test，同时验证主命令的 `doctor default/fancy/verbose/json` 四种入口。
