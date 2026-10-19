using SiameseCat.Core;

namespace SiameseCat.Server;

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
        lock (_holdingSync)
        {
            if (index < 0 || index >= _holdingRegisters.Length)
            {
                return false;
            }

            var previous = _holdingRegisters[index];
            result = (ushort)((previous & andMask) | (orMask & ~andMask));
            _holdingRegisters[index] = result;
            args = new ModbusRegisterWriteEventArgs(index, new[] { result }, new[] { previous });
        }

        HoldingRegistersWritten?.Invoke(this, args);
        return true;
    }

    internal bool TryWriteSingleCoil(int index, bool value)
    {
        ModbusCoilWriteEventArgs? args = null;
        lock (_coilSync)
        {
            if (index < 0 || index >= _coilCount)
            {
                return false;
            }

            var previous = GetBit(_coils, index);
            SetBit(_coils, index, value);
            args = new ModbusCoilWriteEventArgs(index, new[] { value }, new[] { previous });
        }

        CoilsWritten?.Invoke(this, args);
        return true;
    }

    internal bool TryWriteSingleRegister(int index, ushort value)
    {
        ModbusRegisterWriteEventArgs? args = null;
        lock (_holdingSync)
        {
            if (index < 0 || index >= _holdingRegisters.Length)
            {
                return false;
            }

            var previous = _holdingRegisters[index];
            _holdingRegisters[index] = value;
            args = new ModbusRegisterWriteEventArgs(index, new[] { value }, new[] { previous });
        }

        HoldingRegistersWritten?.Invoke(this, args);
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

        ModbusRegisterWriteEventArgs? args = null;
        lock (sync)
        {
            if (!Fits(start, values.Count, buffer.Length))
            {
                return false;
            }

            if (values.Count > 0)
            {
                var written = new ushort[values.Count];
                var previous = new ushort[values.Count];
                Array.Copy(buffer, start, previous, 0, written.Length);
                for (var i = 0; i < written.Length; i++)
                {
                    written[i] = values[i];
                    buffer[start + i] = written[i];
                }

                args = new ModbusRegisterWriteEventArgs(start, written, previous);
            }
        }

        if (args is not null)
        {
            handler?.Invoke(this, args);
        }

        return true;
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

        ModbusCoilWriteEventArgs? args = null;
        lock (sync)
        {
            if (!Fits(start, values.Count, count))
            {
                return false;
            }

            if (values.Count > 0)
            {
                var written = new bool[values.Count];
                var previous = new bool[values.Count];
                for (var i = 0; i < written.Length; i++)
                {
                    written[i] = values[i];
                    previous[i] = GetBit(bits, start + i);
                    SetBit(bits, start + i, written[i]);
                }

                args = new ModbusCoilWriteEventArgs(start, written, previous);
            }
        }

        if (args is not null)
        {
            handler?.Invoke(this, args);
        }

        return true;
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

    private static int ValidateSize(int size, string name) => ModbusBufferSizes.Validate(size, name);
}
