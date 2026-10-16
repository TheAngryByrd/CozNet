# Controlled cooperative receipt cost

Evidence directory: `artifacts/cooperative-receipt-cost`.
Source base: `5455eaa`, plus `CooperativeReceiptCostWorkload.cs` and its command registration.
The diagnostic uses the real receipt tracker and accounting adapter with synthetic native receipt inputs.
It does not measure native snapshot calls, CLR lifetime checks, or controller dispatch.

## Contract and execution

Each of 32 iterations reserves a fresh 500,000 ns lease and constructs ten eligible 50,000 ns child records.
Setup occurs before timing. Observation precedes acceptance on the same thread.
Each iteration checks ten accepted children, 500,000 ns confirmed payment, terminal settlement, and zero remaining peer debt.
The output retains all completed brackets. Failures produce an exception field and a nonzero process exit.
No elapsed-time threshold controls success.

The first Windows run uses `dotnet run --project tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj -c Release -- --measure-cooperative-receipt-cost <path>`.
Later runs use the built assembly directly in separate processes.
Linux runs use the same managed binaries in `mcr.microsoft.com/dotnet/sdk:10.0.100` with networking disabled.
The binary directory is mounted read-only. Only the evidence directory is writable.
`binary-hashes.json` records DLL hashes after the captures. It is not a pre-execution integrity manifest.

The six process exits are zero. Each output contains `passed=true`, no failure, and 32 completed iterations.
No failed capture was discarded or repeated.

## Observed brackets

All durations below are milliseconds. Later medians use iterations 1 through 31 of the same process.

| Capture | First observation | Later observation median | First acceptance | Later acceptance median |
| --- | ---: | ---: | ---: | ---: |
| windows-1 | 2.070300 | 0.001100 | 4.244600 | 0.018300 |
| windows-2 | 1.833500 | 0.001100 | 3.681300 | 0.018400 |
| windows-3 | 1.844900 | 0.001600 | 3.750600 | 0.018600 |
| linux-1 | 5.704353 | 0.001496 | 4.729398 | 0.009641 |
| linux-2 | 2.748078 | 0.001542 | 3.421925 | 0.008810 |
| linux-3 | 4.757362 | 0.001306 | 4.176441 | 0.008837 |

Observation is `(Observed - Started) * 1000 / timestampFrequency`.
Acceptance is `(Accepted - Observed) * 1000 / timestampFrequency`.
Both calculations use managed timestamps only.

## Interpretation

The initial six captures precede the `runtimeEnvironment` output field.
Their commands do not request the live runner's disabled tiering and ReadyToRun settings.
They do not establish timing under that configuration.

## Explicit live compilation settings

Three additional fresh Linux processes set `DOTNET_TieredCompilation=0` and `DOTNET_ReadyToRun=0`.
These match the requested settings in `VerifyBoundSampledPayment.ps1`.
The diagnostic now records both variables and their `COMPlus_` counterparts.
All three outputs record the requested zeros and absent `COMPlus_` counterparts.
These are raw environment settings, not a probe of effective runtime configuration.

All three process exits are zero, and all 96 leases settle exactly.
`configured-before-hashes.json` and `configured-after-hashes.json` contain identical DLL hashes.
The earlier binary hashes differ because the diagnostic now emits runtime environment fields.
The measurement code and fixture remain unchanged.

| Capture | First observation, ms | Later observation median, ms | First acceptance, ms | Later acceptance median, ms |
| --- | ---: | ---: | ---: | ---: |
| linux-configured-1 | 3.565163 | 0.002655 | 13.760920 | 0.006975 |
| linux-configured-2 | 2.948804 | 0.002037 | 9.161252 | 0.005194 |
| linux-configured-3 | 4.600905 | 0.002191 | 12.638070 | 0.005468 |

This separate series retains the first-call pattern under the live runner's requested compilation settings.
It does not isolate JIT cost or establish how much live overhead preparation can remove.

## Next experiment

Both managed components have a larger first-call bracket in every retained process.
This supports an opt-in experiment that prepares managed receipt paths outside the measurement window.
Such preparation must use isolated accounting state and must not alter live debt, ownership, or receipt bounds.
The current diagnostic does not implement that policy or establish its live benefit.

Windows uses .NET 10.0.13 and Linux uses .NET 10.0.0. The comparison does not isolate platform cost.
The synthetic journal has ten children. The earlier live prefix capture has seven children.
Setup already exercises ledger creation and reservation. These are not wholly uninitialized runtime measurements.
The brackets include possible descheduling and do not identify JIT, GC, cache, or CPU attribution.
They cannot be subtracted from a separate live capture to compute removable overhead.

See [the explanation](../../../docsSrc/Explanations/cooperative-pause-cost.md#controlled-managed-receipt-cost)
and [the live dispatch evidence](sparse-first-receipt-timing.md).
