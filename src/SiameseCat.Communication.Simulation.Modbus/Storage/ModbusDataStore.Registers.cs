using SiameseCat.Communication;

namespace SiameseCat.Communication.Simulation.Modbus;

public sealed partial class ModbusDataStore
{

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

}
