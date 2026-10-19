"""Apply the fixed target-local causal profile criteria to one qualification run directory."""
import json
import pathlib
import statistics
import sys

MEDIAN_LIMIT = 0.05
BLOCK_LIMIT = 0.10
BLOCKS = 5
SEED = 20261018
WORKLOADS = {"latency": {"profile": "causal-profile", "plain": "plain", "selected": 6000, "peer": 3000, "durationMs": 0},
             "throughput": {"profile": "throughput-profile", "plain": "plain-throughput", "selected": 40, "peer": 20, "durationMs": 120},
             "pipeline": {"profile": "pipeline-profile", "plain": "plain-pipeline", "selected": 40, "peer": 20, "durationMs": 120},
             "lock": {"profile": "lock-profile", "plain": "plain-lock", "selected": 40, "peer": 20, "durationMs": 120},
             "spinlock": {"profile": "spin-lock-profile", "plain": "plain-spin-lock", "selected": 40, "peer": 20, "durationMs": 120},
             "semaphore": {"profile": "semaphore-profile", "plain": "plain-semaphore", "selected": 24, "peer": 12, "durationMs": 120},
             "transaction": {"profile": "transaction-profile", "plain": "plain-transaction", "selected": 40, "peer": 8, "durationMs": 120}}
MIN_VISITS = 100
SPEEDUPS = (25, 50, 75)
SELECTED_NAME = "CozNet.TargetLocal.Work::selected(uint64)"
PEER_NAME = "CozNet.TargetLocal.Work::peer(uint64)"
CANDIDATES = {"selected": SELECTED_NAME, "peer": PEER_NAME}


def expected_args(seed, block, workload="latency"):
    w = WORKLOADS[workload]
    selected, peer, plain = w["selected"], w["peer"], w["plain"]
    args = {"profile": f"{w['profile']} {seed} {block - 1}", "original": f"{plain} {selected} {peer}"}
    for speedup in SPEEDUPS:
        args[f"selected-{speedup}"] = f"{plain} {selected * (100 - speedup) // 100} {peer}"
        args[f"peer-{speedup}"] = f"{plain} {selected} {peer * (100 - speedup) // 100}"
    return args


