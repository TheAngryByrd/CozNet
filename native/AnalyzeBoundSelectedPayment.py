import json
import pathlib
import sys

from AnalyzeBoundSampledPayment import count, require, signed_count, verify_payments, verify_receipt_interval, verify_rejected_receipt_interval, verify_sample_publication, verify_dispatch_timing


def counts(mapping, signed=()):
    for key, value in mapping.items():
        (signed_count if key in signed else count)(value)


def verify_debt(debt, credit, paid, selected_paid, bounded):
    for field in ("Timestamp", "Global", "SelectedCredit", "PeerWait", "Selected", "Peer",
                  "SelectedLocal", "PeerLocal", "SelectedWait"):
        count(debt[field])
    require(debt["SelectedLocal"] == debt["SelectedCredit"] + debt["SelectedWait"]
            and debt["PeerLocal"] == debt["PeerWait"], "Ledger local delay differs from settled totals")
    require(debt["Selected"] == max(0, debt["Global"] - debt["SelectedLocal"])
            and debt["Peer"] == max(0, debt["Global"] - debt["PeerLocal"]), "Ledger debt arithmetic differs")
    require(debt["SelectedCredit"] <= credit and debt["PeerWait"] <= paid
            and debt["SelectedWait"] <= selected_paid
            and debt["Global"] <= max(debt["SelectedLocal"], debt["PeerLocal"]),
            "Debt observation exceeds retained settlement")
    if bounded:
        require(max(debt["Selected"], debt["Peer"]) <= 2000000, "Accepted debt exceeds 2 ms")


def verify_capture(boundary, record, worker):
    capture = boundary["Capture"]
    require(capture["Failure"] == record["failure"], "Capture replaced the workload failure")
    require(count(capture["Started"]) <= count(capture["Finished"])
            and count(capture["TimestampFrequency"]) == record["timestampFrequency"], "Invalid capture clock")
    debt = boundary["LastObservedDebt"]
    if debt is not None:
        verify_debt(debt, record["credit"], record["paid"], record["selectedPaid"], record["passed"])
        require(debt["Timestamp"] <= capture["Started"], "Debt observation follows failure capture")
    for field in ("Command", "Generation", "Budget", "RequestStarted"):
        count(boundary[field])
    state, raw, error = capture["State"], capture["Raw"], capture["CaptureFailure"]
    require(type(state) is int and state in (0, 1, 2), "Invalid capture state")
    if state == 0:
        require(raw is None and error is None, "Absent request contains capture evidence")
    elif state == 2:
        require(raw is None and isinstance(error, str) and bool(error), "Failed capture lost its error")
    else:
        require(raw is not None and error is None, "Captured request lost its raw evidence")
        header = raw["Header"]
        require(type(raw["Status"]) is int and type(raw["Clock"]["Status"]) is int,
                "Invalid raw capture status")
        count(raw["Clock"]["Nanoseconds"])
        counts(header, signed=("TransportError",))
        if raw["Status"] == 0:
            require(header["Children"] == len(raw["Children"]) and header["Captured"] > 0,
                    "Successful raw capture lost its prefix or clock")
            if raw["Clock"]["Status"] == 0:
                require(raw["Clock"]["Nanoseconds"] >= header["Captured"], "Raw capture clock regressed")
            if boundary["Generation"]:
                require(all(header[key] == boundary[key] for key in ("Generation", "Command", "Budget")),
                        "Raw capture request identity differs")
            for ordinal, child in enumerate(raw["Children"], 1):
                counts(child)
                require(child["Ordinal"] == ordinal and child["Worker"] == worker,
                        "Raw capture changed its slot or child order")


