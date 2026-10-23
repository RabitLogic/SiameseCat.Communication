# SiameseCat

**English** · [简体中文](README.zh-CN.md)

面向工业设备的轻量级 .NET 通讯库。所有包同时提供 **.NET 8 (LTS)**、**.NET 10 (LTS)** 与 **.NET Standard 2.0** 资产——现代目标是**下限而非上限**：.NET 9、.NET 11 及更高版本会自动选用最接近的较低资产，不会被绑定到某一个运行时版本。

> 深度文档（英文）：[使用教程](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/tutorial.md) · [架构](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/architecture.md) · [协议矩阵](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/protocol-matrix.md) · [迁移与接入](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/migration.md) · [发布指南](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/publishing.md) · [仓库设置（规则集、工作流权限）](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/repository-settings.md)
>
> 从零动手复刻教程（中文）：[tutorial/](tutorial/README.md)

本仓库按**层/协议拆分为多个 NuGet 包**，命名空间与旧版保持一致：`SiameseCat` 是组织名，后续项目（如 `SiameseCat.Mes.Template`）是各自独立的仓库与包。

| 包 | 内容 | 依赖 |
| --- | --- | --- |
| `SiameseCat.Communication.Core` | 驱动契约、设备模型、`DriverBase`、注册表、TCP/UDP/串口传输、异常与诊断 | BCL |
| `SiameseCat.Communication.Tcp` | 与设备模型无关的分帧、`TcpFrameConnection`、`CustomTcpClient`、`CustomTcpSimulator` | Core |
| `SiameseCat.Communication.Modbus` | Modbus RTU/TCP 帧、客户端、地址解析与通用 Modbus 驱动 | Core + Tcp |
| `SiameseCat.Communication.S7` | `S7Client`、`S7Address`、S7 选项与读写项、通用 S7 驱动 | Core |
| `SiameseCat.Communication.Simulation.Modbus` | Modbus TCP/RTU 仿真器 | Modbus |
| `SiameseCat.Communication.Simulation.S7` | S7（ISO-on-TCP）仿真器 | S7 |
| `SiameseCat.Communication.Hosting` | `IServiceCollection` 注册、仿真器托管、健康检查、日志适配器 | Core + Modbus + S7 + Simulation.* |
| `SiameseCat.Communication.All` | 便捷聚合包 | 以上全部 |

只引用 `SiameseCat.Communication.S7` 时只会还原 Core + S7，不会引入 Modbus、仿真器或 Hosting。协议栈自行实现（含设备标识 FC2B 与文件记录 FC14/15），不依赖 NModbus、Sharp7 等第三方协议库。

## 架构：可插拔驱动

```text
应用
 │  只依赖驱动契约与 DeviceAddress/DeviceValue，不依赖品牌或协议
 ▼
SiameseCat.Communication（契约 + 模型）
 ├── IDeviceDriver / IReadable / IWritable / IBatchReadable / IBatchWritable / ISubscribable / IHealthCheckable
 ├── DriverBase        状态机 / 事务锁 / 超时 / 重试 / 重连 / 日志 / 指标（5 个 partial）
 ├── DeviceAddress / DeviceValue / DeviceTag / IDeviceAddressParser
 ├── ITransport        字节收发抽象
 └── DriverRegistry    驱动发现与创建
        ▲                                        ▲
        │ 协议（帧 / 客户端 / 通用驱动）          │ 协议（帧 / 客户端 / 通用驱动）
 SiameseCat.Communication.S7                     SiameseCat.Communication.Modbus
 ├── S7Driver、S7AddressParser、S7DriverFactory  ├── ModbusDriver、RTU/TCP 客户端、CRC、寄存器转换
└── S7Client（ISO-on-TCP/COTP/S7 PDU）、S7Address └── ModbusAddressParser、ModbusClientFactory
        │                                                   │
        └────────── 复用协议客户端 ─────────────────────────┘
                              │
                              ▼
                     SiameseCat.Communication.Transport（传输实现）
                     ├── TcpTransport
                     ├── UdpTransport
                     └── SerialPortTransport
```

关键原则：

- **传输层与协议层解耦**：`ITransport` 只提供 `open / close / send / recv / discard`；协议驱动通过构造参数接收传输，因此 Modbus RTU 与 TCP 共用同一套 PDU 逻辑，S7 的 ISO-TP 层也不依赖具体 TCP 实现。
- **能力分离**：没有几十个方法的 `IDriver`，而是 `IReadable` / `IWritable` / `IBatchReadable` / `IBatchWritable` / `ISubscribable` / `IHealthCheckable`，驱动按需实现，并在 `DriverDescriptor.Capabilities` 中声明；`DriverBase` 在调用前校验，越界调用直接抛 `NotSupportedException`。
- **两层地址模型**：用户写协议原生地址（`40001`、`DB1.DBD0`），由 `IDeviceAddressParser` 解析为 `{ Region, Offset, BitOffset?, DataType, Length, Symbol? }`；上层只操作 `DeviceAddress`。符号寻址协议（OPC UA / CIP / ADS）使用 `Symbol` 字段，不把符号拼进字符串。
- **模板方法**：状态机、事务锁、超时、重试、重连、指标都在 `DriverBase`，驱动只实现 `DoConnectAsync` / `DoReadAsync` / `DoWriteAsync`（可选 `DoReadBatchAsync`）。`DriverBase` 按职责拆成 5 个分部文件：`DriverBase.cs`（契约与读写 API）、`.Lifecycle.cs`（连接状态机）、`.Execution.cs`（超时/重试/重连管线）、`.Health.cs`（`PingAsync` / Keep-Alive）、`.Subscription.cs`（轮询订阅）。
- **插件式注册**：`DriverRegistry` 按 id 注册 `IDeviceDriverFactory`，核心库不硬编码任何协议；AOT 场景使用显式注册，非 AOT 场景可用 `RegisterFromAssembly` 扫描。

内置驱动 id：

