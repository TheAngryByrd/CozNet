# Target-local sample period

## Finding

After [the progress settlement](target-local-progress-settlement.md), `transaction` run `20261019T183627302-c5843c4c` predicts too little gain at `selected-75`. Its signed median is -11.83 points, and every valid block is between -10.0 and -13.1 points.

These are medians over the passed `selected` experiments of the runs before and after the change, in microseconds for each transaction:

| Run | Speedup | Wall latency | Virtual latency | Global delay | `peer` payment | `peer` skip |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `20261019T175316141-ec3885bd` | 25 | 906.6 | 681.7 | 177.5 | 44.9 | 134.7 |
| `20261019T175316141-ec3885bd` | 50 | 914.6 | 467.0 | 350.5 | 93.6 | 257.2 |
| `20261019T175316141-ec3885bd` | 75 | 1018.9 | 323.6 | 529.9 | 220.1 | 309.9 |
| `20261019T183627302-c5843c4c` | 25 | 928.1 | 712.1 | 173.5 | 44.6 | 129.5 |
| `20261019T183627302-c5843c4c` | 50 | 980.9 | 540.7 | 353.3 | 86.8 | 266.5 |
| `20261019T183627302-c5843c4c` | 75 | 1124.1 | 429.4 | 525.4 | 176.8 | 351.7 |

- The global delay for each transaction does not change. Payment plus skip equals the global delay within 3 microseconds in both runs, so the totals do not show an overshoot.
- The wall latency grows by 21.5, 66.3, and 105.2 microseconds at 25, 50, and 75%. This is consistent with the change moving payments into the windows, but the totals do not show where each payment falls.
- The design samples every 1 ms. At 75%, each `selected` sample raises the global delay by 750 microseconds, and the failure context of block 3 shows these steps.
  The journal also holds small increments of the global delay from other publishes. A transaction lasts about 900 microseconds, so most windows subtract about 0, 750, or 1500 microseconds.
- Before the change, each `selected-75` experiment of the six unpinned `transaction` runs with the field had 3 to 25 transactions with a negative virtual latency, which count as 0. In the four unpinned `spintransaction` runs, the counts were 0 to 12.
  The other points had at most one such transaction, and the runs after the change have none.
  A negative value occurs only when a window subtracts more delay than its wall time. These counts are consistent with the step size already affecting the 75% point. A payment after the end, as in H1 of the settlement plan, can also cause them.

## Hypotheses

- H4, sample granularity: the virtual latency of one transaction is only correct when a delay step is small against the transaction. At 75% the step is 750 microseconds, so the step size distorts the virtual latency. Before the change, the deferred payment and the clipped negative values hid part of this error.
- H5, the settlement rule: the payment at a stamp corrects too much at high speedups for a reason that does not depend on the step size.
- H6, variation between runs: the -11.83 points of one run do not repeat.

H4 predicts that a shorter period moves `selected-75` toward 0. H5 predicts that it stays near -12 points. H6 predicts that a second run at 1 ms does not repeat it.

## Decision

The sample gets a diagnostic sample period, as [worker pinning](target-local-worker-pinning.md) is a diagnostic.
- `native/VerifyTargetLocalProfile.ps1 -PeriodNs 250000` passes `COZNET_SAMPLE_PERIOD_NS=250000` to each arm, and it writes to `artifacts/target-local-<workload>-period`. It accepts only 250000 and only the `transaction` and `spintransaction` workloads. The manifest records `periodNs`.
- The sample reads `COZNET_SAMPLE_PERIOD_NS`. Without it, the period stays 1 ms. A value that is not a whole number from 1 through 1000000000 ns stops the process with exit code 3.
- Each profile record holds `periodNs`. The analyzer marks a block invalid when its profile period differs from the period of the manifest, or from 1 ms when the manifest has none.
- At 75%, a period of 0.25 ms gives steps of 187.5 microseconds, a fifth of a transaction.
- A period of 0.25 ms also gives four times as many samples, so it adds handler cost and journal events, and it makes payments finer. A pass supports H4 only together with these changes. A failure applies only to this intervention.
- `-PeriodNs` and `-WorkerCpus` can be combined, and the folder then ends in `-pinned-period`. The runs of this plan do not combine them.
- A run with the diagnostic period is not a qualification run. The qualification suites keep the period of 1 ms, and the plain arms do not sample, so they do not change.
- The workload, the rule, the bounds, and the criteria of the earlier plans do not change.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 112 and change none.

113. An analyzer check shows that a block is invalid when its profile period differs from the manifest period, and that a record without `periodNs` counts as 1 ms.
114. On the reviewed tree, the `transaction` suite runs once with the default period and once with `-PeriodNs 250000`. Both runs are retained, and criterion 88 applies to each.
115. The test of H4: in the run with 0.25 ms, the signed median of `selected-75` over valid blocks is between -5.9 and 5.9 points, half of 11.83.
     If it is outside, this intervention did not halve the error, and this line of work stops. That result alone does not show that H4 is false.
     The run with 1 ms tests H6: if its `selected-75` signed median is between -5.9 and 5.9 points, the earlier result did not repeat.
116. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. No run is repeated to replace a failure.

See [target-local progress settlement](target-local-progress-settlement.md) and [target-local status](target-local-status.md).
