from copy import deepcopy
from decimal import Decimal
import unittest

from AnalyzeControllerCpu import analyze, request_interval


def request():
    return {"Slot": 1, "Sequence": 9, "Stage": "measurement", "StartedStage": "measurement",
            "StartedTimestamp": 120, "CompletedTimestamp": 220,
            "ControllerCpuStart": {"BeforeTimestamp": 100, "AfterTimestamp": 110, "CpuNanoseconds": 1000},
            "ControllerCpuEnd": {"BeforeTimestamp": 230, "AfterTimestamp": 240, "CpuNanoseconds": 1045}}


class ControllerCpuChecks(unittest.TestCase):
    def test_request_bounds_exclude_uncertain_outside_work(self):
        result = request_interval(request(), 1_000_000_000)
        self.assertEqual(result["requestNanoseconds"], 100)
        self.assertEqual(result["requestCpuLowerNanoseconds"], 5)
        self.assertEqual(result["requestCpuUpperNanoseconds"], 45)
        self.assertEqual(result["requestOffCpuLowerNanoseconds"], 55)
        self.assertEqual(result["requestOffCpuUpperNanoseconds"], 95)

    def test_frequency_conversion_and_large_timestamps_preserve_precision(self):
        value = request()
        for item in (value["ControllerCpuStart"], value["ControllerCpuEnd"]):
            item["BeforeTimestamp"] += 10**17
            item["AfterTimestamp"] += 10**17
        value["StartedTimestamp"] += 10**17
        value["CompletedTimestamp"] += 10**17
        result = request_interval(value, 10_000_000)
        self.assertEqual(result["requestNanoseconds"], Decimal(10000))
        self.assertEqual(result["requestOffCpuLowerNanoseconds"], Decimal(9955))

    def test_cpu_bounds_clamp_at_request_duration(self):
        value = request()
        value["ControllerCpuEnd"]["CpuNanoseconds"] = 1140
        result = request_interval(value, 1_000_000_000)
        self.assertEqual(result["requestCpuLowerNanoseconds"], 100)
        self.assertEqual(result["requestCpuUpperNanoseconds"], 100)
        self.assertEqual(result["requestOffCpuUpperNanoseconds"], 0)

    def test_invalid_brackets_and_counters_fail(self):
        for kind in ("missing", "late-start", "early-end", "decreasing", "excessive", "reversed"):
            with self.subTest(kind=kind):
                value = request()
                if kind == "missing":
                    value["ControllerCpuStart"] = None
                elif kind == "late-start":
                    value["ControllerCpuStart"]["AfterTimestamp"] = 121
                elif kind == "early-end":
                    value["ControllerCpuEnd"]["BeforeTimestamp"] = 219
                elif kind == "decreasing":
                    value["ControllerCpuEnd"]["CpuNanoseconds"] = 999
                elif kind == "excessive":
                    value["ControllerCpuEnd"]["CpuNanoseconds"] = 1141
                else:
                    value["CompletedTimestamp"] = 119
                with self.assertRaises(ValueError):
                    request_interval(value, 1_000_000_000)

    def test_analysis_keeps_completed_scope_and_orders_long_requests(self):
        first = request()
        second = deepcopy(first)
        second["Sequence"] = 10
        second["CompletedTimestamp"] = 215
        warmup = deepcopy(first)
        warmup["Stage"] = warmup["StartedStage"] = "warmup"
        workload = {"diagnosticTrace": True, "stopwatchFrequency": 1_000_000_000, "requests": [second, warmup, first]}
        result = analyze(workload)
        self.assertEqual(result["requestCount"], 3)
        self.assertEqual(result["measurementRequestCount"], 2)
        self.assertEqual([r["sequence"] for r in result["longestMeasurementRequests"]], [9, 10])
        self.assertTrue(result["completedRequestsOnly"])
        self.assertTrue(result["cpuIntervalsCanOverlap"])
        self.assertFalse(result["offCpuIdentifiesSchedulerCause"])
        workload["diagnosticTrace"] = False
        with self.assertRaises(ValueError):
            analyze(workload)


if __name__ == "__main__":
    unittest.main()
