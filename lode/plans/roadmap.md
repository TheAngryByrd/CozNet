# Capability roadmap

The implemented scope is a serial experiment prototype. The goal is a .NET implementation of sampled Coz semantics.
Keep the serial model separate from the proposed sampled engine.

1. Specify global and per-thread delay accounting, blocked intervals, wake outcomes, and thread lifecycle.
2. Build a narrow CLR startup profiler spike with selective IL rewriting and original source maps.
3. Test long-loop probe coverage, disabled-probe overhead, zero-speedup overhead, and JIT effects.
4. Combine CPU sampling with cooperative delay checks. Keep platform sampling constraints explicit.
5. Compare sampled predictions with real CPU optimizations. Preserve raw observations and failed predictions.
6. Support named synchronization primitives only after dependency-wake and timeout accounting pass.
7. Add ReJIT and verify active-frame, inlining, source-map, and compilation-settling constraints.
8. Separate request lifetime from CPU execution before async support.

IL rewriting supplies intervention points. It does not supply CPU sampling or synchronization semantics.
The controlled Linux countdown comparison passes its accounting checks and substantially reduces dense-probe cost.
Native-range attribution remains unresolved for inline countdown instructions.
Raw rewritten-IL diagnostics separate the inspected loop ranges with empty-stack gate placement on both controlled methods.
Candidate exclusion and whole-method attribution both fail the physical-prediction gate in the controlled countdown loop.
The stage comparison identifies injected probes as the largest measured cost in the controlled loop.
Investigate a lower-cost intervention mechanism before changing sampled accounting.
A native signal experiment must measure GC effects and bound handler work before any claim of CLR safety.
The [bounded signal workload](../profiling/signal-intervention.md) passes the initial arithmetic and forced-GC checks.
Next, verify runtime transition and shutdown cases, then connect independent CPU events to bounded signal delay accounting.
The current signal experiment generates no causal credits and does not test prediction accuracy.
See [component overhead](../../reliability/features/linux-perf/component-overhead.md) and [signal research](../../docsSrc/Research/native-signal-intervention.md).
See [attribution sensitivity](../../reliability/features/linux-perf/attribution-sensitivity.md).
Keep mixed boundaries and debugger limitations explicit. See [the attribution investigation](../../docsSrc/Research/injected-cpu-attribution.md).
The hybrid design remains provisional until measured coverage and overhead support it.
See [the port decision](../../docsSrc/Research/coz-port-and-il-rewriting.md) and [the implementation comparison](../../docsSrc/Research/managed-coz-implementations.md).

```powershell
dotnet run --project samples/CozNet.Serial -c Release -- artifacts/serial
```

The sample's initial acceptance target is two percentage points of prediction error.
One machine cannot establish general accuracy. Retain failures and compare independent runs.
The [current physical run](../../reliability/features/serial/README.md) exceeds this target for the 50% case.
Investigate prediction stability separately. Runtime experiments do not expand the library's supported workload model.

```mermaid
flowchart LR
    CozContracts --> DelayLedger --> RuntimeSpike --> Validity --> Synchronization --> Async
    RuntimeSpike --> Sampling
    RuntimeSpike --> ILProbes
    Serial --> SerialValidity
```

See [current engine](../profiling/summary.md) and [research](../../docsSrc/Research/causal-profiling-dotnet.md).
