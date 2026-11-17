using CoopTime.Core;
using Xunit;

namespace CoopTime.Tests;

public sealed class LedgerMatrixTests
{
    [Fact]
    public void EveryPauseMaskForTwoThreeAndEightPlayersMatchesPolicyTable()
    {
        foreach (int count in new[] { 2, 3, 8 })
        {
            foreach (int pattern in Enumerable.Range(0, 3))
            {
                decimal[] balances = Enumerable.Range(0, count)
                    .Select(i => pattern == 0 ? 0m : pattern == 1 ? (i % 2) * 3m : i + 1m).ToArray();
                for (int mask = 0; mask < (1 << count); mask++)
                {
                    foreach (decimal elapsed in new[] { 0m, 0.5m, 20m })
                    {
                        Ledger ledger = WithBalances(balances);
                        int[] active = Enumerable.Range(0, count).Where(i => (mask & (1 << i)) == 0).ToArray();
                        foreach (int i in Enumerable.Range(0, count))
                        {
                            ledger.SetPaused(i + 1, (mask & (1 << i)) != 0);
                        }

                        decimal world = 0;
                        decimal repayment = 0;
                        if (active.Length > 0)
                        {
                            repayment = active.Length == count ? 0 : Math.Min(elapsed, active.Min(i => balances[i]));
                            world = elapsed - repayment;
                        }

                        ClockStep step = ledger.Advance(elapsed);
                        Assert.Equal((GameMinutes)world, step.WorldMinutes);
                        Assert.Equal((GameMinutes)repayment, step.RepaidMinutes);
                        for (int i = 0; i < count; i++)
                        {
                            decimal expected = balances[i] + (active.Contains(i) ? -repayment : world);
                            Assert.Equal((GameMinutes)expected, ledger.Accounts.Single(a => a.PlayerId == i + 1).Minutes);
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void ArithmeticCoversDefaultNormalizationLargeValuesAndNegativeFloors()
    {
        Assert.Equal((GameMinutes)0, default(GameMinutes));
        Assert.Equal(new GameMinutes(1, 3), new GameMinutes(10, 30));
        Assert.True(new GameMinutes(1, 3) == new GameMinutes(10, 30));
        Assert.Equal(new GameMinutes(1, 3).GetHashCode(), new GameMinutes(10, 30).GetHashCode());
        Assert.Equal(default(GameMinutes).GetHashCode(), ((GameMinutes)0).GetHashCode());
        Assert.True(((GameMinutes)2).Equals((object)(GameMinutes)2));
        Assert.False(((GameMinutes)2).Equals("2"));
        Assert.Equal("-1/2", ((GameMinutes)(-0.5m)).ToString());
        Assert.Equal(-1, (int)new GameMinutes(-1, 2).Floor());
        Assert.Equal(-2, (int)new GameMinutes(-2, 1).Floor());
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameMinutes(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameMinutes(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Ledger().Advance(-1));
        GameMinutes large = decimal.MaxValue;
        Assert.Equal(large, (large / 7) * 7);
        Assert.True(large > 1);
        Assert.True(large != 1);
        Assert.True((GameMinutes)1 < large);
        Assert.True((GameMinutes)1 <= large);
        Assert.True(large >= 1);
    }

    [Fact]
    public void RenamingAndRemovingUnknownPlayersDoNotChangeCredit()
    {
        Ledger ledger = WithBalances(new[] { 3m, 4m });
        ledger.SetPaused(999, true);
        ledger.SetPlayers(new[] { (1L, "Renamed") });
        Assert.Equal("Renamed", ledger.Accounts.Single(a => a.PlayerId == 1).Name);
        Assert.Equal((GameMinutes)3, ledger.Accounts.Single(a => a.PlayerId == 1).Minutes);
        Assert.Equal((GameMinutes)4, ledger.Accounts.Single(a => a.PlayerId == 2).Minutes);
    }

    private static Ledger WithBalances(decimal[] balances)
    {
        Ledger ledger = new();
        long banker = balances.Length + 1;
        ledger.SetPlayers(Enumerable.Range(1, balances.Length + 1).Select(i => ((long)i, $"P{i}")));
        for (int i = 0; i < balances.Length; i++)
        {
            ledger.SetPaused(i + 1, true);
            ledger.Advance(balances[i]);
            ledger.SetPaused(i + 1, false);
        }

        ledger.SetPlayers(Enumerable.Range(1, balances.Length).Select(i => ((long)i, $"P{i}")));
        Assert.False(ledger.Accounts.Single(a => a.PlayerId == banker).Online);
        for (int i = 0; i < balances.Length; i++)
        {
            Assert.Equal((GameMinutes)balances[i], ledger.Accounts.Single(a => a.PlayerId == i + 1).Minutes);
        }

        return ledger;
    }
}
