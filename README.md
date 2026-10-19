# SiameseCat

面向 PLC 和工业设备的轻量级 .NET 通讯库，目标框架为 **.NET 8 (LTS)**。

按**分层**拆分程序集（4 层 + 1 个可选的宿主集成包，共 5 个包），一层一个包，层内再按协议/品牌分组：

| 包 | 层 | 职责 | 依赖 |
| --- | --- | --- | --- |
| `SiameseCat.Core` | 契约层 | 驱动契约、地址/值模型、`DriverBase`、注册表、日志抽象与指标、`ITransport` 抽象 | 无 |
| `SiameseCat.Transport` | 传输层 | TCP / UDP / 串口传输实现 | Core、`System.IO.Ports` |
| `SiameseCat.Protocols` | 协议层 | Modbus（RTU/TCP）与 Siemens S7 的帧、客户端，以及通用 Modbus 驱动 | Core、Transport |
| `SiameseCat.Drivers` | 品牌层 | Siemens / Inovance 品牌驱动（地址映射）+ 注册入口 | Core、Protocols |
| `SiameseCat.Hosting` | 宿主集成（可选） | `Microsoft.Extensions.Logging` 适配器与 `IServiceCollection` 注册 | Core、Drivers、`Microsoft.Extensions.*` |

协议栈自行实现，不依赖 NModbus、Sharp7 等第三方协议库；只有传输层需要微软官方的 `System.IO.Ports`。

## 架构：可插拔驱动

```text
应用
 │  只依赖 Driver 契约（Core），不依赖品牌或协议
 ▼
SiameseCat.Core（契约层）
 ├── IPlcDriver / IReadable / IWritable / IBatchReadable / IBatchWritable / ISubscribable / IHealthCheckable
 ├── DriverBase        状态机 / 事务锁 / 超时 / 重试 / 重连 / 日志 / 指标（5 个 partial）
 ├── PlcAddress / PlcValue / PlcTag / IPlcAddressParser
 ├── ITransport        字节收发抽象
 └── DriverRegistry    驱动发现与创建
        ▲                                        ▲
        │ 品牌层（地址映射 + 工厂）               │ 协议层（帧 / 客户端 / 通用驱动）
 SiameseCat.Drivers                        SiameseCat.Protocols
 ├── Siemens/     SiemensDriver             ├── Modbus/  ModbusDriver、RTU/TCP 客户端、CRC、寄存器转换
 └── Inovance/    InovanceDriver            └── S7/      S7Client（ISO-on-TCP/COTP/S7 PDU）、S7Address
        │                                                   │
        └────────── 复用协议客户端 ─────────────────────────┘
                              │
                              ▼
                     SiameseCat.Transport（传输层）
                     ├── TcpTransport
                     ├── UdpTransport
                     └── SerialPortTransport
```

关键原则：

- **传输层与协议层解耦**：`ITransport` 只提供 `open / close / send / recv / discard`；协议驱动通过构造参数接收传输，因此 Modbus RTU 与 TCP 共用同一套 PDU 逻辑，S7 的 ISO-TP 层也不依赖具体 TCP 实现。
- **能力分离**：没有几十个方法的 `IDriver`，而是 `IReadable` / `IWritable` / `IBatchReadable` / `IBatchWritable` / `ISubscribable` / `IHealthCheckable`，驱动按需实现。
- **两层地址模型**：用户写协议原生地址（`40001`、`DB1.DBD0`），由 `IPlcAddressParser` 解析为 `{ Region, Offset, BitOffset?, DataType, Length }`；上层只操作 `PlcAddress`。
- **模板方法**：状态机、事务锁、超时、重试、重连、指标都在 `DriverBase`，驱动只实现 `DoConnectAsync` / `DoReadAsync` / `DoWriteAsync`（可选 `DoReadBatchAsync`）。`DriverBase` 按职责拆成 5 个分部文件：`DriverBase.cs`（契约与读写 API）、`.Lifecycle.cs`（连接状态机）、`.Execution.cs`（超时/重试/重连管线）、`.Health.cs`（`PingAsync` / Keep-Alive）、`.Subscription.cs`（轮询订阅）。
- **插件式注册**：`DriverRegistry` 按 id 注册 `IPlcDriverFactory`，核心库不硬编码任何协议；AOT 场景使用显式注册，非 AOT 场景可用 `RegisterFromAssembly` 扫描。

内置驱动 id 与打包关系：

| 驱动 id | 定义位置 | 典型设备 |
| --- | --- | --- |
| `modbus` | `SiameseCat.Protocols`（`Protocols.Modbus`） | 通用 Modbus 从站、仪表、变频器 |
| `siemens` | `SiameseCat.Drivers`（`Drivers.Siemens`） | S7-200 SMART / 300 / 400 / 1200 / 1500 |
| `inovance` | `SiameseCat.Drivers`（`Drivers.Inovance`） | H3U / H5U / AM400 / AM600（Modbus RTU/TCP） |

## 驱动选项

协议相关参数通过 `DriverConfiguration.Options` 传入（键不区分大小写）。**非法值会直接抛异常，不会被静默截断或忽略**：

```csharp
var configuration = new DriverConfiguration
{
    Transport = TransportKind.Tcp,
    Host = "192.168.0.10",
};
configuration.Options["unitId"] = "7";
configuration.Options["swapBytes"] = "true";
configuration.Options["rack"] = "0";
configuration.Options["slot"] = "1";
```

| 选项 | 适用驱动 | 取值 | 默认 | 说明 |
| --- | --- | --- | --- | --- |
| `unitId` | modbus、inovance | 0–255 | 1 | 从站地址；0 为广播模式（写不等响应，读直接拒绝） |
| `encoding` | modbus、inovance | `ascii` / `utf8` / `latin1` | `ascii` | `String` 值编码 |
| `encoding` | siemens | `ascii` / `utf8` / `latin1` | `latin1` | S7 `STRING` 编码 |
| `swapBytes` | modbus、inovance | `true` / `false` | `false` | 寄存器内字节交换（字符串/原始字节） |
| `interFrameDelayMs` | modbus（RTU） | 0–60000 | 自动 | 帧间静默间隔；0 表示按波特率计算 |
| `discardEcho` | modbus（RTU） | `true` / `false` | `false` | 丢弃 RS-485 转换器回显 |
| `rack` / `slot` | siemens | 0–7 / 0–31 | 0 / 1 | S7 机架与槽位 |
| `dBase` / `mBase` / `xBase` / `yBase` | inovance | 0–65535 | 0 / 0 / 0 / 8192 | 软元件到 Modbus 区域的基址 |

