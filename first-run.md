# 首次使用

需要 Windows x64、Windows 桌面版 Codex，以及 .NET Framework 4.8 系列。首版实际验证环境为 Windows 11。无需管理员权限、Magpie、Node.js、Python 或额外 API 密钥。

1. 解压发行 ZIP 到可写目录，保留其中的 `portable.flag`。
2. 打开 Codex 和一个有本机记录的聊天，保持左侧导航栏及聊天列表可见。
3. 双击 `CodexLiteHud.exe`，读数显示在头像上方。
4. 输入和输出各显示“本次请求 / 本对话”两行。右键选择项目及顺序，模型默认隐藏；解锁后可自由拖动，重新锁定就贴回侧栏。HUD 不显示悬停提示。

需要桌面快捷方式时，在解压目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install-shortcuts.ps1
```

同时希望登录启动：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install-shortcuts.ps1 -StartWithWindows
```

脚本只创建指向当前文件夹的快捷方式，不会立即启动 HUD。安装后仍需双击 `CodexLiteHud.exe` 或桌面快捷方式；`-StartWithWindows` 在下次登录时生效。设置后保留这个文件夹的位置；移动后重新运行脚本更新路径。退出 HUD 后可删除对应的桌面和启动快捷方式，再移走程序目录；自己的 `data/` 可单独保留。

需要从一个入口同时打开 Codex 和 HUD，可运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install-shortcuts.ps1 -WithCodex
```

桌面会增加 `Codex + HUD` 快捷方式，以后用它启动。启动脚本通过系统应用标识打开当前用户安装的 Windows Codex，启动后立即退出，不增加常驻监听。此功能要求解压目录中保留 `launch-with-codex.ps1`，移动目录后重新创建快捷方式。它不修改原 Codex 的入口。

自定义 Codex 目录使用 `CODEX_HOME`，在启动 HUD 前设置。`CODEX_LITE_HUD_DATA_HOME` 可指定 HUD 自己的数据目录。变量只影响路径选择，不会替你修改 Codex 配置。

从旧版升级时，先退出旧悬浮条，再把新包中的文件复制覆盖到原目录，保留自己的 `data/`，最后重新运行 `CodexLiteHud.exe`。原目录不变时，快捷方式可继续使用；换目录后请重新创建快捷方式。

显示“待识别”时，先确认当前是有本机记录的聊天、聊天列表可见，并检查是否有未归档同名聊天。右键选择会话可以固定。没有额度快照时显示“—”；额度来自本地快照，不是实时服务器查询。
