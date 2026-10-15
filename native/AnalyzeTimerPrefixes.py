import argparse
import hashlib
import json
import pathlib
import uuid
from dataclasses import dataclass

from AnalyzeCpuCounters import boolean, number, require, timestamp
from AnalyzeSampleMembership import finite_float, reject_constant


def wait_value(value):
    return number(value, "confirmed wait", maximum=9223372036854775807)


def lease_identity(value):
    require(type(value) is str, "Invalid lease identity")
    parsed = uuid.UUID(value)
    require(parsed.int != 0, "Empty lease identity")
    return str(parsed)


@dataclass(frozen=True)
class Child:
    waited: int
    captured: int
    started: int
    finished: int


@dataclass(frozen=True)
class Journal:
    slot: int
    command: int
    generation: int
    lease: str
    children: tuple | None
    captured: int | None
    settled: bool
    cancelled: bool
    release_attempted: bool
    failure: str | None


def parse_journal(raw, archived):
    slot = number(raw["Slot"], "slot", maximum=1)
    command = number(raw["Command"], "command", 1)
    generation = number(raw["Generation"], "generation", maximum=4294967295)
    lease = lease_identity(raw["Lease"])
    budget = number(raw["Budget"], "lease budget", 1, 2000000)
    settled, cancelled, released = (boolean(raw[key], key) for key in
                                   ("LedgerSettled", "CancellationRequested", "ReleaseAttempted"))
    require(not released or settled, "Release attempt has no ledger settlement")
    failure = raw["Failure"]
    require(failure is None or type(failure) is str and bool(failure), "Invalid journal failure")
    started = timestamp(raw["StartedTimestamp"])
    observed = timestamp(raw["ObservedTimestamp"])
    accepted = raw["Accepted"]
    children, captured = None, None
    if accepted is None:
        require(not archived and not settled and not released, "Settled journal has no accepted observation")
    else:
        require(number(raw["SubmissionStatus"], "submission status") == 0 and generation > 0,
                "Accepted journal has no successful generation binding")
        require(observed >= started, "Journal observation precedes submission")
        native = accepted["Raw"]
        header = native["Header"]
        require(number(header["Generation"], "header generation") == generation and
                number(header["Command"], "header command") == command and
                number(header["Budget"], "header budget") == budget, "Accepted journal identity differs")
        require(all(number(header[key], key) == 0 for key in ("Error", "TransportError", "BackendError")),
                "Accepted journal contains native failure")
        visibility = number(header["Visibility"], "visibility", maximum=3)
        stop = number(header["Stop"], "stop", maximum=4)
        delivery = number(header["MayHaveDelivery"], "delivery", maximum=1)
        require((stop == 0 and delivery == 1) if visibility == 0 else stop != 0,
                "Journal visibility contradicts stop state")
        require(visibility < 2 or delivery == 0, "Quiescent journal permits delivery")
        require(settled == (visibility >= 2), "Ledger settlement differs from accepted visibility")
        require(not archived or visibility == 3 and settled and released, "Archived journal was not retired and released")
        captured = number(header["Captured"], "native capture", 1)
        clock = native["Clock"]
        require(number(clock["Status"], "native clock status") == 0 and
                number(clock["Nanoseconds"], "native receipt") >= captured, "Invalid native receipt clock")
        raw_children, received = native["Children"], accepted["Received"]
        require(type(raw_children) is list and type(received) is list and
                len(raw_children) == len(received) == number(header["Children"], "child count", maximum=100000),
                "Accepted child inventories differ")
        children, previous = [], None
        total = 0
        for index, (child, receipt) in enumerate(zip(raw_children, received)):
            saved = receipt["Raw"]
            for key in ("Ordinal", "Requested", "Armed", "Started", "Finished", "Waited", "WaitStatus"):
                require(number(child[key], key) == number(saved[key], key), "Received child differs from raw capture")
            require(number(child["Ip"], "instruction pointer", 1) == number(saved["InstructionPointer"], "received IP", 1)
                    and number(child["Valid"], "native validity") == 1 and saved["Valid"] is True,
                    "Invalid accepted child identity or validity")
            armed, begin, end = (number(child[key], key) for key in ("Armed", "Started", "Finished"))
            waited = wait_value(child["Waited"])
            require(number(child["Ordinal"], "ordinal") == index + 1 and armed <= begin <= end,
                    "Child ordinal or clock order differs")
            number(child["Requested"], "requested wait", 1, 50000)
            number(child["WaitStatus"], "wait status", maximum=1)
            require(waited <= end - begin <= 5000000, "Child wait or handler duration exceeds its bound")
            if previous is not None:
                require(previous[0] <= armed <= previous[1] <= begin, "Timer continuation order differs")
            first = number(receipt["FirstCaptured"], "first capture", 1)
            upper = number(receipt["FirstManagedReceiptUpper"], "first receipt", 1)
            require(end <= first <= captured and first <= upper <= clock["Nanoseconds"], "Invalid first receipt ordering")
            require(number(receipt["Timing"]["UpperNanoseconds"], "receipt bound") == upper - armed <= 100000000,
                    "First receipt bound differs or exceeds its limit")
            children.append(Child(waited, first, begin, end))
            total += waited
            previous = begin, end
        require(wait_value(header["Measured"]) == total, "Native aggregate differs from child waits")
        require(stop != 1 or bool(children) and total >= budget and raw_children[-1]["WaitStatus"] == 0,
                "Completed journal has not paid its budget")
        require(stop != 2 or bool(children) and children[-1].waited == 0 and raw_children[-1]["WaitStatus"] == 0,
                "Deferred journal has no zero-wait final child")
        require(stop != 3 or cancelled, "Cancelled journal has no managed intent")
        require(stop != 4 or bool(children) and raw_children[-1]["WaitStatus"] == 1,
                "Interrupted journal has no interrupted final child")
        require(visibility != 0 or total < budget, "Active journal has already paid its budget")
        children = tuple(children)
    return Journal(slot, command, generation, lease, children, captured, settled, cancelled, released, failure)


