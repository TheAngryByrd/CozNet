# Cooperative native ABI checks

`CooperativeApiChecks.cpp` calls the shared library from real controller and owner threads.
The fixture uses the production clock and spin wait. It does not run managed probes or sampled debt service.

| Requirement | Evidence |
| --- | --- |
| Nonzero monotonic clock across threads and bounded spin requests | `nativeClockAndBoundedWait` |
| Registration, duplicate slots, role rejection, idle polling, and close ownership | `registrationAndRoleBoundaries` |
| Null outputs, invalid sites, slots, and handles | `invalidApiInputs` |
| Actual payment, fixed record fields, guarded copy, receipt clock order, and generation ownership | `copiedJournalAndGenerationOwnership` |
| Each mismatched site field defers without payment, and cancellation preserves zero payment | `mismatchedSitesAndCancellation` |
| Distinct target roles, journals, generations, and independent release authorization | `independentTargetOwnership` |
| Pending revocation, blocked registration/submission, retirement, and rejected post-retirement polls | `revocationAndClose` |
| One session per process and stale-handle rejection after close | `main` |

The Windows and Linux runners use these commands:

```powershell
./native/VerifyServiceOwner.ps1
./native/VerifyServiceOwner.ps1 -Platform LinuxContainer
```

The first Linux build exposed a Clang 18 frontend crash while instantiating the generic snapshot lambda.
Explicit `CozCooperativeSession&` parameters avoid that crash. No protocol or timing gate changed.
Windows and Linux each pass all eleven CTest entries, including the final input-boundary and independent-target assertions.

No managed receipt, sampled capacity, instrumentation overhead, or physical prediction claim follows from these native checks.
The existing controlled-clock transport tests cover injected timing failures separately.
See [the ABI reference](../../../docsSrc/Reference/cooperative-interop.md) and [the transport evidence](cooperative-target.md).
