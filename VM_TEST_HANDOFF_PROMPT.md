# VM Test Handoff Prompt

```text
你现在接手一个已经推进到“可安装项目第一阶段”的 `TermForge` 项目，请先快速理解当前状态，然后直接在测试环境里继续做真实安装测试和修复，不要只停留在分析。

项目路径：
<这里替换成虚拟机里的实际路径，例如 D:\TermForge>

你需要先知道的当前状态：
1. 这个项目已经不是单纯的 PowerShell profile 目录了，而是一个可安装项目。
2. 已有交互式安装器和卸载器：
   - install.cmd
   - setup.ps1
   - install.ps1
   - uninstall.ps1
3. 已支持：
   - PowerShell / VSCode PowerShell 受管 profile 注入
   - 动态主命令，默认 termforge
   - 固定恢复入口 wtctl
   - CMD + Clink + Oh My Posh 集成
   - Nerd Font 安装
   - Windows Terminal / VS Code 终端字体配置
   - 内置默认主题 + active.omp.json 共享主题文件
   - proxy 默认关闭，theme 默认开启
   - setup.ps1 会先做环境预检，再转入 install.ps1
4. 已新增统一命令入口：
   - launcher.ps1
5. 仓库内验证已通过：
   - pwsh -NoProfile -File .\verify.ps1
   - launcher.ps1 doctor json
   - 安装器 dry-run 已通过
6. install.cmd 已改成双击失败时 pause，避免错误窗口一闪而过。
7. 当前最需要做的是：在虚拟机里做真实安装测试，而不是继续空谈设计。

你接手后先做这些事：
1. 阅读并理解这些关键文件：
   - README.md
   - DESIGN.md
   - install.cmd
   - setup.ps1
   - install.ps1
   - uninstall.ps1
   - launcher.ps1
   - bootstrap.ps1
   - modules/common.ps1
   - modules/manager.ps1
   - modules/proxy.ps1
   - modules/theme.ps1
   - verify.ps1
   - scc.config.json
2. 先跑一次仓库内自检：
   - pwsh -NoLogo -NoProfile -File .\verify.ps1
3. 然后做真实安装测试，优先覆盖这些场景：
   - 从资源管理器双击 install.cmd
   - 在 PowerShell 中先运行 powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\setup.ps1
   - 在 cmd.exe 中运行 install.cmd
   - 在 PowerShell 中运行 powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
4. 安装测试时重点检查：
   - setup.ps1 的环境预检是否准确，阻塞项和警告项是否合理
   - 缺少依赖时是否会正确提示/安装 pwsh、oh-my-posh、Windows Terminal、Clink
   - 失败时窗口是否会停住
   - 安装目录是否加入用户 PATH
   - 是否生成 `<主命令>.cmd` 和 wtctl.cmd
   - 新开的 cmd / PowerShell / Windows Terminal 中是否能运行：
     - termforge doctor
     - wtctl doctor
   - theme 是否默认启用
   - proxy 是否默认不生效
   - active.omp.json 是否正确生成
   - Windows Terminal 字体是否被写入
   - VS Code terminal 字体是否被写入
   - Clink + Oh My Posh 是否在 cmd 中真正生效
5. 然后测试卸载：
   - powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1
   - 检查 profile 注入是否回滚
   - 检查 PATH 是否回滚
   - 检查 Clink scripts 是否注销
6. 如果发现问题，直接修复代码并重新验证，不要停在问题描述。

工作方式要求：
- 先用 fast_context 搜索和读取代码，不要猜。
- 直接执行、修复、复测。
- 每完成一轮测试，给出：
  - 复现步骤
  - 结果
  - 根因
  - 修复内容
  - 剩余风险
- 如果虚拟机里的实际环境和当前实现假设不一致，优先让安装器适配真实环境。

现在开始：
先读取上述关键文件，概括当前实现，再立即进入真实安装测试。
```
