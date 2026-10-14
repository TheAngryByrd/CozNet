import argparse
from dataclasses import dataclass
from decimal import Decimal
import json
from pathlib import Path


MARKERS = "CozNet-SampledSignals"
RUNTIME = "Microsoft-Windows-DotNETRuntime"


@dataclass(frozen=True)
class ClockAlignment:
    lower: Decimal
    upper: Decimal
    frequency: int
    process: int
    opening: Decimal
    closing: Decimal

    @staticmethod
    def parse(workload, decoded):
        if decoded["eventsLost"] != 0:
            raise ValueError("The trace reports lost events.")
        frequency = workload["stopwatchFrequency"]
        if type(frequency) is not int or frequency <= 0:
            raise ValueError("The Stopwatch frequency must be a positive integer.")
        anchors = workload["traceAnchors"]
        phases = ["run-start"]
        if workload["warmupMilliseconds"] > 0:
            phases += ["warmup-start", "warmup-end"]
        phases += ["measurement-start", "measurement-end", "run-end"]
        actual = [a["Phase"] for a in anchors]
        abort = workload.get("traceAbort")
        if abort is not None:
            prefix = actual[:-2]
            if (actual[-2:] != ["run-abort", "run-end"] or not prefix or prefix != phases[:len(prefix)]
                    or len(prefix) >= len(phases)):
                raise ValueError("The aborted trace has an incomplete or invalid marker prefix.")
            boundary = workload.get("failureBoundary")
            if (not boundary or workload.get("protocolPassed") is not False or workload.get("passed") is not False
                    or not isinstance(workload.get("failures"), list) or not workload["failures"]
                    or type(abort["AnchorId"]) is not int or abort["AnchorId"] != len(prefix)
                    or type(boundary["Timestamp"]) is not int
                    or abort["FailureTimestamp"] != boundary["Timestamp"]
                    or abort["Stage"] != boundary["Stage"]):
                raise ValueError("The abort marker does not identify a recorded workload failure.")
            timestamp = abort["FailureTimestamp"]
            allowed_stages = {"run-start": ("setup",), "warmup-start": ("warmup",),
                              "warmup-end": ("warmup", "warmup-settlement"), "measurement-start": ("measurement",),
                              "measurement-end": ("measurement", "settlement")}
            if abort["Stage"] not in allowed_stages[prefix[-1]]:
                raise ValueError("The abort stage does not match the last completed marker transition.")
            if (type(timestamp) is not int or timestamp < anchors[-3]["AfterTimestamp"]
                    or timestamp > anchors[-2]["BeforeTimestamp"]):
                raise ValueError("The failure boundary is outside the abort marker interval.")
        elif actual != phases:
            raise ValueError("The anchor phases are incomplete or out of order.")
        markers = [e for e in decoded["events"] if e["provider"] == MARKERS]
        if len(markers) != len(anchors):
            raise ValueError("The marker and anchor counts differ.")
        offsets, times, processes = [], [], set()
        previous = -1
        for index, anchor in enumerate(anchors):
            matches = [e for e in markers if e["payload"].get("id") == index]
            if len(matches) != 1 or anchor["Id"] != index:
                raise ValueError("Each anchor must have one matching marker.")
            event = matches[0]
            if (event["id"], event["version"], event["payload"].get("phase")) != (1, 0, anchor["Phase"]):
                raise ValueError("The marker identity does not match its anchor.")
            before, after = anchor["BeforeTimestamp"], anchor["AfterTimestamp"]
            if before < previous or after < before:
                raise ValueError("The anchor brackets are reversed or overlap.")
            previous = after
            time = Decimal(event["milliseconds"])
            if not time.is_finite() or (times and time < times[-1]):
                raise ValueError("The marker timestamps are invalid or out of order.")
            ticks = time * frequency / 1000
            offsets.append((Decimal(before) - ticks, Decimal(after) - ticks))
            times.append(time)
            processes.add(event["processId"])
        lower, upper = max(x[0] for x in offsets), min(x[1] for x in offsets)
        if len(processes) != 1 or lower > upper:
            raise ValueError("The trace has inconsistent process or clock alignment.")
        return ClockAlignment(lower, upper, frequency, processes.pop(), times[0], times[-1])

    def ticks(self, milliseconds):
        return Decimal(milliseconds) * self.frequency / 1000


def pair_episodes(events, alignment):
    episodes, anomalies, pending = [], [], {}
    relevant = [e for e in events if e["provider"] == RUNTIME and e["id"] in (9, 8, 7, 3)
                and e["processId"] == alignment.process
                and alignment.opening <= Decimal(e["milliseconds"]) <= alignment.closing]
    for event in sorted(relevant, key=lambda e: Decimal(e["milliseconds"])):
        instance = event["payload"].get("ClrInstanceID")
        if event["version"] != 1 or type(instance) is not int:
            anomalies.append({"kind": "unsupported-boundary", "events": [event]})
            continue
        group = pending.setdefault(instance, [])
        if event["id"] == 9 and group:
            anomalies.append({"kind": "interrupted-episode", "events": list(group)})
            group.clear()
        group.append(event)
        expected = [9, 8, 7, 3]
        if [e["id"] for e in group] != expected[:len(group)]:
            anomalies.append({"kind": "ambiguous-sequence", "events": list(group)})
            group.clear()
        elif len(group) == 4:
            episodes.append({"sequence": len(episodes), "clrInstanceId": instance,
                             "reason": group[0]["payload"]["Reason"],
                             "gcCount": group[0]["payload"]["Count"], "events": list(group)})
            group.clear()
    for group in pending.values():
        if group:
            anomalies.append({"kind": "incomplete-episode", "events": group})
    return episodes, anomalies


