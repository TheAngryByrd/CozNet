from copy import deepcopy
import hashlib
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest

from AnalyzeCpuCounters import analyze_counter_capture


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