def verify_source(record):
    source, epochs = record["sourceSamples"], record["sourceEpochSamples"]
    require(len(source) == len(epochs), "Source epoch labels are missing")
    begin, size = count(record["nativeRange"]["Start"]), count(record["nativeRange"]["Size"])
    require(begin > 0 and size > 0 and begin + size <= 18446744073709551615, "Invalid native range")
    owner = None
    last_time = last_cpu = last_epoch = 0
    for sample, epoch in zip(source, epochs):
        identity = count(sample["ProcessId"]), count(sample["ThreadId"])
        require(all(identity) and (owner is None or identity == owner), "Source owner changed")
        owner = identity
        time, cpu = count(sample["Timestamp"]), count(sample["CumulativeCpuNanoseconds"])
        require(time > 0 and time >= last_time and cpu >= last_cpu, "Source sample regressed")
        require(epoch["Sample"] == sample and count(epoch["EligibilityEpoch"]) >= max(1, last_epoch),
                "Source sample epoch changed or regressed")
        require((sample["Method"] is not None) == (begin <= count(sample["InstructionPointer"]) < begin + size),
                "Source label differs from the bound native range")
        require(count(sample["PeriodNanoseconds"]) > 0, "Empty CPU period")
        last_time, last_cpu, last_epoch = time, cpu, epoch["EligibilityEpoch"]
    credit = remainder = last_epoch = 0
    for index, item in enumerate(record["samples"]):
        require(count(item["Index"]) == index and index < len(source) and item["Sample"] == source[index]
                and count(item["EligibilityEpoch"]) == epochs[index]["EligibilityEpoch"], "Credited sample prefix changed")
        expected = 0
        if item["EligibilityEpoch"] == last_epoch and item["Sample"]["Method"] is not None:
            expected, remainder = divmod(item["Sample"]["PeriodNanoseconds"] * record["percent"] + remainder, 100)
        require(count(item["Credit"]) == expected, "Epoch baseline or fractional sample credit differs")
        credit += expected
        last_epoch = item["EligibilityEpoch"]
    require(credit == count(record["credit"]), "Aggregate sample credit differs")
    return credit, last_cpu


PAUSE_TIMING_FIELDS = (
    "Started", "ProducerStopped", "DisabledCountersAccepted", "DrainAcknowledged",
    "BatchValidated", "SnapshotReady", "SamplesPublished", "CallbackReturned",
    "SourceEnabled", "EnabledCountersAccepted", "CollectorResumed", "Completed")


def verify_pause_timing(record):
    enabled = record.get("pauseTimingEnabled", False)
    require(type(enabled) is bool, "Pause timing activation is not Boolean")
    for pause in record["pauses"]:
        timing = pause.get("Timing")
        if not enabled:
            require(timing is None, "Disabled pause timing contains phase records")
            continue
        require(isinstance(timing, dict), "Enabled pause timing is missing")
        previous = count(pause["Started"])
        missing = False
        for field in PAUSE_TIMING_FIELDS:
            value = timing[field]
            if value is None:
                missing = True
                continue
            require(not missing and count(value) >= previous and value > 0,
                    "Pause timing regressed or skipped a boundary")
            previous = value
        require(timing["Started"] is not None, "Pause timing did not start")
        if pause["CallbackFinished"] is not None:
            require(timing["SamplesPublished"] is not None
                    and timing["SamplesPublished"] <= pause["CallbackFinished"],
                    "Callback completion precedes sample publication")
        if timing["CallbackReturned"] is not None:
            require(pause["CallbackFinished"] is not None
                    and pause["CallbackFinished"] <= timing["CallbackReturned"],
                    "Collection callback returned before workload completion")
        if pause["Resumed"] is not None:
            require(timing["Completed"] is not None and previous <= pause["Resumed"],
                    "Resumed pause has incomplete or late timing")
        else:
            require(timing["Completed"] is None, "Incomplete pause claims completed timing")


