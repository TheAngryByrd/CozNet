import hashlib
import json
import math
import pathlib
import sys
from dataclasses import dataclass
from fractions import Fraction

from AnalyzeBoundSampledPayment import count, require
from AnalyzeBoundSelectedPayment import verify_record
from SelectedCpuEvidence import verify_selected_cpu
from BoundSampleClockEvidence import verify_bound_sample_clock


MODES = ("physical-baseline", "physical-optimized", "sampled-zero", "sampled-half")
PROTOCOL = "cooperative-matched-v1"
WORKLOAD = "cooperative-checksum-v1"
REQUIRED_FILES = (
    "plan.json", "source/native/VerifyCooperativeComparison.ps1",
    "source/native/CooperativeComparisonRunnerChecks.ps1",
    "source/native/AnalyzeCooperativeComparison.py", "source/native/CooperativeComparisonChecks.py",
    "source/native/BoundSelectedPaymentAnalysisChecks.py",
    "source/native/CooperativeMeasurementEvidence.py", "source/native/AnalyzeBoundSelectedPayment.py",
    "source/native/AnalyzeBoundSampledPayment.py", "source/native/BoundSchedulerEvidence.py",
    "source/tests/CozNet.LinuxPerf/CooperativeCpuWork.cs",
    "source/tests/CozNet.LinuxPerf/CooperativePhysicalWorkload.cs",
    "source/tests/CozNet.LinuxPerf/BoundSampledPaymentWorkload.cs",
    "workload/CozNet.LinuxPerf.dll", "workload/CozNet.dll",
    "workload/CozNet.LinuxPerf.runtimeconfig.json", "workload/CozNet.LinuxPerf.deps.json",
    "native/libCozNet.Cooperative.so", "profiler/libCozNet.Profiler.so")


def read(path):
    def reject(value):
        raise ValueError("Non-finite JSON value: " + value)
    def finite(value):
        parsed = float(value)
        require(math.isfinite(parsed), "Non-finite JSON number")
        return parsed

    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, "Duplicate JSON field: " + key)
            result[key] = value
        return result

    result = json.loads(path.read_text(encoding="utf-8-sig"), parse_constant=reject,
                        parse_float=finite, object_pairs_hook=unique)
    return result


@dataclass(frozen=True)
class Cost:
    lower: Fraction
    upper: Fraction
    progress: int


def cost(window):
    require(window["Status"] == 2 and type(window["Status"]) is int, "The measurement window is incomplete")
    start, end = window["Start"], window["End"]
    frequency = count(start["TimestampFrequency"])
    require(frequency > 0 and count(end["TimestampFrequency"]) == frequency, "The measurement clock changed")
    stamps = [count(start["BeforeTimestamp"]), count(start["AfterTimestamp"]),
              count(end["BeforeTimestamp"]), count(end["AfterTimestamp"])]
    require(0 < stamps[0] <= stamps[1] <= stamps[2] <= stamps[3], "The measurement clock regressed")
    first, last = start["Accounting"], end["Accounting"]
    progress = count(last["Selected"]["Progress"]) - count(first["Selected"]["Progress"])
    delay = count(last["Global"]) - count(first["Global"])
    require(progress > 0 and delay >= 0, "Measurement progress or global delay regressed")
    lower = Fraction((stamps[2] - stamps[1]) * 1000000000, frequency) - delay
    upper = Fraction((stamps[3] - stamps[0]) * 1000000000, frequency) - delay
    require(0 < lower <= upper, "Effective duration is nonpositive")
    return Cost(lower / progress, upper / progress, progress)


