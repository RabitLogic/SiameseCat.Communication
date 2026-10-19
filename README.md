# SiameseCat

[English](README.md) · [简体中文](README.zh-CN.md)

A lightweight .NET communication library for PLCs and industrial devices. It targets **.NET 8 (LTS)** and
**.NET Standard 2.0** (so .NET Framework 4.8 hosts can reference it too).

- **Pluggable drivers** — one contract (`IPlcDriver` / `DriverBase`) with capability flags, batch
  read/write, polling or native subscriptions, health probes and metrics.
- **Protocols built in-house** — Modbus RTU/TCP, Siemens S7 (ISO-on-TCP + COTP + S7 PDU), Inovance
  (D/M/X/Y mapping onto Modbus), plus raw TCP, UDP and serial transports. No NModbus/Sharp7 dependency.
- **Simulation included** — `SiameseCat.Server` ships Modbus TCP/RTU slaves **and an S7 (ISO-on-TCP) slave**
  with thread-safe data stores, write events, snapshots and per-request statistics.
- **Zero-warning, multi-target, Native AOT ready** — analyzer-clean builds, package validation for both
  TFMs, and an AOT probe that is published and executed in CI for Windows, Linux and macOS.

Deeper documentation:

- [Architecture and layering](https://github.com/RabitLogic/SiameseCat/blob/main/docs/architecture.md)
- [Protocol matrix (addresses, function codes, data types)](https://github.com/RabitLogic/SiameseCat/blob/main/docs/protocol-matrix.md)
- [Migration and upgrade notes](https://github.com/RabitLogic/SiameseCat/blob/main/docs/migration.md)
- [Comparison with S7.Net Plus](https://github.com/RabitLogic/SiameseCat/blob/main/docs/comparison.md)

## Packages

| Package | Layer | Responsibility | Depends on |
| --- | --- | --- | --- |
| `SiameseCat.Core` | Contracts | Driver contracts, address/value model, `DriverBase`, registry, logging, metrics, `ITransport` | — |
| `SiameseCat.Transport` | Transport | TCP, UDP and serial port transports | Core, `System.IO.Ports` |
| `SiameseCat.Protocols` | Protocol | Modbus (RTU/TCP) and Siemens S7 frames/clients, generic Modbus driver | Core, Transport |
| `SiameseCat.Drivers` | Brand | Siemens and Inovance drivers (native address mapping) + registration entry point | Core, Protocols |
| `SiameseCat.Hosting` | Host integration (optional) | `Microsoft.Extensions.Logging` adapter, DI registration, slave hosting | Core, Drivers, Server, `Microsoft.Extensions.*` |
| `SiameseCat.Server` | Slave (optional) | Modbus TCP/RTU and S7 (ISO-on-TCP) slaves with statistics and logging, for integration tests and simulation | Core, Protocols, Transport |

## Install

```bash
dotnet add package SiameseCat.Core
dotnet add package SiameseCat.Transport
dotnet add package SiameseCat.Protocols
dotnet add package SiameseCat.Drivers        # Siemens + Inovance
dotnet add package SiameseCat.Hosting        # optional: DI + Microsoft.Extensions.Logging
dotnet add package SiameseCat.Server         # optional: Modbus TCP/RTU slaves
```

## Using a driver

The registry is the recommended entry point: it hides which protocol client and address parser are used.

```csharp
using SiameseCat.Core;
using SiameseCat.Drivers;

var registry = DriverRegistryExtensions.CreateDefaultRegistry();   // modbus, siemens, inovance

await using var driver = registry.Create("modbus", new DriverConfiguration
{
    Transport = TransportKind.Tcp,
    Host = "192.168.0.10",
    Port = 502,
    Timeout = TimeSpan.FromSeconds(2),
    RetryCount = 2,
});

driver.StateChanged += (_, e) => Console.WriteLine($"{e.OldState} -> {e.NewState}");
await driver.ConnectAsync();

var readable = (IReadable)driver;
var writable = (IWritable)driver;
var batch = (IBatchReadable)driver;
var batchWrite = (IBatchWritable)driver;

// Protocol-native text is parsed into a protocol-independent address.
var speed = driver.AddressParser.Parse("40001", PlcDataType.Int16);
ushort raw = (await readable.ReadAsync(speed)).AsUInt16();

await writable.WriteAsync(
    driver.AddressParser.Parse("HR10", PlcDataType.Float),
    PlcValue.FromFloat(12.5f));

// Batch read: Modbus merges contiguous registers, S7 uses one multi-variable request.
var values = await batch.ReadBatchAsync(new[]
{
    driver.AddressParser.Parse("HR0"),
    driver.AddressParser.Parse("HR1"),
    driver.AddressParser.Parse("HR2"),
});

// Batch write: FC16 for contiguous registers, FC15 for coils, multi-variable write for S7.
await batchWrite.WriteBatchAsync(new[]
{
    new PlcWriteRequest(driver.AddressParser.Parse("HR0"), PlcValue.FromUInt16(10)),
    new PlcWriteRequest(driver.AddressParser.Parse("HR1"), PlcValue.FromUInt16(11)),
});

// Named tags with engineering scaling.
var tag = new PlcTag("temperature", "40001", PlcDataType.Int16, scale: 0.1, offset: -40, unit: "°C");
var engineering = tag.ToEngineering((await driver.ReadTagAsync(tag)).AsDouble());

// Polling subscription (jitter + failure back-off by default).
await using var subscription = await driver.SubscribeAsync(
    driver.AddressParser.Parse("40001"),
    TimeSpan.FromMilliseconds(500),
    value => { Console.WriteLine(value); return Task.CompletedTask; });

Console.WriteLine(driver.Metrics.Snapshot());
```

### Configuration

Transport-level settings live on `DriverConfiguration`; protocol-specific parameters go into
`DriverConfiguration.Options` (keys are case-insensitive). **Invalid values throw instead of being
silently truncated.**

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

| Option | Drivers | Values | Default | Meaning |
| --- | --- | --- | --- | --- |
| `unitId` | modbus, inovance | 0–255 | 1 | Station address; `0` is broadcast (writes are fire-and-forget, reads are rejected) |
| `encoding` | modbus, inovance | `ascii` / `utf8` / `latin1` | `ascii` | Encoding of `String` values |
| `encoding` | siemens | `ascii` / `utf8` / `latin1` | `latin1` | Encoding of S7 `STRING` |
| `swapBytes` | modbus, inovance | `true` / `false` | `false` | Swap bytes inside each register (strings/raw) |
| `interFrameDelayMs` | modbus (RTU) | 0–60000 | auto | Inter-frame silence; `0` derives it from the baud rate |
| `framing` | modbus (custom transport) | `tcp` / `rtu` | `tcp` | Modbus framing on an injected transport |
| `discardEcho` | modbus (RTU) | `true` / `false` | `false` | Drop RS-485 converter echoes |
| `rack` / `slot` | siemens | 0–7 / 0–31 | 0 / 1 | S7 rack and slot |
| `dBase` / `mBase` / `xBase` / `yBase` | inovance | 0–65535 | 0 / 0 / 0 / 8192 | Base offsets mapped onto Modbus areas |

Typed helpers: `GetOption(key, default, min, max)`, `GetByteOption`, `GetUInt16Option`,
`GetBooleanOption`, `HasOption`.

Reliability settings are properties, not option keys:

| Property | Default | Meaning |
| --- | --- | --- |
| `Timeout` / `ConnectTimeout` | 3 s / 5 s | Per-request timeout / connect timeout |
| `RetryCount` | 0 | Retries for transient failures (requests and connect) |
| `RetryDelay` / `MaxRetryDelay` | 200 ms / 30 s | Exponential back-off base and cap |
| `RetryJitter` | `true` | 50–100% random jitter so clients do not reconnect in lockstep |
| `RetryWrites` | `false` | Whether writes may be retried |
| `ProbeAddress` | empty | Health probe address used by `PingAsync` and keep-alive |
| `KeepAliveInterval` | `0` (off) | Background probe interval |
| `KeepAliveFailureThreshold` | 2 | Consecutive failures before an automatic reconnect |
| `TransportFactory` | empty | Supplies `ITransport`; the driver then never opens its own link |
| `RequestLock` | empty | Shared request lock for several stations on one physical link |
| `LogFrames` | `false` | Log every TX/RX frame as hex at `Trace` level |
| `SubscriptionJitter` | `true` | Randomise polling intervals by ±20% |
| `SubscriptionMaxBackoff` | 30 s | Exponential back-off cap after failed polls |
| `StampReadTimestamps` | `false` | Stamp every read with a UTC timestamp |

## Protocol clients (without the driver layer)

Use the clients directly when you do not need the driver lifecycle.

### Modbus TCP

```csharp
using SiameseCat.Protocols.Modbus;

var options = new ModbusTcpOptions
{
    UnitId = 1,
    Timeout = TimeSpan.FromSeconds(3),
    Tcp = new TcpTransportOptions { Host = "192.168.0.10", Port = 502 },
};

await using var client = new ModbusTcpClient(options);
await client.ConnectAsync();

ushort[] registers = await client.ReadHoldingRegistersAsync(0, 10);
await client.WriteSingleRegisterAsync(10, 1234);

// 32-bit float occupies two registers.
var buffer = new ushort[2];
ModbusRegisterConverter.WriteSingle(buffer, 0, 12.5f);
await client.WriteMultipleRegistersAsync(20, buffer);
float value = ModbusRegisterConverter.ReadSingle(await client.ReadHoldingRegistersAsync(20, 2), 0);
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

- Without `InterFrameDelay`, the 3.5-character silence is derived from the baud rate (minimum 2 ms).
- Some RS-485 converters echo the request; set `DiscardEcho = true`.
- Keep `SerialPortOptions.ReadTimeout` below `ModbusRtuOptions.Timeout`, otherwise timeouts are delayed
  by the serial read timeout.

### Siemens S7

```csharp
using SiameseCat.Protocols.S7;

var options = new S7Options
{
    Host = "192.168.0.10",
    Rack = 0,
    Slot = 1,                       // S7-1200/1500 usually 1, S7-300/400 usually 2
    Timeout = TimeSpan.FromSeconds(3),
};

await using var client = new S7Client(options);
await client.ConnectAsync();

float speed = await client.ReadSingleAsync(S7Address.Parse("DB1.DBD0"));
await client.WriteSingleAsync(S7Address.Parse("DB1.DBD0"), 12.5f);

bool motorOn = await client.ReadBoolAsync(S7Address.Parse("M0.0"));
await client.WriteBoolAsync(S7Address.Parse("M0.0"), true);
```

The PDU size is negotiated during `ConnectAsync` (Setup Communication requests 960 bytes and the PLC's
answer is capped at 960); multi-variable requests are split automatically for larger batches.

### Raw TCP / UDP / serial

```csharp
using SiameseCat.Transport;

await using var tcp = new TcpTransport(new TcpTransportOptions { Host = "127.0.0.1", Port = 9000 });
await tcp.OpenAsync();
await tcp.WriteAsync(new byte[] { 0x01, 0x02 });
var reply = await tcp.ReadExactlyAsync(2);

await using var udp = new UdpTransport(new UdpTransportOptions { Host = "127.0.0.1", Port = 9001 });
await using var serial = new SerialPortTransport(new SerialPortOptions { PortName = "COM3", BaudRate = 115200 });
```

## Address and value model

| Syntax | Example | Meaning |
| --- | --- | --- |
| Modbus reference numbers | `40001`, `10001`, `30001`, `00001` | Coils / discrete inputs / input registers / holding registers |
| Modbus named offsets | `HR10`, `IR0`, `C0`, `DI0` | Same areas with zero-based offsets |
| Siemens | `DB1.DBX0.0`, `DB1.DBW2`, `DB1.DBD4`, `MW2`, `IW4`, `Q0.0` | Data block, memory, input and output areas |
| Inovance | `D100`, `M10`, `X10`, `Y10` | Soft elements mapped onto Modbus areas (X/Y are octal) |

`PlcAddress` carries `Region`, `Offset`, `BitOffset`, `DataType`, `Length` and an optional `Symbol` for
symbol-addressed protocols. `PlcValue` carries the payload plus optional `Quality` and `Timestamp`;
neither participates in equality (so change detection is unchanged).

Numeric arrays use `Length` as the **number of elements**:

```csharp
var address = new PlcAddress("holding", 0, PlcDataType.UInt16, length: 10);   // 10 registers
var values = PlcValue.FromUInt16Array(new ushort[10]);
ushort[] readBack = values.AsUInt16Array();                                    // clone

var batchValue = await ((IReadable)driver).ReadAsync(
    driver.AddressParser.Parse("HR0", PlcDataType.UInt16).WithLength(10));
```

Supported array element types: `Int16`, `UInt16`, `Int32`, `UInt32`, `Int64`, `UInt64`, `Float`,
`Double`. `Bool` is always a single bit (use separate addresses for bit arrays); `String` and `Raw` keep
using `Length` as character/byte count.

## Modbus extras

```csharp
var client = (ModbusTcpClient)driverClient;   // or drive it through the driver

// FC23: write HR10..HR11 and read HR0..HR1 atomically.
var answer = await client.ReadWriteMultipleRegistersAsync(
    readStartAddress: 0, readQuantity: 2,
    writeStartAddress: 10, values: new ushort[] { 1, 2 });

// Broadcast (unit id 0): writes are sent without waiting, reads are rejected.
configuration.Options["unitId"] = "0";
```

## Subscriptions

```csharp
// Single address.
await using var one = await driver.SubscribeAsync(
    driver.AddressParser.Parse("40001"),
    TimeSpan.FromSeconds(1),
    value => Task.CompletedTask);

// One request per cycle for a group of addresses (Modbus groups by area and contiguity).
await using var many = await driver.SubscribeBatchAsync(
    new[]
    {
        driver.AddressParser.Parse("40001"),
        driver.AddressParser.Parse("40002"),
        driver.AddressParser.Parse("40003"),
    },
    TimeSpan.FromSeconds(1),
    values => { Console.WriteLine(string.Join(", ", values)); return Task.CompletedTask; });
```

- The callback receives the whole current set; it fires once for the initial read and afterwards only
  when at least one element changed (arrays compare element-wise).
- Jitter and back-off apply to both variants; metrics record batch polls as `BatchRead`.
- Batch subscriptions need `DriverCapabilities.SubscribeBatch` and `BatchRead`; a driver with a native
  batch subscription overrides `DoSubscribeBatchAsync` (returns `null` to fall back to polling).

## Shared transports (RS-485 multi-drop)

```csharp
using SiameseCat.Core;
using SiameseCat.Transport;

await using var link = new SharedTransportFactory(() => new SerialPortTransport(new SerialPortOptions
{
    PortName = "COM3",
    BaudRate = 19200,
}));

foreach (var station in new byte[] { 1, 2, 3 })
{
    var configuration = new DriverConfiguration
    {
        Transport = TransportKind.Custom,
        TransportFactory = link,
        RequestLock = link.RequestLock,     // must be shared, otherwise frames interleave
        Timeout = TimeSpan.FromSeconds(1),
    };
    configuration.Options["framing"] = "rtu";
    configuration.Options["unitId"] = station.ToString();

    await using var driver = registry.Create("modbus", configuration);
    // ConnectAsync / ReadAsync / WriteAsync behave like any other driver.
}
```

`LogFrames = true` writes each frame as hex through `IPlcLogger`, for example
`modbus/1 TX 01 03 00 00 00 02 C4 0B` / `RX 01 03 04 00 64 00 C8 ...`.

## Capabilities and optional hooks

Drivers declare what they support; `DriverBase` validates every call and throws `NotSupportedException`
when a capability is missing:

```csharp
new DriverDescriptor(
    "sensor", "Sensor", "Read-only sampler", TransportKind.Serial,
    capabilities: DriverCapabilities.Read | DriverCapabilities.Health);
```

`DoReadAsync` / `DoWriteAsync` / `DoSubscribeAsync` are virtual and throw by default, so read-only or
write-only drivers do not need empty overrides. Batch subscription follows the same pattern with
`DoSubscribeBatchAsync`. Per-call timeouts override `DriverConfiguration.Timeout` without touching the
global setting:

```csharp
var values = await batch.ReadBatchAsync(addresses, TimeSpan.FromSeconds(10));
var value = await readable.ReadAsync(driver.AddressParser.Parse("HR0"), TimeSpan.FromMilliseconds(200));
```

## Host integration (DI / logging)

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddSiameseCat();                       // DriverRegistry + IPlcLogger adapter

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

- Keyed drivers are transient and disposed when their scope ends.
- Without a registered logger the library falls back to `NullPlcLogger`.
- Programmatic integration: `new MicrosoftLoggerAdapter(loggerFactory.CreateLogger("SiameseCat"))`.

Slaves can be hosted as well (`IModbusServer` / `IS7Server` singletons, started and stopped with the host):

```csharp
services.AddModbusServer(options =>
{
    options.Port = 0;                          // TCP slave, 0 = let the OS choose
    options.ServerIdentifier = "SIM-1";        // FC17 Report Server ID
});

services.AddModbusRtuServer(options =>
{
    options.UnitId = 1;
    options.Serial.PortName = "COM3";
});

services.AddS7Server(options =>
{
    options.Port = 0;                          // ISO-on-TCP slave
    options.DefaultDataBlockSize = 1024;
});
```

## Modbus slaves (TCP and RTU)

`SiameseCat.Server` provides in-process slaves for integration tests, demos and offline development.
Both share one PDU processor and the same data store.

```csharp
using SiameseCat.Server.Modbus;

await using var server = new ModbusTcpServer(new ModbusServerOptions
{
    Host = "127.0.0.1",
    Port = 0,                 // 0 = OS-assigned port
    HoldingRegisters = 512,
    Coils = 512,
});

await server.StartAsync();
server.Data.TryWriteHoldingRegisters(0, new ushort[] { 100, 200 });
server.Data.TryWriteCoils(10, new[] { true, false, true });
Console.WriteLine($"listening on 127.0.0.1:{server.Port}");

await using var client = new ModbusTcpClient(new ModbusTcpOptions
{
    Tcp = { Host = "127.0.0.1", Port = server.Port },
});
await client.ConnectAsync();
var values = await client.ReadHoldingRegistersAsync(1, 0, 2);   // [100, 200]
```

- Function codes: `01/02/03/04/05/06/07/0B/0F/10/11/16/17/23`. Out-of-range addresses answer `02`,
  invalid quantities `03`, unsupported functions `01`.
- Diagnostics: `07` exception status (`Data.ExceptionStatus`), `0B` communication event counter
  (`Data.EventCount`, incremented on every successful write), `11` Report Server ID
  (`ServerIdentifier` + `RunIndicator`), `16` mask write register
  (`(current AND andMask) OR (orMask AND NOT andMask)`).
- The unit identifier is echoed back, so one slave can serve several stations; unit id `0` is broadcast:
  writes are applied and no response is sent.
- Concurrent clients are supported (one session task per connection); `StopAsync` / `DisposeAsync` close
  the listener and every session.
- Sessions reuse their request/response buffers and the PDU processor writes directly into them, so a
  served request allocates no frame memory on the slave side (see
  [performance notes](https://github.com/RabitLogic/SiameseCat/blob/main/docs/architecture.md#performance-notes)).

The clients are allocation-lean as well: Modbus TCP/RTU and S7 reuse frame buffers, inline their retry
loops and reuse the request-timeout source when no cancellation token is passed, so a driver read allocates
roughly a third less than before (see the performance notes for measured numbers).

### Statistics and logging

```csharp
using var server = new ModbusTcpServer(new ModbusServerOptions
{
    Port = 0,
    Logger = myPlcLogger,          // any IPlcLogger implementation
    ServerIdentifier = "SIM-1",
});

await server.StartAsync();

Console.WriteLine($"requests={server.Statistics.TotalRequests} "
    + $"exceptions={server.Statistics.TotalExceptions} "
    + $"frames={server.Statistics.FramesReceived} discarded={server.Statistics.FramesDiscarded} "
    + $"FC03={server.Statistics.GetRequests(0x03)}");
```

- Counters: total requests, per-function-code requests and exceptions, received frames, discarded frames
  (invalid MBAP length, RTU CRC errors, truncated frames).
- Log levels: connect/disconnect `Debug`, discarded frames and unsupported functions `Warning`, exception
  details `Debug`. No logger means no logging at all.
- `Statistics.Reset()` clears every counter.

### Data store

```csharp
// 1. React to writes coming from a master.
server.Data.HoldingRegistersWritten += (_, e) =>
    Console.WriteLine($"HR{e.Start}.. = [{string.Join(", ", e.Values)}] (was [{string.Join(", ", e.PreviousValues)}])");

server.Data.CoilsWritten += (_, e) =>
{
    if (e.Start == 10 && e.Values[0])
    {
        server.Data.TryWriteInputRegisters(0, new ushort[] { 1 });   // simulate a device reaction
    }
};

// 2. Preset and restore state.
var snapshot = server.Data.Snapshot();   // immutable copy of all four areas
server.Data.Reset();                     // zero everything, like a power cycle
server.Data.Load(snapshot);              // throws ArgumentException when dimensions differ
```

- Events (`HoldingRegistersWritten`, `InputRegistersWritten`, `CoilsWritten`, `DiscreteInputsWritten`)
  carry `Start`, `Values` and `PreviousValues`, and also fire for writes performed through Modbus.
- Events are raised outside the lock, so callbacks may read or write the store without deadlocking.
- Each area has its own lock; coils and discrete inputs are bit-packed (65536 points use 8 KB).
- Writes outside the buffer return `false` and raise no event.

### RTU slave

```csharp
await using var slave = new ModbusRtuServer(new ModbusRtuServerOptions
{
    UnitId = 1,
    Serial = { PortName = "COM3", BaudRate = 19200, ReadTimeout = TimeSpan.FromMilliseconds(100) },
    HoldingRegisters = 512,
});

await slave.StartAsync();
slave.Data.TryWriteHoldingRegisters(0, new ushort[] { 100, 200 });
```

- `Serial.ReadTimeout` doubles as the frame gap (default 100 ms; tune it to the baud rate).
- Only the configured `UnitId` is answered; station `0` writes are applied without a response; frames
  with a bad CRC are dropped and counted in `Statistics.FramesDiscarded`.
- Function code coverage matches the TCP slave. Request lengths are inferred per function code
  (mask write is a fixed 8-byte frame).
- `new ModbusRtuServer(ITransport, options)` injects a custom link (serial tunnel or test double).

### S7 slave (ISO-on-TCP)

```csharp
using SiameseCat.Server.S7;

await using var s7 = new S7Server(new S7ServerOptions
{
    Host = "127.0.0.1",
    Port = 0,                    // 0 = OS-assigned; 102 is the real PLC port
    MemorySize = 512,            // M area
    InputSize = 512,             // I area
    OutputSize = 512,            // Q area
    DefaultDataBlockSize = 1024, // size used when a DB is created on demand
});

await s7.StartAsync();
Console.WriteLine($"S7 slave listening on 127.0.0.1:{s7.Port}");

// The regular S7 client talks to it unchanged.
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

- Handles the COTP connection handshake, Setup Communication (PDU size configurable, default 960),
  read variable (`0x04`) and write variable (`0x05`) services, including multi-variable requests.
- Areas: M, I, Q and data blocks. Each area has a fixed size; data blocks are created on first use
  (`AutoCreateDataBlocks`, default `true`) unless you pre-create them with `TryEnsureDataBlock`.
- Return codes follow S7 conventions: `0x05` address out of range, `0x0A` object does not exist,
  `0x03` access denied (timer/counter areas), `0x06` data error. Item-level errors surface as
  `S7ItemException` on the client.
- Bits use bit addressing (`M0.3`, `DB1.DBX0.1`); byte-oriented items divide the bit address by eight.
- The data store mirrors the Modbus one: byte-level `ReadBytes`/`TryWriteBytes`,
  `TryReadBit`/`TryWriteBit`, a `Written` event with `Values`/`PreviousValues`, and `Reset()`.
- `Statistics` reports connections, active connections, setup/read/write request counts, items read and
  written, item errors and discarded frames; `S7ServerOptions.Logger` receives the diagnostics.

## Reliability

- **Lifecycle**: `Disconnected → Connecting → Connected → Faulted`, exposed through `State` and
  `StateChanged`; disposal is idempotent.
- **Timeouts and retries**: transient failures (timeout, closed connection, protocol error) are retried
  with exponential back-off plus jitter; writes are not retried unless `RetryWrites` is enabled.
- **Health**: `PingAsync` performs a probe read (`ProbeAddress`); keep-alive reconnects after
  `KeepAliveFailureThreshold` consecutive failures.
- **Metrics**: `driver.Metrics.Snapshot()` returns per-operation counters and latency percentiles
  (P50/P95/max); `PlcDiagnostics` publishes `plc.requests`, `plc.failures`, `plc.retries`, `plc.bytes.*`
  and `plc.latency` through `System.Diagnostics.Metrics`, ready for OpenTelemetry.
- **Exceptions**: `CommunicationException`, `CommunicationTimeoutException`,
  `ConnectionClosedException`, `ProtocolException` / `S7Exception`; messages include the unit id,
  function code and address whenever they are known.

## Native AOT

All libraries enable `IsAotCompatible`; there is no reflection, `dynamic`, runtime serialization or code
generation, and `S7Address` uses a source-generated regex. `tools/SiameseCat.AotProbe` is a
hardware-free probe covering 15 scenarios (CRC, register conversion, address parsing, TCP loopback,
Modbus round trips, numeric arrays, batch subscription, slave data store, DI hosting, S7 slave round
trip, transport injection, logging). It uses **PublishAotCross.Tools**, so every supported RID can be cross-compiled from
any host (win-x64/arm64, linux-x64/arm64, linux-musl-x64/arm64, osx-x64/arm64).

```powershell
./scripts/publish-aot.ps1 -Run                     # host RID
./scripts/publish-aot.ps1 -All                     # every RID
./scripts/publish-aot.ps1 -RuntimeIdentifier linux-x64,linux-musl-x64 -Docker
```

CI runs the probe natively on `win-x64`, `linux-x64` and `osx-arm64`, cross-compiles every RID on Linux
and executes the glibc/musl probes in Docker.

## Build and test

The solution uses the `.slnx` format and needs the .NET SDK 9.0.200 or later (CI uses 10.0.x to build the
`net8.0` targets).

```powershell
dotnet build SiameseCat.slnx
dotnet test SiameseCat.slnx

# Coverage (CI enforces a 75% line-coverage gate, currently ~77%)
dotnet test tests/SiameseCat.Tests --collect:"XPlat Code Coverage" --results-directory test-results

# Hardware-free self test using the built-in Modbus TCP and S7 slaves
dotnet run --project samples/SiameseCat.Demo -- selftest

# Driver example: registry + Modbus driver + batch read + PlcTag
dotnet run --project samples/SiameseCat.Demo -- driver

# Native AOT probe (the project enables PublishAot)
dotnet run --project tools/SiameseCat.AotProbe

# .NET Framework 4.8 compatibility runner (netstandard2.0 assets, Windows only)
dotnet run --project tests/SiameseCat.Compatibility -c Release

# Benchmarks (BenchmarkDotNet; results go to BenchmarkDotNet.Artifacts/)
dotnet run --project benchmarks/SiameseCat.Benchmarks -c Release -- --filter "*"
```

Packaging: every library is packed for `net8.0` and `netstandard2.0`, with
`EnablePackageValidation` ensuring both TFMs expose the same public API. CI verifies that each `.nupkg`
contains both `lib/net8.0` and `lib/netstandard2.0` and that the coverage gate passes. Dependencies are
updated weekly by Dependabot; see [SECURITY.md](https://github.com/RabitLogic/SiameseCat/blob/main/SECURITY.md)
for the vulnerability reporting process.

## Known limitations

- The Modbus slaves cover TCP and RTU; S7 only supports S7comm variable read/write (no upload/download or
  diagnostic buffer services).
- Three drivers ship in the box (generic Modbus, Siemens S7, Inovance). Other brands extend the same
  contracts.
- The Inovance driver maps soft elements onto Modbus; vendor-specific ASCII protocols are not included.
- Siemens S7 currently supports TCP only; Modbus supports TCP and RTU.
- Numeric arrays cover `Int16/UInt16/Int32/UInt32/Int64/UInt64/Float/Double`; `Bool` is a single bit.
- There is no cross-protocol semantic address mapping in v1; `PlcTag` covers configuration-driven tags.
- Serial I/O has no cancellable async API: cancelling a read/write closes the port and requires a new
  `ConnectAsync`.
- Keep `SerialPortOptions.ReadTimeout` below the protocol timeout, otherwise timeout detection is delayed.
- The `netstandard2.0` target is smoke-tested on .NET Framework 4.8 by `tests/SiameseCat.Compatibility`
  (address parsing, Modbus TCP round trip, driver contract, data store events, frame logging). AOT
  analysis and source-generated regexes are disabled there; regexes fall back to compiled caches and TCP
  keep-alive can only be enabled without timing parameters.

## License

MIT. See [LICENSE](https://github.com/RabitLogic/SiameseCat/blob/main/LICENSE).
