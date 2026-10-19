using System.Globalization;
using System.Text.RegularExpressions;
using SiameseCat.Core;

namespace SiameseCat.Protocols.S7;

/// <summary>
/// A symbolic S7 address such as <c>DB1.DBX0.0</c>, <c>DB1.DBW2</c>, <c>M0.0</c> or <c>IW4</c>.
/// </summary>
public readonly partial struct S7Address : IEquatable<S7Address>
{
#if NET8_0_OR_GREATER
    [GeneratedRegex(
        @"^DB(?<db>\d+)\.(?<kind>DBX|DBB|DBW|DBD)(?<offset>\d+)(?:\.(?<bit>\d+))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DbPattern();

    [GeneratedRegex(
        @"^(?<area>M|I|Q|E|A)(?:(?<size>B|W|D)(?<sizeOffset>\d+)|(?<bitOffset>\d+)\.(?<bit>\d+))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AreaPattern();
#else
    private static readonly Regex DbRegex = new(
        @"^DB(?<db>\d+)\.(?<kind>DBX|DBB|DBW|DBD)(?<offset>\d+)(?:\.(?<bit>\d+))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AreaRegex = new(
        @"^(?<area>M|I|Q|E|A)(?:(?<size>B|W|D)(?<sizeOffset>\d+)|(?<bitOffset>\d+)\.(?<bit>\d+))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static Regex DbPattern() => DbRegex;

    private static Regex AreaPattern() => AreaRegex;
#endif

    /// <summary>Creates an address from its components.</summary>
    /// <param name="area">Memory area.</param>
    /// <param name="dbNumber">Data block number; must be 0 unless <paramref name="area"/> is <see cref="S7Area.DataBlock"/>.</param>
    /// <param name="byteOffset">Byte offset inside the area.</param>
    /// <param name="dataType">Access width.</param>
    /// <param name="bitOffset">Bit offset inside the byte, 0..7, only valid for <see cref="S7DataType.Bool"/>.</param>
    public S7Address(
        S7Area area,
        int dbNumber,
        int byteOffset,
        S7DataType dataType = S7DataType.Byte,
        byte bitOffset = 0)
    {
        if (!Enum.IsDefined(area))
        {
            throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown S7 area.");
        }

        if (dbNumber is < 0 or > 0xFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(dbNumber), dbNumber, "DB number must be between 0 and 65535.");
        }

        if (byteOffset is < 0 or > 0x1FFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset), byteOffset, "Byte offset must be between 0 and 2097151.");
        }

        if (area != S7Area.DataBlock && dbNumber != 0)
        {
            throw new ArgumentException("A DB number is only valid for the DataBlock area.", nameof(dbNumber));
        }

        if (bitOffset > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(bitOffset), bitOffset, "Bit offset must be between 0 and 7.");
        }

        if (dataType != S7DataType.Bool && bitOffset != 0)
        {
            throw new ArgumentException("A bit offset is only valid for Bool addresses.", nameof(bitOffset));
        }

        Area = area;
        DbNumber = dbNumber;
        ByteOffset = byteOffset;
        DataType = dataType;
        BitOffset = bitOffset;
    }

    /// <summary>Gets the memory area.</summary>
    public S7Area Area { get; }

    /// <summary>Gets the data block number, or 0 for non-DB areas.</summary>
    public int DbNumber { get; }

    /// <summary>Gets the byte offset inside the area.</summary>
    public int ByteOffset { get; }

    /// <summary>Gets the bit offset inside the byte.</summary>
    public byte BitOffset { get; }

    /// <summary>Gets the access width.</summary>
    public S7DataType DataType { get; }

    /// <summary>Gets the 24-bit bit address used on the wire.</summary>
    public int BitAddress => ByteOffset * 8 + BitOffset;

    /// <summary>Returns the bit address <paramref name="bits"/> bits after this one.</summary>
    /// <param name="bits">Number of bits to advance; must not be negative.</param>
    /// <exception cref="InvalidOperationException">The address is not a Bool address.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The result falls outside the S7 address space.</exception>
    public S7Address AddBits(int bits)
    {
        if (DataType != S7DataType.Bool)
        {
            throw new InvalidOperationException("Bit arithmetic requires a Bool address such as DB1.DBX0.0.");
        }

        if (bits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bits), bits, "Bits to advance cannot be negative.");
        }

        var bit = BitAddress + bits;
        return new S7Address(Area, DbNumber, bit / 8, S7DataType.Bool, (byte)(bit % 8));
    }

    /// <summary>Parses an S7 address string.</summary>
    public static S7Address Parse(string address)
    {
        Guard.NotNull(address, nameof(address));
        var text = address.Trim();
        if (text.Length == 0)
        {
            throw new FormatException("The S7 address is empty.");
        }
        var dbMatch = DbPattern().Match(text);
        if (dbMatch.Success)
        {
            var db = ParseInt(dbMatch.Groups["db"].Value, text);
            var offset = ParseInt(dbMatch.Groups["offset"].Value, text);
            var bit = dbMatch.Groups["bit"].Success
                ? (byte)ParseInt(dbMatch.Groups["bit"].Value, text)
                : (byte)0;

            var kind = dbMatch.Groups["kind"].Value.ToUpperInvariant();
            if (kind == "DBX")
            {
                return new S7Address(S7Area.DataBlock, db, offset, S7DataType.Bool, bit);
            }

            if (dbMatch.Groups["bit"].Success)
            {
                throw new FormatException($"A bit offset is only valid with DBX in '{address}'.");
            }

            var dataType = kind switch
            {
                "DBB" => S7DataType.Byte,
                "DBW" => S7DataType.Word,
                "DBD" => S7DataType.DWord,
                _ => throw new FormatException($"Unsupported S7 address '{address}'."),
            };

            return new S7Address(S7Area.DataBlock, db, offset, dataType);
        }

        var areaMatch = AreaPattern().Match(text);
        if (areaMatch.Success)
        {
            var area = areaMatch.Groups["area"].Value.ToUpperInvariant() switch
            {
                "M" => S7Area.Merkers,
                "I" or "E" => S7Area.Inputs,
                "Q" or "A" => S7Area.Outputs,
                _ => throw new FormatException($"Unsupported S7 address '{address}'."),
            };

            if (areaMatch.Groups["size"].Success)
            {
                var offset = ParseInt(areaMatch.Groups["sizeOffset"].Value, text);
                var dataType = areaMatch.Groups["size"].Value.ToUpperInvariant() switch
                {
                    "B" => S7DataType.Byte,
                    "W" => S7DataType.Word,
                    "D" => S7DataType.DWord,
                    _ => throw new FormatException($"Unsupported S7 address '{address}'."),
                };

                return new S7Address(area, 0, offset, dataType);
            }

            var byteOffset = ParseInt(areaMatch.Groups["bitOffset"].Value, text);
            var bitOffset = (byte)ParseInt(areaMatch.Groups["bit"].Value, text);
            return new S7Address(area, 0, byteOffset, S7DataType.Bool, bitOffset);
        }

        throw new FormatException(
            $"Invalid S7 address '{address}'. Examples: DB1.DBX0.0, DB1.DBW2, DB1.DBD4, M0.0, MW2, I0.0, QW4.");
    }

    /// <summary>Attempts to parse an S7 address without throwing.</summary>
    public static bool TryParse(string? address, out S7Address result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        try
        {
            result = Parse(address!);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Returns the canonical text form of the address, for example <c>DB1.DBX2.3</c>.</summary>
    public override string ToString()
    {
        if (Area == S7Area.DataBlock)
        {
            var kind = DataType switch
            {
                S7DataType.Bool => "DBX",
                S7DataType.Word => "DBW",
                S7DataType.DWord => "DBD",
                _ => "DBB",
            };

            return DataType == S7DataType.Bool
                ? $"DB{DbNumber}.{kind}{ByteOffset}.{BitOffset}"
                : $"DB{DbNumber}.{kind}{ByteOffset}";
        }

        var prefix = Area switch
        {
            S7Area.Inputs => "I",
            S7Area.Outputs => "Q",
            S7Area.Merkers => "M",
            S7Area.Timer => "T",
            S7Area.Counter => "C",
            _ => "?",
        };

        if (DataType == S7DataType.Bool)
        {
            return $"{prefix}{ByteOffset}.{BitOffset}";
        }

        var size = DataType switch
        {
            S7DataType.Word => "W",
            S7DataType.DWord => "D",
            _ => "B",
        };

        return $"{prefix}{size}{ByteOffset}";
    }

    /// <summary>Gets whether <paramref name="other"/> addresses the same location with the same width.</summary>
    /// <param name="other">Address to compare.</param>
    public bool Equals(S7Address other) =>
        Area == other.Area
        && DbNumber == other.DbNumber
        && ByteOffset == other.ByteOffset
        && DataType == other.DataType
        && BitOffset == other.BitOffset;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is S7Address other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + Area.GetHashCode();
            hash = (hash * 31) + DbNumber.GetHashCode();
            hash = (hash * 31) + ByteOffset.GetHashCode();
            hash = (hash * 31) + DataType.GetHashCode();
            hash = (hash * 31) + BitOffset.GetHashCode();
            return hash;
        }
    }

    /// <summary>Compares two addresses for value equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    public static bool operator ==(S7Address left, S7Address right) => left.Equals(right);

    /// <summary>Compares two addresses for inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    public static bool operator !=(S7Address left, S7Address right) => !left.Equals(right);

    private static int ParseInt(string value, string address)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result))
        {
            throw new FormatException($"Invalid numeric value in S7 address '{address}'.");
        }

        return result;
    }
}
