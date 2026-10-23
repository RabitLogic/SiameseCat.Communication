# Architecture

The library ships as multiple NuGet packages, one assembly each. Inside every package, code is grouped by
responsibility into separate namespaces, and dependencies only point inwards — contracts ← transport ←
TCP framing ← protocol ← drivers — so a custom driver only needs the contracts, a custom protocol only
needs the transport and frame abstractions, and the TCP framing layer itself has no device-model dependency.

## Namespaces

| Namespace | Package / project | Contents |
| --- | --- | --- |
| `SiameseCat.Communication` | `SiameseCat.Communication.Core` | driver contracts, address/value model, `DriverBase`, registry, options, metrics, exceptions |
| `SiameseCat.Communication.Transport` | `SiameseCat.Communication.Core` | `TcpTransport`, `UdpTransport`, `SerialPortTransport`, `SharedTransportFactory` |
| `SiameseCat.Communication.Tcp` | `SiameseCat.Communication.Tcp` | `TcpFrameCodec` presets, `ITcpFrameCodec`, `TcpFrameConnection`, `CustomTcpClient` |
| `SiameseCat.Communication.Modbus` | `SiameseCat.Communication.Modbus` | Modbus frames/CRC/converters, `ModbusTcpFrameCodec`, `ModbusTcpClient` / `ModbusRtuClient`, `ModbusDriver`, `ModbusAddressParser` |
| `SiameseCat.Communication.S7` | `SiameseCat.Communication.S7` | `S7Client`, `S7Address`, `S7ReadItem` / `S7WriteItem`, `S7Options`, `S7Driver` |
| `SiameseCat.Communication.Drivers` | `SiameseCat.Communication.All` | `CreateDefaultRegistry()`, `AddSiameseCatDrivers()` |
| `SiameseCat.Communication.Simulation` | `src/SimulatorShared/` | `SimulatorBufferSizes` validation shared by the simulators |
| `SiameseCat.Communication.Simulation.Modbus` | `SiameseCat.Communication.Simulation.Modbus` | `ModbusTcpSimulator`, `ModbusRtuSimulator`, `ModbusPduProcessor`, `ModbusDataStore`, `ModbusSimulatorStatistics` |
| `SiameseCat.Communication.Simulation.S7` | `SiameseCat.Communication.Simulation.S7` | `S7Simulator`, `S7PduProcessor`, `S7DataStore`, `S7SimulatorStatistics` |
| `SiameseCat.Communication.Simulation.Tcp` | `SiameseCat.Communication.Tcp` | `CustomTcpSimulator`, `CustomTcpConnection`, `CustomTcpSimulatorStatistics` |
| `SiameseCat.Communication.Hosting` | `SiameseCat.Communication.Hosting` | `AddSiameseCat()` / `AddSiameseCatDriver()`, simulator hosting, health checks, `Microsoft.Extensions.Logging` adapter |

## Source layout

