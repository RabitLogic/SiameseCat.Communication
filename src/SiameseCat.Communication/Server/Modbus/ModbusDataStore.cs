using SiameseCat.Communication;

namespace SiameseCat.Communication.Server.Modbus;

/// <summary>
/// Thread-safe register and coil storage used by the Modbus TCP and RTU slaves.
/// The application can preload values or change them while a client is connected, and can listen
/// to <see cref="HoldingRegistersWritten"/>, <see cref="InputRegistersWritten"/>,
/// <see cref="CoilsWritten"/> and <see cref="DiscreteInputsWritten"/> to react to writes.
/// </summary>
/// <remarks>
/// Each area has its own lock, so traffic on holding registers does not block coil access.
/// Coils and discrete inputs are stored as a bit map.
/// </remarks>
public sealed class ModbusDataStore
{
    private readonly object _holdingSync = new();
    private readonly object _inputSync = new();
    private readonly object _coilSync = new();
    private readonly object _discreteSync = new();

    private readonly ushort[] _holdingRegisters;
    private readonly ushort[] _inputRegisters;
    private readonly uint[] _coils;
    private readonly uint[] _discreteInputs;
    private readonly int _coilCount;
    private readonly int _discreteInputCount;
    private int _exceptionStatus;
    private long _eventCount;

    /// <summary>Creates a store with the given buffer sizes.</summary>
    /// <param name="holdingRegisters">Number of holding registers (FC03/FC06/FC16/FC23).</param>
    /// <param name="inputRegisters">Number of input registers (FC04).</param>
    /// <param name="coils">Number of coils (FC01/FC05/FC15).</param>
    /// <param name="discreteInputs">Number of discrete inputs (FC02).</param>
    public ModbusDataStore(
        int holdingRegisters = 256,
        int inputRegisters = 256,
        int coils = 256,
        int discreteInputs = 256)
    {
        _holdingRegisters = new ushort[ValidateSize(holdingRegisters, nameof(holdingRegisters))];
        _inputRegisters = new ushort[ValidateSize(inputRegisters, nameof(inputRegisters))];
        _coilCount = ValidateSize(coils, nameof(coils));
        _discreteInputCount = ValidateSize(discreteInputs, nameof(discreteInputs));
        _coils = new uint[BitBufferLength(_coilCount)];
        _discreteInputs = new uint[BitBufferLength(_discreteInputCount)];
    }

    /// <summary>Raised after holding registers were written, including through a Modbus request.</summary>
    public event EventHandler<ModbusRegisterWriteEventArgs>? HoldingRegistersWritten;

    /// <summary>Raised after input registers were written.</summary>
    public event EventHandler<ModbusRegisterWriteEventArgs>? InputRegistersWritten;

    /// <summary>Raised after coils were written, including through a Modbus request.</summary>
    public event EventHandler<ModbusCoilWriteEventArgs>? CoilsWritten;

    /// <summary>Raised after discrete inputs were written.</summary>
    public event EventHandler<ModbusCoilWriteEventArgs>? DiscreteInputsWritten;

    /// <summary>Gets the number of holding registers.</summary>
    public int HoldingRegisterCount => _holdingRegisters.Length;

    /// <summary>Gets the number of input registers.</summary>
    public int InputRegisterCount => _inputRegisters.Length;

    /// <summary>Gets the number of coils.</summary>
    public int CoilCount => _coilCount;

    /// <summary>Gets the number of discrete inputs.</summary>
    public int DiscreteInputCount => _discreteInputCount;

    /// <summary>
    /// Gets or sets the exception status byte returned by function code 07.
    /// The application updates it to simulate device faults.
    /// </summary>
    public byte ExceptionStatus
    {
        get => (byte)Volatile.Read(ref _exceptionStatus);
        set => Volatile.Write(ref _exceptionStatus, value);
    }

    /// <summary>Gets how many write requests were completed, as reported by function code 11.</summary>
    public long EventCount => Interlocked.Read(ref _eventCount);

    /// <summary>Gets or sets a holding register. Indexes outside the buffer are ignored.</summary>
    /// <param name="index">Register offset.</param>
    public ushort this[int index]
    {
        get
        {
            lock (_holdingSync)
            {
                return index >= 0 && index < _holdingRegisters.Length ? _holdingRegisters[index] : (ushort)0;
            }
        }

        set => TryWriteSingleRegister(index, value);
    }

