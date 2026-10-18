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
    ("20261018T162737768-cfc37f48", "Same", "Join retry, construction before open, and transition host record", "Pass"),
    ("20261018T182717084-bb88b1d8", "Same", "Wait protocol in the ledger and the library",
     "Fail. One `peer-longer` block differs by 12.75 points. Its profiled arm ran 11 ms longer than its siblings."),
    ("20261018T183034093-0968049a", "Same", "Per-thread CPU and wall time diagnostics", "Pass"),
    ("20261018T190000743-76bbee08", "Same", "Barrier abort, post race, and token bound", "Pass"),
    ("20261018T191125911-68e03b4b", "Same", "Barrier errors cancel the run", "Pass"),
    ("20261018T192337237-ab4134f4", "Same", "Run-queue delay diagnostic", "Pass"),
]
TRANSITION_HISTORY = [
    ("20261018T151031617-addf680d", "First check", "Fail. 1 of 10 runs broke the debt bound. Its collection count was flawed."),
    ("20261018T153537759-847ee42d", "Count only collections in the work window", "Pass, 10 of 10"),
    ("20261018T155026644-209a9e05", "Profiler moved into the library", "Fail. Every run stopped on a perf-map parser defect before profiling."),
    ("20261018T155303110-d6c77c03", "Parser accepts the `0x` prefix", "Pass, 10 of 10"),
    ("20261018T161924408-457ccd0e", "Cleanup on failure, native layout check, and payment address check", "Pass, 10 of 10"),
    ("20261018T162853008-0529c4ad", "Join retry, construction before open, and cleanup check", "Pass, 10 of 10"),
    ("20261018T182834143-78a15e0b", "Wait protocol in the ledger and the library",
     "Fail. Run 4 broke the debt bound while the peer accepted no sample for six selected samples."),
    ("20261018T183151879-34460604", "Per-thread CPU and wall time diagnostics", "Pass, 10 of 10"),
    ("20261018T190118893-b2109c5d", "Barrier abort, post race, and token bound", "Fail. Run 8: 2 ms debt bound."),
    ("20261018T191244704-9849fce2", "Barrier errors cancel the run", "Pass, 10 of 10"),
]
BARRIER_HISTORY = [
    ("20261018T182448183-601598d1", "First barrier run", "Pass"),
    ("20261018T183213218-c2268d43", "Per-thread CPU and wall time diagnostics. The prediction plan gained an open-failure note.", "Pass"),
    ("20261018T190140317-c13b65a5", "Barrier abort, post race, and token bound", "Pass"),
    ("20261018T191307038-5d4a0177", "Barrier errors cancel the run", "Pass"),
    ("20261018T191852956-237f7bf8", "Blocking plan rows match the implementation",
     "Fail. `peer-bound` block 5: the peer accepted no sample for five selected samples and broke the 2 ms debt bound. No forced GC runs here."),
    ("20261018T192211666-aeadc5b5", "Run-queue delay diagnostic", "Pass"),
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
    if not summary["inputsUnchanged"] or not summary.get("cleanupPassed") or not summary.get("barrierAbortPassed"):
        raise SystemExit("The transition run changed inputs or failed a lifetime check.")
    check_history(root, "target-local-transitions", TRANSITION_HISTORY, run)
    kernel, cpus = host(directory)
    rows, forced, failed_runs = [], [], []
    all_complete = True
    for o in summary["outcomes"]:
        d = load(directory / f"gc-{o['run']}.json")
        forced.append(o["forcedCollections"])
        complete = all(t["complete"] == 1 and t["lost"] == 0 for t in d["threads"])
        debt = f"{d['replay']['MaxObservedDebt']:,}" if d.get("replay") else "replay rejected"
        failure = "None" if d["ledgerFailure"] == 0 else "`debt_exceeded`" if d["ledgerFailure"] == 1 else str(d["ledgerFailure"])
        gap = max(t["wallNs"] - t["cpuNs"] for t in d["threads"]) / 1e6
        run_ok = (o["exitCode"] == 0 and o["recordExists"] and o["passed"] and o["forcedCollections"] >= 10
                  and o["maxHandlerNs"] < 5000000)
        all_complete = all_complete and complete
        if not run_ok:
            reasons = []
            if not o["passed"] and d["ledgerFailure"] == 0 and o["exitCode"] == 0:
                reasons.append("profile result did not pass")
            if o["exitCode"] != 0 or not o["recordExists"]:
                reasons.append("exit or record")
            if d["ledgerFailure"] == 1:
                reasons.append("2 ms debt bound")
            elif d["ledgerFailure"] != 0:
                reasons.append(f"ledger failure {d['ledgerFailure']}")
            if o["forcedCollections"] < 10:
                reasons.append("fewer than 10 forced collections")
            if o["maxHandlerNs"] >= 5000000:
                reasons.append("5 ms handler bound")
            if not complete:
                reasons.append("incomplete epoch or loss")
            failed_runs.append((o["run"], ", ".join(reasons) or "unclassified"))
        rows.append(f"| {o['run']} | {'Yes' if run_ok else 'No'} | {failure} | {debt} | {o['forcedCollections']} | {int(o['maxHandlerNs']):,} | {gap:.1f} | {'Yes' if complete else 'No'} |")
    failed_text = "; ".join(f"run {r}: {why}" for r, why in failed_runs)
    current = "Pass, 10 of 10" if summary["passed"] else f"Fail. {failed_text}."
    total = 10 * sum(1 for _, _, result in TRANSITION_HISTORY + [(run, change, current)] if result.startswith("Pass"))
    history = "\n".join(f"| `{r}` | {ch} | {res} |" for r, ch, res in TRANSITION_HISTORY + [(run, change, current)])
    verdict = ("**Result: the current check passes.** Earlier failed checks stay part of the evidence." if summary["passed"] else
               f"**Result: the current check fails.** {10 - len(failed_runs)} of 10 runs pass. Failed criteria: {failed_text}.")
    longest = max(int(o["maxHandlerNs"]) for o in summary["outcomes"])
    outcome = (f"Every handler interval stays below 5 ms, and no run records a ledger failure. The longest is {longest:,} ns." if summary["passed"] else
               f"The longest handler interval is {longest:,} ns. Each failed run keeps its journal and its replay result.")
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

{verdict}

| Run | Passed | Ledger failure | Max observed debt (ns) | Forced GCs in work window | Max handler (ns) | Wall minus counted CPU (ms) | Epochs complete, zero loss |
| ---: | --- | --- | ---: | ---: | ---: | ---: | --- |
{chr(10).join(rows)}

{"No run crashed or hung. Every run closed both epochs with zero loss, retired both sessions, and restored the handlers." if all_complete else "At least one run did not close both epochs with zero loss. The table shows which."}
The `cleanup` check passes. A profile whose monitor throws raises `AggregateException`, and a second profile in the same process then passes.
The `barrier-abort` check passes. A worker that throws releases its waiting peer, the run fails within 10 s, and a second profile then passes.
{outcome}
Wall minus counted CPU is the largest per-thread difference. Counted CPU stops at disable and wall time includes the closing payment, so it is coarse.

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
One hypothesis is that the host or the GC deschedules one thread. Profile results now record thread CPU and wall time to test it.

## Consequence

The passing current-design checks hold {total} runs with no failure. Two current-design checks each fail 1 of 10 runs with the same signature as the first failure.
A thread can go without samples long enough to break the 2 ms debt bound. The cause is unknown.
GC-heavy workloads stay outside the supported model. The debt bound stays unchanged.

## Check limits

The current runner requires exactly 10 runs. The first run used an earlier runner revision with 10 runs.
A forced collection starts only while a worker reports that it is working. The count does not show how long each collection overlaps the work.
The checks do not measure prediction accuracy under GC. They do not cover JIT, tiering, ReJIT, thread abort, or process exit.

See [the support boundary](../../../docsSrc/Explanations/target-local-prediction.md) and [the prediction qualification](prediction.md).
"""


def barrier(root, run, change):
    directory = root / "artifacts/target-local-barrier" / run
    summary, manifest = load(directory / "summary.json"), load(directory / "manifest.json")
    if not summary["inputsUnchanged"]:
        raise SystemExit("The barrier run changed its inputs.")
    check_history(root, "target-local-barrier", BARRIER_HISTORY, run)
    kernel, cpus = host(directory)
    rows, blocks, longest = [], [], 0
    expected = {"alternating": "about 40%", "selected-dominant": "about 50%", "peer-bound": "about 0%"}
    for c in summary["configurations"]:
        def pct(value):
            return "n/a" if value is None else f"{value*100:.2f}%"
        def pp(value):
            return "n/a" if value is None else f"{value*100:.2f} pp"
        rows.append(f"| `{c['name']}` | `{c['pattern']}` | {expected[c['pattern']]} | {c['validBlocks']} / 5 | {pct(c['medianPredicted'])} | "
                    f"{pct(c['medianActual'])} | {pp(c['medianAbsoluteDifference'])} | {pp(c['maxAbsoluteDifference'])} | "
                    f"{'n/a' if c['medianSkippedNs'] is None else format(c['medianSkippedNs']/1e6, '.1f') + ' ms'} | {'Pass' if c['passed'] else 'Fail'} |")
        for b in c["blocks"]:
            if not b["valid"]:
                blocks.append(f"| `{c['name']}` | {b['block']} | Invalid: {'; '.join(b['reasons'])} | | | | | | | |")
                continue
            blocks.append(f"| `{c['name']}` | {b['block']} | {b['predicted']*100:.2f}% | {b['actual']*100:.2f}% | {b['difference']*100:+.2f} pp | "
                          f"{b['profileSpeedupGlobalNs']/1e6:.1f} | {b['skippedNs']/1e6:.1f} | {b['zeroHandlerShare']*100:.3f}% | "
                          f"{(b.get('zeroMaxRunDelayNs') or 0)/1e6:.2f} | {(b.get('speedupMaxRunDelayNs') or 0)/1e6:.2f} |")
            longest = max(longest, b["maxHandlerNs"])
    failing = [(c["name"], b) for c in summary["configurations"] for b in c["blocks"] if not b["valid"] or abs(b["difference"]) > 0.10]
    failed_configs = [c["name"] for c in summary["configurations"] if not c["passed"]]
    if summary["passed"]:
        verdict = "**Result: the current run passes.**"
        current = "Pass"
    else:
        parts = [f"`{name}` block {b['block']}" + ("" if b["valid"] else " invalid") +
                 (f" ({b['difference']*100:+.2f} pp; run-queue delay {(b.get('zeroMaxRunDelayNs') or 0)/1e6:.1f} ms at 0% and {(b.get('speedupMaxRunDelayNs') or 0)/1e6:.1f} ms at 50%)" if b["valid"] else "")
                 for name, b in failing]
        verdict = f"**Result: the current run fails** in {', '.join(f'`{n}`' for n in failed_configs)}. Failing blocks: {'; '.join(parts)}."
        current = f"Fail. {'; '.join(parts)}."
    history = "\n".join(f"| `{r}` | {ch} | {res} |" for r, ch, res in BARRIER_HISTORY + [(run, change, current)])
    all_blocks = [b for c in summary["configurations"] for b in c["blocks"]]
    treatment_delay = max(b.get("speedupMaxRunDelayNs") or 0 for b in all_blocks)
    zero_delay = max(b.get("zeroMaxRunDelayNs") or 0 for b in all_blocks)
    delay_missing = any(b.get("runDelayMissing") for b in all_blocks)
    return f"""# Target-local barrier qualification

Evidence: `artifacts/target-local-barrier/{run}`.

`TargetLocalBarrierVerify` ran once with seed {manifest['seed']} on kernel `{kernel}`, x64, with {cpus} CPUs.
The criteria are in [the prediction plan](../../../lode/plans/target-local-prediction.md#barrier-qualification), fixed before the first barrier run.
The runner hashes that file, [the blocking plan](../../../lode/plans/target-local-blocking.md), and the sources. All hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

Two registered threads run 10 phases. In each phase the selected thread runs A rounds and the peer runs B rounds. Both then meet at a `TargetBarrier`.
Each block runs `profile-phases 0`, `profile-phases 50`, `plain-phases <pattern> 100`, and `plain-phases <pattern> 50` in a random order.

| Configuration | Pattern | Expected actual | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Median skipped delay | Result |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
{chr(10).join(rows)}

| Configuration | Block | Predicted | Actual | Difference | Global delay (ms) | Skipped delay (ms) | Handler share at 0% | Run-queue delay at 0% (ms) | Run-queue delay at 50% (ms) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
{chr(10).join(blocks)}

{verdict}
The gate stays unchanged. Each failing block is recorded with its run-queue delays.
A valid block has runs that all exit 0, both epochs complete with zero loss, a valid managed replay, no ledger failure, and all 10 phases complete.
Each profiled run in a valid block records at least one dependency wake. The longest handler interval in a valid block is {longest:,} ns.

## What the skipped delay shows

In `selected-dominant`, the peer finishes each phase early and waits while the selected thread publishes credit.
Each wake skips only to the token that the selected thread posted at the barrier. The skipped delay is about half of the global delay.
Without the wake rule, the peer would owe that delay after each wake.
In `peer-bound`, the selected thread waits instead. Its wakes skip only the peer's published payment overshoot, a few hundred nanoseconds, and the prediction stays near 0%.
In `alternating`, the critical thread changes in every phase. The prediction follows the real gain of about 40%.

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
{history}

Exploratory smoke runs before the criteria used other pattern names and are not part of any result.

## Limits

One host and one kernel. Five blocks for each configuration cannot establish a general error bound.
Only `TargetBarrier` uses the wait protocol. Locks, `Monitor`, tasks, and other waits stay unsupported.
A timed-out or cancelled barrier wait skips nothing. Deterministic checks cover it, but no qualified run exercises a timeout.
A worker that is preempted between its arrival and its block skips nothing for the delay inserted in that window. That biases the prediction low.
The failed run shows that a runnable thread can go without samples long enough to break the 2 ms debt bound, even without forced GC.
Each worker records its kernel run-queue delay. In this run the largest value is {treatment_delay/1e6:.2f} ms in a 50% arm and {zero_delay/1e6:.2f} ms in a 0% arm.
{"Some workers could not read their run-queue delay." if delay_missing else "Every worker read its run-queue delay."}
Run-queue delay adds wall time that the profiler does not remove. A long delay in a 0% arm raises E0 and the prediction. A long delay in a 50% arm lowers it.

See [the blocking plan](../../../lode/plans/target-local-blocking.md) and [the API reference](../../../docsSrc/Reference/target-local-api.md).
"""


def main():
    root = pathlib.Path(__file__).resolve().parent.parent
    out = root / "reliability/features/target-local"
    pages = {"prediction.md": prediction(root, sys.argv[1], sys.argv[3]), "transitions.md": transitions(root, sys.argv[2], sys.argv[3])}
    if len(sys.argv) > 5:
        pages["barrier.md"] = barrier(root, sys.argv[4], sys.argv[5])
    for name, text in pages.items():
        (out / name).write_text(text, newline="\n")


if __name__ == "__main__":
    main()
