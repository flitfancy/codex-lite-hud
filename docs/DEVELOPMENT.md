# 开发与发布

源码使用 C# 与 WinForms、Windows 原生接口和系统 SQLite。编译器来自 Windows 的 .NET Framework，无 NuGet、Node.js、Python 运行依赖。

`VERSION` 是版本单一来源。构建生成程序集、文件与产品版本；1.0 对应文件版本 `1.0.0.0`。图标原稿在 `assets/icon.svg`，`scripts/MakeIcon.cs` 生成同样图案的多尺寸 ICO。

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\release.ps1 -DryRun
```

构建和隔离测试结果在 `.local/`，ZIP 与 `.sha256` 在 `dist/`。`release.ps1` 只运行本地检查，不提交或推送。测试使用独立 `CODEX_HOME` 与 HUD 数据目录，不读取开发者真实聊天作为单元测试输入。

Windows 原生窗口用例在隔离的屏外测试窗口运行。`TargetSelectionChecks` 与 `WindowInventory` 是额外的本机只读联动检查，需显式提供现场句柄，不纳入无 Codex 的 CI。测试不激活或移动真实 Codex／宠物窗口。

布局夹具保留各 DPI 和紧凑高度用例，同时将申请的测试视口限制在宿主的 `MaxWindowTrackSize` 内；云端的小桌面不能创建超过该上限的 WinForms 顶层窗口。另跑一遍 700 像素高度上限的模拟小桌面用例，所有读数的裁切、顺序和布局断言仍然执行。

打包采用允许清单，仅包含 EXE、使用说明、许可证、版本、快捷方式脚本和便携标记；不收集工作目录里的任意文件。校验步骤核对 ZIP 文件集、版本和 SHA-256。

公开仓库准备完成后，提交经过复核的源码并等待 main 分支的 validate 工作流通过，再创建与 VERSION 一致的 `v1.0` 标签并推送。GitHub Actions 在 Windows 上重新构建、运行回归、检查 ZIP，再创建试用版 Pre-release，不将其设为 Latest 稳定版。已公开的发行资产不被脚本覆盖。发布前检查许可、版本、兼容范围及发行说明；下载 ZIP 与源代码包用途不同。

本机运行副本与历史备份同样可留在 `.local/`，不进入 Git。发布前用 `git status --short` 和 `git diff --check` 检查文件集。
