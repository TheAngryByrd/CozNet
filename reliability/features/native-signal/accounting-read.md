# Controller accounting read

The controller now reads the ledger's private integer without constructing a `DelayTicks` wrapper.
The typed accessor remains available for domain operations.
`DelayLedger.globalTicks` preserves the ledger's unit-neutral contract. Signal accounting interprets its ticks as nanoseconds.
Input parsing, state transitions, and overflow checks are unchanged.
All 57 deterministic tests pass on Windows.

## Allocation evidence

An isolated read of `SignalAccounting.globalNanoseconds` allocated zero bytes over 100,000 calls on Windows and Linux.
The probe used a non-inlined caller, disabled tiering, and disabled ReadyToRun.
The measured runtimes were .NET 10.0.12 on Windows and .NET 10.0.0 on Linux.
This isolated result did not reproduce allocation in the full controller.

Linux disassembly of `RunStage` showed an allocation between the ledger counter read and the debt calculation.
The old source constructed a `DelayTicks` value and immediately extracted its integer.
After the direct accessor change, this allocation instruction is absent.
The allocation for a retained peak-debt snapshot remains.
The [diagnostic archive](data/accounting-read/run-20261014-read.zip) retains both disassemblies, both zero-percent diagnostic outcomes, and the isolated probe.
Disassembly diagnostics are separate from the capacity run below.

## Retained capacity run

Command: `dotnet run --project build/build.fsproj -- -t LinuxWarmSampleCadenceVerify`.
The [run archive](data/accounting-read/run-20261014T022816450-59d7af92.zip) retains every outcome.
It uses three fixed zero-then-50-percent blocks, 250 ms warmup, and two-second measurement windows.
The four contract-check processes pass on Linux.
All three zero-percent processes pass. Only the third 50-percent process completes and passes.
The first two treatment processes fail during measurement and have no completed measurement totals.

| Completed case | Previous allocation (MiB) | Current allocation (MiB) | Current GC pause total (ms) |
| --- | ---: | ---: | ---: |
| Block 1, zero percent | 447.293 | 5.165 | 0 |
| Block 2, zero percent | 498.657 | 5.167 | 0 |
| Block 3, zero percent | 498.579 | 5.167 | 0 |
| Block 3, 50 percent | 375.341 | 25.307 | 2.362 |

The previous measurements come from the [peer-cadence run](peer-cadence.md).
This is a sequential comparison, not a randomized comparison.
Allocation measures the controller thread. GC pause totals cover the process.
The counter window includes sample enablement and boundary diagnostics.
Lower allocation does not establish stable capacity or prediction accuracy.

## Retained failures

| Block | Request | Failure boundary (ms) | Native wait (ms) | Handler interval (ms) |
| --- | ---: | ---: | ---: | ---: |
| 1 | 2918 | 443.3663 | 5.815261 | 5.816233 |
| 2 | 6320 | 1210.9803 | 7.929205 | 7.929986 |

Both requests target the peer and ask for 50 microseconds.
Both exceed the unchanged 5 ms handler limit.
Their partial measurement windows allocate 5.488 MiB and 15.162 MiB.
Those partial values cannot replace completed two-second measurement totals.
Their GC pause increases are 0 and 2.456 ms. These counters do not identify the cause of either long wait.
All six processes join their workers and restore the backend.
The third treatment's peak peer debt is 0.701905 ms, below the unchanged 2 ms limit.
The 100 ms acknowledgment limit and two-percentage-point prediction gate remain unchanged.

## Verification and next step

`AnalyzeTiming.ps1` verifies 24 source and binary hashes and reconciles eight phases from four completed processes.
It marks both failed processes incomplete. It does not claim complete phase reconciliation for either failed process.
`AnalyzeCounters.ps1` verifies twelve counter windows, including both partial measurement windows.
The run archive includes raw records, derived results, analysis scripts, environment, logs, and outcomes.
The retained measurement precedes the accessor rename from `globalNanoseconds` to `globalTicks` in the ledger.
The archive preserves both measured source files and assemblies. `source-overrides.json` resolves them during hash verification.
The failed capacity run was not repeated to replace its failures.

The next experiment must compare wait mechanisms under the same handler and debt limits.
Keep signal delivery delays separate from time spent in the native wait.
Forced-GC sampled runs, matched controls, and physical-change prediction remain incomplete.
