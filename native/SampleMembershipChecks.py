from copy import deepcopy
import hashlib
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest

from AnalyzeSampleMembership import analyze_membership
from CpuCounterChecks import fixture as counter_fixture, nominal_credit_fixture


def fixture():
    data = counter_fixture()
    def frequencies(value):
        if isinstance(value, dict):
            for key in value:
                if key in ("TimestampFrequency", "timestampFrequency"):
                    value[key] = 1000000000
                else:
                    frequencies(value[key])
        elif isinstance(value, list):
            for item in value:
                frequencies(item)
    frequencies(data)
    def reading(before, native, after):
        return dict(Before=before, NativeNanoseconds=native, After=after, TimestampFrequency=1000000000,
                    ContextIdentity="time:[1]", MonotonicOffsetNanoseconds=0, Error=None)
    data["sampleClock"] = dict(source=dict(Domain=1, UnitsPerSecond=1000000000),
                               metadata=dict(backend="linux-perf", clock="CLOCK_MONOTONIC", clockId=1,
                                             attributeSize=96, runtime=".NET 10.0.0", architecture="X64",
                                             kernel="test-kernel", operatingSystem="test-linux", timestampFrequency=1000000000),
                               start=reading(50, 55, 60), end=reading(2300, 2305, 2310),
                               observedMappingConsistent=True, rejection=None, errorBoundEstablished=False)
    for sample, value in zip(data["samples"], (100, 110, 2120)):
        sample["Sample"]["Timestamp"] = value
    return data


def credit_fixture(percent=50):
    data = fixture()
    data.update(percent=percent, credit=5 if percent else 0)
    for index, raw in enumerate(data["samples"]):
        raw.update(Epoch=1, Credit=data["credit"] if index == 1 else 0)
    data["measurementCapture"]["start"]["Accounting"].update(Credit=0, Epoch=1)
    data["measurementCapture"]["end"]["Accounting"].update(Credit=data["credit"], Epoch=1)
    return data