def expected_fields(seed, block, workload="latency"):
    w = WORKLOADS[workload]
    selected, peer, plain = w["selected"], w["peer"], w["plain"]
    extra = {"durationMs": w["durationMs"]} if workload != "latency" else {}
    fields = {"profile": {"mode": w["profile"], "seed": seed, "rotation": block - 1, "selectedRounds": selected, "peerRounds": peer, **extra},
              "original": {"mode": plain, "selectedRounds": selected, "peerRounds": peer, **extra}}
    for speedup in SPEEDUPS:
        fields[f"selected-{speedup}"] = {"mode": plain, "selectedRounds": selected * (100 - speedup) // 100, "peerRounds": peer, **extra}
        fields[f"peer-{speedup}"] = {"mode": plain, "selectedRounds": selected, "peerRounds": peer * (100 - speedup) // 100, **extra}
    return fields


def visits_sufficient(record, workload):
    if workload == "latency":
        return all(e.get("Visits", 1) == 1 for e in record.get("experiments", []) if e.get("Passed"))
    if "experiments" in record:
        return all(e.get("Visits", 0) >= MIN_VISITS for e in record["experiments"] if e.get("Passed"))
    return record.get("visits", 0) >= MIN_VISITS


def actual_gain(original, optimized, workload):
    if not original or not optimized or not original.get("elapsedNs") or not optimized.get("elapsedNs"):
        return None
    if workload == "latency":
        return 1 - optimized["elapsedNs"] / original["elapsedNs"]
    if not original.get("visits") or not optimized.get("visits"):
        return None
    if workload == "transaction":
        if not all(type(r.get("latencyNs")) is int and r["latencyNs"] > 0 for r in (original, optimized)):
            return None
        return 1 - (optimized["latencyNs"] / optimized["visits"]) / (original["latencyNs"] / original["visits"])
    return 1 - (optimized["elapsedNs"] / optimized["visits"]) / (original["elapsedNs"] / original["visits"])


def latency_counts_complete(profile, data):
    experiments = profile.get("experiments") if isinstance(profile, dict) else None
    if isinstance(experiments, list) and isinstance(profile.get("warmup"), dict):
        experiments = experiments + [profile["warmup"]]
    if not isinstance(experiments, list) or not all(type(e.get("Arrivals")) is int and type(e.get("LatencyNs")) is int for e in experiments):
        return False
    if not all(e["Arrivals"] == e["Visits"] for e in experiments if e.get("Passed")):
        return False
    return all(value is not None and type(value.get("arrivals")) is int and type(value.get("latencyNs")) is int and value["arrivals"] == value.get("visits")
               for arm, value in data.items() if arm != "profile")


def timing_consistent(record):
    return all(e["EffectiveNs"] == e["ElapsedNs"] - e["GlobalNs"] for e in record["experiments"] if e.get("Passed"))


WARMUP_KEYS = ("Block", "Order", "Candidate", "Speedup", "ElapsedNs", "GlobalNs", "EffectiveNs", "Visits", "Threads", "Passed", "Error")


FAILURE_KEYS = ("Worker", "Detail", "DebtNs", "TimeNs", "GapNs", "PreviousKind", "ContextEvents", "Context", "Workers")


def failed_experiments(record):
    if not isinstance(record, dict) or not isinstance(record.get("experiments"), list):
        return []
    experiments = record["experiments"] + ([record["warmup"]] if isinstance(record.get("warmup"), dict) else [])
    return [{"candidate": e.get("Candidate"), "speedup": e.get("Speedup"), "order": e.get("Order"), "error": e.get("Error"),
             "collections": e.get("Collections"), "gcPauseNs": e.get("GcPauseNs"),
             "failures": [{k: f.get(k) for k in FAILURE_KEYS} for f in e.get("Failures") or [] if isinstance(f, dict)],
             "threads": [{k: t.get(k) for k in ("CpuNs", "WallNs", "RunDelayNs")} for t in e.get("Threads") or [] if isinstance(t, dict)]}
            for e in experiments if isinstance(e, dict) and not e.get("Passed")]


def warmup_present(record):
    warmup = record.get("warmup")
    return (isinstance(warmup, dict) and all(k in warmup for k in WARMUP_KEYS) and warmup["Speedup"] == 0 and warmup["Block"] == 0
            and warmup["Order"] == -1 and type(warmup["Passed"]) is bool)


def order_ratios(record):
    experiments = record["experiments"]
    baseline = next((e for e in experiments if e["Speedup"] == 0), None)
    complete = lambda e: isinstance(e.get("Threads"), list) and len(e["Threads"]) == 2
    if baseline is None or not baseline["Passed"] or not complete(baseline):
        return {}
    base = [(t["WallNs"] - t["PaymentNs"]) / max(baseline.get("Visits", 1), 1) for t in baseline["Threads"]]
    return {e["Order"]: [((t["WallNs"] - t["PaymentNs"]) / max(e.get("Visits", 1), 1)) / b if b > 0 else None for t, b in zip(e["Threads"], base)]
            for e in experiments if e["Passed"] and complete(e)}


def positions_balanced(blocks):
    seen = {}
    for b in blocks:
        for candidate, speedup, order in b.get("positions") or []:
            seen.setdefault((candidate, speedup), []).append(order)
    expected = len(CANDIDATES) * len(SPEEDUPS) + 1
    return len(seen) == expected and all(len(v) == BLOCKS and len(set(v)) == BLOCKS for v in seen.values())


def threads_complete(record):
    return all(isinstance(e.get("Threads"), list) and len(e["Threads"]) == 2 for e in record["experiments"] if e.get("Passed"))


def work_ratios(record, candidate, speedup):
    experiments = record["experiments"]
    baseline = next((e for e in experiments if e["Speedup"] == 0), None)
    point = next((e for e in experiments if e["Candidate"] == candidate and e["Speedup"] == speedup), None)
    if baseline is None or point is None or not (baseline["Passed"] and point["Passed"]) or not threads_complete(record):
        return None
    ratios = []
    for b, p in zip(baseline["Threads"], point["Threads"]):
        base = (b["WallNs"] - b["PaymentNs"]) / max(baseline.get("Visits", 1), 1)
        value = (p["WallNs"] - p["PaymentNs"]) / max(point.get("Visits", 1), 1)
        ratios.append(value / base if base > 0 else None)
    return ratios


STAT_KEYS = ("entries", "waitedEntries", "handoffNs")


def lock_stats_complete(profile, arms):
    stats = profile.get("lockStats")
    if not isinstance(stats, list) or len(stats) != len(profile["experiments"]) + 1:
        return False
    plain = [v for k, v in arms.items() if k != "profile" and v is not None]
    return (all(isinstance(s, dict) and all(type(s.get(k)) is int and s[k] >= 0 for k in STAT_KEYS) for s in stats)
            and all(all(type(v.get(k)) is int and v[k] >= 0 for k in STAT_KEYS) for v in plain))


def handoff_shares(profile, plain, candidate, speedup):
    stats = profile.get("lockStats") if isinstance(profile, dict) else None
    experiments = profile.get("experiments") if isinstance(profile, dict) else None
    if not isinstance(stats, list) or not isinstance(experiments, list) or len(stats) != len(experiments) + 1:
        return None
    point = next((e for e in experiments if e["Candidate"] == candidate and e["Speedup"] == speedup), None)
    if point is None or not point.get("ElapsedNs"):
        return None
    profiled = stats[point["Order"] + 1]["handoffNs"] / point["ElapsedNs"]
    real = plain["handoffNs"] / plain["elapsedNs"] if plain and plain.get("elapsedNs") and "handoffNs" in plain else None
    return [profiled, real]


def driver_matches(record):
    points = record.get("points")
    expected = sorted((name, s) for name in CANDIDATES.values() for s in SPEEDUPS)
    if not isinstance(points, list) or sorted((p.get("candidate"), p.get("speedup")) for p in points) != expected:
        return False
    for point in points:
        computed = prediction(record, point.get("candidate"), point.get("speedup"))
        written = point.get("prediction")
        if (computed is None) != (written is None) or (computed is not None and abs(computed - written) > 1e-12):
            return False
    return True


def profile_complete(record):
    experiments = record.get("experiments") if isinstance(record, dict) else None
    if not isinstance(experiments, list) or len(experiments) != 7:
        return False
    arms = sorted((e.get("Candidate"), e.get("Speedup")) for e in experiments if e.get("Speedup") != 0)
    expected = sorted((name, s) for name in CANDIDATES.values() for s in SPEEDUPS)
    return (sum(1 for e in experiments if e.get("Speedup") == 0) == 1 and arms == expected
            and sorted(e.get("Order") for e in experiments) == list(range(7)) and all(e.get("Block") == 1 for e in experiments))


def prediction(record, candidate, speedup):
    experiments = record["experiments"]
    baseline = next((e for e in experiments if e["Speedup"] == 0), None)
    point = next((e for e in experiments if e["Candidate"] == candidate and e["Speedup"] == speedup), None)
    if baseline is None or point is None or not (baseline["Passed"] and point["Passed"] and baseline["EffectiveNs"] > 0):
        return None
    if baseline.get("Visits", 1) <= 0 or point.get("Visits", 1) <= 0:
        return None
    if record.get("mode") == "transaction-profile":
        fields = ("LatencyNs", "ElapsedNs", "EffectiveNs", "Visits")
        if not all(type(e.get(k)) is int and e[k] > 0 for e in (baseline, point) for k in fields):
            return None
        latency = lambda e: e["LatencyNs"] / e["Visits"] * e["EffectiveNs"] / e["ElapsedNs"]
        return 1 - latency(point) / latency(baseline)
    return 1 - (point["EffectiveNs"] / point.get("Visits", 1)) / (baseline["EffectiveNs"] / baseline.get("Visits", 1))


def load(path):
    return json.loads(path.read_text()) if path.exists() else None


def main():
    run = pathlib.Path(sys.argv[1]).resolve()
    manifest = load(run / "manifest.json")
    workload = manifest.get("workload", "latency")
    w = WORKLOADS[workload]
    passed = (manifest["inputsUnchanged"] and manifest["blocks"] == BLOCKS and manifest["seed"] == SEED and manifest["selected"] == w["selected"]
              and manifest["peer"] == w["peer"] and tuple(manifest["speedups"]) == SPEEDUPS)
    blocks = []
    for block in range(1, BLOCKS + 1):
        records = [r for r in manifest["runs"] if r["block"] == block]
        args = expected_args(manifest["seed"], block, workload)
        data = {arm: load(run / f"{block}-{arm}.json") for arm in args}
        reasons = [f"{arm} missing" for arm, value in data.items() if value is None]
        if sorted(r["arm"] for r in records) != sorted(args):
            reasons.append("the manifest does not hold exactly one record for each arm")
        reasons += [f"{r['arm']} exit {r['exitCode']}" for r in records if r["exitCode"] != 0]
        reasons += [f"{r['arm']} has arguments {r.get('args')}" for r in records if r.get("args") != args.get(r["arm"])]
        for arm, fields in expected_fields(manifest["seed"], block, workload).items():
            value = data.get(arm)
            if value is not None:
                reasons += [f"{arm} records {k} {value.get(k)}" for k, v in fields.items() if value.get(k) != v]
        profile = data["profile"]
        complete = profile is not None and profile_complete(profile)
        if not complete:
            reasons.append("the profile record does not hold the fixed experiment design")
        elif not timing_consistent(profile):
            reasons.append("a passed experiment has an effective duration other than elapsed minus global delay")
        elif not driver_matches(profile):
            reasons.append("the driver predictions differ from the experiments")
        elif not threads_complete(profile):
            reasons.append("a passed experiment does not hold two thread records")
        elif not warmup_present(profile):
            reasons.append("the profile record does not hold its warm-up experiment")
        elif workload in ("lock", "spinlock") and not lock_stats_complete(profile, data):
            reasons.append("a lock record does not hold its handoff counts")
        elif workload == "transaction" and not latency_counts_complete(profile, data):
            reasons.append("a transaction record does not hold its arrivals and latency, or its arrivals differ from its departures")
        reasons += [f"{arm} not passed" for arm, value in data.items() if value is not None and not value.get("passed")]
        reasons += [f"{arm} has fewer than {MIN_VISITS} visits" for arm, value in data.items() if value is not None and not visits_sufficient(value, workload)]
        points = {}
        for label, name in CANDIDATES.items():
            for speedup in SPEEDUPS:
                key = f"{label}-{speedup}"
                predicted = prediction(profile, name, speedup) if complete else None
                original, optimized = data["original"], data[key]
                actual = actual_gain(original, optimized, workload)
                points[key] = {"predicted": predicted, "actual": actual, "workRatios": work_ratios(profile, name, speedup) if complete else None,
                               "handoffShares": handoff_shares(profile, optimized, name, speedup)
                               if complete and workload in ("lock", "spinlock") and lock_stats_complete(profile, data) else None,
                               "difference": None if predicted is None or actual is None else predicted - actual}
        blocks.append({"block": block, "valid": not reasons, "reasons": reasons, "points": points,
                       "orderRatios": order_ratios(profile) if complete else {},
                       "warmupPassed": profile["warmup"]["Passed"] if isinstance(profile, dict) and warmup_present(profile) else None,
                       "failedExperiments": failed_experiments(profile),
                       "positions": [[e["Candidate"], e["Speedup"], e["Order"]] for e in profile["experiments"]] if complete else None,
                       "order": [[e["Candidate"], e["Speedup"]] for e in sorted(profile["experiments"], key=lambda e: e["Order"])] if complete else None})
    summary_points = []
    for label in CANDIDATES:
        for speedup in SPEEDUPS:
            key = f"{label}-{speedup}"
            valid = [b for b in blocks if b["valid"] and b["points"][key]["difference"] is not None]
            differences = [abs(b["points"][key]["difference"]) for b in valid]
            rows = [b["points"][key]["workRatios"] for b in valid if b["points"][key]["workRatios"] and None not in b["points"][key]["workRatios"]]
            median = statistics.median(differences) if differences else None
            point_passed = len(valid) == BLOCKS and median is not None and median <= MEDIAN_LIMIT and max(differences) <= BLOCK_LIMIT
            passed = passed and point_passed
            summary_points.append({
                "point": key, "validBlocks": len(valid), "medianAbsoluteDifference": median,
                "maxAbsoluteDifference": max(differences) if differences else None,
                "medianPredicted": statistics.median(b["points"][key]["predicted"] for b in valid) if valid else None,
                "medianActual": statistics.median(b["points"][key]["actual"] for b in valid) if valid else None,
                "medianWorkRatios": [statistics.median(r[i] for r in rows) for i in range(2)] if rows else None,
                "medianHandoffShares": [statistics.median(h[i] for h in shares) for i in range(2)] if (shares := [b["points"][key]["handoffShares"] for b in valid
                    if b["points"][key].get("handoffShares") and None not in b["points"][key]["handoffShares"]]) else None,
                "passed": point_passed})
    balanced = positions_balanced(blocks)
    warmups = all(b["warmupPassed"] is True for b in blocks)
    passed = passed and balanced and warmups
    by_order = {}
    for b in blocks:
        for order, ratios in b["orderRatios"].items():
            if None not in ratios:
                by_order.setdefault(order, []).append(ratios)
    order_summary = {str(o): [statistics.median(r[i] for r in rows) for i in range(2)] for o, rows in sorted(by_order.items())}
    summary = {"passed": passed, "workload": workload, "positionsBalanced": balanced, "warmupsPassed": warmups, "medianWorkRatioByOrder": order_summary, "inputsUnchanged": manifest["inputsUnchanged"], "medianLimit": MEDIAN_LIMIT, "blockLimit": BLOCK_LIMIT,
               "points": summary_points, "blocks": blocks,
               "supportedModel": "Linux x64 CPU-bound managed threads registered through CozNet, " + {"throughput": "throughput progress point", "pipeline": "throughput progress point behind a TargetQueue", "lock": "throughput progress point with a TargetLock", "spinlock": "throughput progress point with a TargetLock that spins up to 1 ms", "semaphore": "throughput progress point with two workers that take turns through TargetSemaphore", "transaction": "latency progress point around a TargetQueue pipeline"}.get(workload, "end-to-end progress point"),
               "generalClrSafetyEstablished": False}
    (run / "summary.json").write_text(json.dumps(summary, indent=2))
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
