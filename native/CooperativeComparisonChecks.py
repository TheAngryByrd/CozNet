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


def sampled_record(percent=50):
    record = fixtures.CooperativeMeasurementEvidenceChecks().finite_record()
    record.update(workloadProtocol=subject.WORKLOAD, directReceiptReadyEnabled=True, receiptReadyCalls=0,
                  selectedHandoffEnabled=False, selectedHandoffAttempts=0)
    if percent == 0:
        record.update(percent=0, credit=0, paid=0, selectedPaid=0, payments=[], selectedPayments=[], pauses=[],
                      selectedReceipt=None, selectedCleanup=None, nativeCleanup=None)
        for item in record["sourceEpochSamples"]:
            item["EligibilityEpoch"] = 1
        for item in record["samples"]:
            item.update(EligibilityEpoch=1, Credit=0)
        for key in ("selectedPeak", "peerPeak", "finalDebt"):
            record[key] = fixtures.debt(110, 0, 0, 0, 0)
        final = record["finalCounters"]
        final.update(Epoch=1, Transitions=[final["Transitions"][0], dict(final["Transitions"][-1], Epoch=1)])
        final["LastReading"]["Epoch"] = 1
        end = record["measurement"]["Window"]["End"]["Accounting"]
        end.update(Global=0, Credit=0, Epoch=1)
        end["Cooperative"]["ReleasedJournalCount"] = 0
        for name in ("Selected", "Peer"):
            end[name]["ReleasedWait"] = 0
            end[name]["Ledger"].update(Global=0, Local=0)
        record["measurementAssessment"]["DescriptiveEstimate"].update(
            GlobalDelayNanoseconds=0, EffectiveLowerNanoseconds=98, EffectiveUpperNanoseconds=98)
    return record


def preparation_record(percent=50, enabled=True):
    record = sampled_record(percent)
    record.update(receiptPreparationEnabled=enabled, receiptPreparation=None)
    if enabled:
        record["receiptPreparation"] = dict(Started=0, Observed=0, Accepted=1, Children=10,
            ConfirmedNanoseconds=500000, Terminal=True, RemainingNanoseconds=0)
    return record


def ownership_record(percent=50, enabled=True):
    record = sampled_record(percent)
    record.update(ownerControllerEnabled=enabled, dispatchTimingEnabled=False, dispatchTiming=None, schedulerTiming=False)
    return record


