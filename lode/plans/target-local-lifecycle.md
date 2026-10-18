# Target-local runtime lifecycle

## Decision

The profiler now enforces its code-binding assumption instead of only documenting it.
A run binds each selected and eligible method to exactly one optimized native range, and the binding must hold from capture to the end of the run.
Rationale: credit and payment eligibility use instruction ranges. A tier change, OSR, or ReJIT moves code to a new range.
Samples in moved code would then earn no credit and pay nowhere, and the prediction would be wrong without any failure signal.

## Code binding

`CodeBinding` in `src/CozNet/TargetLocalProfiler.fs` is portable and parses perf-map lines.

- `CodeBinding.configurationErrors ()` rejects a process unless tiered compilation is off, ReadyToRun is off, and the perf map is on.
  Tiering is off when `DOTNET_TieredCompilation` or `COMPlus_TieredCompilation` is `0`, or when neither is set and the
  `System.Runtime.TieredCompilation` switch is false. An environment value overrides the switch.
- `CodeBinding.tryCapture lines symbols` requires each symbol to have exactly one perf-map entry of any tier, and that entry must be `[Optimized]`.
  A `[QuickJitted]`, `[OptimizedTier1]`, `[OptimizedTier1OSR]`, or second entry rejects.
  The perf-map setting must be `1` or `3`. The value `2` writes jitdump files only.
- `CodeBinding.verify lines binding` repeats the capture and requires the same ranges.
- `TargetLocalProfiler.runBound` checks configuration, captures, runs, and verifies. A failure sets `BindingError` and makes `Passed` false.

ReJIT is not supported. A ReJIT version writes a new perf-map entry, so `verify` detects it, but no check produces one.

## Shutdown

`Environment.Exit` during payment is a supported exit. The runtime runs exit handlers and the kernel releases the event, mapping, and signal state.
No CozNet cleanup runs, and the run produces no result.

## Debt-failure evidence

Each `TargetLedgerEvent` keeps its native time. For samples, it is the perf sample time.
Each profile result reports the longest gap between consecutive samples of each thread.
A debt failure with a sample gap near the failure debt, divided by the speedup, supports the descheduling hypothesis.
In barrier patterns the gap also includes time blocked at the barrier, so the gap does not separate the two causes there.
`RunDelayNs` covers registration release to finish, so it includes start-barrier wake latency. Runs before `20261018T203002656-dab28501` measured it inside the timed window.

## Measurement contamination

Profile-zero overhead rose from about 1.00 to 1.05 to 1.09 in every prediction and barrier configuration, starting with run `20261018T192337237-ab4134f4`.
That run added the run-queue diagnostic, which read `/proc/thread-self/schedstat` inside the timed window.
With tiering off, the first `File.ReadAllText` call compiles at full JIT, so its cost lands inside the profile arms only.
Barrier run `20261018T202640118-21860ac2` then failed `peer-bound` with +12.3 and +7.8 point blocks and overhead ratios of 1.05 to 1.14.
Fix: each worker reads the file once at registration to compile it, then reads it before the start barrier and after its finish time.
Expected result: the overhead ratio returns to about 1.00. The pass rules do not change.

## Acceptance criteria

These criteria are fixed before the first lifecycle run. They join the transition checks.

9. `binding-tiered` runs with `DOTNET_TieredCompilation=1`. `configurationErrors` must report tiering, and `tryCapture` must reject the selected method.
10. `binding-default` runs with the qualified settings. Both checks must pass, and `verify` after 100 ms of work must keep the same ranges.
11. `shutdown` calls `Environment.Exit` from the monitor 30 ms after the work starts. The process must exit with code 0 within 10 s.
    The marker file written before the call must record that a worker is still in its work phase, with sampling and payment active.
    No result file is written. The check cannot show that a handler is inside a payment spin at the exit instant.
12. Every check in this list runs once per transition run. No check is repeated to replace a failure.

Revision: criterion 11 was narrowed and the marker gained the work state after review, before the transition run that follows.

Deterministic tests cover the perf-map rules for each tier tag, duplicate entries, moved ranges, and the configuration precedence.
They also cover the checked run: a configuration error or a moved binding raises before the work starts, and a move during the run is reported.

See [target-local transitions](target-local-transitions.md) and [target-local status](target-local-status.md).
