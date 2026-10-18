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


def load(run, name):
    path = run / name
    return json.loads(path.read_text()) if path.exists() else None


def main():
    run = pathlib.Path(sys.argv[1]).resolve()
    manifest = load(run, "manifest.json")
    configurations = []
    declared = {c["name"]: (c["selected"], c["peer"]) for c in manifest["configurations"]}
    known = {(name, block) for name in CONFIGURATIONS for block in range(1, BLOCKS + 1)}
    unknown = [r for r in manifest["runs"] if (r["configuration"], r["block"]) not in known]
    passed = (manifest["inputsUnchanged"] and manifest["blocks"] == BLOCKS and manifest["speedup"] == SPEEDUP
              and declared == CONFIGURATIONS and len(manifest["configurations"]) == len(CONFIGURATIONS) and not unknown)
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
            rounds = CONFIGURATIONS.get(name)
            if rounds is None:
                reasons.append("unknown configuration")
            else:
                args = expected_args(*rounds)
                reasons += [f"{r['arm']} has arguments {r.get('args')}" for r in records if r.get("args") != args.get(r["arm"])]
                for arm, data in arms.items():
                    if data is not None:
                        for field, value in expected_fields(arm, *rounds).items():
                            if data.get(field) != value:
                                reasons.append(f"{arm} records {field} {data.get(field)}")
            record = {"block": block, "valid": not reasons, "reasons": reasons}
            if not reasons:
                zero, speed, original, optimized = (arms[a] for a in ("profile-zero", "profile-speedup", "original", "optimized"))
                predicted = 1 - speed["effectiveNs"] / zero["effectiveNs"]
                actual = 1 - optimized["elapsedNs"] / original["elapsedNs"]
                record.update({
                    "predicted": predicted, "actual": actual, "difference": predicted - actual,
                    "overhead": zero["elapsedNs"] / original["elapsedNs"],
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
            "name": name, "selectedRounds": configuration["selected"], "peerRounds": configuration["peer"],
            "blocks": blocks, "validBlocks": len(valid), "medianAbsoluteDifference": median,
            "maxAbsoluteDifference": max(differences) if differences else None,
            "medianPredicted": statistics.median(b["predicted"] for b in valid) if valid else None,
            "medianActual": statistics.median(b["actual"] for b in valid) if valid else None,
            "medianOverhead": statistics.median(b["overhead"] for b in valid) if valid else None,
            "passed": configuration_passed,
        })
    summary = {"passed": passed, "inputsUnchanged": manifest["inputsUnchanged"], "medianLimit": MEDIAN_LIMIT,
               "blockLimit": BLOCK_LIMIT, "speedup": manifest["speedup"], "configurations": configurations,
               "supportedModel": "Linux x64 CPU-bound managed fork/join threads registered through CozNet",
               "generalClrSafetyEstablished": False}
    (run / "summary.json").write_text(json.dumps(summary, indent=2))
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
