# 项目进度文件 — MSFS 2024 飞行位置经 ADB 注入 Android

> 本文件是项目的唯一进度权威记录。任何一次开发中断后，恢复开发的流程为：
> 1. 阅读本文件「当前状态」；
> 2. 找到状态为「进行中」的阶段，检查其任务清单中未勾选项；
> 3. 从第一个未完成任务继续。
> 每个阶段结束时更新本文件并提交至 GitHub。

## 0. 项目概述

从《微软模拟飞行 2024》实时读取飞机经纬度，经 Windows 桥接程序通过 ADB 注入 Android 测试位置提供器，使手机系统位置随飞机移动。

```text
MSFS 2024 → SimConnect → Windows 桥接程序 → adb shell cmd location → Android LocationManager → 手机地图应用
```

完整技术方案见 `MSFS 2024 飞行位置经 ADB 注入 Android 的完整技术方案.md`（下称「方案」）。

**核心边界**（方案 §3）：`cmd location` shell 命令只能注入经纬度、精度、时间；不能注入高度、速度、航向；模拟位置标记 `isMock=true` 无法消除。

## 1. 环境信息（2026-08 记录）

> **安装约定（2026-08 用户指令，长期生效）**：此后所有软件一律安装到 `E:\Laptop\softwares` 下（如 adb 位于 `E:\Laptop\softwares\AndroidSdk`）。已安装的 MSFS 2024 SDK（`C:\MSFS SDK`）维持现状不动。
>
> **临时文件约定（2026-08 用户指令，长期生效）**：TEMP 等临时文件统一创建在项目工作目录下：`<项目根>/.tmp`（已加入 .gitignore，不提交）。

| 项 | 状态 | 值/路径 |
|---|---|---|
| 项目目录 | 就绪 | `E:/Projects/Pi/MSFS GPS TO ANDROID` |
| git | 已初始化 | 本仓库 |
| GitHub CLI | 可用 | 已登录 `HaowenCang`，scopes: repo |
| GitHub 远程仓库 | **已创建（公开）** | `https://github.com/HaowenCang/msfs-adb-location-bridge`，初始提交已推送 |
| dotnet SDK | 已有 | 8.0.422（另有 9.0 runtime） |
| Visual Studio 2022 | **已安装**（17.14.37516.0） | 路径 `E:\Laptop\softwares\Microsoft Visual Studio 2022`（用户安装时实际目录名含 "Microsoft " 前缀）；**.NET Framework 4.8 SDK 组件待补装**（参考程序集目录仅有 v4.7.2；命令行静默补装受阻：`--quiet` 需从提权进程启动，Exit 5007/87） |
| .NET Framework 4.8 Developer Pack | 随 VS 安装 | VS 组件 `Microsoft.Net.Component.4.8.SDK`；4.8 运行时 Windows 11 自带 |
| MSFS 2024 游戏 | 已确认 | `E:\XboxGames\Microsoft Flight Simulator 2024`（Xbox 版） |
| MSFS 2024 SDK | **已安装** | `C:\MSFS SDK`（0.24.5，实际路径无 "2024" 后缀）；托管 dll：`C:\MSFS SDK\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll` |
| adb | 已有 | `E:/Laptop/softwares/AndroidSdk/platform-tools/adb.exe`（37.0.0） |
| Android 设备 | 已连接并授权 | 序列号 `c3a3ea64`，型号 `25019PNF3C`，shell UID `2000` |

## 2. 阶段总览

| 阶段 | 名称 | 状态 | 对应方案章节 |
|---|---|---|---|
| 阶段 0 | 环境准备与手工可行性验证 | 未开始 | §4、§5.4、§5.5、§19 阶段一 |
| 阶段 1 | 概念验证：SimConnect 读取 + 独立 adb 注入 | 未开始 | §21 阶段一、§7 |
| 阶段 2 | 可用版本：WinForms 界面 + 自动恢复 + 配置日志 | 未开始 | §21 阶段二、§6、§14、§16、§17、§18 |
| 阶段 3 | 稳定版本：长期 shell + 崩溃恢复 + 状态机 | 未开始 | §21 阶段三、§10.4、§12、§13、§15 |
| 阶段 4 | 扩展版本：Android App 完整 Location | 未开始（仅按需启动） | §21 阶段四 |