class CooperativeComparisonChecks(unittest.TestCase):
    def test_credit_policy_plan_rejects_unknown_or_unversioned_nominal_mode(self):
        self.assertEqual(('same-epoch-interval-v1', False), subject.sample_credit_plan({}))
        for policy in ('same-epoch-interval-v1', 'nominal-event-v1'):
            self.assertEqual((policy, True), subject.sample_credit_plan(dict(sampleCreditPolicy=policy, sampleCreditEvidenceVersion=1)))
        for plan in (dict(sampleCreditPolicy='nominal-event-v1'), dict(sampleCreditEvidenceVersion=1),
                     *[dict(sampleCreditPolicy='nominal-event-v1', sampleCreditEvidenceVersion=v) for v in (None, True, '1', 1.0, 0, 2)],
                     *[dict(sampleCreditPolicy=v, sampleCreditEvidenceVersion=1) for v in (None, True, 1, 'nominal', 'NOMINAL-EVENT-V1')]):
            with self.subTest(plan=plan), self.assertRaises(ValueError):
                subject.sample_credit_plan(plan)

    def test_declared_credit_policy_gates_both_sampled_arms_before_replay(self):
        for mode, percent in (('sampled-zero', 0), ('sampled-half', 50)):
            record = sampled_record(percent)
            for policy in ('same-epoch-interval-v1', 'nominal-event-v1'):
                with self.assertRaisesRegex(ValueError, 'lacks declared sample credit'):
                    subject.analyze_case(record, mode, 5, 0, sample_credit_policy=policy, require_credit_policy=True)
                for actual in (None, True, 1, 'unknown', 'nominal-event-v1' if policy == 'same-epoch-interval-v1' else 'same-epoch-interval-v1'):
                    with self.subTest(mode=mode, policy=policy, actual=actual), self.assertRaisesRegex(ValueError, 'credit policy differs'):
                        subject.analyze_case(dict(record, sampleCreditPolicy=actual), mode, 5, 0, sample_credit_policy=policy, require_credit_policy=True)
            self.assertEqual('unqualified', subject.analyze_case(dict(record, sampleCreditPolicy='same-epoch-interval-v1'), mode, 5, 0,
                require_credit_policy=True)[0])
            with self.assertRaisesRegex(ValueError, 'credit policy differs'):
                subject.analyze_case(dict(record, sampleCreditPolicy='nominal-event-v1'), mode, 5, 0)
        self.assertEqual('complete', subject.analyze_case(physical_record(), 'physical-baseline', 5, 0,
            sample_credit_policy='nominal-event-v1', require_credit_policy=True)[0])

    def test_ownership_plan_requires_explicit_versioned_boolean(self):
        self.assertEqual((False, False), subject.controller_ownership_plan({}))
        self.assertEqual((False, False), subject.controller_ownership_plan(dict(ownerController=False)))
        for enabled in (False, True):
            self.assertEqual((enabled, True), subject.controller_ownership_plan(
                dict(ownerController=enabled, controllerOwnershipEvidenceVersion=1)))
        for plan in (dict(ownerController=True), dict(ownerController=0), dict(controllerOwnershipEvidenceVersion=1),
                     *[dict(ownerController=True, controllerOwnershipEvidenceVersion=v) for v in (None, True, '1', 1.0, 0, 2)],
                     *[dict(ownerController=v, controllerOwnershipEvidenceVersion=1) for v in (None, 0, 1, 'true')]):
            with self.subTest(plan=plan), self.assertRaises(ValueError):
                subject.controller_ownership_plan(plan)

    def test_ownership_binds_both_sampled_arms_and_preserves_legacy(self):
        for mode, percent in (("sampled-zero", 0), ("sampled-half", 50)):
            for enabled in (False, True):
                record = ownership_record(percent, enabled)
                self.assertEqual("unqualified", subject.analyze_case(record, mode, 5, 0,
                    owner_controller=enabled, require_ownership=True)[0])
                for actual in (None, 0, 1, 'true', not enabled):
                    changed = dict(record, ownerControllerEnabled=actual)
                    with self.subTest(mode=mode, enabled=enabled, actual=actual), self.assertRaisesRegex(ValueError, 'ownership differs'):
                        subject.analyze_case(changed, mode, 5, 0, owner_controller=enabled, require_ownership=True)
                del record['ownerControllerEnabled']
                with self.assertRaisesRegex(ValueError, 'lacks declared controller ownership'):
                    subject.analyze_case(record, mode, 5, 0, owner_controller=enabled, require_ownership=True)
            self.assertEqual('unqualified', subject.analyze_case(sampled_record(percent), mode, 5, 0)[0])
            with self.assertRaisesRegex(ValueError, 'ownership differs'):
                subject.analyze_case(ownership_record(percent), mode, 5, 0)
        self.assertEqual('complete', subject.analyze_case(physical_record(), 'physical-baseline', 5, 0,
            owner_controller=True, require_ownership=True)[0])

    def test_preparation_plan_requires_explicit_versioned_boolean(self):
        self.assertEqual((False, False), subject.receipt_preparation_plan({}))
        self.assertEqual((False, False), subject.receipt_preparation_plan(dict(prepareReceipts=False)))
        for enabled in (False, True):
            self.assertEqual((enabled, True), subject.receipt_preparation_plan(
                dict(prepareReceipts=enabled, receiptPreparationEvidenceVersion=1)))
        for plan in (dict(prepareReceipts=True), dict(prepareReceipts=0), dict(receiptPreparationEvidenceVersion=1),
                     *[dict(prepareReceipts=True, receiptPreparationEvidenceVersion=v) for v in (None, True, "1", 1.0, 0, 2)],
                     *[dict(prepareReceipts=v, receiptPreparationEvidenceVersion=1) for v in (None, 0, 1, "true")]):
            with self.subTest(plan=plan), self.assertRaises(ValueError):
                subject.receipt_preparation_plan(plan)

    def test_preparation_metadata_binds_both_sampled_arms_and_preserves_legacy(self):
        for mode, percent in (("sampled-zero", 0), ("sampled-half", 50)):
            for enabled in (False, True):
                record = preparation_record(percent, enabled)
                self.assertEqual("unqualified", subject.analyze_case(record, mode, 5, 0,
                    prepare_receipts=enabled, require_preparation=True)[0])
                for actual in (None, 0, 1, "true", not enabled):
                    changed = copy.deepcopy(record)
                    changed["receiptPreparationEnabled"] = actual
                    with self.subTest(mode=mode, expected=enabled, actual=actual), self.assertRaisesRegex(ValueError, "preparation differs"):
                        subject.analyze_case(changed, mode, 5, 0, prepare_receipts=enabled, require_preparation=True)
                del record["receiptPreparationEnabled"]
                with self.assertRaisesRegex(ValueError, "lacks declared receipt preparation"):
                    subject.analyze_case(record, mode, 5, 0, prepare_receipts=enabled, require_preparation=True)
            self.assertEqual("unqualified", subject.analyze_case(sampled_record(percent), mode, 5, 0)[0])
            self.assertEqual("unqualified", subject.analyze_case(preparation_record(percent, False), mode, 5, 0)[0])
            with self.assertRaisesRegex(ValueError, "preparation differs"):
                subject.analyze_case(preparation_record(percent, True), mode, 5, 0)
        self.assertEqual("complete", subject.analyze_case(physical_record(), "physical-baseline", 5, 0,
            prepare_receipts=True, require_preparation=True)[0])

    def test_probe_interval_plan_preserves_legacy_and_requires_versioned_sparse_plan(self):
        self.assertEqual((1024, False), subject.probe_interval_plan({}))
        self.assertEqual((1024, False), subject.probe_interval_plan(dict(probeInterval=1024)))
        for interval in (1024, 8192):
            self.assertEqual((interval, True), subject.probe_interval_plan(
                dict(probeInterval=interval, probeIntervalEvidenceVersion=1)))
        for plan in (dict(probeInterval=8192), dict(probeInterval=True),
                     dict(probeIntervalEvidenceVersion=1),
                     *[dict(probeInterval=8192, probeIntervalEvidenceVersion=v) for v in (None, True, "1", 1.0, 0, 2)],
                     *[dict(probeInterval=v, probeIntervalEvidenceVersion=1) for v in (None, True, "8192", 8192.0, 1, 8191, 8193)]):
            with self.subTest(plan=plan), self.assertRaises(ValueError):
                subject.probe_interval_plan(plan)

    def test_sampled_interval_must_match_versioned_plan(self):
        for interval in (1024, 8192):
            record = sampled_record()
            record["probeInterval"] = interval
            self.assertEqual("unqualified", subject.analyze_case(record, "sampled-half", 5, 0, interval, True)[0])
            for actual in (None, True, str(interval), float(interval), 8192 if interval == 1024 else 1024):
                record["probeInterval"] = actual
                with self.subTest(interval=interval, actual=actual), self.assertRaisesRegex(ValueError, "differs from the declared plan"):
                    subject.analyze_case(record, "sampled-half", 5, 0, interval, True)
            del record["probeInterval"]
            with self.assertRaisesRegex(ValueError, "lacks declared probe interval"):
                subject.analyze_case(record, "sampled-half", 5, 0, interval, True)
        self.assertEqual("unqualified", subject.analyze_case(sampled_record(), "sampled-half", 5, 0)[0])
        record = sampled_record()
        record["probeInterval"] = 1024
        self.assertEqual("unqualified", subject.analyze_case(record, "sampled-half", 5, 0, 1024, False)[0])
        record["probeInterval"] = 8192
        with self.assertRaisesRegex(ValueError, "differs from the declared plan"):
            subject.analyze_case(record, "sampled-half", 5, 0, 1024, False)

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
        def parsed(record, mode, batches, code, probe_interval, require_probe_interval, prepare_receipts, require_preparation,
                   owner_controller, require_ownership, sample_credit_policy, require_credit_policy):
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
        self.assertEqual((1024, None), (result["probeInterval"], result["probeIntervalEvidenceVersion"]))

    def test_declared_interval_reaches_all_arm_parsers(self):
        plan = subject.read(self.root / "plan.json")
        plan.update(probeInterval=8192, probeIntervalEvidenceVersion=1)
        self.save("plan.json", plan)
        self.manifest["files"]["plan.json"] = hashlib.sha256((self.root / "plan.json").read_bytes()).hexdigest()
        self.save("manifest.json", self.manifest)
        with patch.object(subject, "analyze_case", return_value=("unqualified", [], subject.Cost(Fraction(10), Fraction(12), 5))) as parsed:
            result = subject.analyze(self.root)
        self.assertEqual(4, parsed.call_count)
        self.assertEqual([(8192, True)] * 4, [call.args[4:6] for call in parsed.call_args_list])
        self.assertEqual((8192, 1), (result["probeInterval"], result["probeIntervalEvidenceVersion"]))
        self.assertTrue(result["collectionComplete"])

    def test_mismatched_sampled_interval_preserves_other_arms_and_suppresses_comparison(self):
        plan = subject.read(self.root / "plan.json")
        plan.update(probeInterval=8192, probeIntervalEvidenceVersion=1)
        self.save("plan.json", plan)
        self.manifest["files"]["plan.json"] = hashlib.sha256((self.root / "plan.json").read_bytes()).hexdigest()
        self.save("manifest.json", self.manifest)
        self.save("physical-baseline.json", physical_record())
        self.save("physical-optimized.json", physical_record("optimized"))
        self.save("sampled-zero.json", dict(sampled_record(0), probeInterval=8192))
        self.save("sampled-half.json", dict(sampled_record(), probeInterval=8192))
        self.assertTrue(subject.analyze(self.root)["collectionComplete"])
        for record in (dict(sampled_record(), probeInterval=1024), sampled_record()):
            self.save("sampled-half.json", record)
            result = subject.analyze(self.root)
            self.assertEqual((3, 1), (result["completed"], result["invalid"]))
            self.assertEqual(["complete", "complete", "unqualified", "invalid"], [case["status"] for case in result["cases"]])
            self.assertIn("probe interval", result["cases"][3]["reasons"][0])
            self.assertIsNone(result["descriptiveComparison"])

    def test_preparation_mismatch_preserves_other_arms_and_suppresses_comparison(self):
        plan = subject.read(self.root / "plan.json")
        plan.update(prepareReceipts=True, receiptPreparationEvidenceVersion=1)
        self.save("plan.json", plan)
        self.manifest["files"]["plan.json"] = hashlib.sha256((self.root / "plan.json").read_bytes()).hexdigest()
        self.save("manifest.json", self.manifest)
        self.save("physical-baseline.json", physical_record())
        self.save("physical-optimized.json", physical_record("optimized"))
        for target, percent in (("sampled-zero", 0), ("sampled-half", 50)):
            for missing in (False, True):
                self.save("sampled-zero.json", preparation_record(0))
                self.save("sampled-half.json", preparation_record())
                complete = subject.analyze(self.root)
                self.assertTrue(complete["collectionComplete"])
                self.assertEqual((True, 1), (complete["prepareReceipts"], complete["receiptPreparationEvidenceVersion"]))
                changed = preparation_record(percent, False)
                if missing:
                    del changed["receiptPreparationEnabled"]
                self.save(target + ".json", changed)
                result = subject.analyze(self.root)
                self.assertEqual((3, 1), (result["completed"], result["invalid"]))
                self.assertEqual("invalid", next(case for case in result["cases"] if case["mode"] == target)["status"])
                self.assertIsNone(result["descriptiveComparison"])

    def test_ownership_mismatch_preserves_other_arms_and_suppresses_comparison(self):
        plan = subject.read(self.root / 'plan.json')
        plan.update(ownerController=True, controllerOwnershipEvidenceVersion=1)
        self.save('plan.json', plan)
        self.manifest['files']['plan.json'] = hashlib.sha256((self.root / 'plan.json').read_bytes()).hexdigest()
        self.save('manifest.json', self.manifest)
        self.save('physical-baseline.json', physical_record())
        self.save('physical-optimized.json', physical_record('optimized'))
        for target, percent in (('sampled-zero', 0), ('sampled-half', 50)):
            for missing in (False, True):
                self.save('sampled-zero.json', ownership_record(0))
                self.save('sampled-half.json', ownership_record())
                complete = subject.analyze(self.root)
                self.assertTrue(complete['collectionComplete'])
                self.assertEqual((True, 1), (complete['ownerController'], complete['controllerOwnershipEvidenceVersion']))
                changed = ownership_record(percent, False)
                if missing:
                    del changed['ownerControllerEnabled']
                self.save(target + '.json', changed)
                result = subject.analyze(self.root)
                self.assertEqual((3, 1), (result['completed'], result['invalid']))
                self.assertEqual('invalid', next(case for case in result['cases'] if case['mode'] == target)['status'])
                self.assertIsNone(result['descriptiveComparison'])

    def test_credit_policy_mismatch_preserves_other_arms(self):
        plan = subject.read(self.root / 'plan.json')
        plan.update(sampleCreditPolicy='same-epoch-interval-v1', sampleCreditEvidenceVersion=1)
        self.save('plan.json', plan)
        self.manifest['files']['plan.json'] = hashlib.sha256((self.root / 'plan.json').read_bytes()).hexdigest()
        self.save('manifest.json', self.manifest)
        self.save('physical-baseline.json', physical_record())
        self.save('physical-optimized.json', physical_record('optimized'))
        for target in ('sampled-zero', 'sampled-half'):
            for actual in (None, 'nominal-event-v1'):
                self.save('sampled-zero.json', dict(sampled_record(0), sampleCreditPolicy='same-epoch-interval-v1'))
                self.save('sampled-half.json', dict(sampled_record(), sampleCreditPolicy='same-epoch-interval-v1'))
                complete = subject.analyze(self.root)
                self.assertTrue(complete['collectionComplete'])
                self.assertEqual(('same-epoch-interval-v1', 1), (complete['sampleCreditPolicy'], complete['sampleCreditEvidenceVersion']))
                changed = sampled_record(0 if target == 'sampled-zero' else 50)
                if actual is not None:
                    changed['sampleCreditPolicy'] = actual
                self.save(target + '.json', changed)
                result = subject.analyze(self.root)
                self.assertEqual((3, 1), (result['completed'], result['invalid']))
                self.assertIsNone(result['descriptiveComparison'])

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
