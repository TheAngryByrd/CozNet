import copy
import hashlib
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest
from fractions import Fraction
from unittest.mock import patch

import AnalyzeCooperativeComparison as subject
import BoundSelectedPaymentAnalysisChecks as fixtures


def physical_record(mode="baseline"):
    def boundary(before, after, selected, peer):
        def worker(progress):
            return dict(Progress=progress, ReleasedWait=0, ActiveWait=0, Pending=None, Ledger=None)
        return dict(BeforeTimestamp=before, AfterTimestamp=after, TimestampFrequency=1000000000,
                    Accounting=dict(Global=0, Credit=0, AcceptedSampleSequence=0, Epoch=1,
                                    Selected=worker(selected), Peer=worker(peer), Timer=None, Cooperative=None))
    return dict(workloadProtocol=subject.WORKLOAD, mode=mode, completed=True, joined=True, failures=[],
                expectedChecksum=49995000, predictionAccuracyEstablished=False, requestedBatches=5,
                selectedBatches=5, checksum=249975000, peerBatches=12, peerChecksum=599940000,
                measurementWindow=dict(Status=2, Start=boundary(100, 110, 0, 2), End=boundary(200, 220, 5, 10)),
                timestampFrequency=1000000000, selectedWorkStarted=110, selectedWorkFinished=200)


def sampled_record():
    record = fixtures.CooperativeMeasurementEvidenceChecks().finite_record()
    record.update(workloadProtocol=subject.WORKLOAD, directReceiptReadyEnabled=True, receiptReadyCalls=0,
                  selectedHandoffEnabled=False, selectedHandoffAttempts=0)
    return record