需要强类型读取时可用 `DriverConfiguration` 的辅助方法：`GetOption(key, default, min, max)`、`GetByteOption`、`GetUInt16Option`、`GetBooleanOption`、`HasOption`。

可靠性相关的通用设置是 `DriverConfiguration` 的属性（不是 `Options` 键）：

| 属性 | 默认 | 说明 |
| --- | --- | --- |
| `Timeout` / `ConnectTimeout` | 3 s / 5 s | 单次请求超时 / 建连超时 |
| `RetryCount` | 0 | 瞬时错误重试次数（请求与建连都生效） |
| `RetryDelay` / `MaxRetryDelay` | 200 ms / 30 s | 指数退避的基数与上限 |
| `RetryJitter` | `true` | 退避加 50–100% 随机抖动，避免多客户端同时重连 |
| `RetryWrites` | `false` | 是否允许重试写操作 |
| `ProbeAddress` | 空 | 健康探针地址；`PingAsync` 与 Keep-Alive 使用 |
| `KeepAliveInterval` | `0`（关闭） | 后台探针间隔 |
| `KeepAliveFailureThreshold` | 2 | 连续失败多少次触发自动重连 |

### 使用驱动

```csharp
using SiameseCat.Core;
using SiameseCat.Drivers;

var registry = DriverRegistryExtensions.CreateDefaultRegistry();

await using var driver = registry.Create("modbus", new DriverConfiguration
{
    Transport = TransportKind.Tcp,
    Host = "192.168.0.10",
    Port = 502,
    Timeout = TimeSpan.FromSeconds(2),
    RetryCount = 2,
    RetryDelay = TimeSpan.FromMilliseconds(200),
});

driver.StateChanged += (_, e) => Console.WriteLine($"{e.OldState} -> {e.NewState}");

await driver.ConnectAsync();

var readable = (IReadable)driver;
var writable = (IWritable)driver;
var batch = (IBatchReadable)driver;
var batchWrite = (IBatchWritable)driver;

// 原生地址 -> 结构化地址
var speed = driver.AddressParser.Parse("40001", PlcDataType.Int16);
ushort raw = (await readable.ReadAsync(speed)).AsUInt16();

await writable.WriteAsync(driver.AddressParser.Parse("HR10", PlcDataType.Float), PlcValue.FromFloat(12.5f));

// 批量读：S7 使用多变量请求，Modbus 合并连续寄存器
var values = await batch.ReadBatchAsync(new[]
{
    driver.AddressParser.Parse("HR0"),
    driver.AddressParser.Parse("HR1"),
    driver.AddressParser.Parse("HR2"),
});

// 批量写：Modbus 合并连续寄存器（FC16）/ 线圈（FC15），S7 使用多变量写请求
await batchWrite.WriteBatchAsync(new[]
{
    new PlcWriteRequest(driver.AddressParser.Parse("HR0"), PlcValue.FromUInt16(10)),
    new PlcWriteRequest(driver.AddressParser.Parse("HR1"), PlcValue.FromUInt16(11)),
});

// PlcTag + 工程换算
var tag = new PlcTag("temperature", "40001", PlcDataType.Int16, scale: 0.1, offset: -40, unit: "°C");
var engineering = tag.ToEngineering((await driver.ReadTagAsync(tag)).AsDouble());

// 轮询订阅
await using var subscription = await driver.SubscribeAsync(
    driver.AddressParser.Parse("40001"),
    TimeSpan.FromMilliseconds(500),
    value => { Console.WriteLine(value); return Task.CompletedTask; });

Console.WriteLine(driver.Metrics.Snapshot());
```

### 单次调用超时

`DriverConfiguration.Timeout` 是默认值；批量读大块数据或个别慢点位可以按调用覆盖，互不影响，也不会改动全局配置：

```csharp
// 这一批数据量大，给 10 秒
var values = await batch.ReadBatchAsync(addresses, TimeSpan.FromSeconds(10));

// 单个点位使用更短的超时快速失败
var value = await readable.ReadAsync(driver.AddressParser.Parse("HR0"), TimeSpan.FromMilliseconds(200));
```

写操作与批量写同样支持 `timeout` 重载；`timeout <= 0` 会抛 `ArgumentOutOfRangeException`。

### 宿主集成（DI / 日志）

`SiameseCat.Hosting` 是可选宿主集成包，把驱动的日志接到 `Microsoft.Extensions.Logging`，并支持从 DI 解析：

```csharp
using Microsoft.Extensions.DependencyInjection;
using SiameseCat.Core;
using SiameseCat.Hosting;

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddSiameseCat();                       // DriverRegistry + IPlcLogger 适配器

services.AddSiameseCatDriver("modbus", configuration =>
{
    configuration.Transport = TransportKind.Tcp;
    configuration.Host = "192.168.0.10";
    configuration.ProbeAddress = "40001";
});

var provider = services.BuildServiceProvider();
var driver = provider.GetRequiredKeyedService<IPlcDriver>("modbus");
await driver.ConnectAsync();
```

- 每次解析 keyed 驱动都会创建新实例（transient），作用域释放时自动 `DisposeAsync`。
- 未注册日志时自动退化为 `NullPlcLogger`，因此宿主集成包不强制引入日志实现。
- 单独使用适配器：`new MicrosoftLoggerAdapter(loggerFactory.CreateLogger("SiameseCat"))`。

### 地址模型

| 品牌 | 原生地址示例 | 解析结果 |
| --- | --- | --- |
| Modbus（通用） | `40001` / `HR10` / `IR0` / `C0` / `DI0` | `{ Region="holding", Offset=0, DataType=UInt16 }` |
| Siemens | `DB1.DBX0.0` / `DB1.DBW2` / `M0.0` / `IW4` | `{ Region="DB1", Offset=0, BitOffset=0, DataType=Bool }` |
| Inovance | `D100` / `M10` / `X17`（八进制） / `Y10` | `{ Region="holding", Offset=100, DataType=UInt16 }` |

