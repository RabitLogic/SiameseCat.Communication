# Protocol matrix

Everything SiameseCat speaks, on the wire and in the API.

## Modbus

### Master (client) function codes

| FC | Operation | API | Limits |
| --- | --- | --- | --- |
| `0x01` | Read coils | `ReadCoilsAsync` | 2000 bits per request |
| `0x02` | Read discrete inputs | `ReadDiscreteInputsAsync` | 2000 bits per request |
| `0x03` | Read holding registers | `ReadHoldingRegistersAsync` | 125 registers per request |
| `0x04` | Read input registers | `ReadInputRegistersAsync` | 125 registers per request |
| `0x05` | Write single coil | `WriteSingleCoilAsync` | one bit |
| `0x06` | Write single register | `WriteSingleRegisterAsync` | one register |
| `0x0F` | Write multiple coils | `WriteMultipleCoilsAsync` | 1968 bits per request |
| `0x10` | Write multiple registers | `WriteMultipleRegistersAsync` | 123 registers per request |
| `0x14` | Read file record | `ReadFileRecordsAsync` | up to 35 records, response PDU ≤ 253 bytes |
| `0x15` | Write file record | `WriteFileRecordsAsync` | request PDU ≤ 253 bytes |
| `0x17` | Read/write multiple registers | `ReadWriteMultipleRegistersAsync` | read ≤125, write ≤123 |
| `0x2B` | Read device identification | `ReadDeviceIdentificationAsync` | MEI type `0x0E`, continuation pages followed automatically |

- Larger batches are split automatically by the driver (`IBatchReadable` / `IBatchWritable`).
- `unitId = 0` switches to broadcast: writes are sent without waiting for a response, reads are rejected
  with `ArgumentException`.
- RTU framing adds the CRC16 (Modbus polynomial, low byte first) and respects the 3.5-character silence
  (derived from the baud rate unless `interFrameDelayMs` is set). Some RS-485 converters echo the request;
  set `discardEcho` to drop it.
- TCP framing is MBAP (transaction id, protocol id, length, unit id); transactions are validated.

### Slave function codes (`SiameseCat.Communication.Server`)

| FC | Operation | Behaviour |
| --- | --- | --- |
| `0x01` / `0x02` | Read coils / discrete inputs | `02` when the range exceeds the buffer |
| `0x03` / `0x04` | Read holding / input registers | `02` when the range exceeds the buffer |
| `0x05` | Write single coil | Value must be `0x0000` or `0xFF00`, otherwise `03` |
| `0x06` | Write single register | Echoes the request |
| `0x07` | Read exception status | Returns `ModbusDataStore.ExceptionStatus` |
| `0x0B` | Get communication event counter | Status `0x0000`, counter = `Data.EventCount` |
| `0x0F` | Write multiple coils | Echoes start + quantity |
| `0x10` | Write multiple registers | Echoes start + quantity |
| `0x11` | Report server ID | `ServerIdentifier` bytes + run indicator (`0xFF` / `0x00`) |
| `0x14` | Read file record | `ModbusDataStore` file records; `02` for an unknown file or record |
| `0x15` | Write file record | Echoes the request; `02` for an unknown file, `03` for malformed lengths |
| `0x16` | Mask write register | `(current AND andMask) OR (orMask AND NOT andMask)`, echoes the request |
| `0x17` | Read/write multiple registers | Write first, then read (atomic within the slave) |
| `0x2B` | Read device identification | `ModbusServerOptions.DeviceIdentification`, paged to fit 253 bytes |

- Exception codes: `01` illegal function, `02` illegal data address, `03` illegal data value.
- Unit id is echoed back, so one slave serves several stations; unit id `0` = broadcast (writes applied,
  no response).
