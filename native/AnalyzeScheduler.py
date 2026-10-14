import json
import pathlib
import sys


def parse_capture(capture):
    if capture is None or capture["Failures"] or capture["FinalLost"] != 0:
        raise ValueError("Scheduler capture has errors or incomplete loss accounting")
    boundaries = [capture[key] for key in ("EnableBefore", "EnableAfter", "DisableBefore", "DisableAfter")]
    if boundaries[0] <= 0 or boundaries != sorted(boundaries):
        raise ValueError("Invalid scheduler collection boundaries")
    previous = boundaries[0]
    state = None
    pending = None
    intervals = []
    left = 0
    for record in capture["Records"]:
        time, kind, flags = record["TimestampNanoseconds"], record["Kind"], record["Flags"]
        if not previous <= time <= boundaries[-1]:
            raise ValueError("Scheduler timestamps violate collection boundaries")
        if kind not in (0, 1) or record["Lost"] != 0:
            raise ValueError("Unexpected scheduler loss or record kind")
        if flags & ~(7 | (1 << 13) | (1 << 14)) or bool(flags & (1 << 13)) != (kind == 1):
            raise ValueError("Scheduler switch flags disagree with kind")
        if kind == 0 and flags & (1 << 14):
            raise ValueError("Preemption flag on switch-in")
        if state == kind:
            raise ValueError("Repeated scheduler transition without loss")
        if kind == 1:
            pending = (time, bool(flags & (1 << 14)))
        elif pending is None:
            left += 1
        else:
            intervals.append({"start": pending[0], "end": time, "preempt": pending[1]})
            pending = None
        previous, state = time, kind
    return {"threadId": capture["ThreadId"], "records": len(capture["Records"]),
            "leftCensored": left, "rightCensored": pending is not None, "intervals": intervals}


def overlap(intervals, start, end):
    if not 0 < start <= end:
        raise ValueError("Invalid request phase boundary")
    matches = [{**item, "overlapNanoseconds": min(end, item["end"]) - max(start, item["start"])}
               for item in intervals if item["end"] > start and item["start"] < end]
    return {"wallNanoseconds": end - start,
            "observedSwitchedOutNanoseconds": sum(item["overlapNanoseconds"] for item in matches),
            "intervals": matches}


def analyze(path):
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    if not data.get("schedulerTiming") or len(data["schedulerCaptures"]) != 3:
        raise ValueError("Expected controller and two target captures")
    captures = data["schedulerCaptures"]
    if len({c["ThreadId"] for c in captures if c}) != 3 or len({c["ProcessId"] for c in captures if c}) != 1:
        raise ValueError("Invalid registered scheduler identities")
    parsed = [parse_capture(capture) for capture in captures]
    requests = []
    for request in data["requests"]:
        timing = request["Result"]["Timing"]
        slot = request["Slot"]
        if slot not in (0, 1):
            raise ValueError("Invalid request slot")
        start = timing["RequestStartedNanoseconds"]
        handler_start = timing["HandlerStartedNanoseconds"]
        handler_end = timing["HandlerFinishedNanoseconds"]
        observed = timing["ObservedNanoseconds"]
        if not 0 < start <= handler_start <= handler_end <= observed:
            raise ValueError("Unordered native request phases")
        for index in (0, slot + 1):
            if start < captures[index]["EnableAfter"] or observed > captures[index]["DisableBefore"]:
                raise ValueError("Request lies outside assured collection boundaries")
        requests.append({"slot": slot, "sequence": request["Sequence"], "stage": request["StartedStage"],
                         "waitNanoseconds": request["Result"]["WaitNanoseconds"],
                         "targetBeforeHandler": overlap(parsed[slot + 1]["intervals"], start, handler_start),
                         "controllerBeforeHandler": overlap(parsed[0]["intervals"], start, handler_start),
                         "controllerAfterHandler": overlap(parsed[0]["intervals"], handler_end, observed)})
    peaks = []
    for peak in data["peakDebt"]:
        if peak is None:
            continue
        for pending in peak["Boundary"]["PendingRequests"]:
            found = [r for r in requests if r["slot"] == pending["Slot"] and r["sequence"] == pending["Sequence"]]
            peaks.append({"debtSlot": peak["Slot"], "debtNanoseconds": peak["Boundary"]["DebtNanoseconds"][peak["Slot"]],
                          "pendingSlot": pending["Slot"], "pendingSequence": pending["Sequence"], "completedRequest": found})
    return {"workloadPassed": data["passed"], "capacityViolations": data["capacityViolations"],
            "schedulerIntegrityPassed": True, "captures": parsed,
            "longestBeforeHandler": sorted(requests, key=lambda r: r["targetBeforeHandler"]["wallNanoseconds"], reverse=True)[:5],
            "peakDebtPendingRequests": peaks}


if __name__ == "__main__":
    root = pathlib.Path(sys.argv[1])
    results = {path.parent.name: analyze(path) for path in sorted(root.glob("*/workload.json"))}
    if not results:
        raise ValueError("No scheduler workloads found")
    (root / "scheduler-analysis.json").write_text(json.dumps(results, indent=2) + "\n")
