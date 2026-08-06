# MSFS 2024 飞行位置经 ADB 注入 Android 的完整技术方案

## 1. 项目目标

开发一个运行于 Windows 的桌面桥接程序，从《微软模拟飞行 2024》实时读取用户飞机经纬度，并通过 USB 调试执行 Android 的位置服务命令，使手机系统位置随模拟器中的飞机移动。

最终数据链路为：

```text
Microsoft Flight Simulator 2024
              │
              │ SimConnect
              ▼
Windows 桥接程序
              │
              │ adb shell cmd location
              │ USB 调试通道
              ▼
Android LocationManager 测试位置提供器
              │
              ▼
手机地图或其他位置应用
```

本方案不需要：

- 开发或安装 Android App；
- UDP、TCP 或局域网通信；
- 查询手机 IP 地址；
- 配置路由器；
- 开放 Windows 防火墙端口；
- 保持手机与电脑处于同一 Wi-Fi 网络。

MSFS 2024 的 SimConnect 支持进程外 C#/.NET 客户端，官方也建议优先使用进程外程序，以提高稳定性并简化调试。Android 的 ADB shell 可以调用 `cmd location` 添加、启用和更新测试位置提供器。

---

## 2. 最终技术选型

Windows 桥接程序建议采用：

| 项目 | 选型 |
|---|---|
| 开发语言 | C# |
| UI 框架 | WinForms |
| 运行时 | .NET Framework 4.8 |
| 编译平台 | x64 |
| 模拟器接口 | MSFS 2024 SimConnect 托管接口 |
| 手机通信 | Android SDK Platform-Tools 中的 `adb.exe` |
| 位置注入 | `adb shell cmd location providers ...` |
| 默认位置提供器 | `gps` |
| 默认注入频率 | 5 Hz |
| 默认精度字段 | 3 m |
| 日志 | 本地文本日志，按日期滚动 |
| 配置文件 | JSON |

选择 .NET Framework 4.8，而不是直接使用现代 .NET 8，主要是因为 MSFS 官方 SimConnect 托管包装器明确以 .NET Framework 为基础，程序集位于 SDK 的 `SimConnect SDK\lib\managed` 目录。官方示例要求注册数据结构、通过 Windows 消息接收 SimConnect 通知，并在项目中引用 `Microsoft.FlightSimulator.SimConnect`。

---

## 3. 能力范围与技术限制

### 3.1 可以实现的能力

系统可以持续向 Android 注入：

- 纬度；
- 经度；
- 水平位置精度；
- 当前系统时间。

手机上的普通地图、轨迹显示或位置测试应用通常可以接收到这些更新。

### 3.2 直接 ADB 方案不能完整注入的字段

当前 AOSP 的 `set-test-provider-location` shell 实现只处理：

```text
--location 纬度,经度
--accuracy 精度
--time Unix毫秒时间
```

虽然创建测试提供器时可以声明其支持高度、速度和航向，但 shell 命令本身没有向 `Location` 对象写入高度、速度、航向或垂直速度的选项。AOSP 源码还会自动设置 `elapsedRealtimeNanos`，并在未指定 `--time` 时使用手机当前系统时间。

因此，本方案第一版不能可靠向目标应用提供：

- `Location.getAltitude()`；
- `Location.getSpeed()`；
- `Location.getBearing()`；
- 原始 GNSS 卫星测量；
- NMEA 数据；
- GNSS 状态和卫星数量。

部分地图可能根据连续坐标自行估算速度和运动方向，但这属于目标应用行为，不能作为系统能力保证。

### 3.3 模拟位置标识无法消除

通过测试位置提供器注入的位置会被 Android 标记为模拟位置，应用可以通过 `Location.isMock()` 识别。目标应用如果主动拒绝模拟位置，本方案不能通过标准接口使其接受。

本方案适合：

- 地图位置显示；
- 航线跟踪；
- 飞行过程可视化；
- 自有应用的位置测试；
- Android 地图和定位功能开发测试。

不应将其用于规避考勤、地域限制、反作弊、身份认证或其他依赖真实地理位置的机制。

---

## 4. 开发与运行环境

### 4.1 Windows 端

建议环境：

