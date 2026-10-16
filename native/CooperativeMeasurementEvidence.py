from decimal import Decimal

from AnalyzeBoundSampledPayment import count, require


def verify_measurement(record):
    enabled = record.get("finiteMeasurementEnabled", False)
    require(type(enabled) is bool, "Finite measurement activation is not Boolean")
    if not enabled:
        require(record.get("measurement") is None and record.get("measurementAssessment") is None,
                "Disabled finite measurement contains a capture")
        return None
    require(record.get("ownerDrainEnabled") is True and record["schemaVersion"] == 4,
            "Finite measurement requires owner draining and selected cadence")
    measurement, assessment = record["measurement"], record["measurementAssessment"]
    require(measurement["AcceptanceClosed"] is True, "Finite measurement admission remains open")
    require(assessment["Status"] in ("invalid", "unqualified"), "Finite measurement claimed qualification")
    reasons = assessment["Reasons"]
    require(isinstance(reasons, list) and len(reasons) == len(set(reasons))
            and "sample-boundary-membership-unqualified" in reasons
            and "instrumentation-attribution-unqualified" in reasons, "Finite measurement lost qualification limits")
    window = measurement["Window"]
    status = count(window["Status"])
    require(status in range(5), "Unknown measurement capture status")
    start, end = window["Start"], window["End"]
    require((status in (1, 2, 4)) == (start is not None) and (status == 2) == (end is not None),
            "Measurement status differs from retained boundaries")
    invalid = (not record["completed"] or record["failure"] is not None or record["verificationFailure"] is not None
               or bool(record["cleanupErrors"]) or not all(record[key] for key in ("joined", "controllerJoined", "closed"))
               or measurement["Failure"] is not None or measurement["CaptureFailure"] is not None or status != 2)
    if record["failure"] is not None:
        require(measurement["Failure"] == record["failure"], "Measurement lost its original workload failure")
    elif record["verificationFailure"] is not None:
        require(measurement["Failure"] == record["verificationFailure"], "Measurement lost its verification failure")
    for boundary in (start, end):
        if boundary is None:
            continue
        before, after = count(boundary["BeforeTimestamp"]), count(boundary["AfterTimestamp"])
        require(0 < before <= after and boundary["TimestampFrequency"] == record["timestampFrequency"],
                "Measurement clock bracket is invalid")
        values = boundary["Accounting"]
        global_delay, credit = count(values["Global"]), count(values["Credit"])
        prefix, epoch = count(values["AcceptedSampleSequence"]), count(values["Epoch"])
        require(epoch > 0 and prefix <= len(record["samples"])
                and credit == sum(item["Credit"] for item in record["samples"][:prefix]),
                "Frozen sample prefix or credit differs from accepted samples")
        require(values.get("Timer") is None and values["Cooperative"] is not None,
                "Cooperative capture fabricated timer ownership")
        journals = 0
        for name, payments, raw_pending in (("Selected", record["selectedPayments"], record["selectedPending"]),
                                             ("Peer", record["payments"], record["pending"])):
            worker, native = values[name], values["Cooperative"][name]
            ledger = worker["Ledger"]
            require(ledger is not None and ledger["Global"] == global_delay, "Frozen worker lost its ledger")
            released = [payment for payment in payments if payment["Finished"] <= before]
            require(worker["ReleasedWait"] == sum(payment["Observation"]["Raw"]["Header"]["Measured"] for payment in released),
                    "Frozen released wait includes later cleanup or omits a release")
            journals += len(released)
            settled = 0
            if native is None:
                require(worker["Pending"] is None and worker["ActiveWait"] == 0
                        and ledger["ServiceLease"] is None and ledger["UnsettledWait"] == 0
                        and ledger["AcceptedChildren"] == ledger["LastSequence"] == 0 and ledger["Phase"] == "runnable",
                        "Idle frozen worker contains pending payment")
            else:
                command, generation = count(native["Command"]), count(native["Generation"])
                children, wait = count(native["AcceptedChildren"]), count(native["ConfirmedWait"])
                require(command > 0 and generation <= 4294967295 and count(native["Budget"]) > 0
                        and type(native["LedgerSettled"]) is bool and isinstance(native["Lease"], str),
                        "Frozen native ownership has invalid identity")
                require(generation != 0 or children == wait == 0 and not native["LedgerSettled"],
                        "Unsubmitted ownership contains accepted or settled payment")
                require(worker["Pending"] == dict(Command=command, Generation=generation) and worker["ActiveWait"] == wait,
                        "Frozen native ownership differs from worker totals")
                candidates = [payment for payment in payments if payment["Command"] == command and payment["Generation"] == generation
                              and payment["Budget"] == native["Budget"] and payment["Started"] <= after and payment["Finished"] >= after]
                pending_matches = raw_pending is not None and all(raw_pending[key.lower()] == native[key]
                    for key in ("Command", "Generation", "Budget")) and raw_pending["started"] <= after
                require(candidates or pending_matches, "Frozen ownership does not match a retained request")
                if candidates:
                    retained = candidates[0]["Observation"]["Received"]
                    require(children <= len(retained) and wait == sum(item["Raw"]["Waited"] for item in retained[:children]),
                            "Frozen accepted prefix differs from its later receipt")
                    require(not native["LedgerSettled"] or children == len(retained),
                            "Settled ownership contains only a partial terminal receipt")
                if native["LedgerSettled"]:
                    settled = wait
                    require(ledger["ServiceLease"] is None and ledger["UnsettledWait"] == 0
                            and ledger["AcceptedChildren"] == ledger["LastSequence"] == 0 and ledger["Phase"] == "runnable",
                            "Settled native ownership was counted as unsettled ledger wait")
                else:
                    require(ledger["ServiceLease"] == native["Lease"] and ledger["UnsettledWait"] == wait
                            and ledger["AcceptedChildren"] == children and ledger["Phase"] == "delaying"
                            and ledger["LastSequence"] == ((generation << 32) | children if children else 0),
                            "Frozen accepted prefix differs from ledger ownership")
            expected_local = worker["ReleasedWait"] + settled + (credit if name == "Selected" else 0)
            require(count(ledger["Local"]) == expected_local and count(worker["Progress"]) <= record[
                "selectedBatches" if name == "Selected" else "peerBatches"], "Frozen local credit or progress is inconsistent")
        require(count(values["Cooperative"]["ReleasedJournalCount"]) == journals,
                "Frozen journal inventory includes cleanup")
        if values["Cooperative"]["Selected"] is not None or values["Cooperative"]["Peer"] is not None:
            require("native-ownership-at-boundary" in reasons, "Measurement omitted native ownership qualification")
        if any(values[name]["Ledger"]["Local"] != global_delay for name in ("Selected", "Peer")):
            require("unqualified-ledger-residual" in reasons, "Measurement omitted residual qualification")
    if start is not None:
        first = start["Accounting"]
        require(first["Selected"]["Progress"] == 0 and first["Global"] == first["Credit"] == first["AcceptedSampleSequence"] == 0
                and first["Cooperative"]["ReleasedJournalCount"] == 0 and first["Cooperative"]["Selected"] is None
                and first["Cooperative"]["Peer"] is None, "Measurement start is not the fresh cooperative experiment")
        if record.get("selectedWorkStarted") is not None:
            require(start["AfterTimestamp"] <= record["selectedWorkStarted"], "Measurement start follows selected work")
    estimate = assessment["DescriptiveEstimate"]
    if end is None:
        require(estimate is None, "Missing endpoint produced an estimate")
    else:
        first, last = start["Accounting"], end["Accounting"]
        require(start["AfterTimestamp"] <= end["BeforeTimestamp"], "Measurement endpoints overlap")
        require(last["AcceptedSampleSequence"] == len(record["samples"]), "Samples were accepted after freezing")
        require(last["Selected"]["Progress"] == record["selectedBatches"], "Frozen selected progress differs from completed work")
        if len(record["sourceSamples"]) > last["AcceptedSampleSequence"]:
            require("source-tail-outside-frozen-prefix" in reasons, "Measurement omitted source tail qualification")
        if record["finalDebt"] is not None:
            final = record["finalDebt"]
            require(count(final["Timestamp"]) >= end["AfterTimestamp"] and final["Global"] == last["Global"],
                    "Frozen global delay differs from the post-freeze ledger")
        later = [record.get("selectedPeak"), record.get("peerPeak")]
        if record["failureBoundary"] is not None:
            later.append(record["failureBoundary"]["LastObservedDebt"])
        for observed in later:
            if observed is not None and count(observed["Timestamp"]) >= end["AfterTimestamp"]:
                require(observed["Global"] == last["Global"], "Frozen global delay differs from a later debt observation")
        for key in ("Global", "Credit", "AcceptedSampleSequence", "Epoch"):
            require(last[key] >= first[key], "Frozen cumulative accounting regressed")
        require(last["Cooperative"]["ReleasedJournalCount"] >= first["Cooperative"]["ReleasedJournalCount"],
                "Frozen journal inventory regressed")
        for name in ("Selected", "Peer"):
            require(last[name]["Progress"] >= first[name]["Progress"] and last[name]["ReleasedWait"] >= first[name]["ReleasedWait"],
                    "Frozen worker counters regressed")
        lower = Decimal(end["BeforeTimestamp"] - start["AfterTimestamp"]) * 1000000000 / record["timestampFrequency"]
        upper = Decimal(end["AfterTimestamp"] - start["BeforeTimestamp"]) * 1000000000 / record["timestampFrequency"]
        delay = last["Global"] - first["Global"]
        expected = dict(ElapsedLowerNanoseconds=lower, ElapsedUpperNanoseconds=upper, GlobalDelayNanoseconds=delay,
                        EffectiveLowerNanoseconds=lower - delay, EffectiveUpperNanoseconds=upper - delay,
                        SelectedProgress=last["Selected"]["Progress"] - first["Selected"]["Progress"],
                        PeerProgress=last["Peer"]["Progress"] - first["Peer"]["Progress"],
                        AcceptedSamples=last["AcceptedSampleSequence"] - first["AcceptedSampleSequence"])
        require(estimate is not None and set(estimate) == set(expected), "Frozen estimate has missing or extra fields")
        for key, value in expected.items():
            require(type(estimate[key]) in (int, float) and abs(Decimal(str(estimate[key])) - value) < Decimal("0.000001"),
                    "Frozen estimator arithmetic differs: " + key)
        invalid |= lower - delay <= 0 or expected["SelectedProgress"] <= 0 or expected["PeerProgress"] <= 0 or expected["AcceptedSamples"] < 2
        if record["completed"]:
            require(end["BeforeTimestamp"] >= record["selectedWorkFinished"], "Endpoint precedes selected work completion")
        if record["failureBoundary"] is not None:
            require(end["AfterTimestamp"] <= record["failureBoundary"]["Capture"]["Started"],
                    "Failure diagnostics preceded endpoint capture")
        if record["completed"] and record["finalCounters"] is not None:
            transitions = record["finalCounters"]["Transitions"]
            if transitions and transitions[-1]["Completed"] and not transitions[-1]["Enabling"]:
                require(end["AfterTimestamp"] <= transitions[-1]["Before"], "Source shutdown preceded endpoint capture")
    require(assessment["Status"] == ("invalid" if invalid else "unqualified"), "Measurement outcome hides a failure")
    return assessment
