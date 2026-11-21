# Continuous integration

`.github/workflows/ci.yml` runs on every pull request and on pushes to `main`: simulation tests, the Godot project build and the simulation benchmark. It uses no secrets and publishes nothing.

## Where the build runs

The `build-and-test` job runs on the owner's PC (a self-hosted Linux runner with the labels `self-hosted, pc`) when it is free, and on a GitHub-hosted `ubuntu-latest` runner otherwise.

- A first job, `pick-runner`, always runs on `ubuntu-latest`. It reads the repository variable `PC_RUNNER_HEARTBEAT`, an epoch timestamp (seconds, UTC) that a watcher on the PC refreshes every minute only while the PC is free and idle.
- If the heartbeat is under 180 seconds old, the build uses the PC. If it is missing, stale or in the future, the build falls back to `ubuntu-latest` automatically, so nothing waits for an offline machine.
- **Only `push` and `workflow_dispatch` events can pick the PC.** Pull requests (including forks), scheduled runs and every other event always stay on GitHub-hosted runners, so untrusted code never runs on the PC.
- Nothing in the job depends on the runner image: the .NET SDK comes from `actions/setup-dotnet`.

The job log prints the chosen runner in the `Pick the runner` step.