```text
Windows 11
Visual Studio 2022
.NET Framework 4.8 Developer Pack
MSFS 2024
MSFS 2024 SDK
Android SDK Platform-Tools
```

在 MSFS 2024 中启用开发者模式并安装 SDK。SimConnect 托管程序集通常位于：

```text
$(MSFS2024_SDK)\SimConnect SDK\lib\managed\
Microsoft.FlightSimulator.SimConnect.dll
```

项目应当设置为：

```text
Target framework: .NET Framework 4.8
Platform target: x64
Prefer 32-bit: false
```

MSFS 官方文档允许使用 C# 编写进程外 SimConnect 客户端，并要求托管项目引用 SDK 中的 SimConnect 程序集。

### 4.2 Android 端

手机需要：

```text
Android 设备
已启用开发者选项
已启用 USB 调试
已授权当前电脑的 RSA 调试密钥
系统定位功能可用
```

ADB 是由电脑端客户端、电脑上的 ADB server 和手机端 `adbd` 组成的通信体系。USB 调试启用后，电脑可通过 ADB 在手机的 shell 环境中执行系统命令。Android 4.2.2 及以上设备首次连接时会要求用户确认电脑的 RSA 调试密钥。

---

## 5. 首次兼容性检测

不同 Android 版本和厂商 ROM 对 `cmd location` 的保留情况可能不同。因此，程序第一次运行时必须执行兼容性检测，不能直接假定命令可用。

### 5.1 检查 ADB

```powershell
adb version
adb start-server
adb devices -l
```

程序应当识别以下设备状态：

```text
device          已连接并授权
unauthorized    未在手机上确认调试授权
offline         ADB 通道异常
无设备           未连接或驱动异常
```

如果同时连接多台设备，用户必须选择设备序列号，后续所有命令使用：

```powershell
adb -s <SERIAL> ...
```

### 5.2 检查位置命令

```powershell
adb shell cmd location -h
```

输出中至少应当包含：

```text
set-location-enabled
providers add-test-provider
providers remove-test-provider
providers set-test-provider-enabled
providers set-test-provider-location
```

这些子命令在当前 AOSP `LocationShellCommand` 中由系统位置服务直接处理。

### 5.3 获取 shell UID

```powershell
adb shell id -u
```

多数正式 Android 设备返回：

```text
2000
```

程序不应硬编码该值，而应读取命令输出，并将结果保存为 `shellUid`。

### 5.4 手工可行性测试

正式开发前，应先执行一次手工测试：

```powershell
adb shell cmd location set-location-enabled true
adb shell appops set 2000 android:mock_location allow

adb shell cmd location providers remove-test-provider gps
adb shell cmd location providers add-test-provider gps --requiresSatellite
adb shell cmd location providers set-test-provider-enabled gps true

adb shell cmd location providers set-test-provider-location gps `
  --location 31.2304,121.4737 `
  --accuracy 3
```

其中 `remove-test-provider` 在测试提供器不存在时可能返回错误，初始清理阶段可以忽略该错误。

随后打开手机地图，检查位置是否移动至上海附近。也可使用：

```powershell
adb shell dumpsys location
```

查看位置服务状态。Google 的官方 Android 测试文档同样给出了通过 `appops` 授权 shell UID、添加测试提供器、启用提供器和注入经纬度的完整流程。

### 5.5 手工恢复

测试结束后执行：

```powershell
adb shell cmd location providers set-test-provider-enabled gps false
adb shell cmd location providers remove-test-provider gps
adb shell appops set 2000 android:mock_location default
```

测试提供器会替换同名的现有提供器；删除测试提供器后，原有真实提供器重新生效。

如果上述手工流程不能使地图位置发生变化，就不应继续开发桥接逻辑，而应先确认：

- ROM 是否删除了相关命令；
- shell UID 是否获得 `mock_location` AppOp；
- 目标应用是否只接受其他提供器；
- 目标应用是否拒绝模拟位置；
- 手机系统定位开关是否开启。

---

## 6. Windows 程序总体结构

建议项目结构如下：

