import argparse
from decimal import Decimal
import json
from pathlib import Path


def request_interval(request, frequency):
    start, end = request["ControllerCpuStart"], request["ControllerCpuEnd"]
    if start is None or end is None:
        raise ValueError("A request has no complete controller CPU bracket.")
    first, last = request["StartedTimestamp"], request["CompletedTimestamp"]
    ordered = [start["BeforeTimestamp"], start["AfterTimestamp"], first, last,
               end["BeforeTimestamp"], end["AfterTimestamp"]]
    if any(type(value) is not int or value < 0 for value in ordered) or ordered != sorted(ordered):
        raise ValueError("The CPU brackets do not enclose the managed request.")
    if (type(start["CpuNanoseconds"]) is not int or type(end["CpuNanoseconds"]) is not int
            or start["CpuNanoseconds"] < 0 or end["CpuNanoseconds"] < start["CpuNanoseconds"]):
        raise ValueError("The controller CPU counter is invalid or decreased.")
    scale = Decimal(1_000_000_000) / frequency
    cpu = end["CpuNanoseconds"] - start["CpuNanoseconds"]
    outer_wall = (ordered[-1] - ordered[0]) * scale
    if cpu > outer_wall:
        raise ValueError("Controller CPU time exceeds the enclosing elapsed interval.")
    duration = (last - first) * scale
    outside = ((first - ordered[0]) + (ordered[-1] - last)) * scale
    cpu_lower = max(Decimal(0), cpu - outside)
    cpu_upper = min(duration, cpu)
    return {"slot": request["Slot"], "sequence": request["Sequence"], "stage": request["Stage"],
            "startedStage": request["StartedStage"], "startedTimestamp": first, "completedTimestamp": last,
            "requestNanoseconds": duration, "controllerCpuDeltaNanoseconds": cpu,
            "outsideRequestUpperNanoseconds": outside,
            "requestCpuLowerNanoseconds": cpu_lower, "requestCpuUpperNanoseconds": cpu_upper,
            "requestOffCpuLowerNanoseconds": duration - cpu_upper,
            "requestOffCpuUpperNanoseconds": duration - cpu_lower}


def analyze(workload):
    frequency = workload["stopwatchFrequency"]
    if type(frequency) is not int or frequency <= 0:
        raise ValueError("The Stopwatch frequency must be a positive integer.")
    if workload.get("diagnosticTrace") is not True:
        raise ValueError("Controller CPU analysis requires a diagnostic trace workload.")
    records = [request_interval(request, frequency) for request in workload["requests"]]
    if not records:
        raise ValueError("The workload contains no completed requests.")
    measurement = [record for record in records if record["stage"] == record["startedStage"] == "measurement"]
    return {"valid": True, "completedRequestsOnly": True, "requestCount": len(records),
            "measurementRequestCount": len(measurement),
            "longestMeasurementRequests": sorted(measurement, key=lambda r: r["requestNanoseconds"], reverse=True)[:20],
            "intervals": records, "offCpuIdentifiesSchedulerCause": False,
            "cpuIntervalsCanOverlap": True}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("workload", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    try:
        workload = json.loads(args.workload.read_text(encoding="utf-8-sig"), parse_float=Decimal)
        result = analyze(workload)
    except (ValueError, KeyError, TypeError) as error:
        result = {"valid": False, "error": str(error)}
    args.output.write_text(json.dumps(result, indent=2, default=str), encoding="utf-8")
    return 0 if result["valid"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
