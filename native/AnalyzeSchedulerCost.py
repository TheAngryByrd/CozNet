import json
import math
import pathlib
import statistics
import sys
from AnalyzeScheduler import parse_capture


def analyze(root):
    environment = json.loads((root / "environment.json").read_text(encoding="utf-8-sig"))
    outcomes = json.loads((root / "outcomes.json").read_text(encoding="utf-8-sig"))
    expected = {(block, mode) for block in range(1, 9) for mode in ("enabled", "disabled")}
    order = [(case["block"], case["mode"]) for case in environment["order"]]
    if len(order) != 16 or set(order) != expected:
        raise ValueError("Expected eight complete paired blocks")
    names = [f"block-{block}-{mode}" for block, mode in order]
    if [outcome["name"] for outcome in outcomes] != names or any(outcome["exitCode"] != 0 for outcome in outcomes):
        raise ValueError("Scheduler cost process failed or its recorded order differs")
    cases = {}
    for block, mode in order:
        data = json.loads((root / f"block-{block}-{mode}.json").read_text(encoding="utf-8-sig"))
        if not data["passed"] or data["failures"] or not data["joined"] or not data["stopped"]:
            raise ValueError("Scheduler cost workload failed")
        if data["mode"] != mode or data["iterationsPerThread"] != 300000 or data["checksums"] != [14998500000000] * 3:
            raise ValueError("Scheduler cost fixed work differs")
        if data["elapsedTicks"] <= 0 or data["processCpuTicks"] <= 0 or data["stopwatchFrequency"] <= 0:
            raise ValueError("Invalid scheduler cost measurement")
        if len(data["captures"]) != 3:
            raise ValueError("Expected three scheduler capture slots")
        if mode == "enabled":
            for capture in data["captures"]:
                parse_capture(capture)
            if len({capture["ThreadId"] for capture in data["captures"]}) != 3:
                raise ValueError("Scheduler cost thread identities repeat")
        elif any(capture is not None for capture in data["captures"]):
            raise ValueError("Disabled scheduler cost contains capture")
        cases[block, mode] = {
            "wallMilliseconds": data["elapsedTicks"] * 1000 / data["stopwatchFrequency"],
            "processCpuMilliseconds": data["processCpuTicks"] / 10000,
            "cleanupMilliseconds": data["cleanupTicks"] * 1000 / data["stopwatchFrequency"]}
    pairs = []
    for block in range(1, 9):
        baseline, enabled = cases[block, "disabled"], cases[block, "enabled"]
        pairs.append({"block": block, "disabled": baseline, "enabled": enabled,
                      "wallIncreasePercent": (enabled["wallMilliseconds"] / baseline["wallMilliseconds"] - 1) * 100,
                      "processCpuIncreasePercent": (enabled["processCpuMilliseconds"] / baseline["processCpuMilliseconds"] - 1) * 100})
    summary = {}
    for metric in ("wallIncreasePercent", "processCpuIncreasePercent"):
        values = [pair[metric] for pair in pairs]
        mean = statistics.mean(values)
        half = 2.364624251 * statistics.stdev(values) / math.sqrt(8)
        summary[metric] = {"mean": mean, "lower95": mean - half, "upper95": mean + half}
    return {"passed": True, "pairs": pairs, "summary": summary,
            "coversSignalWorkload": False, "includesCaptureSetupOrCleanup": False}


if __name__ == "__main__":
    root = pathlib.Path(sys.argv[1])
    try:
        result = analyze(root)
    except Exception as error:
        (root / "analysis.json").write_text(json.dumps({"passed": False, "error": str(error)}, indent=2) + "\n")
        raise
    (root / "analysis.json").write_text(json.dumps(result, indent=2) + "\n")
