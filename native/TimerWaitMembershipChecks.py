from copy import deepcopy
import hashlib
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest

from AnalyzeTimerPrefixes import Child
from AnalyzeTimerWaitMembership import analyze_timer_waits, window_wait
from SampleMembershipChecks import fixture as clock_fixture
from TimerPrefixChecks import fixture as prefix_fixture


def fixture():
    data, timer = clock_fixture(), prefix_fixture()
    data.update(journals=timer["journals"], pending=[], failures=["retained debt failure"])
    for endpoint in ("start", "end"):
        data["measurementCapture"][endpoint]["Accounting"].update(timer["measurementCapture"][endpoint]["Accounting"])
    data["measurementCapture"]["end"]["Accounting"]["AcceptedSampleSequence"] = 2
    accepted = data["journals"][0]["Accepted"]
    accepted["Raw"]["Header"].update(Measured=10, Stop=1, Captured=2305)
    child = accepted["Raw"]["Children"][0]
    child.update(Armed=130, Started=140, Finished=150, Waited=10)
    receipt = accepted["Received"][0]
    receipt["Raw"].update(Armed=130, Started=140, Finished=150, Waited=10)
    receipt.update(FirstCaptured=2300, FirstManagedReceiptUpper=2301, Timing=dict(UpperNanoseconds=2171))
    accepted["Raw"]["Clock"]["Nanoseconds"] = 2310
    return data


def released_fixture():
    data = fixture()
    original = data["journals"][0]
    journals = []
    for command, slot, generation, shift in ((1, 1, 1, 0), (2, 1, 2, 20), (3, 0, 1, 0)):
        journal = deepcopy(original)
        journal.update(Command=command, Slot=slot, Generation=generation,
                       Lease=f"00000000-0000-0000-0000-{command:012d}")
        accepted = journal["Accepted"]
        accepted["Raw"]["Header"].update(Command=command, Generation=generation, Captured=405)
        accepted["Raw"]["Clock"]["Nanoseconds"] = 406
        for raw in (accepted["Raw"]["Children"][0], accepted["Received"][0]["Raw"]):
            for field in ("Armed", "Started", "Finished"):
                raw[field] += shift
        accepted["Received"][0].update(FirstCaptured=400, FirstManagedReceiptUpper=401,
                                      Timing=dict(UpperNanoseconds=271 - shift))
        journals.append(journal)
    data["journals"] = journals
    for endpoint in ("start", "end"):
        account = data["measurementCapture"][endpoint]["Accounting"]
        account["Timer"]["ReleasedJournalCount"] = 3 if endpoint == "end" else 0
        for worker, waited in (("Selected", 10), ("Peer", 20)):
            value = waited if endpoint == "end" else 0
            account[worker].update(ReleasedWait=value, Pending=None)
            account["Timer"][worker].update(ReleasedWait=value, Pending=None)
    return data


