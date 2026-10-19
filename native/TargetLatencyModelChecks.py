import unittest

from TargetLatencyModel import counts_at, estimators, g_at, gain, latency_or_none, simulate, upstream_at


def signed(run, count):
    pub = run["publishes"]
    return [(e - b) - (g_at(pub, e) - g_at(pub, b)) for b, e in run["tx"][:count]]


def gains(p, c, K, s, T, phase, stop, warm, **kw):
    base = estimators(simulate(p, c, K, "0", T, stop=stop), warm)
    real = estimators(simulate(p, c, K, s, T, stop=stop, real=True), warm)
    run = simulate(p, c, K, s, T, phase, stop=stop, **kw)
    profile = estimators(run, warm)
    return {"real": gain(real["wall"], base["wall"]), "clipped": gain(profile["clipped"], base["wall"]),
            "signed": gain(profile["signed"], base["wall"], zero_allowed=False), "wall": profile["wall"], "accounting": run["accounting"]}


class InputTests(unittest.TestCase):
    def test_malformed_inputs_are_rejected(self):
        cases = [dict(p=0), dict(c=0), dict(K=0), dict(T=0), dict(phase=0), dict(phase=11), dict(s="1"), dict(s="-1/2"),
                 dict(s="1/3"), dict(stop=0), dict(parts=[3, 2], offsets=[0]), dict(parts=[6, -1], offsets=[0, 1]),
                 dict(parts=[3, 2], offsets=[0, -1]), dict(parts=[3, 1], offsets=[0, 1]), dict(wake="never")]
        for case in cases:
            args = {"p": 10, "c": 4, "K": 4, "s": "1/2", "T": 10, "phase": 1, **case}
            with self.subTest(case=case), self.assertRaises(ValueError):
                simulate(**args)
        with self.assertRaises(ValueError):
            simulate(3, 4, 4, "1/2", 10, 1, real=True)
        with self.assertRaises(ValueError):
            simulate(5, 4, 4, "9/10", 10, 1, real=True)


class TraceTests(unittest.TestCase):
    def test_the_baseline_trace_matches_the_hand_derivation(self):
        run = simulate(10, 4, 4, "1/2", 2, 2, stop=200)
        self.assertEqual(run["publishes"][:4], [(2, 1), (4, 2), (6, 3), (8, 4)])
        self.assertEqual(run["tx"][:2], [(0, 17), (10, 27)])
        self.assertEqual(signed(run, 2), [9, 9])
        acc = run["accounting"]
        self.assertEqual((acc["samples"], acc["scheduled"], acc["published"], acc["skipped"], acc["paid"], acc["finalDebt"]), (100, 100, 100, 43, 57, 0))

    def test_split_credit_conserves_credit_and_moves_single_windows(self):
        whole = simulate(10, 4, 4, "1/2", 4, 4, stop=200)
        split = simulate(10, 4, 4, "1/2", 4, 4, parts=[1, 1], offsets=[0, 3], stop=200)
        self.assertEqual((signed(whole, 2), signed(split, 2)), ([10, 8], [11, 8]))
        for run in (whole, split):
            acc = run["accounting"]
            self.assertEqual((acc["scheduled"], acc["published"]), (100, 100))
            self.assertEqual(acc["skipped"] + acc["paid"] + acc["finalDebt"], acc["published"])

    def test_a_freed_space_and_a_waiting_producer_meet_at_one_instant(self):
        run = simulate(2, 4, 1, "0", 10, 1, stop=20)
        self.assertEqual(run["begins"][:6], [0, 2, 4, 6, 10, 14])
        self.assertEqual(run["tx"][:5], [(0, 6), (2, 10), (4, 14), (6, 18), (10, 22)])


