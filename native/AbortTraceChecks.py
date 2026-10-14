from copy import deepcopy
import unittest
from AnalyzeRuntimeSuspension import ClockAlignment, analyze
from RuntimeSuspensionChecks import fixture, boundary


def aborted_fixture(prefix=None, stage="measurement", warmup=0):
    if prefix is None:
        prefix = ["run-start", "measurement-start"]
    workload, decoded = fixture(prefix + ["run-abort", "run-end"], warmup)
    timestamp = workload["traceAnchors"][-2]["BeforeTimestamp"] - 1
    workload.update(passed=False, protocolPassed=False, failures=["retained failure"],
                    failureBoundary={"Timestamp": timestamp, "Stage": stage},
                    traceAbort={"AnchorId": len(prefix), "FailureTimestamp": timestamp, "Stage": stage})
    return workload, decoded


class AbortTraceChecks(unittest.TestCase):
    def test_explicit_abort_supports_each_reachable_marker_prefix(self):
        cases = [(["run-start"], "setup", 0),
                 (["run-start", "warmup-start"], "warmup", 250),
                 (["run-start", "warmup-start", "warmup-end"], "warmup-settlement", 250),
                 (["run-start", "measurement-start"], "measurement", 0),
                 (["run-start", "warmup-start", "warmup-end", "measurement-start"], "measurement", 250),
                 (["run-start", "measurement-start", "measurement-end"], "settlement", 0)]
        for prefix, stage, warmup in cases:
            with self.subTest(stage=stage, warmup=warmup):
                result = analyze(*aborted_fixture(prefix, stage, warmup))
                self.assertTrue(result["valid"])
                self.assertTrue(result["workloadAborted"])
                self.assertFalse(result["workloadProtocolPassed"])

    def test_abort_does_not_excuse_missing_markers_or_failure_identity(self):
        for kind in ("missing-record", "missing-event", "lost", "success", "contradictory-success", "no-failure", "wrong-id",
                     "wrong-stage", "early-failure", "late-failure", "duplicate", "missing-start"):
            with self.subTest(kind=kind):
                workload, decoded = aborted_fixture()
                if kind == "missing-record":
                    workload["traceAbort"] = None
                elif kind == "missing-event":
                    decoded["events"].pop(2)
                elif kind == "lost":
                    decoded["eventsLost"] = 1
                elif kind == "success":
                    workload["protocolPassed"] = True
                elif kind == "contradictory-success":
                    workload["passed"] = True
                elif kind == "no-failure":
                    workload["failures"] = []
                elif kind == "wrong-id":
                    workload["traceAbort"]["AnchorId"] = 1
                elif kind == "wrong-stage":
                    workload["traceAbort"]["Stage"] = workload["failureBoundary"]["Stage"] = "setup"
                elif kind in ("early-failure", "late-failure"):
                    timestamp = 1099 if kind == "early-failure" else 1201
                    workload["traceAbort"]["FailureTimestamp"] = workload["failureBoundary"]["Timestamp"] = timestamp
                elif kind == "duplicate":
                    workload["traceAnchors"].insert(2, deepcopy(workload["traceAnchors"][2]))
                else:
                    workload["traceAnchors"].pop(1)
                with self.assertRaises(ValueError):
                    ClockAlignment.parse(workload, decoded)

    def test_aborted_trace_still_requires_suspension_evidence_and_complete_episodes(self):
        workload, decoded = aborted_fixture()
        workload["runtimeCountersStart"] = {"StartedTimestamp": 1100, "Gen0Collections": 0}
        workload["failureBoundary"]["RuntimeCounters"] = {"FinishedTimestamp": 1199, "Gen0Collections": 1}
        missing = analyze(workload, decoded)
        self.assertFalse(missing["valid"])
        self.assertEqual(missing["anomalies"][0]["kind"], "collection-counter-without-suspension")
        decoded["events"] += [boundary(i, t) for i, t in zip((9, 8, 7, 3), (110, 120, 140, 150))]
        self.assertTrue(analyze(workload, decoded)["valid"])
        decoded["events"].pop()
        self.assertFalse(analyze(workload, decoded)["valid"])


if __name__ == "__main__":
    unittest.main()
