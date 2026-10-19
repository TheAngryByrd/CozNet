"""Apply the fixed target-local prediction acceptance criteria to one qualification run directory."""
import json
import pathlib
import statistics
import sys

MEDIAN_LIMIT = 0.05
BLOCK_LIMIT = 0.10
BLOCKS = 5
ARMS = ("profile-zero", "profile-speedup", "original", "optimized")
CONFIGURATIONS = {"selected-longer": (6000, 3000), "peer-longer": (3000, 6000), "selected-dominant": (8000, 2000)}
SPEEDUP = 50
SEED = 20261018


def expected_args(selected, peer):
    optimized = selected * (100 - SPEEDUP) // 100
    return {"profile-zero": f"profile 0 {selected} {peer}", "profile-speedup": f"profile {SPEEDUP} {selected} {peer}",
            "original": f"plain {selected} {peer}", "optimized": f"plain {optimized} {peer}"}


def expected_fields(arm, selected, peer):
    optimized = selected * (100 - SPEEDUP) // 100
    return {"profile-zero": {"speedup": 0, "selectedRounds": selected, "peerRounds": peer},
            "profile-speedup": {"speedup": SPEEDUP, "selectedRounds": selected, "peerRounds": peer},
            "original": {"selectedRounds": selected, "peerRounds": peer},
            "optimized": {"selectedRounds": optimized, "peerRounds": peer}}[arm]


BARRIER_CONFIGURATIONS = {"barrier-alternating": "alternating", "barrier-selected-dominant": "selected-dominant",
                          "barrier-peer-bound": "peer-bound"}


def barrier_args(pattern):
    return {"profile-zero": f"profile-phases 0 {pattern}", "profile-speedup": f"profile-phases {SPEEDUP} {pattern}",
            "original": f"plain-phases {pattern} 100", "optimized": f"plain-phases {pattern} {100 - SPEEDUP}"}


def barrier_fields(arm, pattern):
    return {"profile-zero": {"speedup": 0, "pattern": pattern, "phases": 10},
            "profile-speedup": {"speedup": SPEEDUP, "pattern": pattern, "phases": 10},
            "original": {"pattern": pattern, "selectedPercent": 100, "phases": 10},
            "optimized": {"pattern": pattern, "selectedPercent": 100 - SPEEDUP, "phases": 10}}[arm]


def load(run, name):
    path = run / name
    return json.loads(path.read_text()) if path.exists() else None


SAMPLE, WAKE, POST, FAILURE = 0, 6, 7, 4
DEBT_EXCEEDED, DEPENDENCY = 1, 0
START_FIELDS = ("workStartedNs", "startedNs", "finishedNs", "startRunDelayNs", "runDelayNs")


def start_complete(thread):
    return (all(type(thread.get(k)) is int for k in START_FIELDS)
            and thread["startedNs"] <= thread["workStartedNs"] <= thread["finishedNs"]
            and 0 <= thread["startRunDelayNs"] <= thread["runDelayNs"])


TRACE_KEYS = ("ticks", "cpuTimeNs", "runDelayNs", "minorFaults", "majorFaults", "voluntarySwitches", "involuntarySwitches")
SEGMENTS = ("setup", "barrier")


def trace_complete(thread):
    points = thread.get("startTrace")
    if not start_complete(thread) or not isinstance(points, list) or len(points) != 3:
        return False
    if not all(isinstance(p, dict) and all(type(p.get(k)) is int and p[k] >= 0 for k in TRACE_KEYS) for p in points):
        return False
    return (thread["startedNs"] <= points[0]["ticks"] and points[-1]["ticks"] <= thread["workStartedNs"]
            and all(points[i + 1][k] >= points[i][k] for i in range(2) for k in TRACE_KEYS))


