using SiameseCat.Core;

namespace SiameseCat.Drivers.Inovance;

/// <summary>
/// Maps Inovance soft elements onto Modbus areas.
/// </summary>
/// <remarks>
/// <para><c>D</c> (data registers) are holding registers, <c>M</c> (auxiliary relays) and
/// <c>Y</c> (outputs) are coils, <c>X</c> (inputs) are discrete inputs. <c>X</c> and <c>Y</c>
/// numbers are octal, matching the PLC programming convention.</para>
/// <para>The defaults follow the mapping most H3U/H5U slaves expose: D/M/X start at 0 and the Y
/// area starts at coil 8192 (<c>0x2000</c>). Series or slaves that map the areas differently can
/// override every base through the <c>dBase</c>, <c>mBase</c>, <c>xBase</c> and <c>yBase</c>
/// driver configuration options.</para>
/// </remarks>
public readonly record struct InovanceMapping
{
    /// <summary>Gets the offset added to <c>D</c> element numbers inside the holding-register area.</summary>
    public int DBase { get; init; }

    /// <summary>Gets the offset added to <c>M</c> element numbers inside the coil area.</summary>
    public int MBase { get; init; }

    /// <summary>Gets the offset added to <c>X</c> element numbers inside the discrete-input area.</summary>
    public int XBase { get; init; }

    /// <summary>Gets the offset added to <c>Y</c> element numbers inside the coil area.</summary>
    public int YBase { get; init; }

    /// <summary>Gets the default mapping (<c>D</c>/<c>M</c>/<c>X</c> at 0, <c>Y</c> at 8192).</summary>
    public static InovanceMapping Default { get; } = new()
    {
        DBase = 0,
        MBase = 0,
        XBase = 0,
        YBase = 8192,
    };

    /// <summary>Reads the mapping from the driver configuration options.</summary>
    /// <param name="configuration">Driver configuration.</param>
    public static InovanceMapping FromConfiguration(DriverConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var mapping = new InovanceMapping
        {
            DBase = configuration.GetOption("dBase", Default.DBase, 0, ushort.MaxValue),
            MBase = configuration.GetOption("mBase", Default.MBase, 0, ushort.MaxValue),
            XBase = configuration.GetOption("xBase", Default.XBase, 0, ushort.MaxValue),
            YBase = configuration.GetOption("yBase", Default.YBase, 0, ushort.MaxValue),
        };

        mapping.Validate();
        return mapping;
    }

    /// <summary>Throws when a base is outside the Modbus address space.</summary>
    public readonly void Validate()
    {
        ValidateBase(DBase, nameof(DBase));
        ValidateBase(MBase, nameof(MBase));
        ValidateBase(XBase, nameof(XBase));
        ValidateBase(YBase, nameof(YBase));
    }

    private static void ValidateBase(int value, string name)
    {
        if (value < 0 || value > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                "The base must fit in a 16-bit Modbus address.");
        }
    }
}