    /// <summary>Writes several holding registers.</summary>
    /// <param name="start">First register offset.</param>
    /// <param name="values">Values to write.</param>
    /// <returns><see langword="false"/> when the range does not fit the buffer.</returns>
    public bool TryWriteHoldingRegisters(int start, IReadOnlyList<ushort> values) =>
        WriteRegisters(start, values, _holdingRegisters, _holdingSync, HoldingRegistersWritten);

    /// <summary>Reads several holding registers.</summary>
    /// <param name="start">First register offset.</param>
    /// <param name="count">Number of registers.</param>
    /// <returns>The register values, or <see langword="null"/> when the range does not fit the buffer.</returns>
    public ushort[]? ReadHoldingRegisters(int start, int count) =>
        ReadRegisters(start, count, _holdingRegisters, _holdingSync);

    /// <summary>Writes several input registers (usually done by the simulated device, not by the client).</summary>
    /// <param name="start">First register offset.</param>
    /// <param name="values">Values to write.</param>
    /// <returns><see langword="false"/> when the range does not fit the buffer.</returns>
    public bool TryWriteInputRegisters(int start, IReadOnlyList<ushort> values) =>
        WriteRegisters(start, values, _inputRegisters, _inputSync, InputRegistersWritten);

    /// <summary>Reads several input registers.</summary>
    /// <param name="start">First register offset.</param>
    /// <param name="count">Number of registers.</param>
    /// <returns>The register values, or <see langword="null"/> when the range does not fit the buffer.</returns>
    public ushort[]? ReadInputRegisters(int start, int count) =>
        ReadRegisters(start, count, _inputRegisters, _inputSync);

    /// <summary>Writes several coils.</summary>
    /// <param name="start">First coil offset.</param>
    /// <param name="values">Values to write.</param>
    /// <returns><see langword="false"/> when the range does not fit the buffer.</returns>
    public bool TryWriteCoils(int start, IReadOnlyList<bool> values) =>
        WriteCoils(start, values, _coils, _coilCount, _coilSync, CoilsWritten);

    /// <summary>Reads several coils.</summary>
    /// <param name="start">First coil offset.</param>
    /// <param name="count">Number of coils.</param>
    /// <returns>The coil values, or <see langword="null"/> when the range does not fit the buffer.</returns>
    public bool[]? ReadCoils(int start, int count) =>
        ReadBits(start, count, _coils, _coilCount, _coilSync);

    /// <summary>Writes several discrete inputs (usually done by the simulated device, not by the client).</summary>
    /// <param name="start">First input offset.</param>
    /// <param name="values">Values to write.</param>
    /// <returns><see langword="false"/> when the range does not fit the buffer.</returns>
    public bool TryWriteDiscreteInputs(int start, IReadOnlyList<bool> values) =>
        WriteCoils(start, values, _discreteInputs, _discreteInputCount, _discreteSync, DiscreteInputsWritten);

    /// <summary>Reads several discrete inputs.</summary>
    /// <param name="start">First input offset.</param>
    /// <param name="count">Number of inputs.</param>
    /// <returns>The input values, or <see langword="null"/> when the range does not fit the buffer.</returns>
    public bool[]? ReadDiscreteInputs(int start, int count) =>
        ReadBits(start, count, _discreteInputs, _discreteInputCount, _discreteSync);

    /// <summary>Captures a consistent-enough copy of every area for later restore with <see cref="Load"/>.</summary>
    /// <returns>A snapshot that owns its buffers.</returns>
    public ModbusDataStoreSnapshot Snapshot()
    {
        ushort[] holding;
        lock (_holdingSync)
        {
            holding = (ushort[])_holdingRegisters.Clone();
        }

        ushort[] input;
        lock (_inputSync)
        {
            input = (ushort[])_inputRegisters.Clone();
        }

        bool[] coils;
        lock (_coilSync)
        {
            coils = ReadBitsUnlocked(0, _coilCount, _coils);
        }

        bool[] discreteInputs;
        lock (_discreteSync)
        {
            discreteInputs = ReadBitsUnlocked(0, _discreteInputCount, _discreteInputs);
        }

        return new ModbusDataStoreSnapshot(holding, input, coils, discreteInputs);
    }

