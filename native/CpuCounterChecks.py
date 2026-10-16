from copy import deepcopy
import hashlib
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest

from AnalyzeCpuCounters import analyze_counter_capture, analyze_sample_credits


def fixture():
    def reading(epoch, enabled, before, after, count):
        return {"SourceIdentity": "source-1", "Epoch": epoch, "Enabled": enabled, "Before": before, "After": after,
                "TimestampFrequency": 1000, "CpuNanoseconds": count, "Error": None}

    def transition(epoch, enabling, before, after):
        return {"Epoch": epoch, "Enabling": enabling, "Before": before, "After": after,
                "TimestampFrequency": 1000, "Completed": True, "Error": None}

    samples = []
    for index, (count, receipt, accepted, method, ip) in enumerate(((30, 200, True, "selected", 10),
                                                                (60, 1000, True, "selected", 10),
                                                                (110, 2250, False, None, 20))):
        samples.append({"Sample": {"ProcessId": 7, "ThreadId": 14, "Timestamp": (index + 1) * 100,
                                   "PeriodNanoseconds": 10, "InstructionPointer": ip, "Method": method,
                                   "CumulativeCpuNanoseconds": count}, "Timestamp": receipt, "Accepted": accepted})
    return {"timestampFrequency": 1000, "failures": [], "closed": True, "samples": samples,
            "measurementCapture": {"status": "Complete",
                                   "start": {"BeforeTimestamp": 100, "AfterTimestamp": 110, "TimestampFrequency": 1000,
                                             "Accounting": {"AcceptedSampleSequence": 0}},
                                   "end": {"BeforeTimestamp": 2110, "AfterTimestamp": 2120, "TimestampFrequency": 1000,
                                           "Accounting": {"AcceptedSampleSequence": 2}}},
            "records": {"Samples": 3, "DistinctInstructionPointers": 2, "RepresentedCpuNanoseconds": 30,
                        "LostRecords": 0, "LostSamples": 0, "Throttles": 0, "SelectedMethodSamples": 2,
                        "UnmappedSamples": 1, "SelectedSymbols": ["selected"]},
            "cpuCounters": {"startBefore": reading(0, False, 90, 95, 0), "startAfter": reading(1, True, 120, 125, 10),
                            "endBefore": reading(2, True, 2090, 2100, 90), "endAfter": reading(2, True, 2130, 2140, 100),
                            "enabledCountRange": {"Lower": 80, "Upper": 100}, "rejection": None,
                            "source": {"SourceIdentity": "source-1", "Units": "task-clock-nanoseconds", "Enabled": False,
                                       "Epoch": 2, "Overflowed": False, "UnrecordedCleanup": False, "FailedReads": 0,
                                       "FirstReadFailure": None,
                                       "Transitions": [transition(1, True, 102, 104), transition(1, False, 500, 510),
                                                       transition(2, True, 700, 710), transition(2, False, 2200, 2210)],
                                       "LastReading": reading(2, False, 2220, 2230, 120)}}}


def credit_fixture(percent=50):
    data = fixture()
    data.update(percent=percent, credit=5 if percent else 0)
    for index, sample in enumerate(data["samples"]):
        sample.update(Epoch=1, Credit=data["credit"] if index == 1 else 0)
    data["measurementCapture"]["start"]["Accounting"].update(Credit=0, Epoch=1)
    data["measurementCapture"]["end"]["Accounting"].update(Credit=data["credit"], Epoch=1)
    return data


def nominal_credit_fixture(percent=50):
    data = credit_fixture(percent)
    def scale(value):
        if isinstance(value, dict):
            for key in value:
                if key.endswith("CpuNanoseconds") or key == "PeriodNanoseconds":
                    value[key] *= 100000
                else:
                    scale(value[key])
        elif isinstance(value, list):
            for item in value:
                scale(item)
    scale(data)
    data["cpuCounters"]["enabledCountRange"] = {"Lower": 8000000, "Upper": 10000000}
    data.update(sampleCreditPolicy="nominal-event-v1", sampleCreditEvidenceVersion=1,
                nominalSamplePeriodNanoseconds=1000000, credit=percent * 20000)
    for index, raw in enumerate(data["samples"]):
        raw["Credit"] = percent * 10000 if index < 2 else 0
    data["measurementCapture"]["end"]["Accounting"]["Credit"] = data["credit"]
    return data


