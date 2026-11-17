namespace CoopTime.Core;

public sealed class ClientSession
{
    public ClientSession(long playerId = 2)
    {
        this.playerId = playerId;
    }

    public void Reset(long? newPlayerId = null)
    {
        if (newPlayerId.HasValue)
        {
            playerId = newPlayerId.Value;
        }

        receiver.Reset();
        stateSequence = 0;
        lastPause = null;
        sentAt = -1000;
    }

    public bool AcceptSnapshot(LedgerSnapshot? incoming, long sender, long host, long now)
    {
        if (incoming?.Accounts is { Length: > 0 } accounts
            && (accounts.Length != 1 || accounts[0]?.PlayerId != playerId))
        {
            return false;
        }

        string? previous = receiver.Current?.Session;
        string previousConnection = Connection;
        if (!receiver.Accept(incoming, sender, host, now))
        {
            return false;
        }

        if (previous != incoming!.Session || previousConnection != Connection)
        {
            stateSequence = 0;
            lastPause = null;
            sentAt = -1000;
        }

        return true;
    }

    public PlayerState? StateIfDue(bool paused, long now)
    {
        if (View == null || (lastPause == paused && now - sentAt < 1000))
        {
            return null;
        }

        PlayerState state = new(PeerStateTracker.Version, View.Session, ++stateSequence, paused, Connection);
        lastPause = paused;
        sentAt = now;
        return state;
    }

    public bool ViewExpired(long now)
    {
        return View == null || now < receiver.ReceivedAt
            || now - receiver.ReceivedAt > PeerStateTracker.TimeoutMilliseconds;
    }

    public LedgerSnapshot? View => receiver.Current;
    private string Connection => View?.Accounts.FirstOrDefault(a => a.PlayerId == playerId)?.Connection ?? "";

    private readonly SnapshotReceiver receiver = new();
    private long stateSequence;
    private bool? lastPause;
    private long sentAt = -1000;
    private long playerId;
}
