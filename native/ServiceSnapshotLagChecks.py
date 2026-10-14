from copy import deepcopy
import unittest
from AnalyzeServiceSnapshotLag import analyze, correlate_scheduler


def scenario():
    header = {"Command": 1, "Budget": 50, "Children": 0, "Captured": 200,
              "IdentityFirst": 4, "IdentitySecond": 5}
    early = {"Slot": 1, "Identity": "example", "Command": 1, "BudgetNanoseconds": 50,
             "Observation": {"Header": header, "Children": []}}
    late = deepcopy(early)
    late["Observation"]["Header"].update(Children=1, Captured=250)
    late["Observation"]["Children"] = [{"Native": {"Ordinal": 1,
        "Acknowledgment": {"Sequence": 3, "Started": 100, "HandlerStarted": 110,
                           "HandlerFinished": 120, "Observed": 150}}}]
    return {"passed": False, "serviceJournals": [late],
            "boundaries": [{"Stage": "measurement", "PendingServices": [early]}]}


class ServiceSnapshotLagChecks(unittest.TestCase):
    def scheduler_scenario(self):
        value = scenario()
        value.update(schedulerTiming=True, schedulerStopped=True, schedulerCaptures=[
            {"ProcessId": 10, "ThreadId": thread, "Failures": [], "FinalLost": 0,
             "EnableBefore": 1, "EnableAfter": 2, "DisableBefore": 300, "DisableAfter": 301,
             "Records": [{"Kind": 1, "Flags": 24576, "TimestampNanoseconds": 140, "Lost": 0},
                         {"Kind": 0, "Flags": 0, "TimestampNanoseconds": 190, "Lost": 0}]}
            for thread in (11, 12, 13)])
        value["serviceJournals"][0]["Observation"]["Children"][0].update(
            FirstSnapshotCaptured=250, FirstManagedReceiptUpper=260, ClockStatus=0)
        return value

    def test_scheduler_overlap_is_clipped_to_native_interval(self):
        value = self.scheduler_scenario()
        result = correlate_scheduler(value, analyze(value))
        self.assertTrue(result["schedulerIntegrityPassed"])
        self.assertFalse(result["workloadPassed"])
        self.assertFalse(result["overlapEstablishesCause"])
        self.assertEqual(result["findings"][0]["controllerSwitchOverlap"]["observedSwitchedOutNanoseconds"], 40)
        self.assertEqual(result["longestReceiptIntervals"][0]["controllerSwitchOverlap"]["wallNanoseconds"], 110)
        self.assertEqual(result["longestBeforeHandler"][0]["targetBeforeHandler"]["wallNanoseconds"], 10)
        self.assertEqual(result["longestAfterHandler"][0]["targetAfterHandler"]["observedSwitchedOutNanoseconds"], 10)

    def test_owner_overlap_requires_matching_native_identity(self):
        value = self.scheduler_scenario()
        owner = deepcopy(value["schedulerCaptures"][0])
        owner["ThreadId"] = 14
        value["schedulerCaptures"].append(owner)
        value.update(serviceOwnerSchedulerTiming=True, backendDetails={"nativeOwnerThreadId": 14})
        result = correlate_scheduler(value, analyze(value))
        self.assertTrue(result["ownerCaptured"])
        self.assertEqual(result["longestAfterHandler"][0]["ownerAfterHandler"]["observedSwitchedOutNanoseconds"], 10)
        for mode in ("wrong", "missing", "loss"):
            with self.subTest(mode=mode):
                invalid = deepcopy(value)
                if mode == "wrong": invalid["backendDetails"]["nativeOwnerThreadId"] = 15
                if mode == "missing": invalid["schedulerCaptures"].pop()
                if mode == "loss": invalid["schedulerCaptures"][3]["FinalLost"] = 1
                with self.assertRaises(ValueError):
                    correlate_scheduler(invalid, analyze(invalid))

    def test_scheduler_loss_missing_stop_and_wrong_identity_are_rejected(self):
        for mode in ("lost", "stop", "duplicate", "process", "bounds", "receipt"):
            with self.subTest(mode=mode):
                value = self.scheduler_scenario()
                if mode == "lost": value["schedulerCaptures"][2]["FinalLost"] = 1
                if mode == "stop": value["schedulerStopped"] = False
                if mode == "duplicate": value["schedulerCaptures"][1]["ThreadId"] = 11
                if mode == "process": value["schedulerCaptures"][1]["ProcessId"] = 99
                if mode == "bounds": value["schedulerCaptures"][0]["EnableAfter"] = 160
                if mode == "receipt": value["serviceJournals"][0]["Observation"]["Children"][0]["ClockStatus"] = 5
                with self.assertRaises(ValueError):
                    correlate_scheduler(value, analyze(value))

    def test_native_clock_lower_bound_preserves_workload_failure(self):
        result = analyze(scenario())
        self.assertEqual(result["matchedPrefixes"], 1)
        self.assertEqual(result["unmatchedPrefixes"], 0)
        self.assertEqual(result["findings"][0]["postOwnerCopyLowerBoundNanoseconds"], 50)
        self.assertEqual(result["findings"][0]["firstMissingOrdinal"], 1)
        self.assertFalse(result["causeEstablished"])
        self.assertFalse(result["workloadPassed"])

    def test_later_child_does_not_imply_stale_capture(self):
        value = scenario()
        value["serviceJournals"][0]["Observation"]["Children"][0]["Native"]["Acknowledgment"]["Observed"] = 220
        self.assertEqual(analyze(value)["findings"], [])

    def test_unknown_identity_is_unmatched_not_correlated(self):
        value = scenario()
        value["serviceJournals"][0]["Identity"] = "other"
        result = analyze(value)
        self.assertEqual((result["matchedPrefixes"], result["unmatchedPrefixes"], result["findings"]), (0, 1, []))

    def test_inconsistent_count_or_identity_or_clock_is_rejected(self):
        for field, replacement in (("Children", 1), ("IdentityFirst", 99), ("Captured", 300)):
            with self.subTest(field=field):
                value = scenario()
                value["boundaries"][0]["PendingServices"][0]["Observation"]["Header"][field] = replacement
                with self.assertRaises(ValueError):
                    analyze(value)

    def test_changed_prefix_and_duplicate_final_are_rejected(self):
        value = scenario()
        early = value["boundaries"][0]["PendingServices"][0]["Observation"]
        early["Header"]["Children"] = 1
        early["Children"] = deepcopy(value["serviceJournals"][0]["Observation"]["Children"])
        early["Children"][0]["Native"]["Acknowledgment"]["Sequence"] = 2
        with self.assertRaises(ValueError):
            analyze(value)
        value = scenario()
        value["serviceJournals"] *= 2
        with self.assertRaises(ValueError):
            analyze(value)


if __name__ == "__main__":
    unittest.main()
