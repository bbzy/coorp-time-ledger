namespace CoopTime.Core;

public sealed record SavedAccount(long PlayerId, string Name, string Minutes);

public sealed record LedgerSaveData(int SchemaVersion, SavedAccount[] Accounts, string MillisecondRemainder);

public static class LedgerSaveCodec
{
    public static bool TryDecode(LedgerSaveData data, out Account[] accounts,
        out GameMinutes remainder, out string error)
    {
        accounts = Array.Empty<Account>();
        remainder = default;
        error = "";
        if (data.SchemaVersion != SchemaVersion)
        {
            error = "ledger.SchemaVersion: unsupported save schema.";
            return false;
        }

        if (data.Accounts == null)
        {
            error = "ledger.Accounts: missing account list.";
            return false;
        }

        if (!GameMinutes.TryParse(data.MillisecondRemainder, out remainder) || remainder < 0 || remainder >= 1)
        {
            error = "ledger.MillisecondRemainder: expected an exact fraction between zero inclusive and one exclusive.";
            return false;
        }

        Dictionary<long, Account> decoded = new();
        for (int i = 0; i < data.Accounts.Length; i++)
        {
            SavedAccount? saved = data.Accounts[i];
            if (saved == null || saved.Name == null)
            {
                error = $"ledger.Accounts[{i}]: missing account or name.";
                return false;
            }

            if (!GameMinutes.TryParse(saved.Minutes, out GameMinutes minutes) || minutes < 0)
            {
                error = $"ledger.Accounts[{i}].Minutes: expected a nonnegative exact fraction.";
                return false;
            }

            if (!decoded.TryAdd(saved.PlayerId, new Account(saved.PlayerId, saved.Name, false, false, minutes)))
            {
                error = $"ledger.Accounts[{i}].PlayerId: duplicate player.";
                return false;
            }
        }

        accounts = decoded.Values.ToArray();
        return true;
    }

    public const int SchemaVersion = 1;
}
