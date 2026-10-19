"""Apply the fixed target-local causal profile criteria to one qualification run directory."""
import json
import pathlib
import statistics
import sys

MEDIAN_LIMIT = 0.05
BLOCK_LIMIT = 0.10
BLOCKS = 5
SEED = 20261018
SELECTED, PEER = 6000, 3000
SPEEDUPS = (25, 50, 75)
SELECTED_NAME = "CozNet.TargetLocal.Work::selected(uint64)"
PEER_NAME = "CozNet.TargetLocal.Work::peer(uint64)"
CANDIDATES = {"selected": SELECTED_NAME, "peer": PEER_NAME}


def expected_args(seed, block):
    args = {"profile": f"causal-profile {seed + block}", "original": f"plain {SELECTED} {PEER}"}
    for speedup in SPEEDUPS:
        args[f"selected-{speedup}"] = f"plain {SELECTED * (100 - speedup) // 100} {PEER}"
        args[f"peer-{speedup}"] = f"plain {SELECTED} {PEER * (100 - speedup) // 100}"
    return args


def expected_fields(seed, block):
    fields = {"profile": {"mode": "causal-profile", "seed": seed + block, "selectedRounds": SELECTED, "peerRounds": PEER},
              "original": {"mode": "plain", "selectedRounds": SELECTED, "peerRounds": PEER}}
    for speedup in SPEEDUPS:
        fields[f"selected-{speedup}"] = {"mode": "plain", "selectedRounds": SELECTED * (100 - speedup) // 100, "peerRounds": PEER}
        fields[f"peer-{speedup}"] = {"mode": "plain", "selectedRounds": SELECTED, "peerRounds": PEER * (100 - speedup) // 100}
    return fields


def timing_consistent(record):
    return all(e["EffectiveNs"] == e["ElapsedNs"] - e["GlobalNs"] for e in record["experiments"] if e.get("Passed"))


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
    return 1 - point["EffectiveNs"] / baseline["EffectiveNs"]


def load(path):
    return json.loads(path.read_text()) if path.exists() else None


def main():
    run = pathlib.Path(sys.argv[1]).resolve()
    manifest = load(run / "manifest.json")
    passed = (manifest["inputsUnchanged"] and manifest["blocks"] == BLOCKS and manifest["seed"] == SEED and manifest["selected"] == SELECTED
              and manifest["peer"] == PEER and tuple(manifest["speedups"]) == SPEEDUPS)
    blocks = []
    for block in range(1, BLOCKS + 1):
        records = [r for r in manifest["runs"] if r["block"] == block]
        args = expected_args(manifest["seed"], block)
        data = {arm: load(run / f"{block}-{arm}.json") for arm in args}
        reasons = [f"{arm} missing" for arm, value in data.items() if value is None]
        if sorted(r["arm"] for r in records) != sorted(args):
            reasons.append("the manifest does not hold exactly one record for each arm")
        reasons += [f"{r['arm']} exit {r['exitCode']}" for r in records if r["exitCode"] != 0]
        reasons += [f"{r['arm']} has arguments {r.get('args')}" for r in records if r.get("args") != args.get(r["arm"])]
        for arm, fields in expected_fields(manifest["seed"], block).items():
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
        reasons += [f"{arm} not passed" for arm, value in data.items() if value is not None and not value.get("passed")]
        points = {}
        for label, name in CANDIDATES.items():
            for speedup in SPEEDUPS:
                key = f"{label}-{speedup}"
                predicted = prediction(profile, name, speedup) if complete else None
                original, optimized = data["original"], data[key]
                actual = (1 - optimized["elapsedNs"] / original["elapsedNs"]) if original and optimized and original.get("elapsedNs") else None
                points[key] = {"predicted": predicted, "actual": actual,
                               "difference": None if predicted is None or actual is None else predicted - actual}
        blocks.append({"block": block, "valid": not reasons, "reasons": reasons, "points": points,
                       "order": [[e["Candidate"], e["Speedup"]] for e in sorted(profile["experiments"], key=lambda e: e["Order"])] if complete else None})
    summary_points = []
    for label in CANDIDATES:
        for speedup in SPEEDUPS:
            key = f"{label}-{speedup}"
            valid = [b for b in blocks if b["valid"] and b["points"][key]["difference"] is not None]
            differences = [abs(b["points"][key]["difference"]) for b in valid]
            median = statistics.median(differences) if differences else None
            point_passed = len(valid) == BLOCKS and median is not None and median <= MEDIAN_LIMIT and max(differences) <= BLOCK_LIMIT
            passed = passed and point_passed
            summary_points.append({
                "point": key, "validBlocks": len(valid), "medianAbsoluteDifference": median,
                "maxAbsoluteDifference": max(differences) if differences else None,
                "medianPredicted": statistics.median(b["points"][key]["predicted"] for b in valid) if valid else None,
                "medianActual": statistics.median(b["points"][key]["actual"] for b in valid) if valid else None,
                "passed": point_passed})
    summary = {"passed": passed, "inputsUnchanged": manifest["inputsUnchanged"], "medianLimit": MEDIAN_LIMIT, "blockLimit": BLOCK_LIMIT,
               "points": summary_points, "blocks": blocks,
               "supportedModel": "Linux x64 CPU-bound managed fork/join threads registered through CozNet, end-to-end progress point",
               "generalClrSafetyEstablished": False}
    (run / "summary.json").write_text(json.dumps(summary, indent=2))
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
