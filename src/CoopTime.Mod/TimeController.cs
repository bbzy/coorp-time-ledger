using System.Diagnostics;
using CoopTime.Core;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace CoopTime.Mod;

internal sealed class TimeController : GameComponent
{
    public TimeController(Game game, IModHelper helper, IManifest manifest, IMonitor monitor, bool patchReady)
        : base(game)
    {
        this.helper = helper;
        this.manifest = manifest;
        this.monitor = monitor;
        this.patchReady = patchReady;
        persistence = new LedgerPersistence(host);
        Enabled = false;
    }

    public void LoadSave()
    {
        if (Context.IsMainPlayer
            && !persistence.Load(() => helper.Data.ReadSaveData<LedgerSaveData>(SaveKey), out string error))
        {
            monitor.Log($"Save '{Constants.CurrentSavePath ?? Constants.SaveFolderName}', mod-data '{SaveKey}': {error}", LogLevel.Error);
        }
    }

    public void SaveLedger()
    {
        EndDay();
        if (Context.IsMainPlayer && persistence.Ready
            && !persistence.Save(data => helper.Data.WriteSaveData(SaveKey, data), out string error))
        {
            monitor.Log($"Save '{Constants.CurrentSavePath ?? Constants.SaveFolderName}', mod-data '{SaveKey}': {error}", LogLevel.Error);
        }
    }

    public void StartDay()
    {
        host.StartDay(Game1.MasterPlayer.UniqueMultiplayerID, Guid.NewGuid().ToString("N"), Game1.stats.DaysPlayed);
        client.Reset(Game1.player.UniqueMultiplayerID);
        snapshot = null;
        lastPause = null;
        Refresh();
        SendSnapshot();
    }

    public void EndDay()
    {
        host.Suspend();
        SendSnapshot();
    }

    public void ReturnToTitle()
    {
        host.Reset();
        persistence.Reset();
        client.Reset();
        snapshot = null;
    }

    public void Tick()
    {
        if (!Context.IsWorldReady)
        {
            return;
        }

        bool paused = IsLocallyPaused();
        bool changed = lastPause != paused;
        if (Context.IsMainPlayer)
        {
            Refresh();
            if (changed || host.SnapshotDue(Now))
            {
                SendSnapshot();
            }
        }
        else
        {
            SendState(paused);
        }

        lastPause = paused;
    }

    public void PeerChanged(long id)
    {
        host.PeerChanged(id);
        if (Context.IsMainPlayer && Context.IsWorldReady)
        {
            Refresh();
            SendSnapshot();
        }
    }

    public void Receive(ModMessageReceivedEventArgs e)
    {
        if (!Context.IsWorldReady || e.FromModID != manifest.UniqueID)
        {
            return;
        }

        if (Context.IsMainPlayer && e.Type == StateMessage)
        {
            if (host.AcceptState(e.FromPlayerID, e.ReadAs<PlayerState>(), Now))
            {
                Refresh();
                SendSnapshot();
            }
        }
        else if (!Context.IsMainPlayer && e.Type == SnapshotMessage)
        {
            if (client.AcceptSnapshot(e.ReadAs<LedgerSnapshot>(), e.FromPlayerID,
                Game1.MasterPlayer.UniqueMultiplayerID, Now))
            {
                SendState(IsLocallyPaused());
            }
        }
    }

    public int AdjustElapsed(int elapsed)
    {
        if (!Context.IsMainPlayer || !Context.IsWorldReady)
        {
            return elapsed;
        }

        Refresh();
        if (!CanControlClock || elapsed <= 0)
        {
            return elapsed;
        }

        int cadence = Game1.realMilliSecondsPerGameTenMinutes
            + GameClockPatch.GetExtraMilliseconds(Game1.currentLocation) * 10;
        int result = host.AdjustElapsed(elapsed, cadence, Game1.shouldTimePass());
        if (host.SnapshotDue(Now))
        {
            SendSnapshot();
        }

        return result;
    }

    private void Refresh()
    {
        if (!Context.IsMainPlayer || !Context.IsWorldReady)
        {
            return;
        }

        Farmer[] players = Game1.getOnlineFarmers().ToArray();
        PlayerPresence[] roster = players.Select(player =>
        {
            IMultiplayerPeer? peer = helper.Multiplayer.GetConnectedPlayer(player.UniqueMultiplayerID);
            IMultiplayerPeerMod? mod = peer?.GetMod(manifest.UniqueID);
            return new PlayerPresence(player.UniqueMultiplayerID, player.Name,
                player.UniqueMultiplayerID == Game1.player.UniqueMultiplayerID
                    || mod?.Version.ToString() == manifest.Version.ToString(), peer?.IsSplitScreen == true);
        }).ToArray();
        host.Refresh(new HostFrame(Context.IsWorldReady, patchReady, Context.IsSplitScreen,
            Game1.HostPaused, Game1.showingEndOfNightStuff || Game1.newDay, IsLocallyPaused(), roster,
            persistence.Ready), Now);
    }

    private static bool IsLocallyPaused()
    {
        return SinglePlayerTiming.IsPaused(new SinglePlayerClockState(
            Game1.shouldTimePass(ignore_multiplayer: true), Game1.activeClickableMenu != null,
            Game1.currentMinigame != null, Game1.player.viewingLocation.Value != null,
            Game1.options.pauseWhenOutOfFocus, Game1.game1.IsActiveNoOverlay,
            Game1.activeClickableMenu is StardewValley.Menus.ReadyCheckDialog { checkName: "sleep" },
            Game1.currentMinigame?.doMainGameUpdates() ?? false));
    }

    private void SendState(bool paused)
    {
        PlayerState? state = client.StateIfDue(paused, Now);
        if (state == null)
        {
            return;
        }

        helper.Multiplayer.SendMessage(state, StateMessage,
            new[] { manifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }

    private void SendSnapshot()
    {
        if (!Context.IsMainPlayer || !Context.IsWorldReady || host.Session.Length == 0)
        {
            return;
        }

        LedgerSnapshot latest = host.CreateSnapshot(Now);
        snapshot = latest.ForPlayer(latest.HostId);
        foreach (AccountView account in latest.Accounts)
        {
            if (account.Online && account.PlayerId != latest.HostId)
            {
                helper.Multiplayer.SendMessage(latest.ForPlayer(account.PlayerId), SnapshotMessage,
                    new[] { manifest.UniqueID }, new[] { account.PlayerId });
            }
        }
    }

    public bool CanControlClock => Context.IsMainPlayer && host.CanControlClock;
    public AccountView? LocalAccount => Context.IsMainPlayer
        ? snapshot?.Accounts.FirstOrDefault()
        : client.ViewExpired(Now) ? null : client.View?.Accounts.FirstOrDefault();
    public bool? LocalPaused => lastPause;

    private long Now => timer.ElapsedMilliseconds;

    private const string StateMessage = "PlayerState";
    private const string SnapshotMessage = "LedgerSnapshot";
    private const string SaveKey = "ledger";

    private readonly IModHelper helper;
    private readonly IManifest manifest;
    private readonly IMonitor monitor;
    private readonly bool patchReady;
    private readonly HostSession host = new();
    private readonly LedgerPersistence persistence;
    private readonly ClientSession client = new();
    private readonly Stopwatch timer = Stopwatch.StartNew();
    private bool? lastPause;
    private LedgerSnapshot? snapshot;
}