| 驱动 id | 定义位置 | 典型设备 |
| --- | --- | --- |
| `modbus` | `SiameseCat.Communication.Modbus` | 通用 Modbus 从站、仪表、变频器 |
| `s7` | `SiameseCat.Communication.S7` | S7-200 SMART / 300 / 400 / 1200 / 1500 及兼容 S7comm 的设备 |

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
| `unitId` | modbus | 0–255 | 1 | 从站地址；0 为广播模式（写不等响应，读直接拒绝） |
| `encoding` | modbus | `ascii` / `utf8` / `latin1` | `ascii` | `String` 值编码 |
| `encoding` | s7 | `ascii` / `utf8` / `latin1` | `latin1` | S7 `STRING` 编码 |
| `swapBytes` | modbus | `true` / `false` | `false` | 寄存器内字节交换（字符串/原始字节） |
| `interFrameDelayMs` | modbus（RTU） | 0–60000 | 自动 | 帧间静默间隔；0 表示按波特率计算 |
| `framing` | modbus（Custom 传输） | `tcp` / `rtu` | `tcp` | 自定义传输上的 Modbus 组帧方式 |
| `discardEcho` | modbus（RTU） | `true` / `false` | `false` | 丢弃 RS-485 转换器回显 |
| `rack` / `slot` | s7 | 0–7 / 0–31 | 0 / 1 | S7 机架与槽位 |

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
| `TransportFactory` | 空 | 注入 `ITransport` 来源；设置后驱动不再自己创建传输（`TransportKind.Custom` 时必须设置） |
| `RequestLock` | 空 | 共享请求锁；多站共用一条物理链路时设置同一个实例 |
| `LogFrames` | `false` | 在 `Trace` 级别输出收发帧 hex，用于现场排障 |

### 使用驱动

```csharp
using SiameseCat.Communication;
using SiameseCat.Communication.Drivers;

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
var speed = driver.AddressParser.Parse("40001", DeviceDataType.Int16);
ushort raw = (await readable.ReadAsync(speed)).AsUInt16();

await writable.WriteAsync(driver.AddressParser.Parse("HR10", DeviceDataType.Float), DeviceValue.FromFloat(12.5f));

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
    new DeviceWriteRequest(driver.AddressParser.Parse("HR0"), DeviceValue.FromUInt16(10)),
    new DeviceWriteRequest(driver.AddressParser.Parse("HR1"), DeviceValue.FromUInt16(11)),
});

// DeviceTag + 工程换算
var tag = new DeviceTag("temperature", "40001", DeviceDataType.Int16, scale: 0.1, offset: -40, unit: "°C");
var engineering = tag.ToEngineering((await driver.ReadTagAsync(tag)).AsDouble());

// 轮询订阅（默认带抖动与失败退避）
await using var subscription = await driver.SubscribeAsync(
    driver.AddressParser.Parse("40001"),
    TimeSpan.FromMilliseconds(500),
    value => { Console.WriteLine(value); return Task.CompletedTask; });

Console.WriteLine(driver.Metrics.Snapshot());
```

### 复用与共享传输（RS-485 多站）

默认情况下驱动自己创建传输（TCP 连接或串口）。设置 `TransportFactory` 后驱动改用它提供的 `ITransport`，于是可以复用已有链路、把协议塞进自定义隧道，或者让一条 RS-485 总线上的多个站共用一个串口：

```csharp
using SiameseCat.Communication;
using SiameseCat.Communication.Transport;

// 一条物理链路 + 一把共享请求锁
await using var link = new SharedTransportFactory(() => new SerialPortTransport(new SerialPortOptions
{
    PortName = "COM3",
    BaudRate = 19200,
}));

foreach (var station in new byte[] { 1, 2, 3 })
{
    var configuration = new DriverConfiguration
    {
        Transport = TransportKind.Custom,          // 由工厂提供字节流
        TransportFactory = link,
        RequestLock = link.RequestLock,            // 多站必须共享，避免帧交错
        Timeout = TimeSpan.FromSeconds(1),
    };
    configuration.Options["framing"] = "rtu";      // Custom 传输的 Modbus 组帧：rtu / tcp
    configuration.Options["unitId"] = station.ToString();

    await using var driver = registry.Create("modbus", configuration);
    // ConnectAsync / ReadAsync / WriteAsync 与普通驱动一致
}
```

- `TransportKind.Custom` 表示「字节流由 `TransportFactory` 提供」，驱动不再自己开端口；Modbus 用 `framing` 选项决定 MBAP（`tcp`）还是 RTU 组帧，S7 固定使用它自己的 TPKT 组帧。
- 只共享 `TransportFactory`、不共享 `RequestLock` 会让两个站的请求帧交错，务必两者一起设置；`SharedTransportFactory.RequestLock` 就是为此准备的。
- 不想共享时也可以让工厂每次返回新传输，用于复用已有连接或自建隧道。
- `LogFrames = true` 会把每一帧以 `Trace` 级别写到 `ICommunicationLogger`（hex），现场排障时打开即可，例如输出 `modbus/1 TX 01 03 00 00 00 02 C4 0B`、`RX 01 03 04 00 64 00 C8 ...`。

### 能力声明与可选钩子

驱动在 `DriverDescriptor.Capabilities` 里声明自己支持哪些能力，`DriverBase` 会在调用前校验，越界调用直接抛 `NotSupportedException`：

```csharp
new DriverDescriptor(
    "sensor", "Sensor", "只读采集器", TransportKind.Serial,
    capabilities: DriverCapabilities.Read | DriverCapabilities.Health);
```

只读驱动不再需要写一个「抛异常」的 `DoWriteAsync`：`DoReadAsync` / `DoWriteAsync` 是可以不重写的虚方法，默认就抛 `NotSupportedException`。订阅同样是可选的——重写 `DoSubscribeAsync` 就能接入协议原生订阅，返回 `null`（默认）时自动回退到轮询。批量订阅遵循同一套模式：声明 `DriverCapabilities.SubscribeBatch` 后调用 `IBatchSubscribable.SubscribeBatchAsync`，重写 `DoSubscribeBatchAsync` 可接入原生批量订阅，返回 `null` 时用一次 `ReadBatchAsync` 轮询整组点位（因此还需要 `BatchRead` 能力）。

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

`SiameseCat.Communication.Hosting` 是同包内的宿主集成命名空间，把驱动的日志接到 `Microsoft.Extensions.Logging`，并支持从 DI 解析：

```csharp
using Microsoft.Extensions.DependencyInjection;
using SiameseCat.Communication;
using SiameseCat.Communication.Hosting;

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddSiameseCat();                       // DriverRegistry + ICommunicationLogger 适配器

services.AddSiameseCatDriver("modbus", configuration =>
{
    configuration.Transport = TransportKind.Tcp;
    configuration.Host = "192.168.0.10";
    configuration.ProbeAddress = "40001";
});

var provider = services.BuildServiceProvider();
var driver = provider.GetRequiredKeyedService<IDeviceDriver>("modbus");
await driver.ConnectAsync();
```