- TCP accepts 1-byte PDUs (`0x07`/`0x0B`/`0x11` requests are function code only); RTU infers the request
  length per function code (fixed 8-byte frames for `0x01`–`0x06`, 4 bytes for `0x07`/`0x0B`/`0x11`,
  7 bytes for `0x2B`, 10 bytes for `0x16`, variable for `0x0F`/`0x10`/`0x17`, and 5 + byte count for
  `0x14`/`0x15`).
- `ModbusServerStatistics` records total requests, per-FC requests and exceptions, received frames and
  discarded frames (bad CRC, invalid MBAP length, truncated frames).

### Device identification

`ReadDeviceIdentificationAsync` sends function code `0x2B` with MEI type `0x0E` and returns the objects as
raw bytes (vendor specific objects are not always text) plus typed accessors for the standard ones.

```csharp
var basic = await client.ReadDeviceIdentificationAsync();                               // 0x00..0x02
var regular = await client.ReadDeviceIdentificationAsync(1, ModbusDeviceIdCode.Regular); // 0x00..0x06

Console.WriteLine($"{regular.VendorName} / {regular.ProductCode} rev {regular.MajorMinorRevision}");

foreach (var pair in regular.Objects)
{
    Console.WriteLine($"0x{pair.Key:X2} = {regular.GetObjectText(pair.Key)}");
}
```

| Object | Meaning | Category |
| --- | --- | --- |
| `0x00` | Vendor name | basic |
| `0x01` | Product code | basic |
| `0x02` | Major/minor revision | basic |
| `0x03` | Vendor URL | regular |
| `0x04` | Product name | regular |
| `0x05` | Model name | regular |
| `0x06` | User application name | regular |
| `0x80`–`0xFF` | Vendor specific | extended |

The client follows continuation pages (`MoreFollows` / `NextObjectId`) by default, so `Objects` holds the
whole category; pass `followMore: false` to read a single page, or use `ModbusDeviceIdCode.Individual` to
read one object id. On the slave side the objects are configured through
`ModbusServerOptions.DeviceIdentification` and default to the vendor name, the package id and the assembly
version.

### File records

File records are register blocks addressed by file number and record number. Every record in a file has
the same length, and the slave keeps them in `ModbusDataStore`:

```csharp
var options = new ModbusServerOptions { Port = 0 };
await using var slave = new ModbusTcpServer(options);
slave.Data.DefineFile(fileNumber: 4, recordLength: 2, recordCount: 16);  // 16 records of 2 registers
```

```csharp
await client.WriteFileRecordsAsync(new[]
{
    new ModbusFileRecord(4, 0, new ushort[] { 0x1234, 0x5678 }),
});

var records = await client.ReadFileRecordsAsync(new[]
{
    new ModbusFileRecordRequest(4, 0, 2),
});
```

One request can carry several records (up to 35 read sub-requests) and the result is ordered like the
request. File numbers start at 1, record numbers at 0, and a record holds 1 to 125 registers. The slave
echoes writes and the client validates the echo; `unitId = 0` broadcasts a write.

### Address syntax

`ModbusAddressParser` turns the notations used in device documentation into a protocol-neutral
`DeviceAddress`. The protocol itself has no textual address format: the clients take a zero-based numeric
offset only, so this notation lives in the driver layer and can be replaced through
`IDeviceAddressParser`.

| Notation | Example | Area | Zero-based offset |
| --- | --- | --- | --- |
| Reference numbers | `00001`–`09999` | Coils | `N - 1` |
| | `10001`–`19999` | Discrete inputs | `N - 10001` |
| | `30001`–`39999` | Input registers | `N - 30001` |
| | `40001`–`49999` | Holding registers | `N - 40001` |
| | `400001`–`465536` | Holding registers (6-digit) | `N - 400001` |
| Named | `HR10`, `IR10`, `C0`, `DI0` | Holding / input / coil / discrete | literal |
| Explicit region | `holding:10`, `input=10`, `coil:0`, `discrete:0` | as named | literal |
| Bare number | `10` | Holding registers | literal |

