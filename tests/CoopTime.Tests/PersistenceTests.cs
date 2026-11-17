using CoopTime.Core;
using Newtonsoft.Json;
using Xunit;

namespace CoopTime.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public void DebtSurvivesMultipleDaysIncludingOfflinePlayersAndSinglePlayerDays()
    {
        HostSession host = Ready();
        host.AdjustElapsed(123, 7000, true);
        GameMinutes debt = Balance(host, 1);
        string oldConnection = host.ConnectionFor(2);
        PlayerState old = new(PeerStateTracker.Version, host.Session, 100, true, oldConnection);
        host.Suspend();
        host.StartDay(1, "day2", 2);
        Assert.All(host.Accounts, a =>
        {
            Assert.False(a.Online);
            Assert.False(a.Paused);
        });
        host.Refresh(Frame() with { Players = new PlayerPresence[] { new(1, "A") } }, 1);
        Assert.Equal(debt, Balance(host, 1));
        Assert.Equal(700, host.AdjustElapsed(700, 7000, true));
        host.StartDay(1, "day3", 3);
        host.Refresh(Frame(), 2);
        Assert.Equal("syncing", host.Notice);
        Assert.False(host.AcceptState(2, old, 2));
        Assert.NotEqual(oldConnection, host.ConnectionFor(2));
        Assert.Equal(debt, Balance(host, 1));
        Assert.Equal("00:00", host.CreateSnapshot(2).Accounts.Single(a => a.PlayerId == 1).Minutes);
    }

    [Fact]
    public void OfflineDebtSurvivesSeveralDaysAndCanBeRepaidAfterRejoin()
    {
        HostSession host = new();
        Assert.True(host.TryRestoreSave(new LedgerSaveData(1,
            new[] { new SavedAccount(1, "A", "0/1"), new SavedAccount(2, "B", "2/7") }, "0/1"), out _));
        for (uint day = 2; day <= 4; day++)
        {
            host.StartDay(1, $"day{day}", day);
            host.Refresh(Frame(false) with { Players = new PlayerPresence[] { new(1, "A") } }, 0);
            Assert.Equal(new GameMinutes(2, 7), Balance(host, 2));
            Assert.False(host.Accounts.Single(a => a.PlayerId == 2).Online);
            Assert.Equal(16, host.AdjustElapsed(16, 7000, true));
        }

        StartReady(host, true, "day5", 5);
        Assert.Equal(0, host.AdjustElapsed(16, 7000, true));
        Assert.Equal(new GameMinutes(46, 175), Balance(host, 2));
    }

    [Fact]
    public void JsonReloadRetainsExactFractionsCarryAndCanRepayOnTheNextDay()
    {
        HostSession host = new();
        LedgerPersistence storage = new(host);
        Assert.True(storage.Load(() => new LedgerSaveData(1,
            new[] { new SavedAccount(1, "A", "1/900") }, "0/1"), out _));
        StartReady(host, true);
        Assert.True(host.AcceptState(2, State(host, true, 2), 1));
        host.Refresh(Frame(false), 1);
        Assert.Equal(0, host.AdjustElapsed(1, 7000, true));
        LedgerSaveData expected = host.ExportSave();
        Assert.Equal("2/9", expected.MillisecondRemainder);
        Assert.Equal("1/3150", expected.Accounts.Single(a => a.PlayerId == 2).Minutes);

        string? json = null;
        host.Suspend();
        Assert.True(storage.Save(data => json = JsonConvert.SerializeObject(data), out _));
        HostSession reloaded = new();
        LedgerPersistence restored = new(reloaded);
        Assert.True(restored.Load(() => JsonConvert.DeserializeObject<LedgerSaveData>(json!), out _));
        Assert.Equal(JsonConvert.SerializeObject(expected), JsonConvert.SerializeObject(reloaded.ExportSave()));
        StartReady(reloaded, true, "day2", 2);
        Assert.Equal(0, reloaded.AdjustElapsed(1, 9000, true));
        Assert.True(reloaded.Accounts.All(a => a.Minutes >= 0));
        Assert.NotEqual("0/1", reloaded.ExportSave().MillisecondRemainder);
    }

    [Fact]
    public void NewSaveAndRepeatedLoadDoNotLeakOrDuplicateAnotherFarmsBalances()
    {
        Dictionary<string, string> slots = new();
        HostSession host = new();
        LedgerPersistence storage = new(host);
        Assert.True(storage.Load(() => null, out _));
        StartReady(host, true);
        host.AdjustElapsed(700, 7000, true);
        Assert.True(storage.Save(data => slots["farm-a"] = JsonConvert.SerializeObject(data), out _));

        Assert.True(storage.Load(() => null, out _));
        Assert.Empty(host.Accounts);
        StartReady(host, true, "farm-b-day1", 1);
        host.AdjustElapsed(1400, 7000, true);
        Assert.True(storage.Save(data => slots["farm-b"] = JsonConvert.SerializeObject(data), out _));
        for (int i = 0; i < 3; i++)
        {
            Assert.True(storage.Load(() => JsonConvert.DeserializeObject<LedgerSaveData>(slots["farm-a"]), out _));
            Assert.Equal((GameMinutes)1, Balance(host, 1));
            StartReady(host, true);
            Assert.Equal((GameMinutes)1, Balance(host, 1));
        }

        Assert.True(storage.Load(() => JsonConvert.DeserializeObject<LedgerSaveData>(slots["farm-b"]), out _));
        Assert.Equal((GameMinutes)2, Balance(host, 1));
    }

    [Fact]
    public void ExitingWithoutGameSaveRollsBackToLastSavedBalance()
    {
        HostSession host = new();
        LedgerPersistence storage = new(host);
        storage.Load(() => null, out _);
        StartReady(host, true);
        host.AdjustElapsed(700, 7000, true);
        string saved = "";
        storage.Save(data => saved = JsonConvert.SerializeObject(data), out _);
        host.AdjustElapsed(700, 7000, true);
        Assert.Equal((GameMinutes)2, Balance(host, 1));
        host.Reset();
        storage.Reset();
        Assert.False(storage.Ready);
        storage.Load(() => JsonConvert.DeserializeObject<LedgerSaveData>(saved), out _);
        Assert.Equal((GameMinutes)1, Balance(host, 1));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("accounts")]
    [InlineData("carry-text")]
    [InlineData("carry-negative")]
    [InlineData("carry-one")]
    [InlineData("null-account")]
    [InlineData("null-name")]
    [InlineData("minutes-text")]
    [InlineData("minutes-negative")]
    [InlineData("duplicate-player")]
    public void InvalidSaveIsRejectedAtomicallyAndCannotOverwriteOriginal(string kind)
    {
        HostSession host = Ready();
        host.AdjustElapsed(700, 7000, true);
        LedgerSaveData before = host.ExportSave();
        LedgerSaveData bad = Invalid(kind);
        Assert.False(host.TryRestoreSave(bad, out string error));
        Assert.Contains("ledger.", error);
        Assert.DoesNotContain("private-invalid-value", error);
        Assert.Equal(JsonConvert.SerializeObject(before), JsonConvert.SerializeObject(host.ExportSave()));

        LedgerPersistence storage = new(host);
        Assert.False(storage.Load(() => bad, out _));
        Assert.False(storage.Ready);
        Assert.Empty(host.Accounts);
        bool called = false;
        Assert.False(storage.Save(_ => called = true, out string refused));
        Assert.False(called);
        Assert.Contains("not be replaced", refused);
    }

    [Fact]
    public void ReadOrWriteFailuresDisablePersistenceAndRetainInMemoryDebtOnWriteFailure()
    {
        HostSession host = Ready();
        LedgerPersistence storage = new(host);
        Assert.False(storage.Load(() => throw new IOException("private-invalid-value"), out string readError));
        Assert.Contains("IOException", readError);
        Assert.DoesNotContain("private-invalid-value", readError);
        Assert.False(storage.Ready);
        Assert.Empty(host.Accounts);
        Assert.True(storage.Load(() => null, out _));
        StartReady(host, true);
        host.AdjustElapsed(700, 7000, true);
        Assert.False(storage.Save(_ => throw new IOException("private-invalid-value"), out string writeError));
        Assert.Contains("IOException", writeError);
        Assert.False(storage.Ready);
        Assert.Equal((GameMinutes)1, Balance(host, 1));
        host.Refresh(Frame() with { StorageAvailable = storage.Ready }, 1);
        Assert.Equal("storage-error", host.Notice);
        Assert.False(host.CanControlClock);
    }

    [Fact]
    public void StorageFailureWinsOverOtherProblemsWithoutAccrual()
    {
        HostSession host = Ready();
        host.Refresh(Frame() with { StorageAvailable = false, IntegrationAvailable = false, GlobalPause = true }, 0);
        Assert.Equal("storage-error", host.Notice);
        Assert.Equal(700, host.AdjustElapsed(700, 7000, true));
        Assert.Equal((GameMinutes)0, Balance(host, 1));
    }

    [Fact]
    public void EmptyAndHugeExactBalancesSerializeWithoutNumericRounding()
    {
        HostSession host = new();
        Assert.True(host.TryRestoreSave(null, out _));
        Assert.True(host.TryRestoreSave(host.ExportSave(), out _));
        LedgerSaveData large = new(1,
            new[] { new SavedAccount(long.MinValue, "\u8fdc\u7a0b\u519c\u592b", "700000000000000000000000000000000000001/700") }, "1/6300");
        Assert.True(host.TryRestoreSave(JsonConvert.DeserializeObject<LedgerSaveData>(JsonConvert.SerializeObject(large)), out _));
        Assert.Equal(large.Accounts, host.ExportSave().Accounts);
        Assert.Equal(large.MillisecondRemainder, host.ExportSave().MillisecondRemainder);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1/2/3")]
    [InlineData("x/2")]
    [InlineData("1/x")]
    [InlineData("1/0")]
    [InlineData("1/-2")]
    public void InvalidExactDurationsReturnFailureWithoutThrowing(string? value)
    {
        Assert.False(GameMinutes.TryParse(value, out GameMinutes parsed));
        Assert.Equal(default, parsed);
    }

    [Theory]
    [InlineData("0/1")]
    [InlineData("-1/2")]
    [InlineData("1/6300")]
    public void ExactDurationParsingRoundTripsCanonicalValues(string value)
    {
        Assert.True(GameMinutes.TryParse(value, out GameMinutes parsed));
        Assert.Equal(value, parsed.ToString());
    }

    private static LedgerSaveData Invalid(string kind)
    {
        LedgerSaveData valid = new(1, new[] { new SavedAccount(1, "A", "3/7") }, "0/1");
        return kind switch
        {
            "schema" => valid with { SchemaVersion = 99 },
            "accounts" => valid with { Accounts = null! },
            "carry-text" => valid with { MillisecondRemainder = "private-invalid-value" },
            "carry-negative" => valid with { MillisecondRemainder = "-1/2" },
            "carry-one" => valid with { MillisecondRemainder = "1/1" },
            "null-account" => valid with { Accounts = new SavedAccount[] { null! } },
            "null-name" => valid with { Accounts = new[] { new SavedAccount(1, null!, "0/1") } },
            "minutes-text" => valid with { Accounts = new[] { new SavedAccount(1, "A", "private-invalid-value") } },
            "minutes-negative" => valid with { Accounts = new[] { new SavedAccount(1, "A", "-1/7") } },
            _ => valid with { Accounts = new[] { new SavedAccount(1, "A", "1/7"), new SavedAccount(1, "B", "2/7") } }
        };
    }

    private static HostSession Ready()
    {
        HostSession host = new();
        StartReady(host, true);
        return host;
    }

    private static void StartReady(HostSession host, bool localPaused, string session = "day1", uint day = 1)
    {
        host.StartDay(1, session, day);
        host.Refresh(Frame(localPaused), 0);
        Assert.True(host.AcceptState(2, State(host, false, 1), 0));
        host.Refresh(Frame(localPaused), 0);
    }

    private static HostFrame Frame(bool localPaused = true) =>
        new(true, true, false, false, false, localPaused, new PlayerPresence[] { new(1, "A"), new(2, "B") });
    private static PlayerState State(HostSession host, bool paused, long sequence) =>
        new(PeerStateTracker.Version, host.Session, sequence, paused, host.ConnectionFor(2));
    private static GameMinutes Balance(HostSession host, long id) => host.Accounts.Single(a => a.PlayerId == id).Minutes;
}