```text
MSFSAndroidLocationBridge/
├─ Program.cs
├─ MainForm.cs
├─ Models/
│  ├─ AircraftState.cs
│  ├─ BridgeRuntimeState.cs
│  └─ DeviceInfo.cs
├─ Services/
│  ├─ SimConnectService.cs
│  ├─ AdbCommandService.cs
│  ├─ MockLocationService.cs
│  ├─ InjectionScheduler.cs
│  └─ RecoveryService.cs
├─ Configuration/
│  ├─ BridgeSettings.cs
│  └─ SettingsRepository.cs
├─ Diagnostics/
│  ├─ LogService.cs
│  └─ HealthMonitor.cs
└─ Scripts/
   └─ restore-location.ps1
```

模块职责如下。

### SimConnectService

负责：

- 连接 MSFS 2024；
- 注册 SimVars；
- 订阅模拟器状态事件；
- 接收飞机位置；
- 将数据写入最新状态缓存；
- 处理模拟器退出和重新连接。

### AdbCommandService

负责：

- 定位 `adb.exe`；
- 启动 ADB server；
- 枚举设备；
- 执行带设备序列号的命令；
- 处理超时、退出码、标准输出和标准错误；
- 识别 USB 断开。

### MockLocationService

负责：

- 保存手机原始位置开关和 AppOps 状态；
- 授予 shell 模拟位置权限；
- 添加并启用测试提供器；
- 注入坐标；
- 停止并删除测试提供器；
- 恢复原始设置。

### InjectionScheduler

负责：

- 将 SimConnect 高频数据降采样到 2～10 Hz；
- 保证同一时间只执行一条 ADB 注入命令；
- 丢弃过时数据；
- 不建立位置更新积压队列；
- 处理暂停、加载和坐标跳变。

### RecoveryService

负责：

- 程序正常退出时恢复真实定位；
- 程序启动时清理上次崩溃遗留的测试提供器；
- 生成独立恢复脚本；
- 记录尚未恢复的设备状态。

---

## 7. SimConnect 数据设计

### 7.1 读取字段

第一版建议读取：

| SimVar | 请求单位 | 用途 |
|---|---|---|
| `PLANE LATITUDE` | degrees | 注入纬度 |
| `PLANE LONGITUDE` | degrees | 注入经度 |
| `PLANE ALTITUDE` | feet | UI 显示和日志 |
| `GPS GROUND SPEED` | meters per second | UI 显示和跳变判断 |
| `GPS GROUND TRUE TRACK` | degrees | UI 显示 |
| `SIM ON GROUND` | bool | 状态显示 |
| `SIMULATION RATE` | number | 诊断模拟倍率 |

MSFS 文档中 `PLANE LATITUDE` 和 `PLANE LONGITUDE` 的原始单位为弧度，但 SimConnect 数据定义可以直接请求 `degrees`；官方示例也使用该请求方式。`GPS GROUND SPEED` 使用米每秒，`GPS GROUND TRUE TRACK` 表示相对于真北的地面航迹。

### 7.2 数据结构

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct AircraftState
{
    public double LatitudeDeg;
    public double LongitudeDeg;
    public double AltitudeFt;
    public double GroundSpeedMps;
    public double GroundTrackDeg;
    public int OnGround;
    public double SimulationRate;
}
```

结构体字段顺序必须与 `AddToDataDefinition()` 调用顺序完全一致，并且必须执行：

```csharp
simConnect.RegisterDataDefineStruct<AircraftState>(
    (uint)DefinitionId.AircraftState);
```

否则托管包装器无法将 `dwData` 正确封送为目标结构体。

### 7.3 数据定义

```csharp
simConnect.AddToDataDefinition(
    (uint)DefinitionId.AircraftState,
    "PLANE LATITUDE",
    "degrees",
    SIMCONNECT_DATATYPE.FLOAT64,
    0,
    SimConnect.SIMCONNECT_UNUSED);

simConnect.AddToDataDefinition(
    (uint)DefinitionId.AircraftState,
    "PLANE LONGITUDE",
    "degrees",
    SIMCONNECT_DATATYPE.FLOAT64,
    0,
    SimConnect.SIMCONNECT_UNUSED);

simConnect.AddToDataDefinition(
    (uint)DefinitionId.AircraftState,
    "PLANE ALTITUDE",
    "feet",
    SIMCONNECT_DATATYPE.FLOAT64,
    0,
    SimConnect.SIMCONNECT_UNUSED);