A plain number is classified by its **digit count**, not by intent:

| Input | Result | Reason |
| --- | --- | --- |
| `1000` | holding register 1000 | four digits: zero-based offset |
| `10000` | `FormatException` | five digits: a `1xxxx` reference number, whose record number `0000` does not exist |
| `01000` | coil 999 | five digits; the leading zero is a reference-number prefix, not padding |

That trap is inherent to the Modicon notation, so write `holding:1000` or `HR1000` whenever an offset
must be unambiguous (configuration files, tag tables, generated code).

S7 native notation (`DB1.DBW2`, `MW2`, `I0.1`) is **not** a Modbus address and is rejected here; it
belongs to `S7AddressParser`. Vendor nouns such as `D100` or `M10` are not guessed either: map
them in the vendor driver or write `holding:100`.

### Data types and register usage

| `DeviceDataType` | Registers | Notes |
| --- | --- | --- |
| `Bool` | — (bit) | Coils/discrete inputs only; a single bit per address |
| `Byte` | 1 | Low byte of the register (server side) |
| `Int16` / `UInt16` | 1 | |
| `Int32` / `UInt32` / `Float` | 2 | Word order `HighWordFirst` (default) or `LowWordFirst` |
| `Int64` / `UInt64` / `Double` | 4 | Same word-order rules |
| `String` | ⌈`Length` / 2⌉ | Optional byte swap inside each register |
| `Raw` | ⌈`Length` / 2⌉ | Raw bytes |
| Arrays (`Length > 1`) | element size × `Length` | `Int16`…`Double` only |

`ModbusRegisterConverter` exposes the conversions without a client: `ReadInt16`/`WriteInt16`,
`ReadUInt32`/`WriteUInt32`, `ReadInt64`, `ReadSingle`/`WriteSingle`, `ReadDouble`/`WriteDouble`,
`ReadBytes`/`WriteBytes`, `ReadString`/`WriteString`, plus `ModbusWordOrder`.

## S7

- Transport: ISO-on-TCP (RFC 1006) + COTP + S7 PDU, TCP only.
- Setup Communication requests PDU size 960; the negotiated value is capped at 960 and stored in
  `S7Client.PduSize`. Multi-variable requests are split automatically when they exceed the PDU.
- Address syntax: `DB<n>.DBX<byte>.<bit>`, `DB<n>.DBB<byte>`, `DB<n>.DBW<byte>`, `DB<n>.DBD<byte>`,
  `DB<n>.DB<type><byte>` for arrays, plus `M`, `I`, `Q` areas (`M0.0`, `MW2`, `IW4`, `Q0.0`) and
  `S7Address.Parse` / `TryParse` with value equality.
- Supported data types: bit, byte, word (`UInt16`), dword (`UInt32`), int, dint, real (`Float`),
  `String` (S7 format `[maxLen, actualLen, chars…]`) and numeric arrays.
- Batch reads use one multi-variable request; batch writes use one multi-variable write, including
  `STRING` values (each string is a normal data item, padded to an even length).
- Bit arrays: `S7Client.WriteBitsAsync(startAddress, bool[])` writes consecutive bits in one request,
  crossing byte boundaries, split by the negotiated PDU.
- Not supported: upload/download of blocks, diagnostic buffer and other advanced S7 services.

## S7 slave (`SiameseCat.Communication.Server`)

`S7Server` speaks ISO-on-TCP (TPKT + COTP + S7 PDU) and answers the same services the client uses:

| Service | Function | Behaviour |
| --- | --- | --- |
| Setup Communication | `0xF0` | Returns the configured PDU size (default 960; range 240–960) |
| Read Var | `0x04` | One result item per request item: `0xFF`, transport size, bit length, payload (padded to even) |
| Write Var | `0x05` | One return code per request item; `0xFF` means success |

