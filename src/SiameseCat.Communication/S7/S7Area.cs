namespace SiameseCat.Communication.S7;

/// <summary>S7 memory areas.</summary>
public enum S7Area : byte
{
    /// <summary>Process image of the inputs (I area).</summary>
    Inputs = 0x81,

    /// <summary>Process image of the outputs (Q area).</summary>
    Outputs = 0x82,

    /// <summary>Bit memory (M area).</summary>
    Merkers = 0x83,

    /// <summary>Data block.</summary>
    DataBlock = 0x84,

    /// <summary>Timer area.</summary>
    Timer = 0x1C,

    /// <summary>Counter area.</summary>
    Counter = 0x1D,
}

/// <summary>Data type described by an <see cref="S7Address"/>.</summary>
public enum S7DataType
{
    /// <summary>Single bit.</summary>
    Bool,

    /// <summary>One byte.</summary>
    Byte,

    /// <summary>16 bits (two bytes).</summary>
    Word,

    /// <summary>32 bits (four bytes).</summary>
    DWord,
}
