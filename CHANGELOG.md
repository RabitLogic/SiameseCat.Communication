# Changelog

本项目遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。`SiameseCat` 是组织名，本仓库对应产品包
`SiameseCat.Communication`；后续产品（例如 `SiameseCat.Mes.Template`）是各自独立的仓库与包。

## [1.0.0] - 2026-10-19

首个版本：Modbus RTU/TCP 与 Siemens S7 的主站/从站、可插拔驱动模型、传输抽象、单包发布。

### 包与目标框架

- 单一 NuGet 包 **`SiameseCat.Communication`**，包内按命名空间分层：`SiameseCat.Communication`（契约 + 模型）、
  `.Transport`、`.Modbus`、`.S7`、`.Siemens`、`.Drivers`、`.Server.Modbus` / `.Server.S7`、`.Hosting`。
- 提供 `net8.0`、`net10.0` 与 `netstandard2.0` 三份资产：.NET Framework 4.6.1+、.NET Core 2.x+、.NET 5–7 走
  `netstandard2.0`，.NET 8 走 `net8.0`，.NET 10 走 `net10.0`。目标是**下限而非上限**：.NET 9 解析到
  `lib/net8.0`、.NET 11 及以后解析到 `lib/net10.0`，新运行时无需等待包更新；`tests/SiameseCat.FrameworkMatrix`
  在 .NET 8 / 10 / 11 上实跑同一套 Modbus 往返并校验实际加载的资产。需要为新框架额外出资产时用
  `-p:SiameseCatTargetFrameworks=net8.0;net11.0;netstandard2.0` 即可，无需改代码。
  `EnablePackageValidation` 校验各资产的公共 API 面完全一致；`IsAotCompatible` 对现代目标生效。
- Native AOT：程序集标记 `IsAotCompatible`，无反射、`dynamic`、运行时序列化与动态代码生成，`S7Address`
  使用源生成正则；`tools/SiameseCat.AotProbe` 覆盖 15 项无硬件检查，CI 在 Windows/Linux/macOS 原生运行，
  并通过 `PublishAotCross` 交叉编译 win-x64/arm64、linux-x64/arm64、linux-musl-x64/arm64、osx-x64/arm64，
  在 glibc 与 Alpine 容器中执行。
- 包内附带 `README.md`（`PackageReadmeFile`）与 XML 文档，启用 SourceLink 并同时产出 `.snupkg`。
- `scripts/pack.ps1` 一条命令完成构建、打包、三资产/README 校验、输出大小与 SHA256，并可选 `-Version` 覆盖版本、`-Push` 直接推送。
- 发布：新增 `.github/workflows/release.yml`（推送 `v*` 标签即构建 → 测试 → 框架矩阵 → 打包 → 推送 nuget.org → 创建 GitHub Release），采用 **NuGet Trusted Publishing**（GitHub OIDC 令牌换取 1 小时临时 API Key，仓库不保存密钥，只需配置 `NUGET_USER` 变量与 nuget.org 策略）；配套 [docs/publishing.md](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/publishing.md)（策略配置、本地预检、手工发布、私有源、故障排查与发布清单）；`scripts/pack.ps1 -Push` 支持 nuget.org、Azure Artifacts、Nexus 与文件夹源。

### 驱动模型

- `IPlcDriver` 生命周期契约 + 能力接口 `IReadable` / `IWritable` / `IBatchReadable` / `IBatchWritable` /
  `ISubscribable` / `IBatchSubscribable` / `IHealthCheckable`；`DriverDescriptor.Capabilities` 声明能力，
  `DriverBase` 在入口校验，越界调用直接抛 `NotSupportedException`。
- `DriverBase` 统一状态机（Disconnected → Connecting → Connected → Faulted）、事务锁、超时、重试、重连、
  日志与指标，按职责拆成 5 个 partial：`DriverBase.cs`、`.Lifecycle.cs`、`.Execution.cs`、`.Health.cs`、
  `.Subscription.cs`。
