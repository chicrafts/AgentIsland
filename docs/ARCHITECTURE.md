# Agent Island 架构与贡献指南

面向想理解内部实现、修 bug、或扩展状态映射的开发者。

## 技术栈

- 桌面端：C# + WPF + Windows Forms（托盘）
- 目标运行时：.NET Framework 4.x，依赖系统自带的 `csc.exe`
- 安装包：`iexpress.exe`（Windows 自带）
- 构建入口：`scripts/build-installer.ps1`
- 输出：`dist/AgentIslandSetup.exe`

仓库根的 `AgentIsland.csproj` 是早期 .NET 6 实验残留，**不被实际构建链使用**——以 `scripts/build-installer.ps1` 为准。

## 安全红线

**不要修改** `%USERPROFILE%\.codex\config.toml`。早期一次误改导致过 Codex 启动异常。当前正确做法：

- Codex hooks 写入 `%USERPROFILE%\.codex\hooks.json`
- Claude Code hooks 写入 `%USERPROFILE%\.claude\settings.json`
- 写入前自动备份原文件
- 卸载时只移除 Agent Island 自己添加的项，保留用户其它配置

每次安装后建议验证：

```powershell
$config = Join-Path $env:USERPROFILE ".codex\config.toml"
if (Test-Path $config) {
  Select-String -Path $config -Pattern "AgentIsland","Agent Island","hook.ps1","Notification","StopFailure" -SimpleMatch
}
```

正常应无输出。

## 关键路径

| 路径 | 用途 |
|---|---|
| `netfx/` | 全部 C# 源码 |
| `scripts/build-installer.ps1` | 编译 + 打包 |
| `scripts/install.ps1` / `scripts/uninstall.ps1` | 用户机安装 / 卸载 |
| `assets/codex-openai.ico` / `assets/claude-code.ico` | 内置离线官方图标 |
| `%LOCALAPPDATA%\AgentIsland\` | 用户机运行目录 |
| `%LOCALAPPDATA%\AgentIsland\events.ndjson` | hook 事件日志（NDJSON） |
| `%LOCALAPPDATA%\AgentIsland\last-event.json` | 最后一条事件，启动时回放 |
| `%LOCALAPPDATA%\AgentIsland\hook.ps1` | 接收外部 hook 的写入端 |
| `%LOCALAPPDATA%\AgentIsland\settings.ini` | 用户设置 |

## 构建与安装

构建：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

本机安装并重启：

```powershell
Get-Process AgentIsland -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\dist\publish\install.ps1
```

检查进程：

```powershell
Get-Process AgentIsland -ErrorAction SilentlyContinue |
  Select-Object ProcessName,Id,Responding,StartTime,Path
```

卸载：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\AgentIsland\uninstall.ps1"
```

## 文件职责

| 文件 | 职责 |
|---|---|
| `Program.cs` | 入口、单实例锁、构建窗口、启动 EventTailer / CodexActivityWatcher / 托盘 |
| `MainWindow.cs` | 大岛 UI、多会话列表、展开/收起、托盘提醒、图标 |
| `MainWindow.Compact.cs` | 小岛 UI、弹簧缩放、紧凑状态、官方离线图标 |
| `SpringAnimator.cs` | 欠阻尼弹簧动画核心（`CompositionTarget.Rendering` 驱动，含限频和子步） |
| `StatusCoordinator.cs` | 多会话状态合并、置顶策略、过期清理、防止尾流事件冲掉 Done |
| `HookNormalizer.cs` | 把 hook JSON 行转换成 `StatusSnapshot` |
| `StatusSnapshot.cs` | 状态模型 + `IslandMode` 枚举 |
| `SupportFiles.cs` | 安装时落地 `hook.ps1`、`connect-codex.ps1`、`connect-claude.ps1`、示例和 README |
| `CodexActivityWatcher.cs` | 监听 `.codex/sessions/*.jsonl`，作为 Codex 状态的快速探测通道 |
| `EventTailer.cs` | 监听 `events.ndjson` |
| `SettingsWindow.cs` / `AppSettings.cs` | 设置面板和本地设置持久化 |
| `TrayService.cs` | 系统托盘菜单 + 气泡 |
| `AppPaths.cs` | 路径统一 |

