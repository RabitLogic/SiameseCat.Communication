using SiameseCat.Communication;

namespace SiameseCat.Communication.Server.Modbus;

public sealed partial class ModbusDataStore
{

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

}
