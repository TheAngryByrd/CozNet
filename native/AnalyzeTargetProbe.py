"""Describe the probe diagnostic of one paired run and compare its items with retained runs."""
import json
import pathlib
import statistics
import sys

LIMITS = [
    "The probe has no historical baseline. The retained runs have no probe.",
    "A probe runs only before its arm, so it supports only a temporal comparison with the next arm.",
    "Guest counters cannot prove that the host had no contention.",
    "The probe can change the thermal and idle state of the CPUs, so the capture is not an exact repeat of the earlier runs.",
    "No slow arm, no probe variation, or too few comparable items means that the diagnostic cannot resolve the cause.",
]


def load(path):
    try:
        return json.loads(pathlib.Path(path).read_text())
    except (OSError, ValueError):
        return None


def parse_snapshot(text):
    if text is None:
        return None
    sections, current = {"head": []}, "head"
    for line in text.splitlines():
        if line.startswith("== "):
            current = line[3:].strip()
            sections[current] = []
        else:
            sections[current].append(line)
    head = {k: v for k, v in (line.split(" ", 1) for line in sections["head"] if " " in line)}
    utc = int(head["utc"]) if head.get("utc", "").isdigit() else None
    uptime = head.get("uptime", "unsupported")
    boottime = float(uptime.split()[0]) if uptime != "unsupported" and uptime.split() else None
    cpu = next((line.split() for line in sections.get("stat", []) if line.startswith("cpu ")), None)
    ticks = [int(v) for v in cpu[1:]] if cpu else None
    pressure = None
    for line in sections.get("pressure", []):
        if line.startswith("some "):
            fields = dict(item.split("=") for item in line.split()[1:])
            pressure = int(fields["total"]) if "total" in fields else None
    return {"utcNs": utc, "boottimeSeconds": boottime, "totalTicks": sum(ticks[:8]) if ticks and len(ticks) >= 8 else None,
            "stealTicks": ticks[7] if ticks and len(ticks) > 7 else None, "pressureSomeTotalUs": pressure}


def delta(before, after, key):
    if before is None or after is None or before.get(key) is None or after.get(key) is None:
        return "missing"
    return after[key] - before[key]


def probe_values(record):
    if not isinstance(record, dict) or not isinstance(record.get("records"), list) or not record["records"]:
        return {"valid": False, "reason": "no probe record"}
    keys = ("wallNs", "cpuNs", "cpuBefore", "cpuAfter")
    if not all(isinstance(r, dict) and all(type(r.get(k)) is int for k in keys) for r in record["records"]):
        return {"valid": False, "reason": "probe record has the wrong shape"}
    walls = [r["wallNs"] for r in record["records"]]
    cpus = [r["cpuNs"] for r in record["records"] if r["cpuNs"] >= 0]
    valid = record.get("differingResults") == 0 and len(cpus) == len(walls) and record.get("passed") is True
    return {"valid": valid, "reason": None if valid else "checksum or CPU clock failed", "samples": len(walls),
            "medianWallNs": statistics.median(walls), "medianCpuNs": statistics.median(cpus) if cpus else None,
            "cpuOverWall": sum(cpus) / sum(walls) if cpus and sum(walls) else None,
            "cpusSeen": sorted({r["cpuBefore"] for r in record["records"]} | {r["cpuAfter"] for r in record["records"]}),
            "started": record.get("started"), "finished": record.get("finished")}


def rank(values):
    order = sorted(range(len(values)), key=lambda i: values[i])
    ranks = [0.0] * len(values)
    i = 0
    while i < len(order):
        j = i
        while j + 1 < len(order) and values[order[j + 1]] == values[order[i]]:
            j += 1
        for k in range(i, j + 1):
            ranks[order[k]] = (i + j) / 2 + 1
        i = j + 1
    return ranks


def spearman(xs, ys):
    pairs = [(x, y) for x, y in zip(xs, ys) if x is not None and y is not None]
    if len(pairs) < 3:
        return {"n": len(pairs), "rho": None}
    rx, ry = rank([p[0] for p in pairs]), rank([p[1] for p in pairs])
    mx, my = statistics.mean(rx), statistics.mean(ry)
    sx = sum((a - mx) ** 2 for a in rx) ** 0.5
    sy = sum((b - my) ** 2 for b in ry) ** 0.5
    rho = None if sx == 0 or sy == 0 else sum((a - mx) * (b - my) for a, b in zip(rx, ry)) / (sx * sy)
    return {"n": len(pairs), "rho": rho}


def experiment_item(e):
    cpu = sum(t["CpuNs"] for t in e.get("Threads") or []) / e["Visits"] if e.get("Threads") else None
    return {"visits": e["Visits"], "elapsedNs": e["ElapsedNs"], "rate": e["Visits"] / e["ElapsedNs"] * 1e9, "cpuPerTransactionNs": cpu}


