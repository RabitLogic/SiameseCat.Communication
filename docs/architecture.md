# Architecture

`SiameseCat.Communication` is a single package with one assembly. Inside it, code is grouped by
responsibility into separate namespaces, and dependencies only point inwards — contracts ← transport ←
protocol ← drivers — so a custom driver only needs the contracts and a custom protocol only needs the
transport abstractions.

## Namespaces

| Namespace | Folder | Contents |
| --- | --- | --- |
| `SiameseCat.Communication` | root, `Addressing/`, `Diagnostics/`, `Drivers/`, `Exceptions/` | driver contracts, address/value model, `DriverBase`, registry, options, metrics, exceptions |
| `SiameseCat.Communication.Transport` | `Transport/` | `TcpTransport`, `UdpTransport`, `SerialPortTransport`, `SharedTransportFactory` |
| `SiameseCat.Communication.Modbus` | `Modbus/` | Modbus frames/CRC/converters, `ModbusTcpClient` / `ModbusRtuClient`, `ModbusDriver`, `ModbusAddressParser` |
| `SiameseCat.Communication.S7` | `S7/` | `S7Client`, `S7Address`, `S7ReadItem` / `S7WriteItem`, `S7Options` |
| `SiameseCat.Communication.Siemens` | `Siemens/` | `SiemensDriver` + `SiemensAddressParser`; other brands plug in through `IPlcDriverFactory` |
| `SiameseCat.Communication.Drivers` | `DriverRegistryExtensions.cs` | `CreateDefaultRegistry()`, `AddSiameseCatDrivers()` |
| `SiameseCat.Communication.Server` | `Server/` | `IModbusServer`, `IS7Server`, shared buffer-size validation |
| `SiameseCat.Communication.Server.Modbus` | `Server/Modbus/` | `ModbusTcpServer`, `ModbusRtuServer`, `ModbusPduProcessor`, `ModbusDataStore`, `ModbusServerStatistics` |
| `SiameseCat.Communication.Server.S7` | `Server/S7/` | `S7Server`, `S7PduProcessor`, `S7DataStore`, `S7ServerStatistics` |
| `SiameseCat.Communication.Hosting` | `Hosting/` | `AddSiameseCat()` / `AddSiameseCatDriver()`, slave hosting, health checks, `Microsoft.Extensions.Logging` adapter |

## Source layout

```text
src/SiameseCat.Communication/
├── CommunicationOptions.cs / RetryPolicy.cs
├── Addressing/          PlcAddress, PlcValue, PlcValueCodec, PlcTag, IPlcAddressParser   (SiameseCat.Communication)
├── Diagnostics/         IPlcLogger, NullPlcLogger, PlcMetrics, PlcDiagnostics            (SiameseCat.Communication)
├── Drivers/
│   ├── Abstractions/    IPlcDriver + capability interfaces + descriptor + state           (SiameseCat.Communication)
│   ├── Runtime/         DriverBase (partials), DriverRegistry, DriverConfiguration        (SiameseCat.Communication)
│   └── DriverRegistryExtensions.cs                                                        (…Communication.Drivers)
├── Exceptions/          one file per exception type                                       (SiameseCat.Communication)
├── Transport/           ITransport + TCP/UDP/serial implementations                       (…Communication.Transport)
├── Modbus/              frames, clients, driver                                           (…Communication.Modbus)
├── S7/                  S7 client, addresses, options                                     (…Communication.S7)
├── Siemens/             SiemensDriver, SiemensAddressParser, factory                      (…Communication.Siemens)
├── Server/              Modbus and S7 slaves                                              (…Communication.Server.*)
└── Hosting/             DI registration, health checks, logger adapter                    (…Communication.Hosting)
```

Namespaces track the folders, so the protocol or layer is obvious from the `using` directive. The slave
layout follows the same protocol-first structure:

```text
src/SiameseCat.Communication/Server/
├── Modbus/              namespace SiameseCat.Communication.Server.Modbus
│   ├── ModbusTcpServer / ModbusRtuServer / IModbusServer
│   ├── ModbusServerOptions / ModbusRtuServerOptions / ModbusServerIdentifier
│   ├── ModbusPduProcessor / ModbusServerStatistics
│   └── ModbusDataStore / ModbusDataStoreSnapshot / *WriteEventArgs
├── S7/                  namespace SiameseCat.Communication.Server.S7
│   ├── S7Server / IS7Server / S7ServerOptions
│   ├── S7PduProcessor / S7ServerStatistics
│   └── S7DataStore / S7WriteEventArgs
└── ServerBufferSizes.cs internal buffer-size validation shared by both protocols
```

`DriverBase` is split by responsibility:

| File | Responsibility |
| --- | --- |
| `DriverBase.cs` | Read/write API, capability checks, descriptor/state surface |
| `DriverBase.Lifecycle.cs` | Connect/disconnect/dispose state machine, `StateChanged` |
| `DriverBase.Execution.cs` | Serialised execution pipeline: timeout, retry, reconnect, metrics |
| `DriverBase.Health.cs` | `PingAsync` and background keep-alive |
| `DriverBase.Subscription.cs` | Single and batch subscriptions, jitter and back-off |

## Request pipeline

Every read/write/batch request goes through `ExecuteAsync`:

1. **Serialise** — a `RequestLock` (per driver by default, shareable across drivers) guarantees one
   request at a time on a link.
2. **Connect if needed** — `EnsureConnected` reconnects when the driver is `Faulted`.
3. **Attempt loop** — attempts = `RetryCount + 1` (writes only when `RetryWrites` is enabled).
   Each attempt runs under a linked `CancellationTokenSource` that enforces `Timeout`.
4. **Classify failures** — a cancellation that came from the timeout becomes
   `CommunicationTimeoutException`; other exceptions are classified as connection, protocol or unknown.
5. **Recover** — retryable failures wait `RetryDelay × 2^(n-1)` (capped by `MaxRetryDelay`, jittered by
   `RetryJitter`); timeouts additionally trigger a reconnect so late responses cannot pollute the next
   request.
6. **Observe** — every attempt records metrics (`PlcMetrics`) and diagnostics (`PlcDiagnostics`), and the
   connector logs warnings with context (unit id, address).

## Capability model

`DriverDescriptor.Capabilities` advertises `Read`, `Write`, `BatchRead`, `BatchWrite`, `Subscribe`,
`SubscribeBatch` and `Health`. `DriverBase` validates capabilities at the entry point of each operation,
so a read-only driver fails fast with `NotSupportedException` instead of sending a malformed request.

Protocol hooks are virtual and throw by default:

```csharp
protected virtual Task<PlcValue> DoReadAsync(PlcAddress address, CancellationToken token);
protected virtual Task DoWriteAsync(PlcAddress address, PlcValue value, CancellationToken token);
protected virtual Task<IAsyncDisposable?> DoSubscribeAsync(PlcAddress address, TimeSpan interval,
    Func<PlcValue, Task> onChanged, CancellationToken token);          // null → poll
protected virtual Task<IAsyncDisposable?> DoSubscribeBatchAsync(IReadOnlyList<PlcAddress> addresses,
    TimeSpan interval, Func<IReadOnlyList<PlcValue>, Task> onChanged, CancellationToken token);
```

Declaring `KeepAliveInterval` without the `Health` capability is rejected in the constructor.

## Value and address model

- `PlcAddress` — `Region`, `Offset`, `BitOffset?`, `DataType`, `Length` (elements for numeric arrays,
  characters/bytes for `String`/`Raw`), optional `Symbol`.
- `PlcValue` — immutable payload with factories/accessors per data type, array factories
  (`FromUInt16Array`, `FromFloatArray`, …), `IsArray`/`Length`, optional `Quality` and `Timestamp`.
  Equality compares payload only, so subscription change detection ignores metadata.
- `PlcValueCodec` — protocol-independent byte conversions (`ToBytes`, `FromBytes` with element count),
  used by the S7 driver and available to custom drivers.
- `PlcTag` — named tag with scale/offset/unit and `ToEngineering`/`FromEngineering` helpers.

## Multi-targeting and AOT

The package targets `net8.0` and `netstandard2.0`:

- `netstandard2.0` covers .NET Framework 4.6.1+, .NET Core 2.x+ and .NET 5–7 hosts.
- `net8.0` covers .NET 8 and later; newer hosts consume this asset (verified for .NET 10 and .NET 11).
  No separate assets are shipped for net9/net10/net11 because the TFM contract is forward compatible.
