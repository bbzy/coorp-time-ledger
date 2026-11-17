using CoopTime.Core;
using Xunit;

namespace CoopTime.Tests;

public sealed class LedgerTests
{
    [Fact]
    public void AccrualAndPartialRepaymentArePrecise()
    {
        Ledger ledger = TwoPlayers();
        ledger.SetPaused(1, true);
        AssertStep(ledger.Advance(20), 20, 0, ClockMode.Running);
        ledger.SetPaused(1, false);
        ledger.SetPaused(2, true);
        AssertStep(ledger.Advance(7.5m), 0, 7.5m, ClockMode.Repaying);
        Assert.Equal((GameMinutes)12.5m, Balance(ledger, 1));
        Assert.Equal((GameMinutes)0, Balance(ledger, 2));
    }

    [Fact]
    public void RepaymentSplitsFrameAtZeroAndAccruesOnlyRemainder()
    {
        Ledger ledger = TwoPlayers();
        ledger.SetPaused(1, true);
        ledger.Advance(2);
        ledger.SetPaused(1, false);
        ledger.SetPaused(2, true);
        AssertStep(ledger.Advance(3), 1, 2, ClockMode.Running);
        Assert.Equal((GameMinutes)0, Balance(ledger, 1));
        Assert.Equal((GameMinutes)1, Balance(ledger, 2));
    }

    [Fact]
    public void EveryonePausedDoesNotChangeBalances()
    {
        Ledger ledger = TwoPlayers();
        ledger.SetPaused(1, true);
        ledger.Advance(4);
        ledger.SetPaused(2, true);
        AssertStep(ledger.Advance(100), 0, 0, ClockMode.EveryonePaused);
        Assert.Equal((GameMinutes)4, Balance(ledger, 1));
        Assert.Equal((GameMinutes)0, Balance(ledger, 2));
    }

    [Fact]
    public void NoPauseMeansNoRepayment()
    {
        Ledger ledger = ThreePlayersWithUnequalCredit();
        ledger.SetPaused(3, false);
        Account[] before = ledger.Accounts.ToArray();
        AssertStep(ledger.Advance(50), 50, 0, ClockMode.Running);
        Assert.Equal(before, ledger.Accounts);
    }

    [Fact]
    public void SmallestActiveBalanceDeterminesRepaymentBoundary()
    {
        Ledger ledger = ThreePlayersWithUnequalCredit();
        AssertStep(ledger.Advance(8), 3, 5, ClockMode.Running);
        Assert.Equal((GameMinutes)0, Balance(ledger, 1));
        Assert.Equal((GameMinutes)7, Balance(ledger, 2));
        Assert.Equal((GameMinutes)3, Balance(ledger, 3));
    }

    [Fact]
    public void NewActivePlayerWithZeroCreditPreventsFreeze()
    {
        Ledger ledger = ThreePlayersWithUnequalCredit();
        ledger.SetPlayers(new[] { (1L, "A"), (2L, "B"), (3L, "C"), (4L, "D") });
        AssertStep(ledger.Advance(4), 4, 0, ClockMode.Running);
        Assert.Equal((GameMinutes)4, Balance(ledger, 3));
    }

    [Fact]
    public void DisconnectPreservesBalanceWithoutOfflineAccrual()
    {
        Ledger ledger = TwoPlayers();
        ledger.SetPaused(1, true);
        ledger.Advance(6);
        ledger.SetPlayers(new[] { (2L, "B") });
        ledger.Advance(100);
        Assert.Equal((GameMinutes)6, Balance(ledger, 1));
        Assert.False(ledger.Accounts.Single(a => a.PlayerId == 1).Online);

        ledger.SetPlayers(new[] { (1L, "A"), (2L, "B") });
        Assert.Equal((GameMinutes)6, Balance(ledger, 1));
        Assert.True(ledger.Accounts.Single(a => a.PlayerId == 1).Online);
    }

    [Fact]
    public void ExplicitResetClearsAllBalances()
    {
        Ledger ledger = ThreePlayersWithUnequalCredit();
        ledger.Reset();
        Assert.Empty(ledger.Accounts);
        ledger.SetPlayers(new[] { (1L, "A"), (2L, "B") });
        Assert.All(ledger.Accounts, a => Assert.Equal((GameMinutes)0, a.Minutes));
    }

    [Fact]
    public void SinglePlayerKeepsVanillaTimeWithoutAccrual()
    {
        Ledger ledger = new();
        ledger.SetPlayers(new[] { (1L, "A") });
        ledger.SetPaused(1, true);
        AssertStep(ledger.Advance(5), 5, 0, ClockMode.Running);
        Assert.Equal((GameMinutes)0, Balance(ledger, 1));
    }

