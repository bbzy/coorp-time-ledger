using System.Reflection;
using CoopTime.Core;
using HarmonyLib;
using StardewValley;
using StardewValley.Locations;

namespace CoopTime.Mod;

internal static class GameClockPatch
{
    public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        FieldInfo interval = AccessTools.Field(typeof(Game1), nameof(Game1.gameTimeInterval));
        MethodInfo extra = AccessTools.PropertyGetter(typeof(GameLocation), nameof(GameLocation.ExtraMillisecondsPerInGameMinute));
        return GameClockRewriter.Rewrite(instructions, interval, extra,
            AccessTools.Method(typeof(GameClockPatch), nameof(AdjustInterval)),
            AccessTools.Method(typeof(GameClockPatch), nameof(GetExtraMilliseconds)));
    }

    public static int AdjustInterval(int interval, int elapsed)
    {
        TimeController? controller = FindController();
        return interval + (controller?.AdjustElapsed(elapsed) ?? elapsed);
    }

    public static int GetExtraMilliseconds(GameLocation location)
    {
        int original = location.ExtraMillisecondsPerInGameMinute;
        if (FindController()?.CanControlClock != true)
        {
            return original;
        }

        return SinglePlayerTiming.ExtraMilliseconds(original, location is MineShaft mine && mine.getMineArea() == 121);
    }

    private static TimeController? FindController()
    {
        if (Game1.game1 == null)
        {
            return null;
        }

        foreach (Microsoft.Xna.Framework.IGameComponent component in Game1.game1.Components)
        {
            if (component is TimeController controller)
            {
                return controller;
            }
        }

        return null;
    }
}