```text
src/
├── SiameseCat.Communication.Core/          namespace SiameseCat.Communication, .Transport, .Drivers, .Addressing, .Diagnostics
│   ├── CommunicationOptions.cs / RetryPolicy.cs
│   ├── Addressing/                         DeviceAddress, DeviceValue, DeviceValueCodec, DeviceTag, IDeviceAddressParser
│   ├── Diagnostics/                        ICommunicationLogger, CommunicationMetrics, CommunicationDiagnostics
│   ├── Drivers/Abstractions/               IDeviceDriver + capability interfaces + descriptor + state
│   ├── Drivers/Runtime/                    DriverBase (partials), DriverRegistry, DriverConfiguration
│   ├── Exceptions/                         one file per exception type
│   └── Transport/                          ITransport + TCP/UDP/serial implementations
├── SiameseCat.Communication.Tcp/           namespace SiameseCat.Communication.Tcp, .Simulation.Tcp
│   ├── Framing/                            ITcpFrameCodec, TcpFrameCodec, TcpFrameConnection
│   ├── Clients/                            CustomTcpClient, CustomTcpClientOptions
│   └── Simulator/                          CustomTcpSimulator, CustomTcpConnection, statistics
├── SiameseCat.Communication.Modbus/        namespace SiameseCat.Communication.Modbus
│   ├── Clients/                            ModbusTcpClient, ModbusRtuClient, ModbusTcpFrameCodec
│   └── Drivers/                            ModbusDriver, ModbusAddressParser, ModbusDriverFactory
├── SiameseCat.Communication.S7/            namespace SiameseCat.Communication.S7
│   ├── Clients/                            S7Client, S7Options
│   └── Drivers/                            S7Driver, S7AddressParser, S7DriverFactory
├── SiameseCat.Communication.Simulation.Modbus/  namespace SiameseCat.Communication.Simulation.Modbus
│   ├── IModbusSimulator.cs
│   ├── Options/                            ModbusSimulatorOptions / ModbusRtuSimulatorOptions
│   ├── Simulators/                         ModbusTcpSimulator / ModbusRtuSimulator
│   ├── Protocol/                           ModbusPduProcessor (+ .Files, .DeviceIdentification)
│   ├── Storage/                            ModbusDataStore (+ .Registers, .Bits, .Files) / ModbusDataStoreSnapshot
│   ├── Events/                             ModbusCoilWriteEventArgs / ModbusRegisterWriteEventArgs
│   ├── Diagnostics/                        ModbusSimulatorStatistics
│   └── Internal/                           ModbusSimulatorIdentifier
├── SiameseCat.Communication.Simulation.S7/      namespace SiameseCat.Communication.Simulation.S7
│   ├── IS7Simulator.cs
│   ├── Options/                            S7SimulatorOptions
│   ├── Simulators/                         S7Simulator
│   ├── Protocol/                           S7PduProcessor
│   ├── Storage/                            S7DataStore
│   ├── Events/                             S7WriteEventArgs
│   └── Diagnostics/                        S7SimulatorStatistics
├── SiameseCat.Communication.Hosting/       namespace SiameseCat.Communication.Hosting
│   └── DI registration, health checks, simulator hosting, logger adapter
├── SiameseCat.Communication.All/           aggregator package (CreateDefaultRegistry / AddSiameseCatDrivers)
├── Shared/                                 Guard.cs, included per assembly
└── SimulatorShared/                        SimulatorBufferSizes.cs, linked into both simulator projects
```

### TCP framing and reuse

`SiameseCat.Communication.Tcp` has no dependency on the device model: it only knows `ITransport`,
`ITcpFrameCodec` and `TcpFrameConnection`. That makes it reusable outside the driver layer and keeps
the protocol-specific parts small:

| Concern | Owner |
| --- | --- |
| Byte-stream connection, open/close | `TcpTransport` (`SiameseCat.Communication.Transport`) |
| Frame boundaries (raw, length prefix, delimiter, fixed length) | `TcpFrameCodec` / `ITcpFrameCodec` |
| Write + read one frame, optional trace logging | `TcpFrameConnection` |
| Request serialization, timeout, retries, reconnect | `CustomTcpClient` (or `ModbusClientBase` for Modbus) |
| Protocol payloads and validation | caller (`CustomTcpClient` users) or `ModbusTcpClient` |

Modbus TCP is the first consumer of the shared core: `ModbusTcpFrameCodec` supplies MBAP framing to
`TcpFrameConnection`, while `ModbusTcpClient` keeps the transaction-id, protocol-id, unit-id and PDU
validation. A vendor protocol can follow the same split without touching the Modbus code.

The folders group files by responsibility, while the namespace stays at the protocol level: everything
in the `SiameseCat.Communication.Simulation.Modbus` project is in that namespace, so the physical layout
can change without touching a `using` directive. `ModbusDataStore` is a single type split across partial
files by storage area (registers, bits, file records) because the areas share the buffer sizes and locks.

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
6. **Observe** — every attempt records metrics (`CommunicationMetrics`) and diagnostics (`CommunicationDiagnostics`), and the
   connector logs warnings with context (unit id, address).

## Capability model

`DriverDescriptor.Capabilities` advertises `Read`, `Write`, `BatchRead`, `BatchWrite`, `Subscribe`,
`SubscribeBatch` and `Health`. `DriverBase` validates capabilities at the entry point of each operation,
so a read-only driver fails fast with `NotSupportedException` instead of sending a malformed request.