    /// <summary>Replaces every area with the contents of a snapshot and raises the write events.</summary>
    /// <param name="snapshot">Snapshot taken from a store with the same buffer sizes.</param>
    /// <exception cref="ArgumentException">The snapshot dimensions differ from this store.</exception>
    public void Load(ModbusDataStoreSnapshot snapshot)
    {
        Guard.NotNull(snapshot, nameof(snapshot));

        if (snapshot.HoldingRegisters.Count != _holdingRegisters.Length
            || snapshot.InputRegisters.Count != _inputRegisters.Length
            || snapshot.Coils.Count != _coilCount
            || snapshot.DiscreteInputs.Count != _discreteInputCount)
        {
            throw new ArgumentException(
                "The snapshot dimensions do not match this store. Create a store with the same sizes before loading.",
                nameof(snapshot));
        }

        WriteRegisters(0, snapshot.HoldingRegisters, _holdingRegisters, _holdingSync, HoldingRegistersWritten);
        WriteRegisters(0, snapshot.InputRegisters, _inputRegisters, _inputSync, InputRegistersWritten);
        WriteCoils(0, snapshot.Coils, _coils, _coilCount, _coilSync, CoilsWritten);
        WriteCoils(0, snapshot.DiscreteInputs, _discreteInputs, _discreteInputCount, _discreteSync, DiscreteInputsWritten);
    }

    /// <summary>Clears every area to zero and raises the write events, simulating a device power cycle.</summary>
    public void Reset()
    {
        WriteRegisters(0, new ushort[_holdingRegisters.Length], _holdingRegisters, _holdingSync, HoldingRegistersWritten);
        WriteRegisters(0, new ushort[_inputRegisters.Length], _inputRegisters, _inputSync, InputRegistersWritten);
        WriteCoils(0, new bool[_coilCount], _coils, _coilCount, _coilSync, CoilsWritten);
        WriteCoils(0, new bool[_discreteInputCount], _discreteInputs, _discreteInputCount, _discreteSync, DiscreteInputsWritten);
        ExceptionStatus = 0;
        Interlocked.Exchange(ref _eventCount, 0);
    }

    internal void RecordEvent() => Interlocked.Increment(ref _eventCount);

    /// <summary>Applies a mask write (function code 22) to one holding register.</summary>
    /// <param name="index">Register offset.</param>
    /// <param name="andMask">Bits that are preserved.</param>
    /// <param name="orMask">Bits that are set.</param>
    /// <param name="result">The new register value.</param>
    /// <returns><see langword="false"/> when the offset is outside the buffer.</returns>
    internal bool TryMaskWriteHoldingRegister(int index, ushort andMask, ushort orMask, out ushort result)
    {
        result = 0;
        ModbusRegisterWriteEventArgs? args = null;
        var handler = HoldingRegistersWritten;
        lock (_holdingSync)
        {
            if (index < 0 || index >= _holdingRegisters.Length)
            {
                return false;
            }

            var previous = _holdingRegisters[index];
            result = (ushort)((previous & andMask) | (orMask & ~andMask));
            _holdingRegisters[index] = result;
            if (handler is not null)
            {
                args = new ModbusRegisterWriteEventArgs(index, new[] { result }, new[] { previous });
            }
        }

        if (args is not null)
        {
            handler!.Invoke(this, args);
        }

        return true;
    }

    internal bool TryWriteSingleCoil(int index, bool value)
    {
        ModbusCoilWriteEventArgs? args = null;
        var handler = CoilsWritten;
        lock (_coilSync)
        {
            if (index < 0 || index >= _coilCount)
            {
                return false;
            }

            var previous = GetBit(_coils, index);
            SetBit(_coils, index, value);
            if (handler is not null)
            {
                args = new ModbusCoilWriteEventArgs(index, new[] { value }, new[] { previous });
            }
        }

        if (args is not null)
        {
            handler!.Invoke(this, args);
        }

        return true;
    }

