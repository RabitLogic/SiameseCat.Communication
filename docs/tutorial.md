# Tutorial

This tutorial walks through SiameseCat the same way `samples/SiameseCat.Samples` does: one topic per
folder, one runnable program per step. Every step that needs a device also ships an in-process simulator,
so nothing here requires hardware.

## Before you start

```powershell
dotnet build SiameseCat.slnx
dotnet run --project samples/SiameseCat.Samples -- list          # the catalog
dotnet run --project samples/SiameseCat.Samples -- run modbus/tcp
dotnet run --project samples/SiameseCat.Samples -- selftest      # every offline sample
```

| Track | Folder | What you learn |
| --- | --- | --- |
| Modbus | `samples/SiameseCat.Samples/Modbus/` | TCP and RTU framing, every function code, error handling |
| S7 | `samples/SiameseCat.Samples/S7/` | `S7Client` and the `siemens` driver |
| Drivers | `samples/SiameseCat.Samples/Drivers/` | registry, tags, batching, subscriptions, resilience, DI |
| Slaves | `samples/SiameseCat.Samples/Server/` | Modbus TCP/RTU and S7 simulators |
| Transports | `samples/SiameseCat.Samples/Transports/` | raw frames and custom `ITransport` injection |

Install one package:

```bash
dotnet add package SiameseCat.Communication
```

It contains every layer; pick the namespace you need:

| Namespace | Use it for |
| --- | --- |
| `SiameseCat.Communication` | `PlcAddress`, `PlcValue`, `IPlcDriver`, `DriverBase`, metrics, transport contracts |
| `SiameseCat.Communication.Transport` | `TcpTransport`, `UdpTransport`, `SerialPortTransport` |
| `SiameseCat.Communication.Modbus` | `ModbusTcpClient`, `ModbusRtuClient`, the `modbus` driver |
| `SiameseCat.Communication.S7` | `S7Client`, `S7Address` |
| `SiameseCat.Communication.Siemens` | the `siemens` brand driver |
| `SiameseCat.Communication.Drivers` | `CreateDefaultRegistry()`, `AddSiameseCatDrivers()` |
| `SiameseCat.Communication.Server.*` | Modbus TCP/RTU and S7 slaves |
| `SiameseCat.Communication.Hosting` | dependency injection, slave hosting, health checks, logging bridge |

## 1. Modbus: first read and write (`modbus/tcp`)

```csharp
var options = new ModbusTcpOptions
{
    UnitId = 1,
    Timeout = TimeSpan.FromSeconds(2),
    Tcp = new TcpTransportOptions { Host = "192.168.0.10", Port = 502 },
};

await using var client = new ModbusTcpClient(options);
await client.ConnectAsync();

var registers = await client.ReadHoldingRegistersAsync(0, 4);   // FC03, zero-based address
await client.WriteSingleRegisterAsync(2, 4242);                 // FC06
await client.WriteMultipleRegistersAsync(10, new ushort[] { 1, 2, 3 }); // FC16
```

Rules of thumb:

* Addresses are **zero-based**. A device manual that says "40011" means `start = 10`.
* `UnitId` on the options is the default for every call; each method also has an overload that takes a
  `unitId` when you talk to several slaves through one gateway.
* `await using` disposes the socket; `DisconnectAsync()` keeps the instance reusable.
* The client is safe for concurrent callers: requests are serialised internally.

Run it against the simulator:

```powershell
dotnet run --project samples/SiameseCat.Samples -- run modbus/tcp
dotnet run --project samples/SiameseCat.Samples -- run modbus/tcp --host 192.168.0.10 --port 502 --unit 1
```

## 2. Modbus RTU (`modbus/rtu`)

Modbus RTU is the same PDU with a unit id and a CRC16 around it. The only difference in code is the client
type and the serial settings:

```csharp
var options = new ModbusRtuOptions
{
    UnitId = 1,
    Timeout = TimeSpan.FromSeconds(2),
    SerialPort = new SerialPortOptions { PortName = "COM3", BaudRate = 9600 },
};
await using var client = new ModbusRtuClient(options);
```

Broadcast is a normal write with `unitId = 0`; RTU never answers it, so the client returns without
waiting:

```csharp
await client.WriteSingleRegisterAsync(0, address: 3, value: 777);
```

Practical notes:

* `InterFrameDelay` defaults to the 3.5-character silence derived from the baud rate; raise it when a
  converter adds latency.
* `SerialPortOptions.ReadTimeout` must stay **below** `Timeout`, otherwise a silent device is detected
  late.
* Serial I/O cannot be cancelled; cancelling a read closes the port, so call `ConnectAsync()` again.
* `DiscardEcho` supports half-duplex converters that echo the request back.

Without a COM port the sample wires the client to a `ModbusRtuServer` through an in-memory
`LoopbackTransport`, which is exactly how the library's own tests run.

## 3. All function codes (`modbus/function-codes`)

| Method | FC | Direction |
| --- | --- | --- |
| `ReadCoilsAsync` | `0x01` | read bits |
| `ReadDiscreteInputsAsync` | `0x02` | read bits |
| `ReadHoldingRegistersAsync` | `0x03` | read registers |
| `ReadInputRegistersAsync` | `0x04` | read registers |
| `WriteSingleCoilAsync` | `0x05` | write bit |
| `WriteSingleRegisterAsync` | `0x06` | write register |
| `WriteMultipleCoilsAsync` | `0x0F` | write bits |
| `WriteMultipleRegistersAsync` | `0x10` | write registers |
| `ReadFileRecordsAsync` / `WriteFileRecordsAsync` | `0x14` / `0x15` | file records |
| `ReadWriteMultipleRegistersAsync` | `0x17` | write + read in one frame |
| `ReadDeviceIdentificationAsync` | `0x2B` (MEI `0x0E`) | vendor, product, revision, model |

Use `0x17` when a loop reads a setpoint and writes a measurement in the same cycle: one round trip instead
of two.

Two operations are not plain register reads, so they return their own types:

```csharp
// Device identification: vendor name, product code, revision, URL, product/model/application name.
var identity = await client.ReadDeviceIdentificationAsync(1, ModbusDeviceIdCode.Regular);
Console.WriteLine($"{identity.VendorName} {identity.ProductCode} rev {identity.MajorMinorRevision}");

// File records: blocks addressed by file number and record number, several per request.
var records = await client.ReadFileRecordsAsync(new[]
{
    new ModbusFileRecordRequest(4, 0, 2),
    new ModbusFileRecordRequest(4, 1, 2),
});
```

The slave serves both: device identification objects come from `ModbusServerOptions.DeviceIdentification`
and files are created with `server.Data.DefineFile(4, recordLength: 2, recordCount: 16)`. The
`modbus/function-codes` sample exercises the whole set against the built-in slave.

## 4. Failures (`modbus/errors`)

| What happened | What you catch |
| --- | --- |
| Device answered with an exception code | `ModbusException` (`ExceptionCode`, `GetExceptionName`) |
| Device did not answer in time | `CommunicationTimeoutException` |
| Socket closed, refused or reset | `CommunicationException` (incl. `ConnectionClosedException`) |
| Bad arguments (range, unit id 0 on a read) | `ArgumentOutOfRangeException` / `ArgumentException` |

```csharp
try
{
    await client.ReadHoldingRegistersAsync(5000, 4);
}
catch (ModbusException ex) when (ex.ExceptionCode == 0x02)
{
    // Illegal data address: wrong project, not a broken cable.
}
```

Retries (`RetryCount`, `RetryDelay`, `RetryWrites`) only repeat *transient* failures; a protocol exception
is returned immediately because repeating it cannot help. Retries are safe for reads and for register
writes; enable `RetryWrites` deliberately for coils, where a repeat is not idempotent for a running lever.

## 5. S7 (`s7/client`, `s7/driver`)

`S7Client` is the typed, S7-only API:

```csharp
await using var client = new S7Client(new S7Options
{
    Host = "192.168.0.10", Port = 102, Rack = 0, Slot = 1,
});

await client.ConnectAsync();
await client.WriteBoolAsync(S7Address.Parse("DB1.DBX0.0"), true);
await client.WriteUInt16Async(S7Address.Parse("DB1.DBW2"), 1234);
await client.WriteSingleAsync(S7Address.Parse("DB1.DBD8"), 12.5f);
await client.WriteStringAsync(S7Address.Parse("DB1.DBB12"), "SiameseCat", maxLength: 20);

var values = await client.ReadMultipleAsync(new[]
{
    new S7ReadItem(S7Address.Parse("DB1.DBW2"), 2),
    new S7ReadItem(S7Address.Parse("DB1.DBD4"), 4),
});
```