- COTP: the connection request is answered with a Connection Confirm (TPDU size 1024).
- Areas: `M` (`0x83`), `I` (`0x81`), `Q` (`0x82`) and data blocks (`0x84`). Timers/counters answer
  `0x03` (access denied). Data blocks are created on demand unless `AutoCreateDataBlocks` is disabled.
- Addressing: specifications always carry a 24-bit **bit** address; byte access divides it by eight.
- Transport sizes accepted on writes and produced on reads: `BYTE`/`CHAR` (1 byte/element),
  `WORD`/`INT` (2), `DWORD`/`DINT`/`REAL` (4). Unknown sizes answer `0x04`.
- Return codes: `0xFF` success, `0x03` access denied, `0x04` data type not supported, `0x05` address out
  of range, `0x06` data error, `0x0A` object does not exist.
- `S7ServerStatistics` reports connections, active connections, setup/read/write requests, items read and
  written, item errors and discarded frames.
- `S7DataStore` exposes byte and bit access plus a `Written` event with previous values, and `Reset()`.

## Custom TCP (`SiameseCat.Communication.Tcp`)

Protocol-agnostic framing and request/response plumbing. This namespace does not use `DeviceAddress` or
`DeviceValue`, so it also works outside the driver layer.

| `TcpFrameMode` | Wire format | Options |
| --- | --- | --- |
| `Raw` | every transport read is one frame | none |
| `LengthPrefix` | length field + payload | `PrefixLength` (1/2/4), `BigEndian`, `LengthIncludesPrefix` |
| `Delimiter` | payload + delimiter | `Delimiter` (default CR LF), `IncludeDelimiter` |
| `FixedLength` | constant frame size | `FixedLength` |

- `MaxFrameLength` (default 64 KiB) bounds every mode; an oversized frame raises `ProtocolException`.
- Implement `ITcpFrameCodec` for escaping, checksums or a non-standard header.
- `TcpFrameConnection` writes and reads whole frames over any `ITransport` and can log TX/RX at trace
  level; timeouts, retries and serialization stay with the owner of the connection.
- `CustomTcpClient` adds those semantics for a client (`RequestAsync`, `SendAsync`, `ReadFrameAsync`).
- `ModbusTcpFrameCodec` applies MBAP framing (length 2..254) and returns the complete frame so
  `ModbusTcpClient` validates transaction id, protocol id and unit id before parsing the PDU.
- `CustomTcpServer` accepts connections and hands one `CustomTcpConnection` to a handler per client.

## Transports

| Transport | Type | Highlights |
| --- | --- | --- |
| `TcpTransport` | stream | connect timeout, Nagle toggle, keep-alive tuning (net8.0 only) |
| `UdpTransport` | datagram | connected socket, same `ITransport` contract |
| `SerialPortTransport` | stream | baud rate, parity, data bits, stop bits, handshake; cancellation closes the port |
| `SharedTransportFactory` | wrapper | one physical link shared by several drivers, with a shared `RequestLock` |
| `LoggingTransport` | decorator | enabled by `LogFrames`, writes `TX`/`RX` hex at `Trace` |
| `TcpFrameConnection` | framing wrapper | adds `ITcpFrameCodec` framing over any transport; optionally logs `TX`/`RX` |

## Version and asset selection

| Host | Selected asset |
| --- | --- |
| .NET 8 | `lib/net8.0` |
| .NET 9 | `lib/net8.0` (nearest lower) |
| .NET 10 | `lib/net10.0` |
| .NET 11 and later | `lib/net10.0` (nearest lower) |
| .NET Framework 4.6.1 – 4.8 | `lib/netstandard2.0` |
| .NET Core 2.x, .NET 5 – 7 | `lib/netstandard2.0` |

The assets are floors, not ceilings: a host always takes the nearest asset at or below its own version,
so a new runtime (for example .NET 12) keeps working without a package update. Building an asset for a
newer framework only requires
`dotnet pack -p:SiameseCatTargetFrameworks=net8.0;net12.0;netstandard2.0`.

