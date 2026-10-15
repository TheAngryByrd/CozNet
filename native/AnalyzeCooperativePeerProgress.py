import json
import pathlib
import sys

from AnalyzeBoundSampledPayment import count, require
from AnalyzeBoundSelectedPayment import verify_record


def child_window(submit_started, submit_finished, armed, event, debt_timestamp, frequency):
    for value in (submit_started, submit_finished, armed, event, debt_timestamp, frequency):
        count(value)
    require(frequency > 0 and submit_started <= submit_finished and 0 < armed <= event,
            "Invalid peer progress clock interval")
    delta = event - armed
    lower = ((submit_started - debt_timestamp) * 1000000000 + delta * frequency) // frequency
    upper = -(-((submit_finished - debt_timestamp) * 1000000000 + delta * frequency) // frequency)
    relation = "after" if lower > 0 else "before" if upper < 0 else "overlaps"
    return dict(earliestNanosecondsFromDebt=lower, latestNanosecondsFromDebt=upper, relation=relation)


def retained_submits(timing, command):
    retained = timing["Recent"] + [timing[key] for key in ("Longest", "LongestQueue", "LongestAction", "LongestReturn")]
    unique = {}
    for item in retained:
        if item is not None and item["Operation"] == 1 and item["Command"] == command:
            unique[item["Sequence"]] = item
    require(len(unique) <= 1, "Ambiguous peer submit command")
    return next(iter(unique.values()), None)


def analyze_record(record):
    verified = verify_record(record)
    result = dict(evidenceVerified=True, casePassed=verified["casePassed"], completed=verified["completed"],
                  acceptedPeerPayments=len(record["payments"]),
                  assumptions=["Native nanoseconds and managed timestamp frequency measure comparable elapsed time.",
                               "The first child Armed timestamp occurs inside its matched PeerSubmit action."],
                  peer=dict(state="unavailable", reason="no-failure-boundary"))
    boundary = record["failureBoundary"]
    if boundary is None:
        return result
    debt = boundary["LastObservedDebt"]
    if debt is None or not boundary["Command"] or not boundary["Generation"]:
        result["peer"]["reason"] = "no-pending-request-or-debt-observation"
        return result
    peer = result["peer"]
    peer.update(command=boundary["Command"], generation=boundary["Generation"], budget=boundary["Budget"],
                debtTimestamp=debt["Timestamp"], peerDebtNanoseconds=debt["Peer"],
                debtLimitExceeded=max(debt["Selected"], debt["Peer"]) > 2000000)
    pending = record["pending"]
    if pending is None:
        peer["reason"] = "pending-reservation-unavailable"
        return result
    require(tuple(count(pending[key]) for key in ("command", "generation", "budget", "started")) ==
            tuple(boundary[key] for key in ("Command", "Generation", "Budget", "RequestStarted")),
            "Peer progress pending reservation differs")
    capture = boundary["Capture"]
    raw = capture["Raw"]
    if capture["State"] != 1 or raw is None or raw["Status"] != 0:
        peer["reason"] = "no-successful-native-capture"
        return result
    header, children = raw["Header"], raw["Children"]
    peer.update(publishedChildren=len(children), captureVisibility=header["Visibility"],
                captureStarted=capture["Started"], captureFinished=capture["Finished"])
    require((header["Command"], header["Generation"], header["Budget"]) ==
            (boundary["Command"], boundary["Generation"], boundary["Budget"]), "Peer progress request identity differs")
    if not children:
        peer["reason"] = "no-published-child"
        return result
    first = children[0]
    require(count(first["Ordinal"]) == 1 and count(first["Worker"]) == 2
            and count(first["CodeVersion"]) == record["binding"]["Generation"] and count(first["Site"]) == 2,
            "Peer progress first-child identity differs")
    if count(first["Valid"]) != 1:
        peer["reason"] = "first-child-invalid"
        return result
    armed, started, finished = (count(first[key]) for key in ("Armed", "Started", "Finished"))
    require(0 < armed <= started <= finished <= header["Captured"], "Peer progress child clocks are unordered")
    if not record.get("dispatchTimingEnabled"):
        peer["reason"] = "dispatch-timing-disabled"
        return result
    submit = retained_submits(record["dispatchTiming"], boundary["Command"])
    if submit is None:
        peer["reason"] = "submit-not-retained"
        return result
    if submit["Failed"] or submit["Started"] is None or submit["Finished"] is None:
        peer["reason"] = "submit-action-incomplete-or-failed"
        return result
    require(submit["Generation"] == 0, "Peer submit unexpectedly has a returned generation")
    require(boundary["RequestStarted"] <= submit["Submitted"] <= submit["Started"] <= submit["Finished"]
            <= submit["Returned"] <= debt["Timestamp"], "Peer submit does not precede the debt observation")
    require(not any(payment["Command"] == boundary["Command"] for payment in record["payments"]),
            "Pending peer command already has accepted payment")
    peer.update(state="bounded", reason=None, submitSequence=submit["Sequence"],
                submitStarted=submit["Started"], submitFinished=submit["Finished"], firstArmed=armed,
                firstChildStarted=started, firstChildFinished=finished,
                firstChildStart=child_window(submit["Started"], submit["Finished"], armed, started,
                                            debt["Timestamp"], record["timestampFrequency"]),
                firstChildFinish=child_window(submit["Started"], submit["Finished"], armed, finished,
                                             debt["Timestamp"], record["timestampFrequency"]))
    return result


def main(source, output):
    try:
        result = analyze_record(json.loads(source.read_text(encoding="utf-8-sig")))
    except (KeyError, TypeError, ValueError, OSError, IndexError) as error:
        result = dict(evidenceVerified=False, error=f"{type(error).__name__}: {error}")
    output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    return 0 if result["evidenceVerified"] else 1


if __name__ == "__main__":
    sys.exit(main(pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])))
