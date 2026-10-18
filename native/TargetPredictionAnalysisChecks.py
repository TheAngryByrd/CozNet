import unittest

from AnalyzeTargetPrediction import DEBT_EXCEEDED, FAILURE, SAMPLE, residual_run_delay, start_complete, wait_diagnostics


def thread(**changes):
    value = {"workStartedNs": 2000, "startedNs": 1000, "finishedNs": 9000, "startRunDelayNs": 300, "runDelayNs": 800,
             "waitRunDelayNs": [], "waitRunDelaysDropped": 0}
    value.update(changes)
    return value


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
