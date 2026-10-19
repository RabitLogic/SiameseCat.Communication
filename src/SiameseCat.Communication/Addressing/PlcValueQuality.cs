namespace SiameseCat.Communication;

/// <summary>Quality of a value returned by a driver.</summary>
public enum PlcValueQuality
{
    /// <summary>The value was read successfully and is usable.</summary>
    Good = 0,

    /// <summary>The device returned the value but flagged it as uncertain.</summary>
    Uncertain = 1,

    /// <summary>The value is not usable, for example because the device reported a sensor fault.</summary>
    Bad = 2,
}
