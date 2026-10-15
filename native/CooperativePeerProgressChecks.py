import copy
import json
import pathlib
import tempfile
import unittest

from AnalyzeCooperativePeerProgress import analyze_record, child_window, main
from BoundSelectedPaymentAnalysisChecks import failed_record, payment, valid_record


def progress_record():
    record = failed_record()
    current = payment(2, 3, 2, 2, 70, 1000)
    record["failureBoundary"].update(Command=3, Generation=3, Budget=2, RequestStarted=70)
    record["failureBoundary"]["Capture"].update(State=1, Raw=current["Observation"]["Raw"], CaptureFailure=None)
    record["pending"] = dict(command=3, generation=3, budget=2, started=70)
    record["nativeCleanup"] = current["Cleanup"]
    submit = dict(Sequence=1, Operation=1, Command=3, Generation=0, Submitted=75, Started=80,
                  Finished=90, Returned=95, Failed=False)
    record.update(dispatchTimingEnabled=True,
                  dispatchTiming=dict(Calls=1, Failed=0, Incomplete=0, Recent=[submit],
                                      **{key: copy.deepcopy(submit) for key in
                                         ("Longest", "LongestQueue", "LongestAction", "LongestReturn")}))
    return record


class CooperativePeerProgressChecks(unittest.TestCase):
    def test_duration_projection_does_not_require_equal_epochs(self):
        actual = child_window(80, 90, 1000, 1001, 110, 1000000000)
        self.assertEqual(actual, dict(earliestNanosecondsFromDebt=-29, latestNanosecondsFromDebt=-19, relation="before"))
        self.assertEqual(actual, child_window(80, 90, 9000000000, 9000000001, 110, 1000000000))
        after = child_window(80, 90, 1000, 1040, 110, 1000000000)
        self.assertEqual(after, dict(earliestNanosecondsFromDebt=10, latestNanosecondsFromDebt=20, relation="after"))

    def test_boundary_and_fractional_rounding_remain_ambiguous(self):
        for event in (1020, 1025, 1030):
            self.assertEqual(child_window(80, 90, 1000, event, 110, 1000000000)["relation"], "overlaps")
        self.assertEqual(child_window(1, 1, 1, 1, 2, 3000000000),
                         dict(earliestNanosecondsFromDebt=-1, latestNanosecondsFromDebt=0, relation="overlaps"))

    def test_invalid_clock_inputs_reject(self):
        for args in ((90, 80, 1000, 1001, 110, 1000000000), (80, 90, 1000, 999, 110, 1000000000),
                     (80, 90, 0, 1, 110, 1000000000), (80, 90, 1000, 1001, 110, 0),
                     (80, 90, 1000, 1001, True, 1000000000), (80, 90, 1000, 1001, -1, 1000000000)):
            with self.subTest(args=args), self.assertRaises(ValueError):
                child_window(*args)

    def test_full_replay_preserves_failure_and_binds_pending_request(self):
        result = analyze_record(progress_record())
        self.assertTrue(result["evidenceVerified"])
        self.assertFalse(result["casePassed"])
        self.assertFalse(result["completed"])
        self.assertEqual(result["acceptedPeerPayments"], 2)
        self.assertEqual(result["peer"]["firstChildStart"],
                         dict(earliestNanosecondsFromDebt=-29, latestNanosecondsFromDebt=-19, relation="before"))
        self.assertEqual(result["peer"]["firstChildFinish"],
                         dict(earliestNanosecondsFromDebt=-27, latestNanosecondsFromDebt=-17, relation="before"))
        self.assertEqual(result["peer"]["command"], 3)
        self.assertEqual(result["peer"]["generation"], 3)
        self.assertEqual(result["peer"]["state"], "bounded")
        changed = progress_record()
        changed["failureBoundary"]["Generation"] = 4
        with self.assertRaisesRegex(ValueError, "request identity|pending reservation"):
            analyze_record(changed)
        changed = progress_record()
        changed["pending"]["command"] = 4
        with self.assertRaisesRegex(ValueError, "pending reservation"):
            analyze_record(changed)

    def test_existing_replay_failures_prevent_diagnostic_acceptance(self):
        record = progress_record()
        record["paid"] += 1
        with self.assertRaisesRegex(ValueError, "Settled payment totals"):
            analyze_record(record)

    def test_missing_dispatch_or_command_is_unavailable(self):
        record = progress_record()
        record.update(dispatchTimingEnabled=False, dispatchTiming=None)
        self.assertEqual(analyze_record(record)["peer"]["reason"], "dispatch-timing-disabled")
        record = progress_record()
        for item in [record["dispatchTiming"]["Recent"][0]] + [record["dispatchTiming"][key] for key in
                ("Longest", "LongestQueue", "LongestAction", "LongestReturn")]:
            item["Command"] = 99
        self.assertEqual(analyze_record(record)["peer"]["reason"], "submit-not-retained")
        self.assertEqual(analyze_record(valid_record())["peer"]["reason"], "no-failure-boundary")

    def test_no_published_child_does_not_claim_no_execution(self):
        record = progress_record()
        raw = record["failureBoundary"]["Capture"]["Raw"]
        raw["Header"].update(Children=0, Measured=0, Visibility=0, Stop=0, MayHaveDelivery=1)
        raw["Children"] = []
        peer = analyze_record(record)["peer"]
        self.assertEqual(peer["reason"], "no-published-child")
        self.assertEqual(peer["state"], "unavailable")
        self.assertNotIn("firstChildStart", peer)

    def test_failed_capture_and_missing_reservation_are_unavailable(self):
        record = progress_record()
        record["failureBoundary"]["Capture"].update(State=2, Raw=None, CaptureFailure="snapshot failed")
        self.assertEqual(analyze_record(record)["peer"]["reason"], "no-successful-native-capture")
        record = progress_record()
        record["pending"] = None
        self.assertEqual(analyze_record(record)["peer"]["reason"], "pending-reservation-unavailable")

    def test_child_identity_must_match_the_bound_peer(self):
        for key, value in (("CodeVersion", 2), ("Site", 3)):
            record = progress_record()
            record["failureBoundary"]["Capture"]["Raw"]["Children"][0][key] = value
            with self.subTest(key=key), self.assertRaisesRegex(ValueError, "first-child identity"):
                analyze_record(record)

    def test_duplicate_submit_commands_cannot_select_an_arbitrary_anchor(self):
        record = progress_record()
        timing = record["dispatchTiming"]
        second = dict(timing["Recent"][0], Sequence=2, Submitted=96, Started=97, Finished=99, Returned=100)
        timing["Recent"].append(second)
        timing["Calls"] = 2
        with self.assertRaisesRegex(ValueError, "Ambiguous peer submit"):
            analyze_record(record)

    def test_submit_generation_and_request_order_guards(self):
        record = progress_record()
        timing = record["dispatchTiming"]
        for item in timing["Recent"] + [timing[key] for key in ("Longest", "LongestQueue", "LongestAction", "LongestReturn")]:
            item["Generation"] = 3
        with self.assertRaisesRegex(ValueError, "unexpectedly has a returned generation"):
            analyze_record(record)
        record = progress_record()
        record["pending"]["started"] = 80
        record["failureBoundary"]["RequestStarted"] = 80
        with self.assertRaisesRegex(ValueError, "does not precede the debt observation"):
            analyze_record(record)

    def test_already_paid_command_cannot_anchor_pending_progress(self):
        record = progress_record()
        record["pending"]["command"] = 2
        record["failureBoundary"]["Command"] = 2
        record["failureBoundary"]["Capture"]["Raw"]["Header"]["Command"] = 2
        timing = record["dispatchTiming"]
        for item in timing["Recent"] + [timing[key] for key in ("Longest", "LongestQueue", "LongestAction", "LongestReturn")]:
            item["Command"] = 2
        with self.assertRaisesRegex(ValueError, "already has accepted payment"):
            analyze_record(record)

    def test_incomplete_submit_remains_unavailable(self):
        record = progress_record()
        timing = record["dispatchTiming"]
        for item in (timing["Recent"][0], timing["Longest"]):
            item.update(Started=None, Finished=None, Failed=True)
        timing.update(Failed=1, Incomplete=1, LongestQueue=None, LongestAction=None, LongestReturn=None)
        self.assertEqual(analyze_record(record)["peer"]["reason"], "submit-action-incomplete-or-failed")

    def test_invalid_child_and_unordered_clock_are_not_projected(self):
        record = progress_record()
        record["failureBoundary"]["Capture"]["Raw"]["Children"][0]["Valid"] = 0
        self.assertEqual(analyze_record(record)["peer"]["reason"], "first-child-invalid")
        record = progress_record()
        for child in (record["failureBoundary"]["Capture"]["Raw"]["Children"][0], record["nativeCleanup"]["Children"][0]):
            child["Started"] = child["Armed"] - 1
        with self.assertRaisesRegex(ValueError, "child clocks"):
            analyze_record(record)

    def test_file_analysis_retains_runtime_failure_and_rejects_corruption(self):
        with tempfile.TemporaryDirectory() as directory:
            source, output = (pathlib.Path(directory) / name for name in ("record.json", "progress.json"))
            source.write_text(json.dumps(progress_record()), encoding="utf-8")
            self.assertEqual(main(source, output), 0)
            self.assertFalse(json.loads(output.read_text(encoding="utf-8"))["casePassed"])
            source.write_text("{", encoding="utf-8")
            self.assertEqual(main(source, output), 1)
            self.assertFalse(json.loads(output.read_text(encoding="utf-8"))["evidenceVerified"])


if __name__ == "__main__":
    unittest.main()
