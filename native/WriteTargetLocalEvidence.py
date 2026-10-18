"""Write the target-local prediction and transition evidence pages from retained run directories."""
import json
import pathlib
import sys

PREDICTION_HISTORY = [
    ("20261018T135308886-ecb06eba", "Two configurations", "First run", "Pass. Later found to time its arms from different barriers."),
    ("20261018T145436535-35f0e9cb", "Three configurations", "Same barrier for both arms", "Pass"),
    ("20261018T150410931-cfa294a1", "Earlier thread start", "Earlier start for both arms", "Pass"),
    ("20261018T151318850-8709b5a5", "Same", "Forced-GC mode in the sample", "Pass"),
    ("20261018T153621123-8e2e2300", "Same", "Work-window counter in the sample", "Pass"),
    ("20261018T154921544-cbc3c976", "Handler share added", "Profiler moved into the library",
     "Fail. Every profile arm stopped on a perf-map parser defect before profiling."),
    ("20261018T155143041-1a9300c5", "Same", "Parser accepts the `0x` prefix", "Pass"),
    ("20261018T161755678-2da1482f", "Same", "Cleanup on failure, native layout check, and payment address check", "Pass"),
]
TRANSITION_HISTORY = [
    ("20261018T151031617-addf680d", "First check", "Fail. 1 of 10 runs broke the debt bound. Its collection count was flawed."),
    ("20261018T153537759-847ee42d", "Count only collections in the work window", "Pass, 10 of 10"),
    ("20261018T155026644-209a9e05", "Profiler moved into the library", "Fail. Every run stopped on a perf-map parser defect before profiling."),
    ("20261018T155303110-d6c77c03", "Parser accepts the `0x` prefix", "Pass, 10 of 10"),
    ("20261018T161924408-457ccd0e", "Cleanup on failure, native layout check, and payment address check", "Pass, 10 of 10"),
]


def load(path):
    return json.loads(path.read_text())


def host(directory):
    kernel = (directory / "kernel.txt").read_text().split()
    return kernel[2], (directory / "cpus.txt").read_text().strip()


def check_history(root, kind, known, run):
    present = sorted(p.name for p in (root / "artifacts" / kind).iterdir() if p.is_dir())
    listed = sorted([entry[0] for entry in known] + [run])
    if present != listed:
        raise SystemExit(f"The {kind} history differs from the retained runs: {sorted(set(present) ^ set(listed))}")


