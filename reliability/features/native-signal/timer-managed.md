# Timer payment during managed arithmetic

The controlled Linux x64 workload consumes actual timer journals through `TimerAccountingAdapter`.
It uses synthetic selected credit and a stable runtime map for `CpuWorkload.Run`.
It does not collect CPU samples or compare predictions with physical changes.

## Retained run

- Base revision: `0243f6be8608c86c021a8ba6a053b015aa01d8dc`, with the workload changes identified by the source hashes in `manifest.json`.
- Command: `dotnet run --project build/build.fsproj -- -t LinuxTimerManaged`.
- Evidence: `artifacts/timer-managed/20261014T205318945-ce7e12b0`.
- Target framework: .NET 10. Container identities and kernel details are in the evidence directory.
- Fixed schedule: 16 credit steps, with 110,000 ns of synthetic credit per step. No randomized input is used.

| Observation | Result |
| --- | --- |
| Reused timer generations | 15 |
| Completed multi-child leases | 15 |
| Paid children | 42 |
| Total measured wait | 1,762,817 ns |
| Largest measured handler interval | 205,282 ns |
| Largest first managed receipt bound | 4,138,897 ns |
| Skipped credit step | 10 |
| Final credit minus measured wait | -2,817 ns |
| Target join and session close | Passed |
| Final pending accounting | Zero |

Every arithmetic call returned 49,995,000.
The progress counter increased between each command's controller observations before submission and after final receipt processing.
These intervals include controller overhead. They do not isolate progress within the native lease.
The selected method occupied one 26-byte native range that remained unchanged between the saved maps.
Every paid child's interrupted instruction pointer fell inside that range.

Independent record inspection confirmed each child sum, accounting total, generation sequence, requested-wait bound, handler bound, and first receipt bound.
It also reproduced every reserved budget from credit minus measured wait.
Step 9 measured 255,691 ns against a 109,985 ns budget.
That overshoot covered step 10's credit. The model therefore submitted no lease for step 10.
The skipped step preserves measured overshoot instead of discarding it.

## Limits

This run establishes controlled timer payment and ledger settlement during managed arithmetic on the tested Linux x64 runtime.
It does not establish progress between individual handlers, forced-GC behavior, general CLR safety, sampled debt capacity, or prediction accuracy.
The 50,000 ns requested wait, 5 ms handler, 100 ms first receipt, and 2 ms lease-budget bounds remain unchanged.

See [the procedure](../../../docsSrc/How_Tos/run-managed-timer-payment.md) and [the accounting contract](../../../docsSrc/Reference/timer-accounting.md).
