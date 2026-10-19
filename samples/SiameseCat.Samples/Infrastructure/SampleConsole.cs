namespace SiameseCat.Samples.Infrastructure;

/// <summary>
/// Console helpers shared by every sample so that the output of all tutorials looks the same.
/// </summary>
public static class SampleConsole
{
    /// <summary>Writes the banner of a sample.</summary>
    /// <param name="title">Sample title.</param>
    /// <param name="description">What the sample demonstrates.</param>
    public static void Banner(string title, string description)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 72));
        Console.WriteLine(title);
        Console.WriteLine(new string('=', 72));
        Console.WriteLine(description);
        Console.WriteLine();
    }

    /// <summary>Writes a numbered step.</summary>
    /// <param name="message">Step description.</param>
    public static void Step(string message)
    {
        Console.WriteLine();
        Console.WriteLine($"-- {message}");
    }

    /// <summary>Writes a detail line under the current step.</summary>
    /// <param name="message">Detail text.</param>
    public static void Detail(string message) => Console.WriteLine($"   {message}");

    /// <summary>Writes a name/value pair.</summary>
    /// <param name="name">Label.</param>
    /// <param name="value">Value to print.</param>
    public static void Value(string name, object? value) => Console.WriteLine($"   {name,-24} {value}");

    /// <summary>Writes a note.</summary>
    /// <param name="message">Note text.</param>
    public static void Note(string message) => Console.WriteLine($"   > {message}");

    /// <summary>Writes a warning.</summary>
    /// <param name="message">Warning text.</param>
    public static void Warn(string message) => Console.WriteLine($"   ! {message}");

    /// <summary>Throws when a tutorial expectation is not met.</summary>
    /// <param name="condition">Condition that must hold.</param>
    /// <param name="message">Describes the expectation.</param>
    /// <exception cref="SampleFailedException">The condition is <see langword="false"/>.</exception>
    public static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new SampleFailedException(message);
        }

        Console.WriteLine($"   [ok] {message}");
    }

    /// <summary>Writes a table with a header row.</summary>
    /// <param name="headers">Column headers.</param>
    /// <param name="rows">Row values, one array per row.</param>
    public static void Table(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        var widths = new int[headers.Count];
        for (var i = 0; i < headers.Count; i++)
        {
            widths[i] = headers[i].Length;
            foreach (var row in rows)
            {
                widths[i] = Math.Max(widths[i], row[i].Length);
            }
        }

        Console.WriteLine("   " + Format(headers.ToArray(), widths));
        Console.WriteLine("   " + string.Join("-+-", widths.Select(width => new string('-', width))));

        foreach (var row in rows)
        {
            Console.WriteLine("   " + Format(row, widths));
        }
    }

    private static string Format(string[] cells, int[] widths) =>
        string.Join(" | ", cells.Select((cell, index) => cell.PadRight(widths[index])));
}

/// <summary>Raised when a sample detects an unexpected result.</summary>
public sealed class SampleFailedException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="message">Describes the failed expectation.</param>
    public SampleFailedException(string message)
        : base(message)
    {
    }
}