@dataclass(frozen=True)
class WorkerPrefix:
    released: int
    active: int
    journal: Journal | None
    accepted_count: int
    captured: int | None
    flags: tuple
    failure: str | None


@dataclass(frozen=True)
class BoundaryPrefix:
    count: int
    workers: tuple


@dataclass(frozen=True)
class PrefixCapture:
    start: BoundaryPrefix
    end: BoundaryPrefix
    journals: tuple
    archived_count: int
    failures: tuple
    closed: bool


def parse_prefix_capture(data):
    raw_archived, raw_pending = data["journals"], data["pending"]
    require(type(raw_archived) is list and len(raw_archived) <= 100000 and type(raw_pending) is list and len(raw_pending) <= 2,
            "Invalid journal inventory")
    archived = tuple(parse_journal(raw, True) for raw in raw_archived)
    pending = tuple(parse_journal(raw, False) for raw in raw_pending)
    journals = archived + pending
    require(len({j.command for j in journals}) == len(journals) and len({j.lease for j in journals}) == len(journals)
            and len({j.slot for j in pending}) == len(pending), "Duplicate journal identity or pending slot")
    bound = [(j.slot, j.generation) for j in journals if j.generation != 0]
    require(len(set(bound)) == len(bound), "Reused generation within a slot")
    for slot in (0, 1):
        generations = [j.generation for j in sorted(journals, key=lambda j: j.command)
                       if j.slot == slot and j.generation != 0]
        require(all(a < b for a, b in zip(generations, generations[1:])), "Slot generation order regressed")
    closed = boolean(data["closed"], "closure")
    require(not closed or not pending, "Closed session retains pending ownership")
    by_command = {j.command: j for j in journals}
    capture = data["measurementCapture"]
    require(capture["status"] == "Complete", "Frozen measurement is incomplete")
    frequency = number(data["timestampFrequency"], "timestamp frequency", 1, 9223372036854775807)
    boundaries, previous_after = [], 0
    for name in ("start", "end"):
        raw = capture[name]
        before, after = timestamp(raw["BeforeTimestamp"]), timestamp(raw["AfterTimestamp"])
        require(previous_after <= before <= after and number(raw["TimestampFrequency"], "boundary frequency") == frequency,
                "Frozen clock brackets or frequencies differ")
        previous_after = after
        accounting = raw["Accounting"]
        timer = accounting["Timer"]
        count = number(timer["ReleasedJournalCount"], "archived prefix count", maximum=len(archived))
        released_commands = {j.command for j in archived[:count]}
        workers = []
        for slot, worker_name in enumerate(("Selected", "Peer")):
            saved, state = accounting[worker_name], timer[worker_name]
            released = wait_value(state["ReleasedWait"])
            require(released == wait_value(saved["ReleasedWait"]) ==
                    sum(sum(c.waited for c in j.children) for j in archived[:count] if j.slot == slot),
                    "Frozen released waits differ from archived prefix")
            active = wait_value(saved["ActiveWait"])
            owned = state["Pending"]
            journal, accepted_count = None, 0
            native_capture, flags, failure = None, (), None
            if owned is None:
                require(saved["Pending"] is None and active == 0, "Unowned worker has active accounting")
            else:
                command = number(owned["Command"], "pending command", 1)
                generation = number(owned["Generation"], "pending generation", maximum=4294967295)
                journal = by_command.get(command)
                require(journal is not None and journal.slot == slot and journal.generation == generation
                        and journal.lease == lease_identity(owned["Lease"]) and command not in released_commands,
                        "Frozen pending identity has no matching owned journal")
                require(number(saved["Pending"]["Command"], "worker command") == command and
                        number(saved["Pending"]["Generation"], "worker generation") == generation,
                        "Frozen worker and timer identities differ")
                accepted_count = number(owned["AcceptedChildren"], "accepted prefix count", maximum=100000)
                children = journal.children or ()
                require(accepted_count <= len(children) and active == wait_value(owned["ConfirmedWait"]) ==
                        sum(c.waited for c in children[:accepted_count]), "Frozen active wait differs from accepted prefix")
                native_capture = owned["LastAcceptedCapture"]
                failure = owned["Failure"]
                require(failure is None or type(failure) is str and bool(failure), "Invalid frozen failure")
                require(failure is None or journal.failure is not None, "Frozen failure disappeared from final journal")
                if native_capture is None:
                    require(accepted_count == active == 0 and owned["LedgerSettled"] is False,
                            "Unobserved prefix contains accepted accounting")
                else:
                    native_capture = number(native_capture, "frozen native capture", 1)
                    require(journal.captured is not None and native_capture <= journal.captured and
                            all(c.captured <= native_capture for c in children[:accepted_count]),
                            "Frozen accepted capture contradicts final prefix")
                for field, final in (("LedgerSettled", journal.settled), ("CancellationRequested", journal.cancelled),
                                     ("ReleaseAttempted", journal.release_attempted)):
                    require(not boolean(owned[field], field) or final, "Frozen ownership flag regressed")
                require(not owned["ReleaseAttempted"] or owned["LedgerSettled"], "Frozen release lacks settlement")
                require(native_capture is None or all(c.captured >= native_capture for c in children[accepted_count:]),
                        "Frozen prefix omits a child observed before its accepted capture")
                flags = tuple(owned[field] for field in ("LedgerSettled", "CancellationRequested", "ReleaseAttempted"))
                require(not flags[0] or accepted_count == len(children), "Settled prefix has a later accepted suffix")
            workers.append(WorkerPrefix(released, active, journal, accepted_count, native_capture, flags, failure))
        boundaries.append(BoundaryPrefix(count, tuple(workers)))
    start, end = boundaries
    require(start.count <= end.count and all(a.released + a.active <= b.released + b.active
            for a, b in zip(start.workers, end.workers)), "Frozen accepted wait or inventory regressed")
    for a, b in zip(start.workers, end.workers):
        if a.journal is None:
            continue
        if b.journal is not None and a.journal.command == b.journal.command:
            require(a.accepted_count <= b.accepted_count
                    and (a.captured is None or b.captured is not None and a.captured <= b.captured)
                    and all(not old or new for old, new in zip(a.flags, b.flags))
                    and (a.failure is None or b.failure is not None), "Frozen pending prefix regressed")
        else:
            require(a.journal in archived[start.count:end.count], "Start ownership disappeared without archival")
    end_owned = {w.journal.command for w in end.workers if w.journal is not None}
    require({j.command for j in journals[end.count:]} == end_owned, "Final inventory contains an operation absent at the endpoint")
    failures = data["failures"]
    require(type(failures) is list and all(type(value) is str for value in failures), "Invalid workload failures")
    return PrefixCapture(start, end, journals, len(archived), tuple(failures), closed)


