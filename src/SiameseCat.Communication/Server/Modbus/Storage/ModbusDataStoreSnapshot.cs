namespace SiameseCat.Communication.Server.Modbus;

/// <summary>
/// Immutable copy of every <see cref="ModbusDataStore"/> area.
/// Take one with <see cref="ModbusDataStore.Snapshot"/> and restore it with <see cref="ModbusDataStore.Load"/>.
/// </summary>
public sealed class ModbusDataStoreSnapshot
{
    internal ModbusDataStoreSnapshot(
        ushort[] holdingRegisters,
        ushort[] inputRegisters,
        bool[] coils,
        bool[] discreteInputs)
    {
        HoldingRegisters = holdingRegisters;
        InputRegisters = inputRegisters;
        Coils = coils;
        DiscreteInputs = discreteInputs;
    }

    /// <summary>Gets the holding register values (FC03/FC06/FC16/FC23).</summary>
    public IReadOnlyList<ushort> HoldingRegisters { get; }

    /// <summary>Gets the input register values (FC04).</summary>
    public IReadOnlyList<ushort> InputRegisters { get; }

    /// <summary>Gets the coil values (FC01/FC05/FC15).</summary>
    public IReadOnlyList<bool> Coils { get; }

    /// <summary>Gets the discrete input values (FC02).</summary>
    public IReadOnlyList<bool> DiscreteInputs { get; }
}
