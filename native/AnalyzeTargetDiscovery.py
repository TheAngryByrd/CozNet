import hashlib
import json
import pathlib
import sys

FIXTURES = {"CozNet.TargetLocal.Work::selected(uint64)": "selected", "CozNet.TargetLocal.Work::peer(uint64)": "peer"}
CLASS_KEYS = ("unmapped", "notMethodLabel", "notOptimized", "notUnique", "outOfScope", "inScope")
COUNT_KEYS = ("samples",) + CLASS_KEYS + ("omittedMethods", "omittedSamples")
ELIGIBILITY = ("InEligibleBinding", "OutsideEligibleBinding")
WORK = "samples/CozNet.TargetLocal/Work.fs"


def source_hash(text):
    return hashlib.sha256(text.replace("\r\n", "\n").encode("utf-8")).hexdigest().upper()


def body_lines(work_source):
    """The first body line of each fixture function: the line after its `let <name> (rounds: uint64) =` line."""
    lines = work_source.splitlines()
    found = {}
    for symbol, name in FIXTURES.items():
        header = f"let {name} (rounds: uint64) ="
        index = next((i for i, line in enumerate(lines) if line.strip() == header), None)
        found[symbol] = None if index is None else index + 2
    return found


def natural(value):
    return type(value) is int and value >= 0


def parse_discovery(record):
    """Return (discovery, reason). The discovery has checked counts, ranked methods, anchors, and bound ranges, or the reason names the first bad shape."""
    discovery = record.get("discovery")
    if not isinstance(discovery, dict):
        return None, "no discovery record"
    counts = discovery.get("counts")
    if not isinstance(counts, dict) or not all(natural(counts.get(k)) for k in COUNT_KEYS):
        return None, "the class counts are missing or negative"
    ranked = discovery.get("ranked")
    if not isinstance(ranked, list) or not all(isinstance(m, dict) and isinstance(m.get("symbol"), str) and m.get("eligibility") in ELIGIBILITY
                                               and all(natural(m.get(k)) for k in ("start", "end", "samples")) for m in ranked):
        return None, "a ranked method has the wrong shape"
    anchors = discovery.get("anchors")
    if not isinstance(anchors, list) or not all(isinstance(a, dict) and isinstance(a.get("symbol"), str) for a in anchors):
        return None, "an anchor has the wrong shape"
    bound = discovery.get("boundRanges")
    if not isinstance(bound, list) or not all(isinstance(r, list) and len(r) == 2 and all(natural(v) for v in r) for r in bound):
        return None, "a bound range has the wrong shape"
    if not natural(discovery.get("sampleEvents")):
        return None, "the sample event count is missing"
    if not all(isinstance(discovery.get(k), list) and all(isinstance(v, str) for v in discovery[k]) for k in ("outsideBinding", "designEligible", "designCandidates")):
        return None, "the eligibility or design lists have the wrong shape"
    return discovery, None


def run_reasons(manifest, record, record_exit):
    reasons = []
    if not manifest.get("inputsUnchanged"):
        reasons.append("the inputs changed during the run")
    tests = manifest.get("linuxTests") or {}
    if tests.get("exitCode") != 0 or not isinstance(tests.get("executed"), int) or tests["executed"] < 1 or tests.get("failed") != 0:
        reasons.append("the Linux discovery tests did not pass with an executed count above 0")
    if record_exit != 0:
        reasons.append(f"the capture exited with {record_exit}")
    if not isinstance(record, dict):
        reasons.append("no capture record")
        return reasons
    if record.get("supported") is not True:
        reasons.append("the platform is not supported")
    if record.get("configurationErrors"):
        reasons.append("the configuration has errors")
    if record.get("bindError"):
        reasons.append("the eligible methods did not bind: " + str(record["bindError"]))
    if record.get("mapError"):
        reasons.append("the perf map was not captured: " + str(record["mapError"]))
    if record.get("catalogError"):
        reasons.append("the method catalog failed: " + str(record["catalogError"]))
    if record.get("passed") is not True:
        reasons.append("the run did not pass")
    discovery = record.get("discovery")
    if isinstance(discovery, dict) and discovery.get("rankError"):
        reasons.append("rank gave an error: " + str(discovery["rankError"]))
    return reasons


