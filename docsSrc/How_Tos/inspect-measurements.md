# Inspect measurement evidence

Use a new output directory to retain previous results:

```sh
dotnet run --project samples/CozNet.Serial -c Release -- artifacts/serial-next
```

Compare predictions with measured reductions. Inspect block ranges, null interventions, coverage, and GC observations.
Keep failing results. Read the [model limits](../Explanations/Background.md) before interpreting a discrepancy.