数组/文本使用 `Length`：`String` 为最大字符数，`Raw` 为字节数；数值类型必须是单个元素。
v1 不提供跨协议统一语义地址（例如把 `"temperature"` 映射到不同厂商的地址），`PlcTag` 已能满足配置化场景。

### 新增第三方驱动

```csharp
public sealed class MyProtocolFactory : IPlcDriverFactory
{
    public static DriverDescriptor DefaultDescriptor { get; } =
        new("myplc", "My PLC", "Custom protocol", TransportKind.Tcp, 5000, new[] { "D100" });

    public DriverDescriptor Descriptor => DefaultDescriptor;

    public IPlcDriver Create(DriverConfiguration configuration, IPlcLogger? logger = null, PlcMetrics? metrics = null) =>
        new MyProtocolDriver(configuration, logger, metrics);
}

public sealed class MyProtocolDriver : DriverBase
{
    public MyProtocolDriver(DriverConfiguration configuration, IPlcLogger? logger, PlcMetrics? metrics)
        : base(MyProtocolFactory.DefaultDescriptor, new MyAddressParser(), configuration, logger, metrics)
    {
    }

    protected override Task DoConnectAsync(CancellationToken ct) => /* open transport */ Task.CompletedTask;
    protected override Task DoDisconnectAsync() => /* close transport */ Task.CompletedTask;
    protected override Task<PlcValue> DoReadAsync(PlcAddress address, CancellationToken ct) => /* read */ throw new NotImplementedException();
    protected override Task DoWriteAsync(PlcAddress address, PlcValue value, CancellationToken ct) => /* write */ Task.CompletedTask;
}

registry.Register(new MyProtocolFactory());
```

新增协议只需实现契约并注册，**不需要修改核心库**。

## 功能

| 协议 / 传输 | 说明 |
| --- | --- |
| Modbus RTU | 串口主站，支持 CRC16、3.5 字符帧间隔、RS-485 回显处理 |
| Modbus TCP | MBAP 帧、事务号校验，支持多个并发调用串行化 |
| Modbus 扩展 | FC23 原子读写（`ReadWriteRegistersAsync`）、广播写（`unitId=0` 只发不等） |
| Siemens S7 | ISO-on-TCP (RFC 1006) + COTP + S7 PDU，支持 DB/M/I/Q 区、位/字节/字/双字/浮点/字符串、批量读写、位数组写、PDU 分片 |
| Inovance（汇川） | 品牌驱动，D/M/X/Y 软元件映射到 Modbus，底层复用 Modbus RTU/TCP 的 PDU 与批量优化，区域基址可配置 |
| TCP | 原始 TCP 客户端传输，可用于自定义文本/二进制协议 |
| UDP | 连接式 UDP 传输，适用于自建数据报协议（协议超时与重试由驱动负责） |
| SerialPort | 原始串口传输，可配置波特率、校验位、数据位、停止位、流控 |
| 批量能力 | `IBatchReadable` / `IBatchWritable`：Modbus 合并连续地址为单帧，S7 使用多变量请求 |
| Native AOT | 全部库标记 `IsAotCompatible`；通过 PublishAotCross 交叉编译 8 个 RID，并在 CI/Docker 中运行探针 |

Modbus 支持的功能码：

- `01` 读线圈
- `02` 读离散输入
- `03` 读保持寄存器
- `04` 读输入寄存器
- `05` 写单个线圈
- `06` 写单个寄存器
- `15` 写多个线圈
- `16` 写多个寄存器
- `23` 读写多个寄存器（`ModbusDriver.ReadWriteRegistersAsync`，驱动层直接暴露）
- 广播写：`unitId=0` 时写请求只发送、不等待响应（读请求会抛异常）

```csharp
// 原子读写：写 HR10..HR11，同时读 HR0..HR1（FC23）
var readBack = await driver.ReadWriteRegistersAsync(
    driver.AddressParser.Parse("HR10"), new ushort[] { 1, 2 },
    driver.AddressParser.Parse("HR0"), readQuantity: 2);

// 广播：unitId = 0
var broadcast = new DriverConfiguration();
broadcast.Options["unitId"] = "0";
```

## 项目结构

四层，一层一个程序集，依赖严格单向（契约 → 传输 → 协议 → 品牌），另有一个可选的宿主集成包：

| 层 | 程序集 | 职责 |
| --- | --- | --- |
| 契约层 | `SiameseCat.Core` | 驱动契约、`DriverBase`、注册表、`PlcAddress`/`PlcValue`/`PlcTag`、`ITransport` 抽象 |
| 传输层 | `SiameseCat.Transport` | `TcpTransport`、`UdpTransport`、`SerialPortTransport`，只负责字节收发 |
| 协议层 | `SiameseCat.Protocols` | Modbus（RTU/TCP）与 S7 的帧、客户端，以及通用 Modbus 驱动 |
| 品牌层 | `SiameseCat.Drivers` | Siemens / Inovance 的品牌驱动与原生地址映射 |
| 宿主集成（可选） | `SiameseCat.Hosting` | `Microsoft.Extensions.Logging` 适配器与 DI 注册，不参与分层依赖链 |

