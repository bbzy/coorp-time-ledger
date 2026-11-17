using CoopTime.Core;
using Xunit;

namespace CoopTime.Tests;

public sealed class ProtocolBoundaryTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("protocol")]
    [InlineData("negative-sequence")]
    [InlineData("empty-session")]
    [InlineData("wrong-session")]
    [InlineData("negative-clock")]
    public void InvalidPlayerStateDoesNotOverwriteOrRenewValidState(string reason)
    {
        PeerStateTracker peers = new();
        peers.Reset("day1");
        PlayerState valid = new(PeerStateTracker.Version, "day1", 1, true);
        Assert.True(peers.Accept(2, valid, 10));
        PlayerState? bad = reason switch
        {
            "null" => null,
            "protocol" => valid with { Protocol = 99, Sequence = 2 },
            "negative-sequence" => valid with { Sequence = -1 },
            "empty-session" => valid with { Session = "" },
            "wrong-session" => valid with { Session = "day2" },
            _ => valid with { Sequence = 2 }
        };
        Assert.False(peers.Accept(2, bad, reason == "negative-clock" ? -1 : 4000));
        Assert.True(peers.TryGet(2, 5010, out bool paused));
        Assert.True(paused);
        Assert.False(peers.TryGet(2, 5011, out _));
        Assert.False(peers.TryGet(2, 9, out _));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("protocol")]
    [InlineData("host-id")]
    [InlineData("negative-sequence")]
    [InlineData("empty-session")]
    [InlineData("null-notice")]
    [InlineData("null-accounts")]
    [InlineData("null-account")]
    [InlineData("null-name")]
    [InlineData("null-minutes")]
    [InlineData("null-connection")]
    [InlineData("unknown-mode")]
    [InlineData("old-unseen-day")]
    [InlineData("negative-clock")]
    public void InvalidSnapshotCannotPoisonClientViewOrHeartbeat(string reason)
    {
        SnapshotReceiver receiver = new();
        LedgerSnapshot current = new(PeerStateTracker.Version, "day2", 1, 1,
            ClockMode.Running, "", Array.Empty<AccountView>(), 2);
        Assert.True(receiver.Accept(current, 1, 1, 10));
        LedgerSnapshot? bad = reason switch
        {
            "null" => null,
            "protocol" => current with { Protocol = 99 },
            "host-id" => current with { HostId = 2 },
            "negative-sequence" => current with { Sequence = -1 },
            "empty-session" => current with { Session = "" },
            "null-notice" => current with { Notice = null! },
            "null-accounts" => current with { Accounts = null! },
            "null-account" => current with { Accounts = new AccountView[] { null! } },
            "null-name" => current with { Accounts = new[] { new AccountView(2, null!, true, false, "0") } },
            "null-minutes" => current with { Accounts = new[] { new AccountView(2, "B", true, false, null!) } },
            "null-connection" => current with { Accounts = new[] { new AccountView(2, "B", true, false, "0", null!) } },
            "unknown-mode" => current with { Mode = (ClockMode)99 },
            "old-unseen-day" => current with { Day = 1, Session = "never-seen-day1", Sequence = 999 },
            _ => current with { Sequence = 2 }
        };
        Assert.False(receiver.Accept(bad, 1, 1, reason == "negative-clock" ? -1 : 1000));
        Assert.Same(current, receiver.Current);
        Assert.Equal(10, receiver.ReceivedAt);
        receiver.Reset();
        Assert.Null(receiver.Current);
        Assert.Equal(0, receiver.ReceivedAt);
    }

    [Fact]
    public void UninitializedTrackerCannotAcceptEmptySessionOrUnknownPlayers()
    {
        PeerStateTracker peers = new();
        Assert.False(peers.Accept(2, new(PeerStateTracker.Version, "", 1, true), 0));
        Assert.False(peers.TryGet(2, 0, out _));
        peers.Remove(999);
    }
}