**规则**：每个阶段完成后，更新本文件 → git commit → push 至 GitHub → 打 tag（`v0.1.0` 格式随阶段递增）。

## 3. 阶段明细与任务清单

### 阶段 0：环境准备与手工可行性验证

**目标**：证明目标手机与目标应用能够接受 `cmd location` 注入的位置；补齐开发工具链。

任务清单：

- [x] 与用户确认开发环境选型（D2 已定：A，VS 2022 Community）
- [x] 确认 MSFS 2024 游戏安装位置（D3：`E:\XboxGames\Microsoft Flight Simulator 2024`）
- [x] 确认并安装 MSFS 2024 SDK（D3：`C:\MSFS SDK` 0.24.5，SimConnect 托管 dll 已验证）
- [x] 建立 GitHub 远程仓库（D1：`msfs-adb-location-bridge` 公开仓库），初始提交已推送
- [x] 手工可行性测试（D4：用户已完成）
- [x] 执行方案 §5.5 手工恢复（2026-08-06：provider 无残留、AppOp 已恢复 default、定位开关当前 true）
- [x] 记录设备 shell UID = 2000
- [x] 安装 Visual Studio 2022 Community（2026-08-06 用户手动完成，17.14.37516.0 → `E:\Laptop\softwares\Microsoft Visual Studio 2022`）
- [ ] 补装 .NET Framework 4.8 SDK 组件（用户操作：VS Installer → 修改 → 单个组件 → ".NET Framework 4.8 SDK"；或管理员 PowerShell 执行 `setup.exe modify --installPath "E:\Laptop\softwares\Microsoft Visual Studio 2022" --add Microsoft.Net.Component.4.8.SDK --quiet --wait --norestart`）
- [ ] VS 补装完成后验证：net48 targeting pack（`C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8`）存在

验收标准（方案 §19 阶段一）：手工注入后 5 秒内地图移动到指定坐标；删除测试提供器后恢复真实定位；无遗留异常。

完成后 git：commit `阶段 0 完成：手工可行性验证` → tag `v0.0.1` → push。

### 阶段 1：概念验证 — SimConnect 读取 + 独立 adb 注入

**目标**：跑通「MSFS → 桥接程序 → ADB → 手机地图」全链路，频率 2 Hz，独立 adb 进程方式（方案 §10.4 MVP）。

任务清单：

- [ ] 创建解决方案骨架（按方案 §6 结构，先以控制台/最小 WinForms 承载）
- [ ] SimConnectService：连接 MSFS 2024、注册方案 §7.1 字段、`RequestDataOnSimObject(SIM_FRAME)`
- [ ] 验证读取正确性：经纬度单位为度、随飞机移动连续更新、返回主菜单后数据停止
- [ ] AdbCommandService：定位 adb.exe（本机路径已知）、`start-server`、`devices -l`、带序列号执行命令
- [ ] MockLocationService：保存原始状态 → 授权 → 建 provider → 注入（§9 流程）
- [ ] InjectionScheduler：每 500 ms 启动一次 adb.exe 注入（§10.4 MVP），单命令超时 1 s
- [ ] 停止时手工清理（§5.5 / §14 流程手动执行）
- [ ] 与用户联调：MSFS 起飞后手机地图跟随（2 Hz）

验收标准：手机地图随飞机移动；无进程积压；停止后真实定位恢复。

完成后 git：commit → tag `v0.1.0` → push。

### 阶段 2：可用版本 — WinForms 界面 + 自动恢复 + 配置日志

**目标**：形成可用产品形态，正常使用无需手工敲 ADB 命令。

任务清单：

- [ ] WinForms 主界面（方案 §16）：状态显示区、当前飞行数据区、统计区
- [ ] 按钮：检测环境 / 连接并开始注入 / 停止并恢复真实定位 / 打开日志目录（§16）
- [ ] 设备自动检测、多设备选择、unauthorized/offline 状态识别（§5.1）
- [ ] 环境检测：`cmd location -h` 子命令检查（§5.2）、shell UID 动态读取（§5.3，不硬编码）
- [ ] InjectionScheduler 升级为 2~5 Hz 可配置（§10.3、§11 最新值覆盖模型，跳过不排队）
- [ ] 正常退出自动恢复固定顺序（§14 九步）
- [ ] 配置文件 JSON（§17 全部字段）
- [ ] 日志：Info 每 10 秒汇总、Error 记录失败命令、Debug 记录每次坐标（§18）
- [ ] SimConnect 自动重连（方案 §21 阶段二）
- [ ] 暂停处理：注入降频至 1 Hz 并继续更新时间戳（§7.5）