```text
src/
├── SiameseCat.Core/                     # 契约层：无第三方依赖；每个领域目录内「契约 + 实现」相邻
│   ├── Addressing/                      # 地址与值：IPlcAddressParser + PlcAddress / PlcValue / PlcTag / 编解码
│   ├── Diagnostics/                     # 日志与指标：IPlcLogger + NullPlcLogger + PlcMetrics / PlcDiagnostics
│   ├── Drivers/
│   │   ├── Abstractions/                # 驱动契约：IPlcDriver、读写/批量/订阅/健康接口、工厂、描述符、状态
│   │   └── Runtime/                     # 驱动运行时：DriverBase（5 个 partial）、DriverRegistry、DriverConfiguration
│   ├── Exceptions/                      # 异常体系：CommunicationException 及 3 个子类，一类一个文件
│   ├── Transport/                       # 传输契约与助手：ITransport + TransportReader / TransportExtensions
│   ├── CommunicationOptions.cs          # 通讯选项基类
│   └── RetryPolicy.cs                   # 瞬时错误判定与退避策略
├── SiameseCat.Transport/                # 传输层：TCP + UDP + 串口（唯一 System.IO.Ports 依赖）
│   ├── TcpTransport.cs / TcpTransportOptions.cs
│   ├── UdpTransport.cs / UdpTransportOptions.cs
│   └── SerialPortTransport.cs / SerialPortOptions.cs
├── SiameseCat.Protocols/                # 协议层：帧 + 客户端 + 通用 Modbus 驱动
│   ├── Modbus/                          # Modbus 协议公共逻辑
│   │   ├── ModbusCrc16.cs
│   │   ├── ModbusFunctionCode.cs
│   │   ├── ModbusException.cs
│   │   ├── ModbusRegisterConverter.cs
│   │   ├── Clients/                     # 主站基类、RTU 客户端、TCP 客户端、ModbusOptions
│   │   └── Drivers/                     # ModbusDriver、ModbusClientFactory、地址解析、工厂
│   └── S7/                              # S7comm 协议
│       ├── S7Address.cs / S7Area.cs      # 地址模型
│       ├── S7ReadItem.cs / S7WriteItem.cs
│       └── Clients/                     # S7Client（连接/读/写/协议 4 个 partial）、S7Options、S7Exception
├── SiameseCat.Drivers/                  # 品牌层：品牌地址映射 + 驱动 + 注册入口
│   ├── DriverRegistryExtensions.cs      # 一次注册 modbus / siemens / inovance
│   ├── Siemens/                         # SiemensDriver、SiemensAddressParser、SiemensDriverFactory
│   └── Inovance/                        # InovanceDriver、InovanceAddressParser、InovanceMapping、工厂
└── SiameseCat.Hosting/                  # 宿主集成（可选）
    ├── MicrosoftLoggerAdapter.cs        # IPlcLogger -> ILogger
    └── ServiceCollectionExtensions.cs   # AddSiameseCat / AddSiameseCatDriver（Microsoft.Extensions.DependencyInjection 命名空间）

tests/SiameseCat.Tests/
├── Core/        # 驱动契约（Mock 驱动）、地址/值模型、传输、选项、可靠性、指标
├── Modbus/      # CRC、转换、RTU/TCP 客户端、驱动与地址解析
├── Siemens/     # 地址解析、客户端、驱动
├── Inovance/    # 软元件映射、区域基址、驱动读写与批量合并
├── Integration/ # DI 注册与日志适配
└── Fakes/       # MockTransport、MockDriver、协议帧

samples/SiameseCat.Demo/
├── Servers/     # 自测用的 Modbus TCP 从站
└── Program.cs   # selftest / driver / 各协议示例

tools/SiameseCat.AotProbe/   # 无硬件 AOT 探针，支持跨平台交叉编译

scripts/
├── publish-aot.ps1 / publish-aot.sh
└── run-aot-docker.ps1 / run-aot-docker.sh
```

> 命名空间按层收口：`SiameseCat.Core`、`SiameseCat.Transport`、`SiameseCat.Protocols.Modbus` / `.S7`、
> `SiameseCat.Drivers.Siemens` / `.Inovance`、`SiameseCat.Hosting`。层内文件调整不影响引用方。

### 命名约定

| 约定 | 说明 | 示例 |
| --- | --- | --- |
| 程序集 = 层 | 一个层一个包，包名统一以 `SiameseCat.` 开头 | `SiameseCat.Protocols` |
| 命名空间 = 层 + 协议/品牌 | 比目录浅一层，避免 `…Modbus.Protocol` 这类冗余段 | `SiameseCat.Protocols.Modbus` |
| 领域模型统一 `Plc` 前缀 | 核心值类型与契约一律带 `Plc` 前缀，读写代码一眼可辨 | `PlcAddress`、`PlcValue`、`PlcTag`、`IPlcAddressParser` |
| 驱动类按品牌命名 | 品牌驱动用 `品牌 + Driver`，协议客户端保留协议名 | `SiemensDriver`、`InovanceDriver`、`S7Client`、`ModbusDriver` |
| 工厂 = 契约入口 | `IPlcDriverFactory` 的实现类以 `DriverFactory` 结尾 | `SiemensDriverFactory`、`ModbusDriverFactory` |
| 分部类按职责拆文件 | 单类型职责超过 3 块或超过 ~300 行时用 `类型.职责.cs` | `DriverBase.Lifecycle.cs`、`S7Client.Protocol.cs` |
| 契约与实现同域 | 接口放到它服务的领域目录，而不是统一堆到 `Abstractions`；只有跨多个领域的驱动契约才单独成组 | `Addressing/IPlcAddressParser.cs`、`Transport/ITransport.cs`、`Drivers/Abstractions/` |
| 一个公共类型一个文件 | 仅紧耦合的辅助类型可同文件（枚举、事件参数、选项族） | `PlcDataTypeExtensions.cs`、`Exceptions/*.cs`、`ModbusOptions` 三兄弟 |
| 目录只留一层实现分组 | 协议根目录放帧与地址模型，`Clients/` 放会话，`Drivers/` 放驱动适配 | `Modbus/Clients/`、`S7/Clients/` |

项目引用关系（严格单向，无环）：

```text
SiameseCat.Core            （契约 + 模型，无 I/O）
└── SiameseCat.Transport   （TCP / UDP / 串口实现）
    └── SiameseCat.Protocols（Modbus、S7）
        └── SiameseCat.Drivers（Siemens、Inovance 品牌驱动）
            └── SiameseCat.Hosting（可选：DI + Microsoft.Extensions.Logging）
```

引用对应层的项目/包：

```powershell
# 传输层（TCP / UDP / 串口）
dotnet add reference src/SiameseCat.Transport/SiameseCat.Transport.csproj

# 协议层（Modbus RTU/TCP、S7）
dotnet add reference src/SiameseCat.Protocols/SiameseCat.Protocols.csproj

# 品牌层（Siemens / Inovance 驱动）
dotnet add reference src/SiameseCat.Drivers/SiameseCat.Drivers.csproj
```

## 安装

