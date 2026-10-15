import json
import pathlib
import sys


def require(condition, message):
    if not condition:
        raise ValueError(message)


def count(value):
    require(type(value) is int and 0 <= value <= 18446744073709551615, "Invalid unsigned evidence value")
    return value


def verify_failure_boundary(record):
    if "failureBoundary" not in record:
        return
    boundary = record["failureBoundary"]
    if record["failure"] is None:
        require(boundary is None, "Successful body has a failure boundary")
        return
    require(boundary is not None, "Workload failure has no boundary capture")
    capture = boundary["Capture"]
    require(capture["Failure"] == record["failure"], "Diagnostic capture replaced the original failure")
    require(count(capture["Started"]) <= count(capture["Finished"]), "Failure capture clock regressed")
    if "TimestampFrequency" in capture:
        require(count(capture["TimestampFrequency"]) > 0, "Failure capture frequency is zero")
    debt = boundary["LastObservedDebt"]
    if debt is not None:
        for field in ("Global", "SelectedCredit", "PeerWait", "Selected", "Peer"):
            count(debt[field])
        require(count(debt["Timestamp"]) <= capture["Started"], "Failure debt follows capture")
        require(debt["Selected"] == max(0, debt["Global"] - debt["SelectedCredit"])
                and debt["Peer"] == max(0, debt["Global"] - debt["PeerWait"]), "Failure debt arithmetic differs")
    state, raw, error = capture["State"], capture["Raw"], capture["CaptureFailure"]
    require(type(state) is int and state in (0, 1, 2), "Invalid failure capture state")
    if state == 0:
        require(raw is None and error is None, "Absent request contains diagnostic evidence")
    elif state == 2:
        require(raw is None and isinstance(error, str) and bool(error), "Failed capture lost its diagnostic error")
    else:
        require(raw is not None and error is None, "Captured boundary lost its raw evidence")
        header = raw["Header"]
        require(type(raw["Status"]) is int and type(raw["Clock"]["Status"]) is int, "Invalid diagnostic native status")
        count(raw["Clock"]["Nanoseconds"])
        for field in ("Generation", "Command", "Budget", "Children", "Measured", "Visibility", "Stop", "Error", "MayHaveDelivery", "Captured"):
            count(header[field])
        if raw["Status"] == 0:
            require(header["Captured"] > 0, "Diagnostic snapshot clock failed")
            require(header["Children"] == len(raw["Children"]), "Diagnostic child prefix is incomplete")
            if raw["Clock"]["Status"] == 0:
                require(raw["Clock"]["Nanoseconds"] >= header["Captured"], "Diagnostic clock precedes snapshot")
            if boundary["Generation"]:
                require(header["Generation"] == boundary["Generation"] and header["Command"] == boundary["Command"]
                        and header["Budget"] == boundary["Budget"], "Diagnostic request identity differs")
        cleanup = record["nativeCleanup"]
        if raw["Status"] == 0 and cleanup is not None and cleanup["Status"] == 0:
            require(header["Captured"] <= cleanup["Header"]["Captured"], "Failure snapshot follows cleanup")
            require(header["Generation"] == cleanup["Header"]["Generation"]
                    and header["Command"] == cleanup["Header"]["Command"] and header["Budget"] == cleanup["Header"]["Budget"]
                    and raw["Children"] == cleanup["Children"][:len(raw["Children"])], "Diagnostic prefix changed during cleanup")


