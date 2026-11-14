namespace Carts.IntegrationTests;

/// <summary>
/// A clock that, once armed, makes one write to the database at the moment the API reads the
/// time while it holds the user's cart lock: a writer that ignores the lock.
/// </summary>
internal sealed class WriterIgnoringTheLock(string connectionString, string userId, Action write) : TimeProvider
{
    private int _armed;

    public void Arm() => Volatile.Write(ref _armed, 1);

    public override DateTimeOffset GetUtcNow()
    {
        if (Volatile.Read(ref _armed) == 1
            && CartLock.IsHeld(connectionString, userId)
            && Interlocked.Exchange(ref _armed, 0) == 1)
        {
            write();
        }

        return base.GetUtcNow();
    }
}
