from copy import deepcopy
import unittest
import json
import pathlib
import subprocess
import sys
import tempfile
import hashlib

from AnalyzeTimerPrefixes import analyze_prefixes


def fixture():
    child = dict(Ordinal=1, Requested=10, Armed=10, Started=20, Finished=30,
                 Waited=0, WaitStatus=0, Ip=100, Valid=1)
    saved = {key: value for key, value in child.items() if key not in ("Ip", "Valid")}
    saved.update(InstructionPointer=100, Valid=True)
    accepted = dict(Raw=dict(Header=dict(Generation=1, Command=1, Budget=10,
                    Error=0, TransportError=0, BackendError=0, Visibility=3, Stop=2,
                    MayHaveDelivery=0, Captured=50, Children=1, Measured=0),
                    Clock=dict(Status=0, Nanoseconds=60), Children=[child]),
                    Received=[dict(Raw=saved, FirstCaptured=40,
                              FirstManagedReceiptUpper=45, Timing=dict(UpperNanoseconds=35))])
    journal = dict(Slot=1, Lease="00000000-0000-0000-0000-000000000001",
                   Command=1, Generation=1, Budget=10, LedgerSettled=True,
                   CancellationRequested=False, ReleaseAttempted=True, Failure=None,
                   StartedTimestamp=1, ObservedTimestamp=70, SubmissionStatus=0,
                   Accepted=accepted)
    pending = dict(Lease=journal["Lease"], Command=1, Generation=1, AcceptedChildren=0,
                   ConfirmedWait=0, LastAcceptedCapture=15, LedgerSettled=False,
                   CancellationRequested=False, ReleaseAttempted=False, Failure=None)

    def boundary(tick):
        return dict(BeforeTimestamp=tick, AfterTimestamp=tick + 1, TimestampFrequency=1000,
                    Accounting=dict(Selected=dict(ReleasedWait=0, ActiveWait=0, Pending=None),
                    Peer=dict(ReleasedWait=0, ActiveWait=0, Pending=dict(Command=1, Generation=1)),
                    Timer=dict(ReleasedJournalCount=0,
                    Selected=dict(ReleasedWait=0, Pending=None),
                    Peer=dict(ReleasedWait=0, Pending=deepcopy(pending)))))

    return dict(journals=[journal], pending=[], closed=True, failures=["retained workload failure"],
                timestampFrequency=1000,
                measurementCapture=dict(status="Complete", start=boundary(100), end=boundary(200)))


def owned(data, endpoint="end"):
    return data["measurementCapture"][endpoint]["Accounting"]["Timer"]["Peer"]["Pending"]


