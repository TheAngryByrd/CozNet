import json
import pathlib
import sys
from decimal import Decimal
from AnalyzeRuntimeSuspension import analyze as analyze_runtime
from AnalyzeScheduler import analyze as analyze_scheduler


def analyze(directory):
    path = directory / "workload.json"
    workload = json.loads(path.read_text(encoding="utf-8-sig"), parse_float=Decimal)
    events = json.loads((directory / "events.json").read_text(encoding="utf-8-sig"), parse_float=Decimal)
    runtime = analyze_runtime(workload, events)
    scheduler = analyze_scheduler(path)
    if not runtime["valid"]:
        raise ValueError("Runtime correlation is incomplete")
    if any(capture["ProcessId"] != runtime["processId"] for capture in workload["schedulerCaptures"]):
        raise ValueError("Runtime and scheduler process identities differ")
    identities = [(request["Slot"], request["Sequence"]) for request in workload["requests"]]
    if len(set(identities)) != len(identities):
        raise ValueError("Request identities repeat")
    overlaps = {(request["slot"], request["sequence"]): request["overlaps"] for request in runtime["overlappingRequests"]}
    longest = []
    for request in scheduler["longestBeforeHandler"]:
        longest.append({**request, "enclosingManagedRequestSuspensions": overlaps.get((request["slot"], request["sequence"]), [])})
    return {
        "valid": True, "workloadPassed": workload["passed"], "capacityViolations": workload["capacityViolations"],
        "eventsLost": runtime["eventsLost"], "alignmentWidthNanoseconds": runtime["alignmentWidthNanoseconds"],
        "runtimeEpisodes": len(runtime["episodes"]), "longestBeforeHandler": longest,
        "measurementPeaks": runtime["measurementPeaks"],
        "runtimeOverlapScope": "enclosing managed request, not native phase or switched-out interval",
        "overlapEstablishesCausation": False
    }


if __name__ == "__main__":
    root = pathlib.Path(sys.argv[1])
    results = {}
    for directory in sorted(root.glob("block-*")):
        if not directory.is_dir():
            continue
        try:
            results[directory.name] = analyze(directory)
        except (ValueError, KeyError, TypeError, OSError) as error:
            results[directory.name] = {"valid": False, "error": str(error)}
    (root / "scheduler-runtime-analysis.json").write_text(json.dumps(results, indent=2, default=str) + "\n")
    raise SystemExit(0 if results and all(value["valid"] for value in results.values()) else 1)
