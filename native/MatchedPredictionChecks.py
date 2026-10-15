from copy import deepcopy
from fractions import Fraction
import hashlib
import json
import math
import pathlib
import subprocess
import sys
import tempfile
import unittest
from AnalyzeMatchedPrediction import MODES, WindowCost, analyze, analyze_case, conditional_comparison, parse_window, parse_calibration
from CpuCounterChecks import credit_fixture as counter_fixture
from SampleMembershipChecks import fixture as membership_fixture
from TimerPrefixChecks import fixture as timer_fixture


def fixture(mode="profile-half"):
    worker = {"Progress": 0, "ReleasedWait": 0, "ActiveWait": 0, "Pending": None}
    account = {"Global": 0, "Credit": 0, "AcceptedSampleSequence": 0, "Epoch": 1,
               "Selected": deepcopy(worker), "Peer": deepcopy(worker)}
    start = {"BeforeTimestamp": 100, "AfterTimestamp": 110, "TimestampFrequency": 1000, "Accounting": account}
    end = deepcopy(start)
    end.update(BeforeTimestamp=2110, AfterTimestamp=2120)
    end["Accounting"]["Selected"]["Progress"] = 100
    end["Accounting"]["Peer"]["Progress"] = 50
    if mode == "profile-half":
        end["Accounting"].update(Global=500000000, Credit=499000000, AcceptedSampleSequence=50)
        end["Accounting"]["Peer"]["ReleasedWait"] = 10
    data = {"measurementCapture": {"status": "Complete", "start": start, "end": end},
            "timestampFrequency": 1000, "failures": [], "progress": [9999, 8888],
            "mode": mode.removeprefix("physical-"), "joined": [True, True], "expectedChecksum": 49995000,
            "measurementMilliseconds": 2000, "percent": 0 if mode == "profile-zero" else 50,
            "closed": True, "stableMap": True, "forceGc": False, "schedulerTiming": False,
            "controllerPhases": None, "initialIntervalNanoseconds": 10000,
            "initialization": {}, "transportInitialization": {}, "pending": [], "peaks": [1000, 2000],
            "lifetimes": [{"Joined": True, "Retired": True, "Released": True, "error": None}] * 2,
            "records": {"LostRecords": 0, "LostSamples": 0, "Throttles": 0, "SelectedSymbols": ["selected"]},
            "samples": [{"Accepted": True, "Sample": {"Method": "selected"}}],
            "credit": 900000000 if mode == "profile-half" else 0,
            "journals": [{}] if mode == "profile-half" else [],
            "waits": [800000000, 900000000] if mode == "profile-half" else [0, 0]}
    if mode.startswith("profile-"):
        counter = counter_fixture(data["percent"])
        for field in ("cpuCounters", "samples", "records"):
            data[field] = counter[field]
        end["Accounting"]["AcceptedSampleSequence"] = 2
        data["credit"] = end["Accounting"]["Credit"] = counter["credit"]
        data["sampleClock"] = membership_fixture()["sampleClock"]
        def nanosecond_clock(value):
            if isinstance(value, dict):
                for key in value:
                    if key in ("TimestampFrequency", "timestampFrequency"):
                        value[key] = 1000000000
                    elif key in ("BeforeTimestamp", "AfterTimestamp", "Timestamp", "Before", "After", "NativeNanoseconds"):
                        value[key] *= 1000000
                    else:
                        nanosecond_clock(value[key])
            elif isinstance(value, list):
                for item in value:
                    nanosecond_clock(item)
        nanosecond_clock(data)
    if mode.startswith("profile-"):
        empty = {"ReleasedJournalCount": 0, "Selected": {"ReleasedWait": 0, "Pending": None},
                 "Peer": {"ReleasedWait": 0, "Pending": None}}
        for boundary in (start, end):
            boundary["Accounting"]["Timer"] = deepcopy(empty)
        if mode == "profile-half":
            data["journals"] = timer_fixture()["journals"]
            accepted = data["journals"][0]["Accepted"]
            accepted["Raw"]["Header"].update(Measured=10, Stop=1)
            accepted["Raw"]["Children"][0]["Waited"] = 10
            accepted["Received"][0]["Raw"]["Waited"] = 10
            end["Accounting"]["Timer"]["ReleasedJournalCount"] = 1
            end["Accounting"]["Timer"]["Peer"]["ReleasedWait"] = 10
            data["waits"] = [0, 10]
    return data


