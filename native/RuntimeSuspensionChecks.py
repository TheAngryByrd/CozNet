from copy import deepcopy
from decimal import Decimal
import unittest

from AnalyzeRuntimeSuspension import ClockAlignment, MARKERS, RUNTIME, analyze, overlap_bounds, pair_episodes


def boundary(identifier, time, instance=0):
    return {"provider": RUNTIME, "id": identifier, "version": 1, "milliseconds": Decimal(time),
            "processId": 42, "payload": {"ClrInstanceID": instance, "Reason": 6, "Count": 12}}


def fixture(phases=None, warmup=0):
    if phases is None:
        phases = ["run-start", "measurement-start", "measurement-end", "run-end"]
    anchors, events = [], []
    for index, phase in enumerate(phases):
        anchors.append({"Id": index, "Phase": phase, "BeforeTimestamp": 1000 + index * 100,
                        "AfterTimestamp": 1004 + index * 100})
        events.append({"provider": MARKERS, "id": 1, "version": 0, "milliseconds": Decimal(index * 100),
                       "processId": 42, "payload": {"id": index, "phase": phase}})
    workload = {"stopwatchFrequency": 1000, "warmupMilliseconds": warmup, "traceAnchors": anchors,
                "requests": [], "peakDebt": []}
    return workload, {"eventsLost": 0, "events": events}


