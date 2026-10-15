# How to reconcile frozen timer prefixes

Use this procedure with a sampled timer capture that includes frozen `Accounting.Timer` records.
Install Python 3.10 or later.
Run the command from the repository root.
Choose an output path that does not exist.

```text
python native/AnalyzeTimerPrefixes.py capture.json timer-prefix-report.json
```

The command writes a JSON report and returns zero when the recorded prefixes reconcile.
It returns one when reconciliation fails or the output cannot be created.
It does not overwrite an existing report.

Check `reconciled` and `reasons` first.
Check `recordedWorkloadFailures` before interpreting the worker totals.
Keep the original capture with the report.

A successful reconciliation does not establish native tail bounds or prediction accuracy.
Use the [field reference](../Reference/timer-prefix-reconciliation.md) to interpret unknown suffixes and later acceptance.