def plain_item(record):
    return {"visits": record["visits"], "elapsedNs": record["elapsedNs"], "rate": record["visits"] / record["elapsedNs"] * 1e9, "cpuPerTransactionNs": None}


def items(run, entry):
    record = load(run / entry["file"])
    key = (entry["block"], entry["peer"], entry["arm"])
    out = {}
    if isinstance(record, dict) and "experiments" in record:
        for e in record["experiments"] + ([record["warmup"]] if isinstance(record.get("warmup"), dict) else []):
            if e.get("Visits") and e.get("ElapsedNs"):
                out[key + ((e["Speedup"], e["Order"]) if e.get("Order", -1) >= 0 else ("warmup",))] = experiment_item(e)
        return out
    if isinstance(record, dict) and record.get("visits") and record.get("elapsedNs"):
        out[key] = plain_item(record)
    warmup = load(run / f"{entry['file']}.warmup")
    if isinstance(warmup, dict) and warmup.get("visits") and warmup.get("elapsedNs"):
        out[key + ("warmup",)] = plain_item(warmup)
    return out


def run_items(run):
    manifest = load(run / "manifest.json")
    return {k: v for entry in manifest["runs"] for k, v in items(run, entry).items()} if manifest else {}


def main():
    run = pathlib.Path(sys.argv[1]).resolve()
    others = [pathlib.Path(p).resolve() for p in sys.argv[2:]]
    manifest = load(run / "manifest.json")
    retained = {p.name: run_items(p) for p in others}
    arms, probe_walls = [], []
    for index, entry in enumerate(manifest["runs"]):
        f = entry["file"]
        probe = probe_values(load(run / f"{f}.probe.json"))
        snaps = {name: parse_snapshot((run / f"{f}.{name}.txt").read_text() if (run / f"{f}.{name}.txt").exists() else None)
                 for name in ("probe-before", "probe-after", "arm-before", "arm-after")}
        here = items(run, entry)
        compared = {}
        for name, other in retained.items():
            compared[name] = {"|".join(map(str, k)): {"rateRatio": v["rate"] / other[k]["rate"] if k in other else None,
                                                      "cpuRatio": v["cpuPerTransactionNs"] / other[k]["cpuPerTransactionNs"]
                                                      if k in other and v["cpuPerTransactionNs"] and other[k]["cpuPerTransactionNs"] else None}
                              for k, v in here.items()}
        arms.append({"index": index, "block": entry["block"], "peer": entry["peer"], "arm": entry["arm"], "exitCode": entry["exitCode"],
                     "probeExitCode": entry.get("probeExitCode"), "probe": probe,
                     "brackets": {k: (v or {}).get("utcNs") for k, v in snaps.items()},
                     "boottime": {k: (v or {}).get("boottimeSeconds") for k, v in snaps.items()},
                     "armDeltas": {k: delta(snaps["arm-before"], snaps["arm-after"], k) for k in ("totalTicks", "stealTicks", "pressureSomeTotalUs")},
                     "items": {"|".join(map(str, k)): v for k, v in here.items()}, "comparedTo": compared})
        probe_walls.append(probe["medianWallNs"] if probe["valid"] else None)
    walls = [w for w in probe_walls if w is not None]
    spread = {"validProbes": len(walls), "probes": len(probe_walls),
              **({"minNs": min(walls), "medianNs": statistics.median(walls), "maxNs": max(walls),
                  "coefficientOfVariation": statistics.pstdev(walls) / statistics.mean(walls)} if walls else {})}
    association = {}
    for name in retained:
        for kind in ("plain", "profile"):
            xs, ys = [], []
            for arm, wall in zip(arms, probe_walls):
                if (arm["arm"] == "profile") != (kind == "profile"):
                    continue
                ratios = [v["rateRatio"] for k, v in arm["comparedTo"][name].items() if v["rateRatio"] is not None and not k.endswith("|warmup")]
                xs.append(wall)
                ys.append(statistics.median(ratios) if ratios else None)
            association[f"{name}|{kind}"] = {**spearman(xs, ys), "unit": "one arm and its own probe for each pair; n counts the pairs with both values"}
    summary = {"diagnostic": True, "stopped": manifest.get("stopped"), "probeSpread": spread, "probeWallVersusRateRatio": association, "arms": arms, "limits": LIMITS}
    (run / "probe-summary.json").write_text(json.dumps(summary, indent=2))
    print(json.dumps({"stopped": manifest.get("stopped"), "probeSpread": spread, "probeWallVersusRateRatio": association}))
    return 1 if manifest.get("stopped") else 0


if __name__ == "__main__":
    sys.exit(main())
