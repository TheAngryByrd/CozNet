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
It can still compare the observed costs conditionally within a complete block.
This diagnostic exposes the direction and size of the observed difference without claiming an accuracy bound.

The retained interleaved calibration narrows observed variation in the candidate physical optimization.
It does not prove an exact 50-percent local reduction or bound clock and measurement overhead.
Those uncertainties must also enter the eventual two-percentage-point prediction-error gate.
Failed cases cannot disappear from the eight-block series or be replaced by retries.

The diagnostic runner implements the full collection matrix. Accurate paired prediction remains unfinished.

Coz estimates selected CPU from periodic samples. Its paper writes this relationship as an approximation, `s ≈ n*t/P`.
A sampled instruction does not identify every instruction executed during the preceding CPU period.
Exact credit accounting and bounded measurement edges do not establish that the sampling estimate is unbiased.
Physical comparisons must test that estimate together with intervention behavior and profiler overhead.
The conditional block ranges include endpoint uncertainty only. They do not measure variation between blocks.

The [Coz paper, section 3.4](https://arxiv.org/html/1608.03676v1) states the sampling approximation.
The [native engine](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp)
credits matched samples and corrects elapsed duration with global delay.
The [JCoz handler](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc)
also credits a nominal interval for a matching stack. Neither inspected path supplies CozNet's finite-window accuracy bound.
See [the formula reference](../Reference/matched-prediction.md), [the procedure](../How_Tos/run-matched-prediction.md), and [the port decision](../Research/coz-port-and-il-rewriting.md).
