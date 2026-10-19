namespace SiameseCat.Server;

/// <summary>Shared validation for the buffer sizes exposed by the slaves.</summary>
internal static class ModbusBufferSizes
{
    public const int Min = 1;

    public const int Max = 65536;

    public static int Validate(int size, string name)
    {
        if (size is < Min or > Max)
        {
            throw new ArgumentOutOfRangeException(name, size, "Buffer sizes must be between 1 and 65536.");
        }

        return size;
    }
}
