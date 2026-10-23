using System.Text;

namespace SiameseCat.Communication.Modbus;

/// <summary>Access type requested by Read Device Identification (function code 0x2B, MEI type 0x0E).</summary>
public enum ModbusDeviceIdCode : byte
{
    /// <summary>Basic objects: vendor name, product code and revision (object ids 0x00 to 0x02).</summary>
    Basic = 0x01,

    /// <summary>Regular objects: the basic objects plus vendor URL, product name, model name and application name (0x00 to 0x06).</summary>
    Regular = 0x02,

    /// <summary>Extended objects, including vendor specific ids (0x80 to 0xFF).</summary>
    Extended = 0x03,

    /// <summary>A single object, named by the request's object id.</summary>
    Individual = 0x04,
}

/// <summary>
/// Objects returned by Read Device Identification (function code 0x2B, MEI type 0x0E).
/// Values are kept as raw bytes because vendor specific objects are not necessarily text.
/// </summary>
public sealed class ModbusDeviceIdentification
{
    private static readonly Encoding TextEncoding = new UTF8Encoding(false);

    internal ModbusDeviceIdentification(
        byte conformityLevel,
        bool moreFollows,
        byte nextObjectId,
        IReadOnlyDictionary<byte, byte[]> objects)
    {
        ConformityLevel = conformityLevel;
        MoreFollows = moreFollows;
        NextObjectId = nextObjectId;
        Objects = objects;
    }

    /// <summary>Gets the highest access type the server supports, as reported on the wire.</summary>
    public byte ConformityLevel { get; }

    /// <summary>Gets whether the server holds further objects that were not returned.</summary>
    public bool MoreFollows { get; }

    /// <summary>Gets the object id to continue from when <see cref="MoreFollows"/> is set.</summary>
    public byte NextObjectId { get; }

    /// <summary>Gets the raw value of every object that was returned, keyed by object id.</summary>
    public IReadOnlyDictionary<byte, byte[]> Objects { get; }

    /// <summary>Gets object 0x00 (vendor name), or <see langword="null"/> when the server does not expose it.</summary>
    public string? VendorName => GetObjectText(0x00);

    /// <summary>Gets object 0x01 (product code), or <see langword="null"/> when the server does not expose it.</summary>
    public string? ProductCode => GetObjectText(0x01);

    /// <summary>Gets object 0x02 (major/minor revision), or <see langword="null"/> when the server does not expose it.</summary>
    public string? MajorMinorRevision => GetObjectText(0x02);

    /// <summary>Gets object 0x03 (vendor URL), or <see langword="null"/> when the server does not expose it.</summary>
    public string? VendorUrl => GetObjectText(0x03);

    /// <summary>Gets object 0x04 (product name), or <see langword="null"/> when the server does not expose it.</summary>
    public string? ProductName => GetObjectText(0x04);

    /// <summary>Gets object 0x05 (model name), or <see langword="null"/> when the server does not expose it.</summary>
    public string? ModelName => GetObjectText(0x05);

    /// <summary>Gets object 0x06 (user application name), or <see langword="null"/> when the server does not expose it.</summary>
    public string? UserApplicationName => GetObjectText(0x06);

    /// <summary>Reads one object as UTF-8 text.</summary>
    /// <param name="objectId">Object identifier, for example 0x00 for the vendor name.</param>
    /// <returns>The decoded text, or <see langword="null"/> when the object was not returned.</returns>
    public string? GetObjectText(byte objectId) =>
        Objects.TryGetValue(objectId, out var value) ? TextEncoding.GetString(value, 0, value.Length) : null;
}
