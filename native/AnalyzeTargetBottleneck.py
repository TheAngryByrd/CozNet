"""Apply the fixed criteria of the paired peer-work diagnostic to one run directory."""
import json
import pathlib
import statistics
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

from AnalyzeTargetProfile import (SELECTED_NAME, actual_gain, latency_counts_complete, load, period_matches, plain_warmups_passed, prediction,
                                  queue_stats_complete, timing_consistent, warmup_present)

BLOCKS = 5
SEED = 20261018
SELECTED = 40
PEERS = (8, 2)
SPEEDUPS = (25, 50, 75)
MIN_VISITS = 100
CONGESTED = -8.0
CLOSE = 5.0
READY_LIMIT = 0.10


def expected_args(seed, block, peer):
    args = {"profile": f"transaction-selected-profile {peer} {seed} {(block - 1) % 4}", "original": f"plain-transaction {SELECTED} {peer}"}
    for speedup in SPEEDUPS:
        args[f"selected-{speedup}"] = f"plain-transaction {SELECTED * (100 - speedup) // 100} {peer}"
    return args


DURATION_MS = 120


def plain_matches(record, selected, peer):
    return (isinstance(record, dict) and record.get("mode") == "plain-transaction" and record.get("selectedRounds") == selected
            and record.get("peerRounds") == peer and record.get("durationMs") == DURATION_MS)


def profile_complete(profile, peer, block):
    experiments = profile.get("experiments") if isinstance(profile, dict) else None
    if not isinstance(experiments, list) or len(experiments) != 4:
        return False
    if sorted(e.get("Speedup") for e in experiments) != [0, *SPEEDUPS] or any(e.get("Candidate") != SELECTED_NAME for e in experiments):
        return False
    return (profile.get("mode") == "transaction-selected-profile" and profile.get("peerRounds") == peer and profile.get("selectedRounds") == SELECTED
            and profile.get("durationMs") == DURATION_MS and period_matches(profile, None) and profile.get("seed") == SEED and profile.get("rotation") == (block - 1) % 4 and sorted(e.get("Order") for e in experiments) == [0, 1, 2, 3])