* `PduSize` reports the value negotiated by the Setup Communication exchange; batch helpers chunk to it.
* The driver layers addresses on top: `DB1.DBW2`, `M0.0`, `IW4`, `QW0`.
* `SiemensAddressParser.ToS7Address` maps a `PlcAddress` back to the S7 wire form.

The `siemens` driver adds everything protocol-neutral — capability flags, batch requests, subscriptions,
metrics, retries, keep-alive:

```csharp
var registry = DriverRegistryExtensions.CreateDefaultRegistry();
var configuration = new DriverConfiguration
{
    Transport = TransportKind.Tcp,
    Host = "192.168.0.10",
    Port = 102,
    ProbeAddress = "DB1.DBW0",
    RetryCount = 2,
};
configuration.Options["rack"] = "0";
configuration.Options["slot"] = "1";

await using var driver = registry.Create("siemens", configuration);
await driver.ConnectAsync();

var reader = (IReadable)driver;
var writer = (IWritable)driver;
await writer.WriteAsync(driver.AddressParser.Parse("DB1.DBW2"), PlcValue.FromUInt16(1234));
```

## 6. The driver contract (`drivers/*`)

`IPlcDriver` is small on purpose (connect, state, metrics, `AddressParser`); the operations live in
capability interfaces, and `Descriptor.Capabilities` says which ones a driver implements:

| Interface | Capability flag |
| --- | --- |
| `IReadable` / `IWritable` | `Read` / `Write` |
| `IBatchReadable` / `IBatchWritable` | `BatchRead` / `BatchWrite` |
| `ISubscribable` / `IBatchSubscribable` | `Subscribe` / `SubscribeBatch` |
| `IHealthCheckable` | `Health` |

```csharp
if (driver is IBatchReadable batch)
{
    var values = await batch.ReadBatchAsync(addresses);
}
```

### Registry (`drivers/registry`)

```csharp
var registry = DriverRegistryExtensions.CreateDefaultRegistry();   // modbus, siemens
registry.Descriptors;                                              // id, name, transports, port, examples
var driver = registry.Create("modbus", configuration, logger, metrics);
```

`RegisterFromAssembly()` scans assemblies with reflection and is therefore not usable under Native AOT;
register factories explicitly there.

### Tags (`drivers/tags`)

```csharp
var tags = new[]
{
    new PlcTag("Temperature", "HR0", PlcDataType.UInt16, scale: 0.1, offset: -40, unit: "degC"),
    new PlcTag("Running", "C0", PlcDataType.Bool),
};

var values = await driver.ReadTagsAsync(tags);
var temperature = tags[0].ToEngineering(values[0].AsDouble());
```

`PlcValue` is a typed union: `AsUInt16()`, `AsDouble()`, `AsString()`, `AsBytes()`, `AsUInt16Array()`,
plus `IsArray`, `Length`, `Quality` and `Timestamp`. Numeric arrays travel as one request:
`address.WithLength(4)` + `PlcValue.FromUInt16Array(...)`.

### Batching (`drivers/batch`)

The sample counts requests on the slave, which is the only honest measure:

| Scenario | Requests |
| --- | --- |
| 8 contiguous registers, read one by one | 8 |
| the same 8 registers, `ReadBatchAsync` | 1 |
| 4 contiguous registers, written one by one | 4 |
| the same 4 registers, `WriteBatchAsync` | 1 |

Limits the driver respects: 2000 coils / 125 registers per read, 1968 coils / 123 registers per write
(Modbus), and the negotiated PDU size (S7).

### Subscriptions (`drivers/subscription`)

```csharp
await using var handle = await driver.SubscribeAsync(
    driver.AddressParser.Parse("HR0"),
    TimeSpan.FromMilliseconds(100),
    value => { /* ... */ });

await using var group = await driver.SubscribeBatchAsync(addresses, interval, values => { /* ... */ });
```

