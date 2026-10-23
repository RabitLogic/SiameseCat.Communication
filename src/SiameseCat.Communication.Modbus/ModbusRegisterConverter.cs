using System.Buffers.Binary;
using System.Text;

using SiameseCat.Communication;

namespace SiameseCat.Communication.Modbus;

/// <summary>Converts between Modbus 16-bit registers and common device data types.</summary>
/// <remarks>
/// <para>Modbus registers are transmitted big-endian, so the high byte comes first inside a register.</para>
/// <para>
/// Multi-register values can be stored high-word-first (most devices) or low-word-first; see
/// <see cref="ModbusWordOrder"/>. Strings can additionally swap the two bytes inside each register.
/// </para>
/// <para>All methods validate the requested range and throw <see cref="ArgumentOutOfRangeException"/> when it does not fit.</para>
/// </remarks>
public static class ModbusRegisterConverter
{
    /// <summary>Reads an unsigned 16-bit value.</summary>
    /// <param name="registers">Register source.</param>
    /// <param name="index">Register index.</param>
    public static ushort ReadUInt16(ReadOnlySpan<ushort> registers, int index)
    {
        ValidateRange(registers, index, 1);
        return registers[index];
    }

    /// <summary>Reads a signed 16-bit value.</summary>
    /// <param name="registers">Register source.</param>
    /// <param name="index">Register index.</param>
    public static short ReadInt16(ReadOnlySpan<ushort> registers, int index) =>
        unchecked((short)ReadUInt16(registers, index));

    /// <summary>Reads an unsigned 32-bit value from two registers.</summary>
    /// <param name="registers">Register source.</param>
    /// <param name="index">Index of the first register.</param>
    /// <param name="wordOrder">Order of the two 16-bit words.</param>
    public static uint ReadUInt32(
        ReadOnlySpan<ushort> registers,
        int index,
        ModbusWordOrder wordOrder = ModbusWordOrder.HighWordFirst)
    {
        ValidateRange(registers, index, 2);
        ushort high = wordOrder == ModbusWordOrder.HighWordFirst ? registers[index] : registers[index + 1];
        ushort low = wordOrder == ModbusWordOrder.HighWordFirst ? registers[index + 1] : registers[index];
        return ((uint)high << 16) | low;
    }

    /// <summary>Reads a signed 32-bit value from two registers.</summary>
    /// <param name="registers">Register source.</param>
    /// <param name="index">Index of the first register.</param>
    /// <param name="wordOrder">Order of the two 16-bit words.</param>
    public static int ReadInt32(
        ReadOnlySpan<ushort> registers,
        int index,
        ModbusWordOrder wordOrder = ModbusWordOrder.HighWordFirst) =>
        unchecked((int)ReadUInt32(registers, index, wordOrder));

    /// <summary>Reads an unsigned 64-bit value from four registers.</summary>
    /// <param name="registers">Register source.</param>
    /// <param name="index">Index of the first register.</param>
    /// <param name="wordOrder">Order of the four 16-bit words.</param>
    public static ulong ReadUInt64(
        ReadOnlySpan<ushort> registers,
        int index,
        ModbusWordOrder wordOrder = ModbusWordOrder.HighWordFirst)
    {
        ValidateRange(registers, index, 4);
        ulong raw = 0;

        for (var i = 0; i < 4; i++)
        {
            var wordIndex = wordOrder == ModbusWordOrder.HighWordFirst ? index + i : index + 3 - i;
            raw = (raw << 16) | registers[wordIndex];
        }

        return raw;
    }

    /// <summary>Reads a signed 64-bit value from four registers.</summary>
    /// <param name="registers">Register source.</param>
    /// <param name="index">Index of the first register.</param>
    /// <param name="wordOrder">Order of the four 16-bit words.</param>
    public static long ReadInt64(
        ReadOnlySpan<ushort> registers,
        int index,
        ModbusWordOrder wordOrder = ModbusWordOrder.HighWordFirst) =>
        unchecked((long)ReadUInt64(registers, index, wordOrder));

    /// <summary>Reads an IEEE 754 single-precision value from two registers.</summary>
    /// <param name="registers">Register source.</param>
    /// <param name="index">Index of the first register.</param>
    /// <param name="wordOrder">Order of the two 16-bit words.</param>
    public static float ReadSingle(
        ReadOnlySpan<ushort> registers,
        int index,
        ModbusWordOrder wordOrder = ModbusWordOrder.HighWordFirst) =>
        BitConverter.Int32BitsToSingle(ReadInt32(registers, index, wordOrder));

    /// <summary>Reads an IEEE 754 double-precision value from four registers.</summary>
    /// <param name="registers">Register source.</param>
    /// <param name="index">Index of the first register.</param>
    /// <param name="wordOrder">Order of the four 16-bit words.</param>
    public static double ReadDouble(
        ReadOnlySpan<ushort> registers,
        int index,
        ModbusWordOrder wordOrder = ModbusWordOrder.HighWordFirst) =>
        BitConverter.Int64BitsToDouble(ReadInt64(registers, index, wordOrder));

