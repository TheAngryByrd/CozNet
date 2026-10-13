# CozNet

Experimental causal profiling for .NET 10, written in F#.

CozNet asks how much faster a workload could run if one CPU region took less time.
The first engine supports **serial, synchronous CPU workloads with explicit regions**.
Parallel work, async work, blocking I/O, and automatic instrumentation are unsupported.
This is an independently authored experiment, not a port of native Coz.

## Run the sample

Install the .NET 10 SDK. From the repository root:

```sh
dotnet run --project samples/CozNet.Serial -c Release -- artifacts/serial
```

The sample compares predicted gains with real reductions in redundant checksum work.
Every version verifies the same checksum. It records null, 25%, and 50% region reductions.
Raw observations and comparisons go to `measurements.json` in the output directory.
Exit code `1` means at least one comparison exceeded two percentage points of prediction error.
Retain failing results. One passing run does not establish general accuracy.
The [recorded local run](reliability/features/serial/README.md) exceeded the target for the 50% case.

## Use the library

```fsharp
open CozNet

let parse result = Result.defaultWith failwith result
let region = Region.tryCreate "checksum" |> parse
let speedups = [0.0; 0.25; 0.5] |> List.map (VirtualSpeedup.tryCreate >> parse)
let settings = ExperimentSettings.tryCreate region speedups 21 5 1729 |> parse

let result =
    Experiment.run settings (fun session ->
        session.Region(region, fun () -> checksum () |> ignore)
        session.Progress())
```

Here, `checksum` is your synchronous CPU operation. Each invocation must perform equivalent work.
The callback must finish all work before it returns. Keep measured work inside non-nested regions.
Every measured run must record equal positive progress.
The engine rejects marked work on another thread. It cannot detect unmarked concurrent work or blocking inside a callback.
A callback failure aborts the experiment. The runner has no cancellation or timeout control.

## Measurement model

For virtual speedup `s`, the engine adds delay `region time * s / (1 - s)`.
A control delays every region. Its paired treatment exempts the selected region.
Each block randomizes all conditions. Results contain median, minimum, and maximum paired elapsed-time reductions.
These extrema are observed ranges, not confidence intervals. No outliers are removed.

The model assumes marked CPU work dominates elapsed time. Unmarked overhead, scheduling, GC, and spinning can bias results.
Region timestamps measure wall time, not per-thread CPU time.
See [the model](docsSrc/Explanations/Background.md) and [the research](docsSrc/Research/causal-profiling-dotnet.md).

## Build and test

```sh
dotnet tool restore
dotnet fantomas --check src tests samples
dotnet test CozNet.slnx -c Release
```

`build.cmd` and `build.sh` run these checks and build the sample.
Deterministic tests use a controlled clock. The sample exercises real timing separately.
The CI workflow defines deterministic checks for Windows and Linux.

## Project knowledge and origin

The default branch is `master`. Start agent sessions with [AGENTS.md](AGENTS.md) and [the Lode index](lode/lode-map.md).
The [roadmap](lode/plans/roadmap.md) separates current behavior from planned capabilities.

The project uses [MiniScaffold](https://github.com/TheAngryByrd/MiniScaffold) as its basis.
The profiling code is independently authored under the [MIT license](LICENSE.md).
It draws on published ideas in [Coz](https://github.com/plasma-umass/coz) and [Anthony Lloyd's article](https://anthonylloyd.github.io/blog/2019/10/11/causal-profiling).
No code was copied from `AnthonyLloyd/Causal`.