## 状态流

```
Codex / Claude Code hook
  -> %LOCALAPPDATA%\AgentIsland\hook.ps1
  -> events.ndjson
  -> EventTailer
  -> StatusCoordinator.HandleLine
  -> HookNormalizer.TryNormalize
  -> StatusSnapshot
  -> MainWindow.ApplyStatus
```

Codex 还有快速探测通道：

```
%USERPROFILE%\.codex\sessions\*.jsonl
  -> CodexActivityWatcher
  -> 合成 source=session-watch 事件
  -> StatusCoordinator.HandleLine
```

快速探测只能用于"提早识别"，**不能**覆盖等待用户确认的状态——`StatusCoordinator` 里有专门的保护逻辑。

## 状态映射

| IslandMode | 含义 | 触发 |
|---|---|---|
| `Idle` | 待命 | 启动、`SessionStart`、`auth_success` |
| `Thinking` | 思考中 / 检索中 / 处理结果 | `UserPromptSubmit`、`Pre/PostToolUse`（非 edit/cmd） |
| `Editing` | 修改中 | `PreToolUse` 且工具像编辑（`edit`/`write`/`apply_patch` 等） |
| `Running` | 执行中 | `PreToolUse` 且工具像命令（`bash`/`shell` 等） |
| `Waiting` | 等你确认 / 等待回复 / 等待填写 / 需要注意 | `PermissionRequest`、`Notification(idle_prompt)`、`Elicitation` 等 |
| `Done` | 已完成 | `Stop`、`SessionEnd`、`turn_completed` |
| `Error` | 出错了 / 已拒绝 | `PermissionDenied`、`*Failure`、`*Error` |
| `Compacting` | 整理上下文 | `PreCompact` |

`Waiting` 是最重要的状态：

- 在多会话列表里优先级最高
- 同会话处于 `Waiting` 时，来自 Codex session watcher 的普通探测事件 75 秒内不会覆盖它
- 强制展开大岛，不自动缩小
- 触发 Windows 托盘气泡（同一条 45 秒内不重复）

`Done` 也有保护：

- 已是 `Done` 的会话收到尾延迟事件（迟到的 `PostToolUse`、`Notification idle_prompt`、`SubagentStop` 等）时，45 秒内不会回退到 `Thinking` 或 `Waiting`。这避免 Claude Code 在 `Stop` 后立刻发的 idle_prompt 把"已完成"翻成"等待回复"。

## Hook 事件清单

Codex hooks：

- `SessionStart` / `UserPromptSubmit` / `PreToolUse` / `PostToolUse`
- `PermissionRequest`
- `PreCompact` / `PostCompact`
- `SubagentStart` / `SubagentStop`
- `Stop`

Claude Code hooks：

- `SessionStart` / `UserPromptSubmit` / `PreToolUse` / `PostToolUse` / `PostToolUseFailure`
- `PermissionRequest` / `PermissionDenied`
- `Notification` / `Elicitation` / `ElicitationResult`
- `PreCompact` / `PostCompact`
- `SubagentStart` / `SubagentStop` / `TaskCreated` / `TaskCompleted`
- `Stop` / `StopFailure` / `SessionEnd`

关键映射：

- `PermissionRequest` 或 `notification_type=permission_prompt` → "等你确认"
- `Notification` 且 `notification_type=idle_prompt` → "等待回复"
- `Elicitation` 或 `notification_type=elicitation_dialog` → "等待填写"
- `PermissionDenied` → "已拒绝"

## 图标资源

小岛、大岛、列表都用内置离线官方图标，**运行时不联网**：