class RuntimeSuspensionChecks(unittest.TestCase):
    def test_alignment_preserves_offset_interval(self):
        workload, decoded = fixture()
        workload["traceAnchors"][1]["BeforeTimestamp"] += 1
        workload["traceAnchors"][2]["AfterTimestamp"] -= 1
        alignment = ClockAlignment.parse(workload, decoded)
        self.assertEqual((alignment.lower, alignment.upper, alignment.process), (1001, 1003, 42))

    def test_alignment_rejects_loss_missing_duplicate_and_wrong_process(self):
        for change in ("loss", "missing", "duplicate", "process", "phase", "offset", "bracket"):
            with self.subTest(change=change):
                workload, decoded = fixture()
                if change == "loss":
                    decoded["eventsLost"] = 1
                elif change == "missing":
                    decoded["events"].pop()
                elif change == "duplicate":
                    decoded["events"][1] = deepcopy(decoded["events"][0])
                elif change == "process":
                    decoded["events"][1]["processId"] = 43
                elif change == "phase":
                    workload["traceAnchors"][1]["Phase"] = "warmup-start"
                elif change == "offset":
                    decoded["events"][1]["milliseconds"] += 10
                else:
                    workload["traceAnchors"][1]["AfterTimestamp"] = 1099
                with self.assertRaises(ValueError):
                    ClockAlignment.parse(workload, decoded)

    def test_pairing_separates_interleaved_instances(self):
        workload, decoded = fixture()
        events = [boundary(i, 10 + j * 10 + instance, instance)
                  for j, i in enumerate((9, 8, 7, 3)) for instance in (0, 1)]
        episodes, anomalies = pair_episodes(events, ClockAlignment.parse(workload, decoded))
        self.assertEqual(anomalies, [])
        self.assertEqual([e["clrInstanceId"] for e in episodes], [0, 1])
        self.assertEqual([e["reason"] for e in episodes], [6, 6])
        self.assertEqual([e["gcCount"] for e in episodes], [12, 12])

    def test_retry_and_truncated_sequences_fail_without_invented_endpoints(self):
        for ids in ((9, 7, 3), (9, 8), (9, 9, 8, 7, 3), (8, 7, 3)):
            with self.subTest(ids=ids):
                workload, decoded = fixture()
                events = [boundary(i, 10 + j) for j, i in enumerate(ids)]
                episodes, anomalies = pair_episodes(events, ClockAlignment.parse(workload, decoded))
                self.assertTrue(anomalies, f"Sequence {ids} must remain ambiguous.")
                self.assertEqual(sum(len(e["events"]) for e in episodes + anomalies), len(events))
                self.assertEqual(len(episodes), 1 if ids == (9, 9, 8, 7, 3) else 0)

    def test_unknown_version_is_retained_as_anomaly(self):
        workload, decoded = fixture()
        event = boundary(9, 10)
        event["version"] = 2
        episodes, anomalies = pair_episodes([event], ClockAlignment.parse(workload, decoded))
        self.assertEqual(episodes, [])
        self.assertEqual(anomalies, [{"kind": "unsupported-boundary", "events": [event]}])

    def test_overlap_bounds_include_clock_uncertainty_and_touching_intervals(self):
        alignment = ClockAlignment.parse(*fixture())
        result = overlap_bounds(1012, 1022, Decimal(10), Decimal(20), alignment)
        self.assertEqual(result, {"guaranteedNanoseconds": Decimal(6000000),
                                  "possibleNanoseconds": Decimal(10000000)})
        self.assertEqual(overlap_bounds(1024, 1030, 10, 20, alignment)["possibleNanoseconds"], 0)
        with self.assertRaises(ValueError):
            overlap_bounds(1030, 1024, 10, 20, alignment)

    def test_full_analysis_correlates_requests_and_peaks(self):
        workload, decoded = fixture()
        decoded["events"] += [boundary(i, t) for i, t in zip((9, 8, 7, 3), (110, 120, 140, 150))]
        workload["requests"] = [{"Slot": 1, "Sequence": 7, "Stage": "measurement", "StartedStage": "measurement",
                                 "StartedTimestamp": 1125, "CompletedTimestamp": 1135, "Result": {"WaitNanoseconds": 0}}]
        workload["peakDebt"] = [{"Slot": 1, "Boundary": {"Timestamp": 1135},
                                 "Interval": {"StartedTimestamp": 1125, "FinishedTimestamp": 1135}}]
        result = analyze(workload, decoded)
        self.assertTrue(result["valid"])
        self.assertEqual(result["requestCount"], 1)
        request = result["overlappingRequests"][0]
        self.assertEqual((request["sequence"], request["waitNanoseconds"]), (7, 0))
        overlap = request["overlaps"][0]
        self.assertEqual(overlap["suspendedCore"]["guaranteedNanoseconds"], 10000000)
        self.assertEqual(overlap["acquisition"]["possibleNanoseconds"], 0)
        self.assertEqual(result["measurementPeaks"][0]["overlaps"], request["overlaps"])
        self.assertFalse(result["overlapEstablishesCausation"])

    def test_recorded_collections_require_suspension_evidence(self):
        workload, decoded = fixture()
        workload["gcObservations"] = [{"StartedTimestamp": 1100, "FinishedTimestamp": 1160, "CollectionIndex": 1}]
        workload["runtimeCountersStart"] = {"StartedTimestamp": 1100, "Gen0Collections": 0}
        workload["measurement"] = {"RuntimeCounters": {"FinishedTimestamp": 1200, "Gen0Collections": 1}}
        absent = analyze(workload, decoded)
        self.assertFalse(absent["valid"])
        self.assertEqual([a["kind"] for a in absent["anomalies"]],
                         ["forced-collection-without-suspension", "collection-counter-without-suspension"])
        decoded["events"] += [boundary(i, t) for i, t in zip((9, 8, 7, 3), (110, 120, 140, 150))]
        self.assertTrue(analyze(workload, decoded)["valid"])
        workload["gcObservations"].append(dict(workload["gcObservations"][0]))
        self.assertFalse(analyze(workload, decoded)["valid"], "One episode cannot cover two forced collections.")
        workload["gcObservations"].pop()
        workload["gcObservations"][0]["StartedTimestamp"] = 1170
        workload["gcObservations"][0]["FinishedTimestamp"] = 1190
        outside = analyze(workload, decoded)
        self.assertFalse(outside["valid"])
        self.assertEqual([a["kind"] for a in outside["anomalies"]], ["forced-collection-without-suspension"])


if __name__ == "__main__":
    unittest.main()
