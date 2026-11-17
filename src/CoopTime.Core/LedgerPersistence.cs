namespace CoopTime.Core;

public sealed class LedgerPersistence
{
    public LedgerPersistence(HostSession host)
    {
        this.host = host;
    }

    public bool Load(Func<LedgerSaveData?> read, out string error)
    {
        host.Reset();
        Ready = false;
        try
        {
            Ready = host.TryRestoreSave(read(), out error);
            return Ready;
        }
        catch (Exception ex)
        {
            error = $"ledger: unable to read save data ({ex.GetType().Name}).";
            return false;
        }
    }

    public bool Save(Action<LedgerSaveData> write, out string error)
    {
        if (!Ready)
        {
            error = "ledger: save data is unavailable; existing stored data will not be replaced.";
            return false;
        }

        try
        {
            write(host.ExportSave());
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            Ready = false;
            error = $"ledger: unable to write save data ({ex.GetType().Name}).";
            return false;
        }
    }

    public void Reset()
    {
        Ready = false;
    }

    public bool Ready { get; private set; }

    private readonly HostSession host;
}
