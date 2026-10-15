import argparse
import hashlib
import json
import pathlib
from dataclasses import dataclass


def require(condition, message):
    if not condition:
        raise ValueError(message)


def number(value, name, minimum=0, maximum=18446744073709551615):
    require(type(value) is int and minimum <= value <= maximum, f"Invalid {name}")
    return value


def timestamp(value):
    return number(value, "managed timestamp", maximum=9223372036854775807)


def boolean(value, name):
    require(type(value) is bool, f"Invalid {name}")
    return value


def identity(value):
    require(type(value) is str and bool(value.strip()), "Missing counter source identity")
    return value


@dataclass(frozen=True)
class Reading:
    source: str
    epoch: int
    enabled: bool
    before: int
    after: int
    count: int


def parse_reading(raw, source, frequency):
    require(raw["Error"] is None, "Counter read retained an error")
    require(identity(raw["SourceIdentity"]) == source, "Counter event identities differ")
    require(number(raw["TimestampFrequency"], "counter frequency", 1) == frequency, "Counter frequencies differ")
    before, after = timestamp(raw["Before"]), timestamp(raw["After"])
    require(before <= after, "Counter read clock regressed")
    return Reading(source, number(raw["Epoch"], "counter epoch"), boolean(raw["Enabled"], "counter state"),
                   before, after, number(raw["CpuNanoseconds"], "cumulative CPU count"))


@dataclass(frozen=True)
class Transition:
    epoch: int
    enabling: bool
    before: int
    after: int


@dataclass(frozen=True)
class Sample:
    count: int
    receipt: int
    accepted: bool


@dataclass(frozen=True)
class CounterCapture:
    frequency: int
    start: tuple
    end: tuple
    readings: tuple
    transitions: tuple
    samples: tuple
    final: Reading
    failures: tuple
    closed: bool


