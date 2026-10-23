namespace SiameseCat.Communication.Simulation.Modbus;

/// <summary>Describes a write of one or more holding or input registers.</summary>
public sealed class ModbusRegisterWriteEventArgs : EventArgs
{
    /// <summary>Creates the event arguments.</summary>
    /// <param name="start">First register offset.</param>
    /// <param name="values">Values that were written.</param>
    /// <param name="previousValues">Values that were stored before the write.</param>
    public ModbusRegisterWriteEventArgs(int start, ushort[] values, ushort[] previousValues)
    {
        Start = start;
        Values = values ?? throw new ArgumentNullException(nameof(values));
        PreviousValues = previousValues ?? throw new ArgumentNullException(nameof(previousValues));
    }

    /// <summary>Gets the first register offset.</summary>
    public int Start { get; }

    /// <summary>Gets the number of registers that were written.</summary>
    public int Count => Values.Count;

    /// <summary>Gets the values that were written.</summary>
    public IReadOnlyList<ushort> Values { get; }

    /// <summary>Gets the values that were stored before the write.</summary>
    public IReadOnlyList<ushort> PreviousValues { get; }
}
