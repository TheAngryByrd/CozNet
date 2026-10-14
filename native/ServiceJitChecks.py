from copy import deepcopy
from decimal import Decimal
import unittest

from AnalyzeServiceJit import analyze
from RuntimeSuspensionChecks import fixture


def scenario():
    workload, decoded = fixture()
    decoded["includeJit"] = True
    for event in decoded["events"]:
        event["threadId"] = 7
    workload["controllerPhases"] = [{"Stage": "measurement", "Phase": "drain", "Iteration": 1,
        "Before": {"BeforeTimestamp": 1100, "AfterTimestamp": 1101, "CpuNanoseconds": 100},
        "After": {"BeforeTimestamp": 1199, "AfterTimestamp": 1200, "CpuNanoseconds": 50000100}, "Completed": False}]
    return workload, decoded


def compilation(method, start, end, thread=7):
    payload = {"ClrInstanceID": 0, "ModuleID": 1, "MethodID": method, "MethodNamespace": "Example", "MethodName": str(method)}
    return [{"provider": "Microsoft-Windows-DotNETRuntime", "processId": 42, "threadId": thread,
             "id": identifier, "milliseconds": Decimal(time), "payload": deepcopy(payload)}
            for identifier, time in ((145, start), (143, end))]


class ServiceJitChecks(unittest.TestCase):
    def test_nested_compilations_count_once_and_exclude_other_threads(self):
        workload, decoded = scenario()
        for anchor in workload["traceAnchors"]:
            anchor["AfterTimestamp"] = anchor["BeforeTimestamp"]
        decoded["events"] += compilation(1, 110, 160) + compilation(2, 120, 140) + compilation(3, 100, 190, thread=8)
        result = analyze(workload, decoded)
        phase = result["phases"][0]
        self.assertEqual(result["controllerCompilations"], 2)
        self.assertEqual((phase["jitElapsedLowerMilliseconds"], phase["jitElapsedUpperMilliseconds"]), (50, 50))
        self.assertEqual((phase["wallMilliseconds"], phase["cpuMilliseconds"], phase["completed"]), (100, 50, False))
        self.assertEqual([item["method"] for item in phase["methods"]], ["Example.1", "Example.2"])

    def test_partial_overlap_preserves_clock_uncertainty(self):
        workload, decoded = scenario()
        decoded["events"] += compilation(1, 98, 110)
        phase = analyze(workload, decoded)["phases"][0]
        self.assertEqual((phase["jitElapsedLowerMilliseconds"], phase["jitElapsedUpperMilliseconds"]), (8, 14))

    def test_invalid_trace_or_pairing_is_rejected(self):
        for kind in ("loss", "disabled", "duplicate", "unmatched", "controller"):
            with self.subTest(kind=kind):
                workload, decoded = scenario()
                events = compilation(1, 110, 160)
                if kind == "loss":
                    decoded["eventsLost"] = 1
                elif kind == "disabled":
                    decoded["includeJit"] = False
                elif kind == "duplicate":
                    events.insert(0, deepcopy(events[0]))
                elif kind == "unmatched":
                    events.pop()
                elif kind == "controller":
                    decoded["events"][1]["threadId"] = 8
                decoded["events"] += events
                with self.assertRaises(ValueError):
                    analyze(workload, decoded)


if __name__ == "__main__":
    unittest.main()