def parse_capture(data):
    frequency = number(data["timestampFrequency"], "timestamp frequency", 1, 9223372036854775807)
    capture = data["measurementCapture"]
    require(capture["status"] == "Complete", "Measurement capture is incomplete")
    boundaries = []
    for name in ("start", "end"):
        raw = capture[name]
        require(number(raw["TimestampFrequency"], "boundary frequency", 1) == frequency, "Boundary frequencies differ")
        before, after = timestamp(raw["BeforeTimestamp"]), timestamp(raw["AfterTimestamp"])
        require(before <= after, "Measurement boundary clock regressed")
        boundaries.append((before, after))
    start, end = boundaries
    require(start[1] <= end[0], "Measurement endpoints overlap or regress")
    counters = data["cpuCounters"]
    source = counters["source"]
    source_id = identity(source["SourceIdentity"])
    require(source["Units"] == "task-clock-nanoseconds", "Unknown CPU counter units")
    require(source["Overflowed"] is False and source["UnrecordedCleanup"] is False,
            "Counter journal is incomplete")
    require(number(source["FailedReads"], "failed counter reads") == 0 and source["FirstReadFailure"] is None,
            "Counter source retained read failures")
    readings = tuple(parse_reading(counters[name], source_id, frequency)
                     for name in ("startBefore", "startAfter", "endBefore", "endAfter"))
    for previous, current in zip(readings, readings[1:]):
        require(previous.after <= current.before and previous.count <= current.count and previous.epoch <= current.epoch,
                "Counter readings overlap or regress")
    require(readings[0].after <= start[0] and start[1] <= readings[1].before and
            readings[2].after <= end[0] and end[1] <= readings[3].before,
            "Counter readings do not enclose the frozen endpoints")
    require(readings[0].epoch == 0 and not readings[0].enabled and readings[0].count == 0,
            "Counter capture did not start at a fresh disabled event")
    lower, upper = readings[2].count - readings[1].count, readings[3].count - readings[0].count
    saved = counters["enabledCountRange"]
    require(counters["rejection"] is None and number(saved["Lower"], "saved lower count") == lower and
            number(saved["Upper"], "saved upper count") == upper, "Saved count range differs from raw readings")

    raw_transitions = source["Transitions"]
    require(type(raw_transitions) is list and 0 < len(raw_transitions) <= 65536, "Invalid transition inventory")
    transitions = []
    enabled, epoch, previous_after = False, 0, 0
    for raw in raw_transitions:
        require(raw["Completed"] is True and raw["Error"] is None, "Counter transition failed")
        enabling = boolean(raw["Enabling"], "transition direction")
        require(enabling != enabled, "Counter transition does not change state")
        before, after = timestamp(raw["Before"]), timestamp(raw["After"])
        require(previous_after <= before <= after, "Counter transition brackets overlap or regress")
        require(number(raw["TimestampFrequency"], "transition frequency", 1) == frequency, "Transition frequencies differ")
        epoch += int(enabling)
        require(number(raw["Epoch"], "transition epoch") == epoch, "Counter transition epoch differs")
        transitions.append(Transition(epoch, enabling, before, after))
        enabled, previous_after = enabling, after
    require(source["Enabled"] is False and not enabled and number(source["Epoch"], "final epoch") == epoch,
            "Counter source did not finish disabled in the declared epoch")
    require(start[0] <= transitions[0].before <= transitions[0].after <= start[1],
            "The initial enable lies outside the start boundary")
    final = parse_reading(source["LastReading"], source_id, frequency)
    require(not final.enabled and final.epoch == epoch and final.before >= previous_after and
            (final == readings[3] or final.before >= readings[3].after) and final.count >= readings[3].count,
            "Final counter reading does not follow disabled source completion")
    positions = {(0, False): 0}
    positions.update({(transition.epoch, transition.enabling): index + 1 for index, transition in enumerate(transitions)})
    previous_position, previous_reading = 0, None
    for reading in (*readings, final):
        position = positions.get((reading.epoch, reading.enabled))
        require(position is not None and position >= previous_position, "Counter reading state differs from its transition history")
        require((position == 0 or transitions[position - 1].after <= reading.before) and
                (position == len(transitions) or reading.after <= transitions[position].before),
                "Counter reading overlaps a native transition")
        if previous_reading is not None and position == previous_position and not reading.enabled:
            require(reading.count == previous_reading.count, "A disabled counter advanced without an enable")
        previous_position, previous_reading = position, reading

    raw_samples = data["samples"]
    require(type(raw_samples) is list and len(raw_samples) <= 20000, "Invalid sample inventory")
    samples, ips, methods = [], set(), []
    previous_source_time = previous_count = previous_receipt = period_sum = 0
    target = None
    for raw in raw_samples:
        sample = raw["Sample"]
        process = number(sample["ProcessId"], "sample process", 1, 4294967295)
        thread = number(sample["ThreadId"], "sample thread", 1, 4294967295)
        if target is None:
            target = (process, thread)
        require(target == (process, thread), "Sample target identity changed")
        source_time = number(sample["Timestamp"], "source timestamp")
        count = number(sample["CumulativeCpuNanoseconds"], "sample cumulative CPU count")
        receipt = timestamp(raw["Timestamp"])
        require(previous_source_time <= source_time and previous_count <= count and previous_receipt <= receipt,
                "Sample timestamps or counters regressed")
        require(count <= final.count, "A sample counter exceeds the final disabled count")
        period_sum += number(sample["PeriodNanoseconds"], "nominal sample period", 1)
        ips.add(number(sample["InstructionPointer"], "instruction pointer", 1))
        method = sample["Method"]
        require(method is None or type(method) is str and bool(method), "Invalid sample method")
        methods.append(method)
        accepted = boolean(raw["Accepted"], "sample acceptance")
        if accepted:
            require(readings[1].after <= receipt <= readings[2].before and count <= readings[2].count,
                    "Accepted sample does not precede the frozen end counter")
        for reading in readings:
            if receipt <= reading.before:
                require(count <= reading.count, "A received sample exceeds a later counter read")
        samples.append(Sample(count, receipt, accepted))
        previous_source_time, previous_count, previous_receipt = source_time, count, receipt
    records = data["records"]
    require(all(number(records[key], key) == 0 for key in ("LostRecords", "LostSamples", "Throttles")),
            "Sample source retained loss or throttling")
    require(number(records["Samples"], "sample total") == len(samples) and
            number(records["DistinctInstructionPointers"], "instruction total") == len(ips) and
            number(records["RepresentedCpuNanoseconds"], "nominal period total") == period_sum,
            "Sample totals differ from raw records")
    selected = records["SelectedSymbols"]
    require(type(selected) is list and all(type(value) is str and value for value in selected) and
            len(set(selected)) == len(selected) and all(value in methods for value in selected), "Invalid selected symbol inventory")
    require(number(records["SelectedMethodSamples"], "selected sample total") == sum(method in selected for method in methods) and
            number(records["UnmappedSamples"], "unmapped sample total") == methods.count(None),
            "Sample location totals differ")
    require(number(capture["start"]["Accounting"]["AcceptedSampleSequence"], "initial accepted sequence") == 0 and
            number(capture["end"]["Accounting"]["AcceptedSampleSequence"], "final accepted sequence") == sum(s.accepted for s in samples),
            "Frozen accepted sequence differs from captured samples")
    failures = data["failures"]
    require(type(failures) is list and all(type(value) is str for value in failures), "Invalid workload failure inventory")
    return CounterCapture(frequency, start, end, readings, tuple(transitions), tuple(samples), final,
                          tuple(failures), boolean(data["closed"], "native session closure"))