- `DriverRegistry` + `IPlcDriverFactory` 支持运行时按 id 创建驱动；注册显式进行（AOT 友好），
  非 AOT 场景可用 `RegisterFromAssembly` 扫描程序集。
- 两层地址模型：协议原生地址由 `IPlcAddressParser` 解析为
  `PlcAddress { Region, Offset, BitOffset?, DataType, Length, Symbol? }`；`PlcValue` / `PlcValueCodec`
  提供协议无关的值与字节转换；`PlcTag` 提供命名、缩放与单位。
- 数值数组：`PlcAddress.Length` 对数值类型表示元素个数，`PlcValue.FromUInt16Array` 等 8 个数组工厂与
  `As*Array()` 访问器，`IsArray` / `Length` / 按元素比较；Modbus 与 S7 驱动均按元素数换算寄存器/字节。
- 批量读写：`ReadBatchAsync` / `WriteBatchAsync` 合并连续地址（Modbus）或使用多变量请求（S7），
  保持返回顺序一致。
- 订阅：单点 `SubscribeAsync` 与整组 `SubscribeBatchAsync`（一次 `ReadBatchAsync` 请求/周期）；
  `SubscriptionJitter`（±20%）与 `SubscriptionMaxBackoff` 避免多驱动同时打断 PLC；
  `DoSubscribeAsync` / `DoSubscribeBatchAsync` 可接入协议原生订阅，返回 `null` 时回退轮询。
- 重试与恢复：瞬时错误按 `RetryDelay × 2^(n-1)` 退避（上限 `MaxRetryDelay`，带抖动），写操作默认不重试
  （`RetryWrites` 可开）；超时后重连以丢弃迟到响应；可选 Keep-Alive 探针在连续失败后自动重连。
- 单次调用超时：读写与批量 API 均提供 `TimeSpan timeout` 重载，可覆盖 `Timeout`。
- 选项加固：`DriverConfiguration` 提供 `GetOption(key, default, min, max)` / `GetByteOption` /
  `GetUInt16Option` / `GetBooleanOption` / `HasOption`；非法文本与越界值直接抛异常，不静默截断。
- 指标与诊断：`PlcMetrics` 按操作分类计数，延迟提供 P50/P95/Max（64 桶对数直方图，误差 ≤ 19%，无分配），
  失败按超时/连接断开/协议错误/其它分类；`PlcDiagnostics` 通过 `System.Diagnostics.Metrics` 发布
  `plc.requests`、`plc.failures`、`plc.retries`、`plc.bytes.*`、`plc.latency`（Meter 名 `SiameseCat`），
  可直接由 OpenTelemetry 订阅。

### Modbus

- RTU 与 TCP 主站：FC01/02/03/04/05/06/0F/10/17h；RTU 自带 CRC16、3.5 字符帧间隔、RS-485 回显处理，
  TCP 校验 MBAP 事务号。
- `ReadDeviceIdentificationAsync`（FC2B / MEI 0E）：基本、常规、扩展与单对象四种访问方式，返回原始字节
  + `VendorName` / `ProductCode` / `MajorMinorRevision` / `VendorUrl` / `ProductName` / `ModelName` /
  `UserApplicationName` 等便捷访问器；默认自动跟随 `MoreFollows` / `NextObjectId` 续页。
- `ReadFileRecordsAsync` / `WriteFileRecordsAsync`（FC14/15）：一次请求可读写多个文件记录（读最多 35 个
  子请求），结果按请求顺序返回；主站校验写入回声，`unitId = 0` 可广播写。
- `unitId = 0` 为广播：写请求只发送不等待响应，读请求直接拒绝。
- `ReadWriteRegistersAsync`（FC23）一次完成读+写，适合控制回路的设定值更新与反馈读取。
- `ModbusRegisterConverter` 支持 `short/ushort/int/uint/long/ulong/float/double/byte[]/string`，
  字序可切换（默认高字在前），字符串按 NUL 截断后再解码。