- 每次解析 keyed 驱动都会创建新实例（transient），作用域释放时自动 `DisposeAsync`。
- 未注册日志时自动退化为 `NullCommunicationLogger`，因此不强制引入日志实现。
- 单独使用适配器：`new MicrosoftLoggerAdapter(loggerFactory.CreateLogger("SiameseCat"))`。

Modbus 与 S7 从站也可以交给 DI 管理（注册为 `IModbusSimulator` / `IS7Simulator` 单例，随宿主自动启停）：

```csharp
services.AddModbusSimulator(options =>
{
    options.Port = 0;                        // TCP 从站，0 = 系统分配
    options.ServerIdentifier = "SIM-1";      // FC17 Report Server ID
});

// 或串口从站
services.AddModbusRtuSimulator(options =>
{
    options.UnitId = 1;
    options.Serial.PortName = "COM3";
});

// 或 S7（ISO-on-TCP）从站
services.AddS7Simulator(options =>
{
    options.Port = 0;
    options.DefaultDataBlockSize = 1024;
});
```

- Modbus 从站以 `IModbusSimulator` 注册、S7 从站以 `IS7Simulator` 注册，`provider.GetServices<...>()` 可取到全部实例。
- 内部注册一个 `IHostedService`，在宿主启动/停止时统一 `StartAsync` / `StopAsync`。
- 未显式设置 `Logger` 时自动使用 DI 中的 `ICommunicationLogger`。

### 地址模型

| 协议 | 原生地址示例 | 解析结果 |
| --- | --- | --- |
| Modbus（通用） | `40001` / `HR10` / `IR0` / `C0` / `DI0` | `{ Region="holding", Offset=0, DataType=UInt16 }` |
| S7 | `DB1.DBX0.0` / `DB1.DBW2` / `M0.0` / `IW4` | `{ Region="DB1", Offset=0, BitOffset=0, DataType=Bool }` |

裸数字按位数分类：`1000` 是保持寄存器 1000，而 `01000` 是线圈 999；偏移有歧义时请写 `holding:1000`。每种解析器只管自己的记法——Modbus 解析器拒绝 S7 地址，S7 解析器也不认 Modbus 地址。

数组/文本使用 `Length`：`String` 为最大字符数，`Raw` 为字节数，**数值类型为元素个数**（`UInt16` + `Length=10` 就是 10 个寄存器/字，`ByteSize` 自动等于 20）。`Bool` 始终表示单个位，多个位请用多个地址。

```csharp
// 读 10 个连续寄存器为一个数组
var block = await driver.ReadAsync(driver.AddressParser.Parse("HR0", DeviceDataType.UInt16).WithLength(10));
ushort[] values = block.AsUInt16Array();

// 写一个数组，Modbus 会自动改用 FC16 / S7 走多字节写
await driver.WriteAsync(
    driver.AddressParser.Parse("HR0", DeviceDataType.Float).WithLength(4),
    DeviceValue.FromFloatArray(new[] { 1.5f, 2.5f, 3.5f, 4.5f }));
```

支持的数组元素类型：`Int16` / `UInt16` / `Int32` / `UInt32` / `Int64` / `UInt64` / `Float` / `Double`；`DeviceValue.IsArray` / `Length` / `AsUInt16Array()` 等访问器与**按元素比较**的相等性都已提供，标量访问器对数组会抛 `InvalidCastException`。
v1 不提供跨协议统一语义地址（例如把 `"temperature"` 映射到不同厂商的地址），`DeviceTag` 已能满足配置化场景。

**符号寻址**：OPC UA / CIP / ADS 这类按名字寻址的协议不用再把符号拼进字符串，`DeviceAddress` 带一个可选 `Symbol`：

```csharp
var address = DeviceAddress.FromSymbol("ns=2", "s=Temperature", DeviceDataType.Double);
// Region="ns=2", Symbol="s=Temperature", Offset=0, ToString() => "ns=2:s=Temperature[Double]"
```

### 值元数据（Quality / Timestamp）

`DeviceValue` 除了载荷还携带可选的**质量**与**采样时间**，两者都是元数据，不参与相等性比较（轮询变更检测不受影响）：

```csharp
var value = DeviceValue.FromDouble(12.5)
    .WithQuality(DeviceValueQuality.Good)
    .WithTimestamp(DateTimeOffset.UtcNow);

bool usable = value.IsGood;      // Quality == Good
DateTimeOffset? sampledAt = value.Timestamp;
```

打开 `DriverConfiguration.StampReadTimestamps` 后，`DriverBase` 会给每次读与批量读的结果自动打上 UTC 采样时间；协议返回质量位时，驱动可以用 `WithQuality` 覆盖。

### 新增第三方驱动

```csharp
public sealed class MyProtocolFactory : IDeviceDriverFactory
{
    public static DriverDescriptor DefaultDescriptor { get; } =
        new("mydevice", "My Device", "Custom protocol", TransportKind.Tcp, 5000, new[] { "D100" });

    public DriverDescriptor Descriptor => DefaultDescriptor;

    public IDeviceDriver Create(DriverConfiguration configuration, ICommunicationLogger? logger = null, CommunicationMetrics? metrics = null) =>
        new MyProtocolDriver(configuration, logger, metrics);
}

// 只读驱动：声明能力 + 只实现 DoConnect / DoRead，写和批量钩子都不用写
public sealed class MyProtocolDriver : DriverBase
{
    public MyProtocolDriver(DriverConfiguration configuration, ICommunicationLogger? logger, CommunicationMetrics? metrics)
        : base(
            new DriverDescriptor(
                "mydevice", "My Device", "Custom protocol", TransportKind.Tcp, 5000,
                capabilities: DriverCapabilities.Read | DriverCapabilities.Subscribe),
            new MyAddressParser(),
            configuration,
            logger,
            metrics)
    {
    }

    protected override Task DoConnectAsync(CancellationToken ct) => /* open transport */ Task.CompletedTask;
    protected override Task DoDisconnectAsync() => /* close transport */ Task.CompletedTask;
    protected override Task<DeviceValue> DoReadAsync(DeviceAddress address, CancellationToken ct) => /* read */ throw new NotImplementedException();

    // 可选：接入协议原生订阅；不重写则自动回退到轮询 DoReadAsync
    protected override Task<IAsyncDisposable?> DoSubscribeAsync(
        DeviceAddress address,
        TimeSpan interval,
        Func<DeviceValue, Task> onChanged,
        CancellationToken ct) => /* subscribe */ Task.FromResult<IAsyncDisposable?>(null);
}

registry.Register(new MyProtocolFactory());
```