def count_range(lower, upper):
    return {"lower": lower, "upper": upper}


def disabled_intervals(capture):
    intervals, disable = [], None
    for index, transition in enumerate(capture.transitions):
        if transition.enabling:
            intervals.append((disable, (index, transition)))
        else:
            disable = (index, transition)
    intervals.append((disable, None))
    result = []
    for disabled, enabled in intervals:
        earliest_start = disabled[1].before if disabled else capture.start[0]
        latest_start = disabled[1].after if disabled else capture.start[1]
        earliest_end = enabled[1].before if enabled else capture.end[0]
        latest_end = enabled[1].after if enabled else capture.end[1]
        lower = max(0, min(earliest_end, capture.end[0]) - max(latest_start, capture.start[1]))
        upper = max(0, min(latest_end, capture.end[1]) - max(earliest_start, capture.start[0]))
        if upper:
            result.append({"disableTransition": disabled[0] if disabled else None,
                           "enableTransition": enabled[0] if enabled else None, "lowerTicks": lower, "upperTicks": upper})
    return result


def report_capture(capture):
    a, b, c, d = capture.readings
    accepted = [sample for sample in capture.samples if sample.accepted]
    enabled_range = count_range(c.count - b.count, d.count - a.count)
    prefix = count_range(max(0, accepted[0].count - b.count), max(0, accepted[0].count - a.count)) if accepted else None
    suffix = count_range(c.count - accepted[-1].count, d.count - accepted[-1].count) if accepted else enabled_range
    gaps = disabled_intervals(capture)
    lower, upper = sum(gap["lowerTicks"] for gap in gaps), sum(gap["upperTicks"] for gap in gaps)
    return {"reconciled": True, "reasons": [], "enabledCountRange": enabled_range,
            "acceptedSamples": len(accepted), "unacceptedSamples": len(capture.samples) - len(accepted),
            "firstAcceptedCount": accepted[0].count if accepted else None,
            "lastAcceptedCount": accepted[-1].count if accepted else None,
            "countBeforeFirstAccepted": prefix, "countAfterLastAccepted": suffix,
            "disabledElapsed": {"lowerTicks": lower, "upperTicks": upper, "timestampFrequency": capture.frequency,
                                "lowerNanosecondsFloor": lower * 1000000000 // capture.frequency,
                                "upperNanosecondsCeiling": (upper * 1000000000 + capture.frequency - 1) // capture.frequency,
                                "intervals": gaps},
            "recordedWorkloadFailures": list(capture.failures), "nativeSessionClosed": capture.closed,
            "selectedCpuBoundEstablished": False, "missingSampleTailBoundEstablished": False,
            "disabledCpuBoundEstablished": False, "predictionAccuracyEstablished": False}


def analyze_counter_capture(data):
    try:
        return report_capture(parse_capture(data))
    except (ValueError, KeyError, TypeError, IndexError) as error:
        failures = data.get("failures") if type(data) is dict else None
        closed = data.get("closed") if type(data) is dict else None
        return {"reconciled": False, "reasons": [str(error)], "enabledCountRange": None,
                "countBeforeFirstAccepted": None, "countAfterLastAccepted": None, "disabledElapsed": None,
                "recordedWorkloadFailures": failures if type(failures) is list and all(type(value) is str for value in failures) else None,
                "nativeSessionClosed": closed if type(closed) is bool else None,
                "selectedCpuBoundEstablished": False, "missingSampleTailBoundEstablished": False,
                "disabledCpuBoundEstablished": False, "predictionAccuracyEstablished": False}


def main():
    parser = argparse.ArgumentParser(description="Reconcile one sampled timer capture without establishing prediction accuracy.")
    parser.add_argument("capture", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    args = parser.parse_args()
    content = None
    try:
        content = args.capture.read_bytes()
        result = analyze_counter_capture(json.loads(content.decode("utf-8-sig")))
    except (OSError, ValueError) as error:
        result = {"reconciled": False, "reasons": [str(error)], "predictionAccuracyEstablished": False}
    result.update(inputFile=args.capture.name, inputSha256=hashlib.sha256(content).hexdigest() if content is not None else None,
                  analyzerSha256=hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest())
    try:
        with args.output.open("x", encoding="utf-8") as stream:
            json.dump(result, stream, indent=2, allow_nan=False)
            stream.write("\n")
    except FileExistsError:
        parser.exit(1, "Counter analysis already exists. Retain the original output.\n")
    except OSError as error:
        parser.exit(1, f"Cannot write counter analysis: {error}\n")
    return 0 if result["reconciled"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