- `ModbusAddressParser` 支持 `40001` 等参考号、`HR10` / `IR0` / `C0` / `DI0` 命名偏移与 6 位参考号。
- `ModbusDriver` 合并同区域连续地址为单次请求（读 125 寄存器 / 2000 位，写 123 寄存器 / 1968 位），
  按 TCP/RTU 共用同一套 PDU 与转换逻辑。

### Siemens S7

- `S7Client` 实现 ISO-on-TCP（RFC 1006）+ COTP + S7 PDU：Setup Communication 协商 PDU 并据其分片，
  支持 DB/M/I/Q 区，位/字节/字/双字/浮点/字符串读写，多变量读写与位数组写（`WriteBitsAsync`）。
- `S7Address` 支持 `DB1.DBX0.0` / `DB1.DBW2` / `MW2` / `IW4` 等原生写法，`Parse` / `TryParse`，
  实现 `IEquatable` 可直接作字典键；`AddBits` 推进位地址。
- `SiemensDriver` + `SiemensAddressParser` 把 S7 地址映射为统一 `PlcAddress`；批量读写包含 `STRING`
  时不再整批回退，`STRING` 作为普通数据项参与多变量读写。

### 传输层

- `ITransport` 字节收发抽象 + `TcpTransport`（连接超时、Nagle、keep-alive 调优）、`UdpTransport`、
  `SerialPortTransport`（波特率/校验/数据位/停止位/流控可配）。
- `DriverConfiguration.TransportFactory` + `TransportKind.Custom` 可注入自定义传输（复用已有连接、隧道、
  测试桩）；`SharedTransportFactory` + `RequestLock` 让一条 RS-485 总线上的多个站共用一个串口而不交错。
- `LoggingTransport` 与 `LogFrames = true` 以 `Trace` 输出 `TX` / `RX` 帧 hex，便于现场排障。
- 串口路径可自动化验证：内部 `ISerialConnection` 抽象（真实实现包装 `System.IO.Ports.SerialPort`），
  无需硬件即可覆盖打开/关闭/超时/IO 错误/取消关闭端口/丢弃缓冲等分支，以及 RTU 客户端到从站的端到端往返。
- 串口取消语义明确：取消进行中的读写会关闭端口以中断阻塞调用，之后需重新 `ConnectAsync`。

### 从站仿真

- `ModbusTcpServer` / `ModbusRtuServer`（统一 `IModbusServer`）+ `ModbusDataStore`：支持
  FC01/02/03/04/05/06/07/0B/0F/10/11/14/15/16/17h/2B、并发客户端、广播写、坏帧丢弃、站号过滤与帧间隔超时；
  设备标识对象通过 `ModbusServerOptions.DeviceIdentification` 配置（默认厂商名、包名与程序集版本），
  文件记录通过 `ModbusDataStore.DefineFile` 定义并由 FC14/15 读写；RTU 从站按字节数字段推断 FC14/15 帧长，
  FC2B 为固定 7 字节帧；提供四个写入事件（带 `Start` / `Values` / `PreviousValues`）、`Snapshot()` /
  `Load()` / `Reset()`，以及按功能码统计的 `ModbusServerStatistics`。
- `S7Server`（`IS7Server`）+ `S7DataStore`：COTP 握手、Setup Communication（PDU 可配置）、读/写变量
  （含多变量、位、位数组、`STRING`），支持 M/I/Q/DB 区、按需创建数据块、`Written` 事件与统计。
- 从站共用 PDU 处理器与调用方缓冲区，支持 `IPlcLogger` 注入，记录连接、丢弃帧、未知功能码与异常响应。

### 宿主集成与可观测性

- `SiameseCat.Communication.Hosting`：`AddSiameseCat()` 注册 `DriverRegistry` 与 `IPlcLogger`；
  `AddSiameseCatDriver(id, configure)` 以 keyed transient 解析驱动；`AddModbusServer()` /
  `AddModbusRtuServer()` / `AddS7Server()` 把从站注册为单例并由单个 `IHostedService` 随宿主启停。
