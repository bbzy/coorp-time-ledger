using CoopTime.Core;
using Xunit;

namespace CoopTime.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void DuplicateAndReorderedStateDoNotRenewLease()
    {
        PeerStateTracker peers = new();
        peers.Reset("day1");
        Assert.True(peers.Accept(2, new(PeerStateTracker.Version, "day1", 5, true), 100));
        Assert.False(peers.Accept(2, new(PeerStateTracker.Version, "day1", 5, false), 3000));
        Assert.False(peers.Accept(2, new(PeerStateTracker.Version, "day1", 4, false), 4000));
        Assert.True(peers.TryGet(2, 5100, out bool paused));
        Assert.True(paused);
        Assert.False(peers.TryGet(2, 5101, out _));
    }

    [Fact]
    public void PreviousDayAndWrongProtocolAreRejected()
    {
        PeerStateTracker peers = new();
        peers.Reset("day2");
        Assert.False(peers.Accept(2, new(PeerStateTracker.Version, "day1", 100, true), 0));
        Assert.False(peers.Accept(2, new(99, "day2", 1, true), 0));
        Assert.False(peers.TryGet(2, 0, out _));
    }

    [Fact]
    public void DisconnectInvalidatesStateAndReconnectCanRestartSequence()
    {
        PeerStateTracker peers = new();
        peers.Reset("day1");
        peers.Accept(2, new(PeerStateTracker.Version, "day1", 100, true), 0);
        peers.Remove(2);
        Assert.False(peers.TryGet(2, 1, out _));
        Assert.True(peers.Accept(2, new(PeerStateTracker.Version, "day1", 1, false), 2));
        Assert.True(peers.TryGet(2, 2, out bool paused));
        Assert.False(paused);
    }

    [Fact]
    public void NewDayResetInvalidatesEveryLease()
    {
        PeerStateTracker peers = new();
        peers.Reset("day1");
        peers.Accept(2, new(PeerStateTracker.Version, "day1", 1, true), 0);
        peers.Reset("day2");
        Assert.False(peers.TryGet(2, 1, out _));
        Assert.True(peers.Accept(2, new(PeerStateTracker.Version, "day2", 1, false), 2));
    }

    [Fact]
    public void ClientAcceptsOnlyHostAndRejectsRetiredSessions()
    {
        SnapshotReceiver receiver = new();
        Assert.False(receiver.Accept(Snapshot("day1", 1), 3, 1, 0));
        Assert.True(receiver.Accept(Snapshot("day1", 2), 1, 1, 1));
        Assert.False(receiver.Accept(Snapshot("day1", 1), 1, 1, 2));
        Assert.True(receiver.Accept(Snapshot("day2", 1), 1, 1, 3));
        Assert.False(receiver.Accept(Snapshot("day1", 100), 1, 1, 4));
        Assert.Equal("day2", receiver.Current?.Session);
        Assert.Equal(3, receiver.ReceivedAt);
    }

    [Fact]
    public void SnapshotSerializationRoundTripPreservesDisplayAndIdentity()
    {
        LedgerSnapshot source = Snapshot("day1", 1) with
        {
            Accounts = new[] { new AccountView(1234567890123456789, "\u519c\u592b A", true, true, "00:10") }
        };
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(source);
        LedgerSnapshot decoded = Newtonsoft.Json.JsonConvert.DeserializeObject<LedgerSnapshot>(json)!;
        Assert.Equal(source.Session, decoded.Session);
        Assert.Equal(source.Accounts, decoded.Accounts);
    }

    private static LedgerSnapshot Snapshot(string session, long sequence) =>
        new(PeerStateTracker.Version, session, sequence, 1, ClockMode.Running, "", Array.Empty<AccountView>());
}
