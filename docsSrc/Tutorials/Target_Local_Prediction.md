# Predict an optimization with target-local sampling

This tutorial runs the CozNet target-local profiler on a small managed program.
You compare its prediction with a real optimization of the same program.
It takes about ten minutes.

You need Windows or Linux with the .NET 10 SDK, PowerShell 7, Docker, and Python 3.
On Windows, the runner uses `py`. On Linux, it uses `python3`. Set `COZNET_PYTHON` to use another command.
You also need the local images `coznet-native-build:10.0.100` and `mcr.microsoft.com/dotnet/sdk:10.0.100`.
The profiler itself runs only on Linux x64. Docker supplies that environment.

## The program

`samples/CozNet.TargetLocal` starts two managed threads and waits for both to finish.

- The selected thread runs `Work.selected` for a fixed number of rounds.
- The peer thread runs `Work.peer` for a fixed number of rounds.

The question is this: if `Work.selected` became 50% faster, how much faster would the program finish?
The answer depends on which thread finishes last. CozNet answers it without changing `Work.selected`.

## Step 1: Run the comparison

1. Open a terminal in the repository root.
2. Run this command:

   ```sh
   dotnet run --project build/build.fsproj -- -t TargetLocalPredictionVerify
   ```

3. Wait for the target to finish. It prints a new directory under `artifacts/target-local-prediction`.

The target builds the program and the native library. It then runs three configurations, each in five blocks.
Each block runs four programs in a random order:

| Run | What it does |
| --- | --- |
| `profile-zero` | Profiles at 0% virtual speedup. |
| `profile-speedup` | Profiles at 50% virtual speedup of `Work.selected`. |
| `original` | Runs the program without the profiler. |
| `optimized` | Runs the program with `Work.selected` doing half the rounds, without the profiler. |

## Step 2: Read the prediction

Open `summary.json` in the new directory.
Each configuration shows `medianPredicted`, `medianActual`, and `medianAbsoluteDifference`.

In `selected-longer`, the selected thread has twice the work of the peer.
The profiler predicts a reduction of about 50%, and the optimized program is about 50% faster.

In `peer-longer`, the peer has twice the work of the selected thread.
The profiler predicts almost no reduction, and the optimized program is not faster.
In `selected-dominant`, the selected thread has four times the work of the peer.
After a 50% optimization, the selected thread is still the longer thread. The profiler and the optimized program both show about 50%.
This configuration would expose a profiler that inserts too much delay, because the prediction would then exceed 50%.
A conventional CPU profile shows selected work in every configuration, but the `peer-longer` optimization does not help.

## Step 3: See how the profiler got the answer

Open `selected-longer-1-profile-speedup.json`.

- `globalNs` is the delay that the profiler inserted. Each selected sample earned 500,000 ns of credit.
- The peer thread paid that delay in its own signal handler. Each wait requested at most 50 microseconds. A preempted wait can measure more.
- `effectiveNs` is `elapsedNs` minus `globalNs`. The prediction compares it with the `profile-zero` run.
- `totalHandlerNs` is the sum of the measured handler intervals. In a `profile-zero` run it is about 0.1% of `elapsedNs`.
  The interval excludes signal entry, exit, and kernel work, and it can include descheduling. It does not bound the elapsed overhead.
- `replay` shows the managed check of every native ledger event. An empty `replayError` means that the journal matches the ledger rules.

## What you did

You used virtual speedup to predict a real optimization. Then you measured the real optimization and compared the two values.
See [the qualification evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/target-local/prediction.md),
[the API reference](../Reference/target-local-api.md), and [the support boundary](../Explanations/target-local-prediction.md).