def verify_cadence(record):
    if record["schemaVersion"] == 3:
        return
    mode = record["selectedCadence"]
    require(mode in ("Continuous", "QualifiedSample"), "Unknown bound selected cadence")
    requested = count(record["requestedBatches"])
    require(0 < requested <= 2147483647 and count(record["selectedBatches"]) <= requested,
            "Invalid fixed selected work")
    if record["completed"]:
        require(record["selectedBatches"] == requested, "Completed cadence case changed fixed selected work")
        require(count(record["checksum"]) == requested * 49995000, "Cadence checksum differs from fixed selected work")
        require(0 < count(record["selectedWorkStarted"]) <= count(record["selectedWorkFinished"]),
                "Completed cadence case lacks its selected work interval")
    require(count(record["selectedChecks"]) >= len(record["pauses"]), "Pauses exceed admitted checks")
    for epoch, pause in enumerate(record["pauses"], 1):
        admission = pause["Admission"]
        prefix = count(admission["SampleCount"])
        require(count(admission["Epoch"]) == epoch and prefix == pause["FirstUnprocessed"],
                "Cadence admission changed its epoch or sample prefix")
        require(type(admission["Qualified"]) is bool, "Invalid cadence qualification")
        qualified = prefix >= 2 and all(
            item["EligibilityEpoch"] == epoch for item in record["samples"][prefix - 2:prefix])
        require(not admission["Qualified"] or qualified, "Cadence qualification differs from original sample epochs")
        require(mode != "QualifiedSample" or admission["Qualified"] and qualified,
                "Selected pause lacks a complete current-epoch interval")


def verify_pauses(record):
    pauses, epochs = record["pauses"], record["sourceEpochSamples"]
    cursor = previous_processed = previous_finished = 0
    for index, pause in enumerate(pauses):
        started, first = count(pause["Started"]), count(pause["FirstUnprocessed"])
        require(started >= previous_finished and previous_processed <= first <= len(record["samples"]),
                "Pause ordering or provisional prefix changed")
        drained, callback, resumed = (pause[name] for name in ("FinalDrainCount", "CallbackFinished", "Resumed"))
        if drained is not None:
            require(first <= count(drained) <= len(record["samples"]), "Final drain prefix changed")
            require(all(item["EligibilityEpoch"] == index + 1 for item in epochs[cursor:drained]),
                    "Final drain changed a sample's eligibility epoch")
            cursor = previous_processed = drained
        if callback is not None:
            require(drained is not None and count(callback) >= started, "Callback lacks an acknowledged final drain")
        if resumed is not None:
            require(callback is not None and count(resumed) >= callback, "Source resumed before callback return")
            previous_finished = resumed
        else:
            require(index == len(pauses) - 1 and not record["completed"], "An incomplete pause precedes continued execution")
            previous_finished = callback if callback is not None else started
    trailing_epoch = len(pauses) + 1 if not pauses or pauses[-1]["Resumed"] is not None else len(pauses)
    require(all(item["EligibilityEpoch"] == trailing_epoch for item in epochs[cursor:]),
            "Resumed source assigned the wrong eligibility epoch")
    used = set()
    payment_pauses = []
    pause_index = 0
    for payment in record["selectedPayments"]:
        while pause_index + 1 < len(pauses) and pauses[pause_index + 1]["Started"] <= payment["Started"]:
            pause_index += 1
        require(pause_index < len(pauses) and pause_index not in used, "Selected payment lacks a unique pause")
        pause = pauses[pause_index]
        end = pause["CallbackFinished"]
        if end is None:
            require(record["selectedFailureBoundary"] is not None, "Selected payment lacks callback or failure evidence")
            end = record["selectedFailureBoundary"]["Capture"]["Started"]
        require(pause["FinalDrainCount"] is not None and pause["Started"] <= payment["Started"] <= payment["Finished"] <= end,
                "Selected payment escaped its excluded callback")
        used.add(pause_index)
        payment_pauses.append(pause_index)
    return payment_pauses