class SampleCreditChecks(unittest.TestCase):
    def test_nominal_policy_credits_first_and_resumed_events(self):
        for percent in (0, 50):
            data = nominal_credit_fixture(percent)
            data["samples"][1]["Epoch"] = data["samples"][2]["Epoch"] = 2
            data["measurementCapture"]["end"]["Accounting"]["Epoch"] = 2
            result = analyze_sample_credits(data)
            self.assertTrue(result["reconciled"], result["reasons"])
            self.assertEqual((2, 0, percent * 20000),
                             (result["qualifiedSamples"], result["epochExcludedSamples"], result["creditNanoseconds"]))
            self.assertEqual("nominal-event-v1", result["sampleCreditPolicy"])

    def test_versioned_interval_preserves_legacy_credit(self):
        data = credit_fixture()
        expected = analyze_sample_credits(data)
        data.update(sampleCreditPolicy="same-epoch-interval-v1", sampleCreditEvidenceVersion=1,
                    nominalSamplePeriodNanoseconds=None)
        self.assertEqual(expected, analyze_sample_credits(data))

    def test_nominal_policy_does_not_credit_unmapped_or_unaccepted_events(self):
        data = nominal_credit_fixture()
        data["samples"][0]["Sample"].update(Method=None, InstructionPointer=20)
        data["samples"][0]["Credit"] = 0
        data["credit"] = data["measurementCapture"]["end"]["Accounting"]["Credit"] = 500000
        data["records"].update(SelectedMethodSamples=1, UnmappedSamples=2)
        result = analyze_sample_credits(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual((1, 1, 1, 500000), (result["qualifiedSamples"], result["locationExcludedSamples"],
                         result["unacceptedSamples"], result["creditNanoseconds"]))

    def test_credit_policy_rejects_partial_and_invalid_declarations(self):
        fields = ("sampleCreditPolicy", "sampleCreditEvidenceVersion", "nominalSamplePeriodNanoseconds")
        for field in fields:
            data = nominal_credit_fixture()
            del data[field]
            self.assertEqual(["Incomplete timer sample credit policy"], analyze_sample_credits(data)["reasons"])
        for field, value, message in (
            (fields[0], "unknown", "Unknown timer sample credit policy"),
            (fields[1], True, "Invalid timer sample credit version"),
            (fields[1], 2, "Invalid timer sample credit version"),
            (fields[2], True, "Invalid timer nominal sample period"),
            (fields[2], 80000, "Invalid timer nominal sample period"),
            (fields[0], "same-epoch-interval-v1", "Invalid timer nominal sample period")):
            data = nominal_credit_fixture()
            data[field] = value
            self.assertEqual([message], analyze_sample_credits(data)["reasons"])

    def test_nominal_policy_rejects_tail_period_mismatch_and_changed_credit(self):
        data = nominal_credit_fixture()
        data["samples"][2]["Sample"]["PeriodNanoseconds"] = 80000
        data["records"]["RepresentedCpuNanoseconds"] = 2080000
        self.assertEqual(["Timer sample period differs from nominal policy"], analyze_sample_credits(data)["reasons"])
        data = nominal_credit_fixture()
        data["samples"][0]["Credit"] = 0
        self.assertEqual(["Sample credit differs at record 0"], analyze_sample_credits(data)["reasons"])

    def test_credit_cli_preserves_failure_evidence_and_rejects_invalid_credit(self):
        script = pathlib.Path(__file__).with_name("AnalyzeCpuCounters.py")
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            source, output = root / "input.json", root / "output.json"
            data = credit_fixture()
            data.update(failures=["debt exceeded"], closed=False)
            source.write_text(json.dumps(data))
            command = [sys.executable, str(script), str(source), str(output), "--sample-credits"]
            process = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(process.returncode, 0, process.stderr)
            result = json.loads(output.read_text())
            self.assertTrue(result["reconciled"])
            self.assertTrue(result["sampleCreditDiagnostics"]["reconciled"])
            self.assertEqual(result["counterDiagnostics"]["recordedWorkloadFailures"], ["debt exceeded"])
            self.assertFalse(result["counterDiagnostics"]["nativeSessionClosed"])
            self.assertFalse(result["predictionAccuracyEstablished"])
            self.assertEqual(result["inputSha256"], hashlib.sha256(source.read_bytes()).hexdigest())
            self.assertEqual(result["analyzerSha256"], hashlib.sha256(script.read_bytes()).hexdigest())
            self.assertEqual(result["inputFile"], "input.json")
            data["samples"][1]["Credit"] += 1
            source.write_text(json.dumps(data))
            command[3] = str(root / "rejected.json")
            process = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(process.returncode, 1, process.stderr)
            result = json.loads((root / "rejected.json").read_text())
            self.assertFalse(result["reconciled"])
            self.assertTrue(result["counterDiagnostics"]["reconciled"])
            self.assertFalse(result["sampleCreditDiagnostics"]["reconciled"])

    def test_nominal_credit_replays_at_zero_and_half_speedup(self):
        for percent in (0, 50):
            with self.subTest(percent=percent):
                result = analyze_sample_credits(credit_fixture(percent))
                self.assertTrue(result["reconciled"], result["reasons"])
                self.assertEqual(result["creditNanoseconds"], 5 if percent else 0)
                self.assertEqual(result["qualifiedNominalNanoseconds"], 10)
                self.assertEqual((result["acceptedSamples"], result["qualifiedSamples"],
                                  result["epochExcludedSamples"], result["unacceptedSamples"]), (2, 1, 1, 1))
                self.assertFalse(result["eligibilityTransitionsVerified"])
                self.assertFalse(result["selectedCpuBoundEstablished"])
                self.assertFalse(result["predictionAccuracyEstablished"])

    def test_unaccepted_epoch_does_not_replace_credit_baseline(self):
        data = credit_fixture()
        data["samples"][1].update(Accepted=False, Epoch=2, Credit=0)
        data["samples"][2].update(Accepted=True, Epoch=2, Credit=0, Timestamp=1500)
        data["samples"][2]["Sample"].update(CumulativeCpuNanoseconds=80, Method="selected", InstructionPointer=10)
        data["records"].update(SelectedMethodSamples=3, UnmappedSamples=0, DistinctInstructionPointers=1)
        data["measurementCapture"]["end"]["Accounting"].update(Credit=0, Epoch=2)
        data["credit"] = 0
        result = analyze_sample_credits(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual((result["qualifiedSamples"], result["epochExcludedSamples"], result["unacceptedSamples"]), (0, 2, 1))
        self.assertEqual(result["creditNanoseconds"], 0)

    def test_fractional_credit_survives_excluded_location(self):
        data = credit_fixture()
        template = deepcopy(data["samples"][0])
        data["samples"] = []
        for index, method in enumerate(("selected", "selected", None, "selected")):
            sample = deepcopy(template)
            sample.update(Timestamp=200 + index * 200, Credit=1 if index == 3 else 0)
            sample["Sample"].update(Timestamp=100 + index * 100, CumulativeCpuNanoseconds=30 + index * 10,
                                    PeriodNanoseconds=1, Method=method, InstructionPointer=20 if method is None else 10)
            data["samples"].append(sample)
        data["records"].update(Samples=4, RepresentedCpuNanoseconds=4, SelectedMethodSamples=3)
        data["measurementCapture"]["end"]["Accounting"].update(AcceptedSampleSequence=4, Credit=1)
        data["credit"] = 1
        result = analyze_sample_credits(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual((result["qualifiedSamples"], result["locationExcludedSamples"],
                          result["qualifiedNominalNanoseconds"], result["creditNanoseconds"],
                          result["remainderHundredthsNanosecond"]), (2, 1, 2, 1, 0))
        data["samples"][-1]["Sample"]["PeriodNanoseconds"] = 2
        data["records"]["RepresentedCpuNanoseconds"] = 5
        result = analyze_sample_credits(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual(result["remainderHundredthsNanosecond"], 50)

    def test_rejects_changed_record_total_and_frozen_credits(self):
        for target in ("sample", "unaccepted", "total", "frozen"):
            with self.subTest(target=target):
                data = credit_fixture()
                if target == "sample":
                    data["samples"][1]["Credit"] += 1
                elif target == "unaccepted":
                    data["samples"][2]["Credit"] = 1
                elif target == "total":
                    data["credit"] += 1
                else:
                    data["measurementCapture"]["end"]["Accounting"]["Credit"] += 1
                self.assertFalse(analyze_sample_credits(data)["reconciled"])

    def test_rejects_invalid_epochs_percentages_and_missing_evidence(self):
        for field, value in (("Epoch", 0), ("Epoch", 2), ("Epoch", True), ("Credit", True), ("Credit", -1)):
            with self.subTest(field=field, value=value):
                data = credit_fixture()
                data["samples"][1][field] = value
                self.assertFalse(analyze_sample_credits(data)["reconciled"])
        for percent in (True, -1, 25, 100):
            data = credit_fixture()
            data["percent"] = percent
            self.assertFalse(analyze_sample_credits(data)["reconciled"])
        for field in ("Credit", "Epoch"):
            data = credit_fixture()
            del data["samples"][0][field]
            self.assertFalse(analyze_sample_credits(data)["reconciled"])
        data = credit_fixture()
        data["measurementCapture"]["start"]["Accounting"]["Credit"] = 1
        self.assertFalse(analyze_sample_credits(data)["reconciled"])

    def test_counter_failure_rejects_credit_but_workload_failure_does_not_hide_replay(self):
        data = credit_fixture()
        data.update(failures=["debt exceeded"], closed=False)
        result = analyze_sample_credits(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertFalse(result["predictionAccuracyEstablished"])
        data["cpuCounters"]["source"]["FailedReads"] = 1
        self.assertFalse(analyze_sample_credits(data)["reconciled"])

    def test_eligibility_epoch_cannot_regress_within_frozen_epoch_range(self):
        data = credit_fixture()
        data["samples"][0]["Epoch"] = 2
        data["measurementCapture"]["end"]["Accounting"]["Epoch"] = 2
        result = analyze_sample_credits(data)
        self.assertFalse(result["reconciled"])
        self.assertIn("Sample eligibility epochs regress or exceed the frozen endpoint", result["reasons"])

    def test_remainder_survives_an_excluded_epoch_boundary(self):
        data = credit_fixture()
        template = deepcopy(data["samples"][0])
        data["samples"] = []
        for index, epoch in enumerate((1, 1, 2, 2)):
            sample = deepcopy(template)
            sample.update(Epoch=epoch, Timestamp=200 + index * 200, Credit=1 if index == 3 else 0)
            sample["Sample"].update(Timestamp=100 + index * 100, CumulativeCpuNanoseconds=30 + index * 10,
                                    PeriodNanoseconds=1)
            data["samples"].append(sample)
        data["records"].update(Samples=4, RepresentedCpuNanoseconds=4, SelectedMethodSamples=4,
                               UnmappedSamples=0, DistinctInstructionPointers=1)
        data["measurementCapture"]["end"]["Accounting"].update(AcceptedSampleSequence=4, Credit=1, Epoch=2)
        data["credit"] = 1
        result = analyze_sample_credits(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual((result["qualifiedSamples"], result["epochExcludedSamples"],
                          result["creditNanoseconds"], result["remainderHundredthsNanosecond"]), (2, 2, 1, 0))

    def test_unaccepted_record_preserves_fractional_credit(self):
        data = credit_fixture()
        template = deepcopy(data["samples"][0])
        data["samples"] = []
        for index in range(4):
            sample = deepcopy(template)
            sample.update(Accepted=index != 2, Timestamp=200 + index * 200, Credit=1 if index == 3 else 0)
            sample["Sample"].update(Timestamp=100 + index * 100, CumulativeCpuNanoseconds=30 + index * 10,
                                    PeriodNanoseconds=1)
            data["samples"].append(sample)
        data["records"].update(Samples=4, RepresentedCpuNanoseconds=4, SelectedMethodSamples=4,
                               UnmappedSamples=0, DistinctInstructionPointers=1)
        data["measurementCapture"]["end"]["Accounting"].update(AcceptedSampleSequence=3, Credit=1)
        data["credit"] = 1
        result = analyze_sample_credits(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual((result["qualifiedSamples"], result["unacceptedSamples"],
                          result["creditNanoseconds"], result["remainderHundredthsNanosecond"]), (2, 1, 1, 0))


class CpuCounterChecks(unittest.TestCase):
    def test_capture_requires_a_fresh_disabled_event(self):
        for field, value in (("CpuNanoseconds", 1), ("Epoch", 1), ("Enabled", True)):
            with self.subTest(field=field):
                data = fixture()
                data["cpuCounters"]["startBefore"][field] = value
                result = analyze_counter_capture(data)
                self.assertFalse(result["reconciled"])
                self.assertIn("Counter capture did not start at a fresh disabled event", result["reasons"])

    def test_missing_or_malformed_evidence_is_rejected(self):
        for data in (None, [], {}, {"timestampFrequency": True}, fixture()):
            with self.subTest(data=data):
                if isinstance(data, dict) and "cpuCounters" in data:
                    del data["cpuCounters"]
                result = analyze_counter_capture(data)
                self.assertFalse(result["reconciled"])
                self.assertFalse(result["predictionAccuracyEstablished"])

    def test_fractional_nanoseconds_round_outward(self):
        data = fixture()
        data["timestampFrequency"] = 3
        for endpoint in ("start", "end"):
            data["measurementCapture"][endpoint]["TimestampFrequency"] = 3
        counters = data["cpuCounters"]
        for name in ("startBefore", "startAfter", "endBefore", "endAfter"):
            counters[name]["TimestampFrequency"] = 3
        for transition in counters["source"]["Transitions"]:
            transition["TimestampFrequency"] = 3
        counters["source"]["LastReading"]["TimestampFrequency"] = 3
        result = analyze_counter_capture(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual(result["disabledElapsed"]["lowerNanosecondsFloor"], 63333333333)
        self.assertEqual(result["disabledElapsed"]["upperNanosecondsCeiling"], 71333333334)

    def test_equal_transition_and_read_boundaries_are_permitted(self):
        data = fixture()
        counters = data["cpuCounters"]
        counters["startBefore"]["After"] = 100
        counters["startAfter"]["Before"] = 110
        counters["source"]["Transitions"][0].update(Before=100, After=110)
        result = analyze_counter_capture(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual(result["disabledElapsed"]["upperTicks"], 220)

    def test_recomputes_count_prefix_suffix_and_disabled_intervals(self):
        result = analyze_counter_capture(fixture())
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual(result["enabledCountRange"], {"lower": 80, "upper": 100})
        self.assertEqual(result["countBeforeFirstAccepted"], {"lower": 20, "upper": 30})
        self.assertEqual(result["countAfterLastAccepted"], {"lower": 30, "upper": 40})
        self.assertEqual((result["acceptedSamples"], result["unacceptedSamples"]), (2, 1))
        self.assertEqual(result["disabledElapsed"], {"lowerTicks": 190, "upperTicks": 214, "timestampFrequency": 1000,
            "lowerNanosecondsFloor": 190000000, "upperNanosecondsCeiling": 214000000,
            "intervals": [{"disableTransition": None, "enableTransition": 0, "lowerTicks": 0, "upperTicks": 4},
                          {"disableTransition": 1, "enableTransition": 2, "lowerTicks": 190, "upperTicks": 210}]})

    def test_shutdown_samples_do_not_replace_frozen_suffix(self):
        data = fixture()
        original = analyze_counter_capture(data)
        late = deepcopy(data["samples"][-1])
        late["Timestamp"] = 2300
        late["Sample"].update(Timestamp=400, CumulativeCpuNanoseconds=600)
        data["samples"].append(late)
        data["records"].update(Samples=4, UnmappedSamples=2, RepresentedCpuNanoseconds=40)
        data["cpuCounters"]["source"]["LastReading"]["CpuNanoseconds"] = 700
        result = analyze_counter_capture(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual(result["countAfterLastAccepted"], original["countAfterLastAccepted"])
        self.assertEqual(result["enabledCountRange"], original["enabledCountRange"])

    def test_missing_accepted_samples_leave_the_whole_enabled_range_unreceived(self):
        data = fixture()
        for sample in data["samples"]:
            sample["Accepted"] = False
        data["measurementCapture"]["end"]["Accounting"]["AcceptedSampleSequence"] = 0
        result = analyze_counter_capture(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertIsNone(result["countBeforeFirstAccepted"])
        self.assertEqual(result["countAfterLastAccepted"], {"lower": 80, "upper": 100})

    def test_prefix_crossing_start_count_keeps_zero_lower_bound(self):
        data = fixture()
        data["samples"][0]["Sample"]["CumulativeCpuNanoseconds"] = 5
        result = analyze_counter_capture(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual(result["countBeforeFirstAccepted"], {"lower": 0, "upper": 5})

    def test_failed_workload_and_cleanup_are_retained_without_accuracy_claims(self):
        data = fixture()
        data.update(failures=["debt exceeded", "invalid native journal"], closed=False, predictionAccuracyEstablished=True)
        data["cpuCounters"].update(selectedCpuBoundEstablished=True, missingSampleTailBoundEstablished=True)
        result = analyze_counter_capture(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual(result["recordedWorkloadFailures"], data["failures"])
        self.assertFalse(result["nativeSessionClosed"])
        for key in ("selectedCpuBoundEstablished", "missingSampleTailBoundEstablished", "disabledCpuBoundEstablished", "predictionAccuracyEstablished"):
            self.assertIs(result[key], False)
        data["cpuCounters"]["source"]["FailedReads"] = 1
        rejected = analyze_counter_capture(data)
        self.assertFalse(rejected["reconciled"])
        self.assertEqual(rejected["recordedWorkloadFailures"], data["failures"])
        self.assertIsNone(rejected["disabledElapsed"])

    def test_rejects_forged_ranges_and_failed_or_mixed_counter_readings(self):
        corruptions = (("enabledCountRange", "Lower", 79), ("startBefore", "SourceIdentity", "other"),
                       ("startAfter", "Epoch", 2), ("startAfter", "Enabled", False),
                       ("endBefore", "CpuNanoseconds", 9), ("endAfter", "TimestampFrequency", 1000000),
                       ("endAfter", "Error", "read failed"), ("startBefore", "CpuNanoseconds", True),
                       ("endBefore", "Before", 120), ("endBefore", "After", 2111))
        for section, key, value in corruptions:
            with self.subTest(section=section, key=key):
                data = fixture()
                data["cpuCounters"][section][key] = value
                self.assertFalse(analyze_counter_capture(data)["reconciled"])

    def test_rejects_invalid_transition_history_and_final_count(self):
        for field, value in (("Completed", False), ("Enabling", False), ("Epoch", 2), ("Before", 99),
                             ("After", 111), ("TimestampFrequency", 1000000), ("Error", "failed")):
            with self.subTest(field=field):
                data = fixture()
                data["cpuCounters"]["source"]["Transitions"][0][field] = value
                self.assertFalse(analyze_counter_capture(data)["reconciled"])
        for field, value in (("Units", "ticks"), ("Overflowed", True), ("UnrecordedCleanup", True),
                             ("FailedReads", 1), ("FirstReadFailure", "failed"), ("Epoch", 3), ("Enabled", True)):
            with self.subTest(field=field):
                data = fixture()
                data["cpuCounters"]["source"][field] = value
                self.assertFalse(analyze_counter_capture(data)["reconciled"])
        data = fixture()
        data["cpuCounters"]["source"]["LastReading"]["CpuNanoseconds"] = 109
        self.assertFalse(analyze_counter_capture(data)["reconciled"])

    def test_rejects_sample_loss_totals_regression_identity_and_late_acceptance(self):
        for field in ("Samples", "DistinctInstructionPointers", "RepresentedCpuNanoseconds", "SelectedMethodSamples", "UnmappedSamples", "LostRecords", "LostSamples", "Throttles"):
            with self.subTest(field=field):
                data = fixture()
                data["records"][field] += 1
                self.assertFalse(analyze_counter_capture(data)["reconciled"])
        for field, value in (("ProcessId", 8), ("ThreadId", 15), ("Timestamp", 99), ("CumulativeCpuNanoseconds", 29),
                             ("CumulativeCpuNanoseconds", None), ("CumulativeCpuNanoseconds", 121)):
            with self.subTest(field=field, value=value):
                data = fixture()
                data["samples"][1]["Sample"][field] = value
                self.assertFalse(analyze_counter_capture(data)["reconciled"])
        for key, value in (("Accepted", 1), ("Timestamp", 2091), ("Timestamp", 199)):
            with self.subTest(key=key):
                data = fixture()
                data["samples"][1][key] = value
                self.assertFalse(analyze_counter_capture(data)["reconciled"])
        data = fixture()
        data["measurementCapture"]["end"]["Accounting"]["AcceptedSampleSequence"] = 3
        self.assertFalse(analyze_counter_capture(data)["reconciled"])

    def test_disabled_counter_cannot_advance_without_enable(self):
        data = fixture()
        source = data["cpuCounters"]["source"]
        source["Transitions"][-1].update(Before=2000, After=2010)
        data["cpuCounters"]["endBefore"]["Enabled"] = False
        data["cpuCounters"]["endAfter"]["Enabled"] = False
        self.assertFalse(analyze_counter_capture(data)["reconciled"])
        data["cpuCounters"]["endAfter"]["CpuNanoseconds"] = 90
        data["cpuCounters"]["enabledCountRange"]["Upper"] = 90
        source["LastReading"]["CpuNanoseconds"] = 90
        data["samples"][-1]["Sample"]["CumulativeCpuNanoseconds"] = 90
        result = analyze_counter_capture(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual(result["disabledElapsed"]["lowerTicks"], 290)
        self.assertEqual(result["disabledElapsed"]["upperTicks"], 334)

        source["LastReading"]["CpuNanoseconds"] = 91
        rejected = analyze_counter_capture(data)
        self.assertFalse(rejected["reconciled"])
        self.assertIn("A disabled counter advanced without an enable", rejected["reasons"])

    def test_final_snapshot_can_equal_disabled_end_reading(self):
        data = fixture()
        counters = data["cpuCounters"]
        counters["source"]["Transitions"][-1].update(Before=2112, After=2115)
        counters["endAfter"]["Enabled"] = False
        counters["source"]["LastReading"] = deepcopy(counters["endAfter"])
        data["samples"][-1]["Sample"]["CumulativeCpuNanoseconds"] = 100
        result = analyze_counter_capture(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual(result["enabledCountRange"], {"lower": 80, "upper": 100})
        counters["source"]["LastReading"]["Before"] += 1
        rejected = analyze_counter_capture(data)
        self.assertFalse(rejected["reconciled"])
        self.assertIn("Final counter reading does not follow disabled source completion", rejected["reasons"])

    def test_cli_binds_declared_policy_to_runner(self):
        script = pathlib.Path(__file__).with_name("AnalyzeCpuCounters.py")
        cases = ((nominal_credit_fixture(), "nominal-event-v1", 0, None),
                 (nominal_credit_fixture(), "same-epoch-interval-v1", 1, "differs from runner"),
                 (credit_fixture(), "same-epoch-interval-v1", 1, "lacks declared"))
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            for index, (data, policy, expected_exit, reason) in enumerate(cases):
                with self.subTest(policy=policy, index=index):
                    source, output = root / f"input-{index}.json", root / f"output-{index}.json"
                    source.write_text(json.dumps(data))
                    result = subprocess.run([sys.executable, str(script), str(source), str(output),
                                             "--sample-credits", "--expected-policy", policy],
                                            capture_output=True, text=True)
                    self.assertEqual(result.returncode, expected_exit, result.stderr)
                    report = json.loads(output.read_text())
                    self.assertEqual(report["reconciled"], expected_exit == 0)
                    if reason:
                        self.assertTrue(any(reason in item for item in report["reasons"]), report)
                    else:
                        self.assertEqual(report["sampleCreditDiagnostics"]["sampleCreditPolicy"], policy)

    def test_cli_pins_input_and_never_overwrites_existing_output(self):
        script = pathlib.Path(__file__).with_name("AnalyzeCpuCounters.py")
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            source, output = root / "input.json", root / "output.json"
            source.write_text(json.dumps(fixture()))
            result = subprocess.run([sys.executable, str(script), str(source), str(output)], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            content = output.read_bytes()
            self.assertEqual(json.loads(content)["inputSha256"], hashlib.sha256(source.read_bytes()).hexdigest())
            self.assertEqual(json.loads(content)["analyzerSha256"], hashlib.sha256(script.read_bytes()).hexdigest())
            repeated = subprocess.run([sys.executable, str(script), str(source), str(output)], capture_output=True, text=True)
            self.assertNotEqual(repeated.returncode, 0)
            self.assertIn("already exists", repeated.stderr)
            self.assertEqual(output.read_bytes(), content)
            source.write_text("invalid json")
            malformed = root / "malformed.json"
            rejected = subprocess.run([sys.executable, str(script), str(source), str(malformed)], capture_output=True, text=True)
            self.assertNotEqual(rejected.returncode, 0)
            self.assertFalse(json.loads(malformed.read_text())["reconciled"])
            self.assertEqual(json.loads(malformed.read_text())["inputSha256"], hashlib.sha256(source.read_bytes()).hexdigest())


if __name__ == "__main__":
    unittest.main()