class UpstreamTests(unittest.TestCase):
    def test_counters_follow_the_snapshot_rule_at_warm_and_stop(self):
        run = {"begins": [0, 5, 10, 15], "tx": [(0, 7), (5, 10), (10, 20), (15, 25)], "publishes": [(3, 2), (12, 5)]}
        self.assertEqual(counts_at(run, 5), (2, 0))
        self.assertEqual(upstream_at(run, 5, 20), (1, 2, 12))
        self.assertEqual(latency_or_none(1, 2, 12), 6)
        self.assertIsNone(latency_or_none(1, 0, 12))
        self.assertIsNone(latency_or_none(1, 2, 0))

    def test_the_stop_snapshot_matches_the_counters(self):
        run = simulate(10, 4, 4, "1/2", 2, 2, stop=200)
        snapshot = next(x for x in run["snapshots"] if x[0] == 200)
        self.assertEqual(snapshot, (200, 20, 19, 100))
        self.assertEqual(upstream_at(run, 20, 200), (1, 17, 90))

    def test_an_earlier_snapshot_is_not_an_earlier_stop_when_a_begin_shares_the_instant(self):
        run = simulate(10, 4, 4, "1/2", 2, 2, stop=200)
        self.assertEqual(upstream_at(run, 20, 150), (2, 13, 65))
        self.assertEqual(upstream_at(simulate(10, 4, 4, "1/2", 2, 2, stop=150), 20, 150), (1, 12, 65))
        self.assertEqual(upstream_at(run, 20, 155), upstream_at(simulate(10, 4, 4, "1/2", 2, 2, stop=155), 20, 155))

    def test_the_mean_and_the_viewer_aggregate_use_the_same_admissible_snapshots(self):
        run = simulate(10, 4, 4, "1/2", 2, 2, stop=200)
        out = estimators(run, 20, stops=[20, 150, 155])
        self.assertEqual((out["trajectoryObservations"], out["excludedObservations"]), (3, 1))
        samples = [upstream_at(run, 20, 150), upstream_at(run, 20, 155)]
        self.assertAlmostEqual(out["trajectoryMean"], sum(d / (a / u) for d, a, u in samples) / 2)
        total = sum(u for _, _, u in samples)
        self.assertAlmostEqual(out["trajectoryViewerAggregate"], (sum(d * u for d, _, u in samples) / total) / (sum(a for _, a, _ in samples) / total))

    def test_undefined_and_zero_gains(self):
        self.assertIsNone(gain(None, 5))
        self.assertIsNone(gain(5, None))
        self.assertIsNone(gain(5, 0))
        self.assertIsNone(gain(-1, 5))
        self.assertEqual(gain(0, 5), 1)
        self.assertIsNone(gain(0, 5, zero_allowed=False))
        self.assertEqual(gain(2, 8), 0.75)


class CreditTests(unittest.TestCase):
    def test_coarse_credit_has_a_phase_without_payment_and_a_phase_with_payment(self):
        skip = gains(40, 16, 4, "3/4", 160, 20, 2000, 200)
        pay = gains(40, 16, 4, "3/4", 160, 1, 2000, 200)
        self.assertEqual((skip["wall"], skip["accounting"]["paid"]), (56, 0))
        self.assertGreater(pay["wall"], 200)
        self.assertGreater(pay["accounting"]["paid"], 0)
        for result in (skip, pay):
            acc = result["accounting"]
            self.assertEqual(acc["scheduled"], acc["published"])
            self.assertEqual(acc["skipped"] + acc["paid"] + acc["finalDebt"], acc["published"])

    def test_the_coarse_case_has_96_phases_without_payment(self):
        results = [gains(40, 16, 4, "3/4", 160, phase, 2000, 200) for phase in range(1, 161)]
        unpaid = [r for r in results if r["accounting"]["paid"] == 0]
        self.assertEqual(len(unpaid), 96)
        self.assertEqual({r["wall"] for r in unpaid}, {56})
        self.assertEqual(len({round(r["clipped"], 12) for r in results}), 9)

    def test_an_empty_window_gives_no_values(self):
        run = simulate(10, 4, 4, "0", 10, 1, stop=20)
        out = estimators(run, 19)
        self.assertEqual((out["n"], out["wall"], out["clipped"], out["signed"]), (0, None, None, None))
        for warm in (20, -1, 1.5):
            with self.subTest(warm=warm), self.assertRaises(ValueError):
                estimators(run, warm)

    def test_doubling_every_time_value_keeps_every_gain(self):
        for phase in (1, 20):
            once = gains(40, 16, 4, "3/4", 160, phase, 2000, 200)
            twice = gains(80, 32, 4, "3/4", 320, 2 * phase, 4000, 400)
            for key in ("real", "clipped", "signed"):
                self.assertAlmostEqual(once[key], twice[key], places=12)
            self.assertAlmostEqual(2 * once["wall"], twice["wall"], places=9)

    def test_fine_credit_stays_near_the_real_gain_in_the_transition(self):
        results = [gains(40, 16, 4, "3/4", 16, phase, 2000, 200) for phase in range(1, 17)]
        self.assertAlmostEqual(results[0]["real"], -5 / 7, places=12)
        for result in results:
            self.assertTrue(-0.68 < result["clipped"] < -0.65, result["clipped"])


if __name__ == "__main__":
    unittest.main()
