# Protocol matrix

Everything SiameseCat speaks, on the wire and in the API.

## Modbus

### Master (client) function codes

| FC | Operation | API | Limits |
| --- | --- | --- | --- |
| `01` | Read coils | `ReadCoilsAsync` | 2000 bits per request |
| `02` | Read discrete inputs | `ReadDiscreteInputsAsync` | 2000 bits per request |
| `03` | Read holding registers | `ReadHoldingRegistersAsync` | 125 registers per request |
| `04` | Read input registers | `ReadInputRegistersAsync` | 125 registers per request |
| `05` | Write single coil | `WriteSingleCoilAsync` | one bit |
| `06` | Write single register | `WriteSingleRegisterAsync` | one register |
| `15` | Write multiple coils | `WriteMultipleCoilsAsync` | 1968 bits per request |
| `16` | Write multiple registers | `WriteMultipleRegistersAsync` | 123 registers per request |
| `23` | Read/write multiple registers | `ReadWriteMultipleRegistersAsync` | read ≤125, write ≤123 |

- Larger batches are split automatically by the driver (`IBatchReadable` / `IBatchWritable`).
- `unitId = 0` switches to broadcast: writes are sent without waiting for a response, reads are rejected
  with `ArgumentException`.
- RTU framing adds the CRC16 (Modbus polynomial, low byte first) and respects the 3.5-character silence
  (derived from the baud rate unless `interFrameDelayMs` is set). Some RS-485 converters echo the request;
  set `discardEcho` to drop it.
- TCP framing is MBAP (transaction id, protocol id, length, unit id); transactions are validated.

### Slave function codes (`SiameseCat.Server`)

| FC | Operation | Behaviour |
| --- | --- | --- |
| `01` / `02` | Read coils / discrete inputs | `02` when the range exceeds the buffer |
| `03` / `04` | Read holding / input registers | `02` when the range exceeds the buffer |
| `05` | Write single coil | Value must be `0x0000` or `0xFF00`, otherwise `03` |
| `06` | Write single register | Echoes the request |
| `07` | Read exception status | Returns `ModbusDataStore.ExceptionStatus` |
| `0B` | Get communication event counter | Status `0x0000`, counter = `Data.EventCount` |
| `0F` | Write multiple coils | Echoes start + quantity |
| `10` | Write multiple registers | Echoes start + quantity |
| `11` | Report server ID | `ServerIdentifier` bytes + run indicator (`0xFF` / `0x00`) |
| `16` | Mask write register | `(current AND andMask) OR (orMask AND NOT andMask)`, echoes the request |
| `17` | Read/write multiple registers | Write first, then read (atomic within the slave) |

- Exception codes: `01` illegal function, `02` illegal data address, `03` illegal data value.
- Unit id is echoed back, so one slave serves several stations; unit id `0` = broadcast (writes applied,
  no response).
- TCP accepts 1-byte PDUs (FC07/0B/11 requests are function code only); RTU infers the request length per
  function code (fixed 8-byte frames for FC01–06, 4 bytes for FC07/0B/11, 10 bytes for FC16, variable for
  FC0F/10/17).
- `ModbusServerStatistics` records total requests, per-FC requests and exceptions, received frames and
  discarded frames (bad CRC, invalid MBAP length, truncated frames).

### Address syntax

| Notation | Example | Area | Zero-based offset |
| --- | --- | --- | --- |
| Reference numbers | `00001`–`09999` | Coils | `N - 1` |
| | `10001`–`19999` | Discrete inputs | `N - 10001` |
| | `30001`–`39999` | Input registers | `N - 30001` |
| | `40001`–`49999` | Holding registers | `N - 40001` |
| | `400001`–`465536` | Holding registers (6-digit) | `N - 400001` |
| Named | `HR10`, `IR10`, `C0`, `DI0` | Holding / input / coil / discrete | literal |
| Bare number | `10` | Holding registers | literal |

`ModbusAddressParser` also accepts `holding:10`, `input=10`, `coil:0` and `discrete:0` spellings.

### Data types and register usage

| `PlcDataType` | Registers | Notes |
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

## Siemens S7

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

## Inovance

Soft elements are mapped onto Modbus areas; the underlying client is the Modbus client, so all batching
and conversion rules apply.

| Element | Modbus area | Base offset | Notes |
| --- | --- | --- | --- |
| `D` | Holding registers | `dBase` (0) | 16-bit words |
| `M` | Coils | `mBase` (0) | Bits |
| `X` | Discrete inputs | `xBase` (0) | Octal numbering (`X10` = 8) |
| `Y` | Coils | `yBase` (8192) | Octal numbering |

Bases are configurable per series through `InovanceMapping` (option keys `dBase`, `mBase`, `xBase`,
`yBase`).

## Transports

| Transport | Type | Highlights |
| --- | --- | --- |
| `TcpTransport` | stream | connect timeout, Nagle toggle, keep-alive tuning (net8.0 only) |
| `UdpTransport` | datagram | connected socket, same `ITransport` contract |
| `SerialPortTransport` | stream | baud rate, parity, data bits, stop bits, handshake; cancellation closes the port |
| `SharedTransportFactory` | wrapper | one physical link shared by several drivers, with a shared `RequestLock` |
| `LoggingTransport` | decorator | enabled by `LogFrames`, writes `TX`/`RX` hex at `Trace` |

## Version and asset selection

| Host | Selected asset |
| --- | --- |
| .NET Framework 4.6.1 – 4.8 | `lib/netstandard2.0` |
| .NET Core 2.x, .NET 5 – 7 | `lib/netstandard2.0` |
| .NET 8 and later (including 9, 10, 11) | `lib/net8.0` |