```powershell
dotnet add package SiameseCat.Drivers      # 品牌驱动（Siemens / Inovance）+ 协议层 + 传输层
dotnet add package SiameseCat.Protocols    # 只用协议层：Modbus、S7
dotnet add package SiameseCat.Transport    # 只用传输层：TCP / UDP / 串口
dotnet add package SiameseCat.Core         # 只实现自定义驱动
dotnet add package SiameseCat.Hosting  # 可选：Microsoft.Extensions.Logging + 依赖注入
```

## Native AOT

所有库项目都启用了 `IsAotCompatible`，构建时会运行裁剪与 AOT 分析器，代码中没有反射、`dynamic`、运行时序列化或动态代码生成。`S7Address` 使用源生成正则。

`tools/SiameseCat.AotProbe` 是无硬件依赖的 AOT 探针，覆盖 CRC、寄存器转换、S7 地址解析、串口配置校验、TCP 环回、Modbus TCP 读写与批量写、可插拔驱动契约（注册表 → 驱动 → 批量读写 → 健康探针 → 指标）以及日志集成适配器。项目引用了 **PublishAotCross.Tools** 包，因此可以从任意宿主系统交叉编译到受支持的 RID：

| RID | 格式 | 交叉工具链 |
| --- | --- | --- |
| `win-x64` / `win-arm64` | PE | xwin + lld-link |
| `linux-x64` / `linux-arm64` | ELF (glibc) | Zig |
| `linux-musl-x64` / `linux-musl-arm64` | ELF (musl) | Zig |
| `osx-x64` / `osx-arm64` | Mach-O | Zig + MacOSX.SDK |

```powershell
# 当前宿主
./scripts/publish-aot.ps1 -Run

# 全部 RID（Windows/Linux/macOS 均可）
./scripts/publish-aot.ps1 -All

# 只交叉编译 Linux，并在 Docker 中运行验证
./scripts/publish-aot.ps1 -RuntimeIdentifier linux-x64,linux-musl-x64 -Docker
```

```bash
# Linux / macOS
bash scripts/publish-aot.sh all
bash scripts/run-aot-docker.sh linux-x64
bash scripts/run-aot-docker.sh linux-musl-x64
```

也可以使用发布配置：

```powershell
dotnet publish tools/SiameseCat.AotProbe/SiameseCat.AotProbe.csproj -p:PublishProfile=linux-arm64
```

或直接指定 RID：

```powershell
dotnet publish tools/SiameseCat.AotProbe/SiameseCat.AotProbe.csproj `
  -c Release -r osx-arm64 -p:PublishAot=true -o out
```

Docker 构建：

```powershell
docker build -f tools/SiameseCat.AotProbe/Dockerfile -t siamesecat-aot-probe .
docker run --rm siamesecat-aot-probe

# 交叉构建 ARM64 镜像（BuildKit/buildx）
docker buildx build --platform linux/arm64 -f tools/SiameseCat.AotProbe/Dockerfile -t siamesecat-aot-probe:arm64 .
```

说明：

- `PublishAotCross.Tools` 会自动下载 Zig/xwin/lld-link 工具链，首次构建较慢。
- 交叉编译到 macOS 时符号剥离会自动关闭（宿主没有 `dsymutil`/`strip`），因此 Mach-O 体积较大。
- 交叉编译到 Windows 不需要本机安装 Visual Studio C++ 工具；Linux 原生 AOT 需要 `clang` 与 `zlib1g-dev`；macOS 原生 AOT 需要 Xcode 命令行工具。
- CI 一方面在 `win-x64`、`linux-x64`、`osx-arm64` 上执行原生 AOT 探针，另一方面在 Linux 上交叉编译全部 RID，并用 Docker 运行 glibc/musl 两个 Linux 探针。

## 快速开始

### Modbus TCP

```csharp
using SiameseCat.Core;
using SiameseCat.Protocols.Modbus;

var options = new ModbusTcpOptions
{
    UnitId = 1,
    Timeout = TimeSpan.FromSeconds(3),
    Tcp = new TcpTransportOptions
    {
        Host = "192.168.0.10",
        Port = 502,
    },
};

await using var client = new ModbusTcpClient(options);
await client.ConnectAsync();

ushort[] registers = await client.ReadHoldingRegistersAsync(0, 10);
await client.WriteSingleRegisterAsync(10, 1234);

// 32 位浮点：两个寄存器
var floatRegisters = new ushort[2];
ModbusRegisterConverter.WriteSingle(floatRegisters, 0, 12.5f);
await client.WriteMultipleRegistersAsync(20, floatRegisters);

var readBack = await client.ReadHoldingRegistersAsync(20, 2);
float value = ModbusRegisterConverter.ReadSingle(readBack, 0);
```

### Modbus RTU

```csharp
using System.IO.Ports;
using SiameseCat.Protocols.Modbus;
using SiameseCat.Transport;

var options = new ModbusRtuOptions
{
    UnitId = 1,
    Timeout = TimeSpan.FromSeconds(2),
    RetryCount = 1,
    SerialPort = new SerialPortOptions
    {
        PortName = "COM3",
        BaudRate = 9600,
        Parity = Parity.Even,
        DataBits = 8,
        StopBits = StopBits.One,
        ReadTimeout = TimeSpan.FromMilliseconds(500),
    },
};

await using var client = new ModbusRtuClient(options);
await client.ConnectAsync();

var registers = await client.ReadHoldingRegistersAsync(0, 10);
```

RTU 说明：

- 未设置 `InterFrameDelay` 时，会根据波特率自动计算 3.5 字符静默间隔（最小 2 ms）。
- 部分 RS-485 转换器会回显请求帧，可设置 `DiscardEcho = true`。
- `SerialPortOptions.ReadTimeout` 建议小于 `ModbusRtuOptions.Timeout`，否则请求超时会被串口读超时延迟。

### 西门子（Siemens）

```csharp
using SiameseCat.Protocols.S7;

var options = new S7Options
{
    Host = "192.168.0.10",
    Rack = 0,
    Slot = 1,              // S7-1200/1500 通常为 1，S7-300/400 通常为 2
    Timeout = TimeSpan.FromSeconds(3),
};

await using var client = new S7Client(options);
await client.ConnectAsync();

float speed = await client.ReadSingleAsync(S7Address.Parse("DB1.DBD0"));
await client.WriteSingleAsync(S7Address.Parse("DB1.DBD0"), 12.5f);