def verify_counters(record, last_cpu, payment_pauses):
    initial, final = record["initialCounters"], record["finalCounters"]
    require(initial["SourceIdentity"] == final["SourceIdentity"] and bool(initial["SourceIdentity"])
            and initial["Units"] == final["Units"] == "task-clock-nanoseconds", "Counter source identity changed")
    for snapshot in (initial, final):
        count(snapshot["Epoch"])
        count(snapshot["FailedReads"])
        require(snapshot["Enabled"] is False and snapshot["Overflowed"] is False
                and snapshot["UnrecordedCleanup"] is False and snapshot["FailedReads"] == 0
                and snapshot["FirstReadFailure"] is None, "Accepted source lost counter evidence")
    require(initial["Epoch"] == 0 and initial["Transitions"] == []
            and initial["LastReading"]["CpuNanoseconds"] == 0, "Source did not start fresh")
    transitions = final["Transitions"]
    resumed = sum(pause["Resumed"] is not None for pause in record["pauses"])
    require(len(transitions) == 2 * (resumed + 1) and final["Epoch"] == resumed + 1,
            "Counter transitions differ from completed source epochs")
    last = count(initial["LastReading"]["After"])
    frequency = count(initial["LastReading"]["TimestampFrequency"])
    require(frequency > 0 and frequency == record["timestampFrequency"], "Counter timestamp frequency differs from pause clock")
    for index, transition in enumerate(transitions):
        count(transition["Epoch"])
        count(transition["TimestampFrequency"])
        require(transition["Completed"] is True and transition["Error"] is None
                and transition["Enabling"] is (index % 2 == 0) and transition["Epoch"] == index // 2 + 1,
                "Counter transition failed or changed epoch order")
        require(transition["TimestampFrequency"] == frequency
                and last <= count(transition["Before"]) <= count(transition["After"]), "Counter transition clock regressed")
        last = transition["After"]
    for index, pause in enumerate(record["pauses"]):
        enabled, disabled = transitions[2 * index:2 * index + 2]
        require(enabled["After"] <= pause["Started"] <= disabled["Before"]
                and pause["CallbackFinished"] is not None and disabled["After"] <= pause["CallbackFinished"],
                "Pause is not enclosed by its managed counter boundaries")
        if pause["Resumed"] is not None:
            resume = transitions[2 * index + 2]
            require(pause["CallbackFinished"] <= resume["Before"] <= resume["After"] <= pause["Resumed"],
                    "Source enable precedes excluded callback return")
    for payment, index in zip(record["selectedPayments"], payment_pauses):
        require(transitions[2 * index + 1]["After"] <= payment["Started"],
                "Selected payment precedes producer stop acknowledgement")
    for snapshot in (initial, final):
        reading = snapshot["LastReading"]
        count(reading["Epoch"])
        count(reading["TimestampFrequency"])
        require(reading["SourceIdentity"] == snapshot["SourceIdentity"] and reading["Epoch"] == snapshot["Epoch"]
                and reading["Enabled"] is False and reading["Error"] is None
                and reading["TimestampFrequency"] == frequency
                and count(reading["Before"]) <= count(reading["After"]), "Counter reading differs from source state")
    require(final["LastReading"]["Before"] >= last and count(final["LastReading"]["CpuNanoseconds"]) >= last_cpu,
            "Final counter does not cover closed source evidence")