def calibration(root, varied=False):
    root.mkdir(parents=True, exist_ok=True)
    declared, blocks, estimates = [], [], []
    cpu, wall = 1000, 1000
    for index in range(-1, 8):
        steps, segments = [], []
        optimized = (900 if index < 0 else 300 + 50 * index) if varied else 500
        for round_index in range(100):
            for mode, duration in (("Baseline", 1000), ("Optimized", optimized), ("Control", 10)):
                step = {"Round": round_index, "Arithmetic": mode}
                raw = {"CpuBefore": cpu, "CpuAfter": cpu + duration, "BeforeTimestamp": wall,
                       "AfterTimestamp": wall + duration + 20, "Frequency": 1000000000,
                       "Error": None, "CompletedBatches": 500, "Checksum": 24997500000}
                steps.append(step)
                segments.append({"Step": step, "Measurement": raw})
                cpu += duration + 5
                wall += duration + 50
        declared.append({"Index": index, "Warmup": index == -1, "Steps": steps})
        blocks.append({"Index": index, "Warmup": index == -1, "Segments": segments})
        if index >= 0:
            reductions = [0.7, 0.65, 0.6, 0.55, 0.5, 0.45, 0.4, 0.35]
            lower = [0.696969696969697, 0.6464646464646465, 0.595959595959596, 0.5454545454545454,
                     0.4949494949494949, 0.4444444444444444, 0.3939393939393939, 0.3434343434343434]
            upper = [0.71, 0.66, 0.61, 0.56, 0.51, 0.46, 0.41, 0.36]
            estimates.append({"Index": index, "BaselineCpu": 100000, "OptimizedCpu": optimized * 100, "ControlCpu": 1000,
                              "RawReduction": reductions[index] if varied else 0.5,
                              "SensitivityLower": lower[index] if varied else 0.4949494949494949,
                              "SensitivityUpper": upper[index] if varied else 0.51})
    plan = {"protocol": "interleaved-control-v1", "seed": 20261015, "blocks": 8, "roundsPerBlock": 100,
            "callsPerSegment": 500, "plan": declared}
    result = {"protocol": plan["protocol"], "calibrationComplete": True, "failures": [], "unexecutedBlocks": [],
              "predictionAccuracyEstablished": False, "clockAccuracyEstablished": False, "overheadBoundEstablished": False,
              "blocks": blocks, "analysis": {"Errors": [], "Estimate": {"Blocks": estimates, "MeanReduction": 0.5,
              "StandardDeviation": 0, "IntervalHalfWidth": 0, "MaxObservedOverheadShift": 0.01, "DistancePlusIntervalAndSensitivity": 0.01}}}
    if varied:
        result["analysis"]["Estimate"].update(MeanReduction=0.525, StandardDeviation=0.1224744871391589,
                                              IntervalHalfWidth=0.10239123358853752, DistancePlusIntervalAndSensitivity=0.13739123358853752)
    (root / "calibration.json").write_text(json.dumps(result))
    (root / "calibration.json.plan.json").write_text(json.dumps(plan))
    (root / "calibration.json.blocks.jsonl").write_text("".join(json.dumps(block) + "\n" for block in blocks))