bool motorOn = await client.ReadBoolAsync(S7Address.Parse("M0.0"));
await client.WriteBoolAsync(S7Address.Parse("M0.0"), true);

ushort count = await client.ReadUInt16Async(S7Address.Parse("DB2.DBW4"));
await client.WriteUInt16Async(S7Address.Parse("DB2.DBW4"), 123);

string name = await client.ReadStringAsync(S7Address.Parse("DB3.DBB0"), maxLength: 32);
await client.WriteStringAsync(S7Address.Parse("DB3.DBB0"), "SiameseCat", maxLength: 32);

// 一次读取多个变量，减少往返
byte[][] values = await client.ReadMultipleAsync(new[]
{
    new S7ReadItem(S7Address.Parse("DB1.DBW0"), 2),
    new S7ReadItem(S7Address.Parse("DB1.DBD2"), 4),
    new S7ReadItem(S7Address.Parse("M0.0"), 1),
});

// 一次写入多个变量
await client.WriteMultipleAsync(new[]
{
    new S7WriteItem(S7Address.Parse("DB1.DBW0"), new byte[] { 0x12, 0x34 }),
    new S7WriteItem(S7Address.Parse("M0.0"), new byte[] { 0x01 }),
});

// 连续位数组一次写入（自动按 PDU 分块，可跨字节边界）
await client.WriteBitsAsync(S7Address.Parse("M10.0"), new[] { true, false, true, true, false });
```

支持的 S7 地址格式（不区分大小写）：

```text
DB1.DBX0.0     位
DB1.DBB0       字节
DB1.DBW2       字（16 位）
DB1.DBD4       双字（32 位）
M0.0 / MB0 / MW2 / MD4
I0.0 / IB0 / IW2 / ID4       （E 为德语输入别名）
Q0.0 / QB0 / QW2 / QD4       （A 为德语输出别名）
```

S7 注意事项：

- S7-1200/1500 需要在 TIA Portal 中允许 PUT/GET 通讯。
- 访问 DB 时，需要关闭该 DB 的“优化的块访问（Optimized block access）”。
- 大块数据会自动按协商后的 PDU 大小分片读写；`client.PduSize` 可查看协商结果。

### 汇川（Inovance）

汇川 PLC（H3U / H5U / AM 系列）通过 Modbus RTU/TCP 对外提供数据，品牌驱动只负责把软元件变成 Modbus 地址，PDU、批量合并和数据类型转换全部复用协议层的 Modbus 实现。

```csharp
using SiameseCat.Core;
using SiameseCat.Drivers;

var registry = DriverRegistryExtensions.CreateDefaultRegistry();

await using var plc = registry.Create("inovance", new DriverConfiguration
{
    Transport = TransportKind.Tcp,
    Host = "192.168.0.30",
    Port = 502,                        // 默认 502
    Timeout = TimeSpan.FromSeconds(2),
});

await plc.ConnectAsync();

var readable = (IReadable)plc;
var writable = (IWritable)plc;
var batch = (IBatchReadable)plc;

// D 数据寄存器 -> 保持寄存器
ushort speed = (await readable.ReadAsync(plc.AddressParser.Parse("D100"))).AsUInt16();
await writable.WriteAsync(plc.AddressParser.Parse("D100"), PlcValue.FromUInt16(1234));

// M 辅助继电器 -> 线圈
await writable.WriteAsync(plc.AddressParser.Parse("M10"), PlcValue.FromBool(true));

// X 输入（八进制） -> 离散输入；Y 输出（八进制） -> 线圈（默认 +8192）
bool input = (await readable.ReadAsync(plc.AddressParser.Parse("X17"))).AsBool();
await writable.WriteAsync(plc.AddressParser.Parse("Y10"), PlcValue.FromBool(true));

// 批量读：连续的 D 寄存器合并成一次请求
var values = await batch.ReadBatchAsync(new[]
{
    plc.AddressParser.Parse("D0"),
    plc.AddressParser.Parse("D1"),
    plc.AddressParser.Parse("D2"),
});

// 也可以直接写 Modbus 寄存器地址
ushort raw = (await readable.ReadAsync(plc.AddressParser.Parse("40001"))).AsUInt16();
```

软元件映射（不区分大小写，`X`/`Y` 为八进制）：

| 软元件 | Modbus 区域 | 默认基址 | 说明 |
| --- | --- | --- | --- |
| `D100` | 保持寄存器 | 0 | 数据寄存器，可读写，支持 Float/Int32 等跨寄存器类型 |
| `M10` | 线圈 | 0 | 辅助继电器，读写 `Bool` |
| `X17` | 离散输入 | 0 | 输入继电器，只读，八进制 |
| `Y10` | 线圈 | 8192（`0x2000`） | 输出继电器，八进制 |

不同系列或从站的区域映射不同时，用驱动选项覆盖基址：

```csharp
var configuration = new DriverConfiguration
{
    Transport = TransportKind.Tcp,
    Host = "192.168.0.30",
};
configuration.Options["unitId"] = "1";
configuration.Options["dBase"] = "0";
configuration.Options["mBase"] = "0";
configuration.Options["xBase"] = "0";
configuration.Options["yBase"] = "8192";
await using var plc = registry.Create("inovance", configuration);
```

汇川注意事项：

- 默认映射按 H3U/H5U 常见约定：`D`/`M`/`X` 从 0 开始，`Y` 从线圈 8192 开始；若从站文档给出别的基址，用 `dBase`/`mBase`/`xBase`/`yBase` 覆盖。
- 支持 Modbus RTU：把 `Transport` 设为 `Serial` 并配置 `Serial` 段即可，串口参数与通用 Modbus 完全一致。
- 地址也可以直接写 Modbus 形式（`40001`、`HR10`、`C0`），便于对照从站手册。

### 原始 TCP

```csharp
using System.Text;
using SiameseCat.Core;

await using var transport = new TcpTransport(new TcpTransportOptions
{
    Host = "127.0.0.1",
    Port = 5000,
    ConnectTimeout = TimeSpan.FromSeconds(3),
});

await transport.OpenAsync();
await transport.WriteAsync(Encoding.ASCII.GetBytes("PING\r\n"));

