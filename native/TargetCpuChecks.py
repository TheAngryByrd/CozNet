from copy import deepcopy
import unittest
from AnalyzeTargetCpu import analyze, request_interval


def request():
    return {"Slot": 1, "Sequence": 9, "Stage": "measurement", "StartedStage": "measurement",
            "TargetCpuStart": {"BeforeNanoseconds": 100, "AfterNanoseconds": 110, "CpuNanoseconds": 1000,
                               "Registration": 2, "ResolutionNanoseconds": 1},
            "TargetCpuEnd": {"BeforeNanoseconds": 230, "AfterNanoseconds": 240, "CpuNanoseconds": 1045,
                             "Registration": 2, "ResolutionNanoseconds": 1},
            "Result": {"Sequence": 9, "MeasuredHandlerNanoseconds": 10,
                       "Timing": {"RequestStartedNanoseconds": 120, "QueueReturnedNanoseconds": 200,
                                  "HandlerStartedNanoseconds": 190, "HandlerFinishedNanoseconds": 200,
                                  "ObservedNanoseconds": 220}}}


def workload(values):
    return {"targetCpuTiming": True, "backend": "linux-x64-signal", "requests": values,
            "backendDetails": {"timingClock": "linux-clock-monotonic", "timingUnit": "nanoseconds", "targetCpuTiming": True}}


class TargetCpuChecks(unittest.TestCase):
    def test_bounds_exclude_work_outside_first_handler_clock_gap(self):
        value = request()
        value["TargetCpuEnd"]["CpuNanoseconds"] = 1100
        result = request_interval(value)
        self.assertEqual(result["beforeFirstHandlerClockNanoseconds"], 70)
        self.assertEqual(result["modelGapCpuLowerNanoseconds"], 30)
        self.assertEqual(result["modelGapCpuUpperNanoseconds"], 70)
        self.assertEqual(result["modelGapOffCpuLowerNanoseconds"], 0)
        self.assertEqual(result["modelGapOffCpuUpperNanoseconds"], 40)

    def test_low_cpu_retains_execution_and_nonexecution_uncertainty(self):
        result = request_interval(request())
        self.assertEqual(result["modelGapCpuLowerNanoseconds"], 0)
        self.assertEqual(result["modelGapCpuUpperNanoseconds"], 45)
        self.assertEqual(result["modelGapOffCpuLowerNanoseconds"], 25)
        self.assertEqual(result["modelGapOffCpuUpperNanoseconds"], 70)

    def test_large_clock_origins_preserve_integer_bounds(self):
        value = request()
        for snapshot in (value["TargetCpuStart"], value["TargetCpuEnd"]):
            for key in ("BeforeNanoseconds", "AfterNanoseconds", "CpuNanoseconds"):
                snapshot[key] += 10**18
        for key in value["Result"]["Timing"]:
            value["Result"]["Timing"][key] += 10**18
        self.assertEqual(request_interval(value), request_interval(request()))

    def test_full_outer_cpu_budget_fills_gap_without_exceeding_it(self):
        value = request()
        value["TargetCpuEnd"]["CpuNanoseconds"] = 1140
        result = request_interval(value)
        self.assertEqual(result["modelGapCpuLowerNanoseconds"], 70)
        self.assertEqual(result["modelGapCpuUpperNanoseconds"], 70)
        self.assertEqual(result["modelGapOffCpuUpperNanoseconds"], 0)

    def test_queue_return_can_precede_or_follow_handler(self):
        for queue in (120, 180, 210, 220):
            value = request()
            value["Result"]["Timing"]["QueueReturnedNanoseconds"] = queue
            self.assertEqual(request_interval(value)["beforeFirstHandlerClockNanoseconds"], 70)

    def test_bad_identity_clock_order_and_duration_fail(self):
        changes = [("Slot", 2), ("Sequence", True), ("TargetCpuEnd", None)]
        for key, invalid in changes:
            with self.subTest(key=key):
                value = request()
                value[key] = invalid
                with self.assertRaises(ValueError):
                    request_interval(value)
        for key, invalid in (("Registration", 1), ("ResolutionNanoseconds", 2), ("BeforeNanoseconds", 219),
                             ("AfterNanoseconds", 229), ("CpuNanoseconds", 999), ("CpuNanoseconds", 1141)):
            with self.subTest(key=key, invalid=invalid):
                value = request()
                value["TargetCpuEnd"][key] = invalid
                with self.assertRaises(ValueError):
                    request_interval(value)
        for key, invalid in (("QueueReturnedNanoseconds", 221), ("HandlerFinishedNanoseconds", 201)):
            value = request()
            value["Result"]["Timing"][key] = invalid
            with self.assertRaises(ValueError):
                request_interval(value)

    def test_anomalies_retain_raw_requests_and_fail_whole_analysis(self):
        bad = request()
        bad["Sequence"] = bad["Result"]["Sequence"] = 10
        bad["TargetCpuEnd"]["CpuNanoseconds"] = 1141
        result = analyze(workload([request(), bad]))
        self.assertFalse(result["valid"])
        self.assertEqual(result["requestCount"], 2)
        self.assertEqual(result["analyzedRequestCount"], 1)
        self.assertEqual(result["anomalies"][0]["request"], bad)
        self.assertIn("by 1 ns", result["anomalies"][0]["error"])

    def test_duplicate_sequences_fail_and_warmup_stays_separate(self):
        warmup = deepcopy(request())
        warmup["Stage"] = warmup["StartedStage"] = "warmup"
        warmup["Sequence"] = warmup["Result"]["Sequence"] = 10
        result = analyze(workload([warmup, request()]))
        self.assertTrue(result["valid"])
        self.assertEqual(result["measurementRequestCount"], 1)
        self.assertEqual(result["longestMeasurementGaps"][0]["sequence"], 9)
        self.assertFalse(result["clockAccuracyEstablished"])
        self.assertTrue(result["boundsAssumeExactClockAccounting"])
        self.assertFalse(analyze(workload([request(), request()]))["valid"])

    def test_disabled_capture_and_other_clock_contracts_fail(self):
        value = workload([])
        value["targetCpuTiming"] = False
        with self.assertRaises(ValueError):
            analyze(value)
        value = workload([])
        value["backendDetails"]["timingClock"] = "stopwatch"
        with self.assertRaises(ValueError):
            analyze(value)
        self.assertEqual(analyze(workload([]))["analyzedRequestCount"], 0)


if __name__ == "__main__":
    unittest.main()