Protocol hooks are virtual and throw by default:

```csharp
protected virtual Task<DeviceValue> DoReadAsync(DeviceAddress address, CancellationToken token);
protected virtual Task DoWriteAsync(DeviceAddress address, DeviceValue value, CancellationToken token);
protected virtual Task<IAsyncDisposable?> DoSubscribeAsync(DeviceAddress address, TimeSpan interval,
    Func<DeviceValue, Task> onChanged, CancellationToken token);          // null → poll
protected virtual Task<IAsyncDisposable?> DoSubscribeBatchAsync(IReadOnlyList<DeviceAddress> addresses,
    TimeSpan interval, Func<IReadOnlyList<DeviceValue>, Task> onChanged, CancellationToken token);
```

Declaring `KeepAliveInterval` without the `Health` capability is rejected in the constructor.

## Value and address model

- `DeviceAddress` — `Region`, `Offset`, `BitOffset?`, `DataType`, `Length` (elements for numeric arrays,
  characters/bytes for `String`/`Raw`), optional `Symbol`.
- `DeviceValue` — immutable payload with factories/accessors per data type, array factories
  (`FromUInt16Array`, `FromFloatArray`, …), `IsArray`/`Length`, optional `Quality` and `Timestamp`.
  Equality compares payload only, so subscription change detection ignores metadata.
- `DeviceValueCodec` — protocol-independent byte conversions (`ToBytes`, `FromBytes` with element count),
  used by the S7 driver and available to custom drivers.
- `DeviceTag` — named tag with scale/offset/unit and `ToEngineering`/`FromEngineering` helpers.

## Multi-targeting and AOT

The package ships three assets; the modern ones are floors, not ceilings:

| Asset | Consumed by |
| --- | --- |
| `lib/net8.0` | .NET 8 hosts |
| `lib/net10.0` | .NET 10 hosts |
| `lib/netstandard2.0` | .NET Framework 4.6.1+, .NET Core 2.x+, .NET 5–7, and any host that prefers the standard asset |

- .NET 9 resolves `lib/net8.0`, and .NET 11 / .NET 12 resolve `lib/net10.0`: a host always takes the
  nearest asset at or below its own version, so the package is never pinned to one runtime version.
  `tests/SiameseCat.FrameworkMatrix` proves it by running the same Modbus round trip on .NET 8, .NET 10
  and .NET 11 and printing which asset was loaded.
- To produce an asset for a framework that is newer than `net10.0`, build with
  `-p:SiameseCatTargetFrameworks=net8.0;net11.0;netstandard2.0`; no source change is needed.
- Shared sources (`src/Shared/Guard.cs`) replace `ThrowIf*` helpers that do not exist on
  `netstandard2.0`; `IntervalTimer` replaces `PeriodicTimer`; `[GeneratedRegex]` is used on the modern
  targets and falls back to `RegexOptions.Compiled`; UDP uses `ArrayPool` + `ArraySegment` to bridge
  Span APIs.
- `IsAotCompatible` and the trimming/AOT analyzers run for the modern targets. There is no reflection,
  `dynamic`, runtime serialization or code generation anywhere in the package.
- `EnablePackageValidation` compares the public API of every asset at pack time, so an API cannot be
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
  failure messages lazily, and `CommunicationDiagnostics` caches operation-name tag values instead of formatting the
  enum on every request.

Measured on a 14-core Windows machine, loopback TCP, 20k requests per scenario, median of 3 runs
(baseline = the straightforward implementation without buffer reuse):

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
| Drivers use the protocol name | `ModbusDriver`, `S7Driver`, `S7AddressParser` |
| Device-domain types use `Device`; diagnostics use `Communication` | `DeviceAddress`, `DeviceValue`, `IDeviceDriver`, `IDeviceAddressParser`, `ICommunicationLogger`, `CommunicationMetrics` |
| One public type per file | `Exceptions/CommunicationTimeoutException.cs` |
| Optional features are separate namespaces | `SiameseCat.Communication.Hosting`, `SiameseCat.Communication.Simulation` |

See [protocol-matrix.md](protocol-matrix.md) for the wire-level details and
[migration.md](migration.md) when integrating the package into an existing stack.