def prediction(root, run, change):
    directory = root / "artifacts/target-local-prediction" / run
    summary, manifest = load(directory / "summary.json"), load(directory / "manifest.json")
    if not summary["passed"] or not summary["inputsUnchanged"]:
        raise SystemExit("The prediction run did not pass.")
    check_history(root, "target-local-prediction", PREDICTION_HISTORY, run)
    kernel, cpus = host(directory)
    rows, blocks, longest = [], [], 0
    for c in summary["configurations"]:
        rows.append(f"| `{c['name']}` | {c['selectedRounds']} | {c['peerRounds']} | {c['validBlocks']} / 5 | {c['medianPredicted']*100:.2f}% | "
                    f"{c['medianActual']*100:.2f}% | {c['medianAbsoluteDifference']*100:.2f} pp | {c['maxAbsoluteDifference']*100:.2f} pp | Pass |")
        for b in c["blocks"]:
            longest = max(longest, b["maxHandlerNs"])
            blocks.append(f"| `{c['name']}` | {b['block']} | {b['predicted']*100:.2f}% | {b['actual']*100:.2f}% | {b['difference']*100:+.2f} pp | "
                          f"{b['overhead']:.4f} | {b['zeroHandlerShare']*100:.3f}% | {b['profileSpeedupGlobalNs']:,} |")
    overhead = [c["medianOverhead"] for c in summary["configurations"]]
    share = [c["medianZeroHandlerShare"] for c in summary["configurations"]]
    speed_share = [c["medianSpeedupHandlerShare"] for c in summary["configurations"]]
    dominant_blocks = summary["configurations"][2]["blocks"]
    dominant_max = max(b["predicted"] for b in dominant_blocks) * 100
    positive = ", ".join(f"`{c['name']}` {sum(1 for b in c['blocks'] if b['difference'] > 0)} of 5" for c in summary["configurations"])
    history = "\n".join(f"| `{r}` | {k} | {ch} | {res} |" for r, k, ch, res in PREDICTION_HISTORY + [(run, "Same", change, "Pass")])
    dominant = summary["configurations"][2]["medianPredicted"] * 100
    return f"""# Target-local prediction qualification

Evidence: `artifacts/target-local-prediction/{run}`.

`TargetLocalPredictionVerify` ran once with seed {manifest['seed']} on kernel `{kernel}`, x64, with {cpus} CPUs.
The container drops all capabilities, has no network, and uses `seccomp=unconfined`.
The acceptance criteria are in [the prediction plan](../../../lode/plans/target-local-prediction.md). The runner hashes that file with the sources.
All {len(manifest['sources'])} source hashes and {len(manifest['binaries'])} binary hashes stay unchanged during the run.
The sample runs through the library entry point `TargetLocalProfiler.run`. `native/WriteTargetLocalEvidence.py` writes this page.

Each block runs `profile-zero`, `profile-speedup` at 50%, `original`, and `optimized` in a random order.
The prediction is `1 - Es / E0` from effective durations. The actual reduction is `1 - Toptimized / Toriginal`.

| Configuration | Selected rounds | Peer rounds | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
{chr(10).join(rows)}

| Configuration | Block | Predicted | Actual | Difference | Overhead ratio | Handler share at 0% | Global delay (ns) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
{chr(10).join(blocks)}

The pass rule requires 5 valid blocks in each of the three configurations, a median absolute difference of at most 5 percentage points, and every block within 10.
All three configurations pass. Every profile run has complete epochs, zero loss, a valid managed replay, matching global delay and event count, and no ledger failure.
The longest handler interval in any profile run is {longest:,} ns.

In `selected-dominant`, a 50% optimization leaves the selected thread longer than the peer. Over-credit would show as a prediction above 50%.
The median prediction is {dominant:.2f}%. The highest block prediction is {dominant_max:.2f}%, inside the block limit.
{"A median above 50% is the over-credit signature. This run shows it, and five blocks cannot separate it from variation." if dominant > 50 else "The median does not show the over-credit signature."}

## Overhead

The handler share is the sum of every handler interval in both threads divided by elapsed time.
At 0% speedup the handler inserts no delay. Its share is then the measured handler time, and its median is {min(share)*100:.3f}% to {max(share)*100:.3f}% across the three configurations.
At 50% speedup the share also holds the inserted delay, so it is not overhead. Its median is {min(speed_share)*100:.1f}% to {max(speed_share)*100:.1f}%.
The ratio of `profile-zero` elapsed time to `original` elapsed time has medians from {min(overhead):.4f} to {max(overhead):.4f}. That ratio also holds run-to-run variation.
The handler interval excludes signal entry, exit, and kernel work. It can include descheduling inside the handler.
Neither value covers process startup, perf-map writing, or journal copying after the work.

## Run history

Every qualification run is kept. Each change to a hashed source caused one rerun under the current criteria.

| Run | Criteria | Change before the run | Result |
| --- | --- | --- | --- |
{history}

Three exploratory blocks for each of the first two configurations ran before any qualification. They are not part of any result.

## Limits

One host and one kernel. Five blocks for each configuration cannot establish a general error bound.
The program is two CPU-bound threads with no waits before the join. Its methods allocate nothing, so GC is not exercised here.
The `actual` values also vary by several points between blocks with the same work.
Positive differences per configuration: {positive}. Five blocks cannot separate a small bias from variation.
The peer pays by spinning, so payment uses CPU. A host with fewer free CPUs than threads can change the result.

See [the tutorial](../../../docsSrc/Tutorials/Target_Local_Prediction.md) and [the support boundary](../../../docsSrc/Explanations/target-local-prediction.md).
"""