def trace_segments(thread):
    points = thread["startTrace"]
    segments = {"nativeToFirstNs": points[0]["ticks"] - thread["startedNs"]}
    for name, a, b in zip(SEGMENTS, points, points[1:]):
        wall, cpu, queue = b["ticks"] - a["ticks"], b["cpuTimeNs"] - a["cpuTimeNs"], b["runDelayNs"] - a["runDelayNs"]
        segments[name] = {"wallNs": wall, "onCpuNs": cpu, "runQueueNs": queue, "offCpuNs": wall - cpu - queue,
                          **{k: b[k] - a[k] for k in TRACE_KEYS[3:]}}
    return segments


def add_largest(largest, diagnostics):
    for segments in (t for d in diagnostics.values() if d for t in d["startSegments"] if t):
        largest["nativeToFirstNs"] = max(largest["nativeToFirstNs"], segments["nativeToFirstNs"])
        for segment in SEGMENTS:
            largest[segment] = max(largest[segment], segments[segment]["wallNs"])


def residual_run_delay(thread, complete):
    waits = thread.get("waitRunDelayNs", [])
    if not complete or any(d < 0 for d in waits):
        return None
    return thread["runDelayNs"] - thread["startRunDelayNs"] - sum(waits)


def wait_diagnostics(data):
    if data is None:
        return None
    if "threads" not in data or "events" not in data:
        return {"complete": False, "startComplete": False, "startTraceComplete": False, "startSegments": [], "workStartNs": [], "waits": 0, "maxWaitRunDelayNs": None,
                "totalWaitRunDelayNs": 0, "unreadWaits": 0, "debtFailures": []}
    events = sorted(data["events"], key=lambda e: e[9])
    origin = events[0][9] if events else 0
    threads = data["threads"]
    starts = [start_complete(t) for t in threads]
    traces = [trace_segments(t) if trace_complete(t) else None for t in threads]
    wakes = {i: [e for e in events if e[0] == i and e[2] == WAKE] for i in range(len(threads))}
    complete = all("waitRunDelayNs" in t and len(t["waitRunDelayNs"]) == len(wakes[i]) and t.get("waitRunDelaysDropped") == 0
                   for i, t in enumerate(threads))
    first_samples = [next((e[9] - origin for e in events if e[0] == i and e[2] == SAMPLE), None) for i in range(len(threads))]
    failures = []
    for failure in (e for e in events if e[2] == FAILURE and e[8] == DEBT_EXCEEDED):
        item = {"worker": failure[0], "timeNs": failure[9] - origin, "debtNs": failure[5], "afterWake": False}
        prior = [w for w in wakes.get(failure[0], []) if w[9] <= failure[9]]
        if prior:
            wake, delays = prior[-1], threads[failure[0]].get("waitRunDelayNs", [])
            post = (next((e for e in reversed(events) if e[2] == POST and e[9] <= wake[9] and e[5] == wake[6]), None)
                    if wake[8] == DEPENDENCY else None)
            item.update({"afterWake": True, "wakeReason": wake[8], "waitRunDelayNs": delays[len(prior) - 1] if len(prior) <= len(delays) else None,
                         "postToWakeNs": wake[9] - post[9] if post else None, "wakeToFailureNs": failure[9] - wake[9]})
        else:
            thread, started = threads[failure[0]], starts[failure[0]]
            item.update({"startRunDelayNs": thread["startRunDelayNs"] if started else None,
                         "workStartToFailureNs": failure[9] - thread["workStartedNs"] if started else None,
                         "firstSampleNs": first_samples, "residualRunDelayNs": residual_run_delay(thread, started),
                         "startSegments": traces[failure[0]]})
        failures.append(item)
    values = [d for t in threads for d in t.get("waitRunDelayNs", []) if d >= 0]
    return {"complete": complete, "startComplete": all(starts), "startTraceComplete": all(t is not None for t in traces), "startSegments": traces,
            "workStartNs": [t["workStartedNs"] - origin if ok else None for t, ok in zip(threads, starts)], "waits": sum(len(w) for w in wakes.values()), "maxWaitRunDelayNs": max(values, default=None),
            "totalWaitRunDelayNs": sum(values), "unreadWaits": sum(1 for t in threads for d in t.get("waitRunDelayNs", []) if d < 0),
            "debtFailures": failures}