验收标准：方案 §20 表格全部达标（2 小时连续运行、500 ms 端到端延迟、暂停/重连行为正确）。

完成后 git：commit → tag `v0.2.0` → push。

### 阶段 3：稳定版本 — 长期 shell + 崩溃恢复 + 状态机

**目标**：抗异常、可恢复，断电/拔线/崩溃后不遗留模拟位置。

任务清单：

- [ ] 长期 adb shell + 唯一确认标记 `; echo __MSFS_ACK_<N>__$?`（§10.4 正式方案）
- [ ] 超时（1 s）未确认 → 终止 shell → 重建通道 → 重新初始化 provider → 发最新坐标
- [ ] 运行状态机（§13）：Idle → CheckingEnvironment → WaitingForDevice → WaitingForSimulator → PreparingMockProvider → Running / Paused / ReconnectingAdb / RestoringLocation / Faulted
- [ ] 崩溃恢复：`runtime-recovery.json` 记录原始状态（§9.1），启动时清理遗留（§9.2）
- [ ] 独立恢复脚本 `restore-location.ps1`（§15.2），主界面「仅恢复手机定位」按钮
- [ ] USB 断开检测与自动重连（§15.2）
- [ ] 坐标跳变检测与二次确认（§12）
- [ ] 异常事件统一恢复函数：FormClosing / ApplicationExit / ProcessExit / UnhandledException / UnobservedTaskException（§15.1）

验收标准：方案 §19 阶段五异常测试清单全部通过（拔线、重启、强制终止、双设备等）。

完成后 git：commit → tag `v0.3.0` → push。

### 阶段 4：扩展版本 — Android App 完整 Location（按需启动）

**触发条件**：确认目标应用必须使用高度/速度/航向（方案 §3.2 列出的字段），且无法通过 shell 命令解决。

**内容**：开发 Android 模拟位置 App，通过 `LocationManager.setTestProviderLocation()` 构造完整 `Location` 对象（方案 §21 阶段四）。

**当前状态**：不实施。

## 4. 待用户决策事项（决策记录）

| # | 日期 | 事项 | 选项 | 用户决定 |
|---|---|---|---|---|
| D1 | 2026-08 | 仓库名称 | C: `msfs-adb-location-bridge` | **已定：C**，公开仓库已创建并推送初始提交 |
| D2 | 2026-08 | 开发环境选型 | A: 安装 VS 2022（Community）+ .NET Framework 4.8 Dev Pack；B: dotnet CLI + ReferenceAssemblies；C: 改用 .NET 8 | **已定：A**（Community），由用户手动安装，安装包已缓存 |
| D3 | 2026-08 | MSFS 2024 SDK 位置 | 游戏位于 `E:\XboxGames\Microsoft Flight Simulator 2024`（Xbox 版，未含 SDK） | **已定**：自动下载安装完成 → `C:\MSFS SDK` 0.24.5 |
| D4 | 2026-08 | 手工测试时机 | 手工测试由用户自行完成 | **已定**：不再执行；§5.5 恢复已执行（2026-08-06，provider 无残留、AppOp 恢复 default、定位开关当前为 true） |
| D5 | 2026-08 | GitHub 上传粒度 | 按用户要求：每阶段结束上传（含阶段 0 前的初始提交上传） | 已定：按阶段上传 |

## 5. 提交与版本历史

| 日期 | commit | tag | 说明 |
|---|---|---|---|
| 2026-08 | `a3ab9a5`（已推送 origin/main） | — | 初始提交：技术方案 + 本进度文件 |
| 2026-08 | （本次提交） | — | 阶段 0 进度更新：仓库创建、SDK 安装、§5.5 恢复完成、VS 安装移交用户 |