simConnect.AddToDataDefinition(
    (uint)DefinitionId.AircraftState,
    "GPS GROUND SPEED",
    "meters per second",
    SIMCONNECT_DATATYPE.FLOAT64,
    0,
    SimConnect.SIMCONNECT_UNUSED);

simConnect.AddToDataDefinition(
    (uint)DefinitionId.AircraftState,
    "GPS GROUND TRUE TRACK",
    "degrees",
    SIMCONNECT_DATATYPE.FLOAT64,
    0,
    SimConnect.SIMCONNECT_UNUSED);

simConnect.AddToDataDefinition(
    (uint)DefinitionId.AircraftState,
    "SIM ON GROUND",
    "bool",
    SIMCONNECT_DATATYPE.INT32,
    0,
    SimConnect.SIMCONNECT_UNUSED);

simConnect.AddToDataDefinition(
    (uint)DefinitionId.AircraftState,
    "SIMULATION RATE",
    "number",
    SIMCONNECT_DATATYPE.FLOAT64,
    0,
    SimConnect.SIMCONNECT_UNUSED);

simConnect.RegisterDataDefineStruct<AircraftState>(
    (uint)DefinitionId.AircraftState);
```

### 7.4 请求频率

可按模拟帧读取：

```csharp
simConnect.RequestDataOnSimObject(
    (uint)RequestId.AircraftState,
    (uint)DefinitionId.AircraftState,
    SimConnect.SIMCONNECT_OBJECT_ID_USER,
    SIMCONNECT_PERIOD.SIM_FRAME,
    SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
    0,
    0,
    0);
```

SimConnect 回调只负责更新内存中的“最新飞机状态”，不能直接调用 ADB。网络、进程创建或等待手机响应都不应当阻塞 SimConnect 消息处理。

SimConnect 当前不是线程安全接口，因此所有 SimConnect 方法和消息接收应集中在创建连接的 UI/message 线程；其他线程只读取复制后的飞机状态快照。

### 7.5 模拟器运行状态

建议订阅：

```text
Sim
Pause_EX1
PositionChanged
AircraftLoaded
FlightLoaded
```

其中 `Sim` 会返回当前模拟是否处于运行状态；`Pause_EX1` 返回更详细的暂停状态；`PositionChanged` 可用于识别用户通过界面重定位飞机。官方文档还指出，加载或重置航班时可能出现额外的 `SimStart`/`SimStop` 事件，因此不应仅凭一次事件切换运行状态。

程序应当采用以下规则：

```text
Sim = 0：
    停止发送新位置，但保留测试提供器。

Sim = 1 且收到连续三个有效位置：
    开始注入。

暂停：
    将注入频率降低到 1 Hz，继续更新时间戳和最后坐标。

PositionChanged：
    允许下一坐标发生大范围跳变，不进行平滑插值。
```

---

## 8. 飞机位置缓存与有效性判断

SimConnect 回调收到数据后，应执行以下检查：

```text
纬度是有限数值；
经度是有限数值；
纬度位于 [-90, 90]；
经度位于 [-180, 180]；
数据到达时间距当前时间小于 1 秒；
模拟器处于运行状态；
当前位置已连续稳定出现至少三次。
```

不应无条件拒绝 `(0,0)`，因为该坐标在地理上有效。是否属于加载期无效值，应结合模拟运行状态、连续样本和前后位置判断。

建议保存：

```csharp
public sealed class AircraftSnapshot
{
    public long Sequence { get; init; }
    public DateTime ReceivedUtc { get; init; }
    public AircraftState State { get; init; }
    public bool SimRunning { get; init; }
    public bool Paused { get; init; }
    public bool PositionChanged { get; init; }
}
```

每次收到新位置时直接替换旧快照。注入线程只读取当前最新快照，不处理历史队列。

---

## 9. ADB 位置提供器初始化

### 9.1 保存原始状态

桥接程序启动注入前应记录：

```powershell
adb shell cmd location is-location-enabled
adb shell appops get <SHELL_UID> android:mock_location
```

保存内容包括：

```text
原始系统定位开关状态；
原始 mock_location AppOp 状态；
目标设备序列号；
测试提供器名称；
程序启动时间。
```

这些信息写入：

```text
runtime-recovery.json
```

只有在正常清理完成后才删除该文件。

### 9.2 清理上一次遗留状态

```powershell
adb shell cmd location providers set-test-provider-enabled gps false
adb shell cmd location providers remove-test-provider gps
```

错误可以记录但不终止初始化，因为测试提供器可能本来就不存在。

### 9.3 开启位置和授权

```powershell
adb shell cmd location set-location-enabled true
adb shell appops set <SHELL_UID> android:mock_location allow
```

AOSP 的命令帮助明确说明，添加测试提供器要求 `MOCK_LOCATION` 权限，可通过对相应 UID 执行 `appops set ... android:mock_location allow` 开启。

### 9.4 创建测试提供器

默认使用：

```powershell
adb shell cmd location providers add-test-provider gps `
  --requiresSatellite
```