- Shared sources (`src/Shared/Guard.cs`) replace `ThrowIf*` helpers that do not exist on
  `netstandard2.0`; `IntervalTimer` replaces `PeriodicTimer`; `[GeneratedRegex]` is used on `net8.0`
  and falls back to `RegexOptions.Compiled`; UDP uses `ArrayPool` + `ArraySegment` to bridge Span APIs.
- `IsAotCompatible` and the trimming/AOT analyzers run for `net8.0` only. There is no reflection,
  `dynamic`, runtime serialization or code generation anywhere in the libraries.
- `EnablePackageValidation` compares the public API of both TFMs at pack time, so an API cannot be
  accidentally hidden behind `#if NET8_0_OR_GREATER`.

## Performance notes

Both sides of the stack are allocation-lean by design.

**Slave side**

- Each connection owns its request/response buffers; the PDU processors (Modbus and S7) write the response
  into a caller-provided `Span<byte>` instead of allocating a new array per request.
- Data stores copy registers/bits directly into the response buffer (`TryCopyHoldingRegisters`,
  `TryCopyBits`, `TryCopyBytes`) and parse incoming payloads from spans, so no intermediate `ushort[]` or
  `bool[]` is allocated.
- Write events allocate their `Values`/`PreviousValues` arrays only when a subscriber is attached.
- `stackalloc` is used for register payloads (≤123 registers) and no `MemoryStream` remains in the S7 path.

**Client side**

- The Modbus TCP/RTU and S7 clients rent or own their frame buffers (`ArrayPool<byte>` or one buffer per
  client instance) instead of allocating a frame, a header array and a body array per request.
- S7 builds the job frame directly into its send buffer and reads each TPKT response into a single receive
  buffer; only the parameter and data blocks that callers need are copied out.
- Requests are serialised by an internal gate, so each client (and `DriverBase`) reuses one
  `CancellationTokenSource` for the request timeout when the caller passes no token
  (`CancellationTokenSource.TryReset`, .NET 6+). Caller tokens still use a linked source.
- Retry loops are inlined, so no closures or extra state machines are allocated per attempt.
- `DriverBase` measures latency with `Stopwatch.GetTimestamp()` instead of allocating a `Stopwatch`, builds
  failure messages lazily, and `PlcDiagnostics` caches operation-name tag values instead of formatting the
  enum on every request.

Measured on a 14-core Windows machine, loopback TCP, 20k requests per scenario, median of 3 runs
(baseline = before this optimization pass):

| Scenario | Allocations before | Allocations after | Change |
| --- | --- | --- | --- |
| Modbus TCP read 100 registers | 2652 B/op | 1797 B/op | −32.2% |
| Modbus TCP write 10 registers | 2404 B/op | 1525 B/op | −36.6% |
| S7 read REAL | 3165 B/op | 2158 B/op | −31.8% |
| S7 write WORD | 2828 B/op | 1828 B/op | −35.4% |
| Driver read (Modbus via `DriverBase`) | 3652 B/op | 2564 B/op | −29.8% |
| Driver write (Modbus via `DriverBase`) | 3901 B/op | 2818 B/op | −27.8% |

The remaining allocations are the response payload itself plus async/socket machinery, so the savings show
up as lower GC pressure in long-running gateways. Run `benchmarks/SiameseCat.Benchmarks` for reproducible
numbers on your hardware.

## Naming conventions

| Rule | Example |
| --- | --- |
| Protocol types keep the protocol name | `ModbusTcpClient`, `S7Client`, `S7Address` |
| Drivers use the brand name | `SiemensDriver`, `SiemensAddressParser` |
| Contract types use `Plc` prefixes or `I…` | `PlcAddress`, `PlcValue`, `IPlcDriver`, `IPlcAddressParser` |
| One public type per file | `Exceptions/CommunicationTimeoutException.cs` |
| Optional features are separate namespaces | `SiameseCat.Communication.Hosting`, `SiameseCat.Communication.Server` |

See [protocol-matrix.md](protocol-matrix.md) for the wire-level details and
[migration.md](migration.md) before upgrading an existing integration.
