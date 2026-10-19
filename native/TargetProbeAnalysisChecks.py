import unittest

import json
import pathlib
import tempfile

from AnalyzeTargetProbe import delta, items, parse_snapshot, probe_values, spearman

FULL = """utc 1000
uptime 12.50 40.00
== stat
cpu  10 0 5 100 2 0 1 3 0 0
cpu0 5 0 2 50 1 0 0 1 0 0
== pressure
some avg10=0.00 avg60=0.00 avg300=0.00 total=700
full avg10=0.00 avg60=0.00 avg300=0.00 total=20
== loadavg
0.10 0.20 0.30 1/100 42
"""

BARE = """utc 2000
uptime unsupported
== stat
cpu  10 0 5 100
== pressure
unsupported
== loadavg
unsupported
"""


class ProbeAnalysisTests(unittest.TestCase):
    def test_a_full_snapshot_gives_ticks_steal_and_pressure(self):
        s = parse_snapshot(FULL)
        self.assertEqual((s["utcNs"], s["boottimeSeconds"], s["totalTicks"], s["stealTicks"], s["pressureSomeTotalUs"]), (1000, 12.5, 121, 3, 700))
        guest = parse_snapshot(FULL.replace("cpu  10 0 5 100 2 0 1 3 0 0", "cpu  10 0 5 100 2 0 1 3 7 7"))
        self.assertEqual(guest["totalTicks"], 121)

    def test_missing_counters_are_missing_not_zero(self):
        s = parse_snapshot(BARE)
        self.assertEqual((s["boottimeSeconds"], s["stealTicks"], s["pressureSomeTotalUs"]), (None, None, None))
        self.assertEqual(delta(parse_snapshot(FULL), s, "stealTicks"), "missing")
        self.assertEqual(delta(parse_snapshot(FULL), s, "pressureSomeTotalUs"), "missing")
        self.assertEqual(delta(None, s, "totalTicks"), "missing")
        self.assertIsNone(parse_snapshot(None))
        self.assertEqual(delta(parse_snapshot(FULL), parse_snapshot(FULL.replace("total=700", "total=900")), "pressureSomeTotalUs"), 200)

    def test_a_failed_checksum_or_cpu_clock_invalidates_the_probe(self):
        records = [{"wallNs": 100, "cpuNs": 90, "cpuBefore": 1, "cpuAfter": 2}, {"wallNs": 300, "cpuNs": 270, "cpuBefore": 2, "cpuAfter": 2}]
        good = probe_values({"records": records, "differingResults": 0, "passed": True})
        self.assertEqual((good["valid"], good["samples"], good["medianWallNs"], good["medianCpuNs"], good["cpusSeen"]), (True, 2, 200, 180, [1, 2]))
        self.assertAlmostEqual(good["cpuOverWall"], 0.9)
        self.assertFalse(probe_values({"records": records, "differingResults": 1, "passed": True})["valid"])
        self.assertFalse(probe_values({"records": records[:1] + [{**records[1], "cpuNs": -1}], "differingResults": 0, "passed": True})["valid"])
        self.assertFalse(probe_values({"records": records, "differingResults": 0, "passed": False})["valid"])
        self.assertEqual(probe_values({"records": [{"wallNs": 5}], "differingResults": 0, "passed": True})["reason"], "probe record has the wrong shape")
        self.assertFalse(probe_values(None)["valid"])

    def test_the_rank_correlation_handles_ties_and_small_samples(self):
        self.assertAlmostEqual(spearman([1, 2, 3, 4], [10, 20, 30, 40])["rho"], 1.0)
        self.assertAlmostEqual(spearman([1, 2, 3, 4], [40, 30, 20, 10])["rho"], -1.0)
        self.assertAlmostEqual(spearman([1, 1, 2, 3], [5, 5, 6, 7])["rho"], 1.0)
        self.assertEqual(spearman([1, 2], [3, 4]), {"n": 2, "rho": None})
        self.assertEqual(spearman([1, None, 3, 4], [1, 2, None, 4])["n"], 2)
        self.assertIsNone(spearman([1, 1, 1], [1, 2, 3])["rho"])
        self.assertAlmostEqual(spearman([1, 2, 2, 3], [1, 3, 2, 4])["rho"], 4.5 / (4.5 ** 0.5 * 5 ** 0.5))

    def test_items_keep_profile_experiments_warm_ups_and_plain_warm_ups_apart(self):
        with tempfile.TemporaryDirectory() as folder:
            run = pathlib.Path(folder)
            def ex(speedup, order):
                return {"Speedup": speedup, "Order": order, "Visits": 100, "ElapsedNs": 1_000_000_000, "Threads": [{"CpuNs": 50}, {"CpuNs": 50}]}
            (run / "1-p2-profile.json").write_text(json.dumps({"experiments": [ex(0, 1), ex(75, 0)], "warmup": ex(0, -1)}))
            (run / "1-p2-original.json").write_text(json.dumps({"visits": 200, "elapsedNs": 1_000_000_000}))
            (run / "1-p2-original.json.warmup").write_text(json.dumps({"visits": 100, "elapsedNs": 1_000_000_000}))
            profile = items(run, {"file": "1-p2-profile.json", "block": 1, "peer": 2, "arm": "profile"})
            self.assertEqual(set(profile), {(1, 2, "profile", 0, 1), (1, 2, "profile", 75, 0), (1, 2, "profile", "warmup")})
            self.assertEqual(profile[(1, 2, "profile", 75, 0)]["cpuPerTransactionNs"], 1.0)
            plain = items(run, {"file": "1-p2-original.json", "block": 1, "peer": 2, "arm": "original"})
            self.assertEqual({k: v["rate"] for k, v in plain.items()}, {(1, 2, "original"): 200.0, (1, 2, "original", "warmup"): 100.0})


if __name__ == "__main__":
    unittest.main()