def overlap_bounds(start, end, trace_start, trace_end, alignment):
    if end < start:
        raise ValueError("A managed interval has reversed timestamps.")
    first, last = alignment.ticks(trace_start), alignment.ticks(trace_end)
    if last < first:
        raise ValueError("A trace interval has reversed timestamps.")
    guaranteed = max(Decimal(0), min(end, last + alignment.lower) - max(start, first + alignment.upper))
    possible = max(Decimal(0), min(end, last + alignment.upper) - max(start, first + alignment.lower))
    return {"guaranteedNanoseconds": guaranteed * 1_000_000_000 / alignment.frequency,
            "possibleNanoseconds": possible * 1_000_000_000 / alignment.frequency}


def correlate(start, end, episodes, alignment):
    overlaps = []
    for episode in episodes:
        times = [e["milliseconds"] for e in episode["events"]]
        envelope = overlap_bounds(start, end, times[0], times[3], alignment)
        if envelope["possibleNanoseconds"] > 0:
            overlaps.append({"episode": episode["sequence"], "envelope": envelope,
                             "acquisition": overlap_bounds(start, end, times[0], times[1], alignment),
                             "suspendedCore": overlap_bounds(start, end, times[1], times[2], alignment),
                             "restart": overlap_bounds(start, end, times[2], times[3], alignment)})
    return overlaps


def analyze(workload, decoded):
    alignment = ClockAlignment.parse(workload, decoded)
    episodes, anomalies = pair_episodes(decoded["events"], alignment)
    gc_episodes = [e for e in episodes if e["reason"] in (1, 6)]
    matched_collections = set()
    for observation in workload.get("gcObservations") or []:
        matches = correlate(observation["StartedTimestamp"], observation["FinishedTimestamp"], gc_episodes, alignment)
        available = [match["episode"] for match in matches if match["episode"] not in matched_collections]
        if available:
            matched_collections.add(available[0])
        else:
            anomalies.append({"kind": "forced-collection-without-suspension", "observation": observation})
    initial = workload.get("runtimeCountersStart")
    final = (workload.get("measurement") or workload.get("failureBoundary") or {}).get("RuntimeCounters")
    if initial and final and final["Gen0Collections"] > initial["Gen0Collections"]:
        if not correlate(initial["StartedTimestamp"], final["FinishedTimestamp"], gc_episodes, alignment):
            anomalies.append({"kind": "collection-counter-without-suspension", "initial": initial, "final": final})
    requests = []
    for request in workload["requests"]:
        start, end = request["StartedTimestamp"], request["CompletedTimestamp"]
        overlaps = correlate(start, end, episodes, alignment)
        if overlaps:
            requests.append({"slot": request["Slot"], "sequence": request["Sequence"],
                             "stage": request["Stage"], "startedStage": request["StartedStage"],
                             "startedTimestamp": start, "completedTimestamp": end,
                             "waitNanoseconds": request["Result"]["WaitNanoseconds"], "overlaps": overlaps})
    peaks = []
    for peak in workload["peakDebt"]:
        if peak is not None:
            interval = peak["Interval"]
            peaks.append({"slot": peak["Slot"], "boundary": peak["Boundary"],
                          "overlaps": correlate(interval["StartedTimestamp"], interval["FinishedTimestamp"], episodes, alignment)})
    return {"valid": not anomalies, "workloadPassed": workload.get("passed"),
            "workloadProtocolPassed": workload.get("protocolPassed"),
            "workloadAborted": workload.get("traceAbort") is not None, "eventsLost": decoded["eventsLost"],
            "processId": alignment.process, "offsetLowerTicks": alignment.lower, "offsetUpperTicks": alignment.upper,
            "alignmentWidthNanoseconds": (alignment.upper - alignment.lower) * 1_000_000_000 / alignment.frequency,
            "episodes": episodes, "anomalies": anomalies, "requestCount": len(workload["requests"]),
            "overlappingRequests": requests, "measurementPeaks": peaks,
            "overlapEstablishesCausation": False}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("workload", type=Path)
    parser.add_argument("events", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    try:
        workload = json.loads(args.workload.read_text(encoding="utf-8-sig"), parse_float=Decimal)
        decoded = json.loads(args.events.read_text(encoding="utf-8-sig"), parse_float=Decimal)
        result = analyze(workload, decoded)
    except (ValueError, KeyError, TypeError) as error:
        result = {"valid": False, "error": str(error)}
    args.output.write_text(json.dumps(result, indent=2, default=str), encoding="utf-8")
    return 0 if result["valid"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