def verify_child_cpu(record):
    enabled = record.get("childCpuEnabled", False)
    require(type(enabled) is bool, "Child CPU activation is not Boolean")
    seen = {}
    reading_failures = unsupported = 0

    def visit(value):
        nonlocal reading_failures, unsupported
        if isinstance(value, list):
            for item in value:
                visit(item)
        elif isinstance(value, dict):
            if all(key in value for key in ("Header", "Children", "Status")):
                cpu = value.get("Cpu")
                header = value["Header"]
                if not enabled or header["Visibility"] != 3:
                    require(cpu is None, "Unexpected child CPU capture")
                else:
                    require(cpu is not None, "Retired receipt lost child CPU capture")
                    require(cpu["Failure"] is None and type(cpu["Status"]) is int and cpu["Status"] == 0,
                            "Child CPU snapshot failed; retain the original service outcome")
                    slot, generation = count(cpu["Slot"]), count(cpu["Generation"])
                    require(slot in (0, 1) and generation == header["Generation"], "Child CPU capture identity differs")
                    require(count(cpu["Required"]) == len(cpu["Children"]), "Child CPU capture count differs")
                    children = {child["Ordinal"]: child for child in value["Children"]}
                    require(len(children) == len(value["Children"]), "CPU service journal has duplicate ordinals")
                    ordinals = []
                    key = slot, generation
                    if key in seen:
                        require(seen[key] == cpu, "Retired child CPU evidence changed")
                    fresh = key not in seen
                    seen[key] = cpu
                    for observation in cpu["Children"]:
                        for field in ("Generation", "Command", "Ordinal", "Worker", "CodeVersion", "Site", "Requested"):
                            count(observation[field])
                        ordinal = observation["Ordinal"]
                        require(ordinal in children and ordinal not in ordinals, "Child CPU ordinal is missing or duplicated")
                        ordinals.append(ordinal)
                        child = children[ordinal]
                        require(observation["Generation"] == generation and observation["Command"] == header["Command"]
                                and observation["Worker"] == (2 if slot == 0 else 1)
                                and observation["CodeVersion"] == record["binding"]["Generation"] and observation["Site"] == 2
                                and all(observation[field] == child[field] for field in ("Worker", "CodeVersion", "Site", "Requested")),
                                "Child CPU observation changed request or owner")
                        for reading in (observation["Before"], observation["After"]):
                            counts(reading, signed=("Error",))
                            status = reading["Status"]
                            require(status in (0, 1, 2), "Invalid child CPU reading status")
                            if status == 0:
                                require(all(reading[field] == 0 for field in ("Clock", "Nanoseconds", "Resolution", "Error")),
                                        "Unsupported CPU clock contains a measurement")
                                unsupported += int(fresh)
                            elif status == 1:
                                require(reading["Clock"] == 1 and reading["Resolution"] > 0 and reading["Error"] == 0,
                                        "Observed CPU reading lacks clock metadata")
                            else:
                                require(reading["Clock"] == 1 and reading["Error"] != 0
                                        and reading["Nanoseconds"] == reading["Resolution"] == 0,
                                        "Failed CPU read lost its error")
                                reading_failures += int(fresh)
                        before, after = observation["Before"], observation["After"]
                        if before["Status"] == after["Status"] == 1:
                            require(after["Nanoseconds"] >= before["Nanoseconds"], "Child CPU clock regressed")
                            require(child["Finished"] >= child["Started"] and
                                    after["Nanoseconds"] - before["Nanoseconds"] <= child["Finished"] - child["Started"],
                                    "Child CPU exceeds its enclosing wall interval")
                    require(ordinals == sorted(ordinals), "Child CPU order differs")
                    require(all((child["Waited"] == 0 and child["WaitStatus"] == 0) or ordinal in ordinals for ordinal, child in children.items()),
                            "Measured wait lost child CPU evidence")
            for key, item in value.items():
                if key != "Cpu":
                    visit(item)

    visit(record)
    return dict(enabled=enabled, generations=len(seen), failedReadings=reading_failures, unsupportedReadings=unsupported)


