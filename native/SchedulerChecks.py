from copy import deepcopy
import unittest
from AnalyzeScheduler import parse_capture, overlap


def capture():
    return {"ThreadId": 20, "Failures": [], "FinalLost": 0,
            "EnableBefore": 10, "EnableAfter": 20, "DisableBefore": 180, "DisableAfter": 190,
            "Records": [
                {"Kind": 1, "Flags": 24576, "TimestampNanoseconds": 50, "Lost": 0},
                {"Kind": 0, "Flags": 0, "TimestampNanoseconds": 100, "Lost": 0}]}


class SchedulerChecks(unittest.TestCase):
    def test_pairs_switches_and_preserves_preemption(self):
        result = parse_capture(capture())
        self.assertEqual(result, {"threadId": 20, "records": 2, "leftCensored": 0, "rightCensored": False,
                                  "intervals": [{"start": 50, "end": 100, "preempt": True}]})

    def test_keeps_boundary_fragments_censored(self):
        value = capture()
        value["Records"][0].update(Kind=0, Flags=0)
        value["Records"][1].update(Kind=1, Flags=8192)
        result = parse_capture(value)
        self.assertEqual((result["leftCensored"], result["rightCensored"], result["intervals"]), (1, True, []))

    def test_rejects_trailing_loss_without_loss_record(self):
        for final in [None, 1]:
            with self.subTest(final=final):
                value = capture()
                value["FinalLost"] = final
                with self.assertRaisesRegex(ValueError, "incomplete loss accounting"):
                    parse_capture(value)

    def test_rejects_capture_failures(self):
        value = capture()
        value["Failures"] = ["collector did not stop"]
        with self.assertRaises(ValueError):
            parse_capture(value)

    def test_rejects_repeated_transition_and_invalid_flags(self):
        for change in [{"Kind": 1, "Flags": 8192}, {"Flags": 16384}, {"Flags": 32768},
                       {"Kind": 2, "Lost": 1}]:
            with self.subTest(change=change):
                value = capture()
                value["Records"][1].update(change)
                with self.assertRaises(ValueError):
                    parse_capture(value)

    def test_rejects_unordered_or_outside_timestamps(self):
        for index, time in [(0, 9), (1, 49), (1, 191)]:
            with self.subTest(index=index, time=time):
                value = capture()
                value["Records"][index]["TimestampNanoseconds"] = time
                with self.assertRaises(ValueError):
                    parse_capture(value)

    def test_allows_equal_timestamps_and_boundary_records(self):
        for first, second in [(50, 50), (10, 190)]:
            with self.subTest(first=first, second=second):
                value = capture()
                value["Records"][0]["TimestampNanoseconds"] = first
                value["Records"][1]["TimestampNanoseconds"] = second
                result = parse_capture(value)
                self.assertEqual(result["intervals"][0], {"start": first, "end": second, "preempt": True})

    def test_overlap_clips_intervals_without_mutation(self):
        intervals = [{"start": 10, "end": 40, "preempt": False}, {"start": 60, "end": 100, "preempt": True}]
        original = deepcopy(intervals)
        result = overlap(intervals, 20, 80)
        self.assertEqual(result["wallNanoseconds"], 60)
        self.assertEqual(result["observedSwitchedOutNanoseconds"], 40)
        self.assertEqual([r["overlapNanoseconds"] for r in result["intervals"]], [20, 20])
        self.assertEqual(intervals, original)
        self.assertEqual(overlap(intervals, 40, 60)["intervals"], [])
        self.assertEqual(overlap(intervals, 50, 50)["observedSwitchedOutNanoseconds"], 0)


if __name__ == "__main__":
    unittest.main()