def series(root):
    order = [{"block": block, "mode": mode, "name": f"block-{block}-{mode}"}
             for block in range(1, 9) for mode in MODES]
    (root / "plan.json").write_text(json.dumps({"protocol": "matched-timer-diagnostic-v1", "seed": 20261014, "order": order}))
    outcomes = [{"name": item["name"], "executed": True, "recordExists": True, "exitCode": 0} for item in order]
    (root / "outcomes.json").write_text(json.dumps(outcomes))
    for item in order:
        (root / (item["name"] + ".json")).write_text(json.dumps(fixture(item["mode"])))
    calibration(root / "calibration")
    names = ("plan.json", "source/native/AnalyzeMatchedPrediction.py", "source/native/AnalyzeCpuCounters.py",
             "source/native/CpuCounterChecks.py", "source/native/AnalyzeSampleMembership.py", "source/native/SampleMembershipChecks.py",
             "workload/CozNet.LinuxPerf.dll", "workload/CozNet.dll",
             "native/libCozNet.Timer.so", "calibration/calibration.json", "calibration/calibration.json.plan.json",
             "calibration/calibration.json.blocks.jsonl", "calibration/manifest.json")
    names += ("source/native/AnalyzeTimerPrefixes.py", "source/native/TimerPrefixChecks.py")
    hashes = {}
    for name in names:
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        if not path.exists():
            path.write_text("fixture")
        hashes[name] = hashlib.sha256(path.read_bytes()).hexdigest()
    (root / "manifest.json").write_text(json.dumps({"files": hashes}))
    (root / "runner-errors.json").write_text("[]")
    return outcomes