def physical(record, mode, batches):
    require(record["mode"] == mode.removeprefix("physical-") and record["completed"] is True
            and record["joined"] is True and record["failures"] == [], "Physical work did not complete cleanly")
    require(count(record["expectedChecksum"]) == 49995000 and record["predictionAccuracyEstablished"] is False,
            "Physical work changed its checksum contract or claimed accuracy")
    require(count(record["requestedBatches"]) == batches == count(record["selectedBatches"]), "Physical fixed work changed")
    for progress, checksum in (("selectedBatches", "checksum"), ("peerBatches", "peerChecksum")):
        require(count(record[progress]) > 0 and count(record[checksum]) == record[progress] * 49995000,
                "Physical checksum totals do not match completed work")
    window = record["measurementWindow"]
    result = cost(window)
    start, end = window["Start"], window["End"]
    require(count(record["timestampFrequency"]) == start["TimestampFrequency"]
            and start["AfterTimestamp"] <= count(record["selectedWorkStarted"])
            <= count(record["selectedWorkFinished"]) <= end["BeforeTimestamp"], "Physical selected work escaped its window")
    require(start["Accounting"]["Selected"]["Progress"] == 0 and end["Accounting"]["Selected"]["Progress"] == batches,
            "Physical endpoint selected progress differs from its work")
    for boundary in (start, end):
        values = boundary["Accounting"]
        require(all(count(values[key]) == 0 for key in ("Global", "Credit", "AcceptedSampleSequence")) and count(values["Epoch"]) == 1
                and values.get("Timer") is None and values.get("Cooperative") is None, "Physical window contains intervention accounting")
        for name, total in (("Selected", record["selectedBatches"]), ("Peer", record["peerBatches"])):
            worker = values[name]
            require(count(worker["Progress"]) <= total and count(worker["ReleasedWait"]) == count(worker["ActiveWait"]) == 0
                    and worker["Pending"] is None and worker.get("Ledger") is None, "Physical worker contains payment or excess progress")
    require(end["Accounting"]["Peer"]["Progress"] > start["Accounting"]["Peer"]["Progress"], "Physical peer made no measured progress")
    return result


def probe_interval_plan(plan):
    interval = plan.get("probeInterval", 1024)
    version = plan.get("probeIntervalEvidenceVersion")
    if "probeIntervalEvidenceVersion" not in plan:
        require(type(interval) is int and interval == 1024, "Legacy plan requires probe interval 1024")
        return interval, False
    require(type(version) is int and version == 1, "Unsupported probe interval evidence version")
    require("probeInterval" in plan and type(interval) is int and interval in (1024, 8192),
            "Unsupported declared probe interval")
    return interval, True


def receipt_preparation_plan(plan):
    enabled = plan.get("prepareReceipts", False)
    if "receiptPreparationEvidenceVersion" not in plan:
        require(type(enabled) is bool and not enabled, "Legacy plan requires unprepared receipts")
        return False, False
    version = plan["receiptPreparationEvidenceVersion"]
    require(type(version) is int and version == 1, "Unsupported receipt preparation evidence version")
    require("prepareReceipts" in plan and type(enabled) is bool, "Declared receipt preparation is not Boolean")
    return enabled, True


def sample_credit_plan(plan):
    policy = plan.get("sampleCreditPolicy", "same-epoch-interval-v1")
    if "sampleCreditEvidenceVersion" not in plan:
        require(type(policy) is str and policy == "same-epoch-interval-v1", "Legacy plan requires interval sample credit")
        return policy, False
    require(type(plan["sampleCreditEvidenceVersion"]) is int and plan["sampleCreditEvidenceVersion"] == 1,
            "Unsupported sample credit evidence version")
    require("sampleCreditPolicy" in plan and type(policy) is str and policy in ("same-epoch-interval-v1", "nominal-event-v1"),
            "Unsupported declared sample credit policy")
    return policy, True


def controller_ownership_plan(plan):
    enabled = plan.get("ownerController", False)
    if "controllerOwnershipEvidenceVersion" not in plan:
        require(type(enabled) is bool and not enabled, "Legacy plan requires a separate controller")
        return False, False
    version = plan["controllerOwnershipEvidenceVersion"]
    require(type(version) is int and version == 1, "Unsupported controller ownership evidence version")
    require("ownerController" in plan and type(enabled) is bool, "Declared controller ownership is not Boolean")
    return enabled, True


def selected_cpu_plan(plan):
    if 'selectedCpuEvidenceVersion' not in plan:
        require(plan.get('selectedCpu', False) is False, 'Legacy plan cannot enable selected CPU capture')
        return False, False
    require(type(plan['selectedCpuEvidenceVersion']) is int and plan['selectedCpuEvidenceVersion'] == 1 and
            type(plan.get('selectedCpu')) is bool, 'Invalid selected CPU plan')
    return plan['selectedCpu'], True


