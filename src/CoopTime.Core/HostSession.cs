namespace CoopTime.Core;

public sealed record PlayerPresence(long Id, string Name, bool Compatible = true, bool SplitScreen = false);

public sealed record HostFrame(bool WorldReady, bool IntegrationAvailable, bool SplitScreen,
    bool GlobalPause, bool DayTransition, bool LocalPaused, PlayerPresence[] Players, bool StorageAvailable = true);

public sealed class HostSession
{
    public void StartDay(long hostId, string session, uint day)
    {
        HostId = hostId;
        Session = session;
        Day = day;
        ledger.BeginDay();
        connections.Clear();
        peers.Reset(session);
        sequence = 0;
        lastSnapshot = null;
        suspended = false;
        Notice = "syncing";
        frame = null;
    }

    public void Suspend()
    {
        suspended = true;
        Notice = "transition";
    }

    public void Reset()
    {
        ledger.Reset();
        math.Reset();
        connections.Clear();
        peers.Reset("");
        Session = "";
        frame = null;
        lastSnapshot = null;
        suspended = true;
        Notice = "transition";
    }

    public void Refresh(HostFrame input, long now)
    {
        frame = input;
        if (!input.WorldReady)
        {
            Notice = "transition";
            return;
        }

        ledger.SetPlayers(input.Players.Select(p => (p.Id, p.Name)));
        ledger.SetPaused(HostId, input.LocalPaused);
        foreach (long id in connections.Keys.Where(id => !input.Players.Any(p => p.Id == id)).ToArray())
        {
            connections.Remove(id);
            peers.Remove(id);
        }

        bool incompatible = false;
        bool syncing = !input.Players.Any(p => p.Id == HostId);
        foreach (PlayerPresence player in input.Players.Where(p => p.Id != HostId))
        {
            if (!connections.ContainsKey(player.Id))
            {
                connections[player.Id] = Guid.NewGuid().ToString("N");
            }

            if (!player.Compatible || player.SplitScreen)
            {
                incompatible = true;
            }
            else if (peers.TryGet(player.Id, now, out bool paused))
            {
                ledger.SetPaused(player.Id, paused);
            }
            else
            {
                syncing = true;
            }
        }

        if (!input.StorageAvailable)
        {
            Notice = "storage-error";
        }
        else if (!input.IntegrationAvailable || input.SplitScreen)
        {
            Notice = "unsupported";
        }
        else if (suspended || input.GlobalPause || input.DayTransition || Session.Length == 0)
        {
            Notice = "transition";
        }
        else if (incompatible)
        {
            Notice = "incompatible";
        }
        else
        {
            Notice = syncing ? "syncing" : "";
        }
    }

    public bool AcceptState(long id, PlayerState? state, long now)
    {
        return frame?.WorldReady == true && id != HostId && state != null
            && frame.Players.Any(p => p.Id == id && p.Compatible && !p.SplitScreen)
            && connections.TryGetValue(id, out string? connection) && state.Connection == connection
            && peers.Accept(id, state, now);
    }

    public void PeerChanged(long id)
    {
        peers.Remove(id);
        connections.Remove(id);
    }

    public int AdjustElapsed(int elapsed, int cadence, bool nativeClockRunning)
    {
        if (!CanControlClock || !nativeClockRunning || elapsed <= 0)
        {
            return elapsed;
        }

        if (cadence <= 0)
        {
            Notice = "unsupported";
            return elapsed;
        }

        ClockStep step = ledger.Advance(ClockMath.ToMinutes(elapsed, cadence));
        return math.ToMilliseconds(step.WorldMinutes, cadence);
    }

    public bool SnapshotDue(long now)
    {
        return lastSnapshot == null || now - sentAt >= 1000 || lastSnapshot.Mode != Mode
            || lastSnapshot.Notice != Notice;
    }

    public LedgerSnapshot CreateSnapshot(long now)
    {
        lastSnapshot = new LedgerSnapshot(PeerStateTracker.Version, Session, ++sequence, HostId,
            Mode, Notice, ledger.Accounts.Select(a => new AccountView(a.PlayerId, a.Name, a.Online,
                a.Paused, ClockMath.Display(a.Minutes), ConnectionFor(a.PlayerId))).ToArray(), Day);
        sentAt = now;
        return lastSnapshot;
    }

    public LedgerSaveData ExportSave()
    {
        return new LedgerSaveData(LedgerSaveCodec.SchemaVersion,
            ledger.Accounts.Select(a => new SavedAccount(a.PlayerId, a.Name, a.Minutes.ToString())).ToArray(),
            math.MillisecondRemainder.ToString());
    }

    public bool TryRestoreSave(LedgerSaveData? data, out string error)
    {
        if (data == null)
        {
            ledger.Reset();
            math.Reset();
            error = "";
            return true;
        }

        if (!LedgerSaveCodec.TryDecode(data, out Account[] accounts, out GameMinutes remainder, out error))
        {
            return false;
        }

        ledger.RestoreAccounts(accounts);
        math.RestoreRemainder(remainder);
        return true;
    }

    public bool CanControlClock => frame?.WorldReady == true && !suspended && Notice.Length == 0
        && ledger.Accounts.Count(p => p.Online) >= 2;
    public string ConnectionFor(long id) => connections.TryGetValue(id, out string? token) ? token : "";
    public ClockMode Mode => Notice.Length == 0 ? ledger.Mode : ClockMode.Unavailable;
    public long HostId { get; private set; }
    public string Session { get; private set; } = "";
    public uint Day { get; private set; }
    public string Notice { get; private set; } = "transition";
    public IReadOnlyList<Account> Accounts => ledger.Accounts;

    private readonly Ledger ledger = new();
    private readonly ClockMath math = new();
    private readonly PeerStateTracker peers = new();
    private readonly Dictionary<long, string> connections = new();
    private HostFrame? frame;
    private LedgerSnapshot? lastSnapshot;
    private long sequence;
    private long sentAt;
    private bool suspended = true;
}