def transitions(root, run, change):
    directory = root / "artifacts/target-local-transitions" / run
    summary = load(directory / "summary.json")
    if not summary["passed"] or not summary["inputsUnchanged"]:
        raise SystemExit("The transition run did not pass.")
    check_history(root, "target-local-transitions", TRANSITION_HISTORY, run)
    kernel, cpus = host(directory)
    total = 10 * sum(1 for _, _, result in TRANSITION_HISTORY + [(run, change, "Pass")] if result.startswith("Pass"))
    rows, forced = [], []
    for o in summary["outcomes"]:
        d = load(directory / f"gc-{o['run']}.json")
        forced.append(o["forcedCollections"])
        complete = all(t["complete"] == 1 and t["lost"] == 0 for t in d["threads"])
        rows.append(f"| {o['run']} | Yes | None | {d['replay']['MaxObservedDebt']:,} | {o['forcedCollections']} | {int(o['maxHandlerNs']):,} | {'Yes' if complete else 'No'} |")
    history = "\n".join(f"| `{r}` | {ch} | {res} |" for r, ch, res in TRANSITION_HISTORY + [(run, change, "Pass, 10 of 10")])
    failed = root / "artifacts/target-local-transitions/20261018T151031617-addf680d/gc-4.json"
    debt = sorted(load(failed)["events"], key=lambda e: e[1])
    observed = next(e for e in debt if e[2] == 4)
    return f"""# Target-local forced-GC transitions

Evidence: `artifacts/target-local-transitions/{run}`.

`TargetLocalTransitionsVerify` ran `profile-gc 50 6000 3000 5` ten times in the Linux x64 container.
The run records kernel `{kernel}`, x64, with {cpus} CPUs. Earlier transition runs did not record the host.
While at least one profiled thread is in its work phase, the main thread forces a blocking gen-2 GC every 5 ms.
The criteria are in [the transition plan](../../../lode/plans/target-local-transitions.md). The runner hashes that file, and all source hashes stay unchanged.
The sample runs through the library entry point `TargetLocalProfiler.run`. `native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current check passes, and an earlier check failed.** The failure stays part of the evidence.

| Run | Passed | Ledger failure | Max observed debt (ns) | Forced GCs in work window | Max handler (ns) | Epochs complete, zero loss |
| ---: | --- | --- | ---: | ---: | ---: | --- |
{chr(10).join(rows)}

No run crashed or hung. Every run closed both epochs with zero loss, retired both sessions, and restored the handlers.
The `cleanup` check passes. A profile whose monitor throws raises `AggregateException`, and a second profile in the same process then passes.
Every handler interval stays below 5 ms, and no run records a ledger failure.

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
{history}

## Earlier failed check

The first check counted collections from thread start to join. At least 5 ms passes between its forced collections.
Its 42 to 66 collections therefore needed 210 to 330 ms of thread lifetime, while its timed work lasted about 110 ms.
Most of its collections fell outside profiling, so that check did not measure what it claimed.

That check also failed. Nine of ten runs passed. Run 4 recorded `debt_exceeded`.
In sequences 71 through 80 of run 4, the selected thread accepts five selected samples and publishes 2.5 ms of credit.
The journal holds no peer sample in that interval. Sequence {observed[1]} records the failure at {observed[3] - observed[4]:,} ns of debt.
The ledger then pays the debt in bounded children. It forgives nothing.
The journal has no scheduling or GC suspension times, so it cannot show why the peer accepted no sample.
One hypothesis is that the GC suspended and resumed the two threads at different times. No check can test it yet.

## Consequence

The current-design checks show no failure in {total} runs, with {min(forced)} to {max(forced)} forced collections during profiling in the latest run.
The earlier failure shows that a thread can go without samples long enough to break the 2 ms debt bound. Its cause is unknown.
GC-heavy workloads stay outside the supported model. The debt bound stays unchanged.

## Check limits

The current runner requires exactly 10 runs. The first run used an earlier runner revision with 10 runs.
A forced collection starts only while a worker reports that it is working. The count does not show how long each collection overlaps the work.
The checks do not measure prediction accuracy under GC. They do not cover JIT, tiering, ReJIT, thread abort, or process exit.

See [the support boundary](../../../docsSrc/Explanations/target-local-prediction.md) and [the prediction qualification](prediction.md).
"""


def main():
    root = pathlib.Path(__file__).resolve().parent.parent
    out = root / "reliability/features/target-local"
    (out / "prediction.md").write_text(prediction(root, sys.argv[1], sys.argv[3]), newline="\n")
    (out / "transitions.md").write_text(transitions(root, sys.argv[2], sys.argv[3]), newline="\n")


if __name__ == "__main__":
    main()
