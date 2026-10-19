namespace SiameseCat.Samples.Infrastructure;

/// <summary>
/// One runnable tutorial step. Samples are grouped by the first segment of <see cref="Id"/>,
/// which mirrors the folder layout under <c>samples/SiameseCat.Samples</c>.
/// </summary>
public interface ISample
{
    /// <summary>Gets the id used on the command line, for example <c>modbus/read-write</c>.</summary>
    string Id { get; }

    /// <summary>Gets the short title shown in the catalog.</summary>
    string Title { get; }

    /// <summary>Gets the one-line description shown in the catalog.</summary>
    string Description { get; }

    /// <summary>Gets whether the sample talks to real hardware when its options are supplied.</summary>
    /// <remarks>
    /// Samples that also ship an in-process simulator set this to <see langword="false"/> so that
    /// <c>selftest</c> can run them without a device.
    /// </remarks>
    bool RequiresHardware { get; }

    /// <summary>Gets the command line that runs the sample with its default options.</summary>
    string Usage { get; }

    /// <summary>Runs the sample.</summary>
    /// <param name="arguments">Options supplied after the sample id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken);
}
