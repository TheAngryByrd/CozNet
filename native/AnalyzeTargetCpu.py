import argparse
import json
from pathlib import Path


def integer(value, name, minimum=0):
    if type(value) is not int or value < minimum:
        raise ValueError(f"{name} must be an integer of at least {minimum}.")
    return value


def request_interval(request):
    slot = integer(request["Slot"], "slot")
    sequence = integer(request["Sequence"], "sequence", 1)
    if slot not in (0, 1) or integer(request["Result"]["Sequence"], "result sequence", 1) != sequence:
        raise ValueError("The request identity does not match the native result.")
    a, b = request["TargetCpuStart"], request["TargetCpuEnd"]
    if a is None or b is None:
        raise ValueError("The request has no complete target CPU observations.")
    for value in (a, b):
        for key in ("BeforeNanoseconds", "AfterNanoseconds", "ResolutionNanoseconds", "Registration"):
            integer(value[key], key, 1)
        integer(value["CpuNanoseconds"], "CpuNanoseconds")
        if value["Registration"] != slot + 1:
            raise ValueError("The CPU observation names a different target registration.")
    if a["ResolutionNanoseconds"] != b["ResolutionNanoseconds"]:
        raise ValueError("The target CPU clock resolution changed.")
    phase = request["Result"]["Timing"]
    if phase is None:
        raise ValueError("The request has no native phase observations.")
    names = ("RequestStartedNanoseconds", "HandlerStartedNanoseconds", "HandlerFinishedNanoseconds", "ObservedNanoseconds")
    start, handler, finish, observed = [integer(phase[key], key, 1) for key in names]
    queue = integer(phase["QueueReturnedNanoseconds"], "QueueReturnedNanoseconds", 1)
    order = [a["BeforeNanoseconds"], a["AfterNanoseconds"], start, handler, finish,
             observed, b["BeforeNanoseconds"], b["AfterNanoseconds"]]
    if order != sorted(order) or not start <= queue <= observed:
        raise ValueError("The target CPU brackets do not enclose the ordered native request phases.")
    if finish - handler != integer(request["Result"]["MeasuredHandlerNanoseconds"], "handler interval"):
        raise ValueError("The handler phase interval differs from the recorded duration.")
    cpu = b["CpuNanoseconds"] - a["CpuNanoseconds"]
    outer = b["AfterNanoseconds"] - a["BeforeNanoseconds"]
    if cpu < 0:
        raise ValueError("The target CPU counter decreased.")
    if cpu > outer:
        raise ValueError(f"Target CPU exceeds its enclosing wall interval by {cpu - outer} ns.")
    gap = handler - start
    outside = start - a["BeforeNanoseconds"] + b["AfterNanoseconds"] - handler
    lower, upper = max(0, cpu - outside), min(gap, cpu)
    return {"slot": slot, "sequence": sequence, "stage": request["Stage"], "startedStage": request["StartedStage"],
            "beforeFirstHandlerClockNanoseconds": gap, "targetCpuDeltaNanoseconds": cpu,
            "outerWallNanoseconds": outer, "outsideGapUpperNanoseconds": outside,
            "startReadBracketNanoseconds": a["AfterNanoseconds"] - a["BeforeNanoseconds"],
            "endReadBracketNanoseconds": b["AfterNanoseconds"] - b["BeforeNanoseconds"],
            "resolutionNanoseconds": a["ResolutionNanoseconds"],
            "modelGapCpuLowerNanoseconds": lower, "modelGapCpuUpperNanoseconds": upper,
            "modelGapOffCpuLowerNanoseconds": gap - upper, "modelGapOffCpuUpperNanoseconds": gap - lower}


def analyze(workload):
    if workload.get("targetCpuTiming") is not True:
        raise ValueError("Target CPU analysis requires explicit target CPU capture.")
    details = workload["backendDetails"]
    if (workload["backend"] != "linux-x64-signal" or details["timingClock"] != "linux-clock-monotonic"
            or details["timingUnit"] != "nanoseconds" or details.get("targetCpuTiming") is not True):
        raise ValueError("The workload does not establish the supported native clock contract.")
    intervals, anomalies, seen = [], [], set()
    for index, request in enumerate(workload["requests"]):
        try:
            interval = request_interval(request)
            if interval["sequence"] in seen:
                raise ValueError("The request sequence is duplicated.")
            seen.add(interval["sequence"])
            intervals.append(interval)
        except (ValueError, KeyError, TypeError) as error:
            anomalies.append({"requestIndex": index, "slot": request.get("Slot"),
                              "sequence": request.get("Sequence"), "error": str(error), "request": request})
    measurement = [r for r in intervals if r["stage"] == r["startedStage"] == "measurement"]
    return {"valid": not anomalies, "completedRequestsOnly": True,
            "requestCount": len(workload["requests"]), "analyzedRequestCount": len(intervals),
            "measurementRequestCount": len(measurement), "clockAccuracyEstablished": False,
            "boundsAssumeExactClockAccounting": True, "offCpuIdentifiesSchedulerCause": False,
            "longestMeasurementGaps": sorted(measurement, key=lambda r: r["beforeFirstHandlerClockNanoseconds"], reverse=True)[:20],
            "intervals": intervals, "anomalies": anomalies}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("workload", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    try:
        result = analyze(json.loads(args.workload.read_text(encoding="utf-8-sig")))
    except (ValueError, KeyError, TypeError) as error:
        result = {"valid": False, "error": str(error)}
    args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    return 0 if result["valid"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
