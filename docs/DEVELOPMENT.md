# 开发与发布

源码使用 C# 与 WinForms、Windows 原生接口和系统 SQLite。编译器来自 Windows 的 .NET Framework，无 NuGet、Node.js、Python 运行依赖。

## 源码职责

- `Program.cs` 处理启动、单实例和命令行检查；`Data.cs` 保存一次读取结果及显示变化比较；`Reader.cs` 将会话选择和后台读取串起来。
- `SessionReader.cs` 解析会话统计，`SessionCounters.cs` 与 `SessionTiming.cs` 分别负责计数和时间；`QuotaLogs.cs` 发现跨会话额度变化，`QuotaReader.cs` 负责额度对象、时序和过期规则。两条日志链路共用增量读取基础层，保留全量统计与末尾额度扫描各自的策略。
- `NativeThreadCatalog.cs` 只读合并数据库中的会话身份和标题；`WindowSession.cs` 通过 UI Automation 识别选中项及侧栏边界；`WindowTracking.cs` 接收窗口事件，`Native.cs` 集中声明通用窗口系统调用。
- `CodexHud.cs` 保存窗体生命周期、菜单和拖动操作；同一窗体的 `HudFollowing.cs` 负责目标选择与位置跟随，`HudDisplay.cs` 负责布局与呈现，`HudRefresh.cs` 负责刷新协调和诊断写盘。它们是同一个窗体的 partial 声明，不会引入额外线程或独立生命周期。
- 指标元数据和标签槽位统一管理名称、行高、可见性、字体角色与主题颜色，布局、紧凑模式和字体拟合使用同一组标签；特殊读数的格式化仍由各自业务规则负责。`DisplaySettings.cs` 只保存显示选择、顺序和旧配置迁移。

窗口位置事件不执行日志解析、UI Automation 或主题采样。后台读取不操作控件，结果回到 UI 线程后再检查当前选择并显示。兼容代码应当留在对应边界：日志字段别名在解析层，旧配置迁移在设置层，DPI 和原生窗口保护在显示与跟随层。

## 构建与验证

`VERSION` 是版本单一来源。构建生成程序集、文件与产品版本；1.0.2 对应文件版本 `1.0.2.0`。图标原稿在 `assets/icon.svg`，`scripts/MakeIcon.cs` 生成同样图案的多尺寸 ICO。

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\release.ps1 -DryRun
```

构建和隔离测试结果在 `.local/`，ZIP 与 `.sha256` 在 `dist/`。`release.ps1` 只运行本地检查，不提交或推送。测试使用独立 `CODEX_HOME` 与 HUD 数据目录，不读取开发者真实聊天作为单元测试输入。

Windows 原生窗口用例在隔离的屏外测试窗口运行。`TargetSelectionChecks` 与 `WindowInventory` 是额外的本机只读联动检查，需显式提供现场句柄，不纳入无 Codex 的 CI。测试不激活或移动真实 Codex／宠物窗口。

布局夹具保留各 DPI 和紧凑高度用例，同时将申请的测试视口限制在宿主的 `MaxWindowTrackSize` 内；云端的小桌面不能创建超过该上限的 WinForms 顶层窗口。另跑一遍 700 像素高度上限的模拟小桌面用例，所有读数的裁切、顺序和布局断言仍然执行。

打包采用允许清单，仅包含 EXE、使用说明、许可证、版本、快捷方式脚本和便携标记；不收集工作目录里的任意文件。校验步骤核对 ZIP 文件集、版本和 SHA-256。

公开仓库准备完成后，提交经过复核的源码并等待 main 分支的 validate 工作流通过，再创建与 VERSION 一致的标签（例如 `v1.0.2`）并推送。GitHub Actions 在 Windows 上重新构建、运行回归、检查 ZIP，再创建试用版 Pre-release，不将其设为 Latest 稳定版。已公开的发行资产不被脚本覆盖。发布前检查许可、版本、兼容范围及发行说明；下载 ZIP 与源代码包用途不同。

本机运行副本与历史备份同样可留在 `.local/`，不进入 Git。发布前用 `git status --short` 和 `git diff --check` 检查文件集。