    internal bool TryWriteSingleRegister(int index, ushort value)
    {
        ModbusRegisterWriteEventArgs? args = null;
        var handler = HoldingRegistersWritten;
        lock (_holdingSync)
        {
            if (index < 0 || index >= _holdingRegisters.Length)
            {
                return false;
            }

            var previous = _holdingRegisters[index];
            _holdingRegisters[index] = value;
            if (handler is not null)
            {
                args = new ModbusRegisterWriteEventArgs(index, new[] { value }, new[] { previous });
            }
        }

        if (args is not null)
        {
            handler!.Invoke(this, args);
        }

        return true;
    }

    private bool WriteRegisters(
        int start,
        IReadOnlyList<ushort> values,
        ushort[] buffer,
        object sync,
        EventHandler<ModbusRegisterWriteEventArgs>? handler)
    {
        Guard.NotNull(values, nameof(values));

        if (values is ushort[] array)
        {
            return WriteRegisters(start, array.AsSpan(), buffer, sync, handler);
        }

        var scratch = new ushort[values.Count];
        for (var i = 0; i < scratch.Length; i++)
        {
            scratch[i] = values[i];
        }

        return WriteRegisters(start, scratch.AsSpan(), buffer, sync, handler);
    }

    internal bool TryWriteHoldingRegisters(int start, ReadOnlySpan<ushort> values) =>
        WriteRegisters(start, values, _holdingRegisters, _holdingSync, HoldingRegistersWritten);

    private bool WriteRegisters(
        int start,
        ReadOnlySpan<ushort> values,
        ushort[] buffer,
        object sync,
        EventHandler<ModbusRegisterWriteEventArgs>? handler)
    {
        ModbusRegisterWriteEventArgs? args = null;
        lock (sync)
        {
            if (!Fits(start, values.Length, buffer.Length))
            {
                return false;
            }

            if (values.Length > 0)
            {
                if (handler is null)
                {
                    values.CopyTo(buffer.AsSpan(start, values.Length));
                }
                else
                {
                    var written = values.ToArray();
                    var previous = new ushort[written.Length];
                    Array.Copy(buffer, start, previous, 0, written.Length);
                    written.CopyTo(buffer, start);
                    args = new ModbusRegisterWriteEventArgs(start, written, previous);
                }
            }
        }

        if (args is not null)
        {
            handler!.Invoke(this, args);
        }

        return true;
    }

    internal bool TryCopyHoldingRegisters(int start, int count, Span<byte> destination) =>
        TryCopyRegisters(_holdingRegisters, start, count, destination, _holdingSync);

    internal bool TryCopyInputRegisters(int start, int count, Span<byte> destination) =>
        TryCopyRegisters(_inputRegisters, start, count, destination, _inputSync);

    private static bool TryCopyRegisters(
        ushort[] buffer,
        int start,
        int count,
        Span<byte> destination,
        object sync)
    {
        lock (sync)
        {
            if (!Fits(start, count, buffer.Length) || destination.Length < count * 2)
            {
                return false;
            }

            for (var i = 0; i < count; i++)
            {
                var value = buffer[start + i];
                destination[i * 2] = (byte)(value >> 8);
                destination[(i * 2) + 1] = (byte)value;
            }

            return true;
        }
    }

    private static ushort[]? ReadRegisters(int start, int count, ushort[] buffer, object sync)
    {
        lock (sync)
        {
            if (!Fits(start, count, buffer.Length))
            {
                return null;
            }

            var values = new ushort[count];
            Array.Copy(buffer, start, values, 0, count);
            return values;
        }
    }

    private bool WriteCoils(
        int start,
        IReadOnlyList<bool> values,
        uint[] bits,
        int count,
        object sync,
        EventHandler<ModbusCoilWriteEventArgs>? handler)
    {
        Guard.NotNull(values, nameof(values));

        if (values is bool[] array)
        {
            return WriteCoils(start, array.AsSpan(), bits, count, sync, handler);
        }

        var scratch = new bool[values.Count];
        for (var i = 0; i < scratch.Length; i++)
        {
            scratch[i] = values[i];
        }

        return WriteCoils(start, scratch.AsSpan(), bits, count, sync, handler);
    }

    internal bool TryWriteCoils(int start, ReadOnlySpan<byte> packed, int count) =>
        WriteCoils(start, packed, count, _coils, _coilCount, _coilSync, CoilsWritten);