class TimerWaitMembershipChecks(unittest.TestCase):
    def test_overlap_examples(self):
        for started, finished, waited, expected in (
                (120, 150, 20, (20, 20)), (80, 100, 15, (0, 0)),
                (210, 230, 15, (0, 0)), (95, 115, 15, (0, 15)),
                (195, 215, 15, (0, 15)), (90, 220, 120, (80, 110)),
                (100, 210, 0, (0, 0)), (110, 200, 90, (90, 90))):
            with self.subTest(started=started, finished=finished, waited=waited):
                result = window_wait(Child(waited, finished, started, finished), (100, 110), (200, 210))
                self.assertEqual(expected, (result.lower, result.upper))

    def test_ranges_enclose_every_small_wait_placement(self):
        for started in range(8):
            for finished in range(started, 9):
                for waited in range(finished - started + 1):
                    result = window_wait(Child(waited, finished, started, finished), (2, 3), (5, 6))
                    actual = []
                    for position in range(started, finished - waited + 1):
                        for begin in (2, 3):
                            for end in (5, 6):
                                actual.append(len(set(range(position, position + waited)) & set(range(begin, end))))
                    with self.subTest(started=started, finished=finished, waited=waited):
                        self.assertLessEqual(result.lower, min(actual))
                        self.assertGreaterEqual(result.upper, max(actual))
                        self.assertGreaterEqual(result.lower, 0)
                        self.assertLessEqual(result.upper, waited)

    def test_late_acceptance_does_not_exclude_native_wait(self):
        result = analyze_timer_waits(fixture())
        self.assertTrue(result["comparisonDomainAccepted"], result["reasons"])
        peer = result["workers"][1]
        self.assertEqual(dict(lower=10, upper=10), peer["recordedWaitInObservedWindow"])
        self.assertEqual(0, peer["frozenAcceptedWaitDelta"])
        self.assertEqual(["retained debt failure"], result["recordedWorkloadFailures"])
        for flag in ("physicalClockAccuracyEstablished", "exactEventOrderingEstablished", "nativeEligibilityVerified",
                     "nativeTailBoundEstablished", "predictionAccuracyEstablished"):
            self.assertFalse(result[flag])

    def test_unclosed_inventory_has_no_range(self):
        data = fixture()
        data["closed"] = False
        result = analyze_timer_waits(data)
        self.assertFalse(result["comparisonDomainAccepted"])
        self.assertIsNone(result["workers"])
        self.assertIn("closed final journal inventory", result["reasons"][0])

    def test_missing_or_changed_clock_evidence_has_no_range(self):
        for field, value in (("runtime", ".NET 11.0.0"), ("timestampFrequency", 1000)):
            with self.subTest(field=field):
                data = fixture()
                data["sampleClock"]["metadata"][field] = value
                result = analyze_timer_waits(data)
                self.assertFalse(result["comparisonDomainAccepted"])
                self.assertIsNone(result["workers"])
        data = fixture()
        del data["sampleClock"]
        self.assertFalse(analyze_timer_waits(data)["comparisonDomainAccepted"])

    def test_cross_generation_handler_overlap_is_rejected(self):
        data = fixture()
        other = deepcopy(data["journals"][0])
        other.update(Command=2, Generation=2, Lease="00000000-0000-0000-0000-000000000002")
        other["Accepted"]["Raw"]["Header"].update(Command=2, Generation=2)
        data["journals"].append(other)
        for endpoint in ("start", "end"):
            account = data["measurementCapture"][endpoint]["Accounting"]
            account["Peer"].update(ReleasedWait=20, Pending=None)
            account["Timer"]["Peer"].update(ReleasedWait=20, Pending=None)
            account["Timer"]["ReleasedJournalCount"] = 2
        result = analyze_timer_waits(data)
        self.assertFalse(result["comparisonDomainAccepted"])
        self.assertIn("handlers overlap", result["reasons"][0])

    def test_invalid_prefix_cannot_supply_a_range(self):
        data = fixture()
        data["journals"][0]["Accepted"]["Raw"]["Header"]["Measured"] = 11
        result = analyze_timer_waits(data)
        self.assertFalse(result["comparisonDomainAccepted"])
        self.assertIsNone(result["workers"])
        self.assertIn("Native aggregate differs", result["reasons"][0])

    def test_multiple_journals_sum_each_worker_separately(self):
        result = analyze_timer_waits(released_fixture())
        self.assertTrue(result["comparisonDomainAccepted"], result["reasons"])
        self.assertEqual([1, 2], [worker["recordedChildren"] for worker in result["workers"]])
        self.assertEqual([10, 20], [worker["recordedWait"] for worker in result["workers"]])
        self.assertEqual([dict(lower=10, upper=10), dict(lower=20, upper=20)],
                         [worker["recordedWaitInObservedWindow"] for worker in result["workers"]])
        self.assertEqual([10, 20], [worker["frozenAcceptedWaitDelta"] for worker in result["workers"]])

    def test_frozen_accounting_cannot_include_a_future_capture(self):
        for pending in (False, True):
            with self.subTest(pending=pending):
                data = released_fixture() if not pending else fixture()
                if pending:
                    data["measurementCapture"]["end"]["Accounting"]["Timer"]["Peer"]["Pending"]["LastAcceptedCapture"] = 2200
                else:
                    data["journals"][0]["Accepted"]["Raw"]["Header"]["Captured"] = 2200
                    data["journals"][0]["Accepted"]["Raw"]["Clock"]["Nanoseconds"] = 2201
                result = analyze_timer_waits(data)
                self.assertFalse(result["comparisonDomainAccepted"])
                self.assertIsNone(result["workers"])
                self.assertIn("native capture after its endpoint", result["reasons"][0])

    def test_cli_hashes_and_existing_output_protection(self):
        script = pathlib.Path(__file__).with_name("AnalyzeTimerWaitMembership.py")
        with tempfile.TemporaryDirectory() as temporary:
            source, output = (pathlib.Path(temporary) / name for name in ("input.json", "output.json"))
            source.write_text(json.dumps(fixture()), encoding="utf-8")
            command = [sys.executable, "-B", str(script), str(source), str(output)]
            process = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(0, process.returncode, process.stderr)
            content = output.read_bytes()
            report = json.loads(content)
            self.assertEqual(hashlib.sha256(source.read_bytes()).hexdigest(), report["inputSha256"])
            self.assertEqual({name: hashlib.sha256(script.with_name(name).read_bytes()).hexdigest()
                              for name in ("AnalyzeTimerWaitMembership.py", "AnalyzeTimerPrefixes.py",
                                           "AnalyzeCpuCounters.py", "AnalyzeSampleMembership.py")}, report["sourceHashes"])
            self.assertEqual(1, subprocess.run(command, capture_output=True).returncode)
            self.assertEqual(content, output.read_bytes())
            output.unlink()
            source.write_text('{"value": 1e9999}', encoding="utf-8")
            self.assertEqual(1, subprocess.run(command, capture_output=True).returncode)
            self.assertFalse(json.loads(output.read_text())["comparisonDomainAccepted"])


if __name__ == "__main__":
    unittest.main()
