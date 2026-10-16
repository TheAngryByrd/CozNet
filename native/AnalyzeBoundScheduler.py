import json
import pathlib
import sys

from AnalyzeBoundSelectedPayment import verify_record
from AnalyzeBoundSampledPayment import require
from AnalyzeScheduler import parse_capture, overlap
from BoundSchedulerEvidence import native_window


def overlap_bounds(intervals, window):
    inner_start, inner_end = window["latestStart"], window["earliestEnd"]
    lower = overlap(intervals, inner_start, inner_end)["observedSwitchedOutNanoseconds"] if inner_start < inner_end else 0
    upper = overlap(intervals, window["earliestStart"], window["latestEnd"])["observedSwitchedOutNanoseconds"]
    return dict(observedSwitchedOutLowerNanoseconds=lower,
                observedSwitchedOutUpperNanoseconds=min(upper, window["durationUpperNanoseconds"]),
                outerWindowInsideObservedSwitchOut=window["durationUpperNanoseconds"] > 0 and any(item["start"] <= window["earliestStart"] and
                    window["latestEnd"] <= item["end"] for item in intervals))


def analyze_record(record):
    verified = verify_record(record)
    clock = verified["scheduler"]
    result = dict(evidenceVerified=True, casePassed=verified["casePassed"], completed=verified["completed"],
                  scheduler=clock, windows=[], assumptions=[
                      "Managed and scheduler clocks have equal reported elapsed-time rates after unit conversion.",
                      "Clock brackets bound epoch offset; there is no independent physical clock-error bound.",
                      "Only closed observed switch-out intervals contribute to overlap bounds.",
                      "Zero observed overlap does not prove continuous execution or identify scheduling cause."])
    if not clock["enabled"]:
        result.update(state="unavailable", reason="scheduler-disabled")
        return result
    capture = record["schedulerCapture"]
    streams = capture["Captures"]
    parsed = [parse_capture(stream) for stream in streams]
    observations = []
    submit, debt = record.get("peerSubmitProgress"), record.get("peerDebtProgress")
    for name, observation in (("submitQuery", submit), ("debtQuery", debt)):
        if observation is None or observation["Capture"] is None:
            continue
        query = observation["Capture"]
        observations.append((name, query["Started"], query["Finished"], observation))
        if name == "debtQuery":
            observations.append(("requestToDebt", observation["RequestStarted"], observation["DebtTimestamp"], observation))
    if submit is not None and debt is not None and submit["Capture"] is not None and debt["Capture"] is not None and \
            all(submit[key] == debt[key] for key in ("Command", "Generation", "Budget", "RequestStarted")):
        observations.append(("submitQueryToDebtQuery", submit["Capture"]["Finished"], debt["Capture"]["Started"], debt))
    for name, start, finish, observation in observations:
        require(capture["Begin"]["After"] <= start <= finish <= capture["End"]["Before"],
                "Progress window lies outside managed clock anchors")
        window = native_window(start, finish, clock)
        roles = []
        for role, stream, events in zip(("controller", "selected", "peer"), streams, parsed):
            require(stream["EnableAfter"] <= window["earliestStart"] and window["latestEnd"] <= stream["DisableBefore"],
                    "Progress window lies outside assured scheduler collection")
            roles.append(dict(role=role, threadId=events["threadId"], leftCensored=events["leftCensored"],
                              rightCensored=events["rightCensored"], **overlap_bounds(events["intervals"], window)))
        query = observation["Capture"]
        marker = query["Snapshot"] if query["Status"] == 0 and query["Failure"] is None else None
        result["windows"].append(dict(name=name, command=observation["Command"], generation=observation["Generation"],
                                      marker=marker, markerQueryStarted=query["Started"], markerQueryFinished=query["Finished"],
                                      managedStart=start, managedFinish=finish, nativeWindow=window, roles=roles))
    result.update(state="bounded" if observations else "unavailable",
                  reason=None if observations else "no-peer-query-windows")
    return result


def main(source, output):
    try:
        result = analyze_record(json.loads(source.read_text(encoding="utf-8-sig")))
    except (KeyError, TypeError, ValueError, OSError, IndexError) as error:
        result = dict(evidenceVerified=False, state="unavailable", error=f"{type(error).__name__}: {error}")
    output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["evidenceVerified"] else 1


if __name__ == "__main__":
    sys.exit(main(pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])))
