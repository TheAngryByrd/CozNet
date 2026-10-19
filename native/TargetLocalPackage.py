"""Decide the verdict of one target-local qualification package."""
import json
import pathlib
import sys

SUITES = ("prediction", "transitions", "barrier", "latency", "throughput", "pipeline", "lock", "spinlock", "semaphore", "transaction", "spintransaction")


def suite_reasons(suite, start):
    reasons = []
    if suite.get("exitCode") != 0:
        reasons.append(f"exit code {suite.get('exitCode')}")
    if suite.get("readError"):
        reasons.append(f"read error: {suite['readError']}")
    if len(suite.get("runs") or []) != 1:
        reasons.append(f"{len(suite.get('runs') or [])} new run folders")
    summary = suite.get("summary")
    if not isinstance(summary, dict):
        return reasons + ["no summary"]
    if summary.get("passed") is not True:
        reasons.append("not passed")
    if summary.get("inputsUnchanged") is not True:
        reasons.append("inputs changed")
    sources = suite.get("sources")
    if not isinstance(sources, dict) or not sources:
        reasons.append("no source hashes")
    else:
        reasons += [f"{path} has no start hash" if path not in start else f"{path} differs from the start"
                    for path, value in sorted(sources.items()) if path not in start or start[path] != value]
    return reasons


def verdict(package):
    start = package.get("startHashes") or {}
    suites = package.get("suites") or []
    results = [{"suite": s.get("suite"), "reasons": suite_reasons(s, start)} for s in suites]
    complete = [s.get("suite") for s in suites] == list(SUITES)
    changed = sorted(package.get("changedDuringPackage") or [])
    return {"commit": package.get("commit"), "treeClean": not package.get("status"), "suitesComplete": complete, "changedDuringPackage": changed,
            "suites": results, "passed": complete and not changed and all(not r["reasons"] for r in results)}


def main():
    run = pathlib.Path(sys.argv[1]).resolve()
    result = verdict(json.loads((run / "package.json").read_text()))
    (run / "package-summary.json").write_text(json.dumps(result, indent=2))
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