`DoReadAsync` / `DoWriteAsync` / `DoReadBatchAsync` / `DoWriteBatchAsync` / `DoSubscribeAsync` 都是可重写的虚方法，默认实现要么回退到更基础的操作，要么抛 `NotSupportedException`；配合 `DriverCapabilities`，只读或只写驱动不需要写任何「不支持」的空实现。新增协议仍然只需实现契约并注册，**不需要修改核心库**。

## 功能

| 协议 / 传输 | 说明 |
| --- | --- |
| Modbus RTU | 串口主站，支持 CRC16、3.5 字符帧间隔、RS-485 回显处理 |
| Modbus TCP | MBAP 帧、事务号校验，支持多个并发调用串行化 |
| Modbus 扩展 | FC23 原子读写（`ReadWriteRegistersAsync`）、FC14/15 文件记录、FC2B 设备标识、广播写（`unitId=0` 只发不等） |
| S7 | ISO-on-TCP (RFC 1006) + COTP + S7 PDU，支持 DB/M/I/Q 区、位/字节/字/双字/浮点/字符串、批量读写、位数组写、PDU 分片 |
| TCP | 原始 TCP 客户端传输，可用于自定义文本/二进制协议 |
| 自定义 TCP 协议 | `SiameseCat.Communication.Tcp`：原始/长度前缀/分隔符/定长分帧、可插拔 `ITcpFrameCodec`、`CustomTcpClient` 与 `CustomTcpSimulator`；`TcpFrameConnection` 同时被 Modbus TCP 的 MBAP 复用 |
| UDP | 连接式 UDP 传输，适用于自建数据报协议（协议超时与重试由驱动负责） |
| SerialPort | 原始串口传输，可配置波特率、校验位、数据位、停止位、流控 |
| 批量能力 | `IBatchReadable` / `IBatchWritable`：Modbus 合并连续地址为单帧，S7 使用多变量请求（含 `STRING`，不再逐条回退） |
| 订阅 | `ISubscribable` 单点轮询；`IBatchSubscribable` 一组点位一次请求（原生批量订阅可重写 `DoSubscribeBatchAsync`），均带抖动与失败退避 |
| 传输注入 | `ITransportFactory` + `RequestLock`：复用已有链路、自建隧道，或让多站共享一条 RS-485（`SharedTransportFactory`） |
| 帧日志 | `LogFrames = true` 时以 `Trace` 级别输出收发帧 hex，现场排障用 |
| 数值数组 | `DeviceAddress.Length` 表示元素个数，`DeviceValue.FromUInt16Array(...)` 等工厂与 `AsUInt16Array()` 访问器，Modbus/S7 直接按元素读写 |
| Modbus 从站 | `SiameseCat.Communication.Simulation.Modbus`：`ModbusTcpSimulator` / `ModbusRtuSimulator`（统一 `IModbusSimulator`）+ `ModbusDataStore`，支持 FC01/02/03/04/05/06/07/0B/0F/10/11/14/15/16/17/23/2B、并发客户端、广播写、坏 CRC 丢弃；提供写入事件、快照/恢复/复位、按功能码统计与日志 |
| S7 从站 | `SiameseCat.Communication.Simulation.S7`：`S7Simulator`（`IS7Simulator`）+ `S7DataStore`，处理 COTP 握手、Setup Communication、读/写变量（含多变量、位、位数组、STRING），支持 M/I/Q/DB 区、按需创建 DB、写入事件与请求统计 |
| Native AOT | 程序集标记 `IsAotCompatible`；现代目标启用裁剪/AOT 分析器，代码无反射与动态代码生成 |

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

多个项目、按层/协议拆分，依赖严格单向（契约 → 传输 → 分帧 → 协议 → 驱动/仿真/宿主）：

| 包 / 项目 | 职责 |
| --- | --- |
| `SiameseCat.Communication.Core` | 驱动契约、`DriverBase`、注册表、`DeviceAddress`/`DeviceValue`/`DeviceTag`、选项、异常、指标、`ITransport` 与 TCP/UDP/串口实现 |
| `SiameseCat.Communication.Tcp` | 与设备模型无关的分帧与连接核心：`TcpFrameCodec`、`TcpFrameConnection`、`CustomTcpClient`、`CustomTcpSimulator` |
| `SiameseCat.Communication.Modbus` | Modbus（RTU/TCP）帧（含 `ModbusTcpFrameCodec`）、客户端与通用 Modbus 驱动 |
| `SiameseCat.Communication.S7` | `S7Client`、`S7Address`、S7 选项与读写项、`S7Driver` 与原生地址映射 |
| `SiameseCat.Communication.Simulation.Modbus` | Modbus TCP/RTU 仿真器（`ModbusTcpSimulator` / `ModbusRtuSimulator`） |
| `SiameseCat.Communication.Simulation.S7` | S7（ISO-on-TCP）仿真器（`S7Simulator`） |
| `SiameseCat.Communication.Hosting` | DI 注册（`AddSiameseCat` / `AddModbusSimulator` / `AddS7Simulator`）、健康检查、日志适配器 |
| `SiameseCat.Communication.All` | `CreateDefaultRegistry()` / `AddSiameseCatDrivers()` 聚合入口 |