class MatchedPredictionChecks(unittest.TestCase):
    def test_invalid_timer_prefix_suppresses_only_its_block(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            series(root)
            path = root / "block-3-profile-half.json"
            data = json.loads(path.read_text())
            data["measurementCapture"]["end"]["Accounting"]["Timer"]["Peer"]["ReleasedWait"] = 11
            path.write_text(json.dumps(data))
            result = analyze(root)
            case = next(item for item in result["cases"] if item["name"] == "block-3-profile-half")
            self.assertTrue(case["sampleMembershipDiagnostics"]["comparisonDomainAccepted"])
            self.assertFalse(case["timerPrefixDiagnostics"]["reconciled"])
            self.assertTrue(any(reason.startswith("Timer prefix evidence:") for reason in case["reasons"]))
            self.assertIsNone(result["blocks"][2]["conditionalComparison"])
            self.assertTrue(all(item["conditionalComparison"] is not None
                                for item in result["blocks"] if item["block"] != 3))
            self.assertIsNone(result["predictionSummary"])

    def test_missing_timer_snapshot_rejects_profile_only(self):
        data = fixture("profile-zero")
        del data["measurementCapture"]["end"]["Accounting"]["Timer"]
        result = analyze_case(data, "profile-zero", 0)
        self.assertFalse(result["predictionEligible"])
        self.assertFalse(result["timerPrefixDiagnostics"]["reconciled"])
        physical = analyze_case(fixture("physical-baseline"), "physical-baseline", 0)
        self.assertTrue(physical["predictionEligible"])
        self.assertIsNone(physical["timerPrefixDiagnostics"])

    def test_timer_prefix_diagnostics_preserve_workload_failure(self):
        data = fixture()
        data.update(failures=["debt exceeded"], closed=False)
        result = analyze_case(data, "profile-half", 1)
        prefix = result["timerPrefixDiagnostics"]
        self.assertTrue(prefix["reconciled"], prefix["reasons"])
        self.assertEqual(["debt exceeded"], prefix["recordedWorkloadFailures"])
        self.assertFalse(prefix["nativeSessionClosed"])
        self.assertFalse(prefix["nativeTailBoundEstablished"])
        self.assertFalse(result["predictionEligible"])

    def test_conditional_comparison_uses_cost_ratios_and_outward_rounding(self):
        costs = {mode: WindowCost(10, 1, 0, Fraction(duration), Fraction(duration))
                 for mode, duration in zip(MODES, (100, 75, 100, 50))}
        result = conditional_comparison(costs)
        expected = {"predictedElapsedReduction": (Fraction(1, 12), Fraction(17, 44)),
                    "physicalElapsedReduction": (Fraction(7, 18), Fraction(13, 22)),
                    "nominalPredictionMinusPhysical": (Fraction(-67, 132), Fraction(-1, 396))}
        for field, (lower, upper) in expected.items():
            with self.subTest(field=field):
                actual = result[field]
                self.assertLessEqual(Fraction(actual["lower"]), lower)
                self.assertGreater(Fraction(math.nextafter(actual["lower"], math.inf)), lower)
                self.assertGreaterEqual(Fraction(actual["upper"]), upper)
                self.assertLess(Fraction(math.nextafter(actual["upper"], -math.inf)), upper)
        self.assertEqual(result["requestedLocalSpeedup"], 0.5)
        self.assertTrue(result["includesUnboundedAttribution"])
        self.assertFalse(result["isAccuracyBound"])

    def test_conditional_comparison_retains_negative_reductions(self):
        costs = {mode: WindowCost(100, 1, 0, Fraction(duration), Fraction(duration))
                 for mode, duration in zip(MODES, (100, 200, 100, 300))}
        result = conditional_comparison(costs)
        self.assertLess(result["predictedElapsedReduction"]["upper"], 0)
        self.assertLess(result["physicalElapsedReduction"]["upper"], 0)

    def test_conditional_comparison_uses_each_duration_and_progress_endpoint(self):
        inputs = ((10, 100, 110), (20, 75, 80), (30, 150, 180), (40, 60, 90))
        costs = {mode: WindowCost(progress, 1, 0, Fraction(lower), Fraction(upper))
                 for mode, (progress, lower, upper) in zip(MODES, inputs)}
        result = conditional_comparison(costs)
        expected = {"predictedElapsedReduction": (Fraction(51, 95), Fraction(109, 154)),
                    "physicalElapsedReduction": (Fraction(34, 65), Fraction(94, 123)),
                    "nominalPredictionMinusPhysical": (Fraction(-2657, 11685), Fraction(1849, 10010))}
        for field, (lower, upper) in expected.items():
            with self.subTest(field=field):
                self.assertLessEqual(Fraction(result[field]["lower"]), lower)
                self.assertGreater(Fraction(math.nextafter(result[field]["lower"], math.inf)), lower)
                self.assertGreaterEqual(Fraction(result[field]["upper"]), upper)
                self.assertLess(Fraction(math.nextafter(result[field]["upper"], -math.inf)), upper)

    def test_complete_blocks_have_only_conditional_comparisons(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            series(root)
            result = analyze(root)
            self.assertEqual(len(result["blocks"]), 8)
            for block in result["blocks"]:
                self.assertIsNotNone(block["conditionalComparison"])
                self.assertFalse(block["conditionalComparison"]["isAccuracyBound"])
                self.assertEqual(block["diagnosticUnavailableCases"], [])
                self.assertFalse(block["predictionEligible"])
                self.assertIsNone(block["prediction"])
            self.assertIsNone(result["predictionSummary"])
            self.assertFalse(result["predictionAccuracyEstablished"])
            self.assertFalse(result["passed"])

    def test_invalid_counter_evidence_suppresses_only_its_block_diagnostic(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            series(root)
            path = root / "block-3-profile-half.json"
            data = json.loads(path.read_text())
            data["cpuCounters"]["source"]["FailedReads"] = 1
            path.write_text(json.dumps(data))
            result = analyze(root)
            block = result["blocks"][2]
            self.assertIsNone(block["conditionalComparison"])
            self.assertEqual(block["diagnosticUnavailableCases"], ["block-3-profile-half"])
            self.assertTrue(all(item["conditionalComparison"] is not None
                                for item in result["blocks"] if item["block"] != 3))
            self.assertIsNone(result["predictionSummary"])

    def test_invalid_sample_credit_suppresses_only_its_block_diagnostic(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            series(root)
            path = root / "block-3-profile-half.json"
            data = json.loads(path.read_text())
            data["samples"][1]["Credit"] += 1
            path.write_text(json.dumps(data))
            result = analyze(root)
            case = next(item for item in result["cases"] if item["name"] == "block-3-profile-half")
            self.assertTrue(case["cpuCounterDiagnostics"]["reconciled"])
            self.assertFalse(case["sampleCreditDiagnostics"]["reconciled"])
            self.assertFalse(case["predictionEligible"])
            self.assertTrue(any(reason.startswith("Sample credit evidence:") for reason in case["reasons"]))
            self.assertIsNone(result["blocks"][2]["conditionalComparison"])
            self.assertTrue(all(item["conditionalComparison"] is not None for item in result["blocks"] if item["block"] != 3))
            self.assertIsNone(result["predictionSummary"])

    def test_invalid_membership_suppresses_only_its_block_diagnostic(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            series(root)
            path = root / "block-3-profile-half.json"
            data = json.loads(path.read_text())
            data["sampleClock"]["end"]["ContextIdentity"] = "changed"
            path.write_text(json.dumps(data))
            result = analyze(root)
            case = next(item for item in result["cases"] if item["name"] == "block-3-profile-half")
            self.assertTrue(case["cpuCounterDiagnostics"]["reconciled"])
            self.assertTrue(case["sampleCreditDiagnostics"]["reconciled"])
            self.assertFalse(case["sampleMembershipDiagnostics"]["comparisonDomainAccepted"])
            self.assertFalse(case["predictionEligible"])
            self.assertTrue(any(reason.startswith("Sample membership evidence:") for reason in case["reasons"]))
            self.assertIsNone(result["blocks"][2]["conditionalComparison"])
            self.assertTrue(all(item["conditionalComparison"] is not None for item in result["blocks"] if item["block"] != 3))
            self.assertIsNone(result["predictionSummary"])

    def test_missing_clock_evidence_rejects_profile_but_not_physical_case(self):
        data = fixture("profile-zero")
        del data["sampleClock"]
        result = analyze_case(data, "profile-zero", 0)
        self.assertFalse(result["predictionEligible"])
        self.assertFalse(result["sampleMembershipDiagnostics"]["comparisonDomainAccepted"])
        self.assertTrue(result["sampleCreditDiagnostics"]["reconciled"])
        self.assertTrue(analyze_case(fixture("physical-baseline"), "physical-baseline", 0)["predictionEligible"])

    def test_legacy_manifest_requires_both_counter_module_hashes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            series(root)
            manifest_path = root / "manifest.json"
            original = json.loads(manifest_path.read_text())
            for name in ("AnalyzeCpuCounters.py", "CpuCounterChecks.py", "AnalyzeSampleMembership.py", "SampleMembershipChecks.py",
                         "AnalyzeTimerPrefixes.py", "TimerPrefixChecks.py"):
                with self.subTest(name=name):
                    manifest = deepcopy(original)
                    del manifest["files"]["source/native/" + name]
                    manifest_path.write_text(json.dumps(manifest))
                    with self.assertRaisesRegex(ValueError, "Required snapshot hashes are missing"):
                        analyze(root)

    def test_counter_diagnostics_survive_workload_and_cleanup_failure(self):
        data = fixture()
        data.update(failures=["debt exceeded"], closed=False)
        result = analyze_case(data, "profile-half", 1)
        self.assertFalse(result["predictionEligible"])
        self.assertIn("Workload process did not exit successfully", result["reasons"])
        counters = result["cpuCounterDiagnostics"]
        self.assertTrue(counters["reconciled"], counters["reasons"])
        self.assertEqual(counters["recordedWorkloadFailures"], ["debt exceeded"])
        self.assertFalse(counters["nativeSessionClosed"])
        self.assertFalse(counters["predictionAccuracyEstablished"])
        self.assertTrue(result["sampleCreditDiagnostics"]["reconciled"])
        self.assertFalse(result["sampleCreditDiagnostics"]["predictionAccuracyEstablished"])
        membership = result["sampleMembershipDiagnostics"]
        self.assertTrue(membership["comparisonDomainAccepted"], membership["reasons"])
        self.assertEqual(membership["recordedWorkloadFailures"], ["debt exceeded"])
        self.assertFalse(membership["nativeSessionClosed"])
        self.assertEqual(membership["creditMembership"]["replayedCreditNanoseconds"], 5)
        self.assertFalse(membership["predictionAccuracyEstablished"])

    def test_legacy_profile_requires_counters_but_physical_case_does_not(self):
        data = fixture("profile-zero")
        del data["cpuCounters"]
        result = analyze_case(data, "profile-zero", 0)
        self.assertFalse(result["predictionEligible"])
        self.assertIsNotNone(result["observedAccountedCost"])
        self.assertFalse(result["cpuCounterDiagnostics"]["reconciled"])
        self.assertTrue(any(reason.startswith("CPU counter evidence:") for reason in result["reasons"]))
        physical = analyze_case(fixture("physical-baseline"), "physical-baseline", 0)
        self.assertTrue(physical["predictionEligible"], physical["reasons"])
        self.assertIsNone(physical["cpuCounterDiagnostics"])
        self.assertIsNone(physical["sampleCreditDiagnostics"])
        self.assertIsNone(physical["sampleMembershipDiagnostics"])

    def test_calibration_reconciles_all_records_and_independent_statistics(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            calibration(root)
            value = parse_calibration(root)
            self.assertEqual((value.mean, value.half_width), (0.5, 0))
            self.assertAlmostEqual(value.sensitivity, 0.01)

    def test_calibration_uses_sample_deviation_and_seven_degree_interval_for_varied_blocks(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            calibration(root, varied=True)
            value = parse_calibration(root)
            self.assertAlmostEqual(value.mean, 0.525, places=12)
            self.assertAlmostEqual(value.half_width, 0.10239123358853752, places=12)
            self.assertAlmostEqual(value.sensitivity, 0.01, places=12)

    def test_calibration_rejects_missing_journal_reordered_plan_and_changed_estimate(self):
        for corruption in ("journal", "plan", "estimate", "work", "clock", "claim", "stub"):
            with tempfile.TemporaryDirectory() as temporary, self.subTest(corruption=corruption):
                root = pathlib.Path(temporary)
                calibration(root)
                result = json.loads((root / "calibration.json").read_text())
                if corruption == "journal":
                    path = root / "calibration.json.blocks.jsonl"
                    path.write_text("\n".join(path.read_text().splitlines()[:-1]) + "\n")
                elif corruption == "plan":
                    path = root / "calibration.json.plan.json"
                    plan = json.loads(path.read_text())
                    plan["plan"][1]["Steps"][0], plan["plan"][1]["Steps"][1] = plan["plan"][1]["Steps"][1], plan["plan"][1]["Steps"][0]
                    path.write_text(json.dumps(plan))
                elif corruption == "estimate":
                    result["analysis"]["Estimate"]["MeanReduction"] = 0.6
                elif corruption == "claim":
                    result["clockAccuracyEstablished"] = True
                elif corruption == "stub":
                    result = {"calibrationComplete": True}
                else:
                    raw = result["blocks"][1]["Segments"][0]["Measurement"]
                    raw["CompletedBatches" if corruption == "work" else "CpuBefore"] = 0
                    (root / "calibration.json.blocks.jsonl").write_text("".join(json.dumps(block) + "\n" for block in result["blocks"]))
                (root / "calibration.json").write_text(json.dumps(result))
                with self.assertRaises((ValueError, KeyError)):
                    parse_calibration(root)

    def test_uses_frozen_global_delta_and_selected_progress_with_brackets(self):
        data = fixture()
        cost = parse_window(data, "profile-half")
        self.assertEqual((cost.global_delta, cost.selected_progress, cost.peer_progress), (500000000, 100, 50))
        self.assertEqual((cost.lower_seconds, cost.upper_seconds), (Fraction(3, 2), Fraction(38, 25)))
        self.assertAlmostEqual(cost.report()["accountedSecondsPerBatchLower"], 1.5 / 101)
        self.assertAlmostEqual(cost.report()["accountedSecondsPerBatchUpper"], 1.52 / 99)
        data.update(progress=[1000000, 2000000], credit=123, waits=[10000000000, 10000000000])
        self.assertEqual(parse_window(data, "profile-half"), cost)

    def test_rejects_nonpositive_corrected_duration_and_progress(self):
        for global_delay, progress in ((2000000000, 100), (0, 0), (0, 1)):
            data = fixture()
            data["measurementCapture"]["end"]["Accounting"]["Global"] = global_delay
            data["measurementCapture"]["end"]["Accounting"]["Selected"]["Progress"] = progress
            with self.subTest(global_delay=global_delay, progress=progress), self.assertRaises(ValueError):
                parse_window(data, "profile-half")

    def test_rejects_clock_regression_frequency_change_and_unfresh_start(self):
        for field, value in (("BeforeTimestamp", 105), ("AfterTimestamp", 2100), ("TimestampFrequency", 999)):
            data = fixture()
            data["measurementCapture"]["end"][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                parse_window(data, "profile-half")
        data = fixture()
        data["measurementCapture"]["start"]["Accounting"]["Selected"]["Progress"] = 1
        with self.assertRaisesRegex(ValueError, "fresh release gate"):
            parse_window(data, "profile-half")

    def test_zero_intervention_cases_have_bounded_costs(self):
        for mode in ("profile-zero", "physical-baseline", "physical-optimized"):
            with self.subTest(mode=mode):
                result = analyze_case(fixture(mode), mode, 0)
                self.assertTrue(result["predictionEligible"], result["reasons"])
                self.assertEqual(result["observedAccountedCost"]["measurementGlobalDeltaNanoseconds"], 0)

    def test_shutdown_payment_cannot_repair_missing_measurement_intervention(self):
        data = fixture()
        data["measurementCapture"]["end"]["Accounting"]["Peer"]["ReleasedWait"] = 0
        result = analyze_case(data, "profile-half", 0)
        self.assertIsNone(result["observedAccountedCost"])
        self.assertIn("inside the frozen window", result["reasons"][0])

    def test_treatment_cannot_claim_eligibility_from_cleanup_or_forged_flags(self):
        data = fixture()
        data.update(sampleTailBoundEstablished=True, journalTailBoundEstablished=True, predictionAccuracyEstablished=True)
        result = analyze_case(data, "profile-half", 0)
        self.assertFalse(result["predictionEligible"])
        self.assertIsNotNone(result["observedAccountedCost"])
        self.assertEqual(len(result["reasons"]), 2)
        self.assertIn("sample-period", result["reasons"][0])
        self.assertIn("journal suffix", result["reasons"][1])

    def test_failed_exit_workload_loss_and_debt_do_not_produce_costs(self):
        changes = ({"failures": ["failed"]}, {"peaks": [0, 2000001]}, {"stableMap": False},
                   {"pending": [{}]}, {"lifetimes": []}, {"initialIntervalNanoseconds": 1000000})
        for change in changes:
            data = fixture()
            data.update(change)
            with self.subTest(change=change):
                self.assertIsNone(analyze_case(data, "profile-half", 0)["observedAccountedCost"])
        data = fixture()
        data["records"]["LostSamples"] = 1
        self.assertIsNone(analyze_case(data, "profile-half", 0)["observedAccountedCost"])
        self.assertIsNone(analyze_case(fixture(), "profile-half", 124)["observedAccountedCost"])

    def test_retains_all_blocks_and_failed_or_missing_cases_without_subset_claim(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            outcomes = series(root)
            outcomes[0]["exitCode"] = 124
            (root / "outcomes.json").write_text(json.dumps(outcomes))
            (root / "block-2-physical-baseline.json").unlink()
            result = analyze(root)
            self.assertEqual((len(result["cases"]), len(result["blocks"])), (32, 8))
            self.assertFalse(result["cases"][0]["predictionEligible"])
            self.assertFalse(result["cases"][6]["predictionEligible"])
            self.assertTrue(result["cases"][-1]["predictionEligible"])
            self.assertFalse(result["collectionComplete"])
            self.assertIsNone(result["predictionSummary"])
            self.assertTrue(all(block["prediction"] is None and not block["predictionEligible"] for block in result["blocks"]))
            self.assertIsNone(result["blocks"][0]["conditionalComparison"])
            self.assertIsNone(result["blocks"][1]["conditionalComparison"])
            self.assertTrue(all(block["conditionalComparison"] is not None for block in result["blocks"][2:]))

    def test_rejects_missing_reordered_or_duplicate_outcomes(self):
        for mutation in (lambda rows: rows[:-1], lambda rows: rows[::-1], lambda rows: rows[:-1] + rows[:1]):
            with tempfile.TemporaryDirectory() as temporary:
                root = pathlib.Path(temporary)
                rows = series(root)
                (root / "outcomes.json").write_text(json.dumps(mutation(rows)))
                with self.assertRaisesRegex(ValueError, "omit, duplicate, or reorder"):
                    analyze(root)

    def test_changed_snapshot_invalidates_every_case(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            series(root)
            path = root / "calibration/calibration.json"
            path.write_text(path.read_text() + "\n")
            result = analyze(root)
            self.assertEqual(len(result["integrityErrors"]), 1)
            self.assertTrue(all(not item["predictionEligible"] for item in result["cases"]))
            self.assertTrue(all(item["conditionalComparison"] is None for item in result["blocks"]))

    def test_missing_hashes_and_runner_failure_cannot_complete_collection(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            series(root)
            (root / "runner-errors.json").write_text('["failed to persist outcome"]')
            result = analyze(root)
            self.assertFalse(result["collectionComplete"])
            self.assertEqual(result["runnerErrors"], ["failed to persist outcome"])
            self.assertTrue(all(item["conditionalComparison"] is None for item in result["blocks"]))
            (root / "manifest.json").write_text('{"files": {}}')
            with self.assertRaisesRegex(ValueError, "Required snapshot hashes"):
                analyze(root)

    def test_invalid_hash_metadata_has_a_structured_cli_rejection_and_cannot_overwrite_it(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            series(root)
            manifest = json.loads((root / "manifest.json").read_text())
            manifest["files"]["plan.json"] = None
            (root / "manifest.json").write_text(json.dumps(manifest))
            command = [sys.executable, str(pathlib.Path(__file__).with_name("AnalyzeMatchedPrediction.py")), str(root)]
            first = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(first.returncode, 1, first.stderr)
            result_path = root / "analysis.json"
            original = result_path.read_bytes()
            self.assertEqual(json.loads(original)["error"], "Invalid snapshot hash metadata")
            second = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(second.returncode, 1)
            self.assertIn("Analysis already exists", second.stderr)
            self.assertNotIn("Traceback", second.stderr)
            self.assertEqual(result_path.read_bytes(), original)

    def test_nonfinite_case_evidence_produces_complete_cli_report(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            series(root)
            for block, token in ((1, "1e999"), (2, "NaN")):
                path = root / f"block-{block}-profile-zero.json"
                path.write_text(path.read_text()[:-1] + ', "extra": ' + token + '}')
            command = [sys.executable, str(pathlib.Path(__file__).with_name("AnalyzeMatchedPrediction.py")), str(root)]
            run = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(run.returncode, 1, run.stderr)
            self.assertNotIn("Traceback", run.stderr)
            result = json.loads((root / "analysis.json").read_bytes())
            for block, token in ((1, "1e999"), (2, "NaN")):
                case = next(row for row in result["cases"] if row["name"] == f"block-{block}-profile-zero")
                self.assertEqual(case["reasons"], [f"Non-finite JSON number: {token}"])
                self.assertIsNone(result["blocks"][block - 1]["conditionalComparison"])
            self.assertTrue(all(row["conditionalComparison"] is not None for row in result["blocks"][2:]))
            self.assertFalse(result["predictionAccuracyEstablished"])

    def test_boolean_is_not_a_clock_counter(self):
        data = fixture()
        data["timestampFrequency"] = True
        with self.assertRaisesRegex(ValueError, "timestamp frequency"):
            parse_window(data, "profile-half")


if __name__ == "__main__":
    unittest.main()
