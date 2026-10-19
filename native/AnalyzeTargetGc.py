"""Apply the fixed forced-GC window criteria to the forced-GC records of one transition run directory."""
import json
import pathlib
import re
import sys

SAMPLE, FAILURE = 0, 4
DEBT_EXCEEDED = 1
NANOSECONDS = 1_000_000_000


def overlap(window, begin, end):
    return max(0, min(window[1], end) - max(window[0], begin))


def windows_complete(record):
    windows = record.get("gcWindows")
    threads = record.get("threads") or []
    starts = [t.get("workStartedNs") for t in threads]
    if (record.get("stopwatchFrequency") != NANOSECONDS or record.get("gcWindowsDropped") != 0 or not isinstance(windows, list)
            or len(windows) != record.get("forcedCollections") or not starts or not all(type(v) is int for v in starts)):
        return False
    if not all(isinstance(w, list) and len(w) == 2 and all(type(v) is int for v in w) and w[0] <= w[1] for w in windows):
        return False
    return (all(b[0] >= a[1] for a, b in zip(windows, windows[1:]))
            and all(w[0] >= min(starts) for w in windows))


def gc_diagnostics(record):
    complete = windows_complete(record)
    windows = record["gcWindows"] if complete else []
    threads = record.get("threads") or []
    events = sorted(record.get("events") or [], key=lambda e: e[9])
    failures = []
    for failure in (e for e in events if e[2] == FAILURE and e[8] == DEBT_EXCEEDED):
        samples = [e[9] for e in events if e[0] == failure[0] and e[2] == SAMPLE and e[9] <= failure[9]]
        native = threads[failure[0]].get("startedNs") if failure[0] < len(threads) else None
        begin = samples[-2] if len(samples) >= 2 else (native if type(native) is int and native <= failure[9] else failure[9])
        hits = [w for w in windows if overlap(w, begin, failure[9]) > 0]
        failures.append({"worker": failure[0], "timeNs": failure[9], "intervalNs": failure[9] - begin, "windows": hits,
                         "overlapNs": sum(overlap(w, begin, failure[9]) for w in hits)})
    gaps = []
    for worker in sorted({e[0] for e in events}):
        own = [e[9] for e in events if e[0] == worker]
        if len(own) < 2:
            continue
        begin, end = max(zip(own, own[1:]), key=lambda pair: pair[1] - pair[0])
        gaps.append({"worker": worker, "beginNs": begin, "gapNs": end - begin, "overlapNs": sum(overlap(w, begin, end) for w in windows)})
    return {"complete": complete, "windows": len(windows), "windowNs": sum(w[1] - w[0] for w in windows),
            "pauseNs": record.get("gcPauseNs"), "debtFailures": failures, "largestGaps": gaps}


def main():
    run = pathlib.Path(sys.argv[1]).resolve()
    records = sorted((p for p in run.glob("gc-*.json") if re.fullmatch(r"gc-[0-9]+", p.stem)), key=lambda p: int(p.stem.split("-")[1]))
    results = []
    for path in records:
        result = gc_diagnostics(json.loads(path.read_text()))
        result["run"] = int(path.stem.split("-")[1])
        results.append(result)
    complete = len(results) == 10 and all(r["complete"] for r in results)
    (run / "gc-summary.json").write_text(json.dumps({"gcWindowsComplete": complete, "runs": results}, indent=2))
    return 0 if complete else 1


if __name__ == "__main__":
    sys.exit(main())
