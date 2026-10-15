import hashlib
import json
import math
import pathlib
import statistics
import sys
from dataclasses import dataclass
from fractions import Fraction
from AnalyzeCpuCounters import analyze_counter_capture


MODES = ("profile-zero", "profile-half", "physical-baseline", "physical-optimized")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def integer(value, name, minimum=0):
    require(type(value) is int and minimum <= value <= 18446744073709551615, f"Invalid {name}")
    return value


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


@dataclass(frozen=True)
class CalibrationEstimate:
    mean: float
    half_width: float
    sensitivity: float


def parse_calibration(root):
    plan = read(root / "calibration.json.plan.json")
    result = read(root / "calibration.json")
    journal = [json.loads(line) for line in (root / "calibration.json.blocks.jsonl").read_text(encoding="utf-8-sig").splitlines()]
    require(plan["protocol"] == result["protocol"] == "interleaved-control-v1", "Calibration protocol differs")
    require(plan["seed"] == 20261015 and plan["blocks"] == 8 and plan["roundsPerBlock"] == 100
            and plan["callsPerSegment"] == 500, "Calibration plan parameters differ")
    require(result["calibrationComplete"] is True and result["failures"] == [] and result["unexecutedBlocks"] == [],
            "Calibration is incomplete")
    require(all(result[field] is False for field in ("predictionAccuracyEstablished", "clockAccuracyEstablished", "overheadBoundEstablished")),
            "Calibration protocol contains unsupported accuracy claims")
    require(journal == result["blocks"] and len(journal) == len(plan["plan"]) == 9, "Calibration journal and result differ")
    require(result["analysis"]["Errors"] == [] and len(result["analysis"]["Estimate"]["Blocks"]) == 8, "Calibration estimate is incomplete")
    previous_cpu, previous_wall, frequency = None, None, None
    reductions, shifts = [], []
    for index, (declared, block) in enumerate(zip(plan["plan"], journal)):
        require(declared["Index"] == block["Index"] == index - 1
                and declared["Warmup"] is (index == 0) and block["Warmup"] is (index == 0), "Calibration block identity differs")
        require(len(declared["Steps"]) == len(block["Segments"]) == 300, "Calibration block is incomplete")
        totals = dict.fromkeys(("Baseline", "Optimized", "Control"), 0)
        for offset, (step, segment) in enumerate(zip(declared["Steps"], block["Segments"])):
            require(step == segment["Step"] and step["Round"] == offset // 3, "Calibration segment order differs")
            if offset % 3 == 0:
                require({item["Arithmetic"] for item in declared["Steps"][offset:offset + 3]} == set(totals), "Calibration round modes differ")
            raw = segment["Measurement"]
            require(raw["Error"] is None and integer(raw["CompletedBatches"], "calibration work") == 500
                    and integer(raw["Checksum"], "calibration checksum") == 24997500000, "Calibration work is incomplete")
            cpu_before = integer(raw["CpuBefore"], "calibration CPU start")
            cpu_after = integer(raw["CpuAfter"], "calibration CPU end")
            before = integer(raw["BeforeTimestamp"], "calibration wall start")
            after = integer(raw["AfterTimestamp"], "calibration wall end")
            ticks = integer(raw["Frequency"], "calibration frequency", 1)
            cpu, wall = cpu_after - cpu_before, after - before
            require(cpu > 0 and wall > 0 and cpu * ticks <= wall * 1000000000, "Calibration clock interval is invalid")
            if previous_cpu is not None:
                require(cpu_before >= previous_cpu and before >= previous_wall and ticks == frequency, "Calibration cumulative clocks differ")
            previous_cpu, previous_wall, frequency = cpu_after, after, ticks
            totals[step["Arithmetic"]] += cpu
        baseline, optimized, control = (totals[mode] for mode in ("Baseline", "Optimized", "Control"))
        require(0 < control < min(baseline, optimized), "Calibration control dominates method cost")
        if index:
            raw_reduction = 1 - optimized / baseline
            lower, upper = 1 - optimized / (baseline - control), 1 - (optimized - control) / baseline
            saved = result["analysis"]["Estimate"]["Blocks"][index - 1]
            require(saved["Index"] == index - 1 and (saved["BaselineCpu"], saved["OptimizedCpu"], saved["ControlCpu"])
                    == (baseline, optimized, control), "Calibration block totals differ")
            for name, expected in (("RawReduction", raw_reduction), ("SensitivityLower", lower), ("SensitivityUpper", upper)):
                require(type(saved[name]) in (int, float) and math.isclose(saved[name], expected, rel_tol=0, abs_tol=1e-12),
                        f"Calibration {name} differs")
            reductions.append(raw_reduction)
            shifts.append(max(raw_reduction - lower, upper - raw_reduction))
    mean, deviation = statistics.mean(reductions), statistics.stdev(reductions)
    half, shift = 2.364624251 * deviation / math.sqrt(8), max(shifts)
    values = {"MeanReduction": mean, "StandardDeviation": deviation, "IntervalHalfWidth": half,
              "MaxObservedOverheadShift": shift, "DistancePlusIntervalAndSensitivity": abs(mean - 0.5) + half + shift}
    for name, expected in values.items():
        value = result["analysis"]["Estimate"][name]
        require(type(value) in (int, float) and math.isclose(value, expected, rel_tol=0, abs_tol=1e-12), f"Calibration {name} differs")
    return CalibrationEstimate(mean, half, shift)


@dataclass(frozen=True)
class WindowCost:
    selected_progress: int
    peer_progress: int
    global_delta: int
    lower_seconds: Fraction
    upper_seconds: Fraction

    def report(self):
        return {"selectedProgress": self.selected_progress, "peerProgress": self.peer_progress,
                "measurementGlobalDeltaNanoseconds": self.global_delta,
                "effectiveSecondsLower": float(self.lower_seconds),
                "effectiveSecondsUpper": float(self.upper_seconds),
                "accountedSecondsPerBatchLower": float(self.lower_seconds / (self.selected_progress + 1)),
                "accountedSecondsPerBatchUpper": float(self.upper_seconds / (self.selected_progress - 1))}


def parse_window(data, mode):
    capture = data["measurementCapture"]
    require(capture["status"] == "Complete", "Measurement capture is incomplete")
    start, end = capture["start"], capture["end"]
    frequency = integer(data["timestampFrequency"], "timestamp frequency", 1)
    for boundary in (start, end):
        require(integer(boundary["TimestampFrequency"], "boundary frequency", 1) == frequency, "Boundary frequency differs")
        before = integer(boundary["BeforeTimestamp"], "boundary start")
        after = integer(boundary["AfterTimestamp"], "boundary end")
        require(before <= after, "Boundary clock regressed")
        account = boundary["Accounting"]
        for field in ("Global", "Credit", "AcceptedSampleSequence", "Epoch"):
            integer(account[field], field)
        for slot in ("Selected", "Peer"):
            worker = account[slot]
            for field in ("Progress", "ReleasedWait", "ActiveWait"):
                integer(worker[field], field)
            if worker["Pending"] is None:
                require(worker["ActiveWait"] == 0, "Active wait has no pending owner")
            else:
                integer(worker["Pending"]["Command"], "pending command", 1)
                integer(worker["Pending"]["Generation"], "pending generation")
    require(end["BeforeTimestamp"] > start["AfterTimestamp"], "Measurement window is nonpositive")
    a, b = start["Accounting"], end["Accounting"]
    for field in ("Global", "Credit", "AcceptedSampleSequence", "Epoch"):
        require(b[field] >= a[field], f"Regressed {field}")
    require(a["Global"] == a["Credit"] == a["AcceptedSampleSequence"] == 0 and a["Epoch"] == 1,
            "Measurement did not start with fresh accounting")
    progress = []
    for slot in ("Selected", "Peer"):
        require(a[slot]["Progress"] == a[slot]["ReleasedWait"] == a[slot]["ActiveWait"] == 0
                and a[slot]["Pending"] is None, "Worker did not start at the fresh release gate")
        require(b[slot]["ReleasedWait"] >= a[slot]["ReleasedWait"], "Released wait regressed")
        progress.append(integer(b[slot]["Progress"] - a[slot]["Progress"], "progress delta", 2 if slot == "Selected" else 1))
    global_delta = b["Global"] - a["Global"]
    if mode == "profile-half":
        require(global_delta > 0 and b["Credit"] > 0
                and sum(b[slot]["ReleasedWait"] + b[slot]["ActiveWait"] for slot in ("Selected", "Peer")) > 0,
                "Treatment has no credited and paid intervention inside the frozen window")
    else:
        require(global_delta == b["Credit"] == 0, "A zero-intervention case contains delay credit")
        require(all(b[slot]["ReleasedWait"] == b[slot]["ActiveWait"] == 0 and b[slot]["Pending"] is None
                    for slot in ("Selected", "Peer")), "A zero-intervention case contains native wait")
    lower = Fraction(end["BeforeTimestamp"] - start["AfterTimestamp"], frequency) - Fraction(global_delta, 1000000000)
    upper = Fraction(end["AfterTimestamp"] - start["BeforeTimestamp"], frequency) - Fraction(global_delta, 1000000000)
    require(lower > 0, "Corrected duration is nonpositive within the boundary bracket")
    return WindowCost(*progress, global_delta, lower, upper)


def analyze_case(data, mode, exit_code):
    errors = []
    cost = None
    counters = analyze_counter_capture(data) if mode in ("profile-zero", "profile-half") else None
    try:
        require(mode in MODES, "Unknown case mode")
        require(type(exit_code) is int and exit_code == 0, "Workload process did not exit successfully")
        require(data["failures"] == [], "Workload retained failures")
        if mode.startswith("physical-"):
            require(data["mode"] == mode.removeprefix("physical-"), "Physical mode differs")
            require(len(data["joined"]) == 2 and all(value is True for value in data["joined"]), "Physical workers did not join")
            require(data["expectedChecksum"] == 49995000 and data["measurementMilliseconds"] == 2000,
                    "Physical workload contract differs")
        else:
            require(type(data["percent"]) is int and data["percent"] == (0 if mode == "profile-zero" else 50), "Profile percentage differs")
            require(data["closed"] is True and data["stableMap"] is True, "Native cleanup or method map failed")
            require(data["forceGc"] is False and data["schedulerTiming"] is False
                    and data["controllerPhases"] is None and data["initialIntervalNanoseconds"] == 10000,
                    "Profile settings differ from the declared comparison")
            require(data["initialization"] is not None and data["transportInitialization"] is not None,
                    "Profile preparation evidence is missing")
            require(len(data["lifetimes"]) == 2 and all(item["Joined"] is True and item["Retired"] is True
                    and item["Released"] is True and item["error"] is None for item in data["lifetimes"]),
                    "Native target lifetime failed")
            require(data["pending"] == [], "Native ownership remains pending after cleanup")
            require(len(data["peaks"]) == 2 and all(type(value) is int and 0 <= value <= 2000000 for value in data["peaks"]),
                    "Observed debt exceeds the unchanged bound")
            records = data["records"]
            require(all(type(records[key]) is int and records[key] == 0 for key in ("LostRecords", "LostSamples", "Throttles")),
                    "Sample loss or throttle evidence is invalid")
            require(any(item["Accepted"] is True and item["Sample"]["Method"] in records["SelectedSymbols"]
                        for item in data["samples"]), "No accepted selected sample")
            if mode == "profile-zero":
                require(data["credit"] == 0 and data["journals"] == [] and data["waits"] == [0, 0],
                        "Zero-percent case contains intervention")
            else:
                require(integer(data["credit"], "selected credit", 1) > 0
                        and sum(integer(value, "paid wait") for value in data["waits"]) > 0
                        and len(data["journals"]) > 0, "Treatment has no paid intervention")
        cost = parse_window(data, mode).report()
    except (ValueError, KeyError, TypeError, IndexError) as error:
        errors.append(str(error))
    if counters is not None and not counters["reconciled"]:
        errors.extend("CPU counter evidence: " + reason for reason in counters["reasons"])
    if mode == "profile-half":
        errors.extend(["Unreceived sample-period attribution has no verified bound",
                       "Native journal suffix attribution has no verified bound"])
    return {"predictionEligible": not errors, "reasons": errors, "observedAccountedCost": cost,
            "costIncludesUnboundedAttribution": mode == "profile-half", "cpuCounterDiagnostics": counters}


def analyze(root):
    plan = read(root / "plan.json")
    order = plan["order"]
    require(plan["protocol"] == "matched-timer-diagnostic-v1" and plan["seed"] == 20261014,
            "Unknown comparison plan")
    expected = {(block, mode) for block in range(1, 9) for mode in MODES}
    identities = [(item["block"], item["mode"]) for item in order]
    require(len(order) == 32 and set(identities) == expected, "Plan requires eight complete four-case blocks")
    require([item["block"] for item in order] == [block for block in range(1, 9) for _ in MODES],
            "Plan crosses declared block boundaries")
    names = [f"block-{block}-{mode}" for block, mode in identities]
    require([item["name"] for item in order] == names, "Plan filenames differ from case identities")
    outcomes = read(root / "outcomes.json")
    require([item["name"] for item in outcomes] == names, "Outcomes omit, duplicate, or reorder declared cases")
    manifest = read(root / "manifest.json")
    require(isinstance(manifest["files"], dict), "Snapshot hashes must be a mapping")
    require(all(name in manifest["files"] for name in ("plan.json", "source/native/AnalyzeMatchedPrediction.py", "source/native/AnalyzeCpuCounters.py",
        "source/native/CpuCounterChecks.py",
            "workload/CozNet.LinuxPerf.dll", "workload/CozNet.dll", "native/libCozNet.Timer.so",
            "calibration/calibration.json", "calibration/calibration.json.plan.json",
            "calibration/calibration.json.blocks.jsonl", "calibration/manifest.json")), "Required snapshot hashes are missing")
    runner_errors = read(root / "runner-errors.json")
    require(isinstance(runner_errors, list), "Invalid runner error record")
    integrity_errors = []
    for name, digest in manifest["files"].items():
        require(isinstance(name, str) and isinstance(digest, str) and len(digest) == 64
                and all(character in "0123456789abcdefABCDEF" for character in digest), "Invalid snapshot hash metadata")
        path = (root / name).resolve()
        require(path.is_relative_to(root.resolve()), "Manifest path escapes evidence directory")
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest().lower() != digest.lower():
            integrity_errors.append(f"Snapshot hash mismatch: {name}")
    cases = []
    for planned, outcome in zip(order, outcomes):
        path = root / (planned["name"] + ".json")
        try:
            require(outcome["executed"] is True and outcome["recordExists"] is True, "Case was not executed or has no result")
            result = analyze_case(read(path), planned["mode"], outcome["exitCode"])
        except (OSError, ValueError, KeyError, TypeError) as error:
            result = {"predictionEligible": False, "reasons": [str(error)], "observedAccountedCost": None}
        if integrity_errors:
            result["predictionEligible"] = False
            result["reasons"].extend(integrity_errors)
        cases.append({**planned, **result, "exitCode": outcome.get("exitCode")})
    calibration = parse_calibration(root / "calibration")
    calibration_reasons = ["Calibration clock uncertainty has no verified bound",
                           "Calibration overhead sensitivity is not a verified bound",
                           "Local speedup uncertainty has not been propagated into the prediction-error gate"]
    blocks = [{"block": block, "predictionEligible": False, "prediction": None,
               "ineligibleCases": [item["name"] for item in cases if item["block"] == block and not item["predictionEligible"]]}
              for block in range(1, 9)]
    return {"protocol": plan["protocol"], "cases": cases, "blocks": blocks,
            "integrityErrors": integrity_errors, "runnerErrors": runner_errors, "calibrationReasons": calibration_reasons,
            "calibrationDiagnostic": {"meanReduction": calibration.mean, "intervalHalfWidth": calibration.half_width, "maxObservedOverheadShift": calibration.sensitivity},
            "collectionComplete": not runner_errors and all(item["executed"] is True and item["recordExists"] is True and item["exitCode"] == 0 for item in outcomes)
                and all(item["observedAccountedCost"] is not None for item in cases),
            "predictionAccuracyEstablished": False, "predictionSummary": None, "passed": False}


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--validate-calibration":
        try:
            parse_calibration(pathlib.Path(sys.argv[2]))
        except (OSError, ValueError, KeyError, TypeError, IndexError) as error:
            sys.exit(f"Calibration rejected: {error}")
        sys.exit(0)
    root = pathlib.Path(sys.argv[1])
    try:
        result = analyze(root)
    except (OSError, ValueError, KeyError, TypeError) as error:
        result = {"passed": False, "predictionAccuracyEstablished": False, "error": str(error)}
    try:
        with (root / "analysis.json").open("x", encoding="utf-8") as stream:
            json.dump(result, stream, indent=2, allow_nan=False)
            stream.write("\n")
    except FileExistsError:
        sys.exit("Analysis already exists. Retain the original output.")
    sys.exit(0 if result["passed"] else 1)
