import argparse
import csv
import json
from pathlib import Path


def analyze(summary, rows):
    count = summary["count"]
    if type(count) is not int or count < 2 or len(rows) != count or summary["error"]:
        raise ValueError("The clock workload did not complete its requested readings.")
    if summary["capture"] not in ("baseline", "remote") or summary["activity"] not in ("busy", "blocked"):
        raise ValueError("Unknown clock workload mode.")
    remote = summary["capture"] == "remote"
    if summary["activity"] == "blocked" and (summary["initialState"] != "S" or summary["finalState"] != "S"):
        raise ValueError("The target was not observed sleeping at both measurement boundaries.")
    for key in ("elapsedNanoseconds", "controllerCpuNanoseconds", "targetSelfBeforeNanoseconds", "targetSelfAfterNanoseconds"):
        if type(summary[key]) is not int or summary[key] < 0:
            raise ValueError(f"Invalid summary clock: {key}.")
    if summary["targetSelfAfterNanoseconds"] < summary["targetSelfBeforeNanoseconds"]:
        raise ValueError("The target self CPU clock decreased.")
    anomalies = []
    spans, brackets = [], []
    previous = None
    for index, row in enumerate(rows):
        if any(type(value) is not int or value < 0 for value in row.values()) or row["status"] != 0:
            raise ValueError(f"Reading {index} failed or contains an invalid value.")
        if row["before"] == 0 or row["after"] < row["before"] or (previous and row["before"] < previous["after"]):
            raise ValueError(f"Reading {index} has invalid outer clock brackets.")
        spans.append(row["after"] - row["before"])
        if remote:
            if not row["before"] <= row["target_before"] <= row["target_after"] <= row["after"]:
                raise ValueError(f"Reading {index} has invalid native clock brackets.")
            if row["registration"] != 1 or row["resolution"] == 0:
                raise ValueError(f"Reading {index} has invalid registration metadata.")
            if not summary["targetSelfBeforeNanoseconds"] <= row["target_cpu"] <= summary["targetSelfAfterNanoseconds"]:
                raise ValueError(f"Reading {index} is outside the target's self CPU interval.")
            brackets.append(row["target_after"] - row["target_before"])
            if previous:
                delta = row["target_cpu"] - previous["target_cpu"]
                outer = row["target_after"] - previous["target_before"]
                if row["resolution"] != previous["resolution"]:
                    raise ValueError("The reported clock resolution changed.")
                if delta < 0 or delta > outer:
                    anomalies.append({"index": index, "cpuDeltaNanoseconds": delta,
                                      "outerWallNanoseconds": outer, "previous": previous, "current": row})
        elif any(row[key] != 0 for key in ("target_before", "target_cpu", "target_after", "resolution", "registration")):
            raise ValueError("A baseline reading contains a remote CPU observation.")
        previous = row
    target_delta = rows[-1]["target_cpu"] - rows[0]["target_cpu"] if remote else None
    if remote and summary["activity"] == "busy" and target_delta <= 0:
        raise ValueError("The busy target accumulated no observed CPU during measurement.")
    if remote and summary["activity"] == "blocked" and target_delta != 0:
        anomalies.append({"kind": "sleeping-target-cpu-advanced", "cpuDeltaNanoseconds": target_delta})
    spans.sort()
    brackets.sort()
    return {"valid": not anomalies, "readings": count, "activity": summary["activity"], "capture": summary["capture"],
            "controllerCpuPerReadingNanoseconds": summary["controllerCpuNanoseconds"] / count,
            "elapsedPerReadingNanoseconds": summary["elapsedNanoseconds"] / count,
            "callMedianNanoseconds": spans[count // 2], "callP99Nanoseconds": spans[(99 * count + 99) // 100 - 1],
            "callMaximumNanoseconds": spans[-1], "nativeBracketMedianNanoseconds": brackets[count // 2] if remote else None,
            "targetCpuDeltaNanoseconds": target_delta, "anomalies": anomalies,
            "clockAccuracyEstablished": False, "clrSafetyEstablished": False}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("summary", type=Path)
    parser.add_argument("rows", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    try:
        summary = json.loads(args.summary.read_text(encoding="utf-8-sig"))
        with args.rows.open(encoding="utf-8-sig", newline="") as source:
            rows = [{key: int(value) for key, value in row.items()} for row in csv.DictReader(source)]
        result = analyze(summary, rows)
    except (ValueError, KeyError, TypeError) as error:
        result = {"valid": False, "error": str(error)}
    args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    return 0 if result["valid"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
