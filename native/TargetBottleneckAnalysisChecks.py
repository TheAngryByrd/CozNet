import json
import pathlib
import tempfile
import unittest

from AnalyzeTargetBottleneck import SEED, block_reasons, block_values, diagnostics_complete, paired_outcome, plain_matches, expected_args, outcome, profile_complete
from AnalyzeTargetProfile import SELECTED_NAME


def experiment(speedup, order):
    return {"Candidate": SELECTED_NAME, "Speedup": speedup, "Order": order, "Block": 1, "Passed": True}


def profile(peer=2, block=1, **changes):
    value = {"mode": "transaction-selected-profile", "peerRounds": peer, "selectedRounds": 40, "seed": SEED, "rotation": (block - 1) % 4, "durationMs": 120, "periodNs": 1000000,
             "experiments": [experiment(s, i) for i, s in enumerate((0, 25, 50, 75))], "passed": True}
    value.update(changes)
    return value


class BottleneckAnalysisTests(unittest.TestCase):
    def test_the_profile_must_hold_the_four_selected_experiments(self):
        self.assertTrue(profile_complete(profile(), 2, 1))
        self.assertFalse(profile_complete(profile(experiments=profile()["experiments"][:3]), 2, 1))
        self.assertFalse(profile_complete(profile(peerRounds=8), 2, 1))
        wrong = profile()
        wrong["experiments"][2] = {**wrong["experiments"][2], "Candidate": "CozNet.TargetLocal.Work::peer(uint64)"}
        self.assertFalse(profile_complete(wrong, 2, 1))
        self.assertTrue(profile_complete(profile(block=5), 2, 5))
        self.assertFalse(profile_complete(profile(rotation=4), 2, 5))
        self.assertFalse(profile_complete(profile(durationMs=60), 2, 1))
        self.assertFalse(profile_complete(profile(periodNs=250000), 2, 1))

    def test_the_arguments_pair_the_selected_rounds_with_each_peer(self):
        self.assertEqual(expected_args(SEED, 2, 2), {"profile": f"transaction-selected-profile 2 {SEED} 1", "original": "plain-transaction 40 2",
                                                     "selected-25": "plain-transaction 30 2", "selected-50": "plain-transaction 20 2",
                                                     "selected-75": "plain-transaction 10 2"})

    def test_a_block_is_invalid_when_an_arm_is_missing_fails_or_has_no_warm_up(self):
        with tempfile.TemporaryDirectory() as folder:
            run = pathlib.Path(folder)
            args = expected_args(SEED, 1, 2)
            records = [{"block": 1, "peer": 2, "arm": arm, "args": a, "exitCode": 0} for arm, a in args.items()]
            for arm in args:
                if arm != "profile":
                    (run / f"1-p2-{arm}.json").write_text(json.dumps({"passed": True}))
                    (run / f"1-p2-{arm}.json.warmup").write_text(json.dumps({"passed": True}))
            reasons, _ = block_reasons(run, 1, 2, records)
            self.assertIn("profile missing", reasons)
            self.assertNotIn("a plain arm has no passed warm-up record", reasons)
            self.assertIn("original does not hold its mode, rounds, and duration", reasons)
            (run / "1-p2-selected-75.json.warmup").write_text(json.dumps({"passed": False}))
            (run / "1-p2-original.json").write_text(json.dumps({"passed": False}))
            reasons, _ = block_reasons(run, 1, 2, records[:-1] + [{**records[-1], "exitCode": 1}])
            self.assertIn("a plain arm has no passed warm-up record", reasons)
            self.assertIn("original not passed", reasons)
            self.assertIn("selected-75 exit 1", reasons)

    def test_each_plain_field_is_checked(self):
        good = {"mode": "plain-transaction", "selectedRounds": 10, "peerRounds": 2, "durationMs": 120}
        self.assertTrue(plain_matches(good, 10, 2))
        for key, wrong in (("mode", "plain"), ("selectedRounds", 40), ("peerRounds", 8), ("durationMs", 60)):
            self.assertFalse(plain_matches({**good, key: wrong}, 10, 2), key)

    def test_the_paired_outcome_uses_only_blocks_valid_for_both_peers(self):
        def blocks(valid, s75, ready):
            return [{"block": i + 1, "valid": v,
                     "values": {s: {"difference": s75 if s == 75 else 0.0, "upstreamDifference": 0.0, "ready": ready} for s in (25, 50, 75)} if v else None}
                    for i, v in enumerate(valid)]
        common, paired, result = paired_outcome(blocks([True, True, True, True, False], -11.0, 0.05), blocks([False, True, True, True, True], -2.0, 0.05))
        self.assertEqual(common, [2, 3, 4])
        self.assertEqual(result, "not decisive: too few valid blocks")
        common, paired, result = paired_outcome(blocks([True] * 5, -11.0, 0.05), blocks([True, True, True, True, False], -2.0, 0.05))
        self.assertEqual((common, result, paired[2][75]["difference"]), ([1, 2, 3, 4], "congestion supported", -2.0))

    def test_block_values_use_the_virtual_latency_rule(self):
        idle = {"ready": [1, 0, 0, 0, 0], "below100us": [0] * 5, "below400us": [0] * 5, "atLeast400us": [0, 0, 0, 3, 0]}
        def ex(speedup, order, virtual, effective):
            return {"Candidate": SELECTED_NAME, "Speedup": speedup, "Order": order, "Passed": True, "Visits": 100, "LatencyNs": 100_000,
                    "VirtualLatencyNs": virtual, "EffectiveNs": effective, "ElapsedNs": 100_000, "GlobalNs": 100_000 - effective, "NegativeTransactions": 0}
        experiments = [ex(75, 0, 40_000, 50_000), ex(0, 2, 100_000, 100_000), ex(50, 3, 60_000, 70_000), ex(25, 1, 80_000, 90_000)]
        other = {"ready": [0] * 5, "below100us": [4, 0, 0, 0, 0], "below400us": [0] * 5, "atLeast400us": [0] * 5}
        stats = [{"handoffIdle": other}, {"handoffIdle": idle}, {"handoffIdle": other}, {"handoffIdle": other}, {"handoffIdle": other}]
        data = {"profile": {"mode": "transaction-selected-profile", "experiments": experiments, "queueStats": stats},
                "original": {"latencyNs": 100_000, "visits": 100, "elapsedNs": 1}}
        for s, latency in ((25, 75_000), (50, 55_000), (75, 30_000)):
            data[f"selected-{s}"] = {"latencyNs": latency, "visits": 100, "elapsedNs": 1, "handoffIdle": idle}
        values = block_values(data)
        self.assertAlmostEqual(values[75]["difference"], 100 * ((1 - 0.4) - (1 - 0.3)))
        self.assertAlmostEqual(values[25]["difference"], 100 * ((1 - 0.8) - (1 - 0.75)))
        self.assertAlmostEqual(values[75]["upstreamDifference"], 100 * ((1 - 0.5) - (1 - 0.3)))
        self.assertAlmostEqual(values[75]["ready"], 0.25)
        self.assertAlmostEqual(values[75]["windowRate"], 0.6)

    def test_a_block_without_idle_counts_is_incomplete(self):
        idle = {"ready": [1, 0, 0, 0, 0]}
        profile = {"queueStats": [{}, {"handoffIdle": idle}, {"handoffIdle": idle}]}
        self.assertTrue(diagnostics_complete(profile, {"profile": profile, "original": {"handoffIdle": idle}}))
        self.assertFalse(diagnostics_complete(profile, {"profile": profile, "original": {}}))
        self.assertFalse(diagnostics_complete({"queueStats": [{}, {"handoffIdle": idle}, {}]}, {"original": {"handoffIdle": idle}}))

    def test_the_outcome_follows_the_predeclared_limits(self):
        self.assertEqual(outcome(-11.0, -2.0, 0.05, 5, 5), "congestion supported")
        self.assertEqual(outcome(-11.0, -10.0, 0.05, 5, 5), "estimator or boundary bias supported")
        self.assertEqual(outcome(-11.0, -6.5, 0.05, 5, 5), "not decisive")
        self.assertEqual(outcome(-11.0, -2.0, 0.15, 5, 5), "not decisive: the ready share of peer 2 stays above 10%")
        self.assertEqual(outcome(-7.0, -2.0, 0.05, 5, 5), "not decisive: the earlier error did not repeat")
        self.assertEqual(outcome(-11.0, -2.0, 0.05, 3, 5), "not decisive: too few valid blocks")


if __name__ == "__main__":
    unittest.main()
