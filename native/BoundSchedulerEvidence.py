from AnalyzeBoundSampledPayment import count, require
from AnalyzeScheduler import parse_capture


def clock_anchor(value, frequency):
    require(isinstance(value, dict) and value["Failure"] is None, "Scheduler clock observation failed or is missing")
    before, after, native, reported = (count(value[key]) for key in ("Before", "After", "Nanoseconds", "Frequency"))
    require(0 < before <= after and native > 0 and reported == frequency > 0, "Invalid scheduler clock bracket")
    return native * frequency - after * 1000000000, native * frequency - before * 1000000000


def verify_bound_scheduler(record):
    enabled = record.get("schedulerTiming", False)
    require(type(enabled) is bool, "Scheduler activation is not Boolean")
    capture, owners = record.get("schedulerCapture"), record.get("schedulerOwners")
    if not enabled:
        require(capture is None and owners is None, "Disabled scheduler contains evidence")
        return dict(enabled=False)
    require(record.get("selectedPaymentEnabled") is True and record.get("peerProgressEnabled") is True,
            "Bound scheduler requires selected payment and peer progress")
    require(isinstance(capture, dict) and capture["Stopped"] is True and capture["Failures"] == [],
            "Scheduler completion failed or is incomplete")
    require(capture["ClockDomain"] == "linux-clock-monotonic-nanoseconds", "Unsupported scheduler clock domain")
    frequency = count(record["timestampFrequency"])
    begin, end = capture["Begin"], capture["End"]
    first, last = clock_anchor(begin, frequency), clock_anchor(end, frequency)
    require(begin["After"] <= end["Before"] and begin["Nanoseconds"] <= end["Nanoseconds"],
            "Scheduler clock observations are unordered")
    lower, upper = max(first[0], last[0]), min(first[1], last[1])
    require(lower <= upper, "Scheduler clock-rate model is inconsistent")
    streams = capture["Captures"]
    require(isinstance(streams, list) and len(streams) == 3 and isinstance(owners, list) and len(owners) == 3,
            "Expected three scheduler streams and owners")
    identities, summaries = [], []
    for stream, owner in zip(streams, owners):
        require(isinstance(stream, dict) and isinstance(owner, dict), "Scheduler stream or owner is missing")
        identity = tuple(count(stream[key]) for key in ("ProcessId", "ThreadId"))
        require(all(0 < value <= 2147483647 for value in identity) and
                identity == tuple(count(owner[key]) for key in ("Process", "Thread")), "Scheduler owner identity differs")
        identities.append(identity)
        for key in ("EnableBefore", "EnableAfter", "DisableBefore", "DisableAfter", "FinalLost"):
            count(stream[key])
        require(isinstance(stream["Records"], list) and isinstance(stream["Failures"], list), "Invalid scheduler record lists")
        for item in stream["Records"]:
            for key in ("Kind", "Flags", "TimestampNanoseconds", "Lost"):
                count(item[key])
        parsed = parse_capture(stream)
        require(stream["EnableAfter"] <= begin["Nanoseconds"] <= end["Nanoseconds"] <= stream["DisableBefore"],
                "Scheduler anchors lie outside assured collection")
        summaries.append({key: parsed[key] for key in ("threadId", "records", "leftCensored", "rightCensored")})
    require(len(set(identities)) == 3 and len({identity[0] for identity in identities}) == 1,
            "Scheduler owners are duplicated or cross-process")
    require(owners[2] == record.get("peerOwner"), "Scheduler peer differs from progress owner")
    for sample in record["sourceSamples"]:
        require((sample["ProcessId"], sample["ThreadId"]) == identities[1], "Scheduler selected owner differs from samples")
        require(streams[1]["EnableAfter"] <= count(sample["Timestamp"]) <= streams[1]["DisableBefore"],
                "Source sample lies outside assured scheduler collection")
    return dict(enabled=True, clockDomain=capture["ClockDomain"], frequency=frequency,
                offsetLowerNumerator=lower, offsetUpperNumerator=upper, offsetDenominator=frequency,
                streams=summaries)


def native_window(start, finish, clock):
    require(0 < count(start) <= count(finish), "Invalid managed scheduler window")
    frequency = clock["frequency"]
    low, high = clock["offsetLowerNumerator"], clock["offsetUpperNumerator"]
    def floor(value):
        return value // frequency
    def ceiling(value):
        return -(-value // frequency)
    result = dict(earliestStart=floor(start * 1000000000 + low), latestStart=ceiling(start * 1000000000 + high),
                  earliestEnd=floor(finish * 1000000000 + low), latestEnd=ceiling(finish * 1000000000 + high),
                  durationUpperNanoseconds=ceiling((finish - start) * 1000000000))
    for value in result.values():
        count(value)
    require(result["earliestStart"] > 0, "Scheduler mapping reached a nonpositive timestamp")
    return result
