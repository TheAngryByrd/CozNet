# Runtime suspension trace

The explicit `--sampled-signal-trace` route adds EventSource clock anchors to the sampled signal workload.
Ordinary sampled runs do not create the marker source or emit markers.
The marker provider is `CozNet-SampledSignals`, at informational level.
An unavailable provider fails the traced workload and retains the failure in its JSON output.

Each anchor records an ID, phase, and `Stopwatch` timestamps before and after event emission.
Run, warmup, and measurement boundaries provide anchors.
The output includes `diagnosticTrace`, `traceAnchors`, and the existing `stopwatchFrequency`.
The first anchor can include EventSource initialization cost. It precedes the measured stages.
Trace overhead remains part of the diagnostic run. Diagnostic success does not replace retained capacity failures.

```text
dotnet CozNet.LinuxPerf.dll --sampled-signal-trace workload.json 50 qualified-sample warm spin gc
```

Run this command under a collector that enables the marker provider and `Microsoft-Windows-DotNETRuntime:0x1:4`.
The collector must preserve the child exit code and retain failed measurements.
Only the Linux backend currently supports this workload. The marker implementation uses portable .NET APIs.

```mermaid
flowchart LR
    Marker[Bracketed marker] --> Alignment[Clock offset interval]
    Trace[EventPipe events] --> Alignment
    Alignment --> Correlation[Request and suspension overlap]
    Requests[Managed request records] --> Correlation
```

The first local capture contains all six expected markers and reports zero lost events.
Their offset intervals intersect within 9,295 nanoseconds.
This establishes a usable clock alignment for that capture only.
The capture passes protocol and capacity checks. It does not reproduce the earlier forced-GC failures.
`native/ExportRuntimeTrace.ps1` decodes the trace with the locally installed dotnet-trace assemblies.
The decoder uses the public TraceEvent `TimeStampRelativeMSec` property and records the decoder version.
`native/AnalyzeRuntimeSuspension.py` parses marker alignment and pairs suspension events by CLR instance.
It retains incomplete, interrupted, unsupported, and ambiguous sequences. Any such sequence fails correlation validity.
It rejects event loss, missing markers, inconsistent offsets, and mixed marker processes.
Each recorded forced collection requires a distinct GC suspension episode with possible overlap.
An increased measurement collection count also requires GC suspension evidence inside the counter interval.
Missing runtime evidence fails correlation instead of producing an empty overlap result.
It reports conservative overlap bounds for acquisition, the suspended core, restart, and the broad envelope.
Decimal arithmetic preserves precision when converting trace time into large absolute clock values.
Decimal values use JSON strings in the correlation output.
Only events within the run markers participate in correlation.
The first capture contains 104 complete episodes inside those markers and no pairing anomalies.
Fifty-three measurement requests overlap an envelope. Neither peak-debt service interval overlaps one.
`--verify-signal-trace` checks provider rejection, ordered payloads, clock brackets, and provider disablement after listener disposal.
These checks pass on Windows and Linux. They do not test EventPipe timestamp alignment in process.
`LinuxRuntimeTraceCollect` collects three seeded blocks with zero and 50 percent, with and without forced GC.
The twelve-case comparison retains four failed treatments. One also fails a handler bound and lacks its measurement-end marker.
The analyzer rejects that incomplete trace. Eleven cases have valid alignment and event pairing.
The longest request in one failed no-GC case lasts 17.41 ms without a suspension overlap.
It spends 17.35 ms after the handler finishes. Controller work and scheduling remain unresolved in that interval.
Full sampled prediction remains unverified. No runtime-suspension correction is justified by these records.
Raw traces and decoded events remain under ignored `artifacts/runtime-suspension/`.

See [event semantics](../../docsSrc/Research/runtime-suspension-events.md),
[the first correlation](../../reliability/features/native-signal/runtime-suspension.md),
[sampled signals](sampled-signals.md), and [the index](../lode-map.md).
