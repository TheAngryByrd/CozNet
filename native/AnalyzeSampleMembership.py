import argparse
import hashlib
import json
import math
import pathlib
from dataclasses import dataclass

from AnalyzeCpuCounters import SampleCreditEvidence, parse_capture, parse_sample_credits, number, require, timestamp


POLICY = "linux-monotonic-observed-membership-v1"
ASSUMPTION = "No unobserved fast-timekeeper anomaly changes membership."


@dataclass(frozen=True)
class ObservedMembership:
    start: tuple
    end: tuple
    samples: tuple
    failures: tuple
    closed: bool


def classify(evidence, value):
    start, end = evidence.start, evidence.end
    if value < start[0]:
        return "Before"
    if value >= end[1]:
        return "After"
    if start[1] <= value < end[0]:
        return "Inside"
    return "BoundaryAmbiguous"


def parse_membership(data):
    capture = parse_capture(data)
    clock = data["sampleClock"]
    source, metadata = clock["source"], clock["metadata"]
    require(number(source["Domain"], "clock domain") == 1 and
            number(source["UnitsPerSecond"], "source frequency") == 1000000000,
            "Unsupported source clock domain or units")
    require(metadata["backend"] == "linux-perf" and metadata["clock"] == "CLOCK_MONOTONIC" and
            number(metadata["clockId"], "clock ID") == 1 and
            number(metadata["attributeSize"], "attribute size") == 96,
            "Unsupported perf clock configuration")
    require(metadata["runtime"] == ".NET 10.0.0" and metadata["architecture"] == "X64",
            "Unsupported runtime clock path")
    for field in ("kernel", "operatingSystem"):
        require(type(metadata[field]) is str and bool(metadata[field].strip()), f"Missing {field}")
    require(capture.frequency == 1000000000 and
            number(metadata["timestampFrequency"], "metadata frequency") == capture.frequency,
            "Identity conversion requires nanosecond managed timestamps")
    require(clock["observedMappingConsistent"] is True and clock["rejection"] is None and
            clock["errorBoundEstablished"] is False, "Clock consistency evidence rejected or unsupported")
    readings = []
    contexts = []
    for name in ("start", "end"):
        raw = clock[name]
        require(raw["Error"] is None, "Clock reading retained an error")
        require(number(raw["TimestampFrequency"], "clock frequency") == capture.frequency,
                "Clock frequencies differ")
        require(type(raw["MonotonicOffsetNanoseconds"]) is int and raw["MonotonicOffsetNanoseconds"] == 0,
                "Missing zero namespace offset")
        context = raw["ContextIdentity"]
        require(type(context) is str and bool(context.strip()), "Missing clock context")
        contexts.append(context)
        before, native, after = (timestamp(raw[key]) for key in ("Before", "NativeNanoseconds", "After"))
        require(before <= native <= after, "Native clock lies outside its managed bracket")
        readings.append((before, after))
    require(contexts[0] == contexts[1], "Clock context changed")
    require(readings[0][1] <= capture.start[0] and capture.end[1] <= readings[1][0],
            "Clock readings do not enclose frozen endpoints")
    samples = []
    for index, raw in enumerate(data["samples"]):
        value = timestamp(raw["Sample"]["Timestamp"])
        receipt = timestamp(raw["Timestamp"])
        require(value <= receipt, f"Source timestamp exceeds receipt at record {index}")
        samples.append((index, value, receipt, raw["Sample"]["PeriodNanoseconds"],
                        raw["Sample"]["Method"] in data["records"]["SelectedSymbols"], raw["Accepted"]))
    return ObservedMembership(capture.start, capture.end, tuple(samples), capture.failures, capture.closed)


@dataclass(frozen=True)
class RecordedCreditMembership:
    membership: ObservedMembership
    replay: SampleCreditEvidence
    credits: tuple


def parse_credit_membership(data):
    membership = parse_membership(data)
    replay = parse_sample_credits(data)
    return RecordedCreditMembership(membership, replay, tuple(raw["Credit"] for raw in data["samples"]))