- 健康检查：`AddSiameseCatDriverCheck(...)` 把 `PingAsync` 暴露为 `IHealthCheck`，返回
  `Healthy` / `Unhealthy` / `Degraded`，报告携带驱动状态、请求/失败/重试计数、失败分类与 P50/P95，
  可直接挂到 ASP.NET Core `MapHealthChecks` 或 Kubernetes 探针。
- `MicrosoftLoggerAdapter` 把 `IPlcLogger` 适配到 `Microsoft.Extensions.Logging`（`LoggerMessage` 源生成，
  AOT 友好）；未注册日志时回退到 `NullPlcLogger`。

### 性能

- 从站热路径：会话复用请求/响应缓冲区，PDU 处理器写入调用方 `Span<byte>`，数据存储直接复制寄存器/位/字节，
  入站载荷用 `stackalloc` 解析，写入事件仅在存在订阅者时分配。单请求分配下降 11.5%–21.5%。
- 客户端热路径：Modbus TCP/RTU 与 S7 复用帧缓冲区，S7 组帧直写发送缓冲、响应只读入一次，重试循环内联，
  未传令牌时复用超时 `CancellationTokenSource`；`DriverBase` 用 `Stopwatch.GetTimestamp()` 计时并惰性构造
  失败消息，`PlcDiagnostics` 缓存标签值。单次请求分配下降 27.8%–36.6%。

### 示例与文档

- `samples/SiameseCat.Samples`：按目录分层的 17 个可运行示例（Modbus、S7、Drivers、Server、Transports），
  每个带内置仿真器可离线运行，也支持 `--host` / `--port` / `--com` 直连真机；`list` 看目录、
  `run <id>` 跑单步、`selftest` 全量自检。
- 文档：英文 [README](README.md)（同时作为包内 README）与中文 [README.zh-CN.md](README.zh-CN.md)，
  以及 `docs/` 下的[架构](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/architecture.md)、
  [协议矩阵](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/protocol-matrix.md)、
  [迁移与接入](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/migration.md)与
  [教程](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/tutorial.md)。

### 质量与工程化

- 352 个单元/集成测试，行覆盖率 79.8%、分支覆盖率 64.9%（CI 门禁 75%）；`tests/SiameseCat.FrameworkMatrix` 在 .NET 8 / .NET 10 / .NET 11 上分别加载对应资产并完成同一套 Modbus 往返（.NET 11 取 `net10.0` 资产）；
  Release 构建零警告，启用 `latest-recommended` 分析器与 `CS1591` XML 文档检查，`dotnet format --verify-no-changes` 通过。
- `tests/SiameseCat.Compatibility` 在 .NET Framework 4.8 上实际运行 `netstandard2.0` 资产（CI Windows 任务）。
- `tools/SiameseCat.Soak`：多个驱动经 TCP 代理（按计划切断链路、拒绝新连接）访问内置从站，逐秒输出
  ops / 成功率 / 托管堆 / 工作集 / 线程 / 句柄，结束时校验恢复、成功率与资源增长；CI 每次推送跑 1 分钟。
- `benchmarks/SiameseCat.Benchmarks` 提供 BenchmarkDotNet 基线（寄存器转换、CRC、地址解析、值编解码、
  从站吞吐）。
- CI 校验包内 `lib/net8.0`、`lib/net10.0`、`lib/netstandard2.0`、`README.md` 与文档链接，并在 .NET 8 / .NET 10 上运行框架矩阵；Dependabot 每周更新依赖，
  安全报告流程见 [SECURITY.md](SECURITY.md)。

## 版本约定

- 采用语义化版本；`EnablePackageValidation` 以当前版本为基线校验两个 TFM 的 API 一致性。
- 给接口新增成员对外部实现者是破坏性变更，因此新的可选能力以独立接口发布（例如 `IBatchSubscribable`）。
- 弃用 API 至少保留一个次版本，并标注 `[Obsolete]` 与替代方案。
