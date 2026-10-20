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
                 dict(parts=[3, 2], offsets=[0, -1]), dict(parts=[3, 1], offsets=[0, 1]), dict(wake="never"), dict(publication="timer"),
                 dict(publication="release", parts=[5], offsets=[0])]
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
        self.assertEqual((acc["samples"], acc["generated"], acc["published"], acc["skipped"], acc["paid"], acc["finalDebt"]), (100, 100, 100, 43, 57, 0))

    def test_split_credit_conserves_credit_and_moves_single_windows(self):
        whole = simulate(10, 4, 4, "1/2", 4, 4, stop=200)
        split = simulate(10, 4, 4, "1/2", 4, 4, parts=[1, 1], offsets=[0, 3], stop=200)
        self.assertEqual((signed(whole, 2), signed(split, 2)), ([10, 8], [11, 8]))
        for run in (whole, split):
            acc = run["accounting"]
            self.assertEqual((acc["generated"], acc["published"]), (100, 100))
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
        self.assertEqual(snapshot, (200, 20, 19, 100, 0))
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


def outcome(run):
    acc = run["accounting"]
    return run["tx"], run["publishes"], (acc["generated"], acc["published"], acc["skipped"], acc["paid"], acc["finalDebt"], acc["unpublished"])


class PublicationTests(unittest.TestCase):
    """p 10, s 1/2, T 10, phase 3: the producer samples at CPU 3, 13, and 23, and each sample generates 5."""

    def run_both(self, c, K, stop):
        return [simulate(10, c, K, "1/2", 10, 3, stop=stop, publication=policy) for policy in ("immediate", "release")]

    def test_a_consumer_that_pays_before_it_waits_skips_nothing_only_with_immediate_publication(self):
        immediate, release = self.run_both(4, 4, 15)
        self.assertEqual(outcome(immediate), ([(0, 19), (10, 24)], [(3, 5), (13, 10)], (10, 10, 5, 5, 0, 0)))
        self.assertEqual(outcome(release), ([(0, 14), (10, 24)], [(10, 5), (20, 10)], (10, 10, 10, 0, 0, 0)))

    def test_an_enqueue_while_the_consumer_pays_leaves_an_item_that_it_takes_without_a_skip(self):
        immediate, release = self.run_both(6, 4, 15)
        self.assertEqual(outcome(immediate), ([(0, 21), (10, 27)], [(3, 5), (13, 10)], (10, 10, 5, 5, 0, 0)))
        self.assertEqual(outcome(release), ([(0, 16), (10, 26)], [(10, 5), (20, 10)], (10, 10, 10, 0, 0, 0)))

    def test_an_item_already_queued_is_taken_without_a_skip_by_both_policies(self):
        immediate, release = self.run_both(12, 4, 15)
        self.assertEqual(outcome(immediate), ([(0, 27), (10, 39)], [(3, 5), (13, 10)], (10, 10, 5, 5, 0, 0)))
        self.assertEqual(outcome(release), ([(0, 27), (10, 39)], [(10, 5), (20, 10)], (10, 10, 5, 5, 0, 0)))

    def test_a_producer_held_by_a_full_queue_publishes_when_it_enqueues(self):
        immediate, release = self.run_both(25, 1, 25)
        self.assertEqual(outcome(immediate), ([(0, 45), (10, 70), (20, 95)], [(3, 5), (13, 10), (23, 15)], (15, 15, 5, 10, 0, 0)))
        self.assertEqual(outcome(release), ([(0, 40), (10, 70), (20, 95)], [(10, 5), (20, 10), (40, 15)], (15, 15, 5, 10, 0, 0)))
        unpublished = {x[0]: x[4] for x in release["snapshots"]}
        self.assertEqual((unpublished[22], unpublished[23], unpublished[39], unpublished[40]), (0, 5, 5, 0))

    def test_the_stop_snapshot_holds_unpublished_credit_and_the_drain_publishes_it(self):
        immediate, release = self.run_both(4, 4, 25)
        self.assertEqual(outcome(immediate), ([(0, 19), (10, 29), (20, 34)], [(3, 5), (13, 10), (23, 15)], (15, 15, 5, 10, 0, 0)))
        self.assertEqual(outcome(release), ([(0, 14), (10, 24), (20, 34)], [(10, 5), (20, 10), (30, 15)], (15, 15, 15, 0, 0, 0)))
        stop = {run_name: next(x for x in run["snapshots"] if x[0] == 25) for run_name, run in (("immediate", immediate), ("release", release))}
        self.assertEqual(stop, {"immediate": (25, 3, 1, 15, 0), "release": (25, 3, 2, 10, 5)})

    def test_both_policies_generate_the_same_credit_for_the_same_producer_work(self):
        for c, K, stop in ((4, 4, 15), (6, 4, 15), (12, 4, 15), (25, 1, 25), (4, 4, 25)):
            immediate, release = self.run_both(c, K, stop)
            with self.subTest(c=c, K=K, stop=stop):
                self.assertEqual(immediate["begins"], release["begins"])
                for key in ("producerWork", "samples", "generated"):
                    self.assertEqual(immediate["accounting"][key], release["accounting"][key], key)