```text
src/
├── SiameseCat.Communication.Core/          # 契约 + 模型 + 驱动运行时 + 传输 + 诊断
│   ├── Addressing/                        # IDeviceAddressParser、DeviceAddress、DeviceValue、DeviceValueCodec、DeviceTag
│   ├── Diagnostics/                       # ICommunicationLogger、CommunicationMetrics、CommunicationDiagnostics
│   ├── Drivers/                           # IDeviceDriver、DriverBase、DriverRegistry、DriverConfiguration、RequestLock
│   ├── Exceptions/                        # CommunicationException 及子类
│   └── Transport/                         # ITransport + Tcp/Udp/SerialPort 实现
├── SiameseCat.Communication.Tcp/           # Framing/、Clients/、Simulator/
├── SiameseCat.Communication.Modbus/        # Clients/、Drivers/、CRC、寄存器转换、设备标识、文件记录
├── SiameseCat.Communication.S7/            # Clients/、Drivers/、S7Address、读写项
├── SiameseCat.Communication.Simulation.Modbus/  # Modbus/ 仿真器、PDU 处理器、数据存储、统计
├── SiameseCat.Communication.Simulation.S7/      # S7 仿真器、PDU 处理器、数据存储、统计
├── SiameseCat.Communication.Hosting/       # DI、仿真器托管、健康检查、日志适配
├── SiameseCat.Communication.All/           # 聚合注册入口
├── Shared/                                 # Guard.cs，按程序集引入
└── SimulatorShared/                        # SimulatorBufferSizes.cs，供两个仿真项目链接

tests/SiameseCat.Tests/                  # 单元 + 集成测试（Core / Modbus / S7 / Simulation / Integration / Fakes）
tests/SiameseCat.Compatibility/          # net48 冒烟：在 .NET Framework 4.8 上验证 netstandard2.0 资产
tests/SiameseCat.FrameworkMatrix/        # 框架矩阵：在 .NET 8 / 10 / 11 上验证实际加载的资产与 Modbus 往返
samples/SiameseCat.Samples/              # 按目录分层的使用教程：list / run <id> / selftest
tools/SiameseCat.Soak/                   # 长稳 + 故障注入：恢复、成功率、堆/句柄/线程增长校验
benchmarks/SiameseCat.Benchmarks/        # BenchmarkDotNet 基线
```

> 命名空间：`SiameseCat.Communication`、`.Transport`、`.Tcp`、`.Modbus`、`.S7`、`.Drivers`、`.Simulation.Modbus`、`.Simulation.S7`、`.Simulation.Tcp`、`.Hosting`。项目拆分不影响 `using`。

### 命名约定

| 约定 | 说明 | 示例 |
| --- | --- | --- |
| 包名 = 产品 + 层/协议 | 按层与协议拆包，消费者只还原所需依赖 | `SiameseCat.Communication.S7`、`.Simulation.Modbus` |
| 命名空间 = 产品 + 层/协议 | 命名空间保持不变，包拆分不影响 `using` | `SiameseCat.Communication.Modbus` |
| 领域模型用 `Device`，诊断用 `Communication` | 设备域类型统一 `Device` 前缀；日志、指标等通信诊断用 `Communication` 前缀 | `DeviceAddress`、`DeviceValue`、`DeviceTag`、`IDeviceDriver`、`ICommunicationLogger`、`CommunicationMetrics` |
| 驱动类按协议命名 | 通用驱动用 `协议 + Driver`，协议客户端保留协议名 | `ModbusDriver`、`S7Driver`、`S7Client` |
| 工厂 = 契约入口 | `IDeviceDriverFactory` 的实现类以 `DriverFactory` 结尾 | `ModbusDriverFactory`、`S7DriverFactory` |
| 分部类按职责拆文件 | 单类型职责超过 3 块或超过 ~300 行时用 `类型.职责.cs` | `DriverBase.Lifecycle.cs`、`S7Client.Protocol.cs` |
| 一个公共类型一个文件 | 仅紧耦合的辅助类型可同文件（枚举、事件参数、选项族） | `DeviceDataTypeExtensions.cs`、`Exceptions/*.cs` |
| 目录只留一层实现分组 | 协议根目录放帧与地址模型，`Clients/` 放会话，`Drivers/` 放驱动适配 | `Modbus/Clients/`、`S7/Clients/` |

依赖方向（严格单向，无环）：

```text
SiameseCat.Communication.Core                 契约 + 模型 + 驱动运行时 + 传输
├── SiameseCat.Communication.Tcp              分帧 + TcpFrameConnection + 自定义 TCP
│   └── SiameseCat.Communication.Modbus       Modbus 客户端 + 驱动（复用 TcpFrameConnection）
├── SiameseCat.Communication.S7               S7 客户端 + 驱动
├── SiameseCat.Communication.Simulation.Modbus / .Simulation.S7   可选：仿真器
└── SiameseCat.Communication.Hosting          可选：DI + 日志 + 健康检查 + 仿真器托管
SiameseCat.Communication.All                 聚合包，依赖以上全部
```

## 安装

```powershell
# 全部功能，等价于旧的单包
dotnet add package SiameseCat.Communication.All

# 只安装设备需要的部分
dotnet add package SiameseCat.Communication.S7
dotnet add package SiameseCat.Communication.Modbus
dotnet add package SiameseCat.Communication.Simulation.Modbus
dotnet add package SiameseCat.Communication.Hosting
```