def verify_record(record):
    verify_failure_boundary(record)
    require(type(record["passed"]) is bool and type(record["completed"]) is bool, "Invalid result flags")
    percent = record["percent"]
    require(type(percent) is int and percent in (0, 50), "Invalid speedup")
    source = record["sourceSamples"]
    binding = record["binding"]
    extent = record["nativeRange"]
    begin, size = count(extent["Start"]), count(extent["Size"])
    require(begin > 0 and size > 0 and begin + size <= 18446744073709551615, "Invalid bound range")
    owner = None
    last_time = last_cpu = 0
    for sample in source:
        identity = (count(sample["ProcessId"]), count(sample["ThreadId"]))
        require(all(identity) and (owner is None or owner == identity), "Sample owner changed")
        owner = identity
        time, cpu = count(sample["Timestamp"]), count(sample["CumulativeCpuNanoseconds"])
        require(time > 0 and time >= last_time and cpu >= last_cpu, "Sample evidence regressed")
        last_time, last_cpu = time, cpu
        selected = begin <= count(sample["InstructionPointer"]) < begin + size
        require((sample["Method"] is not None) == selected, "Sample label disagrees with native range")
        require(count(sample["PeriodNanoseconds"]) > 0, "Empty sample period")
    expected_credit = credit = remainder = 0
    for index, item in enumerate(record["samples"]):
        require(item["Index"] == index and index < len(source) and item["Sample"] == source[index], "Credited prefix changed")
        sample = item["Sample"]
        expected = 0
        if index > 0 and sample["Method"] is not None:
            expected, remainder = divmod(sample["PeriodNanoseconds"] * percent + remainder, 100)
        require(count(item["Credit"]) == expected, "Sample credit differs from nominal selected period")
        credit += item["Credit"]
        expected_credit += expected
    require(credit == expected_credit == count(record["credit"]), "Aggregate credit differs")
    paid = 0
    for ordinal, payment in enumerate(record["payments"], 1):
        command, generation, budget = (count(payment[name]) for name in ("Command", "Generation", "Budget"))
        require(command == ordinal and generation == ordinal and 0 < budget <= 2000000, "Payment identity or budget differs")
        require(count(payment["Started"]) <= count(payment["Finished"]), "Payment observation clock regressed")
        observation = payment["Observation"]
        raw, received = observation["Raw"], observation["Received"]
        header, children, clock = raw["Header"], raw["Children"], raw["Clock"]
        require(raw["Status"] == 0 and clock["Status"] == 0, "Payment capture failed")
        require(header["Generation"] == generation and header["Command"] == command and header["Budget"] == budget, "Header identity differs")
        require(header["Visibility"] == 3 and header["Stop"] == 1 and header["Error"] == 0
                and header["TransportError"] == 0 and header["MayHaveDelivery"] == 0, "Payment did not retire successfully")
        require(header["Children"] == len(children) == len(received) and len(children) > 0, "Payment children are missing")
        require(count(clock["Nanoseconds"]) >= count(header["Captured"]), "Receipt clock precedes capture")
        measured = 0
        for child_index, (child, receipt) in enumerate(zip(children, received), 1):
            require(child["Ordinal"] == child_index and child["Valid"] == 1 and child["WaitStatus"] == 0, "Invalid payment child")
            require(child["Worker"] == 2 and child["CodeVersion"] == binding["Generation"] and child["Site"] == 2, "Child site differs")
            requested, waited = count(child["Requested"]), count(child["Waited"])
            require(measured < budget and requested == min(50000, budget - measured), "Child request differs from remaining budget")
            armed, started, finished = (count(child[name]) for name in ("Armed", "Started", "Finished"))
            require(armed > 0, "Child arm clock failed")
            require(0 < requested <= 50000 and armed <= started <= finished and finished - started <= 5000000
                    and requested <= waited <= finished - started, "Child timing exceeds its contract")
            if child_index > 1:
                previous = children[child_index - 2]
                require(previous["Started"] <= armed <= previous["Finished"] and started >= previous["Finished"],
                        "Child continuation does not follow its preceding handler")
            captured, upper = count(receipt["FirstCaptured"]), count(receipt["FirstManagedReceiptUpper"])
            require(captured <= header["Captured"], "Child receipt capture exceeds snapshot")
            require(receipt["Raw"] == child and finished <= captured <= upper <= clock["Nanoseconds"]
                    and upper - armed <= 100000000 and receipt["Timing"]["UpperNanoseconds"] == upper - armed, "Child receipt differs")
            measured += waited
        require(measured == header["Measured"] and measured >= budget, "Payment aggregate differs")
        cleanup = payment["Cleanup"]
        require(cleanup["Status"] == 0 and cleanup["Children"] == children, "Released children changed")
        final_header = dict(cleanup["Header"])
        require(count(final_header["Captured"]) >= header["Captured"], "Final copy clock regressed")
        final_header["Captured"] = header["Captured"]
        require(final_header == header, "Released header changed")
        paid += measured
    require(paid == count(record["paid"]), "Retained payments differ from settled wait")
    for key in ("selectedPeak", "peerPeak", "finalDebt"):
        debt = record[key]
        if record["completed"] or (record["passed"] and key != "finalDebt"):
            require(debt is not None, "Accepted case has missing debt evidence")
        if debt is None:
            continue
        for field in ("Global", "SelectedCredit", "PeerWait", "Selected", "Peer"):
            count(debt[field])
        require(debt["Selected"] == max(0, debt["Global"] - debt["SelectedCredit"])
                and debt["Peer"] == max(0, debt["Global"] - debt["PeerWait"]), "Debt arithmetic differs")
        if record["passed"]:
            require(debt["Selected"] <= 2000000 and debt["Peer"] <= 2000000, "Accepted debt exceeds its bound")
            require(debt["SelectedCredit"] <= credit and debt["PeerWait"] <= paid
                    and debt["Global"] <= max(credit, paid), "Debt observation exceeds retained accounting totals")
    if record["passed"]:
        require(record["joined"] is True and record["closed"] is True and not record["cleanupErrors"], "Accepted case lost native ownership cleanup")
        cleanup = record["sourceCleanup"]
        require(all(cleanup[key] is True for key in ("SourceCreated", "DisableSucceeded", "CollectorJoined", "SourceDisposed")),
                "Accepted case lost source ownership cleanup")
        for error in cleanup["Errors"]:
            require(record["invalidateAfterPayment"] and error["Stage"] == "collector"
                    and error["Type"] == "System.IO.InvalidDataException"
                    and "The CLR probe lifetime is no longer admitted." in error["Error"], "Unexpected source cleanup failure")
        if paid:
            native = record["nativeCleanup"]
            header = native["Header"]
            pending = record.get("pending")
            if pending is not None:
                require(not record["completed"] and header["Generation"] == pending["generation"]
                        and header["Command"] == pending["command"] and header["Budget"] == pending["budget"],
                        "Final native cleanup identity differs from pending request")
            else:
                require(native == record["payments"][-1]["Cleanup"], "Final native cleanup differs from last released payment")
            require(native["Status"] == 0 and header["Visibility"] == 3 and header["MayHaveDelivery"] == 0
                    and header["Error"] == header["TransportError"] == 0 and header["Stop"] in (1, 3)
                    and header["Children"] == len(native["Children"]) > 0, "Accepted case lost final native cleanup evidence")
    if record["completed"]:
        require(record["passed"] and record["failure"] is None and record["verificationFailure"] is None
                and not record["cleanupErrors"] and record["joined"] and record["closed"], "Completed run has failed evidence")
        require(len(record["samples"]) == len(source) and record["batch"]["Samples"] == len(source), "Completed source has an unprocessed tail")
        require(record["sourceCleanup"]["SourceDisposed"] and record["sourceCleanup"]["CollectorJoined"], "Source ownership remains live")
        require(record["batch"]["LostRecords"] == record["batch"]["LostSamples"] == record["batch"]["Throttles"] == 0, "Completed source lost records")
        require(record["finalCounters"]["LastReading"]["CpuNanoseconds"] >= last_cpu, "Final counter does not cover samples")
        require(credit == paid == 0 if percent == 0 else paid >= credit > 0, "Payment reach differs")
        final = record["finalDebt"]
        require(final["SelectedCredit"] == credit and final["PeerWait"] == paid and final["Global"] == max(credit, paid)
                and final["Peer"] == 0, "Final debt does not reconcile with retained credit and payments")
    elif record["passed"]:
        require(record["invalidateAfterPayment"] and record["invalidated"] and record["rejectionVerified"] and record["rejectedAfterPayment"]
                and paid > 0 and record["verificationFailure"] is None
                and "The CLR probe lifetime is no longer admitted." in record["failure"], "Expected rejection was not verified")
    return {"samples": len(source), "creditedSamples": len(record["samples"]), "payments": len(record["payments"]),
            "credit": credit, "paid": paid, "completed": record["completed"], "casePassed": record["passed"]}


