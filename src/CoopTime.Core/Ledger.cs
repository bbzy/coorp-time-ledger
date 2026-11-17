namespace CoopTime.Core;

public enum ClockMode
{
    Running,
    Repaying,
    EveryonePaused,
    Unavailable
}

public sealed record Account(long PlayerId, string Name, bool Online, bool Paused, GameMinutes Minutes);

public readonly record struct ClockStep(GameMinutes WorldMinutes, GameMinutes RepaidMinutes, ClockMode Mode);

public sealed class Ledger
{
    public void SetPlayers(IEnumerable<(long Id, string Name)> players)
    {
        HashSet<long> online = new();
        foreach ((long id, string name) in players)
        {
            online.Add(id);
            accounts[id] = accounts.TryGetValue(id, out Account? old)
                ? old with { Name = name, Online = true }
                : new Account(id, name, true, false, 0);
        }

        foreach (long id in accounts.Keys.ToArray())
        {
            if (!online.Contains(id))
            {
                accounts[id] = accounts[id] with { Online = false };
            }
        }
    }

    public void SetPaused(long id, bool paused)
    {
        if (accounts.TryGetValue(id, out Account? account))
        {
            accounts[id] = account with { Paused = paused };
        }
    }

    public ClockStep Advance(GameMinutes minutes)
    {
        if (minutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes));
        }

        Account[] online = accounts.Values.Where(p => p.Online).ToArray();
        ClockMode mode = GetMode(online);
        if (online.Length < 2 || mode == ClockMode.EveryonePaused)
        {
            return new ClockStep(mode == ClockMode.EveryonePaused ? 0 : minutes, 0, mode);
        }

        GameMinutes repaid = 0;
        if (mode == ClockMode.Repaying)
        {
            repaid = GameMinutes.Min(minutes, online.Where(p => !p.Paused).Min(p => p.Minutes));
            foreach (Account account in online.Where(p => !p.Paused))
            {
                accounts[account.PlayerId] = account with { Minutes = account.Minutes - repaid };
            }
        }

        GameMinutes world = minutes - repaid;
        foreach (Account account in online.Where(p => p.Paused))
        {
            accounts[account.PlayerId] = account with { Minutes = account.Minutes + world };
        }

        return new ClockStep(world, repaid, Mode);
    }

    public void Reset()
    {
        accounts.Clear();
    }

    public void BeginDay()
    {
        foreach (long id in accounts.Keys.ToArray())
        {
            accounts[id] = accounts[id] with { Online = false, Paused = false };
        }
    }

    internal void RestoreAccounts(IEnumerable<Account> restored)
    {
        accounts.Clear();
        foreach (Account account in restored)
        {
            accounts.Add(account.PlayerId, account);
        }
    }

    private static ClockMode GetMode(Account[] online)
    {
        if (online.Length < 2 || online.All(p => !p.Paused))
        {
            return ClockMode.Running;
        }

        if (online.All(p => p.Paused))
        {
            return ClockMode.EveryonePaused;
        }

        return online.Where(p => !p.Paused).All(p => p.Minutes > 0)
            ? ClockMode.Repaying
            : ClockMode.Running;
    }

    public ClockMode Mode => GetMode(accounts.Values.Where(p => p.Online).ToArray());
    public IReadOnlyList<Account> Accounts => accounts.Values.OrderBy(p => p.PlayerId).ToArray();

    private readonly Dictionary<long, Account> accounts = new();
}
