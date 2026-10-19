namespace SiameseCat.Communication.Server.Modbus;

/// <summary>Describes a write of one or more coils or discrete inputs.</summary>
public sealed class ModbusCoilWriteEventArgs : EventArgs
{
    /// <summary>Creates the event arguments.</summary>
    /// <param name="start">First coil or input offset.</param>
    /// <param name="values">Values that were written.</param>
    /// <param name="previousValues">Values that were stored before the write.</param>
    public ModbusCoilWriteEventArgs(int start, bool[] values, bool[] previousValues)
    {
        Start = start;
        Values = values ?? throw new ArgumentNullException(nameof(values));
        PreviousValues = previousValues ?? throw new ArgumentNullException(nameof(previousValues));
    }

    /// <summary>Gets the first coil or input offset.</summary>
    public int Start { get; }

    /// <summary>Gets the number of bits that were written.</summary>
    public int Count => Values.Count;

    /// <summary>Gets the values that were written.</summary>
    public IReadOnlyList<bool> Values { get; }

    /// <summary>Gets the values that were stored before the write.</summary>
    public IReadOnlyList<bool> PreviousValues { get; }
}
