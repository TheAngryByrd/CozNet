import unittest

from AnalyzeTargetPrediction import (DEBT_EXCEEDED, FAILURE, SAMPLE, add_largest, residual_run_delay, start_complete, trace_complete, trace_segments,
                                     wait_diagnostics)


def thread(**changes):
    value = {"workStartedNs": 2000, "startedNs": 1000, "finishedNs": 9000, "startRunDelayNs": 300, "runDelayNs": 800,
             "waitRunDelayNs": [], "waitRunDelaysDropped": 0}
    value.update(changes)
    return value


def point(ticks, run_time=0, delay=0, minor=0, voluntary=0):
    return {"ticks": ticks, "cpuTimeNs": run_time, "runDelayNs": delay, "minorFaults": minor, "majorFaults": 0,
            "voluntarySwitches": voluntary, "involuntarySwitches": 0}


def traced(**changes):
    return thread(startTrace=[point(1100), point(1200, 50, 10, 2), point(1900, 450, 30, 5, 1)], **changes)


def event(worker, kind, time, amount=0, detail=0):
    return [worker, 0, kind, 0, 0, amount, 0, 0, detail, time]


class StartGateChecks(unittest.TestCase):
    def test_ordered_start_record_is_complete(self):
        self.assertTrue(start_complete(thread()))

    def test_work_start_before_native_start_is_incomplete(self):
        self.assertFalse(start_complete(thread(workStartedNs=999)))

    def test_work_start_after_native_finish_is_incomplete(self):
        self.assertFalse(start_complete(thread(workStartedNs=9001)))

    def test_failed_start_read_is_incomplete(self):
        self.assertFalse(start_complete(thread(startRunDelayNs=-1)))

    def test_start_delay_above_whole_run_delay_is_incomplete(self):
        self.assertFalse(start_complete(thread(startRunDelayNs=801)))

    def test_boolean_value_is_incomplete(self):
        self.assertFalse(start_complete(thread(startRunDelayNs=True)))

    def test_missing_field_is_incomplete(self):
        record = thread()
        del record["workStartedNs"]
        self.assertFalse(start_complete(record))

    def test_residual_subtracts_start_and_wait_delay(self):
        self.assertEqual(residual_run_delay(thread(waitRunDelayNs=[100, 50]), True), 350)

    def test_residual_is_unknown_with_a_failed_wait_read(self):
        self.assertIsNone(residual_run_delay(thread(waitRunDelayNs=[100, -1]), True))

    def test_record_without_events_fails_both_gates(self):
        result = wait_diagnostics({"threads": [thread()]})
        self.assertFalse(result["complete"])
        self.assertFalse(result["startComplete"])


class StartTraceChecks(unittest.TestCase):
    def test_ordered_trace_is_complete(self):
        self.assertTrue(trace_complete(traced()))

    def test_trace_needs_three_points(self):
        record = traced()
        record["startTrace"] = record["startTrace"][:2]
        self.assertFalse(trace_complete(record))

    def test_first_point_before_native_start_is_incomplete(self):
        record = traced()
        record["startTrace"][0]["ticks"] = 999
        self.assertFalse(trace_complete(record))

    def test_last_point_after_work_start_is_incomplete(self):
        record = traced()
        record["startTrace"][2]["ticks"] = 2001
        self.assertFalse(trace_complete(record))

    def test_decreasing_counter_is_incomplete(self):
        record = traced()
        record["startTrace"][2]["minorFaults"] = 1
        self.assertFalse(trace_complete(record))

    def test_failed_counter_read_is_incomplete(self):
        record = traced()
        record["startTrace"][1]["voluntarySwitches"] = -1
        self.assertFalse(trace_complete(record))

    def test_segments_split_wall_time(self):
        segments = trace_segments(traced())
        self.assertEqual(segments["nativeToFirstNs"], 100)
        self.assertEqual(segments["setup"], {"wallNs": 100, "onCpuNs": 50, "runQueueNs": 10, "offCpuNs": 40, "minorFaults": 2,
                                             "majorFaults": 0, "voluntarySwitches": 0, "involuntarySwitches": 0})
        self.assertEqual(segments["barrier"], {"wallNs": 700, "onCpuNs": 400, "runQueueNs": 20, "offCpuNs": 280, "minorFaults": 3,
                                               "majorFaults": 0, "voluntarySwitches": 1, "involuntarySwitches": 0})

    def test_largest_keeps_the_maximum_of_each_segment(self):
        largest = {"nativeToFirstNs": 0, "setup": 0, "barrier": 0}
        longer = traced()
        longer["startTrace"][2]["ticks"] = 1950
        add_largest(largest, {"profile-zero": wait_diagnostics({"threads": [traced(), longer], "events": []}), "profile-speedup": None})
        add_largest(largest, {"profile-zero": wait_diagnostics({"threads": [traced()], "events": []}), "profile-speedup": None})
        self.assertEqual(largest, {"nativeToFirstNs": 100, "setup": 100, "barrier": 750})

    def test_record_without_trace_fails_trace_gate(self):
        result = wait_diagnostics({"threads": [thread()], "events": []})
        self.assertEqual((result["startComplete"], result["startTraceComplete"]), (True, False), f"result {result}")

    def test_startup_failure_reports_its_segments(self):
        record = {"threads": [traced()], "events": [event(0, FAILURE, 3000, 3000000, DEBT_EXCEEDED)]}
        failure = wait_diagnostics(record)["debtFailures"][0]
        self.assertEqual(failure["startSegments"]["barrier"]["onCpuNs"], 400, f"failure {failure}")


class StartupFailureChecks(unittest.TestCase):
    def test_failure_before_first_wake_reports_start_fields(self):
        record = {"threads": [thread(workStartedNs=2000), thread(workStartedNs=2500, startRunDelayNs=400, runDelayNs=900)],
                  "events": [event(0, SAMPLE, 3000), event(1, SAMPLE, 8000), event(1, FAILURE, 8000, 3000000, DEBT_EXCEEDED)]}
        result = wait_diagnostics(record)
        failure = result["debtFailures"][0]
        self.assertEqual((failure["worker"], failure["afterWake"], failure["startRunDelayNs"], failure["workStartToFailureNs"],
                          failure["firstSampleNs"], failure["residualRunDelayNs"]), (1, False, 400, 5500, [0, 5000], 500),
                         f"failure {failure}")
        self.assertEqual(result["workStartNs"], [-1000, -500])

    def test_startup_failure_keeps_wait_completeness(self):
        record = {"threads": [thread(waitRunDelaysDropped=1)], "events": [event(0, FAILURE, 3000, 3000000, DEBT_EXCEEDED)]}
        result = wait_diagnostics(record)
        self.assertEqual((result["complete"], result["startComplete"]), (False, True), f"result {result}")

    def test_startup_failure_with_complete_waits_stays_complete(self):
        record = {"threads": [thread(startRunDelayNs=-1)], "events": [event(0, FAILURE, 3000, 3000000, DEBT_EXCEEDED)]}
        result = wait_diagnostics(record)
        self.assertEqual((result["complete"], result["startComplete"]), (True, False), f"result {result}")

    def test_failure_with_incomplete_start_record_reports_unknown_start(self):
        record = {"threads": [thread(startRunDelayNs=-1)], "events": [event(0, FAILURE, 3000, 3000000, DEBT_EXCEEDED)]}
        failure = wait_diagnostics(record)["debtFailures"][0]
        self.assertEqual((failure["startRunDelayNs"], failure["workStartToFailureNs"], failure["residualRunDelayNs"]), (None, None, None),
                         f"failure {failure}")


if __name__ == "__main__":
    unittest.main()
