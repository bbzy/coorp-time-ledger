using CoopTime.Core;
using Newtonsoft.Json;

namespace CoopTime.Tests;

internal sealed class SessionSimulation
{
    public void Start(int count)
    {
        Host.StartDay(1, "day1", 1);
        Roster = Enumerable.Range(1, count).Select(i => new PlayerPresence(i, $"P{i}")).ToArray();
        foreach (PlayerPresence player in Roster)
        {
            Pauses[player.Id] = false;
            if (player.Id != 1)
            {
                clients[player.Id] = new ClientSession(player.Id);
            }
        }

        Pump();
    }

    public void Pump()
    {
        for (int pass = 0; pass < 2; pass++)
        {
            Host.Refresh(Frame(), Now);
            if (Host.SnapshotDue(Now))
            {
                LedgerSnapshot snapshot = Host.CreateSnapshot(Now);
                foreach (long id in clients.Keys)
                {
                    string json = JsonConvert.SerializeObject(snapshot.ForPlayer(id));
                    Queue(id, true, json);
                }
            }

            DeliverDue();
            foreach ((long id, ClientSession client) in clients)
            {
                PlayerState? state = client.StateIfDue(Pauses[id], Now);
                if (state != null && !DropStates)
                {
                    Queue(id, false, JsonConvert.SerializeObject(state));
                }
            }

            DeliverDue();
            Host.Refresh(Frame(), Now);
        }
    }

    public int Advance(int milliseconds, int cadence = 7000)
    {
        int world = 0;
        for (int remaining = milliseconds; remaining > 0;)
        {
            int delta = Math.Min(16, remaining);
            Pump();
            world += Host.AdjustElapsed(delta, cadence, true);
            Now += delta;
            remaining -= delta;
        }

        Pump();
        return world;
    }

    private HostFrame Frame() => new(true, true, false, false, false, Pauses[1], Roster);

    private void Queue(long player, bool snapshot, string json)
    {
        long packet = ++sent;
        if (Noisy && packet % 11 == 0)
        {
            return;
        }

        long due = Now + (Noisy ? random.Next(0, 201) : 0);
        pending.Enqueue(new Envelope(player, snapshot, json), (due, packet));
        if (Noisy && packet % 7 == 0)
        {
            pending.Enqueue(new Envelope(player, snapshot, json), (due + 17, packet));
        }
    }

    private void DeliverDue()
    {
        while (pending.TryPeek(out _, out (long At, long Order) priority) && priority.At <= Now)
        {
            Envelope message = pending.Dequeue();
            if (message.Snapshot)
            {
                clients[message.Player].AcceptSnapshot(JsonConvert.DeserializeObject<LedgerSnapshot>(message.Json), 1, 1, Now);
            }
            else
            {
                Host.AcceptState(message.Player, JsonConvert.DeserializeObject<PlayerState>(message.Json), Now);
            }
        }
    }

    public HostSession Host { get; } = new();
    public PlayerPresence[] Roster { get; private set; } = Array.Empty<PlayerPresence>();
    public Dictionary<long, bool> Pauses { get; } = new();
    public long Now { get; set; }
    public bool DropStates { get; set; }
    public bool Noisy { get; set; }
    public IReadOnlyDictionary<long, ClientSession> Clients => clients;

    private sealed record Envelope(long Player, bool Snapshot, string Json);

    private readonly Dictionary<long, ClientSession> clients = new();
    private readonly PriorityQueue<Envelope, (long At, long Order)> pending = new();
    private readonly Random random = new(20261117);
    private long sent;
}