def analyze_prefixes(data):
    result = {"reconciled": False, "reasons": [], "workers": None, "nativeEligibilityVerified": False,
              "nativeTailBoundEstablished": False, "predictionAccuracyEstablished": False}
    try:
        capture = parse_prefix_capture(data)
        workers = []
        for slot, (start, end) in enumerate(zip(capture.start.workers, capture.end.workers)):
            children = end.journal.children if end.journal is not None else ()
            suffix = children[end.accepted_count:] if children is not None else None
            workers.append({"slot": slot, "startAcceptedWait": start.released + start.active,
                            "endAcceptedWait": end.released + end.active,
                            "acceptedWaitDelta": end.released + end.active - start.released - start.active,
                            "pendingCommand": end.journal.command if end.journal else None,
                            "frozenAcceptedChildren": end.accepted_count,
                            "startFailure": start.failure, "endFailure": end.failure,
                            "laterAcceptedChildren": len(suffix) if suffix is not None else None,
                            "laterAcceptedWait": sum(c.waited for c in suffix) if suffix is not None else None})
        result.update(reconciled=True, workers=workers, startArchivedJournals=capture.start.count,
                      endArchivedJournals=capture.end.count, finalArchivedJournals=capture.archived_count,
                      journalFailures=[{"slot": j.slot, "command": j.command, "failure": j.failure}
                                       for j in capture.journals if j.failure is not None])
    except (ValueError, KeyError, TypeError, IndexError, AttributeError) as error:
        result["reasons"] = [str(error)]
    if type(data) is dict:
        result.update(recordedWorkloadFailures=data.get("failures"), nativeSessionClosed=data.get("closed"))
    return result


def main():
    parser = argparse.ArgumentParser(description="Reconcile frozen controller prefixes without establishing native tail bounds.")
    parser.add_argument("capture", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    args = parser.parse_args()
    content = None
    try:
        content = args.capture.read_bytes()
        result = analyze_prefixes(json.loads(content.decode("utf-8-sig"), parse_float=finite_float, parse_constant=reject_constant))
    except (OSError, ValueError) as error:
        result = analyze_prefixes(None)
        result["reasons"] = [str(error)]
    result.update(inputFile=args.capture.name, inputSha256=hashlib.sha256(content).hexdigest() if content is not None else None,
                  sourceHashes={name: hashlib.sha256(pathlib.Path(__file__).with_name(name).read_bytes()).hexdigest()
                                for name in ("AnalyzeTimerPrefixes.py", "AnalyzeCpuCounters.py", "AnalyzeSampleMembership.py")})
    try:
        with args.output.open("x", encoding="utf-8") as stream:
            json.dump(result, stream, indent=2, allow_nan=False)
            stream.write("\n")
    except OSError as error:
        parser.exit(1, f"Cannot create timer prefix report: {error}\n")
    return 0 if result["reconciled"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
