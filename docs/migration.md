# Migration and upgrade notes

## Choosing packages

| Scenario | Packages |
| --- | --- |
| Custom host, direct protocol access | `SiameseCat.Core` + `SiameseCat.Transport` + `SiameseCat.Protocols` |
| Driver abstraction (registry, retries, metrics) | the above + `SiameseCat.Drivers` |
| ASP.NET Core / `Microsoft.Extensions.Hosting` | + `SiameseCat.Hosting` |
| Simulator or integration tests | + `SiameseCat.Server` |

You can start with the protocol clients and move to drivers later: both expose the same options
(`ModbusTcpOptions`, `ModbusRtuOptions`, `S7Options`) and the same address parsers.

## Coming from NModbus / Sharp7

| NModbus / Sharp7 | SiameseCat |
| --- | --- |
| `IModbusMaster.ReadHoldingRegisters(slave, start, count)` | `client.ReadHoldingRegistersAsync(start, count)` (unit id is a client option) |
| `master.WriteSingleRegister(...)` | `client.WriteSingleRegisterAsync(address, value)` |
| `ushort[]` batches assembled by hand | `IBatchReadable.ReadBatchAsync(addresses)` merges contiguous addresses |
| `Plc.Read("DB1.DBD0")` / `S7.ReadArea(...)` | `client.ReadSingleAsync(S7Address.Parse("DB1.DBD0"))` |
| Manual conversions (`BitConverter`) | `ModbusRegisterConverter` / `PlcValueCodec` |

Behavioural differences to be aware of:

- **Word order**: `ModbusRegisterConverter` defaults to `HighWordFirst`; pass `ModbusWordOrder.LowWordFirst`
  explicitly if your devices use the opposite order.
- **Timeouts** are per request (`Timeout`), not per socket read; serial `ReadTimeout` should be smaller so
  the protocol timeout is observed first.
- **Writes are not retried** by default (`RetryWrites = false`) because not every device is idempotent.

## Upgrading from pre-1.0 builds

The 1.0.0 release renamed a few types and projects. The table below maps the old names.

| Old | New |
| --- | --- |
| `SiameseCat.Integration` (package/project) | `SiameseCat.Hosting` |
| `MicrosoftPlcLogger` | `MicrosoftLoggerAdapter` |
| `Tag` | `PlcTag` |
| `TagExtensions` | `PlcTagExtensions` |
| `IAddressParser` | `IPlcAddressParser` |
| `tools/SiameseCat.Tools` | `tools/SiameseCat.AotProbe` |
| `Modbus/Protocol/*`, `S7/Addressing/*` namespaces | folded into the protocol namespaces |
| `SiameseCat.Server` (single namespace) | `SiameseCat.Server.Modbus` and `SiameseCat.Server.S7` — add the matching `using`; `AddModbusServer()` / `AddS7Server()` stay in `Microsoft.Extensions.DependencyInjection` |

Also in 1.0.0:

- `DoReadAsync` / `DoWriteAsync` became virtual (default: throw `NotSupportedException`). Drivers that
  used to implement empty writes can drop them; drivers that relied on being called must declare the
  capability.
- `DriverDescriptor.Capabilities` gates every operation; add the flags you support.
- `PlcAddress.Length` for numeric types now means **elements**, not registers. Code that built
  `Length` from a register count for `Int32`/`Float`/`Double` must divide by the element width.
- `PlcValue` scalars throw `InvalidCastException` when accessed as a different type; array values must
  be read with `AsUInt16Array()` etc.

## Multi-targeting notes (netstandard2.0)

When your host is .NET Framework 4.8 (or any `netstandard2.0` consumer):

- The package resolves `lib/netstandard2.0`; public API is identical to `lib/net8.0` (enforced by
  `EnablePackageValidation`).
- AOT/trimming analyzers and source-generated regexes are not used; regexes fall back to
  `RegexOptions.Compiled`.
- TCP keep-alive can be enabled but socket timing parameters are not tuned.
- Serial I/O has no cancellable async API: cancelling a read/write closes the port, so call
  `ConnectAsync` again afterwards.
- `tests/SiameseCat.Compatibility` is the reference smoke test; run it on Windows to validate the asset.

## Native AOT hosts

- The libraries are `IsAotCompatible`; keep `PublishAot` enabled only in the executable project, not as a
  global property, otherwise `netstandard2.0` targets fail with `NETSDK1207`.
- `System.IO.Ports` works under Native AOT on Windows and Linux; macOS needs the usual USB-serial driver.
- If you enable `LogFrames`, expect `Trace` traffic in production builds; gate it behind configuration.

## Versioning policy

- Semantic Versioning; the 1.0.0 baseline is validated by `EnablePackageValidation`.
- Adding a member to an interface is a breaking change for external implementers — new optional
  capabilities therefore ship as separate interfaces (for example `IBatchSubscribable`).
- Deprecated APIs stay for at least one minor release and are marked `[Obsolete]` with a replacement.
