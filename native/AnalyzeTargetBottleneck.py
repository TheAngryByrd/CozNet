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
EXPORT_LIMIT = 4096


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


def integers(value, length=None):
    return isinstance(value, list) and (length is None or len(value) == length) and all(type(v) is int for v in value)


def evidence_reasons(profile, evidence):
    if not isinstance(evidence, dict) or not isinstance(evidence.get("experiments"), list):
        return ["no evidence record"]
    if not all(isinstance(e, dict) and type(e.get("block")) is int and type(e.get("order")) is int for e in evidence["experiments"]):
        return ["evidence has an entry without an integer block and order"]
    keys = [(e["block"], e["order"]) for e in evidence["experiments"]]
    expected = [(e.get("Block"), e.get("Order")) for e in [profile["warmup"]] + profile["experiments"]]
    if sorted(keys) != sorted(expected):
        return ["evidence does not hold exactly one entry for each experiment"]
    by = dict(zip(keys, evidence["experiments"]))
    reasons = []
    for e in [profile["warmup"]] + profile["experiments"]:
        ev = by[(e.get("Block"), e.get("Order"))]
        name = f"order {e.get('Order')}"
        keys = ("transactions", "signedTotalNs", "clippedNs", "clippedTotalNs", "negativeTransactions", "exported", "omitted", "exportLimit")
        windows = ev.get("windows")
        if (not all(type(ev.get(k)) is int for k in keys) or not integers(ev.get("stepHistogram"), 4) or min(ev["stepHistogram"]) < 0
                or not isinstance(windows, list) or not all(integers(w, 6) for w in windows)):
            reasons.append(f"{name} evidence has the wrong shape")
            continue
        if not (ev["transactions"] == e.get("Visits") and ev["clippedTotalNs"] == e.get("VirtualLatencyNs")
                and ev["negativeTransactions"] == e.get("NegativeTransactions")):
            reasons.append(f"{name} evidence differs from the profile record")
        if ev["clippedTotalNs"] != ev["signedTotalNs"] + ev["clippedNs"]:
            reasons.append(f"{name} clipped total differs from signed total plus clipped amount")
        if sum(ev["stepHistogram"]) != ev["transactions"] or ev["exported"] + ev["omitted"] != ev["transactions"]:
            reasons.append(f"{name} counts do not add up")
        if len(windows) != ev["exported"] or ev["exportLimit"] != EXPORT_LIMIT or ev["exported"] != min(EXPORT_LIMIT, ev["transactions"]):
            reasons.append(f"{name} export does not match its count")
        if any(w[4] != (w[1] - w[0]) - (w[3] - w[2]) or w[5] < 0 or w[1] < w[0] or w[3] < w[2] or (w[1] == w[0] and (w[5] != 0 or w[3] != w[2]))
               for w in windows):
            reasons.append(f"{name} a window does not hold its own arithmetic")
        if ev["omitted"] == 0:
            negative = [w[4] for w in windows if w[4] < 0]
            histogram = [sum(1 for w in windows if min(w[5], 3) == i) for i in range(4)]
            if (sum(w[4] for w in windows) != ev["signedTotalNs"] or len(negative) != ev["negativeTransactions"]
                    or -sum(negative) != ev["clippedNs"] or histogram != ev["stepHistogram"]):
                reasons.append(f"{name} windows do not add up to the totals")
        if e.get("Speedup") == 0 and ev["clippedNs"] != 0:
            reasons.append(f"{name} baseline clips delay")
    return reasons


def evidence_values(profile, data, evidence, valid):
    by = {(e["block"], e["order"]): e for e in evidence["experiments"]}
    experiments = {e["Speedup"]: e for e in profile["experiments"]}
    base = experiments[0]
    base_latency = base["VirtualLatencyNs"] / base["Visits"] if base["Visits"] > 0 and base["VirtualLatencyNs"] > 0 else None
    values = {}
    for speedup in SPEEDUPS:
        e = experiments[speedup]
        ev = by[(e["Block"], e["Order"])]
        actual = actual_gain(data["original"], data[f"selected-{speedup}"], "transaction") if valid else None
        signed = ev["signedTotalNs"]
        visits = e["Visits"]
        unclipped = None if signed <= 0 or actual is None or base_latency is None or visits <= 0 else 100 * ((1 - (signed / visits) / base_latency) - actual)
        values[speedup] = {"unclippedDifference": unclipped, "signedTotalNs": signed, "clippedPerTransactionNs": ev["clippedNs"] / visits if visits > 0 else None,
                           "negativeTransactions": ev["negativeTransactions"], "estimatorAssumptionsValid": ev["negativeTransactions"] == 0,
                           "stepHistogram": ev["stepHistogram"], "windowsWithTwoOrMoreSteps": sum(ev["stepHistogram"][2:]),
                           "collections": e.get("Collections"), "gcPauseNs": e.get("GcPauseNs")}
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