def bound_sample_clock_plan(plan):
    if 'boundSampleClockEvidenceVersion' not in plan:
        require(plan.get('boundSampleClock', False) is False, 'Legacy plan cannot enable bound sample clock capture')
        return False, False
    require(type(plan['boundSampleClockEvidenceVersion']) is int and plan['boundSampleClockEvidenceVersion'] == 1
            and type(plan.get('boundSampleClock')) is bool, 'Invalid bound sample clock plan')
    return plan['boundSampleClock'], True


def analyze_case(record, mode, batches, exit_code, probe_interval=1024, require_probe_interval=False,
                 prepare_receipts=False, require_preparation=False, owner_controller=False, require_ownership=False,
                 sample_credit_policy="same-epoch-interval-v1", require_credit_policy=False,
                 selected_cpu=False, require_selected_cpu=False,
                 bound_sample_clock=False, require_bound_sample_clock=False):
    require(not require_selected_cpu or 'selectedCpuEnabled' in record, 'Arm lacks declared selected CPU evidence')
    require(record.get('selectedCpuEnabled', False) is selected_cpu, 'Selected CPU activation differs from plan')
    verify_selected_cpu(record)
    require(mode in MODES and type(exit_code) is int, "Unknown arm or invalid process exit code")
    require(record["workloadProtocol"] == WORKLOAD, "The case does not use the shared cooperative work")
    if mode.startswith("physical-"):
        require(record.get('boundSampleClockEnabled', False) is False and record.get('boundSampleClock') is None,
                'Physical arm contains bound sample clock capture')
        require(type(exit_code) is int and exit_code == 0, "Physical process failed")
        return "complete", [], physical(record, mode, batches)
    require(not require_bound_sample_clock or 'boundSampleClockEnabled' in record,
            'Sampled arm lacks declared bound sample clock evidence')
    require(record.get('boundSampleClockEnabled', False) is bound_sample_clock,
            'Bound sample clock activation differs from plan')
    require(not require_probe_interval or "probeInterval" in record, "Sampled arm lacks declared probe interval evidence")
    actual_interval = record.get("probeInterval", 1024)
    require(type(actual_interval) is int and actual_interval == probe_interval,
            "Sampled probe interval differs from the declared plan")
    require(not require_preparation or "receiptPreparationEnabled" in record,
            "Sampled arm lacks declared receipt preparation evidence")
    actual_preparation = record.get("receiptPreparationEnabled", False)
    require(type(actual_preparation) is bool and actual_preparation == prepare_receipts,
            "Sampled receipt preparation differs from the declared plan")
    require(not require_ownership or "ownerControllerEnabled" in record,
            "Sampled arm lacks declared controller ownership evidence")
    actual_ownership = record.get("ownerControllerEnabled", False)
    require(type(actual_ownership) is bool and actual_ownership == owner_controller,
            "Sampled controller ownership differs from the declared plan")
    require(not require_credit_policy or "sampleCreditPolicy" in record, "Sampled arm lacks declared sample credit policy")
    actual_policy = record.get("sampleCreditPolicy", "same-epoch-interval-v1")
    require(type(actual_policy) is str and actual_policy == sample_credit_policy, "Sampled credit policy differs from the declared plan")
    require(count(record["timestampFrequency"]) > 0, "The sampled clock frequency is zero")
    replay = verify_record(record)
    require(record["finiteMeasurementEnabled"] is True and count(record["percent"]) == (0 if mode == "sampled-zero" else 50)
            and count(record["requestedBatches"]) == batches and record["selectedCadence"] == "QualifiedSample",
            "Sampled arm differs from the declared finite work")
    require(record.get("selectedHandoffEnabled") is False and record.get("directReceiptReadyEnabled") is True,
            "Sampled arm changed its declared scheduling or receipt policy")
    assessment = replay["measurement"]
    require(assessment is not None, "Sampled arm lost its finite assessment")
    require(type(record["completed"]) is bool, "Invalid sampled completion flag")
    if exit_code != 0 or not record["completed"] or assessment["Status"] == "invalid":
        reasons = list(assessment["Reasons"])
        if exit_code != 0:
            reasons.append("process-exit-code:" + str(exit_code))
        if not record["completed"]:
            reasons.append("workload-incomplete")
        return "invalid", reasons, None
    require(type(exit_code) is int and record["selectedBatches"] == batches and assessment["Status"] == "unqualified",
            "Sampled completion or qualification is inconsistent")
    return "unqualified", assessment["Reasons"], cost(record["measurement"]["Window"])


