# Changelog

本项目遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [1.0.0] - 2026-10-18

### 新增

- **可插拔驱动架构**（`SiameseCat.Core`）：`IPlcDriver` 生命周期契约 + `IReadable` / `IWritable` / `IBatchReadable` / `ISubscribable` 能力接口；`DriverBase` 统一状态机（Disconnected → Connecting → Connected → Faulted）、事务锁、超时、重试、重连、日志与指标；`DriverRegistry` + `IPlcDriverFactory` 支持运行时发现与第三方插件。
- **两层地址模型**：协议原生地址字符串由 `IPlcAddressParser` 解析为 `PlcAddress { Region, Offset, BitOffset?, DataType, Length }`；`PlcValue` / `PlcValueCodec` 提供协议无关的值与字节转换；`PlcTag` 提供命名、缩放与单位。
- **Modbus 驱动**：`ModbusAddressParser` 支持 `40001` / `HR10` / `IR0` / `C0` / `DI0` 与 6 位参考号；`ModbusDriver` 由 TCP 与 RTU 共用 PDU、数据转换与批量合并逻辑，仅帧封装不同。
- **西门子 S7 驱动**（`SiameseCat.Drivers.Siemens`）：`SiemensAddressParser` 将 `DB1.DBX0.0` / `MW2` / `IW4` 映射为统一地址；`SiemensDriver` 支持批量多变量读取，`readBatch` 自动使用 S7 原生多读。
- **批量优化**：Modbus 自动合并同区域连续地址为单次请求；S7 使用多变量请求；`ReadBatchAsync` 保持请求顺序。
- **汇川品牌驱动**（`SiameseCat.Drivers.Inovance`）：`InovanceAddressParser` 把 `D` / `M` / `X` / `Y` 软元件（`X`/`Y` 八进制）映射到 Modbus 保持寄存器、线圈与离散输入；`InovanceMapping` 允许按系列覆盖 `dBase` / `mBase` / `xBase` / `yBase`；底层复用协议层 `SiameseCat.Protocols.Modbus` 的 PDU、批量合并与数据转换。
- **生态**：`IPlcLogger` / `PlcMetrics` / `PlcMetricsSnapshot`；`SiameseCat.Drivers` 品牌驱动包（含一次性注册入口）；演示程序新增 `driver` 命令。
- `SiameseCat.Core`：`ITransport` 抽象、通讯异常体系、通用选项、重试策略、定长读取扩展。
- `SiameseCat.Transport`：TCP 客户端传输（`TcpTransport`），支持连接超时、Nagle 开关、keep-alive 调优。
- `SiameseCat.Transport`：可配置串口传输，取消读写时安全关闭端口。
- `SiameseCat.Protocols`（Modbus）：Modbus RTU / TCP 主站，支持功能码 01/02/03/04/05/06/15/16/23。
- `SiameseCat.Protocols`（Modbus）：CRC16、寄存器与 `short/ushort/int/uint/long/ulong/float/double/byte[]/string` 转换、字序切换。
- `SiameseCat.Protocols`（S7）：ISO-on-TCP + COTP + S7 PDU 客户端，支持 DB/M/I/Q 区、位/字节/字/双字/浮点/字符串。
- `SiameseCat.Protocols`（S7）：批量读写、PDU 协商与自动分片、`S7Address` 解析（含 `TryParse` 与值相等）。
- 全部公共 API 提供 XML 文档（`CS1591` 启用），异常消息包含单元号、功能码和 S7 地址等上下文。
- **Modbus 扩展**：驱动层新增 `ReadWriteRegistersAsync`（FC23 原子读写）；`unitId=0` 进入广播模式，写请求只发送不等待响应（读请求直接拒绝），`ModbusOptions.Validate()` 与 `DefaultUnitId` 同样接受 0。
- **S7 位数组写**：新增 `S7Client.WriteBitsAsync(startAddress, bool[])`，连续位一次多变量写请求，自动按 PDU 分块，可跨字节边界；`S7Address.AddBits` 提供位地址推进。
- **指标增强**：`PlcMetrics` 按操作（读 / 写 / 批量读 / 批量写 / 读写 / 健康探针）分别计数，延迟提供 `P50` / `P95` / `Max`（64 桶对数直方图，误差 ≤ 19%，无分配），失败按超时 / 连接断开 / 协议错误 / 其它分类；新增 `PlcDiagnostics`，通过 BCL 的 `System.Diagnostics.Metrics` 发布 `plc.requests`、`plc.failures`、`plc.retries`、`plc.bytes.*`、`plc.latency`（计量器名 `SiameseCat`），可直接由 OpenTelemetry 订阅。
- **单次调用超时**：`IReadable` / `IWritable` / `IBatchReadable` / `IBatchWritable` 新增带 `TimeSpan timeout` 的重载，可按调用覆盖 `Timeout`，适合大块数据读取或慢点位快速失败。
- **传输注入与共享链路**：`DriverConfiguration.TransportFactory`（`ITransportFactory`）+ `TransportKind.Custom` 让驱动改用外部传输——复用已有连接、把协议塞进自定义隧道，或让一条 RS-485 总线上的多个站共用一个串口；`SharedTransportFactory` + `RequestLock` 保证多站请求不交错，`DriverConfiguration.Validate()` 在 `Custom` 缺少工厂时直接报错，Modbus 用 `framing` 选项（`tcp` / `rtu`）决定自定义传输上的组帧方式。
- **能力声明**：`DriverDescriptor.Capabilities`（`Read` / `Write` / `BatchRead` / `BatchWrite` / `Subscribe` / `Health`，默认 `All`），`DriverBase` 在读、写、批量、订阅、探针入口校验；`DoReadAsync` / `DoWriteAsync` 改为可重写虚方法（默认抛 `NotSupportedException`），只读/只写驱动不再需要空实现；启用 Keep-Alive 但未声明 `Health` 时在构造期报错。
- **原生订阅钩子**：新增 `protected virtual DoSubscribeAsync`，返回句柄即走协议原生订阅，返回 `null`（默认）时沿用轮询；订阅句柄释放时同时取消轮询并释放原生订阅。
- **帧日志**：`LogFrames = true` 时协议客户端用 `LoggingTransport` 包装传输，以 `Trace` 级别输出 `TX` / `RX` 帧 hex（`CommunicationOptions.Logger` / `LogFrames`，AOT 友好、无反射）。
- **地址与值模型扩展**：`PlcAddress.Symbol` + `PlcAddress.FromSymbol` 让 OPC UA / CIP / ADS 这类符号寻址协议不再退化到字符串拼接；`PlcValue.Quality`（`Good` / `Uncertain` / `Bad`）与 `PlcValue.Timestamp` 提供质量与采样时间，`DriverConfiguration.StampReadTimestamps` 可让每次读与批量读自动打 UTC 时间戳；质量/时间戳不参与相等性比较，轮询变更检测行为不变。
- **多目标框架**：5 个库同时提供 `net8.0`（.NET 8 及以上）与 `netstandard2.0`（.NET Framework 4.6.1+、.NET Core 2.x+、.NET 5/6/7），可被 .NET Framework 4.8 等存量宿主引用；新增共享 `Guard`（替代 net6+ 的 `ThrowIf*`）与 `IntervalTimer`（替代 `PeriodicTimer`），`netstandard2.0` 下正则回退到编译缓存实现、UDP 收发用 `ArrayPool` 桥接 Span API；`IsAotCompatible` 只对 `net8.0` 生效，AOT 发布改为只在探针项目启用 `PublishAot`。启用 `EnablePackageValidation` 保证两个 TFM 的公共 API 面一致。新增 `tests/SiameseCat.Compatibility`（net48 冒烟运行器，10 项检查）在 .NET Framework 4.8 上实际验证 netstandard2.0 资产，CI 增加对应 Windows 任务。
- **Modbus TCP 从站**（`SiameseCat.Server`，可选）：`ModbusTcpServer` + `ModbusDataStore`，支持 FC01/02/03/04/05/06/15/16/23、并发客户端、单元号回显与广播写（写生效不响应），用于无真实 PLC 时的联调、仿真与集成测试；演示程序、AOT 探针与 .NET Framework 兼容性运行器全部改用它，删除了三份重复的测试从站实现。
- **数组与结构体类型**：`PlcAddress.Length` 对数值类型表示元素个数（`UInt16` × 10 = 10 个寄存器 / 20 字节），新增 `PlcValue.FromUInt16Array` / `FromInt32Array` / `FromFloatArray` 等 8 个数组工厂与对应 `As*Array()` 访问器；`IsArray` / `Length` / 按元素比较与克隆语义齐备，标量访问器对数组抛 `InvalidCastException`。Modbus 驱动按元素自动换算寄存器数（读写均支持），S7 驱动经 `PlcValueCodec` 数组编解码直接映射，批量读的分组计算也按数组宽度展开。
- **Modbus RTU 从站**：新增 `ModbusRtuServer`，与 TCP 从站共用 `ModbusPduProcessor`，提供 RTU 组帧、CRC 校验（坏帧丢弃）、站号过滤、广播写与帧间隔超时；可通过 `ITransport` 注入自定义链路便于测试。`SiameseCat.Server` 因此依赖协议层（CRC）与传输层（串口）。
- **S7 STRING 批量读写**：`SiemensDriver` 的批量读写不再因包含 `STRING` 而整批回退——批量写把 `[maxLen, actualLen, chars]` 作为普通数据项放进多变量写，批量读按 `maxLen + 2` 请求并就地解码。
- **订阅抖动与退避**：`DriverConfiguration.SubscriptionJitter`（默认 `true`，间隔 ±20%）与 `SubscriptionMaxBackoff`（默认 30 s，失败按 `interval × 2^n` 指数退避，成功后复位）避免多个驱动同步轮询打断 PLC。
- **Modbus 从站协议补齐**：新增 FC07 读异常状态、FC0B 读通信事件计数、FC11 报表服务器标识（`ServerIdentifier` / `RunIndicator`）与 FC16 掩码写保持寄存器；TCP 从站允许单字节 PDU（`07/0B/11` 请求仅 1 字节），RTU 从站按功能码推断帧长（掩码写 8 字节固定帧）。
- **代码组织**：`ModbusDriver` 拆为 `ModbusDriver.cs`（生命周期与 FC23）、`.Read.cs`、`.Write.cs`、`.Conversion.cs` 四个 partial；`ModbusClientBase` 拆为 `ModbusClientBase.cs`（请求管线与帧解析）、`.Read.cs`、`.Write.cs`，公共 API 不变。
- **性能优化（从站热路径）**：TCP/RTU 会话复用请求/响应缓冲区，`ModbusPduProcessor` 与 `S7PduProcessor` 改为写入调用方提供的 `Span<byte>`；数据存储直接把寄存器/位/字节复制进响应（`TryCopyHoldingRegisters` / `TryCopyBits` / `TryCopyBytes`），入站载荷用 `stackalloc` 解析，S7 路径不再使用 `MemoryStream`；写入事件仅在存在订阅者时才分配 `Values`/`PreviousValues`。实测单请求分配：Modbus FC03 −21.5%、FC16 −11.5%、S7 读 −17.3%、S7 写 −12.4%（详见 `docs/architecture.md` 的性能说明），基准测试新增 `SlaveThroughputBenchmarks`。
- **性能优化（协议库热路径）**：Modbus TCP/RTU 与 S7 客户端改用缓冲区复用（`ArrayPool` 或每实例固定缓冲区）、S7 组帧直写发送缓冲区且响应只读入一次、重试循环内联（去掉闭包与额外状态机）、调用方未传令牌时复用请求超时 `CancellationTokenSource`（.NET 6+ `TryReset`）；`DriverBase` 改用 `Stopwatch.GetTimestamp()`、惰性构造失败消息，`PlcDiagnostics` 缓存操作名标签值。实测单次请求分配：Modbus 读 −32.2%、Modbus 写 −36.6%、S7 读 −31.8%、S7 写 −35.4%、驱动读 −29.8%、驱动写 −27.8%（详见 `docs/architecture.md` 性能说明）。
- **从站目录与命名空间按协议拆分**：`SiameseCat.Server` 由扁平的 20 个文件改为 `Modbus/` 与 `S7/` 两个目录，命名空间相应变为 `SiameseCat.Server.Modbus` / `SiameseCat.Server.S7`（引用方需补一个 `using`）；共享的缓冲区尺寸校验收敛为内部 `ServerBufferSizes`。
- **工程化**：CI 测试步骤收集 XPlat 覆盖率并设 75% 行覆盖率门禁、上传 TRX 与 Cobertura 报告；新增 `.github/dependabot.yml`（NuGet 分组 + GitHub Actions 月度）与 `SECURITY.md`。
- **S7 从站**：新增 `S7Server`（`IS7Server`）与 `S7DataStore`，处理 COTP 握手、Setup Communication（PDU 可配置）、读/写变量（含多变量、位、位数组、`STRING`），支持 M/I/Q/DB 区、按需创建数据块、`Written` 事件、`Reset()` 与 `S7ServerStatistics`；Hosting 新增 `AddS7Server()`。net48 运行器与 AOT 探针均增加 S7 从站往返检查。
- **文档重构**：`README.md` 改为英文主文档（同时作为 NuGet 包内 README），原中文全文移至 `README.zh-CN.md`；新增 `docs/` 深度文档（[架构](docs/architecture.md)、[协议矩阵](docs/protocol-matrix.md)、[迁移与升级](docs/migration.md)），包内 README 用绝对 GitHub 链接指向它们；CI 新增文档链接检查与「包内必须包含 README.md」校验。
- **基准测试**：新增 `benchmarks/SiameseCat.Benchmarks`（BenchmarkDotNet + `[MemoryDiagnoser]`），覆盖寄存器转换/CRC、地址解析（Modbus 参考号、命名地址、S7）与值编解码（标量、数组、相等性），结果目录已加入 `.gitignore`。
- **从站可观测性**：`ModbusTcpServer` / `ModbusRtuServer` 都实现新的 `IModbusServer` 并暴露 `Statistics`（总请求、每功能码请求/异常、接收帧、丢弃帧），`ModbusServerOptions` / `ModbusRtuServerOptions` 新增 `Logger`（`IPlcLogger`），记录连接、丢弃帧、未知功能码与异常响应。
- **从站 DI 集成**：`SiameseCat.Hosting` 新增 `AddModbusServer(...)` / `AddModbusRtuServer(...)`，把从站注册为 `IModbusServer` 单例并通过单个 `IHostedService` 随宿主启停，未设置 `Logger` 时自动使用 DI 中的 `IPlcLogger`。
- **批量订阅**：新增 `IBatchSubscribable.SubscribeBatchAsync` 与 `DriverCapabilities.SubscribeBatch`，一组点位共用一次 `ReadBatchAsync` 请求（Modbus 按区/连续性分组），回调收到整组当前值、仅在任一点位变化时触发；协议原生批量订阅可重写 `DoSubscribeBatchAsync`，返回 `null` 时回退轮询（需要 `BatchRead` 能力）。
- **从站数据存储增强**：`ModbusDataStore` 新增四个写入事件（`HoldingRegistersWritten` / `InputRegistersWritten` / `CoilsWritten` / `DiscreteInputsWritten`，带 `Start` / `Values` / `PreviousValues`，锁外触发），支持 `Snapshot()` / `Load()` / `Reset()` 预置与复位状态；内部改为按区独立锁 + 线圈位图存储（65536 点占 8 KB，原为 64 KB）。
- **宿主集成包**（`SiameseCat.Hosting`，可选）：`MicrosoftLoggerAdapter` 把 `IPlcLogger` 适配到 `Microsoft.Extensions.Logging`（`LoggerMessage` 源生成，AOT 友好）；`AddSiameseCat()` 注册 `DriverRegistry` 与日志适配器，`AddSiameseCatDriver(id, configure)` 以 keyed transient 方式解析驱动。
- **重试与恢复**：请求与建连都按瞬时错误重试，退避为 `RetryDelay × 2^(n-1)`（上限 `MaxRetryDelay`，默认带 50–100% 抖动）；可选 Keep-Alive 探针在连续失败后自动重连，`PingAsync` 提供只返回布尔值的健康检查。
- **批量写**：`IBatchWritable.WriteBatchAsync(IReadOnlyList<PlcWriteRequest>)`；Modbus 合并连续保持寄存器为 FC16、连续线圈为 FC15，S7 使用多变量写请求并按协商 PDU 自动分块。
- **选项解析加固**：`DriverConfiguration` 新增 `GetByteOption` / `GetUInt16Option` / `GetBooleanOption` / `HasOption` 与带范围校验的 `GetOption`；`unitId`、`rack`、`slot` 不再静默截断，非法文本与越界值直接抛异常；S7 的 `rack`（0–7）与 `slot`（0–31）在创建时校验。
- **UDP 传输**：新增 `UdpTransport` / `UdpTransportOptions`（连接式 UDP 套接字），可用于自定义数据报协议。
- **文档**：新增「驱动选项」表（每个选项的适用驱动、取值、默认值与含义）。
- 瞬时错误重试与自动重连；写操作默认不重试，可通过 `RetryWrites` 开启。
- 超时/取消后的连接重同步策略，避免迟到响应污染后续请求。
- 全部库支持 Native AOT：启用裁剪/AOT 分析器，`S7Address` 使用源生成正则，无反射与动态代码。
- 新增 `tools/SiameseCat.AotProbe` 无硬件 AOT 探针和跨平台发布脚本，CI 在 win-x64/linux-x64/osx-arm64 上发布并运行。
- 引入 `PublishAotCross.Tools`，支持从任意宿主交叉编译 win-x64/win-arm64、linux-x64/linux-arm64、linux-musl-x64/linux-musl-arm64、osx-x64/osx-arm64。
- 新增 Docker 验证流程：交叉编译后在 glibc / Alpine 容器中运行探针。
- 单元测试、内置 Modbus TCP 从站自测、CI 工作流（含 Native AOT 发布与运行）、SourceLink 与符号包。

