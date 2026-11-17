namespace CoopTime.Core;

public readonly record struct SinglePlayerClockState(bool NativeShouldPass, bool HasMenu,
    bool HasMinigame, bool ViewingRemoteLocation, bool PauseWhenUnfocused, bool WindowActive,
    bool Sleeping, bool MinigameRunsMainUpdates = false);

public static class SinglePlayerTiming
{
    public static bool IsPaused(SinglePlayerClockState state)
    {
        return !state.NativeShouldPass || state.Sleeping
            || (state.HasMinigame && !state.MinigameRunsMainUpdates)
            || ((state.HasMenu || state.HasMinigame) && !state.ViewingRemoteLocation)
            || (state.PauseWhenUnfocused && !state.WindowActive);
    }

    public static int ExtraMilliseconds(int original, bool skullCavern)
    {
        return skullCavern && original == 0 ? 200 : original;
    }
}