    [Fact]
    public void ThousandsOfShortPausesAcrossCadencesHaveNoRoundingDrift()
    {
        Ledger ledger = TwoPlayers();
        ledger.SetPaused(1, true);
        for (int i = 0; i < 6300; i++)
        {
            ledger.Advance(ClockMath.ToMinutes(1, 7000));
            ledger.Advance(ClockMath.ToMinutes(1, 9000));
        }

        Assert.Equal((GameMinutes)16, Balance(ledger, 1));
        ledger.SetPaused(1, false);
        ledger.SetPaused(2, true);
        for (int i = 0; i < 11200; i++)
        {
            ledger.Advance(ClockMath.ToMinutes(1, 7000));
        }

        Assert.Equal((GameMinutes)0, Balance(ledger, 1));
        Assert.Equal(ClockMode.Running, ledger.Mode);
    }

    [Fact]
    public void StateChangesDuringRepaymentKeepExistingCredit()
    {
        Ledger ledger = TwoPlayers();
        ledger.SetPaused(1, true);
        ledger.Advance(10);
        ledger.SetPaused(1, false);
        ledger.SetPaused(2, true);
        ledger.Advance(4);
        ledger.SetPaused(1, true);
        ledger.Advance(100);
        ledger.SetPaused(2, false);
        ledger.Advance(3);
        Assert.Equal((GameMinutes)9, Balance(ledger, 1));
        Assert.Equal((GameMinutes)0, Balance(ledger, 2));
    }

    [Fact]
    public void SplittingElapsedTimeIntoFramesDoesNotChangeOutcome()
    {
        Ledger largeSteps = TwoPlayers();
        Ledger smallSteps = TwoPlayers();
        largeSteps.SetPaused(1, true);
        smallSteps.SetPaused(1, true);
        largeSteps.Advance(ClockMath.ToMinutes(1234, 9000));
        for (int i = 0; i < 1234; i++)
        {
            smallSteps.Advance(ClockMath.ToMinutes(1, 9000));
        }

        foreach (Ledger ledger in new[] { largeSteps, smallSteps })
        {
            ledger.SetPaused(1, false);
            ledger.SetPaused(2, true);
        }

        ClockStep large = largeSteps.Advance(ClockMath.ToMinutes(2000, 7000));
        GameMinutes smallWorld = 0;
        for (int i = 0; i < 2000; i++)
        {
            smallWorld += smallSteps.Advance(ClockMath.ToMinutes(1, 7000)).WorldMinutes;
        }

        Assert.Equal(large.WorldMinutes, smallWorld);
        Assert.Equal(largeSteps.Accounts, smallSteps.Accounts);
    }

    [Fact]
    public void SeededEightPlayerTraceConservesTimeAndNeverCreatesNegativeDebt()
    {
        Ledger ledger = new();
        ledger.SetPlayers(Enumerable.Range(1, 8).Select(i => ((long)i, $"P{i}")));
        Random random = new(20261117);
        for (int frame = 0; frame < 10000; frame++)
        {
            ledger.SetPaused(random.Next(1, 9), random.Next(2) == 0);
            GameMinutes dt = ClockMath.ToMinutes(random.Next(1, 34), random.Next(2) == 0 ? 7000 : 9000);
            Account[] before = ledger.Accounts.ToArray();
            ClockStep step = ledger.Advance(dt);
            Assert.True(step.WorldMinutes >= 0 && step.WorldMinutes <= dt);
            Assert.True(step.RepaidMinutes >= 0 && step.RepaidMinutes <= dt);
            Assert.Equal(before.All(p => p.Paused) ? (GameMinutes)0 : dt,
                step.WorldMinutes + step.RepaidMinutes);
            foreach (Account after in ledger.Accounts)
            {
                Account previous = before.Single(p => p.PlayerId == after.PlayerId);
                Assert.True(after.Minutes >= 0);
                Assert.Equal(previous.Minutes + (previous.Paused ? step.WorldMinutes : 0)
                    - (!previous.Paused ? step.RepaidMinutes : 0), after.Minutes);
            }
        }
    }

    private static Ledger TwoPlayers()
    {
        Ledger ledger = new();
        ledger.SetPlayers(new[] { (1L, "A"), (2L, "B") });
        return ledger;
    }

    private static Ledger ThreePlayersWithUnequalCredit()
    {
        Ledger ledger = TwoPlayers();
        ledger.SetPaused(1, true);
        ledger.Advance(5);
        ledger.SetPaused(1, false);
        ledger.SetPaused(2, true);
        ledger.SetPlayers(new[] { (1L, "A"), (2L, "B"), (3L, "C") });
        ledger.Advance(12);
        ledger.SetPaused(2, false);
        ledger.SetPaused(3, true);
        return ledger;
    }

    private static GameMinutes Balance(Ledger ledger, long id) => ledger.Accounts.Single(a => a.PlayerId == id).Minutes;

    private static void AssertStep(ClockStep step, GameMinutes world, GameMinutes repaid, ClockMode mode)
    {
        Assert.Equal(world, step.WorldMinutes);
        Assert.Equal(repaid, step.RepaidMinutes);
        Assert.Equal(mode, step.Mode);
    }
}