### 质量

- **四层架构**：程序集按层划分——`SiameseCat.Core`（契约 + 模型）→ `SiameseCat.Transport`（TCP + 串口）→ `SiameseCat.Protocols`（Modbus / S7）→ `SiameseCat.Drivers`（Siemens / Inovance 品牌驱动），引用严格单向；程序集数量 7 → 4。SerialPort 不再单独成层，S7 与 Modbus 归入协议层，品牌驱动统一放在品牌层。
- **命名空间跟随分层**：`SiameseCat.Protocols.Modbus` / `.S7`、`SiameseCat.Drivers.Siemens` / `.Inovance`；驱动类统一品牌命名（`SiemensDriver` / `InovanceDriver`），协议类保留协议名（`S7Client` / `S7Address`）。
- **目录收敛**：层内按协议/品牌分组，只保留有内容的目录（`Protocols/Modbus/{Clients,Drivers}`、`Protocols/S7/Clients`、`Drivers/{Siemens,Inovance}`），Core 固定为 `Addressing/` + `Drivers/` + `Transport/` + 库级公共类型；协议根目录直接放帧与地址模型，去掉 `Modbus/Protocol`、`S7/Addressing` 这类冗余嵌套。
- **工程改名**：`SiameseCat.Integration` → `SiameseCat.Hosting`（宿主集成：DI + `Microsoft.Extensions.Logging`），`tools/SiameseCat.Tools` → `tools/SiameseCat.AotProbe`（名副其实的 AOT 探针），`MicrosoftPlcLogger` → `MicrosoftLoggerAdapter`；`AddSiameseCat()` 放入 `Microsoft.Extensions.DependencyInjection` 命名空间，`using Microsoft.Extensions.DependencyInjection;` 即可调用。
- **模型命名统一**：`Tag` → `PlcTag`、`IAddressParser` → `IPlcAddressParser`，与 `PlcAddress` / `PlcValue` / `IPlcDriver` 前缀规则一致。
- **Core 按领域组织**：Core 从「扁平 3 目录 + 根级散落类型」改为「契约与实现同域」——`Addressing/`（地址模型 + `IPlcAddressParser`）、`Diagnostics/`（`IPlcLogger` + 适配器/实现 + `PlcMetrics`）、`Drivers/Abstractions/`（驱动契约、描述符、状态）、`Drivers/Runtime/`（`DriverBase` 5 个 partial、注册表、配置）、`Exceptions/`（异常体系）、`Transport/`（`ITransport` + 助手）；一个公共类型一个文件（扩展类、异常类各自独立）。
- **DriverBase 拆分**：从 819 行拆成 5 个分部文件——`DriverBase.cs`（契约与读写 API）、`DriverBase.Lifecycle.cs`（连接/断开/释放状态机）、`DriverBase.Execution.cs`（超时、重试、重连管线）、`DriverBase.Health.cs`（`PingAsync` 与 Keep-Alive）、`DriverBase.Subscription.cs`（轮询订阅），公共 API 未变。
- **测试命名空间对齐目录**：`SiameseCat.Tests.Core` / `.Modbus` / `.Siemens` / `.Inovance` / `.Integration` / `.Fakes`，不再混用 `SiameseCat.Tests` 与 `.S7`。

- 全部公共 API 补齐 XML 文档，启用 `CS1591` 检查（构建零警告），文档随 NuGet 包发布。
- `S7Client` 按职责拆分为连接、读、写、协议四个 partial 文件，便于维护。
- `ModbusRegisterConverter` 新增 `UInt64` / `Int64` 与原始字节数组转换；字符串先按 NUL 截断再解码，避免多字节编码被截断。
- `S7Address` 实现 `IEquatable<S7Address>` 与 `==` / `!=`，可直接作为字典键；解析使用固定区域性。
- Modbus 的 `ConnectAsync` / `DisconnectAsync` 与请求共用同一把锁，避免打断进行中的请求。
- 异常消息补充单元号、功能码、S7 地址/条目序号等上下文，便于现场诊断。
- 单元测试从 81 个增加到 95 个。
- 新增驱动契约测试（Mock 驱动）、Modbus/Siemens/Inovance 地址解析与驱动测试，测试总数增加到 356 个。