def map_reasons(record, map_bytes):
    reasons = []
    pid = record.get("pid")
    if type(pid) is not int or record.get("mapPath") != f"/tmp/perf-{pid}.map":
        reasons.append("the map path does not name the process id")
    if map_bytes is None:
        reasons.append("no retained perf map")
    elif hashlib.sha256(map_bytes).hexdigest().upper() != str(record.get("mapSha256", "")).upper():
        reasons.append("the retained perf map differs from the recorded hash")
    return reasons


def discovery_reasons(record, discovery, work_source):
    counts, ranked = discovery["counts"], discovery["ranked"]
    reasons = []
    if sum(counts[k] for k in CLASS_KEYS) != counts["samples"]:
        reasons.append("the classes do not add up to the sample count")
    if counts["inScope"] != sum(m["samples"] for m in ranked) + counts["omittedSamples"]:
        reasons.append("the in-scope count differs from the ranked plus omitted samples")
    if counts["samples"] != discovery["sampleEvents"]:
        reasons.append("the sample count differs from the sample events of the run")
    threads = record.get("threads")
    if not isinstance(threads, list) or not all(isinstance(t, dict) and natural(t.get("accepted")) for t in threads) \
            or counts["samples"] != sum(t["accepted"] for t in threads):
        reasons.append("the sample count differs from the samples that the threads accepted")
    symbols = [m["symbol"] for m in ranked]
    if len(set(symbols)) != len(symbols):
        reasons.append("a ranked symbol is not unique")
    for symbol in FIXTURES:
        method = next((m for m in ranked if m["symbol"] == symbol), None)
        if method is None or method["samples"] < 1:
            reasons.append(f"the fixture {symbol} is not ranked with samples")
        elif method["eligibility"] != "InEligibleBinding":
            reasons.append(f"the fixture {symbol} is not in the eligible binding")
    if discovery.get("bindingError") or discovery["boundRanges"] != [[m["start"], m["end"]] for m in ranked]:
        reasons.append("the ranked symbols do not bind to the ranked ranges")
    anchors = {a["symbol"]: a for a in discovery["anchors"]}
    for symbol, line in body_lines(work_source).items():
        anchor = anchors.get(symbol)
        if line is None:
            reasons.append(f"the source of {symbol} has no fixture header")
        elif not anchor or anchor.get("found") is not True or not str(anchor.get("document", "")).replace("\\", "/").endswith("/Work.fs") or anchor.get("line") != line:
            reasons.append(f"the anchor of {symbol} is not Work.fs line {line}")
    if sorted(discovery["outsideBinding"]) != sorted(m["symbol"] for m in ranked if m["eligibility"] == "OutsideEligibleBinding"):
        reasons.append("the outside-binding list differs from the ranked eligibility")
    if discovery.get("designValid") is not True:
        reasons.append("the design is not valid")
    elif record.get("eligible") != discovery["designEligible"]             or sorted(discovery["designCandidates"]) != sorted(m["symbol"] for m in ranked if m["eligibility"] == "InEligibleBinding"):
        reasons.append("the design does not use the eligible binding and the in-binding candidates")
    return reasons


def analyze(manifest, record, record_exit, map_bytes, work_source):
    reasons = run_reasons(manifest, record, record_exit)
    if (manifest.get("sources") or {}).get(WORK) != source_hash(work_source):
        reasons.append("the Work.fs used for the anchor lines differs from the hashed Work.fs")
    if isinstance(record, dict):
        reasons += map_reasons(record, map_bytes)
        discovery, shape = parse_discovery(record)
        if shape:
            if not (isinstance(record.get("discovery"), dict) and record["discovery"].get("rankError")):
                reasons.append(shape)
        else:
            reasons += discovery_reasons(record, discovery, work_source)
    return {"outcome": "passed" if not reasons else "failed", "reasons": reasons, "passed": not reasons,
            "treeClean": manifest.get("treeClean"), "commit": manifest.get("commit")}


def load(path):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def main():
    run = pathlib.Path(sys.argv[1])
    root = pathlib.Path(__file__).resolve().parent.parent
    manifest = load(run / "manifest.json") or {}
    record = load(run / "discovery.json")
    map_path = run / "discovery.json.perf-map"
    map_bytes = map_path.read_bytes() if map_path.exists() else None
    capture = manifest.get("capture") or {}
    summary = analyze(manifest, record, capture.get("exitCode"), map_bytes, (root / WORK).read_text(encoding="utf-8"))
    (run / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(json.dumps(summary, indent=2))
    return 0 if summary["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
