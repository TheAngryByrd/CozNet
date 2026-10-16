# How to analyze bound sample membership

Install Python 3. Use a finite selected-payment capture with enabled [sample clock evidence](run-bound-selected-payment.md#capture-sample-clock-evidence).
Keep the input and analysis files unchanged while the command runs.

1. Choose an output path that does not exist.
2. Run the analyzer for one case:

   ```powershell
   python native/AnalyzeBoundSampleMembership.py <capture>/50.json <new-report.json>
   ```

3. Check `comparisonDomainAccepted` and `reasons` before using the totals.
4. Check `workloadPassed` and retained failures separately.
5. Compare source membership with `acceptedInFrozenPrefix` in `sampleAccountingGroups`.
6. Inspect `recordedCreditBySourceMembership` for accepted credit outside the source-time window.
7. Inspect `unacceptedSelectedNominalNanoseconds` for retained selected samples inside the window but outside the frozen prefix.
8. Keep the input, report, and analysis modules together with their hashes.

Do not add retrospective credit from the report.
Nominal sample weights do not establish exact selected CPU or prediction accuracy.
See [the reference](../Reference/bound-sample-membership.md) for field definitions and policy limits.
