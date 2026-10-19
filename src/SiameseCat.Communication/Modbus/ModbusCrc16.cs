namespace SiameseCat.Communication.Modbus;

/// <summary>Modbus CRC-16 (polynomial 0xA001, initial value 0xFFFF).</summary>
public static class ModbusCrc16
{
    private static readonly ushort[] Table = CreateTable();

    /// <summary>Computes the CRC over <paramref name="data"/>.</summary>
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        int crc = 0xFFFF;
        foreach (byte value in data)
        {
            crc = (ushort)((crc >> 8) ^ Table[(crc ^ value) & 0xFF]);
        }

        return (ushort)crc;
    }

    /// <summary>Writes the CRC in Modbus wire order (low byte first) to <paramref name="destination"/>.</summary>
    public static void Write(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        if (destination.Length < 2)
        {
            throw new ArgumentException("Destination must have room for two bytes.", nameof(destination));
        }

        ushort crc = Compute(data);
        destination[0] = (byte)crc;
        destination[1] = (byte)(crc >> 8);
    }

    /// <summary>Validates a complete RTU frame whose last two bytes are the CRC.</summary>
    public static bool Validate(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 4)
        {
            return false;
        }

        ushort crc = Compute(frame[..^2]);
        return frame[^2] == (byte)crc && frame[^1] == (byte)(crc >> 8);
    }

    private static ushort[] CreateTable()
    {
        ushort[] table = new ushort[256];
        for (int i = 0; i < table.Length; i++)
        {
            ushort value = (ushort)i;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0
                    ? (ushort)((value >> 1) ^ 0xA001)
                    : (ushort)(value >> 1);
            }

            table[i] = value;
        }

        return table;
    }
}