def verify_peer_progress(record):
    enabled = record.get("peerProgressEnabled", False)
    require(type(enabled) is bool, "Peer progress activation is not Boolean")
    fields = ("peerOwner", "peerOwnerFailure", "peerSubmitProgress", "peerDebtProgress")
    if not enabled:
        require(all(record.get(key) is None for key in fields), "Disabled peer progress contains observations")
        return
    owner, owner_error = record.get("peerOwner"), record.get("peerOwnerFailure")
    if owner is not None:
        require(all(0 < count(owner[key]) <= 4294967295 for key in ("Process", "Thread")), "Invalid peer owner identity")
        require(owner_error is None, "Peer owner has both identity and failure")
    elif owner_error is not None:
        require(isinstance(owner_error, str) and bool(owner_error), "Invalid peer owner failure")
    submissions = [(p["Command"], p["Generation"], p["Budget"], p["Started"]) for p in record["payments"]]
    pending = record.get("pending")
    if pending is not None and pending["generation"]:
        submissions.append(tuple(pending[k] for k in ("command", "generation", "budget", "started")))
    require(not submissions or owner is not None or owner_error is not None, "Peer owner evidence is missing")
    latest = record.get("peerSubmitProgress")
    require(not submissions or latest is not None, "Peer submission progress is missing")
    boundary = record.get("failureBoundary")
    debt = boundary.get("LastObservedDebt") if boundary else None
    exceeded = debt is not None and max(debt["Selected"], debt["Peer"]) > 2000000
    at_debt = record.get("peerDebtProgress")
    require(not exceeded or at_debt is not None, "Peer debt progress is missing")
    require(at_debt is None or exceeded, "Peer debt progress has no failed debt gate")
    for observation, is_debt in ((latest, False), (at_debt, True)):
        if observation is None:
            continue
        require(type(observation["Slot"]) is int and observation["Slot"] == 0, "Peer progress changed its slot")
        require(observation["Owner"] == owner, "Peer progress changed its owner")
        identity = tuple(count(observation[k]) for k in ("Command", "Generation", "Budget", "RequestStarted"))
        require(observation["DebtTimestamp"] == (debt["Timestamp"] if is_debt else None), "Peer progress changed its debt timestamp")
        if is_debt:
            require(identity == tuple(boundary[k] for k in ("Command", "Generation", "Budget", "RequestStarted")),
                    "Debt progress differs from the failure request")
        capture, unavailable = observation["Capture"], observation["Unavailable"]
        if capture is None:
            require(is_debt and isinstance(unavailable, str) and bool(unavailable), "Peer progress lacks capture evidence")
            require(pending is None or pending["generation"] == 0, "Pending peer progress was reported absent")
            continue
        require(unavailable is None and identity in submissions, "Peer progress request identity differs")
        if not is_debt:
            require(identity == max(submissions, key=lambda item: item[0]), "Peer submit progress is not the latest request")
        start, finish = count(capture["Started"]), count(capture["Finished"])
        require(identity[3] <= start <= finish, "Invalid peer progress query bracket")
        if is_debt:
            require(debt["Timestamp"] <= start and finish <= boundary["Capture"]["Started"], "Peer progress is outside failure capture order")
        else:
            accepted = next((p for p in record["payments"] if (p["Command"], p["Generation"]) == identity[:2]), None)
            if accepted is not None:
                require(finish <= accepted["ReceiptInterval"]["Observed"], "Peer submission query follows receipt observation")
            elif boundary is not None:
                require(finish <= boundary["Capture"]["Started"], "Peer submission query follows failure capture")
            if at_debt is not None and at_debt["Generation"] == identity[1]:
                require(finish <= at_debt["DebtTimestamp"], "Peer submission query follows failed debt observation")
        status, raw, error = capture["Status"], capture["Snapshot"], capture["Failure"]
        require(status is None or type(status) is int and 0 <= status <= 8, "Invalid peer progress status")
        if error is not None:
            require(isinstance(error, str) and bool(error), "Invalid peer progress failure")
            continue
        require(status is not None and raw is not None, "Peer progress query lost its result")
        counts(raw)
        if status == 0:
            require(raw["Generation"] == identity[1] and 0 < raw["Generation"] <= 4294967295,
                    "Peer progress generation differs")
            require(raw["Ordinal"] <= 100000 and 1 <= raw["Phase"] <= 10 and
                    (raw["Phase"] != 1 or raw["Ordinal"] == 0) and
                    (not 4 <= raw["Phase"] <= 8 or raw["Ordinal"] > 0), "Invalid peer progress phase or ordinal")
        else:
            require(all(value == 0 for value in raw.values()), "Unavailable peer progress contains a marker")


