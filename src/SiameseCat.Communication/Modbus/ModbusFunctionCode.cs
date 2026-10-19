namespace SiameseCat.Communication.Modbus;

/// <summary>Modbus function codes supported by the client.</summary>
public enum ModbusFunctionCode : byte
{
    /// <summary>Read coils (0x01).</summary>
    ReadCoils = 0x01,

    /// <summary>Read discrete inputs (0x02).</summary>
    ReadDiscreteInputs = 0x02,

    /// <summary>Read holding registers (0x03).</summary>
    ReadHoldingRegisters = 0x03,

    /// <summary>Read input registers (0x04).</summary>
    ReadInputRegisters = 0x04,

    /// <summary>Write a single coil (0x05).</summary>
    WriteSingleCoil = 0x05,

    /// <summary>Write a single register (0x06).</summary>
    WriteSingleRegister = 0x06,

    /// <summary>Write multiple coils (0x0F).</summary>
    WriteMultipleCoils = 0x0F,

    /// <summary>Write multiple registers (0x10).</summary>
    WriteMultipleRegisters = 0x10,

    /// <summary>Read and write multiple registers in one transaction (0x17).</summary>
    ReadWriteMultipleRegisters = 0x17,
}
