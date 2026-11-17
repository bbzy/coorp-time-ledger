using CoopTime.Core;
using Xunit;

namespace CoopTime.Tests;

public sealed class HostSessionTests
{
    [Fact]
    public void HandshakeIsRequiredBeforeAnyDebtOrClockControl()
    {
        HostSession host = Start();
        HostFrame frame = Frame(localPaused: true);
        host.Refresh(frame, 0);
        Assert.Equal("syncing", host.Notice);
        Assert.Equal(700, host.AdjustElapsed(700, 7000, true));
        Assert.All(host.Accounts, a => Assert.Equal((GameMinutes)0, a.Minutes));

        Assert.True(host.AcceptState(2, State(host, false), 1));
        host.Refresh(frame, 1);
        Assert.True(host.CanControlClock);
        Assert.Equal(700, host.AdjustElapsed(700, 7000, true));
        Assert.Equal((GameMinutes)1, Balance(host, 1));
    }

    [Fact]
    public void FullHostFlowAccruesRepaysAndSplitsAtExactZero()
    {
        HostSession host = Ready(localPaused: true);
        host.AdjustElapsed(1400, 7000, true);
        Assert.Equal((GameMinutes)2, Balance(host, 1));
        Assert.True(host.AcceptState(2, State(host, true, 2), 1));
        host.Refresh(Frame(localPaused: false), 1);
        Assert.Equal(ClockMode.Repaying, host.Mode);
        Assert.Equal(0, host.AdjustElapsed(1000, 7000, true));
        Assert.Equal(300, host.AdjustElapsed(700, 7000, true));
        Assert.Equal((GameMinutes)0, Balance(host, 1));
        Assert.Equal(new GameMinutes(3, 7), Balance(host, 2));
        Assert.Equal(ClockMode.Running, host.Mode);
    }

    [Fact]
    public void ExpiredPeerFailsOpenAndRecoveryDoesNotChangeExistingCredit()
    {
        HostSession host = Ready(localPaused: true);
        host.AdjustElapsed(700, 7000, true);
        host.Refresh(Frame(localPaused: true), 5001);
        Assert.Equal("syncing", host.Notice);
        Assert.Equal(700, host.AdjustElapsed(700, 7000, true));
        Assert.Equal((GameMinutes)1, Balance(host, 1));
        Assert.True(host.AcceptState(2, State(host, false, 2), 5002));
        host.Refresh(Frame(localPaused: true), 5002);
        host.AdjustElapsed(700, 7000, true);
        Assert.Equal((GameMinutes)2, Balance(host, 1));
    }