class CooperativeComparisonChecks(unittest.TestCase):
    def test_physical_cost_uses_frozen_progress_and_clock_brackets(self):
        result = subject.analyze_case(physical_record(), "physical-baseline", 5, 0)
        self.assertEqual(("complete", [], subject.Cost(Fraction(18), Fraction(24), 5)), result)

    def test_physical_rejects_changed_work_payment_and_cleanup(self):
        mutations = (
            lambda r: r.update(completed=False), lambda r: r.update(joined=False),
            lambda r: r.update(failures=["cleanup failed"]), lambda r: r.update(mode="optimized"),
            lambda r: r.update(checksum=249974999), lambda r: r.update(peerChecksum=599939999),
            lambda r: r.update(requestedBatches=4), lambda r: r.update(predictionAccuracyEstablished=True),
            lambda r: r.update(selectedWorkStarted=109), lambda r: r.update(selectedWorkFinished=201),
            lambda r: r["measurementWindow"]["End"]["Accounting"]["Peer"].update(Progress=2),
            lambda r: r["measurementWindow"]["End"]["Accounting"]["Peer"].update(Progress=13),
            lambda r: r["measurementWindow"]["End"]["Accounting"].update(Global=1),
            lambda r: r["measurementWindow"]["End"]["Accounting"]["Peer"].update(ActiveWait=1),
        )
        for index, mutate in enumerate(mutations):
            with self.subTest(mutation=index):
                record = physical_record()
                mutate(record)
                with self.assertRaises(ValueError):
                    subject.analyze_case(record, "physical-baseline", 5, 0)

    def test_physical_rejects_boolean_and_float_counters(self):
        for path in (("expectedChecksum",), ("timestampFrequency",), ("requestedBatches",),
                     ("measurementWindow", "End", "TimestampFrequency"),
                     ("measurementWindow", "Start", "Accounting", "Epoch"),
                     ("measurementWindow", "Start", "Accounting", "Credit"),
                     ("measurementWindow", "Start", "Accounting", "Selected", "ReleasedWait")):
            for numeric_type in (bool, float):
                with self.subTest(path=path, numeric_type=numeric_type):
                    record = physical_record()
                    parent = record
                    for key in path[:-1]:
                        parent = parent[key]
                    parent[path[-1]] = numeric_type(parent[path[-1]])
                    with self.assertRaises(ValueError):
                        subject.analyze_case(record, "physical-baseline", 5, 0)

    def test_sampled_arm_uses_full_replay_and_global_delay(self):
        record = sampled_record()
        state, reasons, result = subject.analyze_case(record, "sampled-half", 5, 0)
        self.assertEqual("unqualified", state)
        self.assertIn("instrumentation-attribution-unqualified", reasons)
        self.assertEqual(subject.Cost(Fraction(92, 5), Fraction(92, 5), 5), result)
        record["credit"] += 1
        with self.assertRaises(ValueError):
            subject.analyze_case(record, "sampled-half", 5, 0)

    def test_sampled_nonzero_exit_preserves_failure_without_cost(self):
        state, reasons, result = subject.analyze_case(sampled_record(), "sampled-half", 5, 137)
        self.assertEqual("invalid", state)
        self.assertIn("process-exit-code:137", reasons)
        self.assertIsNone(result)
        with self.assertRaises(ValueError):
            subject.analyze_case(sampled_record(), "sampled-half", 5, False)

    def test_sampled_policy_must_match_declared_arm(self):
        for key, value in (("directReceiptReadyEnabled", False), ("selectedHandoffEnabled", True),
                           ("requestedBatches", 6), ("percent", 0), ("workloadProtocol", "old")):
            with self.subTest(key=key):
                record = sampled_record()
                record[key] = value
                with self.assertRaises(ValueError):
                    subject.analyze_case(record, "sampled-half", 5, 0)

    def test_cost_rejects_nonpositive_effective_duration_and_regression(self):
        for mutate in (lambda w: w["End"].update(BeforeTimestamp=109),
                       lambda w: w["End"]["Accounting"].update(Global=90),
                       lambda w: w["End"]["Accounting"]["Selected"].update(Progress=0),
                       lambda w: w["End"].update(TimestampFrequency=1),
                       lambda w: w.update(Status=True)):
            window = physical_record()["measurementWindow"]
            mutate(window)
            with self.assertRaises(ValueError):
                subject.cost(window)

    def test_fraction_bounds_round_outward(self):
        values = subject.outward(Fraction(1, 10), Fraction(1, 3))
        self.assertLessEqual(Fraction(values["lower"]), Fraction(1, 10))
        self.assertGreaterEqual(Fraction(values["upper"]), Fraction(1, 3))
        self.assertLess(Fraction(1, 10) - Fraction(values["lower"]), Fraction(1, 10**15))
        self.assertLess(Fraction(values["upper"]) - Fraction(1, 3), Fraction(1, 10**15))

    def test_comparison_divides_opposite_interval_endpoints(self):
        costs = dict(zip(subject.MODES, (subject.Cost(Fraction(10), Fraction(12), 5),
                                        subject.Cost(Fraction(4), Fraction(6), 5),
                                        subject.Cost(Fraction(20), Fraction(24), 5),
                                        subject.Cost(Fraction(10), Fraction(12), 5))))
        result = subject.compare(costs)
        self.assertEqual(subject.outward(Fraction(2, 5), Fraction(7, 12)), result["predictedElapsedReduction"])
        self.assertEqual(subject.outward(Fraction(2, 5), Fraction(2, 3)), result["physicalElapsedReduction"])
        self.assertEqual(subject.outward(Fraction(-4, 15), Fraction(11, 60)), result["nominalPredictionMinusPhysical"])
        self.assertIs(result["isAccuracyBound"], False)

    def test_outcome_parser_rejects_inconsistent_process_states(self):
        valid = dict(attempted=True, executed=True, recordExists=True, exitCode=0)
        self.assertEqual(subject.Outcome(True, True, True, 0), subject.Outcome.parse(valid))
        for changes in (dict(attempted=False), dict(executed=False), dict(exitCode=False),
                        dict(attempted=1), dict(recordExists=1), dict(exitCode=None)):
            with self.subTest(changes=changes), self.assertRaises(ValueError):
                subject.Outcome.parse(dict(valid, **changes))
        self.assertEqual(subject.Outcome(False, False, False, None),
                         subject.Outcome.parse(dict(attempted=False, executed=False, recordExists=False, exitCode=None)))

    def test_json_rejects_duplicate_fields_and_nonfinite_numbers(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "record.json"
            for contents in ('{"passed": false, "passed": true}', '{"value": 1e999}', '{"value": NaN}'):
                path.write_text(contents, encoding="utf-8")
                with self.assertRaises(ValueError):
                    subject.read(path)


class ComparisonCollectionChecks(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = pathlib.Path(self.directory.name)
        self.save("plan.json", dict(protocol=subject.PROTOCOL, order=list(subject.MODES), selectedBatches=5,
                                    predictionErrorBound=0.02, localOptimizationCalibrated=False))
        files = {}
        for name in subject.REQUIRED_FILES:
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            if not path.exists():
                path.write_text("fixture", encoding="utf-8")
            files[name] = hashlib.sha256(path.read_bytes()).hexdigest()
        self.manifest = dict(files=files, runtimeImage="sha256:" + "a" * 64)
        self.save("manifest.json", self.manifest)
        self.outcomes = [dict(mode=mode, attempted=True, executed=True, recordExists=True, exitCode=0)
                         for mode in subject.MODES]
        self.save("outcomes.json", self.outcomes)
        self.save("integrity.json", dict(originalInputsUnchanged=True))
        self.save("runner-errors.json", [])
        for mode in subject.MODES:
            self.save(mode + ".json", dict(mode=mode))

    def save(self, name, value):
        (self.root / name).write_text(json.dumps(value), encoding="utf-8")

    def analyze_with_parsed_arms(self):
        def parsed(record, mode, batches, code):
            if code:
                return "invalid", ["process failed"], None
            return "unqualified", ["fixture"], subject.Cost(Fraction(10), Fraction(12), 5)
        with patch.object(subject, "analyze_case", side_effect=parsed):
            return subject.analyze(self.root)

    def test_complete_collection_never_claims_qualification(self):
        result = self.analyze_with_parsed_arms()
        self.assertEqual((4, 4, 0, 0), tuple(result[key] for key in ("attempted", "completed", "invalid", "qualified")))
        self.assertTrue(result["collectionComplete"])
        self.assertIsNotNone(result["descriptiveComparison"])
        self.assertEqual("unqualified", result["status"])
        self.assertEqual(0.02, result["predictionErrorBound"])
        self.assertFalse(result["predictionAccuracyEstablished"])
        self.assertFalse(result["passed"])
        self.assertIsNone(result["qualifiedComparison"])

    def test_failed_and_unexecuted_arms_remain_in_report(self):
        self.outcomes[1].update(exitCode=137)
        self.outcomes[2].update(attempted=False, executed=False, recordExists=False, exitCode=None)
        self.save("outcomes.json", self.outcomes)
        result = self.analyze_with_parsed_arms()
        self.assertEqual((3, 2, 2), tuple(result[key] for key in ("attempted", "completed", "invalid")))
        self.assertEqual(list(subject.MODES), [case["mode"] for case in result["cases"]])
        self.assertEqual(137, result["cases"][1]["exitCode"])
        self.assertFalse(result["cases"][2]["executed"])
        self.assertIsNone(result["descriptiveComparison"])

    def test_missing_or_malformed_record_preserves_other_arms(self):
        for contents in (None, "{"):
            with self.subTest(contents=contents):
                path = self.root / "sampled-half.json"
                if contents is None:
                    path.unlink()
                else:
                    path.write_text(contents, encoding="utf-8")
                result = self.analyze_with_parsed_arms()
                self.assertEqual(3, result["completed"])
                self.assertEqual("invalid", result["cases"][3]["status"])
                self.assertTrue(result["cases"][3]["reasons"])
                self.assertIsNone(result["descriptiveComparison"])

    def test_changed_snapshot_suppresses_comparison(self):
        (self.root / "workload/CozNet.dll").write_text("tampered", encoding="utf-8")
        result = self.analyze_with_parsed_arms()
        self.assertEqual(["Snapshot hash mismatch: workload/CozNet.dll"], result["integrityErrors"])
        self.assertIsNone(result["descriptiveComparison"])
        self.assertEqual("invalid", result["status"])

    def test_changed_original_inputs_or_runner_failure_suppresses_comparison(self):
        for name, value in (("integrity.json", dict(originalInputsUnchanged=False)),
                            ("runner-errors.json", ["container launch failed"])):
            self.save("integrity.json", dict(originalInputsUnchanged=True))
            self.save("runner-errors.json", [])
            self.save(name, value)
            result = self.analyze_with_parsed_arms()
            self.assertFalse(result["collectionComplete"])
            self.assertIsNone(result["descriptiveComparison"])

    def test_malformed_records_retain_each_declared_arm(self):
        self.save("physical-baseline.json", dict(measurementWindow=[]))
        result = subject.analyze(self.root)
        self.assertEqual(4, len(result["cases"]))
        self.assertEqual(4, result["invalid"])
        self.assertTrue(all(case["reasons"] for case in result["cases"]))
        self.assertIsNone(result["descriptiveComparison"])

    def test_zero_sample_clock_retains_other_real_arm_results(self):
        self.save("physical-baseline.json", physical_record())
        self.save("physical-optimized.json", physical_record("optimized"))
        self.save("sampled-half.json", sampled_record())
        malformed = sampled_record()
        malformed["timestampFrequency"] = 0
        for boundary in ("Start", "End"):
            malformed["measurement"]["Window"][boundary]["TimestampFrequency"] = 0
        self.save("sampled-zero.json", malformed)
        result = subject.analyze(self.root)
        self.assertEqual(3, result["completed"])
        self.assertEqual(1, result["invalid"])
        self.assertEqual(["complete", "complete", "invalid", "unqualified"],
                         [case["status"] for case in result["cases"]])
        self.assertEqual(["The sampled clock frequency is zero"], result["cases"][2]["reasons"])
        self.assertIsNone(result["descriptiveComparison"])
        command = [sys.executable, str(pathlib.Path(subject.__file__).resolve()), str(self.root)]
        process = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(1, process.returncode, process.stderr)
        report = subject.read(self.root / "analysis.json")
        self.assertEqual(result, report)

    def test_replay_arithmetic_failure_does_not_discard_other_arms(self):
        self.save("physical-baseline.json", physical_record())
        self.save("physical-optimized.json", physical_record("optimized"))
        self.save("sampled-half.json", sampled_record())
        zero = sampled_record()
        zero["percent"] = 0
        self.save("sampled-zero.json", zero)
        replay = subject.verify_record

        def failing_replay(record):
            if record["percent"] == 0:
                raise ArithmeticError("controlled replay arithmetic failure")
            return replay(record)

        with patch.object(subject, "verify_record", side_effect=failing_replay):
            result = subject.analyze(self.root)
        self.assertEqual(3, result["completed"])
        self.assertEqual(1, result["invalid"])
        self.assertEqual(["controlled replay arithmetic failure"], result["cases"][2]["reasons"])
        self.assertEqual("unqualified", result["cases"][3]["status"])
        self.assertIsNone(result["descriptiveComparison"])

    def test_manifest_requires_each_snapshot_and_full_image_digest(self):
        for name in subject.REQUIRED_FILES:
            manifest = copy.deepcopy(self.manifest)
            del manifest["files"][name]
            self.save("manifest.json", manifest)
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "snapshots are missing"):
                subject.analyze(self.root)
        self.save("manifest.json", dict(self.manifest, runtimeImage="sha256:"))
        with self.assertRaisesRegex(ValueError, "Runtime image identity"):
            subject.analyze(self.root)

    def test_snapshot_path_cannot_escape_capture(self):
        self.manifest["files"]["../outside"] = "a" * 64
        self.save("manifest.json", self.manifest)
        with self.assertRaisesRegex(ValueError, "escapes"):
            subject.analyze(self.root)

    def test_cli_retains_invalid_report_and_returns_failure(self):
        self.save("outcomes.json", [])
        command = [sys.executable, str(pathlib.Path(subject.__file__).resolve()), str(self.root)]
        result = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(1, result.returncode, result.stderr)
        report = subject.read(self.root / "analysis.json")
        self.assertEqual("invalid", report["status"])
        self.assertIn("omit or reorder", report["error"])
        self.assertFalse(report["passed"])


if __name__ == "__main__":
    unittest.main()