class SampleMembershipChecks(unittest.TestCase):
    def test_nominal_membership_preserves_failed_workload(self):
        data = nominal_credit_fixture()
        clocks = fixture()
        data["sampleClock"] = clocks["sampleClock"]
        data["timestampFrequency"] = 1000000000
        counters = data["cpuCounters"]
        for name in ("startBefore", "startAfter", "endBefore", "endAfter"):
            counters[name]["TimestampFrequency"] = 1000000000
        counters["source"]["LastReading"]["TimestampFrequency"] = 1000000000
        for transition in counters["source"]["Transitions"]:
            transition["TimestampFrequency"] = 1000000000
        for boundary in data["measurementCapture"].values():
            if isinstance(boundary, dict):
                boundary["TimestampFrequency"] = 1000000000
        for sample in data["samples"]:
            sample["Sample"]["Timestamp"] = 110
        data.update(failures=["debt exceeded"], closed=False)
        result = analyze_membership(data, include_credits=True)
        self.assertTrue(result["comparisonDomainAccepted"], result["reasons"])
        self.assertEqual(result["recordedWorkloadFailures"], ["debt exceeded"])
        self.assertFalse(result["nativeSessionClosed"])
        credit = result["creditMembership"]
        self.assertEqual(credit["sampleCreditPolicy"], "nominal-event-v1")
        self.assertEqual(credit["replayedCreditNanoseconds"], 1000000)
        self.assertEqual(credit["recordedCreditBySourceMembership"],
                         dict(Before=0, Inside=1000000, After=0, BoundaryAmbiguous=0))
        self.assertFalse(result["predictionAccuracyEstablished"])

    def test_same_group_accumulates_counts_periods_and_multiple_credits(self):
        data = credit_fixture()
        data["samples"][0]["Sample"]["Timestamp"] = 110
        data["samples"][1]["Sample"]["PeriodNanoseconds"] = 3
        data["samples"][1]["Credit"] = 1
        extra = deepcopy(data["samples"][1])
        extra.update(Timestamp=2000, Credit=4)
        extra["Sample"].update(Timestamp=2000, PeriodNanoseconds=7, CumulativeCpuNanoseconds=80)
        data["samples"].insert(2, extra)
        data["records"].update(Samples=4, SelectedMethodSamples=3)
        data["measurementCapture"]["end"]["Accounting"]["AcceptedSampleSequence"] = 3
        result = analyze_membership(data, include_credits=True)
        self.assertTrue(result["comparisonDomainAccepted"], result["reasons"])
        credit = result["creditMembership"]
        groups = [group for group in credit["sampleAccountingGroups"] if group["accepted"]]
        self.assertEqual(groups, [dict(sourceMembership="Inside", receiptMembership="Inside", accepted=True,
                                      selected=True, samples=3, nominalNanoseconds=20, recordedCreditNanoseconds=5)])
        self.assertEqual(sum(group["samples"] for group in credit["sampleAccountingGroups"]), 4)
        self.assertEqual(sum(group["nominalNanoseconds"] for group in credit["sampleAccountingGroups"]), 30)
        self.assertEqual(sum(group["recordedCreditNanoseconds"] for group in credit["sampleAccountingGroups"]), 5)
        self.assertEqual(credit["replayedCreditNanoseconds"], 5)
        self.assertEqual(credit["recordedCreditBySourceMembership"], dict(Before=0, Inside=5, After=0, BoundaryAmbiguous=0))

    def test_recorded_credit_partition_preserves_ambiguity_and_zero_speedup(self):
        for percent in (0, 50):
            data = credit_fixture(percent)
            data["samples"][1]["Sample"]["Timestamp"] = 109
            result = analyze_membership(data, include_credits=True)
            self.assertTrue(result["comparisonDomainAccepted"], result["reasons"])
            credit = result["creditMembership"]
            expected = 5 if percent else 0
            self.assertEqual(credit["recordedCreditBySourceMembership"],
                             dict(Before=0, Inside=0, After=0, BoundaryAmbiguous=expected))
            self.assertEqual(credit["recordedCreditInObservedWindow"], dict(lower=0, upper=expected))
            self.assertEqual(credit["replayedCreditNanoseconds"], expected)
            self.assertFalse(credit["unreceivedCreditBoundEstablished"])
            self.assertFalse(credit["eligibilityTransitionsVerified"])
            self.assertFalse(credit["predictionAccuracyEstablished"])

    def test_inside_rejection_and_late_receipt_remain_separate_without_credit(self):
        data = credit_fixture()
        data["samples"][1].update(Accepted=False, Credit=0)
        data["samples"][2]["Sample"].update(Timestamp=2109, Method="selected", PeriodNanoseconds=13)
        data["records"].update(SelectedMethodSamples=3, UnmappedSamples=0, RepresentedCpuNanoseconds=33)
        data["credit"] = 0
        data["measurementCapture"]["end"]["Accounting"].update(AcceptedSampleSequence=1, Credit=0)
        result = analyze_membership(data, include_credits=True)
        self.assertTrue(result["comparisonDomainAccepted"], result["reasons"])
        groups = result["creditMembership"]["sampleAccountingGroups"]
        rejected = [group for group in groups if not group["accepted"]]
        self.assertEqual(rejected, [
            dict(sourceMembership="Inside", receiptMembership="After", accepted=False, selected=True,
                 samples=1, nominalNanoseconds=13, recordedCreditNanoseconds=0),
            dict(sourceMembership="Inside", receiptMembership="Inside", accepted=False, selected=True,
                 samples=1, nominalNanoseconds=10, recordedCreditNanoseconds=0)])
        self.assertEqual(result["creditMembership"]["recordedCreditInObservedWindow"], dict(lower=0, upper=0))

    def test_invalid_credit_rejects_optional_partition_but_not_default_membership(self):
        data = credit_fixture()
        data["samples"][1]["Credit"] += 1
        self.assertTrue(analyze_membership(data)["comparisonDomainAccepted"])
        self.assertNotIn("creditMembership", analyze_membership(data))
        result = analyze_membership(data, include_credits=True)
        self.assertFalse(result["comparisonDomainAccepted"])
        self.assertIsNone(result["creditMembership"])
        self.assertIn("Sample credit differs at record 1", result["reasons"])

    def test_selected_and_other_accepted_samples_have_separate_groups(self):
        data = credit_fixture()
        data["samples"][0]["Sample"].update(Timestamp=110, Method="other", PeriodNanoseconds=3)
        data["records"].update(SelectedMethodSamples=1, RepresentedCpuNanoseconds=23)
        result = analyze_membership(data, include_credits=True)
        self.assertTrue(result["comparisonDomainAccepted"], result["reasons"])
        groups = [group for group in result["creditMembership"]["sampleAccountingGroups"] if group["accepted"]]
        self.assertEqual(groups, [
            dict(sourceMembership="Inside", receiptMembership="Inside", accepted=True, selected=False,
                 samples=1, nominalNanoseconds=3, recordedCreditNanoseconds=0),
            dict(sourceMembership="Inside", receiptMembership="Inside", accepted=True, selected=True,
                 samples=1, nominalNanoseconds=10, recordedCreditNanoseconds=5)])

    def test_credit_cli_partitions_credit_and_rejects_invalid_replay(self):
        script = pathlib.Path(__file__).with_name("AnalyzeSampleMembership.py")
        with tempfile.TemporaryDirectory() as directory:
            source, output = pathlib.Path(directory) / "capture.json", pathlib.Path(directory) / "report.json"
            data = credit_fixture()
            source.write_text(json.dumps(data))
            command = [sys.executable, str(script), str(source), str(output), "--sample-credits"]
            run = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(run.returncode, 0, run.stderr)
            report = json.loads(output.read_bytes())
            self.assertEqual(report["creditMembership"]["recordedCreditInObservedWindow"], dict(lower=5, upper=5))
            self.assertEqual(sum(group["recordedCreditNanoseconds"] for group in
                                 report["creditMembership"]["sampleAccountingGroups"]), 5)
            data["credit"] += 1
            source.write_text(json.dumps(data))
            command[3] = str(output.with_name("invalid.json"))
            self.assertEqual(subprocess.run(command, capture_output=True).returncode, 1)
            self.assertIsNone(json.loads(pathlib.Path(command[3]).read_bytes())["creditMembership"])

    def test_endpoint_equalities_and_adjacent_values(self):
        for value, expected in ((99, "Before"), (100, "BoundaryAmbiguous"), (109, "BoundaryAmbiguous"),
                                (110, "Inside"), (111, "Inside"), (2109, "Inside"),
                                (2110, "BoundaryAmbiguous"), (2119, "BoundaryAmbiguous"),
                                (2120, "After"), (2121, "After")):
            with self.subTest(value=value):
                data = fixture()
                for raw in data["samples"]:
                    raw["Sample"]["Timestamp"] = value
                    raw["Timestamp"] = 2250
                    raw["Accepted"] = False
                data["measurementCapture"]["end"]["Accounting"]["AcceptedSampleSequence"] = 0
                result = analyze_membership(data)
                self.assertTrue(result["comparisonDomainAccepted"], result["reasons"])
                self.assertEqual([o["membership"] for o in result["observations"]], [expected] * 3)

    def test_zero_width_start_and_end_are_half_open(self):
        data = fixture()
        data["measurementCapture"]["start"]["BeforeTimestamp"] = 110
        data["measurementCapture"]["end"]["AfterTimestamp"] = 2110
        data["cpuCounters"]["source"]["Transitions"][0].update(Before=110, After=110)
        for raw, value in zip(data["samples"], (110, 110, 2110)):
            raw["Sample"]["Timestamp"] = value
        result = analyze_membership(data)
        self.assertTrue(result["comparisonDomainAccepted"], result["reasons"])
        self.assertEqual([o["membership"] for o in result["observations"]], ["Inside", "Inside", "After"])

    def test_ambiguous_totals_preserve_nominal_weights_and_failure(self):
        data = fixture()
        data.update(failures=["debt exceeded"], closed=False)
        result = analyze_membership(data)
        self.assertTrue(result["comparisonDomainAccepted"], result["reasons"])
        self.assertEqual(result["membershipCounts"], dict(Before=0, Inside=1, After=1, BoundaryAmbiguous=1))
        self.assertEqual(result["membershipTotals"], dict(samples=dict(lower=1, upper=2),
                         nominalNanoseconds=dict(lower=10, upper=20), selectedSamples=dict(lower=1, upper=2),
                         selectedNominalNanoseconds=dict(lower=10, upper=20)))
        self.assertEqual(result["recordedWorkloadFailures"], ["debt exceeded"])
        self.assertFalse(result["nativeSessionClosed"])
        self.assertEqual(result["startBracket"], [100, 110])
        self.assertEqual(result["endBracket"], [2110, 2120])
        self.assertEqual(result["clockEvidence"], data["sampleClock"])
        self.assertEqual(result["measurementEvidence"], data["measurementCapture"])
        for field in ("physicalClockAccuracyEstablished", "exactEventOrderingEstablished", "predictionAccuracyEstablished"):
            self.assertFalse(result[field])

    def test_late_unaccepted_receipt_keeps_inside_source_membership(self):
        data = fixture()
        data["samples"][2]["Sample"]["Timestamp"] = 2109
        for raw, period in zip(data["samples"], (3, 7, 11)):
            raw["Sample"]["PeriodNanoseconds"] = period
        data["records"]["RepresentedCpuNanoseconds"] = 21
        result = analyze_membership(data)
        self.assertTrue(result["comparisonDomainAccepted"], result["reasons"])
        self.assertEqual(result["observations"][2]["membership"], "Inside")
        self.assertFalse(result["observations"][2]["accepted"])
        self.assertEqual(result["membershipTotals"]["nominalNanoseconds"], dict(lower=18, upper=21))
        self.assertEqual(result["membershipTotals"]["selectedNominalNanoseconds"], dict(lower=7, upper=10))

    def test_empty_capture_has_zero_membership_totals(self):
        data = fixture()
        data["samples"] = []
        data["records"].update(Samples=0, DistinctInstructionPointers=0, RepresentedCpuNanoseconds=0,
                               SelectedMethodSamples=0, UnmappedSamples=0, SelectedSymbols=[])
        data["measurementCapture"]["end"]["Accounting"]["AcceptedSampleSequence"] = 0
        result = analyze_membership(data)
        self.assertTrue(result["comparisonDomainAccepted"], result["reasons"])
        self.assertEqual(result["observations"], [])
        self.assertEqual(result["membershipCounts"], dict(Before=0, Inside=0, After=0, BoundaryAmbiguous=0))
        self.assertTrue(all(value == dict(lower=0, upper=0) for value in result["membershipTotals"].values()))

    def test_incompatible_or_missing_clock_evidence_rejects(self):
        cases = [("source", "Domain", True), ("source", "UnitsPerSecond", 1000),
                 ("metadata", "backend", "unknown"), ("metadata", "clock", "default"),
                 ("metadata", "clockId", 0), ("metadata", "attributeSize", 64),
                 ("metadata", "runtime", ".NET 9.0.0"), ("metadata", "architecture", "Arm64"),
                 ("metadata", "timestampFrequency", 1000), ("metadata", "kernel", ""),
                 ("start", "MonotonicOffsetNanoseconds", None), ("start", "MonotonicOffsetNanoseconds", 1),
                 ("start", "MonotonicOffsetNanoseconds", False), ("start", "ContextIdentity", ""),
                 ("end", "ContextIdentity", "time:[2]"), ("start", "NativeNanoseconds", 61),
                 ("start", "After", 101), ("end", "Before", 2119),
                 ("end", "Error", "failed"), ("end", "TimestampFrequency", 1000)]
        for section, key, value in cases:
            with self.subTest(section=section, key=key, value=value):
                data = fixture()
                data["sampleClock"][section][key] = value
                result = analyze_membership(data)
                self.assertFalse(result["comparisonDomainAccepted"])
                self.assertTrue(result["reasons"])
                self.assertIsNone(result["membershipTotals"])
                self.assertEqual(result["rawSamples"], data["samples"])
        data = fixture()
        del data["sampleClock"]
        self.assertFalse(analyze_membership(data)["comparisonDomainAccepted"])
        for key, value in (("observedMappingConsistent", False), ("rejection", "rejected"), ("errorBoundEstablished", True)):
            data = fixture()
            data["sampleClock"][key] = value
            self.assertFalse(analyze_membership(data)["comparisonDomainAccepted"])

    def test_source_regression_future_receipt_overflow_and_loss_reject(self):
        for field, value in (("Timestamp", 99), ("Timestamp", 1001), ("Timestamp", 9223372036854775808)):
            data = fixture()
            data["samples"][1]["Sample"][field] = value
            self.assertFalse(analyze_membership(data)["comparisonDomainAccepted"])
        data = fixture()
        data["records"]["LostSamples"] = 1
        self.assertFalse(analyze_membership(data)["comparisonDomainAccepted"])

    def test_cli_hashes_failure_retention_rejection_and_no_overwrite(self):
        script = pathlib.Path(__file__).with_name("AnalyzeSampleMembership.py")
        with tempfile.TemporaryDirectory() as directory:
            source, output = pathlib.Path(directory) / "capture.json", pathlib.Path(directory) / "report.json"
            data = fixture()
            data.update(failures=["failed workload"], closed=False)
            source.write_text(json.dumps(data))
            command = [sys.executable, str(script), str(source), str(output)]
            run = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(run.returncode, 0, run.stderr)
            saved = output.read_bytes()
            report = json.loads(saved)
            self.assertEqual(report["inputSha256"], hashlib.sha256(source.read_bytes()).hexdigest())
            self.assertEqual(report["analyzerSha256"], hashlib.sha256(script.read_bytes()).hexdigest())
            self.assertEqual(report["counterParserSha256"], hashlib.sha256(script.with_name("AnalyzeCpuCounters.py").read_bytes()).hexdigest())
            self.assertEqual(report["recordedWorkloadFailures"], ["failed workload"])
            self.assertEqual(subprocess.run(command, capture_output=True).returncode, 1)
            self.assertEqual(output.read_bytes(), saved)
            data["sampleClock"]["end"]["ContextIdentity"] = "changed"
            source.write_text(json.dumps(data))
            command[-1] = str(output.with_name("rejected.json"))
            self.assertEqual(subprocess.run(command, capture_output=True).returncode, 1)
            rejected = json.loads(pathlib.Path(command[-1]).read_bytes())
            self.assertFalse(rejected["comparisonDomainAccepted"])
            self.assertEqual(rejected["rawSamples"], data["samples"])
            source.write_text('{"invalid": NaN}')
            command[-1] = str(output.with_name("malformed.json"))
            self.assertEqual(subprocess.run(command, capture_output=True).returncode, 1)
            malformed = json.loads(pathlib.Path(command[-1]).read_bytes())
            self.assertEqual(malformed["reasons"], ["Non-finite JSON number: NaN"])
            for index, token in enumerate(("1e999", "-1e999")):
                source.write_text(json.dumps(fixture())[:-1] + ', "unknown": ' + token + '}')
                command[-1] = str(output.with_name(f"exponent-{index}.json"))
                self.assertEqual(subprocess.run(command, capture_output=True).returncode, 1)
                malformed = json.loads(pathlib.Path(command[-1]).read_bytes())
                self.assertFalse(malformed["comparisonDomainAccepted"])
                self.assertEqual(malformed["reasons"], [f"Non-finite JSON number: {token}"])


if __name__ == "__main__":
    unittest.main()