Group subscriptions read the whole set with one request per cycle. Two options keep several drivers from
hammering one PLC:

* `SubscriptionJitter = true` spreads poll phases.
* `SubscriptionMaxBackoff` grows the interval after failures (up to the value) and resets on success.

Dispose the handle (or cancel the token) to stop polling. Note that polling never overlaps a request:
it uses the same lock as your own calls, so a slow cycle simply slips.

### Resilience (`drivers/resilience`)

| Option | Effect |
| --- | --- |
| `RetryCount` / `RetryDelay` / `MaxRetryDelay` / `RetryJitter` | exponential back-off with jitter for transient errors |
| `RetryWrites` | also retry writes (off by default) |
| `Timeout` / `ConnectTimeout` | per call / per connect |
| `ProbeAddress` + `PingAsync()` | side-effect-free health probe, reports `false` instead of throwing |
| `KeepAliveInterval` / `KeepAliveFailureThreshold` | background probe and automatic reconnect |
| `RequestLock` | share one physical port between several drivers |

Failures are counted by kind in `PlcMetrics` (`Timeouts`, `ConnectionClosed`, `Protocol`, `Other`) together
with latency percentiles, so alerts do not need log scraping.

### Dependency injection (`drivers/hosting`)

```csharp
services.AddLogging();                                    // optional: IPlcLogger bridges to ILogger
services.AddSiameseCat();                                 // registry + IPlcLogger
services.AddSiameseCatDriver("modbus", configuration =>   // keyed transient driver
{
    configuration.Transport = TransportKind.Tcp;
    configuration.Host = "192.168.0.10";
});
services.AddModbusServer(new ModbusServerOptions { Port = 502 });
services.AddS7Server(new S7ServerOptions { Port = 102 });

await using var provider = services.BuildServiceProvider();
await using var driver = provider.GetRequiredKeyedService<IPlcDriver>("modbus");
```

The slaves are registered as singletons plus an `IHostedService`, so a generic host starts and stops them.

### Health checks (`drivers/hosting` and ASP.NET Core)

```csharp
services.AddHealthChecks()
    .AddSiameseCatDriverCheck("modbus", configuration =>
    {
        configuration.Transport = TransportKind.Tcp;
        configuration.Host = "192.168.0.10";
        configuration.Timeout = TimeSpan.FromSeconds(2);
        configuration.ProbeAddress = "40001";
    }, tags: new[] { "ready" });
```

| Result | Meaning |
| --- | --- |
| `Healthy` | the driver is connected and the probe address answered |
| `Unhealthy` | the driver is faulted or the device did not answer |
| `Degraded` | the probe cannot run — `ProbeAddress` is missing, or the check was cancelled |

The report carries `driver`, `state`, `requests`, `failures`, `retries`, failure kinds and latency
percentiles. To watch a driver the application already owns, pass an accessor instead:
`.AddSiameseCatDriverCheck("plc-main", sp => sp.GetRequiredKeyedService<IPlcDriver>("modbus"))`.

## 7. Slaves (`server/*`)

### Modbus TCP (`server/modbus-tcp`)

```csharp
await using var slave = new ModbusTcpServer(new ModbusServerOptions
{
    Host = "0.0.0.0",
    Port = 502,
    HoldingRegisters = 64,
    Coils = 64,
    ServerIdentifier = "MyLine",
});

slave.Data.HoldingRegistersWritten += (_, e) => Console.WriteLine($"HR{e.Start}..+{e.Count}");
await slave.StartAsync();

slave.Data.TryWriteHoldingRegisters(0, new ushort[] { 42, 43 });
slave.Data[20] = 1234;                      // application side update
var snapshot = slave.Data.Snapshot();       // copy the whole process image
slave.Data.Load(snapshot);
slave.Data.Reset();

slave.Statistics.GetRequests((byte)ModbusFunctionCode.ReadHoldingRegisters);
```

The slave implements `0x01`, `0x02`, `0x03`, `0x04`, `0x05`, `0x06`, `0x07`, `0x0B`, `0x0F`, `0x10`,
`0x11`, `0x14`, `0x15`, `0x16` (mask write), `0x17` (read/write multiple) and `0x2B` (device
identification), answers broadcast writes without a reply, validates lengths and returns the correct
exception codes.