def block_reasons(run, block, peer, records):
    args = expected_args(SEED, block, peer)
    names = {arm: f"p{peer}-{arm}" for arm in args}
    data = {arm: load(run / f"{block}-{names[arm]}.json") for arm in args}
    mine = [r for r in records if r.get("block") == block and r.get("peer") == peer]
    reasons = [f"{arm} missing" for arm, value in data.items() if value is None]
    if sorted(r.get("arm") for r in mine) != sorted(args):
        reasons.append("the manifest does not hold exactly one record for each arm")
    reasons += [f"{r['arm']} exit {r.get('exitCode')}" for r in mine if r.get("exitCode") != 0]
    reasons += [f"{r['arm']} has arguments {r.get('args')}" for r in mine if r.get("args") != args.get(r.get("arm"))]
    reasons += [f"{arm} not passed" for arm, value in data.items() if value is not None and value.get("passed") is not True]
    plain = {"original": SELECTED, **{f"selected-{s}": SELECTED * (100 - s) // 100 for s in SPEEDUPS}}
    reasons += [f"{arm} does not hold its mode, rounds, and duration" for arm, rounds in plain.items()
                if data[arm] is not None and not plain_matches(data[arm], rounds, peer)]
    if not plain_warmups_passed(run, block, [name for arm, name in names.items() if arm != "profile"]):
        reasons.append("a plain arm has no passed warm-up record")
    profile = data["profile"]
    if profile is None or not profile_complete(profile, peer, block):
        reasons.append("the profile record does not hold the fixed experiment design")
    elif not warmup_present(profile) or profile["warmup"].get("Passed") is not True:
        reasons.append("the profile record has no passed warm-up experiment")
    elif not timing_consistent(profile) or not queue_stats_complete(profile, data) or not latency_counts_complete(profile, data):
        reasons.append("the profile or a plain record is incomplete")
    elif any(e.get("Visits", 0) < MIN_VISITS for e in profile["experiments"]) or any(v.get("visits", 0) < MIN_VISITS for k, v in data.items() if k != "profile"):
        reasons.append("an arm has fewer than 100 visits")
    elif not diagnostics_complete(profile, data):
        reasons.append("a record does not hold its idle counts")
    return reasons, data


def diagnostics_complete(profile, data):
    stats = profile.get("queueStats") or []
    return (all(isinstance(s, dict) and isinstance(s.get("handoffIdle"), dict) for s in stats[1:])
            and all(isinstance(v.get("handoffIdle"), dict) for k, v in data.items() if k != "profile"))


def ready_share(stats):
    idle = stats.get("handoffIdle") if isinstance(stats, dict) else None
    if not isinstance(idle, dict):
        return None
    total = sum(sum(row) for row in idle.values())
    return sum(idle["ready"]) / total if total else None


def block_values(data):
    profile, original = data["profile"], data["original"]
    experiments = profile["experiments"]
    order = sorted(experiments, key=lambda e: e["Order"])
    stats = {e["Speedup"]: profile["queueStats"][i + 1] for i, e in enumerate(order)}
    by = {e["Speedup"]: e for e in experiments}
    upstream = lambda e: e["LatencyNs"] / e["Visits"] * e["EffectiveNs"] / e["ElapsedNs"]
    values = {}
    for speedup in SPEEDUPS:
        actual = actual_gain(original, data[f"selected-{speedup}"], "transaction")
        predicted = prediction(profile, SELECTED_NAME, speedup)
        e = by[speedup]
        values[speedup] = {
            "difference": None if actual is None or predicted is None else 100 * (predicted - actual),
            "upstreamDifference": None if actual is None else 100 * ((1 - upstream(e) / upstream(by[0])) - actual),
            "ready": ready_share(stats[speedup]),
            "windowRate": (e["LatencyNs"] - e["VirtualLatencyNs"]) / e["LatencyNs"] if e["NegativeTransactions"] == 0 else None,
            "delayRate": e["GlobalNs"] / e["ElapsedNs"]}
    plain = data["selected-75"].get("handoffIdle")
    values["plainIdle"] = {k: sum(v) for k, v in plain.items()} if isinstance(plain, dict) else None
    return values


def median(values):
    values = [v for v in values if v is not None]
    return statistics.median(values) if values else None


def outcome(s8, s2, r2, valid8, valid2):
    if valid8 < 4 or valid2 < 4 or None in (s8, s2, r2):
        return "not decisive: too few valid blocks"
    if s8 > CONGESTED:
        return "not decisive: the earlier error did not repeat"
    if r2 > READY_LIMIT:
        return "not decisive: the ready share of peer 2 stays above 10%"
    if -CLOSE <= s2 <= CLOSE:
        return "congestion supported"
    if s2 <= CONGESTED:
        return "estimator or boundary bias supported"
    return "not decisive"


def paired_outcome(blocks8, blocks2):
    common = [b8["block"] for b8, b2 in zip(blocks8, blocks2) if b8["valid"] and b2["valid"]]
    paired = {peer: {s: {k: median(b["values"][s][k] for b in blocks if b["block"] in common) for k in ("difference", "upstreamDifference", "ready")}
                     for s in SPEEDUPS} for peer, blocks in ((8, blocks8), (2, blocks2))}
    return common, paired, outcome(paired[8][75]["difference"], paired[2][75]["difference"], paired[2][75]["ready"], len(common), len(common))


def main():
    run = pathlib.Path(sys.argv[1]).resolve()
    manifest = load(run / "manifest.json")
    complete = (manifest["inputsUnchanged"] and manifest["blocks"] == BLOCKS and manifest["seed"] == SEED and manifest["selected"] == SELECTED
                and tuple(manifest["peers"]) == PEERS and tuple(manifest["speedups"]) == SPEEDUPS)
    peers = {}
    for peer in PEERS:
        blocks = []
        for block in range(1, BLOCKS + 1):
            reasons, data = block_reasons(run, block, peer, manifest["runs"])
            blocks.append({"block": block, "valid": not reasons, "reasons": reasons, "values": block_values(data) if not reasons else None})
        valid = [b["values"] for b in blocks if b["valid"]]
        points = {s: {k: median(v[s][k] for v in valid) for k in ("difference", "upstreamDifference", "ready", "windowRate", "delayRate")} for s in SPEEDUPS}
        idle = {}
        for v in valid:
            for k, n in (v["plainIdle"] or {}).items():
                idle[k] = idle.get(k, 0) + n
        peers[peer] = {"validBlocks": len(valid), "blocks": blocks, "points": points, "plainSelected75Idle": idle}
    common, paired, result = paired_outcome(peers[8]["blocks"], peers[2]["blocks"])
    summary = {"inputsUnchanged": manifest["inputsUnchanged"], "complete": complete, "outcome": result if complete else "not decisive: run incomplete",
               "commonValidBlocks": common, "paired": {str(k): v for k, v in paired.items()},
               "peers": {str(k): v for k, v in peers.items()}, "diagnostic": True, "qualification": False}
    (run / "summary.json").write_text(json.dumps(summary, indent=2))
    print(json.dumps({"outcome": summary["outcome"], "commonValidBlocks": common, "paired": paired}))
    return 0 if complete else 1


if __name__ == "__main__":
    sys.exit(main())
