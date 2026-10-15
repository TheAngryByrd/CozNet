import argparse
import hashlib
import json
import pathlib
from dataclasses import dataclass

from AnalyzeCpuCounters import require
from AnalyzeSampleMembership import ASSUMPTION, ObservedMembership, finite_float, parse_membership, reject_constant
from AnalyzeTimerPrefixes import Child, PrefixCapture, parse_prefix_capture


POLICY = "linux-monotonic-recorded-timer-waits-v1"


@dataclass(frozen=True)
class WaitRange:
    lower: int
    upper: int


def overlap(begin, end, window_begin, window_end):
    return max(0, min(end, window_end) - max(begin, window_begin))


def window_wait(child: Child, start, end):
    guaranteed = overlap(child.started, child.finished, start[1], end[0])
    possible = overlap(child.started, child.finished, start[0], end[1])
    outside = child.finished - child.started - guaranteed
    return WaitRange(max(0, child.waited - outside), min(child.waited, possible))


@dataclass(frozen=True)
class WorkerWaits:
    slot: int
    children: int
    waited: int
    window: WaitRange
    accepted_delta: int


@dataclass(frozen=True)
class ObservedTimerWaits:
    clock: ObservedMembership
    prefixes: PrefixCapture
    workers: tuple


def parse_timer_waits(data):
    prefixes = parse_prefix_capture(data)
    clock = parse_membership(data)
    require(prefixes.closed and prefixes.archived_count == len(prefixes.journals),
            "Observed wait ranges require a closed final journal inventory")
    workers = []
    for slot in (0, 1):
        children = tuple(child for journal in sorted(prefixes.journals, key=lambda item: item.command)
                         if journal.slot == slot for child in journal.children)
        require(all(a.finished <= b.started for a, b in zip(children, children[1:])),
                "Recorded handlers overlap or regress within one worker")
        ranges = tuple(window_wait(child, clock.start, clock.end) for child in children)
        start, end = prefixes.start.workers[slot], prefixes.end.workers[slot]
        delta = end.released + end.active - start.released - start.active
        workers.append(WorkerWaits(slot, len(children), sum(child.waited for child in children),
                                   WaitRange(sum(item.lower for item in ranges), sum(item.upper for item in ranges)), delta))
    for prefix, bracket in ((prefixes.start, clock.start), (prefixes.end, clock.end)):
        captures = [journal.captured for journal in prefixes.journals[:prefix.count]]
        captures.extend(worker.captured for worker in prefix.workers if worker.captured is not None)
        require(all(capture <= bracket[1] for capture in captures),
                "Frozen accounting contains a native capture after its endpoint")
    return ObservedTimerWaits(clock, prefixes, tuple(workers))


def analyze_timer_waits(data):
    result = dict(policy=POLICY, assumption=ASSUMPTION,
                  timerClockAssumption="Recorded timer intervals use the current Linux CLOCK_MONOTONIC backend.",
                  comparisonDomainAccepted=False, reasons=[], workers=None,
                  physicalClockAccuracyEstablished=False, exactEventOrderingEstablished=False,
                  nativeEligibilityVerified=False, nativeTailBoundEstablished=False,
                  predictionAccuracyEstablished=False)
    try:
        evidence = parse_timer_waits(data)
        result.update(comparisonDomainAccepted=True,
                      workers=[dict(slot=worker.slot, recordedChildren=worker.children,
                                    recordedWait=worker.waited,
                                    recordedWaitInObservedWindow=dict(lower=worker.window.lower, upper=worker.window.upper),
                                    frozenAcceptedWaitDelta=worker.accepted_delta) for worker in evidence.workers],
                      startBracket=evidence.clock.start, endBracket=evidence.clock.end)
    except (ValueError, KeyError, TypeError, IndexError, AttributeError) as error:
        result["reasons"] = [str(error)]
    if type(data) is dict:
        result.update(recordedWorkloadFailures=data.get("failures"), nativeSessionClosed=data.get("closed"))
    return result


def main():
    parser = argparse.ArgumentParser(description="Bound recorded timer wait overlap under the observed clock policy.")
    parser.add_argument("capture", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    args = parser.parse_args()
    content = None
    try:
        content = args.capture.read_bytes()
        result = analyze_timer_waits(json.loads(content.decode("utf-8-sig"),
                                               parse_float=finite_float, parse_constant=reject_constant))
    except (OSError, ValueError) as error:
        result = analyze_timer_waits(None)
        result["reasons"] = [str(error)]
    sources = ("AnalyzeTimerWaitMembership.py", "AnalyzeTimerPrefixes.py", "AnalyzeCpuCounters.py", "AnalyzeSampleMembership.py")
    result.update(inputFile=args.capture.name,
                  inputSha256=hashlib.sha256(content).hexdigest() if content is not None else None,
                  sourceHashes={name: hashlib.sha256(pathlib.Path(__file__).with_name(name).read_bytes()).hexdigest()
                                for name in sources})
    try:
        with args.output.open("x", encoding="utf-8") as stream:
            json.dump(result, stream, indent=2, allow_nan=False)
            stream.write("\n")
    except OSError as error:
        parser.exit(1, f"Cannot create timer wait report: {error}\n")
    return 0 if result["comparisonDomainAccepted"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
