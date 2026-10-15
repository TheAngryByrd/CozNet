import hashlib
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest

from AnalyzeSampleMembership import analyze_membership
from CpuCounterChecks import fixture as counter_fixture


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


class SampleMembershipChecks(unittest.TestCase):
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