class CreditTests(unittest.TestCase):
    def test_coarse_credit_has_a_phase_without_payment_and_a_phase_with_payment(self):
        skip = gains(40, 16, 4, "3/4", 160, 20, 2000, 200)
        pay = gains(40, 16, 4, "3/4", 160, 1, 2000, 200)
        self.assertEqual((skip["wall"], skip["accounting"]["paid"]), (56, 0))
        self.assertGreater(pay["wall"], 200)
        self.assertGreater(pay["accounting"]["paid"], 0)
        for result in (skip, pay):
            acc = result["accounting"]
            self.assertEqual(acc["generated"], acc["published"])
            self.assertEqual(acc["skipped"] + acc["paid"] + acc["finalDebt"], acc["published"])

    def test_the_coarse_case_has_96_phases_without_payment(self):
        results = [gains(40, 16, 4, "3/4", 160, phase, 2000, 200) for phase in range(1, 161)]
        unpaid = [r for r in results if r["accounting"]["paid"] == 0]
        self.assertEqual(len(unpaid), 96)
        self.assertEqual({r["wall"] for r in unpaid}, {56})
        self.assertEqual(len({round(r["clipped"], 12) for r in results}), 9)
        paid = [r["clipped"] for r in results if r["accounting"]["paid"] > 0]
        self.assertEqual({round(r["clipped"], 12) for r in unpaid}, {0.25})
        self.assertAlmostEqual(min(paid), -23 / 21, places=12)
        self.assertAlmostEqual(max(paid), -17 / 21, places=12)

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

    def test_release_publication_makes_no_payment_in_any_coarse_or_fine_phase(self):
        for T, clipped in ((160, 0.25), (16, 15 / 28)):
            results = [gains(40, 16, 4, "3/4", T, phase, 2000, 200, publication="release") for phase in range(1, T + 1)]
            with self.subTest(T=T):
                self.assertEqual({r["accounting"]["paid"] for r in results}, {0})
                self.assertEqual({r["wall"] for r in results}, {56})
                for result in results:
                    acc = result["accounting"]
                    self.assertEqual((acc["generated"], acc["unpublished"]), (acc["published"], 0))
                    self.assertEqual(acc["skipped"] + acc["finalDebt"], acc["published"])
                    self.assertAlmostEqual(result["clipped"], clipped, places=12)
                    self.assertAlmostEqual(result["real"], -5 / 7, places=12)

    def test_fine_credit_stays_near_the_real_gain_in_the_transition(self):
        results = [gains(40, 16, 4, "3/4", 16, phase, 2000, 200) for phase in range(1, 17)]
        self.assertAlmostEqual(results[0]["real"], -5 / 7, places=12)
        for result in results:
            self.assertTrue(-0.68 < result["clipped"] < -0.65, result["clipped"])


if __name__ == "__main__":
    unittest.main()
