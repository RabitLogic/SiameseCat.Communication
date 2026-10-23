using SiameseCat.Communication;

namespace SiameseCat.Communication.Modbus;

public abstract partial class ModbusClientBase
{

    /// <summary>Writes a single coil (function code 05) to <see cref="DefaultUnitId"/>.</summary>
    /// <param name="address">Zero-based coil address.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WriteSingleCoilAsync(
        ushort address,
        bool value,
        CancellationToken cancellationToken = default) =>
        WriteSingleCoilAsync(DefaultUnitId, address, value, cancellationToken);

    /// <summary>Writes a single coil (function code 05) to a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="address">Zero-based coil address.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task WriteSingleCoilAsync(
        byte unitId,
        ushort address,
        bool value,
        CancellationToken cancellationToken = default)
    {
        byte[] pdu = new byte[]
        {
            (byte)ModbusFunctionCode.WriteSingleCoil,
            High(address),
            Low(address),
            value ? (byte)0xFF : (byte)0x00,
            0x00,
        };

        await ExecuteWriteAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a single holding register (function code 06) to <see cref="DefaultUnitId"/>.</summary>
    /// <param name="address">Zero-based register address.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WriteSingleRegisterAsync(
        ushort address,
        ushort value,
        CancellationToken cancellationToken = default) =>
        WriteSingleRegisterAsync(DefaultUnitId, address, value, cancellationToken);

    /// <summary>Writes a single holding register (function code 06) to a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="address">Zero-based register address.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task WriteSingleRegisterAsync(
        byte unitId,
        ushort address,
        ushort value,
        CancellationToken cancellationToken = default)
    {
        byte[] pdu = new byte[]
        {
            (byte)ModbusFunctionCode.WriteSingleRegister,
            High(address),
            Low(address),
            High(value),
            Low(value),
        };

        await ExecuteWriteAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes multiple coils (function code 0F) to <see cref="DefaultUnitId"/>.</summary>
    /// <param name="startAddress">Zero-based address of the first coil.</param>
    /// <param name="values">Values to write, 1..1968 items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WriteMultipleCoilsAsync(
        ushort startAddress,
        IReadOnlyList<bool> values,
        CancellationToken cancellationToken = default) =>
        WriteMultipleCoilsAsync(DefaultUnitId, startAddress, values, cancellationToken);

    /// <summary>Writes multiple coils (function code 0F) to a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="startAddress">Zero-based address of the first coil.</param>
    /// <param name="values">Values to write, 1..1968 items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task WriteMultipleCoilsAsync(
        byte unitId,
        ushort startAddress,
        IReadOnlyList<bool> values,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(values, nameof(values));
        ValidateCount(values.Count, 1968, nameof(values));

        int byteCount = (values.Count + 7) / 8;
        byte[] pdu = new byte[6 + byteCount];
        pdu[0] = (byte)ModbusFunctionCode.WriteMultipleCoils;
        pdu[1] = High(startAddress);
        pdu[2] = Low(startAddress);
        pdu[3] = High((ushort)values.Count);
        pdu[4] = Low((ushort)values.Count);
        pdu[5] = (byte)byteCount;

        for (int i = 0; i < values.Count; i++)
        {
            if (values[i])
            {
                pdu[6 + i / 8] |= (byte)(1 << (i % 8));
            }
        }

        await ExecuteWriteAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes multiple holding registers (function code 10) to <see cref="DefaultUnitId"/>.</summary>
    /// <param name="startAddress">Zero-based address of the first register.</param>
    /// <param name="values">Registers to write, 1..123 items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WriteMultipleRegistersAsync(
        ushort startAddress,
        IReadOnlyList<ushort> values,
        CancellationToken cancellationToken = default) =>
        WriteMultipleRegistersAsync(DefaultUnitId, startAddress, values, cancellationToken);

    /// <summary>Writes multiple holding registers (function code 10) to a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="startAddress">Zero-based address of the first register.</param>
    /// <param name="values">Registers to write, 1..123 items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task WriteMultipleRegistersAsync(
        byte unitId,
        ushort startAddress,
        IReadOnlyList<ushort> values,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(values, nameof(values));
        ValidateCount(values.Count, 123, nameof(values));

        byte[] pdu = new byte[6 + values.Count * 2];
        pdu[0] = (byte)ModbusFunctionCode.WriteMultipleRegisters;
        pdu[1] = High(startAddress);
        pdu[2] = Low(startAddress);
        pdu[3] = High((ushort)values.Count);
        pdu[4] = Low((ushort)values.Count);
        pdu[5] = (byte)(values.Count * 2);

        for (int i = 0; i < values.Count; i++)
        {
            pdu[6 + i * 2] = High(values[i]);
            pdu[7 + i * 2] = Low(values[i]);
        }

        await ExecuteWriteAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads and writes registers in one transaction (function code 17) for <see cref="DefaultUnitId"/>.</summary>
    /// <param name="readStartAddress">Zero-based address of the first register to read.</param>
    /// <param name="readQuantity">Number of registers to read, 1..125.</param>
    /// <param name="writeStartAddress">Zero-based address of the first register to write.</param>
    /// <param name="writeValues">Registers to write, 1..121 items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registers read before the write is applied.</returns>
    public Task<ushort[]> ReadWriteMultipleRegistersAsync(
        ushort readStartAddress,
        ushort readQuantity,
        ushort writeStartAddress,
        IReadOnlyList<ushort> writeValues,
        CancellationToken cancellationToken = default) =>
        ReadWriteMultipleRegistersAsync(
            DefaultUnitId, readStartAddress, readQuantity, writeStartAddress, writeValues, cancellationToken);

    /// <summary>Reads and writes registers in one transaction (function code 17) for a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="readStartAddress">Zero-based address of the first register to read.</param>
    /// <param name="readQuantity">Number of registers to read, 1..125.</param>
    /// <param name="writeStartAddress">Zero-based address of the first register to write.</param>
    /// <param name="writeValues">Registers to write, 1..121 items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registers read before the write is applied.</returns>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task<ushort[]> ReadWriteMultipleRegistersAsync(
        byte unitId,
        ushort readStartAddress,
        ushort readQuantity,
        ushort writeStartAddress,
        IReadOnlyList<ushort> writeValues,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(writeValues, nameof(writeValues));
        ValidateCount(readQuantity, 125, nameof(readQuantity));
        ValidateCount(writeValues.Count, 121, nameof(writeValues));

        byte[] pdu = new byte[10 + writeValues.Count * 2];
        pdu[0] = (byte)ModbusFunctionCode.ReadWriteMultipleRegisters;
        pdu[1] = High(readStartAddress);
        pdu[2] = Low(readStartAddress);
        pdu[3] = High(readQuantity);
        pdu[4] = Low(readQuantity);
        pdu[5] = High(writeStartAddress);
        pdu[6] = Low(writeStartAddress);
        pdu[7] = High((ushort)writeValues.Count);
        pdu[8] = Low((ushort)writeValues.Count);
        pdu[9] = (byte)(writeValues.Count * 2);

        for (int i = 0; i < writeValues.Count; i++)
        {
            pdu[10 + i * 2] = High(writeValues[i]);
            pdu[11 + i * 2] = Low(writeValues[i]);
        }

        byte[] response = await ExecutePduAsync(
            unitId, pdu, isWrite: true, cancellationToken).ConfigureAwait(false);
        return ParseRegisterResponse(
            response, ModbusFunctionCode.ReadWriteMultipleRegisters, readQuantity);
    }

}
