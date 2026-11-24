[README.md](https://github.com/user-attachments/files/35333659/README.md)
# -# 桌面备忘录 · DesktopMemo

一个常驻 Windows 桌面右下角的轻量备忘录程序。窗口始终置顶、不进任务栏，可收起为悬浮球；支持长期 / 短期备忘分类、完成打卡（双向切换）、按天统计与开机自启。

- **技术栈**：C# / WPF，基于 .NET Framework 4.x。
- **零依赖部署**：用 Windows 自带的 `csc.exe` 从源码编译成**单个绿色 exe**，无需 Visual Studio、无需 dotnet SDK、无需安装程序、无需管理员权限。
- **单文件运行**：`ui.xaml`、`app.ico`、清单文件全部嵌入 exe，拷贝即用。

---

## 目录

1. [项目简介](#1-项目简介)
2. [主要功能](#2-主要功能)
3. [环境依赖与安装步骤](#3-环境依赖与安装步骤)
4. [配置项说明](#4-配置项说明)
5. [使用示例](#5-使用示例)
6. [常见问题与注意事项](#6-常见问题与注意事项)
7. [目录结构](#7-目录结构)
8. [验证脚本](#8-验证脚本)

---

## 1. 项目简介

很多人需要一个「一直看得见、随时记两笔」的便签：既不能挡住正在用的窗口，又不能被别的窗口盖掉，还不能每次开机都手动去开。桌面备忘录就是为这个场景做的。

它启动后固定在屏幕右下角，默认展开为一张小卡片；点最小化 / 关闭会收起成一颗小悬浮球贴在角落，点球又展开。备忘支持「短期（24 小时自动失效）」和「长期（永久或指定日期区间）」两类，可为每条任务打卡，并按天回看完成情况。关机重启或睡眠唤醒后，靠三重自启机制自动回到桌面角落。

程序把数据写在本机 `notes.xml`（另有自动备份），不联网、不上传，完全本地。

---

## 2. 主要功能

### 2.1 桌面常驻窗口

- 固定在屏幕**右下角**，窗口始终**置顶**（`WS_EX_TOPMOST`）。
- **不出现在任务栏 / Alt+Tab**（`WS_EX_TOOLWINDOW`，未设置 `WS_EX_APPWINDOW`）。按 `Win+D` 也不会消失。
- 失焦时只归位、不隐藏；禁止最小化，避免误操作把它缩没。
- 图钉按钮保持「恒置顶」状态；空闲 30 秒会自动重新归位到右下角。

### 2.2 悬浮球

- 点最小化 / 关闭按钮，窗口收起为右下角的一颗圆形悬浮球（不抢焦点）。
- 点击悬浮球即可重新展开为卡片。
- 以 `--startup` 参数启动时，直接进入悬浮球状态（开机自启用），安静不打扰。

### 2.3 备忘分类

点「新建备忘」后**先选类型**，再填标题和内容：

| 类型 | 子模式 | 失效规则 | 侧栏标识色 |
|---|---|---|---|
| 短期 | — | 创建后 **24 小时**自动失效，到点自动清理 | 棕 `#B46B2D` |
| 长期 | 永久保留 | 一直保留，直到手动删除 | 绿 `#769062` |
| 长期 | 日期区间 | 保留到结束日期，过期自动清理 | 蓝 `#5B7C99` |

侧栏会显示剩余时间（如「短期·剩18时」「长期·至11/30」）。程序每 1 分钟检查一次并清理过期备忘。

### 2.4 完成打卡（双向）

- 编辑区右下角是一颗**纯色圆形按钮**，在两种状态间来回切换：
  - 未完成：**「待办」** + 空心圆 `○`，**棕色** `#B46B2D`（复用页面同一颜色变量 `AccentBrush`）。
  - 已完成：**「已完成」** + 对勾 `✓`，**绿色** `#769062`（`LongForeverBrush`）。
- **两态都可点击**，可反复来回切换，没有「只能打一次」的限制。
- 每次切换会同步更新：按钮文案 / 图标 / 颜色、标题下方的状态文本（「未完成」/「今天完成 HH:mm」）、列表项的 ✓ 标记、底部统计、统计面板，以及数据文件里的 `completed` / `completedAt`（取消打卡时清空）。
- 纯色设计：整颗按钮只有一个实心圆，无描边、外环、阴影、渐变，也没有悬停 / 按下变色；两态共用同一套形状与字体，仅靠文案 / 图标 / 颜色区分。点击有轻微放大回弹动效；圆形 + `Viewbox` 等比缩放保证任何尺寸下文字都不裁切。

### 2.5 每日统计

底部 📊 图标打开「任务完成统计」面板，按天列出每天已完成（✓）与未完成（○）的任务数量和明细，最近几天带「昨天 / 前天」标签。

### 2.6 自动保存

- 编辑后约 0.65 秒自动保存；也可按 `Ctrl+S` 立即保存。
- 写入采用「临时文件 + 原子替换」，并保留 `notes.backup.xml` 备份，避免写坏数据。
- 状态栏显示「已保存 / 正在输入 / 保存失败」及字数。

### 2.7 开机自启（三重保障）

齿轮菜单可开关。开启时同时布置三道机制，任一生效即可拉起程序：

1. **注册表**：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DesktopMemo`，命令行 `"<exe路径>" --startup`。
2. **启动文件夹快捷方式**：`DesktopMemo.lnk`，参数 `--startup`。
3. **计划任务** `\DesktopMemo`：含三类触发器——登录时（LogonTrigger）、Modern Standby 唤醒（Power-Troubleshooter 事件 ID 1）、传统睡眠恢复（Kernel-Power 事件 ID 107）。

前两道覆盖冷启动 / 重新登录；第三道专门解决「睡眠唤醒不重新登录」导致前两道不触发的问题。程序每次首个实例启动都会做一次自愈：发现计划任务被删会自动重建。

---

## 3. 环境依赖与安装步骤

### 3.1 依赖

- **操作系统**：Windows 7 / 8 / 10 / 11（64 位或 32 位均可）。
- **运行时**：.NET Framework 4.x（4.0 及以上，Windows 自带；Win10/11 通常为 4.8）。**不需要 dotnet SDK、不需要 Visual Studio**。
- **从源码编译时**：需要 Windows PowerShell 3.0+（系统自带）与 .NET Framework 的 `csc.exe`（构建脚本会自动定位）。
- **直接运行编译好的 exe 时**：只需要 .NET Framework 4.x，无需 PowerShell、无需任何安装步骤。

### 3.2 方式 A：直接运行（最快）

拿到 `桌面备忘录.exe` 后，双击即可。窗口会出现在屏幕右下角。不需要安装、不需要管理员权限。

### 3.3 方式 B：从源码编译

在仓库根目录打开 **Windows PowerShell 5.1**（注意：不要用 PowerShell 7 跑这些 `.ps1`，脚本按 5.1 的行为设计）：

```powershell
# 1) 先校验 XAML（csc 只嵌入不解析 XAML，编译成功不代表界面合法）
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File scripts\check-xaml.ps1

# 2) 编译，产物在 bin\桌面备忘录.exe
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1
```

构建脚本会自动读取当前 CLR 目录，拼出 `csc.exe` 与 WPF 引用程序集路径，不写死任何本机目录，换电脑也能用。产物是单个 exe。

### 3.4 方式 C：编译并装到桌面（可选）

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\install.ps1
```

该脚本会把 `bin\` 里编译好的 exe 复制到**当前用户的桌面**并独立启动（会先结束正在运行的实例以释放单实例锁）。加 `-NoLaunch` 可只复制不启动。

### 3.5 重新生成图标（可选）

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\make-icon.ps1
```

用 `System.Drawing` 生成 7 种尺寸的 `assets\app.ico`。不改图标可跳过。

---

## 4. 配置项说明

程序几乎不需要手动配置，以下是可调项：

### 4.1 环境变量

| 变量 | 作用 | 示例 |
|---|---|---|
| `DESKTOP_MEMO_DATA_DIR` | 指定数据目录（`notes.xml` 所在处）。设置后优先使用，便于绿色部署 / U 盘便携 / 多套数据隔离。 | `set DESKTOP_MEMO_DATA_DIR=D:\MyMemoData` |

若未设置，程序按以下顺序挑选**第一个可写**的目录：

1. `%APPDATA%\DesktopMemo`（默认，每用户独立）
2. `<exe所在目录>\桌面备忘录数据`（便携回退）
3. `%TEMP%\DesktopMemo`（最后兜底）

### 4.2 启动参数

| 参数 | 含义 |
|---|---|
| （无） | 正常启动，展开为右下角卡片 |
| `--startup` | 静默启动为悬浮球，不弹窗、不抢焦点。开机自启的三道机制都用这个参数 |

手动模拟开机效果：

```powershell
"C:\Users\<你>\Desktop\桌面备忘录.exe" --startup
```

### 4.3 注册表与计划任务

| 项 | 位置 / 名称 | 用途 |
|---|---|---|
| Run 值 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DesktopMemo` | 登录时自启 |
| 快捷方式 | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\DesktopMemo.lnk` | 登录时自启（双保险） |
| 计划任务 | 任务计划程序库 `\DesktopMemo` | 登录 / 唤醒 / 睡眠恢复时自启 |

这些由齿轮菜单统一管理，一般不用手动改。齿轮菜单显示「开机自启动 已开启 / 已关闭」，点它即可开关三道机制。

### 4.4 代码内常量（进阶）

以下值定义在 `src\Program.cs`，如需调整可修改后重新编译：

| 常量 | 默认值 | 含义 |
|---|---|---|
| `DockIdleSeconds` | 30 | 空闲多少秒后自动归位到右下角 |
| `_saveTimer.Interval` | 650 ms | 编辑后自动保存的防抖延时 |
| `_expireTimer.Interval` | 1 min | 过期备忘检查周期 |
| 短期时长 | 24 h | 短期备忘自动失效时间（`CreatedAt.AddHours(24)`） |
| 单实例 mutex | `Local\DesktopMemo.SingleInstance.v1` | 保证全局只有一个实例 |
| 颜色 | `#B46B2D` / `#769062` / `#5B7C99` | 短期(棕) / 长期永久(绿) / 长期区间(蓝)，打卡按钮复用前两者 |

---

## 5. 使用示例

### 5.1 记一条短期备忘（比如「今天 18:00 取快递」）

1. 点左侧「新建备忘」（或按 `Ctrl+N`）。
2. 选「短期」——提示会显示「短期备忘将在 X月X日 XX:XX 自动失效」。
3. 填标题「取快递」、内容「18:00 前到驿站」。
4. 修改自动保存；24 小时后这条会自动消失。

### 5.2 记一条长期备忘（比如「护照 12 月前续签」）

1. `Ctrl+N` → 选「长期」。
2. 再选模式：
   - **永久保留**：一直留着直到你删；
   - **日期区间**：选起止日期（如今天到 11/30），到期自动清理。
3. 填标题内容即可。

### 5.3 给任务打卡 / 取消打卡

- 点编辑区右下角的圆形按钮：
  - 「待办（棕）」→ 点一下 → 「已完成（绿）」，状态变「今天完成 HH:mm」，列表出现 ✓；
  - 「已完成」→ 再点一下 → 回到「待办」，状态变「未完成」，✓ 消失。
- 可反复来回；底部 📊 统计会随之更新。

### 5.4 查看每日完成情况

点底部 📊 图标，打开面板，按天查看每天已完成 / 未完成任务的数量与明细。

### 5.5 收起为悬浮球 / 展开

- 点窗口右上角的最小化或关闭按钮 → 收成右下角悬浮球。
- 点悬浮球 → 重新展开卡片。
- 想让开机后只有悬浮球：打开齿轮菜单的「开机自启动」，重启后即为悬浮球态。

### 5.6 命令行验证开机自启

```powershell
# 查看三道机制是否已配好
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v DesktopMemo
dir "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\DesktopMemo.lnk"
schtasks /Query /TN DesktopMemo /FO LIST

# 立即模拟一次自启（等价于开机）
schtasks /Run /TN DesktopMemo
```

### 5.7 跑验证脚本

在程序已运行时（把 `<PID>` 换成实际进程号）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\verify-toggle.ps1 -AppPid <PID>
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File tests\verify-style.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\uia-verify.ps1 -AppPid <PID>
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\verify2.ps1 -TargetPath "C:\Users\<你>\Desktop\桌面备忘录.exe"
```

---

## 6. 常见问题与注意事项

**Q：改动界面后，编译通过了但窗口不对 / XAML 报错？**
`csc.exe` 只把 `ui.xaml` 当资源嵌入，**不解析它**，所以「编译成功」不代表 XAML 合法。改完 XAML 一定先跑 `check-xaml.ps1`。另外 XML 注释里**不能出现双连字符 `--`**（如 `<!-- ... -- -->`），否则 `XamlReader` 会报 `An XML comment cannot contain '--'`；本项目历史上多次踩这个坑，注释请用 `:` 等替代。

**Q：`.ps1` 脚本里的中文变成乱码 / 期望字符串比较失败？**
Windows PowerShell **5.1 按 GBK 读取 `.ps1` 文件**，脚本里直接写中文字面量会被破坏。因此本仓库的脚本都是**纯 ASCII**，中文期望串一律用 Unicode 码点构造（如 `[char]0x5F85` 表示「待」）。新增脚本请沿用这个约定，或把中文通过命令行参数（UTF-16）传入。

**Q：为什么脚本必须用 `powershell.exe -ExecutionPolicy Bypass` 调用？**
系统默认执行策略可能禁止运行本地脚本；`-ExecutionPolicy Bypass` 只对本次进程生效，不改系统设置。`-STA` 是 WPF `XamlReader` 加载界面所必需的单线程单元模式。

**Q：为什么开机后程序没自动出现？**
先看是不是「睡眠唤醒」而非冷启动：Modern Standby 唤醒不重新登录，注册表 Run 和启动快捷方式都不会触发——这正是计划任务那道机制存在的意义。用 `schtasks /Query /TN DesktopMemo` 确认计划任务在不在；被安全软件清理后，程序下次启动会自愈重建。也可用 `schtasks /Run /TN DesktopMemo` 手动验证。

**Q：为什么任务栏 / Alt+Tab 里看不到程序？**
这是设计使然：窗口被设为工具窗口（不含 `WS_EX_APPWINDOW`），不进任务栏。它仍会一直置顶显示在右下角；想找到它就直接看屏幕角落，或点悬浮球。

**Q：数据存在哪里？会不会丢？**
默认在 `%APPDATA%\DesktopMemo\notes.xml`，同目录有 `notes.backup.xml` 备份；写入用临时文件 + 原子替换。可通过 `DESKTOP_MEMO_DATA_DIR` 改用别的目录。卸载 / 清理软件时如需保留备忘，记得备份这个目录。

**Q：为什么双击 exe 没反应 / 看不到窗口？**
先检查是不是已经有一个实例在右下角（单实例机制：重复启动只会激活已有窗口）。再看 `%TEMP%\DesktopMemo-startup.log` 是否有启动异常记录。若数据目录不可写，程序会自动回退到 exe 同目录或临时目录。

**Q：打卡按钮能取消吗？**
能。第 14 轮起打卡是双向的：「待办」⇄「已完成」可反复切换，两态都可点击。

**Q：窗口能被拖动 / 最小化吗？**
不能拖动、也不允许最小化——它被固定在右下角，始终置顶。这是为了保证「长期看得见、不被遮挡」。空闲 30 秒还会自动归位。

**Q：验证脚本报控件坐标是「∞」或数值异常？**
悬浮球状态下部分控件不在可视化树里，UIA 会返回无穷大矩形；`uia-verify.ps1` 已用 `[double]::IsInfinity` 跳过这类控件。请先确保窗口处于展开态再跑 UI 相关验证。

---

## 7. 目录结构

```
desktop-memo/
├── README.md                 # 本文件
├── .gitignore                # 忽略编译产物 / 本地数据 / 临时文件
├── src/
│   ├── Program.cs            # 主程序：逻辑、数据、窗口行为、自启动
│   ├── ui.xaml               # WPF 界面：资源、样式、控件树
│   └── app.manifest          # DPI 感知 + asInvoker 执行级别
├── assets/
│   └── app.ico               # 程序图标（多尺寸）
├── scripts/
│   ├── build.ps1             # 从源码编译 → bin\桌面备忘录.exe
│   ├── check-xaml.ps1        # 校验 ui.xaml（需 -STA）
│   ├── install.ps1           # 可选：复制到桌面并独立启动
│   └── make-icon.ps1         # 可选：重新生成 app.ico
├── tests/
│   ├── verify-toggle.ps1     # 打卡双向切换 + 数据同步验证
│   ├── verify-style.ps1      # 纯色按钮样式 / 触发器校验（需 -STA）
│   ├── uia-verify.ps1        # 控件矩形 / 名称 / 裁切检查
│   ├── verify2.ps1           # 窗口置顶 / 工具窗口 / 位置检查
│   └── wu.cs                 # verify2.ps1 的 Win32 P/Invoke 辅助类
└── docs/
    └── 使用说明.txt           # 面向使用者的中文说明
```

`bin\`（编译输出）与数据文件不入库，由 `.gitignore` 忽略。

---

## 8. 验证脚本

| 脚本 | 作用 | 前置条件 |
|---|---|---|
| `scripts\check-xaml.ps1` | 解析 `ui.xaml`，确认界面合法 | `-STA` |
| `scripts\build.ps1` | 从源码编译出单文件 exe | .NET Framework 4.x |
| `tests\verify-style.ps1` | 读真实 XAML，校验纯色按钮 / 触发器 / 共享颜色变量 | `-STA` |
| `tests\verify-toggle.ps1` | 通过 UI Automation 无鼠标点击，验证 4 次翻转中文案 / 图标 / 状态 / 数据同步 | 程序已运行，传 `-AppPid` |
| `tests\uia-verify.ps1` | 打印打卡相关控件的矩形 / 名称 / 是否裁切 | 程序展开态，传 `-AppPid` |
| `tests\verify2.ps1` | 枚举窗口，检查置顶 / 工具窗口 / 位置 | 程序已运行，传 `-TargetPath` |

调用模板统一为：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass [-STA] -File <脚本路径> [参数]
```

---

## 许可

可自由使用与修改。图标由 `scripts/make-icon.ps1` 生成。
