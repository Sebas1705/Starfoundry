# Continuous integration

`.github/workflows/ci.yml` runs on every pull request and on pushes to `main`: simulation tests, the Godot project build and the simulation benchmark. A scheduled run (nightly) runs the slow tier only. It uses no secrets and publishes nothing.

## Test tiers

Tests that take more than about 2 seconds (bot runs, brute-force walks, long replays, wave balance measurements) carry `[Trait("Tier", "Slow")]`, by class where the whole class is heavy, otherwise by method. Everything else is the fast tier.

- Fast loop while developing (about 20 s): `dotnet test --filter "Tier!=Slow"`.
- Slow tier only: `dotnet test --filter "Tier=Slow"`. The full `dotnet test` runs both and must stay green before a merge.
- CI runs two jobs in parallel, `fast` (fast tests, Godot build, benchmark) and `slow`. The required check `build-and-test` is a small gate job that needs both and passes only when both pass, so branch protection is unchanged.
- Mark a new test `Slow` when it takes over about 2 seconds (check with `dotnet test --logger "console;verbosity=normal"`).

## Determinism guard

`src/Starfoundry.Sim` uses `Microsoft.CodeAnalysis.BannedApiAnalyzers` with `BannedSymbols.txt`: `System.Random`, wall-clock time, `Guid.NewGuid`, `Stopwatch`, `MathF` and the floating-point `Math` functions (trig, `Pow`, `Exp`, `Log`, `Sqrt`, `Floor`, `Ceiling`, `Round`) are build errors in the sim (RS0030). Use `IntMath` (`ISqrt`, `FloorDiv`, `CeilDiv`) instead. A legitimate use outside the simulation state needs a narrow `#pragma warning disable RS0030` with a comment saying why.

## Where the build runs

The jobs `fast` and `slow` run on the owner's PC (a self-hosted Linux runner with the labels `self-hosted, pc`) when it is free, and on a GitHub-hosted `ubuntu-latest` runner otherwise.

- A first job, `pick-runner`, always runs on `ubuntu-latest`. It reads the repository variable `PC_RUNNER_HEARTBEAT`, an epoch timestamp (seconds, UTC) that a watcher on the PC refreshes every minute only while the PC is free and idle.
- If the heartbeat is under 180 seconds old, the build uses the PC. If it is missing, stale or in the future, the build falls back to `ubuntu-latest` automatically, so nothing waits for an offline machine.
- **Only `push` and `workflow_dispatch` events can pick the PC.** Pull requests (including forks), scheduled runs and every other event always stay on GitHub-hosted runners, so untrusted code never runs on the PC.
- Nothing in the job depends on the runner image: the .NET SDK comes from `actions/setup-dotnet`.

The job log prints the chosen runner in the `Pick the runner` step.
