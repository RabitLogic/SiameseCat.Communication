namespace SiameseCat.Communication;

/// <summary>Lifecycle state of a PLC driver.</summary>
public enum DriverState
{
    /// <summary>Not connected.</summary>
    Disconnected,

    /// <summary>Connect is in progress.</summary>
    Connecting,

    /// <summary>Connected and ready for requests.</summary>
    Connected,

    /// <summary>The last connection attempt or request failed and the driver requires a reconnect.</summary>
    Faulted,
}

/// <summary>Describes a driver state transition.</summary>
public sealed class DriverStateChangedEventArgs : EventArgs
{
    /// <summary>Creates the event arguments.</summary>
    /// <param name="oldState">State before the transition.</param>
    /// <param name="newState">State after the transition.</param>
    public DriverStateChangedEventArgs(DriverState oldState, DriverState newState)
    {
        OldState = oldState;
        NewState = newState;
    }

    /// <summary>Gets the state before the transition.</summary>
    public DriverState OldState { get; }

    /// <summary>Gets the state after the transition.</summary>
    public DriverState NewState { get; }
}