@dataclass(frozen=True)
class Outcome:
    attempted: bool
    executed: bool
    record_exists: bool
    exit_code: int | None

    @staticmethod
    def parse(raw):
        attempted, executed, exists = (raw[key] for key in ("attempted", "executed", "recordExists"))
        require(all(type(value) is bool for value in (attempted, executed, exists)), "Invalid outcome flags")
        code = raw["exitCode"]
        require((type(code) is int if executed else code is None)
                and (attempted or not executed) and (executed or not exists), "Inconsistent process outcome")
        return Outcome(attempted, executed, exists, code)


def outward(lower, upper):
    low, high = float(lower), float(upper)
    require(math.isfinite(low) and math.isfinite(high), "Comparison bounds overflowed")
    if Fraction(low) > lower:
        low = math.nextafter(low, -math.inf)
    if Fraction(high) < upper:
        high = math.nextafter(high, math.inf)
    return dict(lower=low, upper=high)


def compare(costs):
    def reduction(control, treatment):
        before, after = costs[control], costs[treatment]
        return 1 - after.upper / before.lower, 1 - after.lower / before.upper
    predicted = reduction("sampled-zero", "sampled-half")
    physical_change = reduction("physical-baseline", "physical-optimized")
    return dict(predictedElapsedReduction=outward(*predicted), physicalElapsedReduction=outward(*physical_change),
                nominalPredictionMinusPhysical=outward(predicted[0] - physical_change[1], predicted[1] - physical_change[0]),
                isAccuracyBound=False)