byte[] header = await transport.ReadExactlyAsync(4);
var buffer = new byte[1024];
int read = await transport.ReadAsync(buffer);
```

### 原始 UDP

```csharp
using SiameseCat.Transport;

await using var transport = new UdpTransport(new UdpTransportOptions
{
    Host = "127.0.0.1",
    Port = 5000,
    ConnectTimeout = TimeSpan.FromSeconds(3),
});

await transport.OpenAsync();
await transport.WriteAsync(payload);          // 发送一个数据报
var response = await transport.ReadExactlyAsync(expectedBytes);
```

UDP 不保证送达与顺序：协议层超时与驱动的 `RetryCount` 负责重试，收到重复或迟到的数据报时应由协议自行丢弃。

### 原始串口

```csharp
using SiameseCat.Transport;

await using var transport = new SerialPortTransport(new SerialPortOptions
{
    PortName = "COM3",
    BaudRate = 115200,
    ReadTimeout = TimeSpan.FromMilliseconds(500),
});

await transport.OpenAsync();
await transport.WriteAsync(Encoding.ASCII.GetBytes("PING\r\n"));

var buffer = new byte[1024];
int read = await transport.ReadAsync(buffer);
```

## 数据类型转换（Modbus）

`ModbusRegisterConverter` 支持：

- `UInt16` / `Int16`
- `UInt32` / `Int32`
- `UInt64` / `Int64`
- `Single` / `Double`
- 原始字节数组（`ReadBytes` / `WriteBytes`）
- 字符串（ASCII、UTF-8 或自定义 `Encoding`，可选择寄存器内字节交换）
- 字序：`HighWordFirst`（默认）和 `LowWordFirst`

```csharp
var int32 = ModbusRegisterConverter.ReadInt32(registers, 0, ModbusWordOrder.HighWordFirst);
var uint64 = ModbusRegisterConverter.ReadUInt64(registers, 2);
var bytes = ModbusRegisterConverter.ReadBytes(registers, 6, 4);
var text = ModbusRegisterConverter.ReadString(registers, 10, 10);

