# Testing

## Automated execution

Requires the .NET 10 SDK, Nushell, `zip`, and installed game and SMAPI assemblies. Run from the repository root in Nushell:

```nu
$env.STARDEW_GAME_PATH = '/path/to/game/assembly-directory'
nu --no-config-file tests/run.nu
```

This command builds the package, runs tests and generates coverage reports. It does not launch the game or install the mod. Cecil reads real game assemblies as data; executable IL-rewriter tests use synthetic methods.

## Automated coverage

| Area | Checks |
| --- | --- |
| Exact accounting | Fraction arithmetic, millisecond accumulation, speed conversion, frame partitioning, zero boundaries and large balances |
| Pause and repayment | Multiplayer pause combinations, partial balances, everyone paused, native single-player timing and suspended settlement during day transitions |
| Multiplayer sessions | Handshake, heartbeat, timeout fallback, recovery, reconnection, roster changes, stale sessions and malformed messages |
| Network simulation | JSON round trips, seeded delays, loss, duplication, reordering and eventual convergence |
| Cross-day persistence | Retention across days and disconnects, exact fractions and conversion remainders, simulated save isolation and unsaved rollback |
| Storage failures | Unsupported versions, corrupt fields, duplicate players and read/write failures without partial restoration or overwriting original data |
| Interface state | Display rounding, hour carry, long balances and private account delivery |
| Game integration | Static target-DLL contracts, event subscriptions, pause predicate calls, IL patch sites and safe failure |
| Distribution | File allowlist, assembly entry point and manifest, without game DLLs |

The core library and IL rewriter require 100% line and branch coverage. Reports include unexecuted game-adapter types and lines rather than excluding them to inflate coverage. Failed or skipped tests fail the gate.

Results are written to the ignored `artifacts/test-results/` directory: `tests.trx` contains individual results, `coverage.cobertura.xml` contains coverage data, `coverage-summary.json` lists per-type coverage and unexecuted lines, and `regression-summary.json` records test results and optional historical comparisons.

If `artifacts/baseline-122/tests.trx` exists locally, the runner compares against it; otherwise it tries `artifacts/baseline-40/tests.trx`. Without historical results, all tests and coverage gates still run. Historical results record renamed cases and cases retired with the previous ledger panel. These comparisons do not establish identical game behavior across versions.

## Manual multiplayer acceptance

These steps require the tester to explicitly launch the game; automation does not execute them. Use an isolated mod directory and a test save. Install only this mod, at the same version, on the host and clients. Start with at least two online players.

1. Have A open a menu that pauses single-player while B remains active. Confirm that the world clock advances and only A accrues owed time. Check that the paused player sees a red number and the active player sees a white number. At five owed minutes the display changes from `00:00` to `00:10`.
2. Have B pause too. Confirm that the clock stops and neither player's balance accrues or repays.
3. Keep B paused while A resumes. Confirm that the clock stops while A's balance repays, then resumes at zero. With nobody paused, time advances without repayment.
4. Check dialogue, shops, fishing phases, cutscenes, minigames, loss of focus and sleep waiting against equivalent single-player conditions. Confirm that day transitions suspend settlement.
5. Check that each player sees one number for their own account. Click and scroll over the number and confirm the game receives the input normally.
6. Disconnect and reconnect a client, including a period with only the host present. Confirm balance retention and native single-player timing, followed by compensation after a new handshake.
7. Carry a nonzero balance across a day, save and exit, then reload the same save and reconnect. Confirm restoration and continued repayment. Switch test saves to check isolation.
8. Change balances without saving, exit and reload. Confirm restoration of the last saved values. Check native clock behavior with a missing compatible client. Interrupt the network long enough for a snapshot to expire, then check that the client’s number hides and reappears after synchronization resumes.

## Verification limits

Automated persistence round trips use simulated storage and cannot replace acceptance of SMAPI's actual save/load lifecycle. Actual game hooks, rendering, input events and multiplayer timing also require manual validation. Network latency and frame sampling affect pause boundaries; exact fractional accounting does not remove those delays.

Machine-specific paths, logs, packages, raw coverage data and intermediate verification records stay outside version control. Source, tests, drivers and reusable documentation are tracked. Other time-control mods, local split-screen and unverified game patch versions are outside the current support scope.
