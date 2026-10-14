from copy import deepcopy
import unittest
from AnalyzeTargetCpuClock import analyze


def fixture():
    summary = {"count": 2, "error": "", "capture": "remote", "activity": "busy", "initialState": "R", "finalState": "R",
               "elapsedNanoseconds": 50, "controllerCpuNanoseconds": 30,
               "targetSelfBeforeNanoseconds": 1000, "targetSelfAfterNanoseconds": 1100}
    rows = [{"before": 100, "after": 120, "status": 0, "target_before": 105, "target_after": 115,
             "target_cpu": 1020, "resolution": 1, "registration": 1},
            {"before": 130, "after": 150, "status": 0, "target_before": 135, "target_after": 145,
             "target_cpu": 1040, "resolution": 1, "registration": 1}]
    return summary, rows


class TargetCpuClockChecks(unittest.TestCase):
    def test_busy_read_cost_and_identity(self):
        result = analyze(*fixture())
        self.assertTrue(result["valid"])
        self.assertEqual(result["controllerCpuPerReadingNanoseconds"], 15)
        self.assertEqual(result["callMedianNanoseconds"], 20)
        self.assertEqual(result["nativeBracketMedianNanoseconds"], 10)
        self.assertEqual(result["targetCpuDeltaNanoseconds"], 20)
        self.assertFalse(result["clockAccuracyEstablished"])

    def test_blocked_requires_sleep_and_stable_cpu(self):
        summary, rows = fixture()
        summary.update(activity="blocked", initialState="S", finalState="S")
        rows[1]["target_cpu"] = rows[0]["target_cpu"]
        self.assertTrue(analyze(summary, rows)["valid"])
        rows[1]["target_cpu"] += 1
        self.assertFalse(analyze(summary, rows)["valid"])
        summary["finalState"] = "R"
        with self.assertRaises(ValueError):
            analyze(summary, rows)

    def test_baseline_has_no_remote_readings(self):
        summary, rows = fixture()
        summary["capture"] = "baseline"
        with self.assertRaises(ValueError):
            analyze(summary, rows)
        for row in rows:
            for key in ("target_before", "target_after", "target_cpu", "resolution", "registration"):
                row[key] = 0
        self.assertIsNone(analyze(summary, rows)["targetCpuDeltaNanoseconds"])

    def test_inconsistencies_keep_both_raw_readings(self):
        for cpu in (1019, 1061):
            summary, rows = fixture()
            rows[1]["target_cpu"] = cpu
            result = analyze(summary, rows) if cpu > 1020 else None
            if result is None:
                with self.assertRaises(ValueError):
                    analyze(summary, rows)
            else:
                self.assertFalse(result["valid"])
                self.assertEqual(result["anomalies"][0]["previous"], rows[0])
                self.assertEqual(result["anomalies"][0]["current"], rows[1])

    def test_missing_readings_errors_identity_and_brackets_fail(self):
        summary, rows = fixture()
        with self.assertRaises(ValueError):
            analyze(summary, rows[:1])
        for key, value in (("status", 5), ("registration", 2), ("target_before", 129), ("target_after", 151),
                           ("target_cpu", 1101), ("resolution", 2), ("before", 119)):
            changed = deepcopy(rows)
            changed[1][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                analyze(summary, changed)
        summary["error"] = "clock read failed"
        with self.assertRaises(ValueError):
            analyze(summary, rows)


if __name__ == "__main__":
    unittest.main()