def verify_case(record, name):
    require(name in ("0", "50", "invalidate-after-payment"), "Unknown case")
    invalidating = name == "invalidate-after-payment"
    require(record["percent"] == (50 if invalidating else int(name))
            and record["invalidateAfterPayment"] is invalidating, "Record does not match its declared case")
    if record["passed"]:
        require(record["completed"] is (not invalidating) and record["rejectionVerified"] is invalidating
                and record["invalidated"] is invalidating, "Successful record does not reach its required case outcome")
    return verify_record(record)


def main(directory, output=None):
    results = []
    for name in ("0", "50", "invalidate-after-payment"):
        try:
            record = json.loads((directory / f"{name}.json").read_text(encoding="utf-8-sig"))
            result = {"case": name, "evidenceVerified": True, **verify_case(record, name)}
        except (KeyError, TypeError, ValueError, OSError, IndexError) as error:
            result = {"case": name, "evidenceVerified": False, "error": f"{type(error).__name__}: {error}"}
        results.append(result)
    passed = all(item["evidenceVerified"] and item.get("casePassed") is True for item in results)
    (output or directory / "analysis.json").write_text(json.dumps({"passed": passed, "cases": results}, indent=2), encoding="utf-8")
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main(pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]) if len(sys.argv) > 2 else None))
