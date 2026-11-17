# Coop Time Ledger

Compensates players for game time lost while they would be paused in single-player. Supports network multiplayer on PC Stardew Valley 1.6 with SMAPI 4.x. The host and all players must install the same version. Local split-screen and combinations with other time-control mods are outside the supported scope.

## Timing rules

- When a player would pause in single-player but the shared clock keeps running, that player accrues owed time.
- When at least one player is paused and every active player has owed time, the clock stops to repay the active players simultaneously. The clock resumes as soon as any active player's balance reaches zero.
- With nobody paused, time advances normally without repayment. With everyone paused, the clock stops and balances remain unchanged.
- Only the clock is controlled; players, monsters and animations continue. Native progress that depends on the clock is also affected.
- Balances survive day changes and disconnections. Offline players accrue no additional time. Accounting and repayment stop during day transitions, and player states are synchronized again the next day.
- Balances are saved with the game and restored on load. Exiting without saving rolls back both game progress and the ledger to the last save. Each save has its own ledger.
- With only one player, native single-player timing applies and existing balances remain unchanged.

Pause detection calls the game's `shouldTimePass(true)` and includes the single-player update loop's outer gates for menus, minigames and loss of focus. Waiting for sleep counts as paused; day transitions do not participate in accounting. Tool actions such as fishing are evaluated according to their current phase.

The multiplayer world has one clock. Its speed follows the single-player rules for the host's location, including the native Skull Cavern timing adjustment. Players in different locations do not receive independent world clocks.

Balances use exact fractions, preserving engine millisecond granularity and conversion remainders across clock speeds. The display rounds to the nearest ten game minutes, with ties rounding up. For example, 4 minutes displays as `00:00`, 5 minutes as `00:10`, and 55 minutes as `01:00`. The hour field shows total hours and can exceed 24.

Save data retains exact fractions and sub-millisecond conversion remainders. The ledger uses SMAPI's per-save data API rather than global configuration or files in the mod directory. Corrupt or unsupported data disables compensation and logs the save directory and affected field; it is never overwritten with an empty ledger.

Clients report state changes immediately and send a heartbeat every second. The host settles time after receiving those states. Network latency and frame sampling affect when a state takes effect; exact accounting does not imply zero-latency input. Missing player updates for over five seconds, a missing compatible mod, or unsupported clock integration suspends compensation and restores native timing while preserving balances.

## Ledger interface

The upper-left corner shows only your owed game time. The number is red while you are paused and white while you are active. The host sends each client only that player’s account. The number appears once your account is available and hides if its snapshot expires.

## Build and test

Development requires the .NET 10 SDK, Nushell and `zip`. The mod and core library target the game's .NET 6 runtime; tests run on .NET 10. Builds reference installed game assemblies without launching the game or deploying to Mods.

Set the assembly directory in Nushell and run:

```nu
$env.STARDEW_GAME_PATH = '/path/to/game/assembly-directory'
nu --no-config-file tests/run.nu
```

The directory must contain `Stardew Valley.dll`, `StardewModdingAPI.dll`, `MonoGame.Framework.dll` and `smapi-internal/`. On macOS, it is usually `Contents/MacOS` inside the game bundle.

To build only the distribution package, run `nu --no-config-file build.nu`. Output is `artifacts/CoopTimeLedger-0.1.3.zip`; raw test results are in `artifacts/test-results/tests.trx`. The package contains only the mod, core library and manifest, without game DLLs.

`tests/run.nu` collects line and branch coverage and runs a standalone C# gate. The core library and IL rewriter require full coverage; game-dependent adapters must also appear in the report, including unexecuted code. If local historical results exist, the runner prefers `artifacts/baseline-122/tests.trx`, otherwise `artifacts/baseline-40/tests.trx`. Reports record renamed cases and cases retired with the previous ledger panel. Reports are written to `artifacts/test-results/coverage-summary.json` and `regression-summary.json`.

## Verification limits

Automation covers exact accounting, player transitions, zero-balance boundaries, disconnections, cross-day retention, persistence round trips and failures, message ordering, display rounding, packaging and static checks against the target game DLL. Cecil reads the DLL as data without executing or initializing the game.

Automation does not validate actual menus, fishing, cutscenes, sleep, multiplayer compensation, HUD rendering, mouse events or the game's save/load lifecycle. Passing static checks does not establish in-game multiplayer acceptance. See [Testing](docs/testing.md) for coverage and manual acceptance steps.

Version 0.1.3 adds private account snapshots and a personal HUD. All players in a room must use the same version. Existing saves retain their balances.
