# Matched physical prediction

Coz estimates optimization effects by removing injected global delay from elapsed time and dividing by progress.
CozNet's matched experiment uses the selected worker's completed checksum batches as its progress point.
It compares zero-percent and 50-percent sampled cases with uninstrumented baseline and selected-worker optimization cases.
The peer runs the original arithmetic in every case.
This measures the selected progress point under a controlled two-worker workload, not arbitrary application throughput.

The global-delay correction is a measurement-window delta.
Shutdown can publish later waits and progress, so shutdown totals cannot replace the frozen endpoints.
Summing worker waits would also count concurrent payments more than once.
Boundary brackets and one-batch progress quantization describe uncertainty in the captured endpoints.

These brackets do not bound all attribution error.
Samples can represent CPU periods outside their receipt time. Native handlers can finish journal entries after the controller's last accepted prefix.
A successful shutdown does not assign those intervals to the earlier measurement window.
The analyzer therefore rejects treatment eligibility until sample and journal tails have explicit, verified bounds.

The retained interleaved calibration narrows observed variation in the candidate physical optimization.
It does not prove an exact 50-percent local reduction or bound clock and measurement overhead.
Those uncertainties must also enter the eventual two-percentage-point prediction-error gate.
Failed cases cannot disappear from the eight-block series or be replaced by retries.

The diagnostic runner implements the full collection matrix. Accurate paired prediction remains unfinished.
See [the formula reference](../Reference/matched-prediction.md), [the procedure](../How_Tos/run-matched-prediction.md), and [the port decision](../Research/coz-port-and-il-rewriting.md).
