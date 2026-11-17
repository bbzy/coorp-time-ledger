using CoopTime.Core;
using Xunit;

namespace CoopTime.Tests;

public sealed class ClientSessionTests
{
    [Fact]
    public void NoSessionMeansNoOutgoingStateAndNoUsableView()
    {
        ClientSession client = new();
        Assert.Null(client.StateIfDue(true, 0));
        Assert.True(client.ViewExpired(0));
        Assert.False(client.AcceptSnapshot(null, 1, 1, 0));
        Assert.Null(client.View);
    }

    [Fact]
    public void StateChangesSendImmediatelyAndHeartbeatUsesOneSecondBoundary()
    {
        ClientSession client = Ready();
        Assert.Equal(1, client.StateIfDue(false, 0)?.Sequence);
        Assert.Null(client.StateIfDue(false, 999));
        Assert.Equal(2, client.StateIfDue(false, 1000)?.Sequence);
        Assert.True(client.StateIfDue(true, 1001)?.Paused);
        Assert.False(client.StateIfDue(false, 1002)?.Paused);
        Assert.Null(client.StateIfDue(false, 1003));
    }

    [Fact]
    public void LeaseExpiryDoesNotPreventSendingRecoveryHeartbeat()
    {
        ClientSession client = Ready();
        client.StateIfDue(false, 0);
        Assert.False(client.ViewExpired(5000));
        Assert.True(client.ViewExpired(5001));
        Assert.NotNull(client.StateIfDue(false, 5001));
        Assert.True(client.AcceptSnapshot(Snapshot("day1", 2), 1, 1, 5002));
        Assert.False(client.ViewExpired(5002));
        Assert.True(client.ViewExpired(5001));
    }

    [Fact]
    public void SameSessionSnapshotsDoNotRestartOutgoingSequence()
    {
        ClientSession client = Ready();
        client.StateIfDue(false, 0);
        Assert.True(client.AcceptSnapshot(Snapshot("day1", 2), 1, 1, 1));
        Assert.Null(client.StateIfDue(false, 1));
        Assert.Equal(2, client.StateIfDue(true, 2)?.Sequence);
        Assert.False(client.AcceptSnapshot(Snapshot("day1", 1), 1, 1, 3));
    }

    [Fact]
    public void ReconnectChangesTokenAndRestartsSequenceWithoutDiscardingTheDay()
    {
        ClientSession client = new(2);
        LedgerSnapshot initial = Snapshot("day1", 1) with
        {
            Accounts = new[] { new AccountView(2, "B", true, false, "10", "connection-old") }
        };
        Assert.True(client.AcceptSnapshot(initial, 1, 1, 0));
        Assert.Equal("connection-old", client.StateIfDue(false, 0)?.Connection);
        Assert.Equal(2, client.StateIfDue(true, 1)?.Sequence);
        LedgerSnapshot reconnected = initial with
        {
            Sequence = 2,
            Accounts = new[] { new AccountView(2, "B", true, false, "10", "connection-new") }
        };
        Assert.True(client.AcceptSnapshot(reconnected, 1, 1, 2));
        PlayerState? fresh = client.StateIfDue(true, 2);
        Assert.Equal(1, fresh?.Sequence);
        Assert.Equal("connection-new", fresh?.Connection);
        Assert.Equal("day1", fresh?.Session);
        Assert.Equal("10", client.View?.Accounts[0].Minutes);
        client.Reset(3);
        Assert.True(client.AcceptSnapshot(reconnected with
        {
            Accounts = new[] { new AccountView(3, "C", true, false, "0", "connection-third-player") }
        }, 1, 1, 3));
        Assert.Equal("connection-third-player", client.StateIfDue(false, 3)?.Connection);
    }

    [Fact]
    public void NewDayOrResetRequiresImmediateStateAndRetiresOldDay()
    {
        ClientSession client = Ready();
        client.StateIfDue(false, 0);
        Assert.True(client.AcceptSnapshot(Snapshot("day2", 1) with { Day = 2 }, 1, 1, 1));
        PlayerState? state = client.StateIfDue(false, 1);
        Assert.Equal("day2", state?.Session);
        Assert.Equal(1, state?.Sequence);
        Assert.False(client.AcceptSnapshot(Snapshot("unseen-old-day", 100), 1, 1, 2));
        client.Reset();
        Assert.Null(client.View);
        Assert.Null(client.StateIfDue(true, 3));
        Assert.True(client.AcceptSnapshot(Snapshot("day3", 1) with { Day = 3 }, 1, 1, 4));
        Assert.Equal(1, client.StateIfDue(true, 4)?.Sequence);
    }

    [Fact]
    public void ForeignAccountsAndMalformedPrivateSnapshotsCannotReplaceCurrentView()
    {
        ClientSession client = new(2);
        AccountView own = new(2, "B", true, false, "00:10", "connection-b");
        AccountView foreign = new(3, "C", true, false, "05:30", "connection-c");
        LedgerSnapshot valid = Snapshot("day1", 1) with { Accounts = new[] { own } };
        Assert.True(client.AcceptSnapshot(valid, 1, 1, 0));

        LedgerSnapshot update = valid with { Sequence = 2 };
        foreach (AccountView[] accounts in new[]
        {
            new[] { foreign }, new[] { own, foreign }, new[] { own, own }, new AccountView[] { null! }, null!
        })
        {
            Assert.False(client.AcceptSnapshot(update with { Accounts = accounts }, 1, 1, 1));
            Assert.Same(valid, client.View);
        }

        Assert.False(client.AcceptSnapshot(update with { Protocol = 2 }, 1, 1, 1));
        Assert.True(client.AcceptSnapshot(update, 1, 1, 1));
        Assert.Equal("connection-b", client.StateIfDue(false, 1)?.Connection);
    }

    private static ClientSession Ready()
    {
        ClientSession client = new();
        Assert.True(client.AcceptSnapshot(Snapshot("day1", 1), 1, 1, 0));
        return client;
    }

    private static LedgerSnapshot Snapshot(string session, long sequence) =>
        new(PeerStateTracker.Version, session, sequence, 1, ClockMode.Running, "", Array.Empty<AccountView>(), 1);
}