def verify_record(record):
    verify_peer_progress(record)
    verify_dispatch_timing(record)
    verify_sample_publication(record)
    verify_rejected_receipt_interval(record)
    require(type(record["schemaVersion"]) is int and record["schemaVersion"] in (3, 4), "Expected schema version 3 or 4")
    require(count(record["timestampFrequency"]) > 0, "Managed timestamp frequency is zero")
    require(record["selectedPaymentEnabled"] is True, "Selected payment mode is absent")
    for key in ("passed", "completed", "invalidateAfterPayment", "invalidateAfterSelectedPayment", "invalidated",
                "rejectionVerified", "rejectedAfterPayment", "joined", "controllerJoined", "closed"):
        require(type(record[key]) is bool, "Invalid result flag: " + key)
    require(type(record["percent"]) is int and record["percent"] in (0, 50), "Invalid speedup")
    credit, last_cpu = verify_source(record)
    paid = verify_payments(record["payments"], record["binding"], 2)
    selected_paid = verify_payments(record["selectedPayments"], record["binding"], 1)
    for payment in record["payments"] + record["selectedPayments"]:
        verify_receipt_interval(payment, record["timestampFrequency"])
    require(paid == count(record["paid"]) and selected_paid == count(record["selectedPaid"]), "Settled payment totals differ")
    payment_pauses = verify_pauses(record)
    verify_cadence(record)
    verify_pause_timing(record)
    child_cpu = verify_child_cpu(record)
    for key in ("selectedPeak", "peerPeak", "finalDebt"):
        debt = record[key]
        require(not record["passed"] or key == "finalDebt" and not record["completed"] or debt is not None,
                "Accepted case lost debt evidence")
        if debt is not None:
            verify_debt(debt, credit, paid, selected_paid, record["passed"])
    if record["failure"] is None:
        require(record["failureBoundary"] is None and record["selectedFailureBoundary"] is None,
                "Successful body has failure captures")
    else:
        for key, worker in (("failureBoundary", 2), ("selectedFailureBoundary", 1)):
            require(record[key] is not None, "Workload failure lost one slot's capture")
            verify_capture(record[key], record, worker)
        peer, selected = record["failureBoundary"]["Capture"], record["selectedFailureBoundary"]["Capture"]
        require(peer["Finished"] <= selected["Started"] and peer["TimestampFrequency"] == selected["TimestampFrequency"],
                "Slot capture order or clock units changed")
    if record["passed"]:
        require(record["joined"] and record["controllerJoined"] and record["closed"] and not record["cleanupErrors"]
                and record["verificationFailure"] is None, "Accepted case lost native cleanup")
        require(record["selectedPending"] is None and (not record["completed"] or record["pending"] is None),
                "Accepted case retains an unexpected ledger lease")
        native = record["nativeCleanup"]
        if record["pending"] is not None:
            pending = record["pending"]
            require(native is not None and native["Status"] == 0, "Cancelled peer has no final native copy")
            header = native["Header"]
            counts(header, signed=("TransportError",))
            require(all(header[field] == count(pending[field.lower()]) for field in ("Command", "Generation", "Budget"))
                    and header["Visibility"] == 3 and header["Stop"] in (1, 3) and header["MayHaveDelivery"] == 0
                    and header["Error"] == header["TransportError"] == 0 and count(header["Captured"]) > 0
                    and header["Children"] == len(native["Children"]), "Cancelled peer lost retired ownership")
            measured = 0
            for index, child in enumerate(native["Children"], 1):
                counts(child)
                require(child["Ordinal"] == index and child["Valid"] == 1 and child["Worker"] == 2
                        and child["CodeVersion"] == record["binding"]["Generation"] and child["Site"] == 2,
                        "Cancelled peer cleanup changed child ownership")
                measured += count(child["Waited"])
            require(measured == header["Measured"], "Cancelled peer cleanup aggregate differs")
            capture = record["failureBoundary"]["Capture"]
            if capture["State"] == 1 and capture["Raw"]["Status"] == 0:
                raw = capture["Raw"]
                require(raw["Header"]["Captured"] <= header["Captured"]
                        and raw["Children"] == native["Children"][:len(raw["Children"])],
                        "Cancelled peer lost its captured prefix")
        else:
            releases = [payments[-1] for payments in (record["payments"], record["selectedPayments"]) if payments]
            latest = max((payment["Finished"] for payment in releases), default=0)
            copies = [payment["Cleanup"] for payment in releases if payment["Finished"] == latest]
            require(native in copies if copies else native is None, "Final native copy differs from the latest released slot")
        cleanup = record["sourceCleanup"]
        require(all(cleanup[key] is True for key in ("SourceCreated", "DisableSucceeded", "CollectorJoined", "SourceDisposed")),
                "Accepted case lost source cleanup")
        for error in cleanup["Errors"]:
            require(record["invalidateAfterSelectedPayment"] and error["Stage"] == "collector"
                    and error["Type"] == "System.IO.InvalidDataException"
                    and "The CLR probe lifetime is no longer admitted." in error["Error"], "Unexpected source cleanup failure")
        verify_counters(record, last_cpu, payment_pauses)
        source, batch = record["sourceSamples"], record["batch"]
        counts({key: value for key, value in batch.items() if key != "SelectedSymbols"})
        symbols = {sample["Method"] for sample in source if sample["Method"] is not None}
        require(batch["Samples"] == len(source) and batch["LostRecords"] == batch["LostSamples"] == batch["Throttles"] == 0
                and batch["RepresentedCpuNanoseconds"] == sum(sample["PeriodNanoseconds"] for sample in source)
                and batch["DistinctInstructionPointers"] == len({sample["InstructionPointer"] for sample in source})
                and batch["SelectedMethodSamples"] == sum(sample["Method"] is not None for sample in source)
                and batch["UnmappedSamples"] == sum(sample["Method"] is None for sample in source)
                and set(batch["SelectedSymbols"]) == symbols and len(batch["SelectedSymbols"]) == len(symbols),
                "Accepted sample batch differs from retained records")
        if record["selectedPayments"]:
            require(record["selectedCleanup"] == record["selectedPayments"][-1]["Cleanup"]
                    and record["selectedReceipt"] == record["selectedPayments"][-1]["Observation"],
                    "Final selected receipt or cleanup changed")
        else:
            require(record["selectedCleanup"] is None and record["selectedReceipt"] is None,
                    "Empty selected payment history contains a receipt")
    if record["completed"]:
        require(record["passed"] and record["failure"] is None and not record["invalidated"]
                and not record["rejectionVerified"] and not record["rejectedAfterPayment"]
                and not record["invalidateAfterPayment"] and not record["invalidateAfterSelectedPayment"]
                and record["peerFailure"] is None, "Completed case has rejected evidence")
        require(len(record["samples"]) == len(record["sourceSamples"]) == record["batch"]["Samples"]
                and record["batch"]["LostRecords"] == record["batch"]["LostSamples"] == record["batch"]["Throttles"] == 0,
                "Completed source lost or left unprocessed samples")
        final = record["finalDebt"]
        require(final["SelectedCredit"] == credit and final["PeerWait"] == paid and final["SelectedWait"] == selected_paid
                and final["Global"] == max(credit + selected_paid, paid) and final["Peer"] == 0,
                "Final ledger does not reconcile with credit and settled waits")
        if record["percent"] == 0:
            require(credit == paid == selected_paid == 0 and not record["pauses"], "Zero speedup performed payment")
        else:
            require(paid >= credit > 0 and selected_paid > 0, "Treatment did not reach selected and peer payment")
    elif record["passed"]:
        require(record["invalidateAfterSelectedPayment"] and record["invalidated"] and record["rejectionVerified"]
                and record["rejectedAfterPayment"] and selected_paid > 0 and paid > 0
                and "The CLR probe lifetime is no longer admitted." in record["failure"]
                and record["pauses"][-1]["Resumed"] is None, "Expected selected invalidation was not verified")
    return dict(samples=len(record["sourceSamples"]), credit=credit, paid=paid, selectedPaid=selected_paid,
                selectedPayments=len(record["selectedPayments"]), pauses=len(record["pauses"]),
                completed=record["completed"], casePassed=record["passed"], childCpu=child_cpu)


def verify_case(record, name):
    require(name in ("0", "50", "invalidate-after-selected-payment"), "Unknown selected-payment case")
    require(record["invalidateAfterPayment"] is False, "Selected case uses peer invalidation")
    require(record["invalidateAfterSelectedPayment"] is (name == "invalidate-after-selected-payment")
            and record["percent"] == (0 if name == "0" else 50), "Selected case identity differs")
    return verify_record(record)


def main(directory, output=None):
    results = []
    for name in ("0", "50", "invalidate-after-selected-payment"):
        try:
            record = json.loads((directory / f"{name}.json").read_text(encoding="utf-8-sig"))
            results.append(dict(case=name, evidenceVerified=True, **verify_case(record, name)))
        except (KeyError, TypeError, ValueError, OSError, IndexError) as error:
            results.append(dict(case=name, evidenceVerified=False, error=f"{type(error).__name__}: {error}"))
    passed = all(item["evidenceVerified"] and item.get("casePassed") is True for item in results)
    (output or directory / "analysis.json").write_text(json.dumps(dict(passed=passed, cases=results), indent=2), encoding="utf-8")
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main(pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]) if len(sys.argv) > 2 else None))
