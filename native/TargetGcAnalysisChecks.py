import unittest

from AnalyzeTargetGc import DEBT_EXCEEDED, FAILURE, SAMPLE, gc_diagnostics, windows_complete


def record(windows, **changes):
    value = {"stopwatchFrequency": 1_000_000_000, "gcWindowsDropped": 0, "gcWindows": windows, "forcedCollections": len(windows),
             "gcPauseNs": 70, "threads": [{"workStartedNs": 100}, {"workStartedNs": 120}], "events": []}
    value.update(changes)
    return value


def event(worker, kind, time, detail=0):
    return [worker, 0, kind, 0, 0, 0, 0, 0, detail, time]


class WindowChecks(unittest.TestCase):
    def test_ordered_windows_are_complete(self):
        self.assertTrue(windows_complete(record([[200, 230], [300, 340]])))

    def test_count_mismatch_is_incomplete(self):
        self.assertFalse(windows_complete(record([[200, 230]], forcedCollections=2)))

    def test_dropped_window_is_incomplete(self):
        self.assertFalse(windows_complete(record([[200, 230]], gcWindowsDropped=1)))

    def test_other_frequency_is_incomplete(self):
        self.assertFalse(windows_complete(record([[200, 230]], stopwatchFrequency=10_000_000)))

    def test_reversed_window_is_incomplete(self):
        self.assertFalse(windows_complete(record([[230, 200]])))

    def test_overlapping_windows_are_incomplete(self):
        self.assertFalse(windows_complete(record([[200, 330], [300, 340]])))

    def test_window_before_work_start_is_incomplete(self):
        self.assertFalse(windows_complete(record([[90, 130]])))

    def test_missing_windows_are_incomplete(self):
        value = record([])
        del value["gcWindows"]
        self.assertFalse(windows_complete(value))


class OverlapChecks(unittest.TestCase):
    def test_failure_reports_overlap_from_the_sample_before_the_failing_sample(self):
        events = [event(1, SAMPLE, 1000), event(1, SAMPLE, 7000), event(1, FAILURE, 7010, DEBT_EXCEEDED)]
        result = gc_diagnostics(record([[500, 900], [2000, 2500], [6000, 6200]], events=events))
        failure = result["debtFailures"][0]
        self.assertEqual((failure["intervalNs"], failure["windows"], failure["overlapNs"]), (6010, [[2000, 2500], [6000, 6200]], 700),
                         f"failure {failure}")

    def test_largest_gap_reports_window_time_inside_it(self):
        events = [event(0, SAMPLE, 1000), event(0, SAMPLE, 2000), event(0, SAMPLE, 6000)]
        result = gc_diagnostics(record([[1500, 1600], [3000, 3500]], events=events))
        self.assertEqual(result["largestGaps"], [{"worker": 0, "beginNs": 2000, "gapNs": 4000, "overlapNs": 500}])

    def test_incomplete_record_reports_no_windows(self):
        events = [event(0, SAMPLE, 1000), event(0, SAMPLE, 6000)]
        result = gc_diagnostics(record([[3000, 3500]], events=events, gcWindowsDropped=1))
        self.assertEqual((result["complete"], result["windows"], result["largestGaps"][0]["overlapNs"]), (False, 0, 0))

    def test_failure_at_the_first_sample_starts_at_the_native_start(self):
        value = record([[150, 400]], threads=[{"workStartedNs": 100, "startedNs": 90}], events=[event(0, SAMPLE, 1000), event(0, FAILURE, 1010, DEBT_EXCEEDED)])
        failure = gc_diagnostics(value)["debtFailures"][0]
        self.assertEqual((failure["intervalNs"], failure["overlapNs"]), (920, 250), f"failure {failure}")

    def test_sums_window_lengths_and_keeps_pause(self):
        result = gc_diagnostics(record([[200, 230], [300, 340]]))
        self.assertEqual((result["windowNs"], result["pauseNs"]), (70, 70))


class MainChecks(unittest.TestCase):
    def test_a_second_analysis_ignores_the_summary_file(self):
        import json, pathlib, sys, tempfile
        from unittest.mock import patch
        import AnalyzeTargetGc
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            for index in range(1, 11):
                (root / f"gc-{index}.json").write_text(json.dumps(record([[200, 230]])))
            with patch.object(sys, "argv", ["AnalyzeTargetGc.py", directory]):
                self.assertEqual(AnalyzeTargetGc.main(), 0)
                self.assertEqual(AnalyzeTargetGc.main(), 0)
            self.assertEqual(len(json.loads((root / "gc-summary.json").read_text())["runs"]), 10)


if __name__ == "__main__":
    unittest.main()
