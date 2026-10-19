namespace SiameseCat.Server;

/// <summary>Validation shared by the slave options.</summary>
internal static class ModbusServerIdentifier
{
    public const int MaxLength = 240;

    public static void Validate(string? identifier)
    {
        if (identifier is null || string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("The server identifier must be specified.", nameof(identifier));
        }

        if (identifier.Length > MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(identifier),
                identifier.Length,
                $"The server identifier must not exceed {MaxLength} characters.");
        }

        for (var i = 0; i < identifier.Length; i++)
        {
            if (identifier[i] > 0x7F)
            {
                throw new ArgumentException(
                    "The server identifier must contain ASCII characters only.",
                    nameof(identifier));
            }
        }
    }
}
