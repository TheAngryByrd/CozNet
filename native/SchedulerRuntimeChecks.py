from copy import deepcopy
import json
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory
import unittest

from AnalyzeSchedulerRuntime import analyze
from RuntimeSuspensionChecks import boundary, fixture


def combined_fixture():
    workload, events = fixture()
    workload.update(schedulerTiming=True, passed=False, capacityViolations=["peer debt"], peakDebt=[])
    workload["schedulerCaptures"] = [
        {"ThreadId": thread, "ProcessId": 42, "Failures": [], "FinalLost": 0,
         "EnableBefore": 10, "EnableAfter": 20, "DisableBefore": 180,
         "DisableAfter": 190, "Records": []}
        for thread in (10, 20, 30)
    ]
    events["events"] += [boundary(i, t) for i, t in zip((9, 8, 7, 3), (110, 120, 140, 150))]
    workload["requests"] = [
        {"Slot": slot, "Sequence": sequence, "Stage": "measurement", "StartedStage": "measurement",
         "StartedTimestamp": start, "CompletedTimestamp": start + 10,
         "Result": {"WaitNanoseconds": 0, "Timing": {
             "RequestStartedNanoseconds": 30, "HandlerStartedNanoseconds": handler,
             "HandlerFinishedNanoseconds": 80, "ObservedNanoseconds": 90}}}
        for slot, sequence, start, handler in ((0, 7, 1125, 40), (1, 7, 1170, 60), (0, 8, 1180, 50))
    ]
    return workload, events


def write_case(directory, workload, events):
    directory.mkdir(parents=True, exist_ok=True)
    (directory / "workload.json").write_text(json.dumps(workload), encoding="utf-8")
    (directory / "events.json").write_text(json.dumps(events, default=int), encoding="utf-8")


class SchedulerRuntimeChecks(unittest.TestCase):
    def test_joins_by_slot_and_sequence_after_scheduler_sort(self):
        with TemporaryDirectory() as temp:
            directory = Path(temp)
            write_case(directory, *combined_fixture())
            result = analyze(directory)
        requests = result["longestBeforeHandler"]
        self.assertEqual([(r["slot"], r["sequence"]) for r in requests], [(1, 7), (0, 8), (0, 7)])
        self.assertEqual([len(r["enclosingManagedRequestSuspensions"]) for r in requests], [0, 0, 1])
        overlap = requests[2]["enclosingManagedRequestSuspensions"][0]
        self.assertEqual(overlap["suspendedCore"]["guaranteedNanoseconds"], 10000000)
        self.assertEqual(result["runtimeOverlapScope"], "enclosing managed request, not native phase or switched-out interval")
        self.assertFalse(result["overlapEstablishesCausation"])

    def test_rejects_duplicate_request_identity(self):
        workload, events = combined_fixture()
        workload["requests"].append(deepcopy(workload["requests"][0]))
        with TemporaryDirectory() as temp:
            directory = Path(temp)
            write_case(directory, workload, events)
            with self.assertRaisesRegex(ValueError, "Request identities repeat"):
                analyze(directory)

    def test_rejects_incomplete_runtime_correlation(self):
        workload, events = combined_fixture()
        events["events"].pop()
        with TemporaryDirectory() as temp:
            directory = Path(temp)
            write_case(directory, workload, events)
            with self.assertRaisesRegex(ValueError, "Runtime correlation is incomplete"):
                analyze(directory)

    def test_rejects_scheduler_loss(self):
        workload, events = combined_fixture()
        workload["schedulerCaptures"][1]["FinalLost"] = 1
        with TemporaryDirectory() as temp:
            directory = Path(temp)
            write_case(directory, workload, events)
            with self.assertRaisesRegex(ValueError, "incomplete loss accounting"):
                analyze(directory)

    def test_rejects_mismatched_process_identity(self):
        workload, events = combined_fixture()
        for event in events["events"]:
            event["processId"] = 43
        with TemporaryDirectory() as temp:
            directory = Path(temp)
            write_case(directory, workload, events)
            with self.assertRaisesRegex(ValueError, "Runtime and scheduler process identities differ"):
                analyze(directory)

    def test_retains_capacity_failure_in_valid_analysis(self):
        with TemporaryDirectory() as temp:
            directory = Path(temp)
            write_case(directory, *combined_fixture())
            result = analyze(directory)
        self.assertTrue(result["valid"])
        self.assertFalse(result["workloadPassed"])
        self.assertEqual(result["capacityViolations"], ["peer debt"])
        self.assertEqual((result["eventsLost"], result["runtimeEpisodes"]), (0, 1))

    def test_cli_retains_valid_and_invalid_cases_and_fails(self):
        with TemporaryDirectory() as temp:
            root = Path(temp)
            write_case(root / "block-valid", *combined_fixture())
            workload, events = combined_fixture()
            events["eventsLost"] = 1
            write_case(root / "block-invalid", workload, events)
            completed = subprocess.run([sys.executable, str(Path(__file__).with_name("AnalyzeSchedulerRuntime.py")),
                                        str(root)], capture_output=True, text=True)
            result = json.loads((root / "scheduler-runtime-analysis.json").read_text())
        self.assertEqual(completed.returncode, 1, completed.stderr)
        self.assertEqual(set(result), {"block-valid", "block-invalid"})
        self.assertTrue(result["block-valid"]["valid"])
        self.assertFalse(result["block-invalid"]["valid"])
        self.assertIn("error", result["block-invalid"])


if __name__ == "__main__":
    unittest.main()
