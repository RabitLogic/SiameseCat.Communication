namespace SiameseCat.Core;

/// <summary>Optional capabilities that a driver can support.</summary>
[Flags]
public enum DriverCapabilities
{
    /// <summary>Nothing; the driver accepts no requests.</summary>
    None = 0,

    /// <summary>Single-address reads (<see cref="IReadable"/>).</summary>
    Read = 1,

    /// <summary>Single-address writes (<see cref="IWritable"/>).</summary>
    Write = 2,

    /// <summary>Native or grouped batch reads (<see cref="IBatchReadable"/>).</summary>
    BatchRead = 4,

    /// <summary>Native or grouped batch writes (<see cref="IBatchWritable"/>).</summary>
    BatchWrite = 8,

    /// <summary>Change notifications (<see cref="ISubscribable"/>).</summary>
    Subscribe = 16,

    /// <summary>Health probes (<see cref="IHealthCheckable"/>).</summary>
    Health = 32,

    /// <summary>Every capability; the default for full-featured drivers.</summary>
    All = Read | Write | BatchRead | BatchWrite | Subscribe | Health,
}
