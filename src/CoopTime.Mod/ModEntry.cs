using CoopTime.Core;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace CoopTime.Mod;

public sealed class ModEntry : StardewModdingAPI.Mod
{
    public override void Entry(IModHelper helper)
    {
        bool ready = false;
        try
        {
            Harmony harmony = new(ModManifest.UniqueID);
            harmony.Patch(AccessTools.Method(typeof(Game1), nameof(Game1.UpdateGameClock)),
                transpiler: new HarmonyMethod(typeof(GameClockPatch), nameof(GameClockPatch.Transpile)));
            ready = true;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Clock integration unavailable; original timing retained: {ex}", LogLevel.Error);
        }

        bool patchReady = ready;
        helper.Events.GameLoop.GameLaunched += (_, _) =>
        {
            controller = new TimeController(GameRunner.instance, Helper, ModManifest, Monitor, patchReady);
            Game1.game1.Components.Add(controller);
        };
        helper.Events.GameLoop.SaveLoaded += (_, _) => controller?.LoadSave();
        helper.Events.GameLoop.DayStarted += (_, _) => controller?.StartDay();
        helper.Events.GameLoop.DayEnding += (_, _) => controller?.EndDay();
        helper.Events.GameLoop.Saving += (_, _) => controller?.SaveLedger();
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => controller?.ReturnToTitle();
        helper.Events.GameLoop.UpdateTicked += (_, _) => controller?.Tick();
        helper.Events.Multiplayer.ModMessageReceived += OnMessage;
        helper.Events.Multiplayer.PeerConnected += (_, e) => controller?.PeerChanged(e.Peer.PlayerID);
        helper.Events.Multiplayer.PeerDisconnected += (_, e) => controller?.PeerChanged(e.Peer.PlayerID);
        helper.Events.Display.Rendered += OnRendered;
    }

    private void OnMessage(object? sender, ModMessageReceivedEventArgs e)
    {
        try
        {
            controller?.Receive(e);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Ignored invalid ledger message from {e.FromPlayerID}: {ex.Message}", LogLevel.Warn);
        }
    }

    private void OnRendered(object? sender, RenderedEventArgs e)
    {
        if (!Context.IsWorldReady || Game1.newDay || Game1.showingEndOfNightStuff
            || controller?.LocalAccount is not AccountView account)
        {
            return;
        }

        bool paused = controller.LocalPaused ?? account.Paused;
        float textWidth = Game1.smallFont.MeasureString(account.Minutes).X;
        float scale = Math.Min(1f, Math.Max(0, Game1.uiViewport.Width - 24) / textWidth);
        e.SpriteBatch.DrawString(Game1.smallFont, account.Minutes, new Vector2(12, 12),
            paused ? Color.Red : Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
    }

    private TimeController? controller;
}