### Modbus RTU (`server/modbus-rtu`)

```csharp
await using var slave = new ModbusRtuServer(new ModbusRtuServerOptions
{
    UnitId = 1,
    Serial = new SerialSettings { PortName = "COM4", BaudRate = 19200, ReadTimeout = TimeSpan.FromMilliseconds(20) },
});
```

`Serial.ReadTimeout` is the frame gap: a pause ends the current frame. Frames for other stations and
frames with a bad CRC are dropped and counted in `Statistics.FramesDiscarded`.

### S7 (`server/s7`)

```csharp
await using var slave = new S7Server(new S7ServerOptions
{
    Port = 102,
    DefaultDataBlockSize = 512,
    PduSize = 480,
    AutoCreateDataBlocks = true,
});

slave.Data.TryEnsureDataBlock(1, 128);
slave.Data.Written += (_, e) => Console.WriteLine($"{e.Area} DB{e.DbNumber} @{e.Start}");
await slave.StartAsync();
```

The slave speaks COTP + S7comm (Setup Communication, read/write variable), so Snap7 or a real
engineering tool can use it.

## 8. Transports (`transports/*`)

| Situation | Use |
| --- | --- |
| Plain socket | `new TcpTransport(new TcpTransportOptions { Host = ..., Port = ... })` |
| Serial port | `new SerialPortTransport(new SerialPortOptions { PortName = "COM3" })` |
| Datagrams | `new UdpTransport(...)` |
| Shared physical port | `SharedTransportFactory` + `DriverConfiguration.RequestLock` |
| Custom channel (bridge, modem, TLS, tests) | implement `ITransport` and pass it to a client or a `TransportFactory` |
| Diagnostics | `LoggingTransport` decorator, or `DriverConfiguration.LogFrames = true` |

```csharp
public sealed class MyTransport : ITransport
{
    public bool IsOpen => ...;
    public Task OpenAsync(CancellationToken cancellationToken = default) => ...;
    public Task CloseAsync() => ...;
    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => ...;
    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ...;
    public void DiscardInput() => ...;
}
```

`transport/raw-tcp` also shows how to send hand-built MBAP frames (FC07 Read Exception Status, FC0B Comm
Event Counter, FC17 Report Server ID) — the same pattern works for vendor-specific frames.

## 9. Production checklist

1. **One driver per device, one `PlcMetrics`**, exposed through OpenTelemetry or your monitoring.
2. **Set `ProbeAddress`** so `PingAsync`, keep-alive and the health check have something harmless to read.
3. **Configure retries** (`RetryCount`, `RetryDelay`, `MaxRetryDelay`) and leave `RetryWrites` off unless
   the device tolerates repeats.
4. **Never call `ConnectAsync` per request.** Connect once and let the driver reconnect; when a cycle fails
   and the state is `Faulted`, call `ConnectAsync` again instead of retrying requests.
5. **Batch adjacent tags** instead of looping single reads; publish the batch interval that the PLC tolerates.
6. **Size the slave buffers** you simulate to the real device (a 256-register slave hides address bugs that
   a real 32-register device would expose).
7. **Log frames only while diagnosing** (`LogFrames = true`), then turn it off.
8. **Stay AOT-friendly**: no reflection over drivers; register factories explicitly.
9. **Wire a health check** (`AddSiameseCatDriverCheck`) into `/health/ready` and the orchestrator probe.
10. **Run the soak test** against the target topology before every release:

    ```powershell
    dotnet run --project tools/SiameseCat.Soak -c Release -- --duration 00:05:00 --workers 8
    ```

    It drives several drivers through a proxy that cuts the link on a schedule and then verifies recovery,
    success rate, managed-heap growth, handle growth and thread growth. Exit code 1 means an invariant
    broke; the per-second table shows where. Use `--fault-period 00:00:00` to measure a clean link.

## Where to go next

* [architecture.md](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/architecture.md) — layering, request pipeline, capability model.
* [protocol-matrix.md](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/protocol-matrix.md) — function codes, address syntax, slave services.
* [migration.md](https://github.com/RabitLogic/SiameseCat.Communication/blob/main/docs/migration.md) — moving from NModbus or Sharp7, plus the integration checklist.
