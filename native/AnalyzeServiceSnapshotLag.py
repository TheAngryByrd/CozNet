import argparse
import json
from pathlib import Path
from AnalyzeScheduler import parse_capture, overlap


def key(journal):
    return journal["Slot"], journal["Identity"], journal["Command"]


def observation(journal):
    value = journal["Observation"]
    header = value["Header"]
    children = value["Children"]
    if (header["Command"] != journal["Command"]
            or header["Budget"] != journal["BudgetNanoseconds"]
            or header["Children"] != len(children)
            or header["Captured"] <= 0):
        raise ValueError("The snapshot header does not match its journal.")
    previous = 0
    for ordinal, child in enumerate(children, 1):
        native = child["Native"]
        ack = native["Acknowledgment"]
        if (native["Ordinal"] != ordinal or ack["Sequence"] <= previous
                or not 0 < ack["Started"] <= ack["HandlerStarted"] <= ack["HandlerFinished"] <= ack["Observed"] <= header["Captured"]):
            raise ValueError("The child prefix or native timing is invalid.")
        previous = ack["Sequence"]
    return header, children


def analyze(workload):
    final = {}
    for journal in workload.get("serviceJournals", []) + workload.get("pendingServiceJournals", []):
        if journal.get("Observation") is None:
            continue
        identity = key(journal)
        if identity in final:
            raise ValueError("A final service journal is duplicated.")
        final[identity] = journal, observation(journal)
    findings = []
    matched = 0
    unmatched = 0
    for boundary_index, boundary in enumerate(workload.get("boundaries", [])):
        for journal in boundary.get("PendingServices", []):
            if journal.get("Observation") is None:
                continue
            header, children = observation(journal)
            match = final.get(key(journal))
            if match is None:
                unmatched += 1
                continue
            terminal, (later_header, later_children) = match
            if (header["IdentityFirst"] != later_header["IdentityFirst"]
                    or header["IdentitySecond"] != later_header["IdentitySecond"]
                    or journal["BudgetNanoseconds"] != terminal["BudgetNanoseconds"]
                    or header["Captured"] > later_header["Captured"]
                    or len(children) > len(later_children)
                    or [c["Native"] for c in children] != [c["Native"] for c in later_children[:len(children)]]):
                raise ValueError("The retained snapshot is not a matching earlier prefix.")
            matched += 1
            if len(children) == len(later_children):
                continue
            missing = later_children[len(children)]["Native"]
            observed = missing["Acknowledgment"]["Observed"]
            if observed < header["Captured"]:
                findings.append({"boundaryIndex": boundary_index, "stage": boundary["Stage"],
                    "slot": journal["Slot"], "identity": journal["Identity"], "command": journal["Command"],
                    "prefixChildren": len(children), "firstMissingOrdinal": missing["Ordinal"],
                    "firstMissingObservedNanoseconds": observed, "snapshotCapturedNanoseconds": header["Captured"],
                    "postOwnerCopyLowerBoundNanoseconds": header["Captured"] - observed})
    findings.sort(key=lambda item: item["postOwnerCopyLowerBoundNanoseconds"], reverse=True)
    return {"matchedPrefixes": matched, "unmatchedPrefixes": unmatched, "findings": findings,
            "causeEstablished": False, "workloadPassed": workload.get("passed")}


def correlate_scheduler(workload, result):
    captures = workload.get("schedulerCaptures", [])
    owner_capture = workload.get("serviceOwnerSchedulerTiming", False)
    expected = 4 if owner_capture else 3
    if (not workload.get("schedulerTiming") or not workload.get("schedulerStopped") or len(captures) != expected
            or any(capture is None for capture in captures)
            or len({c["ThreadId"] for c in captures}) != expected
            or len({c["ProcessId"] for c in captures}) != 1):
        raise ValueError("Expected stopped scheduler captures with distinct thread identities.")
    if owner_capture and captures[3]["ThreadId"] != workload.get("backendDetails", {}).get("nativeOwnerThreadId"):
        raise ValueError("The owner capture does not match the native owner thread identity.")
    parsed = [parse_capture(capture) for capture in captures]
    def observed(start, end, index=0):
        capture = captures[index]
        if start < capture["EnableAfter"] or end > capture["DisableBefore"]:
            raise ValueError("The native interval lies outside assured thread collection boundaries.")
        return overlap(parsed[index]["intervals"], start, end)

    findings = [{**finding, "controllerSwitchOverlap": observed(
        finding["firstMissingObservedNanoseconds"], finding["snapshotCapturedNanoseconds"])}
        for finding in result["findings"]]
    receipts = []
    phases = []
    for journal in workload.get("serviceJournals", []) + workload.get("pendingServiceJournals", []):
        if journal.get("Observation") is None:
            continue
        _, children = observation(journal)
        slot = journal["Slot"]
        if slot not in (0, 1):
            raise ValueError("Invalid native service slot.")
        for child in children:
            ack = child["Native"]["Acknowledgment"]
            start = ack["Observed"]
            end = child["FirstManagedReceiptUpper"]
            if child["ClockStatus"] != 0 or not start <= child["FirstSnapshotCaptured"] <= end:
                raise ValueError("The managed receipt has invalid native clock bounds.")
            receipts.append({"slot": journal["Slot"], "command": journal["Command"],
                "ordinal": child["Native"]["Ordinal"], "nativeObserved": start,
                "managedReceiptUpper": end, "controllerSwitchOverlap": observed(start, end)})
            phases.append({"slot": slot, "command": journal["Command"], "ordinal": child["Native"]["Ordinal"],
                "targetBeforeHandler": observed(ack["Started"], ack["HandlerStarted"], slot + 1),
                "targetDuringHandler": observed(ack["HandlerStarted"], ack["HandlerFinished"], slot + 1),
                "controllerAfterHandler": observed(ack["HandlerFinished"], ack["Observed"]),
                "targetAfterHandler": observed(ack["HandlerFinished"], ack["Observed"], slot + 1)})
            if owner_capture:
                phases[-1]["ownerAfterHandler"] = observed(ack["HandlerFinished"], ack["Observed"], 3)
                phases[-1]["ownerBeforeHandler"] = observed(ack["Started"], ack["HandlerStarted"], 3)
    receipts.sort(key=lambda item: item["controllerSwitchOverlap"]["wallNanoseconds"], reverse=True)
    return {**result, "findings": findings, "schedulerIntegrityPassed": True,
            "captures": parsed, "ownerCaptured": owner_capture, "longestReceiptIntervals": receipts[:10],
            "longestBeforeHandler": sorted(phases, key=lambda p: p["targetBeforeHandler"]["wallNanoseconds"], reverse=True)[:10],
            "longestAfterHandler": sorted(phases, key=lambda p: p["targetAfterHandler"]["wallNanoseconds"], reverse=True)[:10],
            "overlapEstablishesCause": False}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("workload", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--scheduler", action="store_true")
    args = parser.parse_args()
    workload = json.loads(args.workload.read_text(encoding="utf-8-sig"))
    result = analyze(workload)
    if args.scheduler:
        result = correlate_scheduler(workload, result)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