然后启用：

```powershell
adb shell cmd location providers set-test-provider-enabled gps true
```

使用 `gps` 名称会暂时以测试提供器替换真实 GPS provider。Android API 文档明确说明，同名测试提供器会替换此前存在的提供器。

### 9.5 提供器兼容模式

程序可提供以下模式：

```text
GPS 模式：
    provider = gps

Fused 模式：
    provider = fused

自定义模式：
    用户手动输入 provider 名称
```

默认只启用 `gps`。如果目标应用不能收到位置，可测试 `fused`。Google 官方示例将 `gps`、`network`、`fused` 和 `passive` 均列为可使用的标准 provider 名称，但不同手机和 Google Play 服务版本的行为并不完全一致。

第一版不建议同时向多个 provider 注入，因为这会增加 ADB 调用次数，并可能导致目标应用收到重复或相互竞争的位置。

---

## 10. 实时位置注入

### 10.1 注入命令

每个位置更新执行：

```powershell
adb -s <SERIAL> shell cmd location providers `
  set-test-provider-location gps `
  --location <LATITUDE>,<LONGITUDE> `
  --accuracy 3
```

例如：

```powershell
adb -s 8d31f82a shell cmd location providers `
  set-test-provider-location gps `
  --location 35.5522580,139.7796940 `
  --accuracy 3
```

不建议显式传递：

```text
--time
```

因为 AOSP 在未指定时间时自动使用手机当前系统时间，并同时填写单调时钟字段。这样可以避免电脑与手机时钟不同步，也避免将 MSFS 模拟时间误写为 Android 位置时间。

### 10.2 数值格式

所有数字必须使用：

```csharp
CultureInfo.InvariantCulture
```

经纬度建议输出 7 位小数：

```csharp
latitude.ToString("F7", CultureInfo.InvariantCulture)
longitude.ToString("F7", CultureInfo.InvariantCulture)
```

否则中文 Windows 区域设置可能使用逗号作为小数分隔符，与 `纬度,经度` 的参数格式发生冲突。

### 10.3 注入频率

建议提供：

```text
2 Hz：兼容模式
5 Hz：默认模式
10 Hz：高频实验模式
```

第一版默认采用 5 Hz，即每 200 ms 注入一次。手机地图通常不需要模拟器帧率级别的位置更新。

注入调度应满足：

```text
任何时刻最多一条 ADB 命令正在执行；
如果上一条命令尚未结束，跳过当前周期；
不排队保存旧位置；
下一次始终发送最新坐标；
单条命令默认超时 1000 ms；
连续三次失败后重建 ADB 通道。
```

### 10.4 ADB 执行策略

#### MVP 方案：每次启动独立 adb.exe

实现最简单：

```text
每 200～500 ms
    启动 adb.exe
    执行一条位置命令
    等待退出
```

优点是错误边界清晰、实现简单。缺点是频繁创建 Windows 进程，建议将频率限制在 2～5 Hz。

#### 正式方案：长期保持 adb shell

程序只启动一次：

```powershell
adb -s <SERIAL> shell
```

随后持续向标准输入写入：

```text
cmd location providers set-test-provider-location gps --location ... --accuracy 3
```

每条命令末尾附加唯一确认标记：

```text
; echo __MSFS_ACK_18452__$?
```

程序异步读取输出并等待对应确认标记。如果超过 1000 ms 未收到确认：