def report_credit_membership(evidence):
    groups = {}
    credits = dict.fromkeys(("Before", "Inside", "After", "BoundaryAmbiguous"), 0)
    for sample, credit in zip(evidence.membership.samples, evidence.credits):
        _, source, receipt, period, selected, accepted = sample
        source_membership = classify(evidence.membership, source)
        receipt_membership = classify(evidence.membership, receipt)
        key = source_membership, receipt_membership, accepted, selected
        group = groups.setdefault(key, {"sourceMembership": source_membership, "receiptMembership": receipt_membership,
                                       "accepted": accepted, "selected": selected, "samples": 0,
                                       "nominalNanoseconds": 0, "recordedCreditNanoseconds": 0})
        group["samples"] += 1
        group["nominalNanoseconds"] += period
        group["recordedCreditNanoseconds"] += credit
        credits[source_membership] += credit
    return {"reconciled": True, "percent": evidence.replay.percent,
            "sampleAccountingGroups": [groups[key] for key in sorted(groups)],
            "recordedCreditBySourceMembership": credits,
            "recordedCreditInObservedWindow": {"lower": credits["Inside"],
                                               "upper": credits["Inside"] + credits["BoundaryAmbiguous"]},
            "replayedCreditNanoseconds": evidence.replay.credit_nanoseconds,
            "eligibilityTransitionsVerified": False, "unreceivedCreditBoundEstablished": False,
            "predictionAccuracyEstablished": False}


def analyze_membership(data, include_credits=False):
    result = {"policy": POLICY, "assumption": ASSUMPTION, "comparisonDomainAccepted": False,
              "reasons": [], "observations": [], "membershipCounts": None, "membershipTotals": None,
              "physicalClockAccuracyEstablished": False, "exactEventOrderingEstablished": False,
              "predictionAccuracyEstablished": False}
    try:
        credit_evidence = parse_credit_membership(data) if include_credits else None
        evidence = credit_evidence.membership if credit_evidence is not None else parse_membership(data)
        counts = dict.fromkeys(("Before", "Inside", "After", "BoundaryAmbiguous"), 0)
        observations = []
        totals = {name: {"lower": 0, "upper": 0} for name in
                  ("samples", "nominalNanoseconds", "selectedSamples", "selectedNominalNanoseconds")}
        for index, value, receipt, period, selected, accepted in evidence.samples:
            membership = classify(evidence, value)
            counts[membership] += 1
            observations.append({"recordIndex": index, "sourceTimestamp": value, "receiptTimestamp": receipt,
                                 "nominalNanoseconds": period, "selected": selected, "accepted": accepted,
                                 "membership": membership})
            if membership in ("Inside", "BoundaryAmbiguous"):
                for name, amount in (("samples", 1), ("nominalNanoseconds", period),
                                     ("selectedSamples", int(selected)),
                                     ("selectedNominalNanoseconds", period if selected else 0)):
                    totals[name]["upper"] += amount
                    if membership == "Inside":
                        totals[name]["lower"] += amount
        result.update(comparisonDomainAccepted=True, observations=observations, membershipCounts=counts,
                      membershipTotals=totals, startBracket=list(evidence.start), endBracket=list(evidence.end))
        if credit_evidence is not None:
            result["creditMembership"] = report_credit_membership(credit_evidence)
    except (ValueError, KeyError, TypeError, IndexError) as error:
        result["reasons"] = [str(error)]
    if include_credits:
        result.setdefault("creditMembership", None)
    if type(data) is dict:
        result.update(clockEvidence=data.get("sampleClock"), measurementEvidence=data.get("measurementCapture"),
                      rawSamples=data.get("samples"),
                      recordedWorkloadFailures=data.get("failures"), nativeSessionClosed=data.get("closed"))
    return result


def reject_constant(value):
    raise ValueError(f"Non-finite JSON number: {value}")


def finite_float(value):
    parsed = float(value)
    require(math.isfinite(parsed), f"Non-finite JSON number: {value}")
    return parsed


def main():
    parser = argparse.ArgumentParser(description="Classify recorded sample times without establishing prediction accuracy.")
    parser.add_argument("capture", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    parser.add_argument("--sample-credits", action="store_true", help="Reconcile and partition recorded sample credits.")
    args = parser.parse_args()
    content = None
    try:
        content = args.capture.read_bytes()
        result = analyze_membership(json.loads(content.decode("utf-8-sig"), parse_constant=reject_constant,
                                               parse_float=finite_float), include_credits=args.sample_credits)
    except (OSError, ValueError) as error:
        result = analyze_membership(None, include_credits=args.sample_credits)
        result["reasons"] = [str(error)]
    result.update(inputFile=args.capture.name, inputSha256=hashlib.sha256(content).hexdigest() if content is not None else None,
                  analyzerSha256=hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest(),
                  counterParserSha256=hashlib.sha256(pathlib.Path(__file__).with_name("AnalyzeCpuCounters.py").read_bytes()).hexdigest())
    try:
        with args.output.open("x", encoding="utf-8") as stream:
            json.dump(result, stream, indent=2, allow_nan=False)
            stream.write("\n")
    except OSError as error:
        parser.exit(1, f"Cannot create membership report: {error}\n")
    return 0 if result["comparisonDomainAccepted"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