    /// <summary>Reads raw bytes stored across consecutive registers.</summary>
    /// <param name="registers">Register source.</param>
    /// <param name="index">Index of the first register.</param>
    /// <param name="registerCount">Number of registers to convert.</param>
    /// <param name="swapBytes">Swaps the two bytes inside every register.</param>
    public static byte[] ReadBytes(
        ReadOnlySpan<ushort> registers,
        int index,
        int registerCount,
        bool swapBytes = false)
    {
        ValidateRange(registers, index, registerCount);

        var bytes = new byte[registerCount * 2];
        for (var i = 0; i < registerCount; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i * 2), registers[index + i]);
        }

        if (swapBytes)
        {
            SwapBytes(bytes);
        }

        return bytes;
    }

    /// <summary>Reads a string stored in consecutive registers, one character byte per register byte.</summary>
    /// <param name="registers">Register source.</param>
    /// <param name="index">Index of the first register.</param>
    /// <param name="registerCount">Number of registers that belong to the string.</param>
    /// <param name="encoding">Text encoding. Defaults to ASCII.</param>
    /// <param name="swapBytes">Swaps the two bytes inside every register.</param>
    /// <param name="trimAtNull">Stops at the first NUL character, as many devices pad strings with zero bytes.</param>
    /// <remarks>
    /// Trimming happens before decoding, so a multi-byte encoding is never cut in the middle of a character.
    /// </remarks>
    public static string ReadString(
        ReadOnlySpan<ushort> registers,
        int index,
        int registerCount,
        Encoding? encoding = null,
        bool swapBytes = false,
        bool trimAtNull = true)
    {
        ValidateRange(registers, index, registerCount);

        var bytes = ReadBytes(registers, index, registerCount, swapBytes);
        if (trimAtNull)
        {
            var nullIndex = Array.IndexOf(bytes, (byte)0);
            if (nullIndex >= 0)
            {
                var trimmed = new byte[nullIndex];
                Array.Copy(bytes, trimmed, nullIndex);
                bytes = trimmed;
            }
        }

        return (encoding ?? Encoding.ASCII).GetString(bytes);
    }

    /// <summary>Writes an unsigned 16-bit value.</summary>
    /// <param name="registers">Register destination.</param>
    /// <param name="index">Register index.</param>
    /// <param name="value">Value to write.</param>
    public static void WriteUInt16(Span<ushort> registers, int index, ushort value)
    {
        ValidateRange(registers, index, 1);
        registers[index] = value;
    }

    /// <summary>Writes a signed 16-bit value.</summary>
    /// <param name="registers">Register destination.</param>
    /// <param name="index">Register index.</param>
    /// <param name="value">Value to write.</param>
    public static void WriteInt16(Span<ushort> registers, int index, short value) =>
        WriteUInt16(registers, index, unchecked((ushort)value));

    /// <summary>Writes an unsigned 32-bit value into two registers.</summary>
    /// <param name="registers">Register destination.</param>
    /// <param name="index">Index of the first register.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="wordOrder">Order of the two 16-bit words.</param>
    public static void WriteUInt32(
        Span<ushort> registers,
        int index,
        uint value,
        ModbusWordOrder wordOrder = ModbusWordOrder.HighWordFirst)
    {
        ValidateRange(registers, index, 2);
        var high = (ushort)(value >> 16);
        var low = (ushort)value;

        if (wordOrder == ModbusWordOrder.HighWordFirst)
        {
            registers[index] = high;
            registers[index + 1] = low;
        }
        else
        {
            registers[index] = low;
            registers[index + 1] = high;
        }
    }

    /// <summary>Writes a signed 32-bit value into two registers.</summary>
    /// <param name="registers">Register destination.</param>
    /// <param name="index">Index of the first register.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="wordOrder">Order of the two 16-bit words.</param>
    public static void WriteInt32(
        Span<ushort> registers,
        int index,
        int value,
        ModbusWordOrder wordOrder = ModbusWordOrder.HighWordFirst) =>
        WriteUInt32(registers, index, unchecked((uint)value), wordOrder);

    /// <summary>Writes an unsigned 64-bit value into four registers.</summary>
    /// <param name="registers">Register destination.</param>
    /// <param name="index">Index of the first register.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="wordOrder">Order of the four 16-bit words.</param>
    public static void WriteUInt64(
        Span<ushort> registers,
        int index,
        ulong value,
        ModbusWordOrder wordOrder = ModbusWordOrder.HighWordFirst)
    {
        ValidateRange(registers, index, 4);

        for (var i = 0; i < 4; i++)
        {
            var word = (ushort)(value >> (48 - i * 16));
            registers[wordOrder == ModbusWordOrder.HighWordFirst ? index + i : index + 3 - i] = word;
        }
    }

    /// <summary>Writes a signed 64-bit value into four registers.</summary>
    /// <param name="registers">Register destination.</param>
    /// <param name="index">Index of the first register.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="wordOrder">Order of the four 16-bit words.</param>
    public static void WriteInt64(
        Span<ushort> registers,
        int index,
        long value,
        ModbusWordOrder wordOrder = ModbusWordOrder.HighWordFirst) =>
        WriteUInt64(registers, index, unchecked((ulong)value), wordOrder);

    /// <summary>Writes an IEEE 754 single-precision value into two registers.</summary>
    /// <param name="registers">Register destination.</param>
    /// <param name="index">Index of the first register.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="wordOrder">Order of the two 16-bit words.</param>
    public static void WriteSingle(
        Span<ushort> registers,
        int index,
        float value,
        ModbusWordOrder wordOrder = ModbusWordOrder.HighWordFirst) =>
        WriteInt32(registers, index, BitConverter.SingleToInt32Bits(value), wordOrder);

    /// <summary>Writes an IEEE 754 double-precision value into four registers.</summary>
    /// <param name="registers">Register destination.</param>
    /// <param name="index">Index of the first register.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="wordOrder">Order of the four 16-bit words.</param>
    public static void WriteDouble(
        Span<ushort> registers,
        int index,
        double value,
        ModbusWordOrder wordOrder = ModbusWordOrder.HighWordFirst) =>
        WriteInt64(registers, index, BitConverter.DoubleToInt64Bits(value), wordOrder);

    /// <summary>Writes raw bytes into consecutive registers, padding with zero bytes.</summary>
    /// <param name="registers">Register destination.</param>
    /// <param name="index">Index of the first register.</param>
    /// <param name="registerCount">Number of registers available for the payload.</param>
    /// <param name="data">Bytes to write.</param>
    /// <param name="swapBytes">Swaps the two bytes inside every register.</param>
    /// <exception cref="ArgumentException">The payload does not fit in <paramref name="registerCount"/> registers.</exception>
    public static void WriteBytes(
        Span<ushort> registers,
        int index,
        int registerCount,
        ReadOnlySpan<byte> data,
        bool swapBytes = false)
    {
        ValidateRange(registers, index, registerCount);

        var capacity = registerCount * 2;
        if (data.Length > capacity)
        {
            throw new ArgumentException(
                $"The payload is {data.Length} bytes but only {capacity} bytes are available.",
                nameof(data));
        }

        Span<byte> buffer = capacity <= 256 ? stackalloc byte[capacity] : new byte[capacity];
        buffer.Clear();
        data.CopyTo(buffer);

        if (swapBytes)
        {
            SwapBytes(buffer);
        }

        for (var i = 0; i < registerCount; i++)
        {
            registers[index + i] = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(i * 2, 2));
        }
    }

    /// <summary>Writes a string into consecutive registers, padding with zero bytes.</summary>
    /// <param name="registers">Register destination.</param>
    /// <param name="index">Index of the first register.</param>
    /// <param name="registerCount">Number of registers available for the text.</param>
    /// <param name="value">Text to write.</param>
    /// <param name="encoding">Text encoding. Defaults to ASCII.</param>
    /// <param name="swapBytes">Swaps the two bytes inside every register.</param>
    /// <exception cref="ArgumentException">The encoded text does not fit in <paramref name="registerCount"/> registers.</exception>
    public static void WriteString(
        Span<ushort> registers,
        int index,
        int registerCount,
        string value,
        Encoding? encoding = null,
        bool swapBytes = false)
    {
        Guard.NotNull(value, nameof(value));
        ValidateRange(registers, index, registerCount);

        var capacity = registerCount * 2;
        var encoded = (encoding ?? Encoding.ASCII).GetBytes(value);
        if (encoded.Length > capacity)
        {
            throw new ArgumentException(
                $"The encoded string is {encoded.Length} bytes but only {capacity} bytes are available.",
                nameof(value));
        }

        WriteBytes(registers, index, registerCount, encoded, swapBytes);
    }

    private static void SwapBytes(Span<byte> bytes)
    {
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            (bytes[i], bytes[i + 1]) = (bytes[i + 1], bytes[i]);
        }
    }

    private static void ValidateRange(ReadOnlySpan<ushort> registers, int index, int count)
    {
        if (index < 0 || count < 0 || index > registers.Length - count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                $"The requested range ({index}..{index + count - 1}) is outside the register array of length {registers.Length}.");
        }
    }
}

/// <summary>Order of the 16-bit words that make up a 32-bit or 64-bit value.</summary>
public enum ModbusWordOrder
{
    /// <summary>Most significant word first (for example Schneider, Wago and most Modbus devices).</summary>
    HighWordFirst,

    /// <summary>Least significant word first (for example some older Siemens gateways).</summary>
    LowWordFirst,
}