- OpenAI / Codex：`assets/codex-openai.ico`
- Anthropic / Claude Code：`assets/claude-code.ico`

构建脚本会复制到 `dist/publish/`，安装脚本再复制到 `%LOCALAPPDATA%\AgentIsland\assets\`。

## UI 规则

- 小岛：状态灯 + 会话短标题 + 工具图标 + 状态文字 + 右侧强调条
- 大岛：官方图标 + Agent 名 + 状态 + 详情 + 波纹动画
- 多会话列表：状态灯 + 工具图标 + 标题 + 详情 + 时间 + 清除按钮
- `Waiting` 必须比普通执行状态更醒目
- 多会话列表排序：`Waiting` > `Error` > 其它 active > `Done`
- 小岛和大岛使用 `SpringAnimator`，不要换成线性 `DoubleAnimation`
- `Waiting` 状态不自动缩小

## 性能要点

- 所有 `DropShadowEffect` 必须 `RenderingBias = RenderingBias.Performance`，否则 GPU 在 morph 阶段会冒烟
- `ApplyMorphProgress` 里 Width/Height 的 set 有 delta gating（变化 < 0.4 px 跳过）
- `SpringAnimator` 用 13 ms 的 emit 限频，避免 144 Hz 显示器跑出冗余帧
- 不要把 `BlurRadius` / `ShadowDepth` 接到 morph 上每帧改——只动 `Opacity`

## 设置项

设置文件：`%LOCALAPPDATA%\AgentIsland\settings.ini`

字段：`autoHide`、`autoHideDelaySeconds`、`palette`、`calmMotion`、`showMultiConversationCount`、`keepCompletedConversations`、`fastStatusProbe`、`compactMode`、`compactDelaySeconds`、`compactTitleChars`

## 常用验证片段

检查 Claude Code 已写入 Agent Island hook：

```powershell
$settings = Join-Path $env:USERPROFILE ".claude\settings.json"
$json = [IO.File]::ReadAllText($settings,[Text.Encoding]::UTF8) | ConvertFrom-Json
foreach($event in "PermissionRequest","PermissionDenied","Notification","Elicitation","ElicitationResult"){
  $found=$false
  $prop=$json.hooks.PSObject.Properties[$event]
  if($prop){
    foreach($g in @($prop.Value)){
      foreach($h in @($g.hooks)){
        if(([string]$h.command) -like "*AgentIsland*hook.ps1*"){ $found=$true }
      }
    }
  }
  [pscustomobject]@{Event=$event; AgentIslandHook=$found}
}
```

模拟"等待确认"：

```powershell
$line = '{"v":1,"agent":"claude","event":"PermissionRequest","session_id":"demo-wait","message":"需要确认或拒绝一次命令执行"}'
Add-Content -Path "$env:LOCALAPPDATA\AgentIsland\events.ndjson" -Value $line -Encoding UTF8
```

模拟"等待回复"：

```powershell
$line = '{"v":1,"agent":"claude","event":"Notification","notification_type":"idle_prompt","session_id":"demo-idle","message":"Claude Code 正在等待你的下一句"}'
Add-Content -Path "$env:LOCALAPPDATA\AgentIsland\events.ndjson" -Value $line -Encoding UTF8
```

## 贡献注意事项

- 优先小改，UI 大改要先讨论
- `apply_patch` 在某些环境偶发崩溃；失败时改用精确文本替换，但**必须重新编译验证**
- 编译参数里必须有 `/codepage:65001`，否则中文 UI 乱码
- 不要删除 `assets/*.ico`，安装包依赖它们
- 不要直接覆盖用户已有 hook 配置的无关项，只能合并或移除 Agent Island 自己的项
- 改状态逻辑要同步检查 `HookNormalizer.cs` 和 `StatusCoordinator.cs`
- 改接入事件要同步 `SupportFiles.cs` 的连接脚本和示例
- 安装后务必检查进程和 `.codex/config.toml`