```text
终止当前 adb shell；
重新检查设备；
重新建立 shell；
重新初始化测试提供器；
发送最新坐标。
```

正式版本推荐长期 shell，但第一阶段应先用独立进程方式验证完整链路。

---

## 11. 注入调度算法

核心调度逻辑为：

```text
SimConnect 持续更新 latestSnapshot

每 200 ms：
    如果尚未开始注入：
        返回

    如果 ADB 命令正在执行：
        丢弃本周期

    如果设备未连接：
        进入 Reconnecting 状态

    如果模拟器未运行：
        不注入

    如果最新数据超过 1 秒：
        不注入

    如果坐标无效：
        不注入

    如果属于异常跳变且没有 PositionChanged 标记：
        等待下一帧确认

    执行位置注入
```

该算法属于“最新值覆盖”模型。位置数据具有实时性，旧坐标在新坐标到达后已经失去传输价值，因此不应使用普通 FIFO 队列。

---

## 12. 坐标跳变处理

以下操作会产生位置瞬移：

- 在世界地图中重新选择机场；
- 使用 slew 模式；
- 加载保存的航班；
- 重置航班；
- 切换活动或任务；
- 使用开发者工具改变飞机位置。

建议计算相邻两次位置之间的大圆距离。如果位置变化远大于飞机当前地速在相应时间内可能移动的距离，应当标记为跳变。

处理逻辑：

```text
收到 PositionChanged 事件：
    接受下一坐标；
    清除历史速度和连续性判断；
    立即注入新位置。

未收到 PositionChanged，但出现异常大跳变：
    暂不注入；
    等待下一次 SimConnect 样本；
    如果第二个样本与新位置一致，则确认跳变并注入；
    如果第二个样本恢复旧区域，则判定为瞬时异常值。
```

不建议在旧位置和新位置之间进行插值。重新定位属于真实的模拟器状态变化，插值反而会在手机上产生一条不存在的高速路径。

---

## 13. 程序运行状态机

建议使用以下状态：

```text
Idle
CheckingEnvironment
WaitingForDevice
WaitingForSimulator
PreparingMockProvider
Running
Paused
ReconnectingAdb
RestoringLocation
Faulted
```

典型转换：

```text
Idle
  → CheckingEnvironment
  → WaitingForDevice
  → WaitingForSimulator
  → PreparingMockProvider
  → Running
```

异常转换：

```text
Running
  → ReconnectingAdb
  → PreparingMockProvider
  → Running
```

停止转换：

```text
Running
  → RestoringLocation
  → Idle
```

任何不可恢复错误进入：

```text
Faulted
```

但即使进入 `Faulted`，程序仍应尝试执行测试提供器清理。

---

## 14. 正常停止与恢复真实位置

停止顺序应固定为：

```text
1. 停止注入定时器；
2. 等待当前 ADB 命令完成或超时；
3. 禁用测试提供器；
4. 删除测试提供器；
5. 恢复 shell UID 原始 AppOp；
6. 恢复系统定位开关原始状态；
7. 删除 runtime-recovery.json；
8. 断开 SimConnect；
9. 关闭程序。
```

对应命令：

```powershell
adb shell cmd location providers set-test-provider-enabled gps false
adb shell cmd location providers remove-test-provider gps
```

如果程序启动前 `mock_location` 为默认状态，则恢复：

```powershell
adb shell appops set <SHELL_UID> android:mock_location default
```

如果启动前系统定位关闭，则恢复：

```powershell
adb shell cmd location set-location-enabled false
```

如果启动前系统定位已经开启，则保持开启。

---

## 15. 崩溃和 USB 断开处理

### 15.1 正常软件异常

程序应在以下事件中调用统一恢复函数：

```text
FormClosing
Application.ApplicationExit
AppDomain.ProcessExit
AppDomain.UnhandledException
TaskScheduler.UnobservedTaskException
```

恢复函数必须设计为幂等操作，即重复执行不会产生额外损害。

### 15.2 USB 突然断开

USB 断开时，电脑已无法向手机发送清理命令。因此，纯 ADB 方案存在一个无法完全消除的边界：

> 在数据线突然断开、电脑关机或进程被强制终止时，测试位置提供器可能暂时保留在手机中。

程序应当采取以下补偿措施：

