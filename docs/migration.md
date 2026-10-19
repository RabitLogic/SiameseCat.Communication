# Migration and integration notes

## Choosing namespaces

There is exactly one package:

```bash
dotnet add package SiameseCat.Communication
```

It contains the protocol clients, the driver layer, the slaves, the dependency-injection helpers and the
health checks. Pick what you need by namespace:

| Scenario | Namespaces |
| --- | --- |
| Direct protocol access (Modbus/S7 clients) | `SiameseCat.Communication`, `.Transport`, `.Modbus`, `.S7` |
| Driver abstraction (registry, retries, metrics) | + `SiameseCat.Communication.Drivers`, `.Siemens` |
| ASP.NET Core / `Microsoft.Extensions.Hosting` | + `SiameseCat.Communication.Hosting` |
| Simulator or integration tests | + `SiameseCat.Communication.Server.Modbus`, `.Server.S7` |

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
| Socket/`SerialPort` plumbing per device | `DriverConfiguration` + `ITransport` (TCP, UDP, serial, custom) |

Behavioural differences to be aware of:

- **Word order**: `ModbusRegisterConverter` defaults to `HighWordFirst`; pass `ModbusWordOrder.LowWordFirst`
  explicitly if your devices use the opposite order.
- **Timeouts** are per request (`Timeout`), not per socket read; serial `ReadTimeout` should be smaller so
  the protocol timeout is observed first.
- **Writes are not retried** by default (`RetryWrites = false`) because not every device is idempotent.

## Integrating a device step by step

1. `dotnet add package SiameseCat.Communication`.
2. Pick a driver: `modbus` for any Modbus RTU/TCP device, `siemens` for S7, or implement `IPlcDriver`
   (usually by deriving from `DriverBase`) for a new protocol or brand.
3. Configure `DriverConfiguration` (`Transport`, `Host`/`Port` or `Serial`, `Timeout`, retries) and set
   `ProbeAddress` so health checks and keep-alive have a harmless address to read.
4. Map addresses with `driver.AddressParser.Parse("40001" | "HR10" | "DB1.DBW2")`, or describe them once
   as `PlcTag` objects.
5. Run a sample against a device (`dotnet run --project samples/SiameseCat.Samples -- run modbus/tcp
   --host ...`) or against the in-process slave, then run `tools/SiameseCat.Soak` for a longer session.

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

- The package is `IsAotCompatible`; keep `PublishAot` enabled only in the executable project, not as a
  global property, otherwise `netstandard2.0` targets fail with `NETSDK1207`.
- `System.IO.Ports` works under Native AOT on Windows and Linux; macOS needs the usual USB-serial driver.
- If you enable `LogFrames`, expect `Trace` traffic in production builds; gate it behind configuration.

## Versioning policy

- Semantic Versioning; the 1.0.0 baseline is validated by `EnablePackageValidation`.
- Adding a member to an interface is a breaking change for external implementers — new optional
  capabilities therefore ship as separate interfaces (for example `IBatchSubscribable`).
- Deprecated APIs stay for at least one minor release and are marked `[Obsolete]` with a replacement.
