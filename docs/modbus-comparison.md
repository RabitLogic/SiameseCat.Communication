# Comparison with NModbus and FluentModbus

[![NModbus](https://img.shields.io/badge/NModbus-3.0.83-blue)](https://github.com/NModbus/NModbus)
[![FluentModbus](https://img.shields.io/badge/FluentModbus-5.3.2-blue)](https://github.com/Apollo3zehn/FluentModbus)

NModbus and FluentModbus are the two most used Modbus libraries for .NET. SiameseCat implements its own
Modbus stack, so this page records how the three compare and what the measured differences are.

All three were exercised against the **same in-process slave** (`SiameseCat.Communication.Server.Modbus.ModbusTcpServer`
on loopback TCP), so the bytes on the wire, the addressing and the latency are identical:

```csharp
await using var slave = new ModbusTcpServer(new ModbusServerOptions { Port = 0 });
await slave.StartAsync();

// SiameseCat
await using var ours = new ModbusTcpClient(new ModbusTcpOptions
{
    UnitId = 1,
    Tcp = new TcpTransportOptions { Host = "127.0.0.1", Port = slave.Port },
});
await ours.ConnectAsync();
await ours.ReadHoldingRegistersAsync(0, 10);

// NModbus
var master = new ModbusFactory().CreateMaster(new TcpClient("127.0.0.1", slave.Port));
master.ReadHoldingRegisters(1, 0, 10);

// FluentModbus
var fluent = new FluentModbus.ModbusTcpClient();
fluent.Connect(new IPEndPoint(IPAddress.Loopback, slave.Port), ModbusEndianness.BigEndian);
fluent.ReadHoldingRegisters<ushort>(1, 0, 10);
```

## Scope

| | SiameseCat | NModbus 3.0.83 | FluentModbus 5.3.2 |
| --- | --- | --- | --- |
| Modbus master | RTU, TCP | RTU, ASCII, TCP, UDP | RTU, TCP, UDP (client and server) |
| Modbus slave | `SiameseCat.Communication.Server` (RTU + TCP, FC01…FC17h) | `ModbusFactory.CreateSlave(Network)` | `ModbusTcpServer` / `ModbusRtuServer` |
| Other protocols | S7 (client + slave), raw TCP/UDP/Serial | Modbus only | Modbus only |
| Abstraction | `IPlcDriver` + capability interfaces + registry + metrics + subscriptions | `IModbusMaster` with sync/async pairs | `ModbusClient` family, span-based, sync-first |
| Target frameworks | `net8.0`, `netstandard2.0` | `net46`, `netstandard1.3`, `netstandard2.0`, `net6.0` | `netstandard2.0`, `netstandard2.1`, `net5.0`+ |
| License | MIT | MIT | MIT |
| Tests in CI of this repo | yes (`SiameseCat.Tests`) | n/a | n/a |

## Feature matrix (Modbus master)

| Feature | SiameseCat | NModbus | FluentModbus |
| --- | --- | --- | --- |
| FC01/02/03/04 read | ✅ | ✅ | ✅ |
| FC05/06/0F/10 write | ✅ | ✅ | ✅ |
| FC17h read/write multiple | ✅ | ✅ | ✅ |
| FC16 mask write (client) | ❌ (slave only) | ❌ | ⚠️ `MaskWriteRegister` (not implemented in 5.3.2) |
| FC07/0B/11 diagnostics | ❌ client (slave ✅) | ⚠️ via custom message | ❌ |
| Device identification (FC2B) | ❌ | ✅ `ReadDeviceIdentification` | ❌ |
| File record (FC14/15) | ❌ | ✅ | ⚠️ stub |
| Broadcast (unit id 0) | ✅ `unitId = 0` | ✅ `BroadcastWriteSingleRegister` | ⚠️ manual |
| Async API | ✅ everywhere, cancellable | ✅ async pairs (no cancellation on the base master) | ✅ async (adds an extra `Task` per call) |
| Allocation control | pooled buffers per client instance | allocates a message + array per call | returns `Span<T>` over a reusable buffer |
| Typed access | `PlcValue`, arrays, `PlcTag` scaling | raw `ushort[]` / `bool[]` | generic `ReadHoldingRegisters<T>` |
| Retry / reconnect / keep-alive | ✅ built into `DriverBase` | ❌ | ❌ |
| Metrics / tracing | ✅ `PlcMetrics` + `System.Diagnostics.Metrics` | ❌ | ❌ |
| Change subscriptions | ✅ polling, single + batch, jitter/back-off | ❌ | ❌ |
| Transport injection | ✅ `ITransport` / `ITransportFactory` | ✅ `IStreamResource` | ✅ `Initialize(TcpClient, ...)` |
| Native AOT | ✅ verified in CI | ✅ (no reflection) | ✅ (no reflection) |
| Tag/addressing helpers | ✅ `HR10`, `IR0`, `C0`, `DI0`, `40001`, `DB1.DBW2` | ❌ numeric only | ❌ numeric only |

## Measured performance

Windows 10, 14 logical cores, loopback TCP, 20 000 iterations per scenario after a 2 000 iteration warmup,
median of 3 runs, `GC.GetTotalAllocatedBytes(precise: true)` per operation. Two independent sessions
produced the same numbers within a few percent for allocations and 2–15 % for throughput (loopback
throughput is sensitive to machine load; allocations are stable).

### Read 10 holding registers

| Client | Throughput | Allocation |
| --- | --- | --- |
| SiameseCat `ModbusTcpClient` (async) | ~33 000 op/s | **1 536 B/op** |
| NModbus (sync) | ~31 700 op/s | 2 139 B/op |
| NModbus (async) | ~28 000 op/s | 2 401 B/op |
| FluentModbus (sync, `Span<ushort>`) | ~35 400 op/s | **470 B/op** |
| FluentModbus (async, `Memory<ushort>`) | ~42 000 op/s | 1 575 B/op |

### Write one holding register

| Client | Throughput | Allocation |
| --- | --- | --- |
| SiameseCat (async) | ~40 000 op/s | **1 595 B/op** |
| NModbus (sync) | ~32 000 op/s | 2 190 B/op |
| NModbus (async) | ~28 000 op/s | 2 454 B/op |
| FluentModbus (sync) | ~35 000 op/s | **478 B/op** |

### Read 100 holding registers

| Client | Throughput | Allocation |
| --- | --- | --- |
| SiameseCat (async) | ~40 000 op/s | **1 895 B/op** |
| NModbus (sync) | ~31 500 op/s | 3 400 B/op |
| FluentModbus (sync) | ~34 300 op/s | **471 B/op** |

### Reading the numbers

* **FluentModbus wins on allocation** in its *synchronous* API because it hands out a `Span<T>` over an
  internal reusable buffer — no per-call array at all. That span is only valid until the next call, so it
  cannot be stored or used across an `await`; its async API copies into a `Memory<T>` and lands at
  ~1.57 kB/op, the same order as SiameseCat.
* **SiameseCat allocates ~1.5 kB per round trip** and stays flat as the payload grows (1 536 → 1 895 B/op
  when the response grows tenfold), because the socket buffer, the header buffer and the timeout source are
  reused per client. That is 28 % less than NModbus sync and 36 % less than NModbus async.
* **NModbus** builds a request message and a response message object per call, which is why its async path
  is the most expensive of the four.
* Throughput differences of 10–20 % on loopback are less meaningful than they look: a real PLC answers in
  milliseconds, so protocol allocation and request count dominate, not microbenchmark op/s. This is why the
  driver layer batches (`ReadBatchAsync`, `SubscribeBatchAsync`) instead of optimising single calls harder.

## When to pick which

| If you need… | Choose |
| --- | --- |
| Modbus only, synchronous code, tight memory budget, spans are fine | **FluentModbus** |
| Modbus only, familiar API, ASCII/UDP, device identification, file records | **NModbus** |
| Modbus **and** S7 in one application, one address/value model, retries, metrics, batch and subscriptions | **SiameseCat** |
| A Modbus/S7 **slave** for tests or offline development | **SiameseCat.Communication.Server** (or FluentModbus's server) |
| Native AOT with no reflection | all three (SiameseCat and FluentModbus also offer AOT-friendly typed helpers) |

The honest summary: for a Modbus-only, allocation-critical, single-protocol reader, FluentModbus's
span-based synchronous API is the leanest option. SiameseCat's value is the layer above the frame —
`PlcAddress`/`PlcValue`, capability-driven drivers, batch and subscription handling, metrics, retries and a
matching slave — and it gets there with measurably less allocation than NModbus.

## Reproducing

The numbers come from a console harness that references `SiameseCat.Communication.Modbus`, `SiameseCat.Communication.Server`,
`NModbus` 3.0.83 and `FluentModbus` 5.3.2 and points all three clients at one `ModbusTcpServer`. The
library's own BenchmarkDotNet suite (`benchmarks/SiameseCat.Benchmarks`) measures the same paths without
third-party packages:

```powershell
dotnet run --project benchmarks/SiameseCat.Benchmarks -c Release -- --filter "*Modbus*"
```
