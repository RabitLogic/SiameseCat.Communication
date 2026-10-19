using SiameseCat.Communication;

namespace SiameseCat.Communication.Modbus;

public abstract partial class ModbusClientBase
{
    /// <summary>Reads coils (function code 01) from <see cref="DefaultUnitId"/>.</summary>
    /// <param name="startAddress">Zero-based coil address.</param>
    /// <param name="quantity">Number of coils to read, 1..2000.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One flag per coil.</returns>
    public Task<bool[]> ReadCoilsAsync(
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default) =>
        ReadCoilsAsync(DefaultUnitId, startAddress, quantity, cancellationToken);

    /// <summary>Reads coils (function code 01) from a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="startAddress">Zero-based coil address.</param>
    /// <param name="quantity">Number of coils to read, 1..2000.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One flag per coil.</returns>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task<bool[]> ReadCoilsAsync(
        byte unitId,
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default)
    {
        ValidateCount(quantity, 2000, nameof(quantity));
        byte[] pdu = BuildReadRequest(ModbusFunctionCode.ReadCoils, startAddress, quantity);
        byte[] response = await ExecutePduAsync(unitId, pdu, isWrite: false, cancellationToken).ConfigureAwait(false);
        return ParseBitResponse(response, ModbusFunctionCode.ReadCoils, quantity);
    }

    /// <summary>Reads discrete inputs (function code 02) from <see cref="DefaultUnitId"/>.</summary>
    /// <param name="startAddress">Zero-based input address.</param>
    /// <param name="quantity">Number of inputs to read, 1..2000.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One flag per discrete input.</returns>
    public Task<bool[]> ReadDiscreteInputsAsync(
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default) =>
        ReadDiscreteInputsAsync(DefaultUnitId, startAddress, quantity, cancellationToken);

    /// <summary>Reads discrete inputs (function code 02) from a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="startAddress">Zero-based input address.</param>
    /// <param name="quantity">Number of inputs to read, 1..2000.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One flag per discrete input.</returns>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task<bool[]> ReadDiscreteInputsAsync(
        byte unitId,
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default)
    {
        ValidateCount(quantity, 2000, nameof(quantity));
        byte[] pdu = BuildReadRequest(ModbusFunctionCode.ReadDiscreteInputs, startAddress, quantity);
        byte[] response = await ExecutePduAsync(unitId, pdu, isWrite: false, cancellationToken).ConfigureAwait(false);
        return ParseBitResponse(response, ModbusFunctionCode.ReadDiscreteInputs, quantity);
    }

    /// <summary>Reads holding registers (function code 03) from <see cref="DefaultUnitId"/>.</summary>
    /// <param name="startAddress">Zero-based register address.</param>
    /// <param name="quantity">Number of registers to read, 1..125.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One 16-bit register per element.</returns>
    public Task<ushort[]> ReadHoldingRegistersAsync(
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default) =>
        ReadHoldingRegistersAsync(DefaultUnitId, startAddress, quantity, cancellationToken);

    /// <summary>Reads holding registers (function code 03) from a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="startAddress">Zero-based register address.</param>
    /// <param name="quantity">Number of registers to read, 1..125.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One 16-bit register per element.</returns>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task<ushort[]> ReadHoldingRegistersAsync(
        byte unitId,
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default)
    {
        ValidateCount(quantity, 125, nameof(quantity));
        byte[] pdu = BuildReadRequest(ModbusFunctionCode.ReadHoldingRegisters, startAddress, quantity);
        byte[] response = await ExecutePduAsync(unitId, pdu, isWrite: false, cancellationToken).ConfigureAwait(false);
        return ParseRegisterResponse(response, ModbusFunctionCode.ReadHoldingRegisters, quantity);
    }

    /// <summary>Reads input registers (function code 04) from <see cref="DefaultUnitId"/>.</summary>
    /// <param name="startAddress">Zero-based register address.</param>
    /// <param name="quantity">Number of registers to read, 1..125.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One 16-bit register per element.</returns>
    public Task<ushort[]> ReadInputRegistersAsync(
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default) =>
        ReadInputRegistersAsync(DefaultUnitId, startAddress, quantity, cancellationToken);

    /// <summary>Reads input registers (function code 04) from a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="startAddress">Zero-based register address.</param>
    /// <param name="quantity">Number of registers to read, 1..125.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One 16-bit register per element.</returns>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task<ushort[]> ReadInputRegistersAsync(
        byte unitId,
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default)
    {
        ValidateCount(quantity, 125, nameof(quantity));
        byte[] pdu = BuildReadRequest(ModbusFunctionCode.ReadInputRegisters, startAddress, quantity);
        byte[] response = await ExecutePduAsync(unitId, pdu, isWrite: false, cancellationToken).ConfigureAwait(false);
        return ParseRegisterResponse(response, ModbusFunctionCode.ReadInputRegisters, quantity);
    }
}
