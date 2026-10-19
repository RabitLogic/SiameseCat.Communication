# Comparison with S7.Net Plus

> Looking for the Modbus comparison? See [modbus-comparison.md](./modbus-comparison.md) (NModbus and FluentModbus).

[S7.Net Plus](https://github.com/S7NetPlus/s7netplus) (`S7netplus`, MIT, ~1.6k stars) is the de‑facto
community S7 driver for .NET. SiameseCat implements its own S7 stack, so this page records how the two
compare and what the measured differences are.

Both were exercised against the **same in-process S7 slave** (`SiameseCat.Communication.Server.S7.S7Server`), so the
wire format, error handling and network latency are identical:

```csharp
await using var server = new S7Server(new S7ServerOptions { Port = 0, DefaultDataBlockSize = 1024 });
await server.StartAsync();

// SiameseCat
await using var local = new S7Client(new S7Options { Host = "127.0.0.1", Port = server.Port });
await local.ConnectAsync();
await local.ReadSingleAsync(S7Address.Parse("DB1.DBD0"));

// S7.Net Plus 0.20.0
var plc = new Plc(CpuType.S71200, "127.0.0.1", server.Port, 0, 1);
plc.Open();
plc.Read("DB1.DBD0");
```

## Scope

| | SiameseCat | S7.Net Plus |
| --- | --- | --- |
| Protocols | Modbus RTU/TCP, Siemens S7, raw TCP/UDP/Serial | S7 only |
| Abstraction | Address/value model, `IPlcDriver` + `DriverBase`, registry, capability flags, batch/subscription, metrics | `Plc` class with string addresses and `DataItem`s |
| Target frameworks | `net8.0` + `netstandard2.0` (package validation for both) | `net452`, `netstandard1.3`, `netstandard2.0` |
| Simulation | `SiameseCat.Communication.Server`: Modbus TCP/RTU **and** S7 slaves | none (tests use the native Snap7 DLL) |
| History | new (1.0) | continuation of S7.Net (2009), 657 forks, 116 open issues |

## Feature matrix (S7)

| Feature | SiameseCat | S7.Net Plus 0.20.0 |
| --- | --- | --- |
| COTP + Setup Communication, PDU negotiation | ✅ (960 max, ≥240 validated) | ✅ (`MaxPDUSize` from the response) |
| Areas | DB, M, I, Q | DB, M, I, Q (+ S7‑200 style addressing via `TsapPair`) |
| Bit / byte / word / dword / real / string | ✅ | ✅ |
| Arrays of primitives | ✅ (`PlcAddress.Length`, `AsUInt16Array()` …) | ✅ via `ReadBytes`/`DataItem` |
| Multi-variable read/write | ✅ `ReadMultipleAsync` / `WriteMultipleAsync`, chunked by PDU | ✅ `ReadMultipleVars` |
| Struct/class mapping | ❌ (deliberate: no reflection, AOT safe) | ✅ `ReadStruct<T>` / `ReadClass` / `WriteStruct` (reflection) |
| Sync API | ❌ (async only) | ✅ |
| Retry / reconnect | ✅ exponential back-off, write-safe retry policy, keep-alive | ❌ (caller reopens the socket) |
| Timeouts | per request + per connect, cancellable | `ReadTimeout` / `WriteTimeout` on the socket |
| Metrics / tracing | `PlcMetrics` + `System.Diagnostics.Metrics` (OpenTelemetry ready) | ❌ |
| Change subscriptions | ✅ polling (single and batch) with jitter/back-off | ❌ |
| Native AOT | ✅ verified by an AOT probe in CI | ⚠️ reflection-based struct mapping is not AOT friendly |
| Diagnostics (`ReadStatus`, SZL/order code) | ❌ | ✅ `ReadStatus` |

## Measured performance

Windows 10, 14 logical cores, loopback TCP, 20 000 iterations per scenario, median of 3 runs; both clients
point at the same `S7Server` instance. Allocations are stable across runs; throughput varies by a few
percent with machine load.

| Scenario | SiameseCat | S7.Net Plus (sync) | S7.Net Plus (async) |
| --- | --- | --- | --- |
| Read REAL (`DB1.DBD0`) | **2 262 B/op**, 25.5k op/s | 2 532 B/op, 19.7k op/s | 3 437 B/op, 38.2k op/s |
| Read REAL with per-call address parse | 3 047 B/op, 39.2k op/s | — | — |
| Write WORD (`DB1.DBW4`) | **1 934 B/op**, 36.5k op/s | 2 579 B/op, 20.2k op/s | 3 182 B/op, 42.6k op/s |
| Read 100 bytes | **2 263 B/op**, 43.3k op/s | 2 508 B/op, 20.4k op/s | — |

Reading the numbers:

- **Allocations** – with a pre-parsed `S7Address` (the intended usage) SiameseCat allocates 11% less per
  call than `Plc.Read` and 34% less than `Plc.ReadAsync`; writing is 25–39% cheaper. If you parse the
  address string on every call, the read cost rises above `Plc.Read` (3 047 vs 2 532 B/op), which is the
  price of the `S7Address.Parse` path — cache parsed addresses in hot loops.
- **Throughput** – the async variants land in the same 38–43k op/s band; the synchronous S7.Net Plus API
  is roughly half that on this machine because each call blocks on the socket.
- S7.Net Plus re-uses one `byte[]` buffer per response and parses with `BinaryPrimitives`; our remaining
  allocations come from the returned value objects plus async/socket machinery.

## When to choose which

Choose **S7.Net Plus** when:

- you only talk to Siemens PLCs and want the smallest possible surface,
- you need `ReadStruct<T>` / `ReadClass` DTO mapping or plan to use `ReadStatus` / SZL data,
- you prefer a synchronous API, or need S7‑200 specific addressing via `TsapPair`,
- you want the most battle-tested (2009‑era, heavily used) S7 code path.

Choose **SiameseCat** when:

- one application talks to several protocols (Modbus RTU/TCP, S7) behind one driver contract,
- you need retries, reconnects, keep-alive, state machine and metrics out of the box,
- you deploy with Native AOT/trimming (no reflection anywhere),
- you want subscriptions with batched polling and back-off,
- you need a simulator (S7 and Modbus slaves) for integration tests,
- you target .NET 8+ and .NET Framework 4.8 from the same package.

## Interoperability finding

S7.Net Plus 0.20.0 connects to `SiameseCat.Communication.Server`'s S7 slave without any tweaks and both reads and writes
behave correctly, which confirms the simulator speaks standard S7comm (COTP handshake, Setup
Communication, read variable, write variable). This also means you can use S7.Net Plus as an independent
client when validating the simulator.

## Reproducing

```powershell
# scratch project outside the solution (add S7netplus 0.20.0 + a SiameseCat.Communication.Server reference)
dotnet run --project <scratch>/Compare.csproj -c Release
```

The harness connects both clients to one `S7Server`, warms up 2 000 iterations, then measures
`GC.GetTotalAllocatedBytes(precise: true)` and elapsed time around a 20 000-iteration loop.