每次发布也会镜像到 GitHub Packages，源地址与凭据见 [docs/publishing.md](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/publishing.md#4-github-packages-mirror)。

所有包同时提供 `net8.0`、`net10.0` 与 `netstandard2.0` 资产。`System.IO.Ports` 只在 Core；`Microsoft.Extensions.*` 抽象包只在 Hosting，因此纯 TCP 或纯 S7 消费方不会被动引入它们。

## Native AOT

所有库项目都启用了 `IsAotCompatible`，`net8.0`/`net10.0` 构建时会运行裁剪与 AOT 分析器；代码中没有反射、`dynamic`、运行时序列化或动态代码生成，`S7Address` 使用源生成正则。`PublishAot` 只应在可执行项目中开启，不要加在库上。`System.IO.Ports` 在 Windows/Linux 的 Native AOT 下可用，macOS 需要常规的 USB 串口驱动。

## 快速开始

### Modbus TCP

```csharp
using SiameseCat.Communication;
using SiameseCat.Communication.Modbus;

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
using SiameseCat.Communication.Modbus;
using SiameseCat.Communication.Transport;

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

### S7

```csharp
using SiameseCat.Communication.S7;

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

### 原始 TCP

```csharp
using System.Text;
using SiameseCat.Communication;

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

#### 自定义 TCP 协议

`SiameseCat.Communication.Tcp` 提供与设备模型解耦的分帧与请求/响应能力：

```csharp
using SiameseCat.Communication.Tcp;

var framing = new TcpFrameOptions { Mode = TcpFrameMode.Delimiter };   // CR LF

await using var client = new CustomTcpClient(new CustomTcpClientOptions
{
    Tcp = { Host = "127.0.0.1", Port = 5000 },
    Framing = framing,
    Timeout = TimeSpan.FromSeconds(2),
});

await client.ConnectAsync();
var answer = await client.RequestAsync(Encoding.ASCII.GetBytes("READ temperature"));
```

`TcpFrameOptions.Mode` 可选 `Raw`、`LengthPrefix`（1/2/4 字节长度字段，可配置字节序与是否含头）、
`Delimiter`（保留或去除分隔符）与 `FixedLength`。厂商协议若有转义或校验，实现 `ITcpFrameCodec` 即可。

服务端与共享连接核心：

```csharp
using SiameseCat.Communication.Simulation.Tcp;

await using var server = new CustomTcpSimulator(
    async (connection, token) =>
    {
        var request = await connection.ReadFrameAsync(token);
        if (request is not null)
        {
            await connection.SendAsync(request, token);
        }
    },
    new CustomTcpSimulatorOptions { Port = 5000, Framing = framing });
```

`TcpFrameConnection` 是可复用核心：在任意 `ITransport` 上收发完整帧，并可输出 Trace 帧日志；超时、
重试与并发串行化留给持有连接的调用方。Modbus TCP 通过 `ModbusTcpFrameCodec` 复用同一核心：MBAP
分帧由 codec 负责，事务号与单元号校验仍留在 `ModbusTcpClient`。

### 原始 UDP

```csharp
using SiameseCat.Communication.Transport;

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
using SiameseCat.Communication.Transport;

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

## Modbus 从站（联调 / 仿真）

`SiameseCat.Communication.Simulation.Modbus` / `.S7` 提供进程内的 Modbus **TCP 与 RTU** 从站，在没有真实设备时用于联调、集成测试和演示。TCP 从站只依赖 Core，RTU 从站复用协议层的 CRC 与传输层串口；两者共用同一套 PDU 处理逻辑。

```csharp
using SiameseCat.Communication.Simulation.Modbus;

await using var server = new ModbusTcpSimulator(new ModbusSimulatorOptions
{
    Host = "127.0.0.1",
    Port = 0,               // 0 = 由操作系统分配空闲端口
    HoldingRegisters = 512,
    Coils = 512,
});

await server.StartAsync();
server.Data.TryWriteHoldingRegisters(0, new ushort[] { 100, 200 });
server.Data.TryWriteCoils(10, new[] { true, false, true });

Console.WriteLine($"从站监听 127.0.0.1:{server.Port}");

await using var client = new ModbusTcpClient(new ModbusTcpOptions
{
    Tcp = { Host = "127.0.0.1", Port = server.Port },
});
await client.ConnectAsync();
var values = await client.ReadHoldingRegistersAsync(1, 0, 2);   // [100, 200]
```

- 支持功能码 `01/02/03/04/05/06/07/0B/0F/10/11/16/17/23`；越界地址返回 `02`，非法数量返回 `03`，未知功能码返回 `01`。
- 诊断类功能码：`07` 读异常状态（`Data.ExceptionStatus`，应用可随时修改）、`0B` 读通信事件计数（`Data.EventCount`，每次成功写入递增）、`11` 报表服务器标识（`ServerIdentifier` + `RunIndicator`）、`16` 掩码写保持寄存器（`(当前值 AND andMask) OR (orMask AND NOT andMask)`）。
- 单元号原样回显，一个从站可服务多个站号；单元号 `0` 视为广播，写请求生效但不返回响应（与客户端广播模式配套）。
- 支持并发客户端，每个连接一个会话任务；`StopAsync` / `DisposeAsync` 会关闭监听与全部会话。
- 每个会话复用请求/响应缓冲区，PDU 处理器直接写入该缓冲区，服务端处理一次请求不再分配帧内存（见[性能说明](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/architecture.md#performance-notes)）。
- 客户端同样低分配：Modbus TCP/RTU 与 S7 复用组帧缓冲区、内联重试循环，并在调用方未传取消令牌时复用请求超时 CTS，驱动单次读的分配量比优化前降低约三成（实测数字见性能说明）。
- `ModbusDataStore` 线程安全，可在运行期间修改输入寄存器与离散输入来模拟现场变化。

### 可观测性（统计与日志）

两个从站都实现 `IModbusSimulator`，并暴露 `Statistics`（`ModbusSimulatorStatistics`）：

```csharp
using var server = new ModbusTcpSimulator(new ModbusSimulatorOptions
{
    Port = 0,
    Logger = myCommunicationLogger, // 可选：实现 ICommunicationLogger 即可
    ServerIdentifier = "SIM-1",
});

await server.StartAsync();
// … 客户端读写 …

Console.WriteLine($"requests={server.Statistics.TotalRequests} "
    + $"exceptions={server.Statistics.TotalExceptions} "
    + $"frames={server.Statistics.FramesReceived} discarded={server.Statistics.FramesDiscarded} "
    + $"FC03={server.Statistics.GetRequests(0x03)}");
```

- 计数维度：总请求 / 每功能码请求 / 每功能码异常 / 接收帧数 / 丢弃帧数（TCP 长度非法、RTU CRC 错误或半帧）。
- 日志级别：连接建立/断开为 `Debug`；丢弃帧与未知功能码为 `Warning`；异常响应的细节为 `Debug`。未设置 `Logger` 时完全不产生日志。
- `Statistics.Reset()` 可清零计数器，便于按测试用例统计。

### 数据存储（`ModbusDataStore`）

```csharp
// 1. 监听写入：上位机写线圈/寄存器后联动仿真逻辑
server.Data.HoldingRegistersWritten += (_, e) =>
    Console.WriteLine($"HR{e.Start}.. = [{string.Join(", ", e.Values)}]（原值 [{string.Join(", ", e.PreviousValues)}]）");

server.Data.CoilsWritten += (_, e) =>
{
    if (e.Start == 10 && e.Values[0])
    {
        server.Data.TryWriteInputRegisters(0, new ushort[] { 1 });   // 模拟设备响应
    }
};

// 2. 预置/复位状态
var snapshot = server.Data.Snapshot();   // 四个区的不可变副本
server.Data.Reset();                     // 全部清零（相当于设备断电重启）
server.Data.Load(snapshot);              // 恢复快照，尺寸不一致会抛 ArgumentException
```

- 事件：`HoldingRegistersWritten` / `InputRegistersWritten`（`ModbusRegisterWriteEventArgs`）与 `CoilsWritten` / `DiscreteInputsWritten`（`ModbusCoilWriteEventArgs`），均带 `Start`、`Values`、`PreviousValues`；通过 Modbus 请求的写入同样会触发。
- 事件在锁外触发，回调里可以直接调用 `Read*` / `TryWrite*`，不会死锁。
- 每个区一把独立锁，寄存器访问不会阻塞线圈访问；线圈与离散输入以位图存储（65536 点占 8 KB）。
- 越界写入返回 `false` 且不触发事件。

### Modbus RTU 从站

同一套 PDU 逻辑也提供 RTU 版本，用于 RS-485 联调或串口仿真：

```csharp
using SiameseCat.Communication.Simulation.Modbus;

await using var slave = new ModbusRtuSimulator(new ModbusRtuSimulatorOptions
{
    UnitId = 1,
    Serial = { PortName = "COM3", BaudRate = 19200, ReadTimeout = TimeSpan.FromMilliseconds(100) },
    HoldingRegisters = 512,
});

await slave.StartAsync();
slave.Data.TryWriteHoldingRegisters(0, new ushort[] { 100, 200 });
```

- `Serial.ReadTimeout` 同时用作**帧间隔**：超时即认为当前帧结束（默认 100 ms，视波特率调整）。
- 只应答 `UnitId` 指定的站号；站号 `0` 的写请求生效但不响应；CRC 错误的帧直接丢弃（计入 `Statistics.FramesDiscarded`）。
- 功能码覆盖与 TCP 从站完全一致（含 `07/0B/11/16`）；请求长度按功能码推断，掩码写（`16`）为 8 字节固定长度帧。
- 也可以通过 `new ModbusRtuSimulator(ITransport, options)` 注入自定义链路（串口隧道或测试替身），便于在无硬件环境验证。

### S7 从站（ISO-on-TCP）

```csharp
using SiameseCat.Communication.Simulation.S7;
using SiameseCat.Communication.S7;

await using var s7 = new S7Simulator(new S7SimulatorOptions
{
    Host = "127.0.0.1",
    Port = 0,                     // 0 = 系统分配；真实 S7 端口是 102
    MemorySize = 512,             // M 区
    InputSize = 512,              // I 区
    OutputSize = 512,             // Q 区
    DefaultDataBlockSize = 1024,  // 按需创建的 DB 大小
});

await s7.StartAsync();
Console.WriteLine($"S7 从站监听 127.0.0.1:{s7.Port}");

// 现有 S7 客户端无需任何改动即可连接
await using var s7Client = new S7Client(new S7Options
{
    Host = "127.0.0.1",
    Port = s7.Port,
    Rack = 0,
    Slot = 1,
});

await s7Client.ConnectAsync();
await s7Client.WriteSingleAsync(S7Address.Parse("DB1.DBD0"), 12.5f);
float speed = await s7Client.ReadSingleAsync(S7Address.Parse("DB1.DBD0"));
```

- 处理 COTP 连接握手、Setup Communication（PDU 大小可配置，默认 960）、读变量（`0x04`）与写变量（`0x05`），支持多变量请求。
- 区域：M、I、Q 与数据块；各区域大小固定，数据块默认按需创建（`AutoCreateDataBlocks`），也可用 `TryEnsureDataBlock` 预建。
- 返回码遵循 S7 约定：`0x05` 地址越界、`0x0A` 对象不存在、`0x03` 拒绝访问（T/C 区）、`0x06` 数据错误；客户端侧表现为 `S7ItemException`。
- 位访问使用位地址（`M0.3`、`DB1.DBX0.1`）；字节访问会把位地址除以 8。
- 数据存储与 Modbus 一致：`ReadBytes` / `TryWriteBytes`、`TryReadBit` / `TryWriteBit`、`Written` 事件（含 `Values` / `PreviousValues`）与 `Reset()`。
- `Statistics` 提供连接数、活动连接、Setup/读/写请求数、读写变量数、条目错误与丢弃帧；`S7SimulatorOptions.Logger` 输出诊断日志。

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

### 轮询订阅（抖动与退避）

`SubscribeAsync` 在驱动没有原生订阅时按固定间隔轮询，间隔默认带 **±20% 抖动**，连续读失败时按指数退避，避免多个驱动同时轮询设备：

```csharp
var configuration = new DriverConfiguration
{
    SubscriptionJitter = true,                             // 默认：间隔随机化，防止同步抖动
    SubscriptionMaxBackoff = TimeSpan.FromSeconds(30),     // 默认：失败退避上限，0 表示关闭退避
};

await using var subscription = await driver.SubscribeAsync(
    driver.AddressParser.Parse("40001"),
    TimeSpan.FromSeconds(1),
    value => { Console.WriteLine(value); return Task.CompletedTask; });
```

- 抖动只作用于轮询间隔，不改变业务语义；变化检测仍按 `DeviceValue` 载荷比较。
- 失败退避为 `interval × 2^失败次数`（上限 `SubscriptionMaxBackoff`），首次读取成功后立即复位。
- 协议原生订阅（重写 `DoSubscribeAsync`）不受这两个设置影响。

#### 批量订阅（一组点位一次请求）

单个 `SubscribeAsync` 一次只读一个地址；点位多时用 `SubscribeBatchAsync` 把整组地址交给 `ReadBatchAsync`，每个周期只发一次请求（Modbus 会按区/连续性自动分组）：

```csharp
var batchSubscription = await driver.SubscribeBatchAsync(
    new[]
    {
        driver.AddressParser.Parse("40001"),
        driver.AddressParser.Parse("40002"),
        driver.AddressParser.Parse("40003"),
    },
    TimeSpan.FromSeconds(1),
    values =>
    {
        // values 顺序与地址顺序一致，任一值变化时回调
        Console.WriteLine($"{values[0]} {values[1]} {values[2]}");
        return Task.CompletedTask;
    });
```

- 回调参数是**整组**当前值：首次读取触发一次，之后任一点位变化时触发。
- 变化检测按元素逐个比较（数组值按元素相等性），载荷完全相同时不回调。
- 抖动与失败退避同样生效；指标记为 `BatchRead`。
- 需要声明 `DriverCapabilities.SubscribeBatch` 与 `DriverCapabilities.BatchRead`；协议原生批量订阅可重写 `DoSubscribeBatchAsync`，此时不需要 `BatchRead`。

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
| `device.requests` | Counter | `driver`、`operation` | 请求次数 |
| `device.failures` | Counter | `driver`、`operation`、`error` | 失败次数与错误类型 |
| `device.retries` | Counter | `driver` | 重试次数 |
| `device.bytes.read` / `device.bytes.written` | Counter | `driver` | 负载字节数 |
| `device.latency` | Histogram (ms) | `driver`、`operation` | 请求延迟 |

接入 OpenTelemetry 只需订阅计量器名称：

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(CommunicationDiagnostics.MeterName)   // "SiameseCat"
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
- 命名空间跟随分层：`SiameseCat.Communication`、`SiameseCat.Communication.Transport`、`SiameseCat.Communication.Modbus` / `.S7`。
- 所有公共 API 都有 XML 文档（`CS1591` 已启用），生成的 `.xml` 随 NuGet 包一起发布。

### 版本与打包

- 目标框架：`net8.0` + `net10.0`（.NET 8 / .NET 10 两个 LTS 代际）+ `netstandard2.0`（.NET Framework 4.6.1+、.NET Core 2.x+、.NET 5/6/7 等）；`IsAotCompatible` 与 Native AOT 分析对现代目标生效
- **上限不封顶**：目标框架是「下限」而不是「上限」——.NET 9 自动选用 `lib/net8.0`，.NET 11 / .NET 12 选用 `lib/net10.0`（`tests/SiameseCat.FrameworkMatrix` 在 .NET 8 / 10 / 11 上实跑同一套 Modbus 往返并打印实际加载的资产）。需要为新框架单独出资产时无需改代码：`dotnet pack -p:SiameseCatTargetFrameworks=net8.0;net11.0;netstandard2.0`
- 启用 .NET 分析器（`latest-recommended`）
- 启用包校验（`EnablePackageValidation`）：各资产（`net8.0` / `net10.0` / `netstandard2.0`）的公共 API 面必须一致，防止某个 API 被误放进 `#if` 分支
- 集中包版本管理（`Directory.Packages.props`）
- Release 构建生成 `.nupkg` 与 `.snupkg`，并启用 SourceLink
- 一键打包脚本：`./scripts/pack.ps1` 构建并校验包（三资产 + 包内 README）与符号包，输出大小与 SHA256；`-Version 0.9.0-preview.1` 覆盖版本，`-Push <源>` 直接推送（`NUGET_API_KEY` 环境变量提供密钥）
- 发布：CI 用 **NuGet Trusted Publishing**（GitHub OIDC 换取 1 小时临时 Key，仓库里不存任何 NuGet 密钥），只需在 nuget.org 建策略并在仓库设置变量 `NUGET_USER`；推送 `v*` 标签即由 `.github/workflows/release.yml` 自动发布，详见 [docs/publishing.md](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/publishing.md)
- CI 在每次 push / PR 时执行构建、测试和打包（见 [.github/workflows/ci.yml](.github/workflows/ci.yml)）；测试同时收集 XPlat 覆盖率，行覆盖率低于 **75%** 时任务失败（当前约 80%）
- 依赖升级由 `.github/dependabot.yml` 每周自动发起（NuGet 按 `Microsoft.Extensions.*`、测试工具分组；GitHub Actions 每月）；安全报告流程见 [SECURITY.md](SECURITY.md)

## 构建与测试

解决方案使用 `.slnx` 格式，需要 .NET SDK 10.0.100 或更高版本（`net10.0` 资产需要能编译该框架的 SDK；CI 使用 10.0.x）。运行框架矩阵需要装好对应运行时。

```powershell
dotnet build SiameseCat.slnx
dotnet test SiameseCat.slnx

# 覆盖率（CI 会做同样的事，行覆盖率低于 75% 会失败）
dotnet test tests/SiameseCat.Tests --collect:"XPlat Code Coverage" --results-directory test-results

# 示例目录（每个主题一个文件夹，无硬件也能跑）
dotnet run --project samples/SiameseCat.Samples -- list
dotnet run --project samples/SiameseCat.Samples -- run modbus/tcp
dotnet run --project samples/SiameseCat.Samples -- run drivers/batch
dotnet run --project samples/SiameseCat.Samples -- run simulation/s7

# 跑完全部带仿真器的示例（18 步，约 10 秒，失败返回 1）
dotnet run --project samples/SiameseCat.Samples -- selftest

# .NET Framework 4.8 兼容性验证（跑 netstandard2.0 资产，仅 Windows）
dotnet run --project tests/SiameseCat.Compatibility -c Release

# 框架矩阵：每个运行时实际加载哪个资产（需装好对应运行时）
dotnet run --project tests/SiameseCat.FrameworkMatrix -f net8.0
dotnet run --project tests/SiameseCat.FrameworkMatrix -f net10.0

# 基准测试（BenchmarkDotNet；-c Release 必须，结果写到 BenchmarkDotNet.Artifacts/）
dotnet run --project benchmarks/SiameseCat.Benchmarks -c Release -- --filter "*"
dotnet run --project benchmarks/SiameseCat.Benchmarks -c Release -- --filter "*AddressParsing*" --job short

# 打包
dotnet pack src/SiameseCat.Communication/SiameseCat.Communication.csproj -c Release -o artifacts
```

示例目录（`samples/SiameseCat.Samples`）用 `-- list` 看目录、`-- run <id>` 跑单步、`-- selftest` 跑全部无硬件示例；每个示例都支持 `--host` / `--port` / `--com` 直连真机，详见 [docs/tutorial.md](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/tutorial.md)。

## 当前限制

- Modbus 从站提供 TCP 与 RTU 版本（`SiameseCat.Communication.Simulation.Modbus`）；S7 仅支持 S7comm 的读/写变量，不支持上传/下载、诊断缓冲区等高级服务。
- 驱动层内置两个通用协议驱动：Modbus（RTU/TCP）与 S7；品牌专有地址映射按 `IDeviceDriverFactory` 以插件形式扩展（每个品牌都需按真机验证）。
- S7 目前只支持 TCP 传输；Modbus 支持 TCP/RTU。
- 数值数组支持 `Int16/UInt16/Int32/UInt32/Int64/UInt64/Float/Double`；`Bool` 只表示单个位（多位请用多个地址），`String`/`Raw` 继续用 `Length` 表示字符数/字节数。
- v1 不提供跨协议统一语义地址映射，`DeviceTag` 覆盖配置化场景。
- 串口没有可取消的异步 API：取消正在进行的读写会关闭串口，需要重新调用 `ConnectAsync`。
- 串口 `ReadTimeout` 建议小于协议 `Timeout`，否则超时检测会被串口读超时延迟。
- `netstandard2.0` 目标由 `tests/SiameseCat.Compatibility` 在 .NET Framework 4.8 上做冒烟验证（地址解析、Modbus TCP 往返、驱动契约、帧日志）；完整单元测试仍运行在 `net8.0`。该目标下 AOT 分析与源生成正则不生效，正则回退为编译缓存实现，TCP keep-alive 只能开启探测而不调优时间参数。

## 许可证

[MIT](LICENSE)
