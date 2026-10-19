namespace SiameseCat.Samples.Infrastructure;

/// <summary>
/// Parsed <c>--name value</c> pairs passed to a sample.
/// </summary>
public sealed class SampleArguments
{
    private static readonly SampleArguments EmptyInstance = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private readonly Dictionary<string, string> _values;

    private SampleArguments(Dictionary<string, string> values) => _values = values;

    /// <summary>Gets no arguments.</summary>
    public static SampleArguments Empty => EmptyInstance;

    /// <summary>Gets whether an option was supplied.</summary>
    /// <param name="name">Option name without the leading dashes.</param>
    public bool Has(string name) => _values.ContainsKey(name);

    /// <summary>Gets a string option.</summary>
    /// <param name="name">Option name without the leading dashes.</param>
    /// <param name="fallback">Value used when the option is missing.</param>
    public string Get(string name, string fallback) =>
        _values.TryGetValue(name, out var value) ? value : fallback;

    /// <summary>Gets an integer option.</summary>
    /// <param name="name">Option name without the leading dashes.</param>
    /// <param name="fallback">Value used when the option is missing.</param>
    public int GetInt(string name, int fallback) =>
        _values.TryGetValue(name, out var value)
            ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            : fallback;

    /// <summary>Gets a boolean option; a bare flag such as <c>--verbose</c> counts as <see langword="true"/>.</summary>
    /// <param name="name">Option name without the leading dashes.</param>
    /// <param name="fallback">Value used when the flag is missing.</param>
    public bool GetFlag(string name, bool fallback = false) =>
        _values.TryGetValue(name, out var value)
            ? value.Length == 0 || !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            : fallback;

    /// <summary>Parses command-line arguments of the form <c>--name value</c>.</summary>
    /// <param name="arguments">Arguments that follow the sample id.</param>
    public static SampleArguments Parse(IReadOnlyList<string> arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < arguments.Count; i++)
        {
            var token = arguments[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{token}'. Options use the form --name value.");
            }

            var name = token[2..];
            if (name.Length == 0)
            {
                throw new ArgumentException("Empty option name.");
            }

            var next = i + 1;
            if (next < arguments.Count && !arguments[next].StartsWith("--", StringComparison.Ordinal))
            {
                values[name] = arguments[next];
                i = next;
            }
            else
            {
                values[name] = string.Empty;
            }
        }

        return new SampleArguments(values);
    }
}
