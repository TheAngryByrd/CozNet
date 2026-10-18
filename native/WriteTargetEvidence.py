"""Write the target dispatch and credit evidence pages from one LinuxPerfRingVerify run directory."""
import itertools
import json
import pathlib
import statistics
import sys


def load(run, name):
    return json.loads((run / name).read_text())


def n(value):
    return f"{value:,}"


def runs(dispatches):
    return [(key, len(list(group))) for key, group in itertools.groupby((d[2], d[4]) for d in dispatches)]


def handler(dispatches):
    return [d[6] - d[5] for d in dispatches]


def dispatch_page(run, name):
    modes = {m: load(run, f"dispatch-{m}.json") for m in ("bounded", "backlog", "incomplete", "cancel")}
    for mode, data in modes.items():
        if not data["passed"]:
            raise SystemExit(f"dispatch-{mode} did not pass")
    b, k, i, c = (modes[m] for m in ("bounded", "backlog", "incomplete", "cancel"))
    if any(d[2] == "time_exhausted" for data in modes.values() for d in data["dispatches"]):
        raise SystemExit("A dispatch reached the elapsed budget. Update the page text before generation.")
    if runs(b["dispatches"]) != [(("drained_snapshot", 1), b["deliveries"])]:
        raise SystemExit("A bounded dispatch read other than one sample. Update the page text before generation.")
    any_max = max(d["maxHandlerNs"] for d in modes.values())
    order = runs(i["dispatches"])
    if [key for key, _ in order] != [("drained_snapshot", 1), ("records_exhausted", 1), ("drained_snapshot", 1),
                                     ("drained_snapshot", 0), ("source_failed", 0), ("terminal", 0)]:
        raise SystemExit(f"incomplete dispatch order differs: {order}")
    before, exhausted, last, empty, _, terminal = (count for _, count in order)
    cancel_before = sum(d[4] for d in c["dispatches"] if d[2] != "cancelled")
    rows = []
    for mode, data, result in (("bounded", b, "Three complete epochs"), ("backlog", k, "Complete"),
                               ("incomplete", i, "Terminal, `source_failed`"), ("cancel", c, "Complete, resume refused")):
        steps = ", ".join(str(e["closeSteps"]) for e in data["epochs"])
        lost = ", ".join(str(e["lost"]) for e in data["epochs"]) if mode != "incomplete" else f"{data['retirement']['rawLost']} (raw)"
        rows.append(f"| `{mode}` | {data['deliveries']} | {data['journalSamples']} | {data['exhaustedWithBacklog']} | {steps} | {lost} | {result} |")
    values = ", ".join(str(e["value"]) for e in b["epochs"])
    values = values.rsplit(", ", 1)
    values = f"{values[0]}, and {values[1]}"
    return f"""# Target-local sample dispatch

Evidence: `artifacts/perf-ring/{name}`.

`LinuxPerfRingVerify` builds the checks and runs each `TargetDispatchWorkload` mode once after the `PerfRingWorkload` modes.
The kernel is `{b['kernel']}` on x64.
The container drops all capabilities, has no network, and uses `seccomp=unconfined`.
The `perf-ring`, `perf-sample-ring`, `target-sample-dispatch`, and `target-delay-ledger` checks pass. All source and binary hashes remain unchanged.
Each perf sample overflow requests one `SIGRTMIN + 8` signal to the target thread through fasync.
The kernel merges overflows that occur before its deferred wakeup work runs.
Each mode file retains every dispatch, every journal sample, the epoch records, and the retirement record.
`native/WriteTargetEvidence.py` writes this page from the run directory.

| Mode | Notifications | Journal samples | Budget stops with backlog | Closure steps | Final lost | Epoch result |
| --- | ---: | ---: | ---: | --- | --- | --- |
{chr(10).join(rows)}

All modes report zero stale, reentered, foreign, `SIGIO` fallback, and dropped log entries.
In every mode, a second `create` on the target thread returns `EBUSY`.
Every retirement blocks the signal, disables the event, clears `O_ASYNC`, unmaps, closes, and restores the mask with no error.
The maximum handler interval in any mode is {n(any_max)} ns.

In `bounded`, every one of the {b['deliveries']} dispatches reads exactly one sample and ends with `drained_snapshot`.
The median handler interval is {n(int(statistics.median(handler(b['dispatches']))))} ns. The maximum is {n(b['maxHandlerNs'])} ns.
The ring wraps. Epoch values {values} ns increase, and each covers its last sample reading.

In `backlog`, two masked steps queue notifications while samples accumulate.
After the first mask ends, {k['exhaustedWithBacklog']} one-record dispatches stop with backlog `remaining`. Later queued notifications drain it.
The second mask holds into closure. The session drains {k['retirement']['pendingDrained']} queued signals without dispatch. Closure reads the backlog in {k['epochs'][0]['closeSteps']} bounded steps.

In `incomplete`, a 300 ms masked step overflows the one-page ring. Before the mask, {before} dispatches read one sample each.
After the mask ends, {exhausted + last} one-record dispatches empty the ring. {exhausted} of them stop with backlog `remaining`.
The next {empty} queued notifications find an empty ring. The kernel requests a notification for each overflow, including each lost sample.
The next successful output writes a `PERF_RECORD_LOST` record first. The decoder rejects it (error 5).
The dispatcher then stays terminal. Its {terminal} later notifications return `terminal` and read nothing.
All {i['journalSamples']} accepted samples keep generation 1. Retirement reads {i['retirement']['rawLost']} lost samples from the descriptor. Resume returns `EPROTO`.

In `cancel`, another thread cancels after 50 ms of wall time. {cancel_before} dispatches accept samples before cancellation, and {c['cancelledDispatches']} dispatches after it read nothing.
Closure ignores cancellation and drains the remaining samples in {c['epochs'][0]['closeSteps']} steps. The epoch completes with zero loss, and resume returns `EPROTO`.

No live mode reached the 250,000 ns elapsed budget. Elapsed-budget stops have synthetic checks only.

## Rejected trigger

Evidence: `artifacts/perf-ring/20261018T114054564-e9338966`.

The first workload used a `CLOCK_THREAD_CPUTIME_ID` timer with a 2 ms interval.
Its wall-time gap between handler starts has a median near 10 ms in every mode. Most deliveries report overrun 4.
`bounded` received 30 notifications for 300 ms of thread CPU. 28 of them stopped at the 8-record budget with backlog.
At 50% speedup, ten samples in one notification can publish 5 ms of credit. That conflicts with the 2 ms debt bound.
That run passed its declared rules. The cadence evidence caused the change to fasync notification.

## Limits

These runs do not test CLR execution or prediction accuracy.
The workload thread is both producer CPU and consumer. Cross-CPU publication is not observed.
One notification for each sample costs about one signal for each 1 ms of thread CPU. Its total overhead in an application is not measured.
Each run uses one session. Concurrent retirement of two sessions and the handler quiescence timeout are not tested.
The code keeps session memory after a timeout, but no check forces that path.
Fasync identity uses the descriptor number. A reused number after retirement can cause one extra bounded drain of another session's own ring.
Other kernels, signal queue limits, and seccomp policies are not verified.

See [the dispatch reference](../../../docsSrc/Reference/target-sample-dispatch.md), [the procedure](../../../docsSrc/How_Tos/verify-perf-ring.md#check-target-local-dispatch), and [the plan](../../../lode/plans/target-local-dispatch.md).
"""


