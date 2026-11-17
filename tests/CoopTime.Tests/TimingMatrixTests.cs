using CoopTime.Core;
using Xunit;

namespace CoopTime.Tests;

public sealed class TimingMatrixTests
{
    [Fact]
    public void All256SinglePlayerStateCombinationsObeyNativeAndOuterUpdateGates()
    {
        for (int mask = 0; mask < 256; mask++)
        {
            bool nativePass = (mask & 1) != 0;
            bool menu = (mask & 2) != 0;
            bool minigame = (mask & 4) != 0;
            bool viewing = (mask & 8) != 0;
            bool pauseUnfocused = (mask & 16) != 0;
            bool activeWindow = (mask & 32) != 0;
            bool sleep = (mask & 64) != 0;
            bool minigameMainUpdates = (mask & 128) != 0;

            // Model the original update gates in execution order, not the mod's combined predicate.
            bool clockReached = activeWindow || !pauseUnfocused;
            if (minigame && !minigameMainUpdates)
            {
                clockReached = false;
            }

            if (!viewing && (menu || minigame))
            {
                clockReached = false;
            }

            bool shouldPause = !(clockReached && nativePass) || sleep;
            Assert.Equal(shouldPause, SinglePlayerTiming.IsPaused(new(nativePass, menu, minigame,
                viewing, pauseUnfocused, activeWindow, sleep, minigameMainUpdates)));
        }
    }
}