def main():
    run = pathlib.Path(sys.argv[1]).resolve()
    manifest = load(run, "manifest.json")
    configurations = []
    barrier = manifest.get("suite") == "barrier"
    table = BARRIER_CONFIGURATIONS if barrier else CONFIGURATIONS
    args_for = (lambda value: barrier_args(value)) if barrier else (lambda value: expected_args(*value))
    fields_for = (lambda arm, value: barrier_fields(arm, value)) if barrier else (lambda arm, value: expected_fields(arm, *value))
    declared = ({c["name"]: c["pattern"] for c in manifest["configurations"]} if barrier
                else {c["name"]: (c["selected"], c["peer"]) for c in manifest["configurations"]})
    known = {(name, block) for name in table for block in range(1, BLOCKS + 1)}
    unknown = [r for r in manifest["runs"] if (r["configuration"], r["block"]) not in known]
    passed = (manifest["inputsUnchanged"] and manifest["blocks"] == BLOCKS and manifest["speedup"] == SPEEDUP and manifest["seed"] == SEED
              and declared == table and len(manifest["configurations"]) == len(table) and not unknown)
    diagnostics_complete = True
    start_diagnostics_complete = True
    start_trace_complete = True
    largest = {"nativeToFirstNs": 0, "setup": 0, "barrier": 0}
    for configuration in manifest["configurations"]:
        name = configuration["name"]
        blocks = []
        for block in range(1, manifest["blocks"] + 1):
            arms = {arm: load(run, f"{name}-{block}-{arm}.json") for arm in ARMS}
            records = [r for r in manifest["runs"] if r["configuration"] == name and r["block"] == block]
            exits = {r["arm"]: r["exitCode"] for r in records}
            reasons = [f"{arm} missing" for arm, data in arms.items() if data is None]
            if sorted(r["arm"] for r in records) != sorted(ARMS):
                reasons.append("the manifest does not hold exactly one record for each arm")
            reasons += [f"{arm} exit {code}" for arm, code in exits.items() if code != 0]
            reasons += [f"{arm} not passed" for arm, data in arms.items() if data is not None and not data.get("passed")]
            rounds = table.get(name)
            if rounds is None:
                reasons.append("unknown configuration")
            else:
                args = args_for(rounds)
                reasons += [f"{r['arm']} has arguments {r.get('args')}" for r in records if r.get("args") != args.get(r["arm"])]
                for arm, data in arms.items():
                    if data is not None:
                        for field, value in fields_for(arm, rounds).items():
                            if data.get(field) != value:
                                reasons.append(f"{arm} records {field} {data.get(field)}")
            record = {"block": block, "valid": not reasons, "reasons": reasons,
                      "waitDiagnostics": {arm: wait_diagnostics(arms[arm]) for arm in ("profile-zero", "profile-speedup")}}
            diagnostics_complete = diagnostics_complete and all(d is None or d["complete"] for d in record["waitDiagnostics"].values())
            start_diagnostics_complete = start_diagnostics_complete and all(d is None or d["startComplete"] for d in record["waitDiagnostics"].values())
            start_trace_complete = start_trace_complete and all(d is None or d["startTraceComplete"] for d in record["waitDiagnostics"].values())
            add_largest(largest, record["waitDiagnostics"])
            if not reasons:
                zero, speed, original, optimized = (arms[a] for a in ("profile-zero", "profile-speedup", "original", "optimized"))
                predicted = 1 - speed["effectiveNs"] / zero["effectiveNs"]
                actual = 1 - optimized["elapsedNs"] / original["elapsedNs"]
                record.update({
                    "predicted": predicted, "actual": actual, "difference": predicted - actual,
                    "overhead": zero["elapsedNs"] / original["elapsedNs"],
                    "zeroHandlerShare": zero.get("totalHandlerNs", 0) / zero["elapsedNs"] if "totalHandlerNs" in zero else None,
                    "skippedNs": (speed.get("replay") or {}).get("SkippedDelay"),
                    "speedupWallMinusCountedCpuNs": max((t["wallNs"] - t["cpuNs"] for t in speed["threads"] if "cpuNs" in t), default=None),
                    "speedupMaxRunDelayNs": max((t["runDelayNs"] for t in speed["threads"] if t.get("runDelayNs", -1) >= 0), default=None),
                    "zeroMaxRunDelayNs": max((t["runDelayNs"] for t in zero["threads"] if t.get("runDelayNs", -1) >= 0), default=None),
                    "runDelayMissing": any(t.get("runDelayNs", -1) < 0 for t in speed["threads"] + zero["threads"]),
                    "zeroWallMinusCountedCpuNs": max((t["wallNs"] - t["cpuNs"] for t in zero["threads"] if "cpuNs" in t), default=None),
                    "speedupHandlerShare": speed.get("totalHandlerNs", 0) / speed["elapsedNs"] if "totalHandlerNs" in speed else None,
                    "profileZeroElapsedNs": zero["elapsedNs"], "profileSpeedupElapsedNs": speed["elapsedNs"],
                    "profileSpeedupGlobalNs": speed["globalNs"], "originalElapsedNs": original["elapsedNs"],
                    "optimizedElapsedNs": optimized["elapsedNs"],
                    "maxHandlerNs": max(t["maxHandlerNs"] for t in speed["threads"] + zero["threads"]),
                })
            blocks.append(record)
        valid = [b for b in blocks if b["valid"]]
        differences = [abs(b["difference"]) for b in valid]
        median = statistics.median(differences) if differences else None
        configuration_passed = (len(valid) == len(blocks) and median is not None and median <= MEDIAN_LIMIT
                                and max(differences) <= BLOCK_LIMIT)
        passed = passed and configuration_passed
        configurations.append({
            "name": name, "selectedRounds": configuration.get("selected"), "peerRounds": configuration.get("peer"),
            "pattern": configuration.get("pattern"),
            "medianSkippedNs": statistics.median(b["skippedNs"] for b in valid) if valid and all(b["skippedNs"] is not None for b in valid) else None,
            "blocks": blocks, "validBlocks": len(valid), "medianAbsoluteDifference": median,
            "maxAbsoluteDifference": max(differences) if differences else None,
            "medianPredicted": statistics.median(b["predicted"] for b in valid) if valid else None,
            "medianActual": statistics.median(b["actual"] for b in valid) if valid else None,
            "medianOverhead": statistics.median(b["overhead"] for b in valid) if valid else None,
            "medianZeroHandlerShare": statistics.median(b["zeroHandlerShare"] for b in valid) if valid and all(b["zeroHandlerShare"] is not None for b in valid) else None,
            "medianSpeedupHandlerShare": statistics.median(b["speedupHandlerShare"] for b in valid) if valid and all(b["speedupHandlerShare"] is not None for b in valid) else None,
            "passed": configuration_passed,
        })
    observed = sum(d["totalWaitRunDelayNs"] for c in configurations for b in c["blocks"]
                   for d in b["waitDiagnostics"].values() if d is not None)
    if barrier:
        passed = passed and observed > 0
    passed = passed and diagnostics_complete and start_diagnostics_complete and start_trace_complete
    summary = {"passed": passed, "waitDiagnosticsComplete": diagnostics_complete, "startDiagnosticsComplete": start_diagnostics_complete,
               "startTraceComplete": start_trace_complete, "largestStartSegmentNs": largest, "totalWaitRunDelayNs": observed, "inputsUnchanged": manifest["inputsUnchanged"], "medianLimit": MEDIAN_LIMIT,
               "blockLimit": BLOCK_LIMIT, "speedup": manifest["speedup"], "configurations": configurations,
               "supportedModel": "Linux x64 CPU-bound managed fork/join threads registered through CozNet",
               "generalClrSafetyEstablished": False}
    (run / "summary.json").write_text(json.dumps(summary, indent=2))
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