- 下次连接设备时自动检查并删除遗留测试提供器；
- 在主界面提供“仅恢复手机真实定位”按钮；
- 随程序附带独立恢复脚本；
- 在状态栏明确显示“手机仍处于模拟位置状态”；
- 正常退出时强制等待恢复流程完成。

独立恢复脚本：

```powershell
param(
    [string]$Serial = "",
    [string]$Provider = "gps"
)

$adbArgs = @()

if ($Serial -ne "") {
    $adbArgs += "-s"
    $adbArgs += $Serial
}

& adb @adbArgs shell cmd location providers `
    set-test-provider-enabled $Provider false

& adb @adbArgs shell cmd location providers `
    remove-test-provider $Provider

$uid = (& adb @adbArgs shell id -u).Trim()

& adb @adbArgs shell appops set `
    $uid android:mock_location default
```

如果手机长期停留在最后一次模拟位置，可重新连接 USB 并运行该脚本；手机重启通常也会清除当前测试 provider 状态，但软件不应依赖重启作为正常恢复机制。

---

## 16. 用户界面

主界面建议显示：

```text
MSFS 连接：已连接 / 未连接
Android 设备：型号、序列号、Android 版本
ADB 状态：正常 / 未授权 / 离线
位置命令支持：支持 / 不支持
测试提供器：gps / fused
模拟器状态：运行 / 暂停 / 加载
当前纬度
当前经度
高度
地速
真航迹
模拟倍率
注入频率
最近一次注入耗时
累计成功次数
累计失败次数
```

主要按钮：

```text
[检测环境]
[连接并开始注入]
[停止并恢复真实定位]
[仅恢复手机定位]
[打开日志目录]
```

设置项：

```text
ADB 路径
目标设备序列号
位置提供器
注入频率
位置精度
是否随 MSFS 自动连接
是否在暂停时继续刷新
是否自动重连设备
是否退出时恢复原始定位开关
```

不建议将“开始注入”和“恢复定位”隐藏在菜单中，因为位置提供器的生命周期必须让用户明确可见。

---

## 17. 配置文件

示例：

```json
{
  "adbPath": "C:\\Android\\platform-tools\\adb.exe",
  "deviceSerial": "",
  "provider": "gps",
  "injectionFrequencyHz": 5,
  "accuracyMeters": 3.0,
  "adbCommandTimeoutMs": 1000,
  "simConnectReconnectIntervalMs": 2000,
  "adbReconnectIntervalMs": 2000,
  "refreshWhilePaused": true,
  "pausedFrequencyHz": 1,
  "restoreLocationSwitchOnExit": true,
  "autoCleanupStaleProvider": true
}
```

不应把 shell UID 写死在配置中；每次连接设备时重新读取。

---

## 18. 日志设计

日志至少记录：

```text
程序启动和版本
MSFS 连接和断开
设备枚举结果
设备授权状态
Android SDK/API 级别
cmd location 能力检测
原始系统定位状态
原始 mock_location AppOp
测试提供器创建、启用和删除
每次错误的 ADB 命令、退出码和错误输出
SimConnect 异常
坐标跳变
USB 断开和重连
清理是否成功
```

正常位置更新不应每次都写入普通日志，否则 5 Hz 长时间运行会产生大量文件。可以：

```text
Debug 日志：记录每次坐标；
Info 日志：每 10 秒汇总一次；
Error 日志：记录失败命令。
```

---

## 19. 测试方案

### 阶段一：Android 手工注入测试

验证：

- `cmd location` 是否存在；
- shell UID 是否可获得 mock AppOp；
- `gps` 测试提供器是否能创建；
- 手机地图是否显示指定位置；
- 删除 provider 后真实位置是否恢复。

通过标准：

```text
手工注入后 5 秒内地图移动到指定坐标；
删除测试提供器后恢复真实定位；
手机重启后无遗留异常。
```

### 阶段二：SimConnect 独立测试

暂不调用 ADB，只将数据打印到界面。

验证：

- 纬度和经度与模拟器一致；
- 单位为度而非弧度；
- 飞机移动时数据连续；
- 切换飞机后自动恢复；
- 返回主菜单时停止有效数据；
- 模拟器关闭后程序不崩溃。

MSFS SDK 自带的数据请求示例会读取用户飞机的纬度、经度和高度，可作为实现参考。

### 阶段三：低频端到端测试

采用：

```text
2 Hz
独立 adb.exe 进程
gps provider
```

验证：

- 起飞后手机位置随飞机移动；
- 地图没有长时间停滞；
- 停止按钮能恢复真实位置；
- 运行 30 分钟无进程积压。

### 阶段四：正式频率测试

切换至：

```text
5 Hz
长期 adb shell
```

记录：

- 平均命令响应时间；
- 95% 响应时间；
- 连续失败次数；
- CPU 占用；
- ADB shell 是否意外退出；
- 手机锁屏后的更新行为。

### 阶段五：异常测试

必须覆盖：

```text
飞行中拔掉 USB；
飞行中关闭 USB 调试；
手机锁屏；
手机重启；
ADB server 重启；
程序被任务管理器强制终止；
MSFS 崩溃；
MSFS 返回主菜单；
重新加载航班；
模拟暂停和活动暂停；
使用 slew 移动飞机；
同时连接两台 Android 设备；
目标手机显示 unauthorized；
系统定位开关原本关闭。
```

---

## 20. 验收标准

第一版可按以下标准验收：

| 指标 | 目标 |
|---|---|
| SimConnect 连接 | MSFS 进入飞行后可自动连接 |
| 坐标读取 | 纬度、经度与模拟器状态一致 |
| 注入频率 | 稳定达到 5 Hz |
| 端到端延迟 | 正常情况下小于 500 ms |
| 连续运行 | 2 小时无命令积压、无明显内存增长 |
| 暂停处理 | 暂停时位置保持，恢复后继续更新 |
| 重新定位 | 可在确认后跳转至新位置 |
| USB 重连 | 重连后可自动恢复注入 |
| 正常停止 | 删除测试提供器并恢复真实定位 |
| 日志 | 所有关键状态和错误可追溯 |

“第三方目标应用接受模拟位置”只能作为兼容性测试结果，不能作为桥接程序本身必然满足的验收条件，因为目标应用可以主动拒绝 `isMock=true` 的位置。

---

## 21. 分阶段实施顺序

### 第一阶段：概念验证

完成：

```text
手工 ADB 注入；
SimConnect 读取经纬度；
每 500 ms 启动一次 adb.exe 注入；
停止时手工清理。
```

该阶段目标是证明目标手机和目标应用能够接受该位置。

### 第二阶段：可用版本

增加：

```text
WinForms 界面；
设备自动检测；
位置提供器自动初始化；
2～5 Hz 注入；
正常退出自动恢复；
基础日志；
SimConnect 自动重连。
```

### 第三阶段：稳定版本

增加：

```text
长期 adb shell；
命令确认和超时机制；
USB 自动重连；
运行状态机；
崩溃恢复文件；
独立恢复脚本；
坐标跳变确认；
多设备选择。
```

### 第四阶段：扩展版本

如果后续确认目标应用必须使用高度、速度或航向，应当停止继续扩展 shell 命令方案，改为开发 Android 模拟位置 App，通过 `LocationManager.setTestProviderLocation()` 构造完整 `Location` 对象。Android API 本身支持向测试位置写入完整字段，而当前 `cmd location` shell 包装只开放了其中一部分。

---

## 22. 最终推荐实施参数

建议初始版本采用：

```text
Windows：C# WinForms
框架：.NET Framework 4.8
架构：x64
SimConnect：MSFS 2024 官方托管包装器
Android 连接：USB ADB
位置提供器：gps
SimConnect 读取频率：SIM_FRAME
Android 注入频率：2 Hz 起步，稳定后提高至 5 Hz
ADB 实现：第一版每次独立进程
位置精度：3 m
位置时间：由 Android 自动生成
暂停注入频率：1 Hz
退出行为：强制删除测试提供器
```

该设计是当前约束下实现成本最低、链路最短且便于调试的方案。其主要边界是无法通过标准 `cmd location` shell 命令注入高度、速度和航向，也无法保证主动拒绝模拟位置的第三方应用接受数据。在只要求手机地图位置跟随 MSFS 飞机的条件下，不需要开发 Android App，也不需要引入 UDP 或 TCP 通信层。