def analyze(root):
    root = root.resolve()
    plan, manifest, raw_outcomes = (read(root / name) for name in ("plan.json", "manifest.json", "outcomes.json"))
    require(plan["protocol"] == PROTOCOL and plan["order"] == list(MODES), "Comparison order or protocol changed")
    probe_interval, require_probe_interval = probe_interval_plan(plan)
    prepare_receipts, require_preparation = receipt_preparation_plan(plan)
    owner_controller, require_ownership = controller_ownership_plan(plan)
    sample_credit_policy, require_credit_policy = sample_credit_plan(plan)
    selected_cpu, require_selected_cpu = selected_cpu_plan(plan)
    bound_sample_clock, require_bound_sample_clock = bound_sample_clock_plan(plan)
    batches = count(plan["selectedBatches"])
    require(0 < batches <= 2147483647 and plan["predictionErrorBound"] == 0.02
            and plan["localOptimizationCalibrated"] is False, "Comparison work or accuracy contract changed")
    require(isinstance(raw_outcomes, list) and [item["mode"] for item in raw_outcomes] == list(MODES),
            "Comparison outcomes omit or reorder planned arms")
    outcomes = [Outcome.parse(item) for item in raw_outcomes]
    files = manifest["files"]
    require(isinstance(files, dict) and all(name in files for name in REQUIRED_FILES),
            "Required comparison snapshots are missing")
    require(not require_selected_cpu or 'source/native/SelectedCpuEvidence.py' in files,
            'Versioned selected CPU plan lacks its replay snapshot')
    require(not require_bound_sample_clock or 'source/native/BoundSampleClockEvidence.py' in files,
            'Versioned bound sample clock plan lacks its replay snapshot')
    integrity_errors = []
    for name, digest in files.items():
        require(isinstance(name, str) and isinstance(digest, str) and len(digest) == 64
                and all(char in "0123456789abcdefABCDEF" for char in digest), "Invalid snapshot metadata")
        path = (root / name).resolve()
        require(path.is_relative_to(root), "Snapshot path escapes the comparison")
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest().lower() != digest.lower():
            integrity_errors.append("Snapshot hash mismatch: " + name)
    identity = manifest["runtimeImage"]
    require(isinstance(identity, str) and identity.startswith("sha256:") and len(identity) == 71
            and all(char in "0123456789abcdef" for char in identity[7:]), "Runtime image identity is absent")
    integrity = read(root / "integrity.json")
    if integrity["originalInputsUnchanged"] is not True:
        integrity_errors.append("Original inputs changed during capture")
    runner_errors = read(root / "runner-errors.json")
    require(isinstance(runner_errors, list) and all(isinstance(error, str) and error for error in runner_errors),
            "Invalid runner error list")
    cases, costs = [], {}
    for mode, outcome in zip(MODES, outcomes):
        state, reasons, value = "invalid", [], None
        cpu = None
        sample_clock = None
        try:
            require(outcome.executed and outcome.record_exists, "Case was not executed or has no retained result")
            record = read(root / (mode + ".json"))
            state, reasons, value = analyze_case(record, mode, batches, outcome.exit_code,
                                                 probe_interval, require_probe_interval, prepare_receipts, require_preparation,
                                                 owner_controller, require_ownership, sample_credit_policy, require_credit_policy,
                                                 selected_cpu, require_selected_cpu,
                                                 bound_sample_clock, require_bound_sample_clock)
            if mode.startswith('sampled-'):
                sample_clock = verify_bound_sample_clock(record)
            if selected_cpu and state != 'invalid':
                cpu = verify_selected_cpu(record)
        except (OSError, ValueError, KeyError, TypeError, IndexError, AttributeError, ArithmeticError) as error:
            reasons = [str(error)]
        if value is not None:
            costs[mode] = value
        cases.append(dict(mode=mode, status=state, reasons=reasons, attempted=outcome.attempted,
                          executed=outcome.executed, exitCode=outcome.exit_code,
                          descriptiveCostNanoseconds=None if value is None else outward(value.lower, value.upper)))
        if require_selected_cpu:
            cases[-1]['selectedCpu'] = cpu
        if require_bound_sample_clock:
            cases[-1]['boundSampleClock'] = sample_clock
    complete = not runner_errors and not integrity_errors and len(costs) == 4
    descriptive = compare(costs) if complete else None
    return dict(protocol=PROTOCOL, status="unqualified" if complete else "invalid", collectionComplete=complete,
        probeInterval=probe_interval, probeIntervalEvidenceVersion=1 if require_probe_interval else None,
                prepareReceipts=prepare_receipts, receiptPreparationEvidenceVersion=1 if require_preparation else None,
                ownerController=owner_controller, controllerOwnershipEvidenceVersion=1 if require_ownership else None,
                sampleCreditPolicy=sample_credit_policy, sampleCreditEvidenceVersion=1 if require_credit_policy else None,
                **(dict(selectedCpu=selected_cpu, selectedCpuEvidenceVersion=1) if require_selected_cpu else {}),
                **(dict(boundSampleClock=bound_sample_clock, boundSampleClockEvidenceVersion=1) if require_bound_sample_clock else {}),
                attempted=sum(item.attempted for item in outcomes), completed=len(costs), qualified=0,
                invalid=sum(item["status"] == "invalid" for item in cases), cases=cases,
                integrityErrors=integrity_errors, runnerErrors=runner_errors, descriptiveComparison=descriptive,
                qualificationReasons=["sampled-arms-unqualified", "local-optimization-not-calibrated", "single-declared-block"],
                predictionErrorBound=0.02, predictionAccuracyEstablished=False, qualifiedComparison=None, passed=False)


if __name__ == "__main__":
    directory = pathlib.Path(sys.argv[1])
    try:
        result = analyze(directory)
    except (OSError, ValueError, KeyError, TypeError, IndexError, AttributeError, ArithmeticError) as error:
        result = dict(status="invalid", passed=False, predictionAccuracyEstablished=False, error=str(error))
    (directory / "analysis.json").write_text(json.dumps(result, indent=2, allow_nan=False), encoding="utf-8")
    sys.exit(0 if result.get("passed") else 1)
