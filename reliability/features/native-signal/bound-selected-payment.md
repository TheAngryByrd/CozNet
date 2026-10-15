# Bound selected-owner payment evidence

The opt-in draft uses a separate native controller, a sampled selected owner, and a peer owner.
The selected owner stops its source before it pays a selected lease.
The source resumes only after callback return and the CLR lifetime check.

## Retained registration failure

`artifacts/bound-selected-payment/20261015T154345170-cd2378f6` retains three failed cases.
Each case attempted to register the native controller as a payment target.
The native role check rejected registration before sampling or payment.
Cleanup also reported target retirement failure. No case completed ownership cleanup.

The corrected draft moves native controller operations to a dedicated worker.
The native role check remains unchanged.

## Corrected runtime run

`artifacts/bound-selected-payment/20261015T154641258-aac1c426` retains source copies, source and binary hashes, commands, logs, and JSON records.
The source and binary hashes did not change during execution.
The run reuses the unchanged cooperative library from `20261015T132712330-55ac6e00` and the existing Linux CLR profiler.
Both managed command groups passed in the Linux container:

- `--verify-bound-cooperative-managed`
- `--verify-pausable-managed`

| Runtime case | Result | Selected payment | Final debt |
| --- | --- | --- | --- |
| Zero speedup | Completed | No leases or pauses | Zero for both owners |
| 50% speedup | Completed | 2,727 leases; 494,503 ns settled wait | Selected: 94 ns; peer: zero |
| Invalidate after selected payment | Expected rejection verified | One lease; 139 ns settled wait | No accepted final measurement |

The 50% case recorded 2,728 pauses and 1,000,000 ns of sample credit.
Its peer settled 1,494,597 ns. Its final selected local delay was 1,494,503 ns.
The invalidation case retained its completed selected payment and no resume timestamp.
All corrected cases joined the peer and controller and closed native ownership without cleanup errors.

## Build target and independent analysis

`artifacts/bound-selected-payment/20261015T160430592-62810d5f` retains the first complete `LinuxBoundSelectedPayment` target run.
The native suite passed all twelve checks. All three managed groups and both eighteen-test Python suites passed.
All three runtime cases passed their expected result and the retained version 2 analyzer.
Source and binary hashes remained unchanged during execution.

The treatment settled 1,977 selected leases across 1,977 pauses.
It earned 4,500,000 ns of credit and settled 510,280 ns of selected wait.
The peer settled 5,010,409 ns. Final selected debt was 129 ns. Final peer debt was zero.
The invalidation case retained one selected payment of 155 ns and rejected completion.

The later analyzer check also rejects a first source transition before its initial counter reading.
The eighteen selected-analysis tests passed after that change.
Offline analysis of the same records passed and is retained as `rechecked-analysis.json`.
No unchanged timing case was repeated for that check.

## Cancellation coordinator checks

`CooperativeCleanupCancellationChecks` exercises failures during pending lookup, visibility reads, and cancellation.
Each failure leaves the other slot available for cancellation. Aggregate errors retain the original exceptions in slot order.
The checks also cover polling permission, absent and retired slots, retry recovery, and retained prior cancellation.
The managed group passed on Windows.

`artifacts/bound-selected-payment/20261015T161231098-3723a32f` retains the full Linux target after the coordinator extraction.
Native checks, all three managed groups, both Python suites, and all three live cases passed.
The source and binary hashes remained unchanged. The version 2 analyzer passed each case.

## Receipt interval revision

The current selected command writes schema version 3.
It records the managed clock frequency and a parsed request-to-receipt interval for each settled lease.
The workload checks the 100 ms interval before applying the returned ledger settlement.
Release completion remains a separate, later endpoint.
Earlier version 2 runs retain their snapshot analyzers. They do not contain the new interval and frequency evidence.

`artifacts/bound-selected-payment/20261015T163547073-6612a57a` retains the first version 3 target run.
All native checks, three managed groups, eighteen legacy-analysis tests, twenty-eight selected-analysis tests, and three live cases passed.
Source and binary hashes remained unchanged. The selected analyzer verified each case.
A later capture-frequency check also passed the selected suite and offline replay in `rechecked-analysis.json`.

`artifacts/bound-sampled-payment/20261015T163853102-5a1873fe` retains the peer-only check after the shared workload changes.
Zero speedup and invalidation passed. The treatment failed at 2,499,909 ns of observed peer debt.
Cleanup completed without errors. Version 1 replay verified the retained partial evidence and preserved the failed runtime result.
This run does not establish stable peer-only capacity or unchanged observer cost.

## Evidence limits

Current selected runtime records use schema version 3. The legacy analyzer accepts only peer-only version 1 records.
The selected analyzer checks retained credit, receipts, local debt, pause order, source transitions, and cleanup evidence.
It uses managed timestamps and native receipt timestamps separately. It does not infer a clock mapping between them.
The format does not retain every ledger transition. The analyzer cannot reconstruct every intermediate reservation decision.
The checks do not establish stable capacity, general CLR safety, or prediction accuracy.
The high pause count and low credited CPU require an overhead investigation.
The run does not measure an acceptable overhead threshold or prove exact profiler CPU exclusion.
Live native cancellation and release failures remain untested in the selected path.

See [the contract](../../../docsSrc/Reference/bound-sampled-payment.md) and [sample eligibility](../../../docsSrc/Research/cooperative-sample-eligibility.md).
