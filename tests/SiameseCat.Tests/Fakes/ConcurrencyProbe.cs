namespace SiameseCat.Tests.Fakes;

/// <summary>Counts how many operations run at the same time across drivers that share it.</summary>
internal sealed class ConcurrencyProbe
{
    private int _active;

    public int MaxActive { get; private set; }

    public void Enter()
    {
        var active = Interlocked.Increment(ref _active);
        if (active > MaxActive)
        {
            MaxActive = active;
        }
    }

    public void Exit() => Interlocked.Decrement(ref _active);
}
