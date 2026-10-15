# How to inspect recorded timer wait membership

Use a sampled timer capture with clock evidence, frozen timer prefixes, and a closed final journal inventory.
Install Python 3.10 or later.
Run the command from the repository root with a new output path.

```text
python native/AnalyzeTimerWaitMembership.py capture.json timer-wait-report.json
```

The command returns zero when the comparison domain passes.
It returns one on rejected input or output failure.
It does not overwrite an existing report.

Check `comparisonDomainAccepted` and `reasons` first.
Then compare each worker's `recordedWaitInObservedWindow` with `frozenAcceptedWaitDelta`.
Keep the workload failures with the report.

These ranges do not establish prediction accuracy.
See [the field reference](../Reference/timer-wait-membership.md) for the clock assumptions and range calculation.