def credit_page(run, name):
    zero, half = load(run, "credit-zero.json"), load(run, "credit-half.json")
    for mode, data in (("zero", zero), ("half", half)):
        if not data["passed"] or not data["replay"]["valid"]:
            raise SystemExit(f"credit-{mode} did not pass")
    rows, walls = [], []
    for mode, data in (("zero", zero), ("half", half)):
        r = data["replay"]
        rows.append(f"| `{mode}` | {r['selectedSamples']} | {n(r['credited'])} | {n(data['global'])} | {n(r['children'])} | {n(r['measured'])} | {n(r['maxDebt'])} | Valid |")
        s, p = data["workers"]
        walls.append(f"| `{mode}` | {n(s['wallNs'])} | {n(p['wallNs'])} | {n(p['local'])} | {n(s['maxHandlerNs'])} | {n(p['maxHandlerNs'])} |")
    events = half["events"]
    selected_publishes = sum(1 for e in events if e[2] == 1 and e[0] == 0)
    peer_publishes = sum(1 for e in events if e[2] == 1 and e[0] == 1)
    selected_children = sum(1 for e in events if e[2] == 2 and e[0] == 0)
    requested = sum(e[6] for e in events if e[2] == 2)
    growth = (half["workers"][1]["wallNs"] - zero["workers"][1]["wallNs"]) / 1e6
    pays = "never pays" if selected_children == 0 else f"pays in {selected_children} children"
    return f"""# Native target-local credit and payment

Evidence: `artifacts/perf-ring/{name}` (`credit-zero.json` and `credit-half.json`).

`TargetCreditWorkload` runs two native threads on kernel `{half['kernel']}` on x64.
Each thread owns a `LinuxTargetSession` with a credit sink and one shared `ExperimentDelay`.
The selected thread runs code in the `coz_selected` section. The peer runs code in the `coz_peer` section.
Both sections are payment-eligible. Only `coz_selected` is selected.
Each thread runs a fixed number of work rounds, calibrated to about 300 ms of thread CPU before the sessions start.
No managed controller or central thread credits or pays. Each thread credits and pays in its own handler.
`native/WriteTargetEvidence.py` writes this page from the run directory.

| Mode | Selected samples | Credited (ns) | Global delay (ns) | Children | Measured payment (ns) | Max observed debt (ns) | Replay |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
{chr(10).join(rows)}

| Mode | Selected wall (ns) | Peer wall (ns) | Peer local (ns) | Max selected handler (ns) | Max peer handler (ns) |
| --- | ---: | ---: | ---: | ---: | ---: |
{chr(10).join(walls)}

In `half`, each selected sample adds 500,000 ns. The selected thread publishes {selected_publishes} times and {pays}. The peer publishes its measured overshoot {peer_publishes} times.
The peer pays {n(half['workers'][1]['local'])} ns in {n(half['replay']['children'] - selected_children)} children. No child requests more than 50,000 ns.
The children request {n(requested)} ns. Measured overshoot makes up the rest of the measured payment.
The peer wall time grows by about {growth:.0f} ms compared with `zero`. The experiment records no failure. Deferred-debt events in the journal: {half['replay']['deferred']}. The closing settlement runs at an ineligible address and can defer.

The replay orders all {n(half['sequences'])} events by sequence. Every sequence from 1 to {n(half['sequences'])} occurs once.
For each event, the replay requires the same prior local delay and an observed global delay that some publish produced.
It recomputes credit from the selected spans and requires the global delay to equal the largest publish.
Sorted by value, every publish must name the previous publish as its observed global delay, so no update is lost.
Each child must follow positive observed debt and must not request more than that debt.

## Limits

This is native code only. It does not test CLR execution, managed reconciliation, or prediction accuracy.
The peer pays in a spin, so payment consumes its CPU. Each settlement requests at most 2 ms. Measured overshoot can add more. The handler bound stays below 5 ms.
The two threads do not share work. The wall-time change is not a prediction of an optimization.
One run per mode. Other kernels and CPU counts are not verified.
The task-clock event keeps sampling during spin payment. The peer accepts {half['workers'][1]['accepted']} samples in `half` and {zero['workers'][1]['accepted']} in `zero`. Payment samples are unselected and earn no credit, but they fill the journal.
Measured overshoot publishes as global delay. A preempted child on a loaded host can make other threads owe more than 2 ms and fail the experiment.
The final settlement at closure can only publish. Up to 2 ms of peer debt can stay unpaid at the end. The pass rule allows that residue.
Each replayed thread starts at local delay 0. The workload creates both accounts before either thread starts, so the global delay is 0 then.
The journal has no settlement identity, so the replay cannot check the 2 ms request cap for each settlement. The synthetic checks cover that cap.

See [the ledger reference](../../../docsSrc/Reference/target-delay-ledger.md) and [the plan](../../../lode/plans/target-local-credit.md).
"""


def main():
    run = pathlib.Path(sys.argv[1]).resolve()
    out = pathlib.Path(sys.argv[2]).resolve()
    summary = load(run, "summary.json")
    if not summary["passed"] or not summary["inputsUnchanged"]:
        raise SystemExit("The run did not pass or its inputs changed.")
    (out / "target-dispatch.md").write_text(dispatch_page(run, run.name), newline="\n")
    (out / "target-credit.md").write_text(credit_page(run, run.name), newline="\n")


if __name__ == "__main__":
    main()
