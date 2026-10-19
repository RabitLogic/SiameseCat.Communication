namespace SiameseCat.Communication;

/// <summary>Argument and state checks that work on every target framework.</summary>
internal static class Guard
{
    public static void NotNull(object? value, string? name)
    {
        if (value is null)
        {
            throw new ArgumentNullException(name);
        }
    }

    public static void NotNullOrWhiteSpace(string? value, string? name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must be specified.", name);
        }
    }

    public static void NotNegative(int value, string? name)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Value cannot be negative.");
        }
    }

    public static void NotDisposed(bool disposed, object instance)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(disposed, instance);
#else
        if (disposed)
        {
            throw new ObjectDisposedException(instance.GetType().FullName);
        }
#endif
    }
}