ModbusRegisterConverter.WriteInt32(registers, 0, -12345);
ModbusRegisterConverter.WriteUInt64(registers, 2, 0x0123456789ABCDEFUL);
ModbusRegisterConverter.WriteBytes(registers, 6, 4, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
ModbusRegisterConverter.WriteString(registers, 10, 10, "SiameseCat");
```

说明：

- 字符串在解码前先截断到第一个 NUL，因此 UTF-8 等多字节编码不会被截断在字符中间。
- `WriteString` / `WriteBytes` 会把剩余寄存器补零。
- 所有方法都会校验寄存器范围，越界时抛出 `ArgumentOutOfRangeException`。

## 可靠性设计

### 生命周期

```csharp
await using var client = new ModbusTcpClient(options);  // 或 S7Client、TcpTransport、SerialPortTransport
await client.ConnectAsync();
// ... 多次请求
```

- `ConnectAsync` 幂等：已连接时直接返回，可在重连逻辑中反复调用。
- `DisconnectAsync` 只关闭连接，实例仍可再次 `ConnectAsync`。
- `DisposeAsync` 之后任何操作都会抛出 `ObjectDisposedException`；重复释放是安全的。
- 所有客户端都按请求串行化，同一个实例可以安全地被多个调用方共享。
- 传输层（`TcpTransport`、`SerialPortTransport`）本身不保证线程安全，应通过客户端使用。

### 超时与重连

| 场景 | 行为 |
| --- | --- |
| 请求超时 | 抛出 `CommunicationTimeoutException` |
| Modbus TCP 超时或取消 | 关闭连接，避免迟到的响应污染下一次请求 |
| Modbus RTU 超时 | 保留串口，下一次请求前丢弃残留字节 |
| S7 超时或取消 | 关闭连接；下次请求必须重新握手（重试开启时自动完成） |
| 串口读/写被取消 | 关闭串口，避免半帧数据留在总线上 |
| 连接被对端关闭 | 抛出 `ConnectionClosedException` |

`RetryCount` 只重试瞬时错误（超时、连接断开、I/O 失败）。协议错误、Modbus 异常、S7 错误不会重试。
**连接失败同样会重试**：`ConnectAsync` 内部按 `RetryCount` 重试瞬时错误，因此主机重启、交换机抖动后无需外部重试循环。
重试前会先按指数退避等待（`RetryDelay × 2^(n-1)`，上限 `MaxRetryDelay`，默认加 50–100% 抖动），再重连并重发请求。

```csharp
var options = new ModbusTcpOptions
{
    Timeout = TimeSpan.FromSeconds(2),
    RetryCount = 2,
    RetryDelay = TimeSpan.FromMilliseconds(300),
    RetryWrites = false,           // 默认：不重试写操作
};

// 驱动层（DriverConfiguration）的等价配置
var configuration = new DriverConfiguration
{
    RetryCount = 2,
    RetryDelay = TimeSpan.FromMilliseconds(300),
    MaxRetryDelay = TimeSpan.FromSeconds(10),   // 退避上限
    RetryJitter = true,                         // 抖动，避免多客户端同时重连
};
```

写操作默认不重试，因为设备可能已经执行了写入但响应丢失。确认写入幂等后，可以显式开启 `RetryWrites = true`。

### 健康检查（Ping / Keep-Alive）

配置一个**无副作用的探针地址**后，可以用 `PingAsync` 主动探测，或让驱动在后台定期探测并在连续失败后自动重连：

```csharp
var configuration = new DriverConfiguration
{
    Transport = TransportKind.Tcp,
    Host = "192.168.0.10",
    ProbeAddress = "40001",                      // 建议选一个只读、一定存在的地址
    KeepAliveInterval = TimeSpan.FromSeconds(5), // 0（默认）表示关闭
    KeepAliveFailureThreshold = 2,               // 连续失败 2 次触发重连
};

await using var driver = registry.Create("modbus", configuration);
await driver.ConnectAsync();

bool alive = await ((IHealthCheckable)driver).PingAsync();   // 失败返回 false，不抛异常
```

- `PingAsync` 只做一次探针读：通讯失败返回 `false`，取消时抛 `OperationCanceledException`；未配置 `ProbeAddress` 时抛 `InvalidOperationException`。
- Keep-Alive 在 `ConnectAsync` 成功后启动，`DisconnectAsync` / `DisposeAsync` 停止；每次探测都经过事务锁，不会与业务请求交错。
- 探针读同样计入指标（`Metrics.Requests`），便于观察心跳开销。

### 指标与 OpenTelemetry 导出

`driver.Metrics.Snapshot()` 返回按操作分类的计数与延迟分布：

```csharp
var snapshot = driver.Metrics.Snapshot();

Console.WriteLine(snapshot);                     // 单行摘要
Console.WriteLine(snapshot.Operations.Read);     // 读次数（Read / Write / ReadBatch / WriteBatch / Ping 分开统计）
Console.WriteLine(snapshot.P95LatencyMs);        // P50 / P95 / Max（对数直方图估算，误差 ≤ 19%，且不会超过 Max）
Console.WriteLine(snapshot.FailureKinds.Timeouts); // 超时 / 连接断开 / 协议错误 / 其它 分类计数
```

同时，`DriverBase` 会向 `System.Diagnostics.Metrics` 的 **`SiameseCat`** 计量器写入标准指标（纯 BCL 实现，无需额外依赖）：

| 指标 | 类型 | 标签 | 说明 |
| --- | --- | --- | --- |
| `plc.requests` | Counter | `driver`、`operation` | 请求次数 |
| `plc.failures` | Counter | `driver`、`operation`、`error` | 失败次数与错误类型 |
| `plc.retries` | Counter | `driver` | 重试次数 |
| `plc.bytes.read` / `plc.bytes.written` | Counter | `driver` | 负载字节数 |
| `plc.latency` | Histogram (ms) | `driver`、`operation` | 请求延迟 |

接入 OpenTelemetry 只需订阅计量器名称：

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(PlcDiagnostics.MeterName)   // "SiameseCat"
        .AddPrometheusExporter());
```

### 异常模型

- `CommunicationException`：通讯失败基类
- `CommunicationTimeoutException`：请求超时
- `ConnectionClosedException`：连接被对端关闭或传输不可用
- `ProtocolException`：响应格式不符合协议
- `ModbusException`：Modbus 异常码（`ExceptionCode`），消息包含单元号与功能码
- `S7Exception` / `S7ItemException`：S7 协议或条目错误，条目错误消息包含地址或条目序号

```text
Modbus exception 0x02 for unit 1, function 0x03: Illegal data address.
S7 item error 0x0A: Object does not exist. Address: DB1.DBW0.
```

- 单元号 0 视为广播：写请求只发送不等待响应，读请求直接拒绝。
- 客户端的 `ConnectAsync` / `DisconnectAsync` 与请求共用同一把锁，不会打断进行中的请求。
- 命名空间跟随分层：`SiameseCat.Core`、`SiameseCat.Transport`、`SiameseCat.Protocols.Modbus` / `.S7`、`SiameseCat.Drivers.Siemens` / `.Inovance`。
- 所有公共 API 都有 XML 文档（`CS1591` 已启用），生成的 `.xml` 随 NuGet 包一起发布。

### 版本与打包

- 目标框架：`net8.0`（LTS）
- 启用 .NET 分析器（`latest-recommended`）
- 集中包版本管理（`Directory.Packages.props`）
- Release 构建生成 `.nupkg` 与 `.snupkg`，并启用 SourceLink
- CI 在每次 push / PR 时执行构建、测试和打包（见 [.github/workflows/ci.yml](.github/workflows/ci.yml)）

## 构建与测试

解决方案使用 `.slnx` 格式，需要 .NET SDK 9.0.200 或更高版本（CI 使用 10.0.x 构建 `net8.0` 目标）。

```powershell
dotnet build SiameseCat.slnx
dotnet test SiameseCat.slnx

# 无硬件自测：内置 Modbus TCP 从站
dotnet run --project samples/SiameseCat.Demo -- selftest

# 可插拔驱动示例：注册表 + Modbus 驱动 + 批量读 + PlcTag
dotnet run --project samples/SiameseCat.Demo -- driver

# 直接以 Native AOT 运行探针（项目已启用 PublishAot）
dotnet run --project tools/SiameseCat.AotProbe

# 仅用 JIT 快速运行
dotnet run --project tools/SiameseCat.AotProbe -p:PublishAot=false

# 打包全部库
dotnet pack src/SiameseCat.Core/SiameseCat.Core.csproj -c Release -o artifacts
dotnet pack src/SiameseCat.Transport/SiameseCat.Transport.csproj -c Release -o artifacts
dotnet pack src/SiameseCat.Protocols/SiameseCat.Protocols.csproj -c Release -o artifacts
dotnet pack src/SiameseCat.Drivers/SiameseCat.Drivers.csproj -c Release -o artifacts
```

示例程序的其它命令：

```text
dotnet run -- modbus-tcp <host> [port] [unit]
dotnet run -- modbus-rtu <com> [baud] [unit]
dotnet run -- siemens <host> [rack] [slot] [address]
dotnet run -- tcp <host> <port> [text]
dotnet run -- serial <com> [baud] [text]
```
## 当前限制

- Modbus 仅实现主站/客户端，未包含从站/服务器；广播模式只支持写（读请求必然无响应）。
- S7 仅支持 S7comm 的读/写变量，不支持上传/下载、诊断缓冲区等高级服务。
- 驱动层内置三个驱动：通用 Modbus（RTU/TCP）、西门子 S7、汇川（D/M/X/Y 映射到 Modbus）；其他品牌可按同一契约扩展。
- 汇川驱动基于 Modbus，按软元件映射实现；厂商私有 ASCII/串口协议未内置。
- 西门子 S7 目前只支持 TCP 传输；Modbus 支持 TCP/RTU。
- 批量写中若包含 S7 `String`，该批会回退为逐条写入（`STRING` 需要两字节长度头）。
- v1 不提供跨协议统一语义地址映射，`PlcTag` 覆盖配置化场景。
- 串口没有可取消的异步 API：取消正在进行的读写会关闭串口，需要重新调用 `ConnectAsync`。
- 串口 `ReadTimeout` 建议小于协议 `Timeout`，否则超时检测会被串口读超时延迟。

## 许可证

[MIT](LICENSE)