class TimerPrefixChecks(unittest.TestCase):
    def reject(self, data, reason):
        result = analyze_prefixes(data)
        self.assertFalse(result["reconciled"])
        self.assertIn(reason, result["reasons"][0])
        self.assertIsNone(result["workers"])

    def test_later_acceptance_preserves_failure_and_limits(self):
        result = analyze_prefixes(fixture())
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual((1, 0), (result["workers"][1]["laterAcceptedChildren"],
                                 result["workers"][1]["laterAcceptedWait"]))
        self.assertEqual(["retained workload failure"], result["recordedWorkloadFailures"])
        for flag in ("nativeEligibilityVerified", "nativeTailBoundEstablished", "predictionAccuracyEstablished"):
            self.assertFalse(result[flag])

    def test_unknown_final_observation_is_not_zero(self):
        data = fixture()
        journal = data["journals"].pop()
        journal.update(Accepted=None, LedgerSettled=False, ReleaseAttempted=False, Failure="read failed")
        data.update(pending=[journal], closed=False)
        for endpoint in ("start", "end"):
            owned(data, endpoint)["LastAcceptedCapture"] = None
        result = analyze_prefixes(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertIsNone(result["workers"][1]["laterAcceptedChildren"])
        self.assertIsNone(result["workers"][1]["laterAcceptedWait"])
        self.assertEqual("read failed", result["journalFailures"][0]["failure"])

    def test_closed_session_rejects_pending(self):
        data = fixture()
        data["pending"] = [data["journals"].pop()]
        self.reject(data, "Closed session")

    def test_reused_generation(self):
        data = fixture()
        other = deepcopy(data["journals"][0])
        other.update(Command=2, Lease="00000000-0000-0000-0000-000000000002")
        other["Accepted"]["Raw"]["Header"]["Command"] = 2
        data["journals"].append(other)
        self.reject(data, "Reused generation")

    def test_zero_wait_child_count_cannot_regress(self):
        data = fixture()
        owned(data, "start").update(AcceptedChildren=1, LastAcceptedCapture=40)
        owned(data).update(LastAcceptedCapture=40)
        self.reject(data, "Frozen pending prefix regressed")

    def test_native_capture_cannot_regress(self):
        data = fixture()
        owned(data, "start")["LastAcceptedCapture"] = 20
        self.reject(data, "Frozen pending prefix regressed")

    def test_frozen_prefix_cannot_omit_an_earlier_observed_child(self):
        data = fixture()
        owned(data)["LastAcceptedCapture"] = 41
        self.reject(data, "Frozen prefix omits a child")

    def test_equal_capture_clock_does_not_prove_acceptance_order(self):
        data = fixture()
        owned(data)["LastAcceptedCapture"] = 40
        result = analyze_prefixes(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual(1, result["workers"][1]["laterAcceptedChildren"])

    def test_ownership_flags_cannot_regress(self):
        for flag in ("LedgerSettled", "CancellationRequested", "ReleaseAttempted"):
            with self.subTest(flag=flag):
                data = fixture()
                data["journals"][0][flag] = True
                for endpoint in ("start", "end"):
                    owned(data, endpoint).update(AcceptedChildren=1, LastAcceptedCapture=40)
                owned(data, "start")[flag] = True
                if flag == "ReleaseAttempted":
                    owned(data, "start")["LedgerSettled"] = True
                self.reject(data, "Frozen pending prefix regressed")

    def test_frozen_failure_is_preserved(self):
        data = fixture()
        data["journals"][0]["Failure"] = "final failure"
        for endpoint in ("start", "end"):
            owned(data, endpoint)["Failure"] = "initial failure"
        result = analyze_prefixes(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual("initial failure", result["workers"][1]["endFailure"])
        owned(data)["Failure"] = None
        self.reject(data, "Frozen pending prefix regressed")

    def test_malformed_frozen_failure(self):
        data = fixture()
        owned(data)["Failure"] = 1
        self.reject(data, "Invalid frozen failure")

    def test_settled_prefix_cannot_grow(self):
        data = fixture()
        owned(data)["LedgerSettled"] = True
        self.reject(data, "Settled prefix has a later accepted suffix")

    def test_positive_active_prefix_reconciles_exact_wait(self):
        data = fixture()
        accepted = data["journals"][0]["Accepted"]
        accepted["Raw"]["Header"].update(Measured=10, Stop=1)
        accepted["Raw"]["Children"][0]["Waited"] = 10
        accepted["Received"][0]["Raw"]["Waited"] = 10
        owned(data).update(AcceptedChildren=1, ConfirmedWait=10, LastAcceptedCapture=40)
        data["measurementCapture"]["end"]["Accounting"]["Peer"]["ActiveWait"] = 10
        result = analyze_prefixes(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual(10, result["workers"][1]["acceptedWaitDelta"])
        self.assertEqual(0, result["workers"][1]["laterAcceptedWait"])
        owned(data)["ConfirmedWait"] = 9
        self.reject(data, "Frozen active wait differs")

    def test_nonzero_start_and_positive_later_suffix(self):
        data = fixture()
        journal = data["journals"][0]
        journal["Budget"] = 23
        accepted = journal["Accepted"]
        template_child = accepted["Raw"]["Children"][0]
        template_receipt = accepted["Received"][0]
        children, receipts = [], []
        for ordinal, (armed, started, finished, waited, captured) in enumerate(
                ((10, 20, 30, 5, 40), (25, 40, 50, 7, 60), (45, 60, 75, 11, 80)), 1):
            child, receipt = deepcopy(template_child), deepcopy(template_receipt)
            child.update(Ordinal=ordinal, Armed=armed, Started=started, Finished=finished, Waited=waited)
            receipt["Raw"].update({key: child[key] for key in
                                   ("Ordinal", "Armed", "Started", "Finished", "Waited")})
            receipt.update(FirstCaptured=captured, FirstManagedReceiptUpper=captured + 5,
                           Timing=dict(UpperNanoseconds=captured + 5 - armed))
            children.append(child)
            receipts.append(receipt)
        accepted["Raw"]["Header"].update(Budget=23, Children=3, Measured=23, Stop=1, Captured=90)
        accepted["Raw"]["Clock"]["Nanoseconds"] = 100
        accepted["Raw"]["Children"], accepted["Received"] = children, receipts
        for endpoint, count, waited, captured in (("start", 1, 5, 40), ("end", 2, 12, 60)):
            owned(data, endpoint).update(AcceptedChildren=count, ConfirmedWait=waited, LastAcceptedCapture=captured)
            data["measurementCapture"][endpoint]["Accounting"]["Peer"]["ActiveWait"] = waited
        result = analyze_prefixes(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        peer = result["workers"][1]
        self.assertEqual((5, 12, 7), (peer["startAcceptedWait"], peer["endAcceptedWait"], peer["acceptedWaitDelta"]))
        self.assertEqual((1, 11), (peer["laterAcceptedChildren"], peer["laterAcceptedWait"]))

    def test_cli_preserves_output_and_rejects_nonfinite_json(self):
        script = pathlib.Path(__file__).with_name("AnalyzeTimerPrefixes.py")
        with tempfile.TemporaryDirectory() as directory:
            source, output = (pathlib.Path(directory) / name for name in ("capture.json", "report.json"))
            source.write_text(json.dumps(fixture()), encoding="utf-8")
            command = [sys.executable, "-B", str(script), str(source), str(output)]
            run = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(0, run.returncode, run.stderr)
            report = json.loads(output.read_text(encoding="utf-8"))
            self.assertTrue(report["reconciled"])
            self.assertEqual(hashlib.sha256(source.read_bytes()).hexdigest(), report["inputSha256"])
            self.assertEqual({name: hashlib.sha256(script.with_name(name).read_bytes()).hexdigest()
                              for name in ("AnalyzeTimerPrefixes.py", "AnalyzeCpuCounters.py", "AnalyzeSampleMembership.py")},
                             report["sourceHashes"])
            original = output.read_bytes()
            run = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(1, run.returncode)
            self.assertEqual(original, output.read_bytes())
            output.unlink()
            source.write_text('{"value": NaN}', encoding="utf-8")
            run = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(1, run.returncode)
            report = json.loads(output.read_text(encoding="utf-8"))
            self.assertFalse(report["reconciled"])
            self.assertIsNone(report["workers"])

    def test_raw_receipt_mismatch(self):
        data = fixture()
        data["journals"][0]["Accepted"]["Received"][0]["Raw"]["Waited"] = 1
        self.reject(data, "Received child differs")

    def test_native_aggregate_mismatch(self):
        data = fixture()
        data["journals"][0]["Accepted"]["Raw"]["Header"]["Measured"] = 1
        self.reject(data, "Native aggregate differs")

    def test_corrupt_journal_and_inventory_fields_are_rejected(self):
        cases = (
            (("Accepted", "Raw", "Header", "Command"), 2, "Accepted journal identity differs"),
            (("Accepted", "Raw", "Header", "Stop"), 0, "visibility contradicts"),
            (("Accepted", "Raw", "Header", "MayHaveDelivery"), 1, "Quiescent journal"),
            (("Accepted", "Raw", "Header", "Stop"), 1, "has not paid its budget"),
            (("Accepted", "Raw", "Header", "Stop"), 3, "no managed intent"),
            (("Accepted", "Raw", "Header", "Stop"), 4, "no interrupted final child"),
            (("Accepted", "Received", 0, "FirstCaptured"), 29, "Invalid first receipt ordering"),
            (("Accepted", "Received", 0, "FirstManagedReceiptUpper"), 39, "Invalid first receipt ordering"),
            (("Accepted", "Received", 0, "Timing", "UpperNanoseconds"), 36, "First receipt bound differs"),
        )
        for path, value, reason in cases:
            with self.subTest(path=path, value=value):
                data = fixture()
                target = data["journals"][0]
                for key in path[:-1]:
                    target = target[key]
                target[path[-1]] = value
                self.reject(data, reason)
        data = fixture()
        accepted = data["journals"][0]["Accepted"]
        accepted["Raw"]["Children"][0]["Ordinal"] = 2
        accepted["Received"][0]["Raw"]["Ordinal"] = 2
        self.reject(data, "Child ordinal or clock order differs")
        data = fixture()
        for endpoint in ("start", "end"):
            account = data["measurementCapture"][endpoint]["Accounting"]
            account["Peer"]["Pending"] = account["Timer"]["Peer"]["Pending"] = None
        self.reject(data, "Final inventory contains an operation absent at the endpoint")

    def test_released_prefix_and_wait_delta(self):
        data = fixture()
        accepted = data["journals"][0]["Accepted"]
        accepted["Raw"]["Header"].update(Measured=10, Stop=1)
        accepted["Raw"]["Children"][0]["Waited"] = 10
        accepted["Received"][0]["Raw"]["Waited"] = 10
        end = data["measurementCapture"]["end"]["Accounting"]
        end["Timer"]["ReleasedJournalCount"] = 1
        end["Timer"]["Peer"].update(ReleasedWait=10, Pending=None)
        end["Peer"].update(ReleasedWait=10, Pending=None)
        result = analyze_prefixes(data)
        self.assertTrue(result["reconciled"], result["reasons"])
        self.assertEqual(10, result["workers"][1]["acceptedWaitDelta"])
        end["Peer"]["ReleasedWait"] = 9
        self.reject(data, "Frozen released waits differ")


if __name__ == "__main__":
    unittest.main()
