# Physical CPU calibration

`PhysicalCalibration` saves an immutable plan before clock setup and executes two warmups plus eight paired measurements.
Every measurement verifies 50,000 checksum calls on the current thread.
The exact saved order, seed, runtime identity, raw clocks, completed calls, and checksum remain in evidence.
Analysis requires all eighteen trials in declared order. Failures and missing steps cannot disappear from a complete estimate.
Negative reductions are retained.
Each trial is flushed to a JSON-lines journal before the next measurement. Storage failure stops collection.
An incomplete trailing row or a missing final result cannot establish a completed series.

```powershell
./build.cmd LinuxPhysicalCalibration
```

The FAKE target builds the workload and calls `VerifyPhysicalCalibration.ps1`.
The script captures source and binary snapshots before execution and checks input hashes afterward.
The source snapshot omits shared imports and referenced project sources. Retained binaries support replay, not a standalone source rebuild.
It runs portable checks on the host and Linux, then physical baseline, optimized, and calibration cases on Linux.
Linux processes use a 30-second watchdog.
The host verifier retains its individual worker bounds.

`IChecksumArithmetic` value types separate original and optimized calls.
The shared worker chooses its implementation once, then uses a constrained generic loop.
Controlled arithmetic implementations prove selected-only dispatch and prevent wrong checksums from publishing progress.
Calibration uses the same concrete arithmetic types, but its fixed-work loop has no worker progress publication.

```mermaid
flowchart LR
    Plan[Save exact paired plan] --> Warmup[Two warmup trials]
    Warmup --> Trials[Eight randomized pairs]
    Trials --> Raw[Retain all measurements and failures]
    Raw --> Validate[Require complete valid plan]
    Validate --> Estimate[Paired CPU reductions and interval]
```

The core accepts `IThreadCpuClock` and `TimeProvider`. The command currently selects the existing Linux x64 CPU clock.
Portable controlled-clock checks run on Windows and Linux. No Windows live CPU-clock backend is claimed.
The first retained estimate has mean 0.5127666452 and interval half-width 0.0345791551.
Its distance-plus-half-width from 0.5 is 0.0473458003, above the entire 0.02 prediction-error budget.
The estimate also includes loop and measurement overhead and does not bound clock bias.
No automatic speedup selection or prediction accuracy follows from capture completion.

See [the reference](../../docsSrc/Reference/physical-calibration.md), [matched workers](matched-physical.md), and [the evidence](../../reliability/features/native-signal/physical-calibration.md).
