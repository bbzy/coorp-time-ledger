using CoopTime.Core;
using Xunit;

namespace CoopTime.Tests;

public sealed class SessionSimulationTests
{
    [Fact]
    public void ThreeRealCoordinatorsExchangeJsonAndRepayUnequalCredits()
    {
        SessionSimulation simulation = new();
        simulation.Start(3);
        Assert.True(simulation.Host.CanControlClock);
        simulation.Pauses[1] = true;
        Assert.Equal(7000, simulation.Advance(7000));
        Assert.Equal((GameMinutes)10, Balance(simulation, 1));

        simulation.Pauses[1] = false;
        simulation.Pauses[2] = true;
        Assert.Equal(3500, simulation.Advance(3500));
        Assert.Equal((GameMinutes)5, Balance(simulation, 2));
        simulation.Now += 1000;
        simulation.Pump();
        Assert.Equal("00:10", Assert.Single(simulation.Clients[2].View!.Accounts).Minutes);
        Assert.Equal("00:00", Assert.Single(simulation.Clients[3].View!.Accounts).Minutes);

        simulation.Pauses[2] = false;
        simulation.Pauses[3] = true;
        Assert.Equal(0, simulation.Advance(3500));
        Assert.Equal((GameMinutes)5, Balance(simulation, 1));
        Assert.Equal((GameMinutes)0, Balance(simulation, 2));
        Assert.Equal(700, simulation.Advance(700));
        Assert.Equal((GameMinutes)1, Balance(simulation, 3));
        Assert.All(simulation.Clients, entry =>
        {
            Assert.Equal("day1", entry.Value.View?.Session);
            Assert.Equal(entry.Key, Assert.Single(entry.Value.View!.Accounts).PlayerId);
        });

        simulation.Now += 1000;
        simulation.Pump();
        Assert.Equal("00:00", Assert.Single(simulation.Clients[2].View!.Accounts).Minutes);
        Assert.Equal("00:00", Assert.Single(simulation.Clients[3].View!.Accounts).Minutes);
        LedgerSnapshot snapshot = simulation.Host.CreateSnapshot(simulation.Now);
        Assert.Equal("00:10", Assert.Single(snapshot.ForPlayer(1).Accounts).Minutes);
        Assert.Empty(snapshot.ForPlayer(99).Accounts);
        Assert.Equal(3, snapshot.Accounts.Length);
    }

    [Fact]
    public void BothPauseAndResumeWithoutManufacturingCredit()
    {
        SessionSimulation simulation = new();
        simulation.Start(2);
        simulation.Pauses[1] = true;
        simulation.Pauses[2] = true;
        Assert.Equal(0, simulation.Advance(7000));
        Assert.Equal(ClockMode.EveryonePaused, simulation.Host.Mode);
        Assert.All(simulation.Host.Accounts, a => Assert.Equal((GameMinutes)0, a.Minutes));
        simulation.Pauses[1] = false;
        simulation.Pauses[2] = false;
        Assert.Equal(7000, simulation.Advance(7000));
        Assert.Equal(ClockMode.Running, simulation.Host.Mode);
    }

    [Fact]
    public void LostHeartbeatsFailOpenAndNextValidHeartbeatRecovers()
    {
        SessionSimulation simulation = new();
        simulation.Start(2);
        simulation.Pauses[1] = true;
        simulation.Advance(700);
        GameMinutes credit = Balance(simulation, 1);
        simulation.DropStates = true;
        simulation.Now = 6000;
        simulation.Pump();
        Assert.Equal("syncing", simulation.Host.Notice);
        Assert.Equal(700, simulation.Advance(700));
        Assert.Equal(credit, Balance(simulation, 1));
        simulation.DropStates = false;
        simulation.Advance(1500);
        Assert.True(simulation.Host.CanControlClock);
        Assert.True(Balance(simulation, 1) > credit);
    }

    [Fact]
    public void EightClientsWithDelayedDroppedDuplicatedAndReorderedPacketsRecoverAndStayNonNegative()
    {
        SessionSimulation simulation = new();
        simulation.Start(8);
        simulation.Noisy = true;
        Random actions = new(20261117);
        for (int frame = 0; frame < 20000; frame++)
        {
            if (frame % 17 == 0)
            {
                simulation.Pauses[actions.Next(1, 9)] = actions.Next(2) == 0;
            }

            int elapsed = frame % 3 == 0 ? 7 : frame % 3 == 1 ? 16 : 33;
            int world = simulation.Advance(elapsed, frame % 1000 < 500 ? 7000 : 9000);
            Assert.InRange(world, 0, elapsed + 1);
            Assert.All(simulation.Host.Accounts, a => Assert.True(a.Minutes >= 0));
        }

        simulation.Noisy = false;
        foreach (long id in simulation.Pauses.Keys.ToArray())
        {
            simulation.Pauses[id] = false;
        }

        simulation.Advance(6000);
        Assert.True(simulation.Host.CanControlClock);
        Assert.Equal(ClockMode.Running, simulation.Host.Mode);
        Assert.All(simulation.Clients.Values, c =>
        {
            Assert.False(c.ViewExpired(simulation.Now));
            Assert.Equal(ClockMode.Running, c.View?.Mode);
        });
    }

    private static GameMinutes Balance(SessionSimulation simulation, long id) =>
        simulation.Host.Accounts.Single(a => a.PlayerId == id).Minutes;
}