    [Theory]
    [InlineData(false, false, false, false, false, "transition")]
    [InlineData(true, false, false, false, false, "unsupported")]
    [InlineData(true, true, true, false, false, "unsupported")]
    [InlineData(true, true, false, true, false, "transition")]
    [InlineData(true, true, false, false, true, "transition")]
    public void UnavailableFramesNeverMutateDebt(bool world, bool integration, bool split,
        bool global, bool transition, string reason)
    {
        HostSession host = Ready(localPaused: true);
        host.AdjustElapsed(700, 7000, true);
        host.Refresh(Frame(true) with
        {
            WorldReady = world, IntegrationAvailable = integration, SplitScreen = split,
            GlobalPause = global, DayTransition = transition
        }, 1);
        Assert.Equal(reason, host.Notice);
        Assert.Equal(ClockMode.Unavailable, host.Mode);
        Assert.False(host.CanControlClock);
        Assert.Equal(700, host.AdjustElapsed(700, 7000, true));
        Assert.Equal((GameMinutes)1, Balance(host, 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompatiblePeerCannotBeHiddenByAnotherUnsyncedPeer(bool reverse)
    {
        HostSession host = Start();
        PlayerPresence[] roster = { new(1, "A"), new(2, "B", false), new(3, "C") };
        host.Refresh(Frame(true) with { Players = reverse ? roster.Reverse().ToArray() : roster }, 0);
        Assert.Equal("incompatible", host.Notice);
        Assert.False(host.AcceptState(2, State(host, false), 0));
        Assert.Equal(700, host.AdjustElapsed(700, 7000, true));
        Assert.All(host.Accounts, a => Assert.Equal((GameMinutes)0, a.Minutes));
    }

    [Fact]
    public void UnsupportedIntegrationReasonWinsOverPeerProblems()
    {
        HostSession host = Start();
        host.Refresh(Frame(true) with
        {
            IntegrationAvailable = false,
            Players = new PlayerPresence[] { new(1, "A"), new(2, "B", false), new(3, "C") }
        }, 0);
        Assert.Equal("unsupported", host.Notice);
    }

    [Fact]
    public void RemoteSplitScreenAndUnknownOrHostStatesAreRejected()
    {
        HostSession host = Start();
        host.Refresh(Frame() with { Players = new PlayerPresence[] { new(1, "A"), new(2, "B", true, true) } }, 0);
        Assert.Equal("incompatible", host.Notice);
        Assert.False(host.AcceptState(2, State(host, true), 0));
        Assert.False(host.AcceptState(1, State(host, true), 0));
        Assert.False(host.AcceptState(999, State(host, true), 0));
    }

    [Fact]
    public void NativeStopAndNonPositiveElapsedDoNotAccrueOrRepay()
    {
        HostSession host = Ready(true);
        Assert.Equal(700, host.AdjustElapsed(700, 7000, false));
        Assert.Equal(0, host.AdjustElapsed(0, 7000, true));
        Assert.Equal(-1, host.AdjustElapsed(-1, 7000, true));
        Assert.All(host.Accounts, a => Assert.Equal((GameMinutes)0, a.Minutes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidCadenceFallsBackWithoutTouchingBalances(int cadence)
    {
        HostSession host = Ready(true);
        Assert.Equal(700, host.AdjustElapsed(700, cadence, true));
        Assert.Equal("unsupported", host.Notice);
        Assert.All(host.Accounts, a => Assert.Equal((GameMinutes)0, a.Minutes));
    }

    [Fact]
    public void DisconnectAndReconnectRetainCreditButRequireFreshState()
    {
        HostSession host = Ready(true);
        host.AdjustElapsed(700, 7000, true);
        host.PeerChanged(2);
        host.Refresh(Frame(true) with { Players = new PlayerPresence[] { new(1, "A") } }, 1);
        Assert.False(host.CanControlClock);
        Assert.Equal(700, host.AdjustElapsed(700, 7000, true));
        Assert.Equal((GameMinutes)1, Balance(host, 1));
        Assert.False(host.Accounts.Single(a => a.PlayerId == 2).Online);

        host.Refresh(Frame(true), 2);
        Assert.Equal("syncing", host.Notice);
        Assert.True(host.AcceptState(2, State(host, false), 2));
        host.Refresh(Frame(true), 2);
        Assert.True(host.CanControlClock);
    }

    [Fact]
    public void RosterRemovalInvalidatesLeaseEvenWithoutPeerEvent()
    {
        HostSession host = Ready();
        PlayerState queued = State(host, true, 99);
        string originalToken = host.ConnectionFor(2);
        host.Refresh(Frame() with { Players = new PlayerPresence[] { new(1, "A") } }, 1);
        Assert.Equal("", host.ConnectionFor(2));
        Assert.False(host.AcceptState(2, queued, 1));
        host.Refresh(Frame(), 2);
        Assert.NotEqual(originalToken, host.ConnectionFor(2));
        Assert.Equal("syncing", host.Notice);
        Assert.False(host.AcceptState(2, queued, 2));
        Assert.True(host.AcceptState(2, State(host, false), 3));
        host.Refresh(Frame(), 3);
        Assert.True(host.CanControlClock);
    }

    [Fact]
    public void ReconnectRejectsAlreadyQueuedStateFromPreviousConnection()
    {
        HostSession host = Ready();
        PlayerState queuedBeforeDisconnect = State(host, true, 99);
        host.PeerChanged(2);
        host.Refresh(Frame(), 1);
        Assert.False(host.AcceptState(2, queuedBeforeDisconnect, 1));
        Assert.True(host.AcceptState(2, State(host, false), 2));
    }

    [Fact]
    public void MissingHostOrUninitializedDayCannotControlClock()
    {
        HostSession host = new();
        Assert.False(host.AcceptState(2, null, 0));
        host.Refresh(Frame(), 0);
        Assert.Equal("transition", host.Notice);
        Assert.False(host.CanControlClock);
        host.StartDay(1, "day1", 1);
        host.Refresh(Frame() with { Players = new PlayerPresence[] { new(2, "B"), new(3, "C") } }, 0);
        Assert.True(host.AcceptState(2, State(host, false), 0));
        Assert.True(host.AcceptState(3, State(host, false) with { Connection = host.ConnectionFor(3) }, 0));
        host.Refresh(Frame() with { Players = new PlayerPresence[] { new(2, "B"), new(3, "C") } }, 0);
        Assert.Equal("syncing", host.Notice);
        Assert.False(host.CanControlClock);
    }

    [Fact]
    public void MissingConnectionAndNullStateAreRejectedBeforeFreshHandshake()
    {
        HostSession host = Ready();
        Assert.False(host.AcceptState(2, null, 1));
        host.PeerChanged(2);
        Assert.False(host.AcceptState(2, State(host, true), 1));
    }

    [Fact]
    public void SleepTransitionResetAndNewDayCannotCarryOldMessages()
    {
        HostSession host = Ready(true);
        host.AdjustElapsed(700, 7000, true);
        PlayerState old = State(host, true, 100);
        host.Suspend();
        host.Refresh(Frame(true), 1);
        Assert.Equal("transition", host.Notice);
        Assert.Equal(700, host.AdjustElapsed(700, 7000, true));
        host.StartDay(1, "day2", 2);
        host.Refresh(Frame(), 2);
        Assert.Equal((GameMinutes)1, Balance(host, 1));
        Assert.Equal((GameMinutes)0, Balance(host, 2));
        Assert.False(host.AcceptState(2, old, 2));
        Assert.Equal(2u, host.CreateSnapshot(2).Day);
        host.Reset();
        Assert.Empty(host.Accounts);
        Assert.Equal("", host.Session);
        Assert.False(host.AcceptState(2, old, 3));
        Assert.False(host.CanControlClock);
    }

    [Fact]
    public void SnapshotHasIndependentAccountsAndExactHeartbeatBoundary()
    {
        HostSession host = Ready(true);
        Assert.True(host.SnapshotDue(0));
        LedgerSnapshot first = host.CreateSnapshot(0);
        Assert.False(host.SnapshotDue(999));
        Assert.True(host.SnapshotDue(1000));
        host.AdjustElapsed(3500, 7000, true);
        Assert.Equal("00:00", first.Accounts.Single(a => a.PlayerId == 1).Minutes);
        Assert.Equal("00:10", host.CreateSnapshot(1000).Accounts.Single(a => a.PlayerId == 1).Minutes);
        host.AcceptState(2, State(host, true, 2), 1001);
        host.Refresh(Frame(true), 1001);
        Assert.True(host.SnapshotDue(1001));
        Assert.Equal(ClockMode.EveryonePaused, host.Mode);
        Assert.Equal(0, host.AdjustElapsed(700, 7000, true));
    }

    private static HostSession Start()
    {
        HostSession host = new();
        host.StartDay(1, "day1", 1);
        return host;
    }

    private static HostSession Ready(bool localPaused = false)
    {
        HostSession host = Start();
        host.Refresh(Frame(localPaused), 0);
        Assert.True(host.AcceptState(2, State(host, false), 0));
        host.Refresh(Frame(localPaused), 0);
        return host;
    }

    private static HostFrame Frame(bool localPaused = false) =>
        new(true, true, false, false, false, localPaused, new PlayerPresence[] { new(1, "A"), new(2, "B") });
    private static PlayerState State(HostSession host, bool paused, long sequence = 1) =>
        new(PeerStateTracker.Version, host.Session, sequence, paused, host.ConnectionFor(2));
    private static GameMinutes Balance(HostSession host, long id) => host.Accounts.Single(a => a.PlayerId == id).Minutes;
}
