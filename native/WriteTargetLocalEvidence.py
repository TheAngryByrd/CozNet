"""Write the target-local prediction and transition evidence pages from retained run directories."""
import json
import statistics
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
    ("20261018T192337237-ab4134f4", "Same", "Run-queue delay diagnostic", "Pass. Median overhead ratio rose to about 1.05."),
    ("20261018T193237454-cc14a621", "Same", "Run-queue delay excludes failed reads", "Pass. Median overhead ratio about 1.06 to 1.08."),
    ("20261018T202523516-b9c53a5a", "Same", "Code binding enforced in every profile mode", "Pass. Median overhead ratio about 1.05 to 1.09."),
    ("20261018T203002656-dab28501", "Same", "Run-queue reads outside the timed window", "Pass. Median overhead ratio about 0.99 to 1.02."),
    ("20261018T204012284-1a60b3d6", "Same", "SMT diagnostic note in the plan", "Pass"),
    ("20261018T210529693-6820c454", "Same", "Review fixes: perf-map setting check, checked-run seam, shutdown work-state marker, and wording",
     "Fail. `peer-longer` 5 valid blocks, median 5.69 pp, block differences -5.7, -5.9, -3.7, -7.0, -2.3."),
    ("20261018T214023824-28b274dc", "Same", "Per-wait run-queue delay, pread reader, and LF-normalized source hashes, before review fixes",
     "Fail. `peer-longer` block 1 +16.79 pp: its plain `optimized` arm ran 118.5 ms against 104.1 ms for `original`."),
    ("20261018T221254076-9a1e3880", "Same", "Review fixes: wait copy after the finish time, scalar parser, debt-exceeded and dependency-wake pairing", "Pass"),
    ("20261018T233840885-22a9d378", "Same", "Start run-queue delay diagnostic",
     "Fail. `selected-dominant` block 5 invalid: the peer started work 5.3 ms late, with 8.3 microseconds of start run-queue delay, and its first sample recorded a 3 ms debt failure."),
    ("20261019T001613652-a3f06d98", "Same", "Start trace, with on-CPU time from schedstat field 1, before the revision", "Pass"),
    ("20261019T002411461-b833589f", "Same", "Start trace with the thread CPU clock", "Pass"),
    ("20261019T004805873-c97ab2e5", "Same", "Thread state set before the native start, criterion 27 for barrier runs",
     "Fail. Criterion 26: one of 60 setup segments takes 0.60 ms, with a median of 0.0049 ms. All 15 blocks are within the limits."),
    ("20261019T011552600-3ac91378", "Same", "Forced-GC windows", "Pass"),
    ("20261019T014112845-eec890b5", "Same", "ReJIT detection check", "Pass"),
    ("20261019T020044104-92e8b8b1", "Same", "Linux backend file split and lifecycle plan update", "Pass"),
    ("20261019T024239257-fae8a160", "Same", "Causal profile driver", "Pass"),
    ("20261019T032259730-367c4746", "Same", "Throughput progress points", "Pass"),
    ("20261019T040632196-702f8052", "Same", "Blocking queue and pipeline profile", "Pass"),
    ("20261019T043903739-5859c385", "Same", "Per-thread experiment diagnostics", "Pass"),
    ("20261019T051231424-1e546bce", "Same", "Profile warm-up experiment", "Pass"),
    ("20261019T060749814-6eba8ca5", "Same", "Rotated experiment order and warm-up gate", "Pass"),
    ("20261019T063130911-86311728", "Same", "TargetLock and lock profile", "Pass"),
    ("20261019T070800008-220def3b", "Same", "Lock handoff counts", "Fail. `peer-longer` has 4 valid blocks. In block 4, the peer breaks the 2 ms debt bound 1.38 ms after its work start, with 3.75 ms of start run-queue delay."),
    ("20261019T075004985-14232a40", "Same", "Spin before a lock wait", "Pass"),
    ("20261019T082954122-98c31e2d", "Same", "TargetSemaphore and semaphore profile", "Pass"),
    ("20261019T091027391-fbd17a7d", "Same", "Failure and GC records", "Pass"),
    ("20261019T094506888-d9c14620", "Same", "Failure context", "Fail. `peer-longer` has one block of -12.57 points, and `selected-dominant` block 5 is invalid from a startup debt failure."),
    ("20261019T110837795-78ce6bcb", "Same", "Latency progress points", "Pass"),
    ("20261019T114345641-a76a3c68", "Same", "Per-transaction virtual latency", "Pass"),
    ("20261019T121854888-ee18e87d", "Same", "Transaction queue handoff", "Pass"),
    ("20261019T125645104-7e8dced4", "Same", "Spin before a queue wait", "Pass"),
    ("20261019T132443343-058778d3", "Same", "Handoff parts", "Pass"),
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
    ("20261018T192454148-24d37dd2", "Run-queue delay diagnostic", "Pass, 10 of 10"),
    ("20261018T202431027-b539e1a4", "Code binding, tiered rejection, and shutdown checks", "Pass, 10 of 10"),
    ("20261018T203245961-56927b66", "Run-queue reads outside the timed window", "Pass, 10 of 10"),
    ("20261018T210459943-dfa8e472", "Review fixes: perf-map setting check, checked-run seam, shutdown work-state marker, and wording", "Pass, 10 of 10"),
    ("20261018T213950841-bc5dbc42", "Per-wait run-queue delay, pread reader, and LF-normalized source hashes, before review fixes", "Fail. Run 4: 2 ms debt bound under 19 forced GCs."),
    ("20261018T221219867-6603996f", "Review fixes: wait copy after the finish time, scalar parser, debt-exceeded and dependency-wake pairing", "Pass, 10 of 10"),
    ("20261018T233751688-5acdb8cc", "Start run-queue delay diagnostic", "Pass, 10 of 10"),
    ("20261019T001542478-b4d2cdb6", "Start trace, with on-CPU time from schedstat field 1, before the revision",
     "Fail. Run 8: 2 ms debt bound at 39 ms, after a 6.5 ms peer gap with no event. The peer has 5.3 ms of residual run-queue delay."),
    ("20261019T002340083-c152f4ca", "Start trace with the thread CPU clock", "Pass, 10 of 10"),
    ("20261019T004724440-fd9cd00c", "Thread state set before the native start, criterion 27 for barrier runs", "Pass, 10 of 10"),
    ("20261019T011520304-41a7c250", "Forced-GC windows", "Pass, 10 of 10"),
    ("20261019T014043978-f7a0ddbc", "ReJIT detection check",
     "Fail. Run 7: 2 ms debt bound. GC windows cover 0.22 ms of the 5.75 ms failure interval. The ReJIT check passes."),
    ("20261019T020010673-c0788652", "Linux backend file split and lifecycle plan update", "Pass, 10 of 10"),
    ("20261019T024210440-65ed47ee", "Causal profile driver", "Pass, 10 of 10"),
    ("20261019T032230942-d027dbb5", "Throughput progress points", "Fail. Run 10: 2 ms debt bound. One GC window covers 1.16 ms of the 5.77 ms failure interval."),
    ("20261019T040604415-1a034540", "Blocking queue and pipeline profile", "Fail. Run 9: 2 ms debt bound. Two GC windows cover 0.91 ms of the 6.50 ms failure interval."),
    ("20261019T043835718-5c15e486", "Per-thread experiment diagnostics", "Pass, 10 of 10"),
    ("20261019T051202514-ddf03668", "Profile warm-up experiment", "Fail. Run 1: 2 ms debt bound. One GC window covers 1.31 ms of the 5.26 ms failure interval."),
    ("20261019T060721421-9e406e92", "Rotated experiment order and warm-up gate", "Pass, 10 of 10"),
    ("20261019T063102813-1448d8f5", "TargetLock and lock profile", "Pass, 10 of 10"),
    ("20261019T070731753-86c6ac1e", "Lock handoff counts", "Pass, 10 of 10"),
    ("20261019T074936096-7103de7f", "Spin before a lock wait", "Pass, 10 of 10"),
    ("20261019T082925705-0e9bf3e5", "TargetSemaphore and semaphore profile", "Pass, 10 of 10"),
    ("20261019T090959282-d4b0da38", "Failure and GC records", "Pass, 10 of 10"),
    ("20261019T094438941-7638b960", "Failure context", "Pass, 10 of 10"),
    ("20261019T110806692-b4b671d5", "Latency progress points", "Pass, 10 of 10"),
    ("20261019T114316513-16ceb9b8", "Per-transaction virtual latency", "Pass, 10 of 10"),
    ("20261019T121826636-adddad30", "Transaction queue handoff", "Fail, 9 of 10. Run 7 breaks the 2 ms debt bound."),
    ("20261019T125615160-ba0dd35e", "Spin before a queue wait", "Pass, 10 of 10"),
    ("20261019T132415383-4b7b1039", "Handoff parts", "Pass, 10 of 10"),
]
BARRIER_HISTORY = [
    ("20261018T182448183-601598d1", "First barrier run", "Pass"),
    ("20261018T183213218-c2268d43", "Per-thread CPU and wall time diagnostics. The prediction plan gained an open-failure note.", "Pass"),
    ("20261018T190140317-c13b65a5", "Barrier abort, post race, and token bound", "Pass"),
    ("20261018T191307038-5d4a0177", "Barrier errors cancel the run", "Pass"),
    ("20261018T191852956-237f7bf8", "Blocking plan rows match the implementation",
     "Fail. `peer-bound` block 5: the peer accepted no sample for five selected samples and broke the 2 ms debt bound. No forced GC runs here."),
    ("20261018T192211666-aeadc5b5", "Run-queue delay diagnostic", "Pass"),
    ("20261018T193110908-a2f5b436", "Run-queue delay excludes failed reads",
     "Fail. `barrier-alternating` blocks 4 and 5 differ by +11.73 and -10.77 points, with 81.3 ms and 17.1 ms of run-queue delay in one arm each."),
    ("20261018T202640118-21860ac2", "Code binding enforced in every profile mode",
     "Fail. `peer-bound` blocks 4 and 5 differ by +12.29 and +7.85 points. The run-queue diagnostic read inside the timed window raised overhead."),
    ("20261018T203120679-31605bcf", "Run-queue reads outside the timed window",
     "Fail. `peer-bound` median 5.28 pp, with every difference positive."),
    ("20261018T204129945-675e5b4b", "SMT diagnostic note in the prediction plan and the `-CpuSet` option", "Pass"),
    ("20261018T210647512-cd99aa09", "Review fixes: perf-map setting check, checked-run seam, shutdown work-state marker, and wording",
     "Fail. `barrier-selected-dominant` block 1 invalid: a 5 ms debt failure at the peer's first sample, with 82.9 ms of whole-run run-queue delay."),
    ("20261018T214145452-1972f479", "Per-wait run-queue delay, pread reader, and LF-normalized source hashes, before review fixes",
     "Fail. `peer-bound` block 5 +12.18 pp. Its arms vary by about 7%, with at most 0.34 ms of run-queue delay in the profiled arms."),
    ("20261018T221415876-73afdb27", "Review fixes: wait copy after the finish time, scalar parser, debt-exceeded and dependency-wake pairing",
     "Fail. `barrier-alternating` block 3 invalid: a 3 ms debt failure at the peer's first sample, before its first wake, with 6.7 ms of whole-run run-queue delay."),
    ("20261018T234001547-747aae56", "Start run-queue delay diagnostic", "Pass"),
    ("20261019T001733954-3bae192a", "Start trace, with on-CPU time from schedstat field 1, before the revision", "Pass"),
    ("20261019T002531212-f435da11", "Start trace with the thread CPU clock", "Fail. Criterion 14: all 300 wait values are zero. All 15 blocks are within the limits, and 27 of 60 whole-run delays are above zero."),
    ("20261019T004925668-914e6b56", "Thread state set before the native start, criterion 27 for barrier runs", "Pass"),
    ("20261019T011713329-52b0bbca", "Forced-GC windows", "Pass"),
    ("20261019T014236242-6440bd58", "ReJIT detection check", "Pass"),
    ("20261019T020204625-4ef1deb4", "Linux backend file split and lifecycle plan update", "Pass"),
    ("20261019T024401530-b69ae382", "Causal profile driver",
     "Fail. Criterion 26: one of 60 setup segments takes 0.70 ms, with 9.3 microseconds on a CPU. All 15 blocks are within the limits."),
    ("20261019T032421198-d2441fed", "Throughput progress points", "Pass"),
    ("20261019T040754718-d668c4c1", "Blocking queue and pipeline profile", "Pass"),
    ("20261019T044024104-2513fe0c", "Per-thread experiment diagnostics", "Fail. `barrier-selected-dominant` block 2 invalid: a debt failure 2 microseconds after a wake whose wait has 7.40 ms of run-queue delay."),
    ("20261019T051353871-0dc405c5", "Profile warm-up experiment", "Pass"),
    ("20261019T060913007-5812aec4", "Rotated experiment order and warm-up gate", "Pass"),
    ("20261019T063253419-973e8541", "TargetLock and lock profile", "Pass"),
    ("20261019T070924325-199df270", "Lock handoff counts", "Pass"),
    ("20261019T075126709-39bc016f", "Spin before a lock wait", "Pass"),
    ("20261019T083116455-261fde3b", "TargetSemaphore and semaphore profile", "Pass"),
    ("20261019T091147974-ff48eb23", "Failure and GC records", "Pass"),
    ("20261019T094627976-f1d19c78", "Failure context", "Pass"),
    ("20261019T111000032-74adbcce", "Latency progress points", "Fail. `barrier-peer-bound` median 5.10 points."),
    ("20261019T114508965-bf1d556a", "Per-transaction virtual latency", "Pass"),
    ("20261019T122016119-859682da", "Transaction queue handoff", "Pass"),
    ("20261019T125807312-5f10cdcc", "Spin before a queue wait", "Pass"),
    ("20261019T132604565-2f365b47", "Handoff parts", "Pass"),
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


def pct(value, digits=2, suffix="%"):
    return "n/a" if value is None else f"{value*100:.{digits}f}{suffix}"


def prediction(root, run, change):
    directory = root / "artifacts/target-local-prediction" / run
    summary, manifest = load(directory / "summary.json"), load(directory / "manifest.json")
    if not summary["inputsUnchanged"]:
        raise SystemExit("The prediction run changed its inputs.")
    check_history(root, "target-local-prediction", PREDICTION_HISTORY, run)
    kernel, cpus = host(directory)
    rows, blocks, longest = [], [], 0
    for c in summary["configurations"]:
        rows.append(f"| `{c['name']}` | {c['selectedRounds']} | {c['peerRounds']} | {c['validBlocks']} / 5 | {pct(c.get('medianPredicted'))} | "
                    f"{pct(c.get('medianActual'))} | {pct(c.get('medianAbsoluteDifference'), suffix=' pp')} | {pct(c.get('maxAbsoluteDifference'), suffix=' pp')} | {'Pass' if c['passed'] else 'Fail'} |")
        for b in c["blocks"]:
            if not b["valid"]:
                blocks.append(f"| `{c['name']}` | {b['block']} | invalid: {', '.join(b.get('reasons') or [])} | | | | | |")
                continue
            longest = max(longest, b["maxHandlerNs"])
            blocks.append(f"| `{c['name']}` | {b['block']} | {b['predicted']*100:.2f}% | {b['actual']*100:.2f}% | {b['difference']*100:+.2f} pp | "
                          f"{b['overhead']:.4f} | {b['zeroHandlerShare']*100:.3f}% | {b['profileSpeedupGlobalNs']:,} |")
    overhead = [c["medianOverhead"] for c in summary["configurations"] if c.get("medianOverhead") is not None] or [float("nan")]
    share = [c["medianZeroHandlerShare"] for c in summary["configurations"] if c.get("medianZeroHandlerShare") is not None] or [float("nan")]
    speed_share = [c["medianSpeedupHandlerShare"] for c in summary["configurations"] if c.get("medianSpeedupHandlerShare") is not None] or [float("nan")]
    dominant_blocks = [b for b in summary["configurations"][2]["blocks"] if b["valid"]]
    dominant_max = max((b["predicted"] for b in dominant_blocks), default=float("nan")) * 100
    positive = ", ".join(f"`{c['name']}` {sum(1 for b in c['blocks'] if b['valid'] and b['difference'] > 0)} of {c['validBlocks']}" for c in summary["configurations"])
    failed = [c for c in summary["configurations"] if not c["passed"]]
    if not summary["passed"] and not failed:
        detail = run_level_detail(summary)
        current = f"Fail. {detail.capitalize()}."
        verdict = f"**Result: the current run fails.** {detail.capitalize()}. The run is kept, and no rerun replaces it."
    elif failed:
        detail = "; ".join(f"`{c['name']}` {c['validBlocks']} valid blocks, median {pct(c.get('medianAbsoluteDifference'), suffix=' pp')}, block differences "
                           + ", ".join(f"{b['difference']*100:+.1f}" if b["valid"] else f"invalid ({', '.join(b.get('reasons') or [])})" for b in c["blocks"])
                           + "".join(f", block {b['block']} outside the 10-point limit" for b in c["blocks"] if b["valid"] and abs(b["difference"]) > 0.10)
                           for c in failed)
        current = f"Fail. {detail}."
        verdict = f"**Result: the current run fails.** {detail}. The run is kept, and no rerun replaces it."
        if all(c["validBlocks"] == 5 for c in summary["configurations"]):
            verdict += ("\nEvery profile run still has complete epochs, zero loss, a valid managed replay, "
                        "matching global delay and event count, and no ledger failure.")
    else:
        current = "Pass"
        verdict = ("All three configurations pass. Every profile run has complete epochs, zero loss, a valid managed replay, "
                   "matching global delay and event count, and no ledger failure.")
    history = "\n".join(f"| `{r}` | {k} | {ch} | {res} |" for r, k, ch, res in PREDICTION_HISTORY + [(run, "Same", change, current)])
    dominant_median = summary["configurations"][2].get("medianPredicted")
    dominant = (float("nan") if dominant_median is None else dominant_median) * 100
    return f"""# Target-local prediction qualification

Evidence: `artifacts/target-local-prediction/{run}`.

`TargetLocalPredictionVerify` ran once with seed {manifest['seed']} on kernel `{kernel}`, x64, with {cpus} CPUs.
The container drops all capabilities, has no network, and uses `seccomp=unconfined`.
The acceptance criteria are in [the prediction plan](../../../lode/plans/target-local-prediction.md). The runner hashes that file with the sources.
All {len(manifest['sources'])} source hashes and {len(manifest['binaries'])} binary hashes stay unchanged during the run.
The sample runs through the checked library entry point `TargetLocalProfiler.runBound` with a `CodeBinding`. `native/WriteTargetLocalEvidence.py` writes this page.

Each block runs `profile-zero`, `profile-speedup` at 50%, `original`, and `optimized` in a random order.
The prediction is `1 - Es / E0` from effective durations. The actual reduction is `1 - Toptimized / Toriginal`.

| Configuration | Selected rounds | Peer rounds | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
{chr(10).join(rows)}

| Configuration | Block | Predicted | Actual | Difference | Overhead ratio | Handler share at 0% | Global delay (ns) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
{chr(10).join(blocks)}

The pass rule requires 5 valid blocks in each of the three configurations, a median absolute difference of at most 5 percentage points, and every block within 10.
{verdict}
The longest handler interval in any profile run of a valid block is {longest:,} ns.

In `selected-dominant`, a 50% optimization leaves the selected thread longer than the peer. Over-credit would show as a prediction above 50%.
The median prediction is {dominant:.2f}%. The highest block prediction is {dominant_max:.2f}%.{' Every block is inside the block limit.' if all(abs(b['difference']) <= 0.10 for b in dominant_blocks) and len(dominant_blocks) == 5 else ' At least one block is invalid or outside the block limit.'}
{"A median above 50% is the over-credit signature. This run shows it, and five blocks cannot separate it from variation." if dominant > 50 else "The median does not show the over-credit signature."}

## Run-queue delay

{wait_section(summary)}

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
    lifecycle = summary.get("lifecycle", {})
    lifecycle_failed = [k[:-len("Passed")] for k, v in lifecycle.items() if k.endswith("Passed") and not v]
    failed_text = "; ".join([f"run {r}: {why}" for r, why in failed_runs] + [f"lifecycle check {k}" for k in lifecycle_failed]
                            + (["the GC window records are incomplete"] if summary.get("gcWindowsComplete") is False else []))
    current = "Pass, 10 of 10" if summary["passed"] else f"Fail. {failed_text}."
    total = 10 * sum(1 for _, _, result in TRANSITION_HISTORY + [(run, change, current)] if result.startswith("Pass"))
    debt_failures = ["No", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve"][sum(1 for _, _, result in (TRANSITION_HISTORY + [(run, change, current)])[1:] if "debt bound" in result)]
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
The sample runs through the checked library entry point `TargetLocalProfiler.runBound` with a `CodeBinding`. `native/WriteTargetLocalEvidence.py` writes this page.

{verdict}

| Run | Passed | Ledger failure | Max observed debt (ns) | Forced GCs in work window | Max handler (ns) | Wall minus counted CPU (ms) | Epochs complete, zero loss |
| ---: | --- | --- | ---: | ---: | ---: | ---: | --- |
{chr(10).join(rows)}

{"No run crashed or hung. Every run closed both epochs with zero loss, retired both sessions, and restored the handlers." if all_complete else "At least one run did not close both epochs with zero loss. The table shows which."}
The `cleanup` check passes. A profile whose monitor throws raises `AggregateException`, and a second profile in the same process then passes.
The `barrier-abort` check passes. A worker that throws releases its waiting peer, the run fails within 10 s, and a second profile then passes.
{outcome}
Wall minus counted CPU is the largest per-thread difference. Counted CPU stops at disable and wall time includes the closing payment, so it is coarse.

## GC windows

{gc_section(directory)}

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

The passing current-design checks hold {total} runs with no failure. {debt_failures} current-design checks each fail 1 of 10 runs by the 2 ms debt bound, the same bound as the first failure.
A thread can go without samples long enough to break the 2 ms debt bound. The cause is unknown.
GC-heavy workloads stay outside the supported model. The debt bound stays unchanged.

## Check limits

The current runner requires exactly 10 runs. The first run used an earlier runner revision with 10 runs.
A forced collection starts only while a worker reports that it is working. The count does not show how long each collection overlaps the work.
The checks do not measure prediction accuracy under GC.

## Lifecycle checks

{chr(10).join(f"- `{k}`: {'pass' if v else 'fail'}" for k, v in lifecycle.items() if k.endswith("Passed"))}

`bindingTiered` runs with tiering on and requires both a configuration error and a rejected capture. `bindingDefault` requires a capture that still verifies after 100 ms of work.
`shutdown` requires an exit through `Environment.Exit` while a worker is still working. In this run the exit code is {lifecycle.get('shutdownExitCode')}. The check does not show that a handler was inside a payment spin at the exit.
{"`rejit` loads a test CLR profiler that requests a ReJIT of `Work.selected` during the run. It requires the ReJIT to finish and the run to fail with `BindingError`. See [the ReJIT plan](../../../lode/plans/target-local-rejit.md)." if "rejitPassed" in lifecycle else "This run predates the ReJIT check."}
The checks do not cover thread abort. See [the lifecycle plan](../../../lode/plans/target-local-lifecycle.md).

See [the support boundary](../../../docsSrc/Explanations/target-local-prediction.md) and [the prediction qualification](prediction.md).
"""


CPUSET_RUN = "20261018T203443644-3be38dc4"


def gc_section(directory):
    path = directory / "gc-summary.json"
    if not path.exists():
        if "gcWindowsComplete" in load(directory / "summary.json"):
            return "The GC analyzer wrote no summary. See `gc-analysis.log` in the run directory."
        return "This run predates the GC window records."
    gc = load(path)
    state = "complete" if gc["gcWindowsComplete"] else "incomplete"
    rows = []
    for r in gc["runs"]:
        gap = max(r["largestGaps"], key=lambda g: g["gapNs"], default=None)
        failures = "; ".join(f"worker {f['worker']}: {len(f['windows'])} windows, {fine_ms(f['overlapNs'])} of {fine_ms(f['intervalNs'])}"
                             for f in r["debtFailures"]) or "none"
        rows.append(f"| {r['run']} | {'Yes' if r['complete'] else 'No'} | {r['windows']} | {fine_ms(r['windowNs'])} | {fine_ms(r['pauseNs'])} | "
                    + (f"worker {gap['worker']}: {fine_ms(gap['gapNs'])} with {fine_ms(gap['overlapNs'])} in windows" if gap else "none")
                    + f" | {failures} |")
    return (f"The GC window records are {state}. Each forced `GC.Collect` call is bracketed by `Stopwatch` reads on the monitor thread.\n"
            "The pause total from `GC.GetTotalPauseDuration` also counts collections that the sample does not force.\n"
            "A debt failure lists the window time inside the interval from the sample before the failing sample to the failure. For a failure at the first sample of a worker, the interval starts at the native start of that worker. An incomplete record reports no windows.\n\n"
            "| Run | Complete | Windows | Window total | Pause total | Largest event gap | Debt failures |\n"
            "| ---: | --- | ---: | ---: | ---: | --- | --- |\n" + "\n".join(rows))


def run_level_detail(summary, barrier=False):
    if barrier and summary.get("readerFresh") is False:
        return "the readable wait values and whole-run run-queue delays sum to zero, so criterion 27 fails"
    if barrier and "readerFresh" not in summary and summary.get("totalWaitRunDelayNs", 1) == 0:
        return "the readable wait values sum to zero, so criterion 14 fails"
    if summary.get("startSetupWithinLimit") is False:
        return (f"the setup segments are outside the limits of criterion 26, with a median of {fine_ms(summary.get('medianSetupNs'))} "
                f"and a largest value of {fine_ms(summary['largestStartSegmentNs']['setup'])}")
    if not summary.get("waitDiagnosticsComplete", True):
        return "the wait diagnostics are incomplete"
    if not summary.get("startDiagnosticsComplete", True):
        return "the start diagnostics are incomplete"
    if not summary.get("startTraceComplete", True):
        return "the start trace is incomplete"
    return "a run-level criterion failed"


def start_place(f, work_starts):
    samples = f.get("firstSampleNs") or []
    workers = ", ".join(f"worker {i} starts work at {signed_ms(work_starts[i] if i < len(work_starts) else None)} and takes its first sample at {ms(v)}"
                        for i, v in enumerate(samples))
    return (f"before the first wake: start run-queue delay {fine_ms(f.get('startRunDelayNs'))}, work start to failure {signed_ms(f.get('workStartToFailureNs'))}, "
            f"residual run-queue delay {fine_ms(f.get('residualRunDelayNs'))}. Times from the first event: {workers or 'unknown'}"
            + segment_text(f.get("startSegments")))


def segment_text(segments):
    if not segments:
        return ""
    parts = [f"native start to first point {fine_ms(segments['nativeToFirstNs'])}"]
    for name in ("setup", "barrier"):
        s = segments[name]
        parts.append(f"{name} {fine_ms(s['wallNs'])} wall, {fine_ms(s['onCpuNs'])} on a CPU, {fine_ms(s['runQueueNs'])} in the run queue, "
                     f"{signed_ms(s['offCpuNs'])} off the CPU, {s['minorFaults']} minor faults, {s['majorFaults']} major faults, "
                     f"{s['voluntarySwitches']} voluntary and {s['involuntarySwitches']} involuntary switches")
    return ". Start segments: " + "; ".join(parts)


def fine_ms(value):
    return "unknown" if value is None or value < 0 else f"{value/1e6:.4f} ms"


def signed_ms(value):
    return "unknown" if value is None else f"{value/1e6:+.1f} ms"


def ms(value):
    return "unknown" if value is None or value < 0 else f"{value/1e6:.1f} ms"


def invalid_arms(directory, name, block):
    delays, failures = {}, []
    for arm in ("zero", "speedup"):
        path = directory / f"{name}-{block}-profile-{arm}.json"
        if not path.exists():
            continue
        record = load(path)
        values = [t.get("runDelayNs") for t in record.get("threads", [])]
        delays[arm] = None if any(v is None or v < 0 for v in values) else max(values, default=0)
        if record.get("ledgerFailure"):
            failures.append(f"ledger failure {record['ledgerFailure']} in the {'0%' if arm == 'zero' else '50%'} arm")
    return delays, failures


def wait_section(summary):
    if "waitDiagnosticsComplete" not in summary:
        return "This run predates the run-queue diagnostics."
    rows = []
    for c in summary["configurations"]:
        for b in c["blocks"]:
            for arm, d in (b.get("waitDiagnostics") or {}).items():
                for f in (d or {}).get("debtFailures", []):
                    where = (f"after a wake: wait run-queue delay {ms(f.get('waitRunDelayNs'))}, post to wake {ms(f.get('postToWakeNs'))}, "
                             f"wake to failure {ms(f.get('wakeToFailureNs'))}") if f["afterWake"] else start_place(f, d.get("workStartNs") or [])
                    rows.append(f"| `{c['name']}` | {b['block']} | {arm} | {f['worker']} | {f['timeNs']/1e6:.3f} ms | {f['debtNs']/1e6:.3f} ms | {where} |")
    complete = "complete" if summary["waitDiagnosticsComplete"] else "incomplete"
    text = (f"The wait records are {complete}: each profile arm must hold one value for each wake, with none dropped. "
            f"The readable wait values sum to {summary.get('totalWaitRunDelayNs', 0)/1e6:.2f} ms across the run.")
    if "startTraceComplete" in summary:
        largest = summary["largestStartSegmentNs"]
        text += (f" The start trace is {'complete' if summary['startTraceComplete'] else 'incomplete'}. Across all profile arms, the largest native start to first point is "
                 f"{fine_ms(largest['nativeToFirstNs'])}, the largest setup segment is {fine_ms(largest['setup'])}{', with a median of ' + fine_ms(summary['medianSetupNs']) if summary.get('medianSetupNs') is not None else ''}, and the largest barrier segment is {fine_ms(largest['barrier'])}.")
    if "startDiagnosticsComplete" in summary:
        text += (f" The start records are {'complete' if summary['startDiagnosticsComplete'] else 'incomplete'}: each profile thread must hold "
                 "a start run-queue delay and a work start time in the order of its native start and finish.")
    if not rows:
        return text + "\n\nNo profile arm records a debt failure."
    return (text + "\n\n| Configuration | Block | Arm | Worker | Time | Debt | Place |\n| --- | ---: | --- | ---: | ---: | ---: | --- |\n"
            + "\n".join(rows))


def cpuset_section(root):
    directory = root / "artifacts/target-local-barrier-cpuset" / CPUSET_RUN
    if not directory.exists():
        return f"The diagnostic run directory `artifacts/target-local-barrier-cpuset/{CPUSET_RUN}` is not present, so this section is not generated."
    summary, manifest = load(directory / "summary.json"), load(directory / "manifest.json")
    peer = next(c for c in summary["configurations"] if c["name"] == "barrier-peer-bound")
    diffs = ", ".join(f"{b['difference']*100:+.1f}" for b in peer["blocks"] if b["valid"])
    failed = load(directory / "barrier-selected-dominant-3-profile-speedup.json")
    events = sorted(failed["events"], key=lambda e: e[1])
    failure = next(e for e in events if e[2] == 4)
    wake = next(e for e in reversed(events[:events.index(failure)]) if e[0] == failure[0] and e[2] == 6)
    post = next(e for e in events if e[2] == 7 and e[5] == wake[6])
    credit = sum(e[5] for e in events if e[0] != failure[0] and e[2] == 0 and post[9] < e[9] < wake[9])
    delay = failed["threads"][failure[0]]["runDelayNs"]
    return f"""Diagnostic run: `artifacts/target-local-barrier-cpuset/{CPUSET_RUN}`, with `--cpuset-cpus {manifest['cpuSet']}`. It does not replace any qualification run.
The host CPUs 0-1 and 2-3 are SMT pairs, so that run puts one thread on each physical core.

In qualification run `20261018T203120679-31605bcf`, the `peer-bound` differences are all positive. With one thread per physical core, they are {diffs} points, with no sign bias.
The outlying block in that run comes from a slow plain `optimized` arm, not from a profiled arm.
This is consistent with the hypothesis that SMT contention changes while a thread spins to pay delay. The cpuset also halves the CPU count, so the run does not isolate SMT.
The later unrestricted qualification run also shows no sign bias in `peer-bound`. The hypothesis stays open.

The same run captures one debt failure with a run-queue correlation. In `selected-dominant` block 3, the selected thread posts a token and releases the peer.
The peer's wake runs {(wake[9] - post[9])/1e6:.3f} ms later. In that interval, the selected thread publishes {credit/1e6:.1f} ms of credit, so the woken peer owes {failure[5]/1e6:.1f} ms.
The peer's run-queue delay across the whole run is {delay/1e6:.3f} ms, close to the length of that interval. The diagnostic has no interval times, so it cannot place the delay.
A runnable peer that waited for a CPU in that interval would explain the failure.
If so, the wake rule is correct here: the credit after the post belongs to time when the peer was runnable, and a thread that does not run cannot pay."""


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
    for c in summary["configurations"]:
        for b in c["blocks"]:
            if not b["valid"]:
                delays, b["ledgerFailures"] = invalid_arms(directory, c["name"], b["block"])
                for key, arm in (("zeroMaxRunDelayNs", "zero"), ("speedupMaxRunDelayNs", "speedup")):
                    if b.get(key) is None:
                        b[key] = delays.get(arm)
                    if b[key] is None:
                        b["runDelayMissing"] = True
    failing = [(c["name"], b) for c in summary["configurations"] for b in c["blocks"] if not b["valid"] or abs(b["difference"]) > 0.10]
    failed_configs = [c["name"] for c in summary["configurations"] if not c["passed"]]
    if summary["passed"]:
        verdict = "**Result: the current run passes.**"
        current = "Pass"
    else:
        parts = [f"`{name}` block {b['block']}" + ("" if b["valid"] else f" invalid ({', '.join((b.get('reasons') or []) + b.get('ledgerFailures', []))}; "
                 f"run-queue delay {ms(b.get('zeroMaxRunDelayNs'))} at 0% and {ms(b.get('speedupMaxRunDelayNs'))} at 50%)") +
                 (f" ({b['difference']*100:+.2f} pp; run-queue delay {(b.get('zeroMaxRunDelayNs') or 0)/1e6:.1f} ms at 0% and {(b.get('speedupMaxRunDelayNs') or 0)/1e6:.1f} ms at 50%)" if b["valid"] else "")
                 for name, b in failing]
        medians = "; ".join(f"`{c['name']}` median {c['medianAbsoluteDifference']*100:.2f} pp" for c in summary["configurations"]
                            if not c["passed"] and c["medianAbsoluteDifference"] is not None and c["medianAbsoluteDifference"] > 0.05)
        detail = "; ".join(x for x in [medians, "; ".join(parts)] if x) or run_level_detail(summary, barrier=True).capitalize()
        verdict = (f"**Result: the current run fails** in {', '.join(f'`{n}`' for n in failed_configs)}. Failed criteria: {detail}." if failed_configs
                   else f"**Result: the current run fails.** Failed criteria: {detail}.")
        current = f"Fail. {detail}."
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

## Run-queue delay

{wait_section(summary)}

## SMT and run-queue diagnostic

{cpuset_section(root)}

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


PROFILE_HISTORY = [
    ("20261019T024109669-c30228ca", "First causal profile run", "Pass"),
    ("20261019T032133781-6cc15ea3", "Throughput progress points", "Fail. `selected-75` and `peer-75` each have one block above 10 points, both in block 1, where the plain arms give 36.0% and 4.5%."),
    ("20261019T040508680-38464c4a", "Blocking queue and pipeline profile", "Fail. Block 3 is invalid: its `selected` experiment at 75% breaks the 2 ms debt bound."),
    ("20261019T043738634-485c3ea9", "Per-thread experiment diagnostics", "Fail. `peer-50` median 5.52 points and one block of 10.91 points. `peer-75` has one block of 12.72 points."),
    ("20261019T051105836-8f4f2ab0", "Profile warm-up experiment", "Pass"),
    ("20261019T060624408-2e3fb8c4", "Rotated experiment order and warm-up gate", "Pass"),
    ("20261019T063004675-e6636cdf", "TargetLock and lock profile", "Pass"),
    ("20261019T070634914-3cd1d60c", "Lock handoff counts", "Pass"),
    ("20261019T074837405-5db1c839", "Spin before a lock wait", "Fail. Block 1 is invalid: `selected` at 75% breaks the 2 ms debt bound, with 8.41 ms of run-queue delay on thread 1."),
    ("20261019T082828297-eb8d2196", "TargetSemaphore and semaphore profile", "Fail. Block 1 is invalid: `peer` at 75% records ledger failure 1, with no large run-queue delay."),
    ("20261019T090901082-03de2f25", "Failure and GC records", "Fail. Block 3 is invalid: `selected` at 25% records ledger failure 1, with no collection and no run-queue delay."),
    ("20261019T094341955-4375de13", "Failure context", "Pass"),
    ("20261019T110709359-b184c519", "Latency progress points", "Pass"),
    ("20261019T114214114-cd942bc0", "Per-transaction virtual latency", "Pass"),
    ("20261019T121727927-5e679245", "Transaction queue handoff", "Pass"),
    ("20261019T125516048-6470f3c8", "Spin before a queue wait", "Pass"),
    ("20261019T132316833-16878b7d", "Handoff parts", "Pass"),
]
THROUGHPUT_HISTORY = [
    ("20261019T032027341-60c2ab7c", "First throughput profile run", "Pass"),
    ("20261019T040411141-1b17380b", "Blocking queue and pipeline profile", "Pass"),
    ("20261019T043639774-d3af17e6", "Per-thread experiment diagnostics", "Pass"),
    ("20261019T051006922-f2f80c26", "Profile warm-up experiment", "Pass"),
    ("20261019T060526482-e851bced", "Rotated experiment order and warm-up gate", "Pass"),
    ("20261019T062905608-e6d24e45", "TargetLock and lock profile", "Fail. Block 4 invalid: a 2 ms debt failure, and a handler interval of 5.99 ms with 45.8 ms of run-queue delay."),
    ("20261019T070536063-c1a60568", "Lock handoff counts", "Pass"),
    ("20261019T074738456-b0d0236a", "Spin before a lock wait", "Pass"),
    ("20261019T082728956-3c510383", "TargetSemaphore and semaphore profile", "Pass"),
    ("20261019T090759533-41316f2b", "Failure and GC records", "Pass"),
    ("20261019T094241716-2243c889", "Failure context", "Pass"),
    ("20261019T110611005-2a35600a", "Latency progress points", "Pass"),
    ("20261019T113911746-669e1783", "Per-transaction virtual latency", "Fail. `peer-75` has one block of -10.54 points."),
    ("20261019T121628106-2be55ac2", "Transaction queue handoff", "Pass"),
    ("20261019T125416608-a58c20e0", "Spin before a queue wait", "Pass"),
    ("20261019T132215488-c111dbb5", "Handoff parts", "Pass"),
    ("20261019T150855392-75711219", "Worker pinning", "Fail. `peer-25` one block of 10.25 points."),
]
SPINTRANSACTION_HISTORY = [
    ("20261019T124806158-79f85b15", "First spin transaction profile run", "Fail. `peer-25` median 5.34 points and `peer-75` median 6.92 points. The three `selected` points pass."),
    ("20261019T131601688-284620fc", "Handoff parts", "Fail. `peer-75` median 7.29 points. The other five points pass."),
    ("20261019T150209127-dbb260bc", "Worker pinning", "Fail. `peer-25` median 9.56 points and `peer-50` median 5.06 points."),
    ("20261019T175441808-251c8262", "Matched plain warm-up", "Fail. `peer-25` median 9.22 points, `selected-50` one block of 25.47 points, and `peer-50` median 6.25 points. The other three points pass."),
]
TRANSACTION_HISTORY = [
    ("20261019T110105861-414dba20", "First transaction profile run", "Fail. All six points fail. The real gains follow the model, but the `peer` predictions stay near 5% at each speedup."),
    ("20261019T113356248-f1cbfddc", "Per-transaction virtual latency", "Fail. `selected-50` median 7.09 points and `peer-25` median 5.47 points. The other four points pass."),
    ("20261019T121118409-e589636a", "Transaction queue handoff", "Fail. `selected-25` median 7.58 points and `selected-50` median 10.07 points. The other four points pass."),
    ("20261019T124914850-b10c2f47", "Spin before a queue wait", "Fail. `selected-25` one block of 27.61 points, `selected-50` median 8.32 points, and `peer-50` one block of -12.59 points."),
    ("20261019T131709144-4aa0a3ef", "Handoff parts", "Fail. `selected-25` median 7.25 points and `selected-50` median 7.76 points."),
    ("20261019T150326902-04963faa", "Worker pinning", "Fail. Five points fail. Only `selected-75` passes."),
    ("20261019T175316141-ec3885bd", "Matched plain warm-up", "Fail. All six points fail. Block 3 is above the real gain at every point, by 12.01 to 16.26 points."),
]
SEMAPHORE_HISTORY = [
    ("20261019T082323819-903a6cef", "First semaphore profile run", "Fail. `selected-75` has one block of -10.61 points, and `peer-75` median 5.24 points."),
    ("20261019T090345985-b2474637", "Failure and GC records", "Pass"),
    ("20261019T093837289-67cf3f9e", "Failure context", "Fail. `selected-50` median 5.09 points and `selected-75` median 5.41 points."),
    ("20261019T110212880-744018ed", "Latency progress points", "Fail. Five points fail: four by one block each, and `peer-50` by two blocks."),
    ("20261019T113503667-76dc9109", "Per-transaction virtual latency", "Fail. `selected-50` median 5.43 points and `selected-75` median 6.21 points."),
    ("20261019T121226146-ac4f2bea", "Transaction queue handoff", "Fail. `selected-50` median 6.60 points and `selected-75` median 5.27 points."),
    ("20261019T125015330-52253802", "Spin before a queue wait", "Fail. `selected-50` median 5.51 points."),
    ("20261019T131809935-75def747", "Handoff parts", "Pass"),
    ("20261019T150434417-1c70a9de", "Worker pinning", "Pass"),
]
SPINLOCK_HISTORY = [
    ("20261019T074539772-03c40325", "First spin lock profile run", "Fail. Blocks 4 and 5 are invalid: `peer` at 75% has 97 and 98 visits. In the three valid blocks, `selected-50` has a median of 1.77 points, and `selected-75` fails at 6.87 points."),
    ("20261019T082529889-abecc54b", "TargetSemaphore and semaphore profile", "Fail. Block 3 is invalid: `peer` at 75% has 96 visits. `selected-75` median 6.70 points with one block of 10.52 points."),
    ("20261019T090601089-7d489720", "Failure and GC records", "Fail. Block 4 is invalid: `peer` at 75% has 98 visits."),
    ("20261019T094042685-1d761439", "Failure context", "Fail. Block 5 is invalid: `peer` at 75% has 99 visits. `selected-75` median 8.59 points."),
    ("20261019T110413189-a8e4bb89", "Latency progress points", "Fail. Block 5 is invalid: `peer` at 75% has 99 visits. `selected-75` median 7.87 points."),
    ("20261019T113703476-df6944a8", "Per-transaction virtual latency", "Fail. Block 4 is invalid: `peer` at 75% has 95 visits. `selected-75` median 6.93 points."),
    ("20261019T121428141-2d666768", "Transaction queue handoff", "Fail. `selected-75` median 6.37 points. All five blocks are valid."),
    ("20261019T125215820-36780b18", "Spin before a queue wait", "Fail. Block 2 is invalid: `peer` at 75% has 99 visits. `selected-75` median 5.60 points."),
    ("20261019T132014752-3e849f73", "Handoff parts", "Fail. `selected-75` one block of 12.34 points. All five blocks are valid."),
    ("20261019T150642975-999bd98a", "Worker pinning", "Fail. Block 2 is invalid: `peer` at 75% has 99 visits. `selected-75` median 8.09 points."),
]
LOCK_HISTORY = [
    ("20261019T062708436-df83442b", "First lock profile run", "Fail. `selected-50` median 9.13 points, all five blocks below the actual gain. `peer-25` has one block of 11.87 points."),
    ("20261019T070331580-523cc380", "Lock handoff counts", "Fail. Blocks 1 and 2 are invalid: `selected` at 50% breaks the 2 ms debt bound. In the three valid blocks, `selected-50` predicts 48.17% against 51.79%. The handoff share at `selected-50` is 7.40% in the profile and 2.18% in the real optimization."),
    ("20261019T074433206-6d45795e", "Spin before a lock wait", "Fail. `selected-50` median 7.89 points, all five blocks below the actual gain. `selected-75` median 5.07 points, with one block of 10.51 points."),
    ("20261019T082429851-cb3998b5", "TargetSemaphore and semaphore profile", "Fail. `selected-50` median 5.62 points, `selected-75` median 5.17 points with one block of 12.83 points, and `peer-75` median 5.55 points."),
    ("20261019T090501903-273c483d", "Failure and GC records", "Fail. Block 4 is invalid: `peer` at 75% records ledger failure 1. In the four valid blocks, `selected-50` median 8.32 points."),
    ("20261019T093943719-10b7cd71", "Failure context", "Fail. `selected-50` median 6.09 points with one block of -12.53 points."),
    ("20261019T110313420-3292dd51", "Latency progress points", "Pass"),
    ("20261019T113602781-8f0b3de1", "Per-transaction virtual latency", "Fail. Five points fail. `selected-50` median 7.49 points, `selected-75` median 5.67 points, and the three `peer` points by block 5."),
    ("20261019T121327119-d95b4f14", "Transaction queue handoff", "Fail. `selected-50` median 6.84 points, `selected-75` one block of 12.51 points, and `peer-25` one block of -15.85 points."),
    ("20261019T125115354-73f1edc7", "Spin before a queue wait", "Fail. `selected-50` median 6.28 points, `peer-50` median 5.18, `peer-75` median 5.74, and `peer-25` one block of -13.92 points."),
    ("20261019T131912794-bfae28ce", "Handoff parts", "Fail. Block 1 is invalid: a debt failure after one child payment of 3.35 ms for a request of 50 microseconds."),
    ("20261019T150538269-4c383533", "Worker pinning", "Fail. `selected-50` median 9.20 points and `selected-75` median 6.47 points."),
]
PIPELINE_HISTORY = [
    ("20261019T040306290-64463c6b", "First pipeline profile run", "Fail. `peer-50` median 6.76 points. Across the `peer` points, 13 of 15 block predictions are below their actual gains."),
    ("20261019T043536804-514995d6", "Per-thread experiment diagnostics", "Fail. `peer-75` has one block of 14.82 points. The `peer-50` prediction tracks the producer work ratio."),
    ("20261019T050900839-0a049f8a", "Profile warm-up experiment", "Fail. `peer-50` median 6.01 points. The order-0 producer work ratio stays 1.041 with the warm-up."),
    ("20261019T060421721-4a882c0a", "Rotated experiment order and warm-up gate", "Pass. `peer-50` median 4.92 points."),
    ("20261019T062807524-d72126fc", "TargetLock and lock profile", "Pass"),
    ("20261019T070437097-c99cf6c4", "Lock handoff counts", "Fail. `peer-50` has one block of 10.02 points. Its median difference is 2.70 points."),
    ("20261019T074639406-cf96acce", "Spin before a lock wait", "Pass"),
    ("20261019T082629301-c4292b43", "TargetSemaphore and semaphore profile", "Fail. `peer-25` has one block of 10.11 points."),
    ("20261019T090700489-b82b1791", "Failure and GC records", "Pass"),
    ("20261019T094141941-f8894fe3", "Failure context", "Fail. Block 5 is invalid from a debt failure after a wake with a skip of 0, and `peer-50` has one block of -11.12 points."),
    ("20261019T110512150-9b6ceb9a", "Latency progress points", "Pass"),
    ("20261019T113807687-40ce0fab", "Per-transaction virtual latency", "Pass"),
    ("20261019T121528493-e18bd4fd", "Transaction queue handoff", "Fail. `peer-75` median 6.62 points with one block of -10.53 points."),
    ("20261019T125316611-20aa9ebf", "Spin before a queue wait", "Fail. `peer-50` one block of 12.29 points and `peer-75` median 5.13 points."),
    ("20261019T132114927-617f3dac", "Handoff parts", "Pass"),
    ("20261019T150749398-6a7afb49", "Worker pinning", "Fail. `peer-25` one block of 12.47 points and `peer-75` one block of 10.19 points."),
]


def profile(root, run, change, kind="target-local-profile"):
    directory = root / "artifacts" / kind / run
    summary, manifest = load(directory / "summary.json"), load(directory / "manifest.json")
    if not summary["inputsUnchanged"]:
        raise SystemExit("The profile run changed its inputs.")
    workload = {"target-local-throughput": "throughput", "target-local-pipeline": "pipeline", "target-local-lock": "lock", "target-local-spinlock": "spinlock", "target-local-semaphore": "semaphore", "target-local-transaction": "transaction", "target-local-spintransaction": "spintransaction"}.get(kind, "latency")
    throughput = workload != "latency"
    if manifest.get("workload", "latency") != workload:
        raise SystemExit(f"The run in {kind} records the workload {manifest.get('workload')}.")
    known = {"throughput": THROUGHPUT_HISTORY, "pipeline": PIPELINE_HISTORY, "lock": LOCK_HISTORY, "spinlock": SPINLOCK_HISTORY, "semaphore": SEMAPHORE_HISTORY, "transaction": TRANSACTION_HISTORY, "spintransaction": SPINTRANSACTION_HISTORY}.get(workload, PROFILE_HISTORY)
    check_history(root, kind, known, run)
    kernel, cpus = host(directory)
    rows = "\n".join(f"| `{p['point']}` | {p['validBlocks']} / 5 | {pct(p['medianPredicted'])} | {pct(p['medianActual'])} | "
                     f"{pct(p['medianAbsoluteDifference'], suffix=' pp')} | {pct(p['maxAbsoluteDifference'], suffix=' pp')} | {'Pass' if p['passed'] else 'Fail'} |"
                     for p in summary["points"])
    blocks = "\n".join(f"| {b['block']} | {'Yes' if b['valid'] else 'No: ' + '; '.join(b['reasons'])} | "
                       + " | ".join(pct(b['points'][k]['difference'], suffix=' pp') if b['points'][k]['difference'] is not None else "n/a" for k in
                                    ("selected-25", "selected-50", "selected-75", "peer-25", "peer-50", "peer-75")) + " |"
                       for b in summary["blocks"])
    ratio = lambda p: ", ".join(f"{v:.3f}" for v in p["medianWorkRatios"]) if p.get("medianWorkRatios") else "n/a"
    handoff = "\n".join(f"| `{p['point']}` | {pct(p['medianHandoffShares'][0])} | {pct(p['medianHandoffShares'][1])} |" for p in summary["points"] if p.get("medianHandoffShares"))
    handoff_text = ("\n\n## Lock handoff\n\nThe handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. "
                    "The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.\n\n"
                    "| Point | Median handoff share, profile | Median handoff share, real optimization |\n| --- | ---: | ---: |\n" + handoff) if handoff else ""
    ms = lambda v: f"{v / 1e6:.3f} ms" if type(v) is int else "n/a"
    failed = [(b["block"], e, f) for b in summary["blocks"] for e in b.get("failedExperiments") or [] for f in e["failures"] or [None]]
    failure_rows = "\n".join(
        f"| {block} | `{e['candidate'].split('::')[-1].split('(')[0] if e['candidate'] else 'n/a'}` {e['speedup']}% | "
        + (f"{f['Worker']} | {f['Detail']} | {ms(f['DebtNs'])} | {ms(f['TimeNs'])} | {ms(f['GapNs'])} after `{f['PreviousKind'] or 'none'}` | " if f
           else f"No ledger failure: {' '.join((e['error'] or 'no error').split()).replace('|', '/')} | | | | | ")
        + f"{e['collections'] if type(e['collections']) is int and e['collections'] >= 0 else 'n/a'} | {ms(e['gcPauseNs']) if type(e['gcPauseNs']) is int and e['gcPauseNs'] >= 0 else 'n/a'} |" for block, e, f in failed)
    failure_text = ("\n\n## Failed experiments\n\nEach row is a ledger failure of an experiment that did not pass, or the error of a failed experiment without a ledger failure. The time counts from the first journal event with a time. "
                    "The amount is the debt for failure 1 and 0 for other failures. The gap is the time from the previous journal event of the same worker, in sequence order, and a buffered sample can make it negative. A failure without a time shows n/a. The collection count and the GC pause cover all threads of the process during the experiment.\n\n"
                    "| Block | Experiment | Worker | Failure | Amount | Time | Gap | Collections | GC pause |\n| ---: | --- | ---: | ---: | ---: | ---: | --- | ---: | ---: |\n"
                    + failure_rows) if failed else ""
    context_rows = []
    for block, e, f in failed:
        for w in (f or {}).get("Workers") or []:
            name = e["candidate"].split("::")[-1].split("(")[0] if e["candidate"] else "n/a"
            context_rows.append(
                f"| {block} | `{name}` {e['speedup']}% | {f['Worker']} | {f.get('ContextEvents')} | {w['Worker']} | {w['Events']} | {w['Children']} ({w.get('FailedChildren')} failed) | "
                f"{ms(w['MeasuredNs'])} for {ms(w['RequestedNs'])} | "
                + (f"{ms(w['MaxOvershootNs'])} at {ms(w['MaxOvershootOffsetNs'])} | " if w["MaxOvershootNs"] is not None else "none | ")
                + f"{w['Publishes']} | "
                + (f"{ms(w['LastPublishLocal'])} over {ms(w['LastPublishObserved'])} at {ms(w['LastPublishOffsetNs'])} |" if w["LastPublishLocal"] is not None else "none |"))
    context_text = ("\n\n## Failure context\n\nFor each failure, each row summarizes one worker over the journal events that come before the failure in sequence and no more than 10 ms before it in time. "
                    "The record also keeps the 64 latest of these events. Child payments show their total measured time for their total requested time over the whole window, which can cover several payments. The overshoot is the largest measured minus requested time of one completed child. A failed child wait is counted, but it is not ranked. "
                    "The ledger records a publish only when it raises the global delay, from the observed value to the local delay of its worker. "
                    "Offsets count from the failure. An event of another worker can have a positive offset, because it takes its time before its sequence.\n\n"
                    "| Block | Experiment | Failing worker | Window events | Worker | Events | Children | Child time | Largest overshoot | Publishes | Last publish |\n"
                    "| ---: | --- | ---: | ---: | ---: | ---: | ---: | --- | --- | ---: | --- |\n"
                    + "\n".join(context_rows)) if context_rows else ""
    us = lambda v: f"{v / 1e3:.1f} us" if isinstance(v, (int, float)) else "n/a"
    queue_rows = "\n".join(f"| `{p['point']}` | {us(p['medianQueueHandoffNs'][0])} | {us(p['medianQueueHandoffNs'][1])} |" for p in summary["points"] if p.get("medianQueueHandoffNs"))
    base = [b["baselineQueueHandoffNs"] for b in summary["blocks"] if b.get("valid") and b.get("baselineQueueHandoffNs") and None not in b["baselineQueueHandoffNs"]]
    base_row = f"| baseline | {us(statistics.median(x[0] for x in base))} | {us(statistics.median(x[1] for x in base))} |\n" if base else ""
    queue_text = ("\n\n## Queue handoff\n\nThe handoff of an item is the time from just before its enqueue to the return of the dequeue that takes it. "
                  "The table gives the median over valid blocks of the mean handoff, in the profile experiment and in the real arm. The baseline row compares the profile baseline with the plain original arm, and it is the only row that compares equal measures. "
                  "At a speedup above 0, a profiled handoff also includes the delay that the producer pays in `Enqueue`. At every point, the handoff also includes the time that an item waits in the queue.\n\n"
                  "| Point | Median mean handoff, profile | Median mean handoff, real arm |\n| --- | ---: | ---: |\n" + base_row + queue_rows) if queue_rows else ""
    parts = [b["baselineHandoffParts"] for b in summary["blocks"] if b.get("valid") and b.get("baselineHandoffParts") and None not in b["baselineHandoffParts"].values()]
    part = lambda arm, i: statistics.median(x[arm][i] for x in parts)
    parts_text = ("\n\nThe second value is the signed difference between the dequeue return and the enqueue return of the same item, as a mean. It can be negative, because the consumer can take an item before the producer reads its time after `Enqueue`. The table gives the median over valid blocks of the baseline values.\n\n"
                  "| Arm | Mean enqueue time | Mean dequeue return minus enqueue return | Share of spin takes |\n| --- | ---: | ---: | ---: |\n"
                  + "\n".join(f"| {name} | {us(part(arm, 0))} | {us(part(arm, 1))} | {part(arm, 2) * 100:.1f}% |" for name, arm in (("Profile baseline", "profile"), ("Plain original", "plain")))) if parts else ""
    queue_text = queue_text + parts_text
    ratios = "\n".join(f"| `{p['point']}` | {ratio(p)} |" for p in summary["points"])
    warmup_values = [b.get("warmupPassed") for b in summary["blocks"]]
    if not all("warmupPassed" in b for b in summary["blocks"]):
        warmups = "no block, because this run predates the warm-up"
    else:
        unknown = sum(1 for v in warmup_values if v is None)
        warmups = f"{sum(1 for v in warmup_values if v is True)} of {len(warmup_values)} blocks" + (f", and {unknown} blocks have no complete warm-up record" if unknown else "")
    order_text = "; ".join(f"order {o}: " + ", ".join(f"{v:.3f}" for v in r) for o, r in summary.get("medianWorkRatioByOrder", {}).items()) or "not recorded in this run"
    failed = [p["point"] for p in summary["points"] if not p["passed"]]
    current = "Pass" if summary["passed"] else f"Fail. Points {', '.join(failed) or 'none'} fail, or a run-level criterion fails."
    verdict = "**Result: the current run passes.**" if summary["passed"] else f"**Result: the current run fails.** {current[6:]} The run is kept, and no rerun replaces it."
    history = "\n".join(f"| `{r}` | {ch} | {res} |" for r, ch, res in known + [(run, change, current)])
    title = {"throughput": "Target-local throughput profile", "pipeline": "Target-local pipeline profile", "lock": "Target-local lock profile", "spinlock": "Target-local spin lock profile", "semaphore": "Target-local semaphore profile", "transaction": "Target-local transaction profile", "spintransaction": "Target-local spin transaction profile"}.get(workload, "Target-local causal profile")
    target, modes = {"throughput": ("TargetLocalThroughputVerify", "`throughput-profile` process and seven `plain-throughput`"),
                     "pipeline": ("TargetLocalPipelineVerify", "`pipeline-profile` process and seven `plain-pipeline`"),
                     "lock": ("TargetLocalLockVerify", "`lock-profile` process and seven `plain-lock`"),
                     "spinlock": ("TargetLocalSpinLockVerify", "`spin-lock-profile` process and seven `plain-spin-lock`"),
                     "semaphore": ("TargetLocalSemaphoreVerify", "`semaphore-profile` process and seven `plain-semaphore`"),
                     "transaction": ("TargetLocalTransactionVerify", "`transaction-profile` process and seven `plain-transaction`"),
                     "spintransaction": ("TargetLocalSpinTransactionVerify", "`spin-transaction-profile` process and seven `plain-spin-transaction`")}.get(
        workload, ("TargetLocalProfileVerify", "`causal-profile` process and seven `plain`"))
    description = {"throughput": "Each experiment runs for 120 ms. The selected worker repeats `Work.selected 40` and the peer repeats `Work.peer 20`, with one visit of the progress point after each call.",
                   "pipeline": "Each experiment runs for 120 ms. A producer repeats `Work.selected 40` and puts one item into a `TargetQueue` of 4 items. A consumer takes each item, runs `Work.peer 20`, and visits the progress point.",
                   "lock": "Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock`, then `Work.selected 40` inside it, and then visit the progress point.",
                   "spinlock": "Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock` that spins up to 1 ms before it blocks, then `Work.selected 40` inside it, and then visit the progress point.",
                   "semaphore": "Each experiment runs for 120 ms. Two workers take turns through two `TargetSemaphore` objects. Worker 0 runs `Work.selected 24` and releases worker 1, which runs `Work.peer 12`, visits the progress point, and releases worker 0.",
                   "transaction": "Each experiment runs for 120 ms with a latency progress point. Worker 0 begins a transaction, runs `Work.selected 40`, and enqueues it in a `TargetQueue` of 4 items. Worker 1 dequeues it, runs `Work.peer 8`, and ends it. The prediction is `1 - (VL / V) / (VL0 / V0)`, where VL sums the virtual latency of the transactions, each its wall latency minus the global delay added during it and at least 0, and V counts the departures. The real gain is `1 - W / W0`, where W is the mean wall latency.",
                   "spintransaction": "Each experiment runs for 120 ms with a latency progress point. Worker 0 begins a transaction, runs `Work.selected 40`, and enqueues it in a `TargetQueue` of 4 items. Worker 1 dequeues it, spinning up to 1 ms before it blocks, runs `Work.peer 8`, and ends it. The prediction is `1 - (VL / V) / (VL0 / V0)`, where VL sums the virtual latency of the transactions, each its wall latency minus the global delay added during it and at least 0, and V counts the departures. The real gain is `1 - W / W0`, where W is the mean wall latency."}.get(
        workload, "The selected thread runs 6000 rounds and the peer 3000. The progress point is the end of the workload.")
    plan = {"throughput": "[the throughput plan](../../../lode/plans/target-local-throughput.md)",
            "pipeline": "[the queue plan](../../../lode/plans/target-local-queue.md)",
            "lock": "[the lock plan](../../../lode/plans/target-local-lock.md)",
            "spinlock": "[the spin lock plan](../../../lode/plans/target-local-spin-lock.md)",
            "semaphore": "[the semaphore plan](../../../lode/plans/target-local-semaphore.md)",
            "transaction": "[the latency point plan](../../../lode/plans/target-local-transactions.md) and [the transaction delay plan](../../../lode/plans/target-local-transaction-delay.md)",
            "spintransaction": "[the spin queue plan](../../../lode/plans/target-local-spin-queue.md)"}.get(workload, "[the profile plan](../../../lode/plans/target-local-causal-profile.md)")
    expected = {"throughput": "If both workers have the same cost for each round, the expected gains are 10%, 25%, and 50% for `Work.selected` and 18.2%, 40%, and 66.7% for `Work.peer`. The criteria use the measured gains.",
                "pipeline": "The producer is the bottleneck, so the model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. Above 50%, the consumer sets the rate.",
                "lock": "The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.",
                "spinlock": "The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.",
                "semaphore": "No work runs in parallel, so the model gives `Work.selected` 16.7%, 33.3%, and 50%, and `Work.peer` 8.3%, 16.7%, and 25%. The model leaves out the two handoffs of each visit.",
                "transaction": "The producer sets the rate at every point, so the latency of a transaction is about the sum of both parts. The model gives `Work.selected` 20.8%, 41.7%, and 62.5%, and `Work.peer` 4.2%, 8.3%, and 12.5%. The model leaves out the queue handoff and any queue wait.",
                "spintransaction": "The producer sets the rate at every point, so the latency of a transaction is about the sum of both parts. The model gives `Work.selected` 20.8%, 41.7%, and 62.5%, and `Work.peer` 4.2%, 8.3%, and 12.5%. The model leaves out the queue handoff and any queue wait."}.get(
        workload, "The fork/join model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. The plateau at 75% comes from the peer, which then sets the end time.")
    limits = ("A latency experiment counts each transaction that ends, from the first work start to the last finish. The consumer drains the queue after the stop, so each transaction that begins also ends."
              if workload in ("transaction", "spintransaction") else
              "A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts."
              if throughput else "The progress point is the end of the workload.")
    return f"""# {title}

Evidence: `artifacts/{kind}/{run}`.

`{target}` ran {manifest["blocks"]} blocks in the Linux x64 container, with kernel `{kernel}`, x64, and {cpus} CPUs.
Each block runs one {modes} processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. {description}
The criteria are in {plan}. The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

{verdict}

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
{rows}

{expected}

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
{ratios}

The median work ratio by run order in the process is {order_text}. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in {warmups}.

{handoff_text}{queue_text}{failure_text}{context_text}

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
{blocks}

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
{history}

## Limits

{limits}
{"The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`." if workload in ("lock", "spinlock") else "The profile covers CPU-bound threads on Linux x64 that block only at a `TargetSemaphore` with one releasing thread and one waiting thread for each semaphore." if workload == "semaphore" else "The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer, with a latency progress point." if workload in ("transaction", "spintransaction") else "The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer." if workload == "pipeline" else "The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`."} CLR safety outside this workload is not established.
"""


def main():
    root = pathlib.Path(__file__).resolve().parent.parent
    out = root / "reliability/features/target-local"
    pages = {"prediction.md": prediction(root, sys.argv[1], sys.argv[3]), "transitions.md": transitions(root, sys.argv[2], sys.argv[3])}
    if len(sys.argv) > 5:
        pages["barrier.md"] = barrier(root, sys.argv[4], sys.argv[5])
    if len(sys.argv) > 7:
        pages["profile.md"] = profile(root, sys.argv[6], sys.argv[7])
    if len(sys.argv) > 9:
        pages["throughput.md"] = profile(root, sys.argv[8], sys.argv[9], "target-local-throughput")
    if len(sys.argv) > 11:
        pages["pipeline.md"] = profile(root, sys.argv[10], sys.argv[11], "target-local-pipeline")
    if len(sys.argv) > 13:
        pages["lock.md"] = profile(root, sys.argv[12], sys.argv[13], "target-local-lock")
    if len(sys.argv) > 15:
        pages["spinlock.md"] = profile(root, sys.argv[14], sys.argv[15], "target-local-spinlock")
    if len(sys.argv) > 17:
        pages["semaphore.md"] = profile(root, sys.argv[16], sys.argv[17], "target-local-semaphore")
    if len(sys.argv) > 19:
        pages["transaction.md"] = profile(root, sys.argv[18], sys.argv[19], "target-local-transaction")
    if len(sys.argv) > 21:
        pages["spintransaction.md"] = profile(root, sys.argv[20], sys.argv[21], "target-local-spintransaction")
    for name, text in pages.items():
        (out / name).write_text(text, newline="\n")


if __name__ == "__main__":
    main()
