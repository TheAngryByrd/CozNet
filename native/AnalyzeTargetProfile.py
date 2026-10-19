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
             "pipeline": {"profile": "pipeline-profile", "plain": "plain-pipeline", "selected": 40, "peer": 20, "durationMs": 120}}
MIN_VISITS = 100
SPEEDUPS = (25, 50, 75)
SELECTED_NAME = "CozNet.TargetLocal.Work::selected(uint64)"
PEER_NAME = "CozNet.TargetLocal.Work::peer(uint64)"
CANDIDATES = {"selected": SELECTED_NAME, "peer": PEER_NAME}


def expected_args(seed, block, workload="latency"):
    w = WORKLOADS[workload]
    selected, peer, plain = w["selected"], w["peer"], w["plain"]
    args = {"profile": f"{w['profile']} {seed + block}", "original": f"{plain} {selected} {peer}"}
    for speedup in SPEEDUPS:
        args[f"selected-{speedup}"] = f"{plain} {selected * (100 - speedup) // 100} {peer}"
        args[f"peer-{speedup}"] = f"{plain} {selected} {peer * (100 - speedup) // 100}"
    return args


def expected_fields(seed, block, workload="latency"):
    w = WORKLOADS[workload]
    selected, peer, plain = w["selected"], w["peer"], w["plain"]
    extra = {"durationMs": w["durationMs"]} if workload != "latency" else {}
    fields = {"profile": {"mode": w["profile"], "seed": seed + block, "selectedRounds": selected, "peerRounds": peer, **extra},
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
    return 1 - (optimized["elapsedNs"] / optimized["visits"]) / (original["elapsedNs"] / original["visits"])


def timing_consistent(record):
    return all(e["EffectiveNs"] == e["ElapsedNs"] - e["GlobalNs"] for e in record["experiments"] if e.get("Passed"))


WARMUP_KEYS = ("Block", "Order", "Candidate", "Speedup", "ElapsedNs", "GlobalNs", "EffectiveNs", "Visits", "Threads", "Passed", "Error")


def warmup_present(record):
    warmup = record.get("warmup")
    return (isinstance(warmup, dict) and all(k in warmup for k in WARMUP_KEYS) and warmup["Speedup"] == 0 and warmup["Block"] == 0
            and warmup["Order"] == -1 and type(warmup["Passed"]) is bool)


def order_ratios(record):
    experiments = record["experiments"]
    baseline = next((e for e in experiments if e["Speedup"] == 0), None)
    if baseline is None or not baseline["Passed"] or not threads_complete(record):
        return {}
    base = [(t["WallNs"] - t["PaymentNs"]) / max(baseline.get("Visits", 1), 1) for t in baseline["Threads"]]
    return {e["Order"]: [((t["WallNs"] - t["PaymentNs"]) / max(e.get("Visits", 1), 1)) / b if b > 0 else None for t, b in zip(e["Threads"], base)]
            for e in experiments if e["Passed"]}


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
                               "difference": None if predicted is None or actual is None else predicted - actual}
        blocks.append({"block": block, "valid": not reasons, "reasons": reasons, "points": points,
                       "orderRatios": order_ratios(profile) if complete else {},
                       "warmupPassed": profile["warmup"]["Passed"] if isinstance(profile, dict) and warmup_present(profile) else None,
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
                "passed": point_passed})
    by_order = {}
    for b in blocks:
        for order, ratios in b["orderRatios"].items():
            if None not in ratios:
                by_order.setdefault(order, []).append(ratios)
    order_summary = {str(o): [statistics.median(r[i] for r in rows) for i in range(2)] for o, rows in sorted(by_order.items())}
    summary = {"passed": passed, "workload": workload, "medianWorkRatioByOrder": order_summary, "inputsUnchanged": manifest["inputsUnchanged"], "medianLimit": MEDIAN_LIMIT, "blockLimit": BLOCK_LIMIT,
               "points": summary_points, "blocks": blocks,
               "supportedModel": "Linux x64 CPU-bound managed threads registered through CozNet, " + {"throughput": "throughput progress point", "pipeline": "throughput progress point behind a TargetQueue"}.get(workload, "end-to-end progress point"),
               "generalClrSafetyEstablished": False}
    (run / "summary.json").write_text(json.dumps(summary, indent=2))
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