    private bool WriteCoils(
        int start,
        ReadOnlySpan<byte> packed,
        int count,
        uint[] bits,
        int length,
        object sync,
        EventHandler<ModbusCoilWriteEventArgs>? handler)
    {
        ModbusCoilWriteEventArgs? args = null;
        lock (sync)
        {
            if (!Fits(start, count, length))
            {
                return false;
            }

            if (count > 0)
            {
                if (handler is null)
                {
                    for (var i = 0; i < count; i++)
                    {
                        SetBit(bits, start + i, (packed[i / 8] & (1 << (i % 8))) != 0);
                    }
                }
                else
                {
                    var written = new bool[count];
                    var previous = new bool[count];
                    for (var i = 0; i < count; i++)
                    {
                        written[i] = (packed[i / 8] & (1 << (i % 8))) != 0;
                        previous[i] = GetBit(bits, start + i);
                        SetBit(bits, start + i, written[i]);
                    }

                    args = new ModbusCoilWriteEventArgs(start, written, previous);
                }
            }
        }

        if (args is not null)
        {
            handler!.Invoke(this, args);
        }

        return true;
    }

    private bool WriteCoils(
        int start,
        ReadOnlySpan<bool> values,
        uint[] bits,
        int count,
        object sync,
        EventHandler<ModbusCoilWriteEventArgs>? handler)
    {
        ModbusCoilWriteEventArgs? args = null;
        lock (sync)
        {
            if (!Fits(start, values.Length, count))
            {
                return false;
            }

            if (values.Length > 0)
            {
                if (handler is null)
                {
                    for (var i = 0; i < values.Length; i++)
                    {
                        SetBit(bits, start + i, values[i]);
                    }
                }
                else
                {
                    var written = values.ToArray();
                    var previous = new bool[written.Length];
                    for (var i = 0; i < written.Length; i++)
                    {
                        previous[i] = GetBit(bits, start + i);
                        SetBit(bits, start + i, written[i]);
                    }

                    args = new ModbusCoilWriteEventArgs(start, written, previous);
                }
            }
        }

        if (args is not null)
        {
            handler!.Invoke(this, args);
        }

        return true;
    }

    internal bool TryCopyCoils(int start, int count, Span<byte> destination) =>
        TryCopyBits(_coils, start, count, _coilCount, destination, _coilSync);

    internal bool TryCopyDiscreteInputs(int start, int count, Span<byte> destination) =>
        TryCopyBits(_discreteInputs, start, count, _discreteInputCount, destination, _discreteSync);

    private static bool TryCopyBits(
        uint[] bits,
        int start,
        int count,
        int length,
        Span<byte> destination,
        object sync)
    {
        lock (sync)
        {
            var byteCount = (count + 7) / 8;
            if (!Fits(start, count, length) || destination.Length < byteCount)
            {
                return false;
            }

            destination.Slice(0, byteCount).Clear();
            for (var i = 0; i < count; i++)
            {
                if (GetBit(bits, start + i))
                {
                    destination[i / 8] |= (byte)(1 << (i % 8));
                }
            }

            return true;
        }
    }

    private static bool[]? ReadBits(int start, int count, uint[] bits, int length, object sync)
    {
        lock (sync)
        {
            if (!Fits(start, count, length))
            {
                return null;
            }

            return ReadBitsUnlocked(start, count, bits);
        }
    }

    private static bool[] ReadBitsUnlocked(int start, int count, uint[] bits)
    {
        var values = new bool[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = GetBit(bits, start + i);
        }

        return values;
    }

    private static bool GetBit(uint[] bits, int index) => (bits[index >> 5] & (1u << (index & 31))) != 0;

    private static void SetBit(uint[] bits, int index, bool value)
    {
        var mask = 1u << (index & 31);
        if (value)
        {
            bits[index >> 5] |= mask;
        }
        else
        {
            bits[index >> 5] &= ~mask;
        }
    }

    private static bool Fits(int start, int count, int length) =>
        start >= 0 && count >= 0 && (long)start + count <= length;

    private static int BitBufferLength(int count) => (count + 31) / 32;

    private static int ValidateSize(int size, string name) => ServerBufferSizes.Validate(size, name);
}