def unclipped_summary(blocks8, blocks2, common):
    pairs = {b8["block"]: (b8, b2) for b8, b2 in zip(blocks8, blocks2)}
    subset = [n for n in common if pairs[n][0].get("evidenceValid") and pairs[n][1].get("evidenceValid")]
    result = {"blocks": subset}
    for peer, blocks in ((8, blocks8), (2, blocks2)):
        chosen = [b for b in blocks if b["block"] in subset]
        profiles = [b for b in blocks if b.get("evidenceValid")]
        result[str(peer)] = {
            "evidenceInvalid": {b["block"]: b.get("evidenceReasons") for b in blocks if not b.get("evidenceValid")},
            **{str(s): {"medianDifference": median(b["values"][s]["difference"] for b in chosen),
                        "medianUnclippedDifference": median(b["evidence"][s]["unclippedDifference"] for b in chosen),
                        "undefinedUnclipped": sum(1 for b in chosen if b["evidence"][s]["unclippedDifference"] is None),
                        "allProfiles": [b["block"] for b in profiles],
                        "medianClippedPerTransactionNs": median(b["evidence"][s]["clippedPerTransactionNs"] for b in profiles),
                        "estimatorAssumptionsValidProfiles": sum(1 for b in profiles if b["evidence"][s]["estimatorAssumptionsValid"]),
                        "windowsWithTwoOrMoreSteps": sum(b["evidence"][s]["windowsWithTwoOrMoreSteps"] for b in profiles),
                        "collections": [b["evidence"][s]["collections"] for b in profiles],
                        "stepHistogram": [sum(b["evidence"][s]["stepHistogram"][i] for b in profiles) for i in range(4)]} for s in SPEEDUPS}}
    return result


def main():
    run = pathlib.Path(sys.argv[1]).resolve()
    manifest = load(run / "manifest.json")
    complete = (manifest["inputsUnchanged"] and manifest["blocks"] == BLOCKS and manifest["seed"] == SEED and manifest["selected"] == SELECTED
                and tuple(manifest["peers"]) == PEERS and tuple(manifest["speedups"]) == SPEEDUPS and not manifest.get("stopped"))
    peers = {}
    for peer in PEERS:
        blocks = []
        for block in range(1, BLOCKS + 1):
            reasons, data = block_reasons(run, block, peer, manifest["runs"])
            entry = {"block": block, "valid": not reasons, "reasons": reasons, "values": block_values(data) if not reasons else None}
            evidence = load(run / f"{block}-p{peer}-profile.json.evidence.json")
            profile = data["profile"]
            if profile is not None and profile_complete(profile, peer, block) and warmup_present(profile):
                entry["evidenceReasons"] = evidence_reasons(profile, evidence)
                entry["evidenceValid"] = not entry["evidenceReasons"]
                entry["evidence"] = evidence_values(profile, data, evidence, not reasons) if entry["evidenceValid"] else None
            else:
                entry["evidenceReasons"], entry["evidenceValid"], entry["evidence"] = ["the profile record is incomplete"], False, None
            blocks.append(entry)
        valid = [b["values"] for b in blocks if b["valid"]]
        points = {s: {k: median(v[s][k] for v in valid) for k in ("difference", "upstreamDifference", "ready", "windowRate", "delayRate")} for s in SPEEDUPS}
        idle = {}
        for v in valid:
            for k, n in (v["plainIdle"] or {}).items():
                idle[k] = idle.get(k, 0) + n
        peers[peer] = {"validBlocks": len(valid), "blocks": blocks, "points": points, "plainSelected75Idle": idle}
    common, paired, result = paired_outcome(peers[8]["blocks"], peers[2]["blocks"])
    unclipped = unclipped_summary(peers[8]["blocks"], peers[2]["blocks"], common)
    summary = {"inputsUnchanged": manifest["inputsUnchanged"], "complete": complete, "stopped": manifest.get("stopped"), "outcome": result if complete else "not decisive: run incomplete",
               "commonValidBlocks": common, "paired": {str(k): v for k, v in paired.items()}, "unclipped": unclipped,
               "peers": {str(k): v for k, v in peers.items()}, "diagnostic": True, "qualification": False}
    (run / "summary.json").write_text(json.dumps(summary, indent=2))
    print(json.dumps({"outcome": summary["outcome"], "commonValidBlocks": common, "paired": paired, "unclipped": unclipped}))
    return 0 if complete else 1


if __name__ == "__main__":
    sys.exit(main())
