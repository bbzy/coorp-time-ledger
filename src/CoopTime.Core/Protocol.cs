namespace CoopTime.Core;

public sealed record PlayerState(int Protocol, string Session, long Sequence, bool Paused, string Connection = "");

public sealed record AccountView(long PlayerId, string Name, bool Online, bool Paused, string Minutes, string Connection = "");

public sealed record LedgerSnapshot(int Protocol, string Session, long Sequence, long HostId,
    ClockMode Mode, string Notice, AccountView[] Accounts, uint Day = 0)
{
    public LedgerSnapshot ForPlayer(long playerId)
    {
        return this with { Accounts = Accounts.Where(account => account.PlayerId == playerId).ToArray() };
    }
}

public sealed class PeerStateTracker
{
    public void Reset(string newSession)
    {
        session = newSession;
        leases.Clear();
    }

    public bool Accept(long playerId, PlayerState? state, long nowMilliseconds)
    {
        if (state == null || session.Length == 0 || state.Protocol != Version
            || state.Session != session || state.Sequence < 0 || nowMilliseconds < 0)
        {
            return false;
        }

        if (leases.TryGetValue(playerId, out Lease? previous) && state.Sequence <= previous.Sequence)
        {
            return false;
        }

        leases[playerId] = new Lease(state.Sequence, nowMilliseconds, state.Paused);
        return true;
    }

    public bool TryGet(long playerId, long nowMilliseconds, out bool paused)
    {
        if (leases.TryGetValue(playerId, out Lease? lease)
            && nowMilliseconds >= lease.ReceivedAt && nowMilliseconds - lease.ReceivedAt <= TimeoutMilliseconds)
        {
            paused = lease.Paused;
            return true;
        }

        paused = false;
        return false;
    }

    public void Remove(long playerId)
    {
        leases.Remove(playerId);
    }

    public const int Version = 3;
    public const int TimeoutMilliseconds = 5000;

    private sealed record Lease(long Sequence, long ReceivedAt, bool Paused);

    private string session = "";
    private readonly Dictionary<long, Lease> leases = new();
}

public sealed class SnapshotReceiver
{
    public bool Accept(LedgerSnapshot? incoming, long sender, long host, long nowMilliseconds)
    {
        if (incoming == null || sender != host || incoming.HostId != host || incoming.Protocol != PeerStateTracker.Version
            || incoming.Sequence < 0 || string.IsNullOrEmpty(incoming.Session)
            || retiredSessions.Contains(incoming.Session) || nowMilliseconds < 0
            || incoming.Notice == null || incoming.Accounts == null
            || incoming.Accounts.Any(a => a == null || a.Name == null || a.Minutes == null || a.Connection == null)
            || !Enum.IsDefined(typeof(ClockMode), incoming.Mode)
            || (Current != null && incoming.Day < Current.Day))
        {
            return false;
        }

        if (Current?.Session == incoming.Session && incoming.Sequence <= Current.Sequence)
        {
            return false;
        }

        if (Current != null && Current.Session != incoming.Session)
        {
            retiredSessions.Add(Current.Session);
        }

        Current = incoming;
        ReceivedAt = nowMilliseconds;
        return true;
    }

    public void Reset()
    {
        Current = null;
        ReceivedAt = 0;
        retiredSessions.Clear();
    }

    public LedgerSnapshot? Current { get; private set; }
    public long ReceivedAt { get; private set; }

    private readonly HashSet<string> retiredSessions = new();
}
