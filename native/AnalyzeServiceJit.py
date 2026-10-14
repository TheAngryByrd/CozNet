import argparse
from decimal import Decimal
import json
from pathlib import Path

from AnalyzeRuntimeSuspension import ClockAlignment, overlap_bounds


def analyze(workload, decoded):
    alignment = ClockAlignment.parse(workload, decoded)
    if not decoded.get("includeJit"):
        raise ValueError("The decoder did not request JIT events.")
    controller_threads = {e["threadId"] for e in decoded["events"] if e["provider"] == "CozNet-SampledSignals"}
    if len(controller_threads) != 1:
        raise ValueError("Clock anchors must identify one controller thread.")
    pending, intervals = {}, []
    for event in sorted(decoded["events"], key=lambda e: Decimal(e["milliseconds"])):
        if (event["provider"] != "Microsoft-Windows-DotNETRuntime" or event["processId"] != alignment.process
                or event["threadId"] not in controller_threads or event["id"] not in (143, 145)):
            continue
        payload = event["payload"]
        key = (payload["ClrInstanceID"], payload["ModuleID"], payload["MethodID"])
        if event["id"] == 145:
            if key in pending:
                raise ValueError("A controller compilation start is duplicated.")
            pending[key] = event
        elif key in pending:
            start = pending.pop(key)
            intervals.append((Decimal(start["milliseconds"]), Decimal(event["milliseconds"]),
                              payload["MethodNamespace"] + "." + payload["MethodName"]))
    if pending:
        raise ValueError("A controller compilation has no matching method-load event.")
    intervals.sort()
    merged = []
    for start, end, _ in intervals:
        if merged and start <= merged[-1][1]:
            merged[-1] = (merged[-1][0], max(merged[-1][1], end))
        else:
            merged.append((start, end))
    phases = []
    for phase in workload["controllerPhases"]:
        before, after = phase["Before"], phase["After"]
        lower, upper, methods = Decimal(0), Decimal(0), []
        for start, end, method in intervals:
            bounds = overlap_bounds(before["BeforeTimestamp"], after["AfterTimestamp"], start, end, alignment)
            if bounds["possibleNanoseconds"] > 0:
                methods.append({"method": method, "possibleMilliseconds": float(bounds["possibleNanoseconds"] / 1000000)})
        for start, end in merged:
            bounds = overlap_bounds(before["BeforeTimestamp"], after["AfterTimestamp"], start, end, alignment)
            lower += bounds["guaranteedNanoseconds"]
            upper += bounds["possibleNanoseconds"]
        phases.append({"stage": phase["Stage"], "phase": phase["Phase"], "iteration": phase["Iteration"],
                       "completed": phase["Completed"],
                       "wallMilliseconds": (after["AfterTimestamp"] - before["BeforeTimestamp"]) * 1000 / alignment.frequency,
                       "cpuMilliseconds": (after["CpuNanoseconds"] - before["CpuNanoseconds"]) / 1000000,
                       "jitElapsedLowerMilliseconds": float(lower / 1000000),
                       "jitElapsedUpperMilliseconds": float(upper / 1000000),
                       "methods": sorted(methods, key=lambda item: item["possibleMilliseconds"], reverse=True)})
    return {"eventsLost": decoded["eventsLost"], "controllerCompilations": len(intervals),
            "alignmentWidthNanoseconds": float((alignment.upper - alignment.lower) * 1000000000 / alignment.frequency),
            "jitIntervalsMeasureElapsedTime": True, "phases": phases}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("workload", type=Path)
    parser.add_argument("events", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    workload = json.loads(args.workload.read_text(encoding="utf-8-sig"), parse_float=Decimal)
    decoded = json.loads(args.events.read_text(encoding="utf-8-sig"), parse_float=Decimal)
    args.output.write_text(json.dumps(analyze(workload, decoded), indent=2), encoding="utf-8")
