import json
import pathlib
import tempfile
import unittest

from AnalyzeTargetBottleneck import SEED, block_reasons, block_values, boundary_of, summarize_boundaries, upstream_gain, upstream_latency, viewer_aggregate, diagnostics_complete, evidence_reasons, evidence_values, paired_outcome, plain_matches, expected_args, outcome, profile_complete
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

    def evidence_case(self, signed75=50_000, clipped75=10_000):
        def ex(speedup, order, visits, virtual, negative):
            return {"Block": 1, "Order": order, "Speedup": speedup, "Visits": visits, "VirtualLatencyNs": virtual, "NegativeTransactions": negative}
        warmup = {"Block": 0, "Order": -1, "Speedup": 0, "Visits": 2, "VirtualLatencyNs": 2_000, "NegativeTransactions": 0}
        experiments = [ex(0, 0, 2, 2_000, 0), ex(25, 1, 2, 1_500, 0), ex(50, 2, 2, 1_000, 0), ex(75, 3, 2, signed75 + clipped75, 1)]
        profile = {"warmup": warmup, "experiments": experiments}
        def ev(e, signed, clipped):
            first = signed + clipped
            windows = ([[0, first, 0, 0, first, 0], [first, first + 1, 0, clipped + 1, -clipped, 1]] if clipped
                       else [[0, signed, 0, 0, signed, 0], [signed, signed, 0, 0, 0, 0]])
            return {"block": e["Block"], "order": e["Order"], "transactions": e["Visits"], "signedTotalNs": signed, "clippedNs": clipped,
                    "clippedTotalNs": signed + clipped, "negativeTransactions": e["NegativeTransactions"], "stepHistogram": [1, 1, 0, 0] if clipped else [2, 0, 0, 0],
                    "exported": 2, "omitted": 0, "exportLimit": 4096, "windows": windows}
        evidence = {"experiments": [ev(warmup, 2_000, 0)] + [ev(e, e["VirtualLatencyNs"], 0) for e in experiments[:3]] + [ev(experiments[3], signed75, clipped75)]}
        return profile, evidence

    def test_evidence_must_match_the_profile_and_its_own_identities(self):
        profile, evidence = self.evidence_case()
        self.assertEqual(evidence_reasons(profile, evidence), [])
        evidence["experiments"][4]["clippedTotalNs"] += 1
        self.assertIn("order 3 evidence differs from the profile record", evidence_reasons(profile, evidence))
        self.assertIn("order 3 clipped total differs from signed total plus clipped amount", evidence_reasons(profile, evidence))
        profile, evidence = self.evidence_case()
        evidence["experiments"][1] = {**evidence["experiments"][1], "clippedNs": 5, "signedTotalNs": 1_995}
        self.assertIn("order 0 baseline clips delay", evidence_reasons(profile, evidence))
        profile, evidence = self.evidence_case()
        evidence["experiments"][4]["windows"][0][3] = 7
        self.assertEqual(evidence_reasons(profile, evidence), ["order 3 a window does not hold its own arithmetic"])
        profile, evidence = self.evidence_case()
        evidence["experiments"][4]["windows"][1] = [55_000, 55_001, 0, 5_001, -5_000, 1]
        evidence["experiments"][4]["windows"][0] = [0, 55_000, 0, 0, 55_000, 0]
        self.assertEqual(evidence_reasons(profile, evidence), ["order 3 windows do not add up to the totals"])
        profile, evidence = self.evidence_case()
        evidence["experiments"][4]["windows"][1][5] = 3
        self.assertEqual(evidence_reasons(profile, evidence), ["order 3 windows do not add up to the totals"])
        profile, evidence = self.evidence_case()
        evidence["experiments"][4]["exported"] = 1
        evidence["experiments"][4]["omitted"] = 1
        self.assertEqual(evidence_reasons(profile, evidence), ["order 3 export does not match its count"])
        profile, evidence = self.evidence_case()
        evidence["experiments"][4].update(windows=[], exported=0, omitted=2)
        self.assertEqual(evidence_reasons(profile, evidence), ["order 3 export does not match its count"])
        profile, evidence = self.evidence_case()
        evidence["experiments"][1]["windows"][1] = [5, 5, 0, 0, 0, 1]
        self.assertIn("order 0 a window does not hold its own arithmetic", evidence_reasons(profile, evidence))
        profile, evidence = self.evidence_case()
        self.assertEqual(evidence_reasons(profile, {"experiments": evidence["experiments"] + [None]}), ["evidence has an entry without an integer block and order"])
        self.assertEqual(evidence_reasons(profile, {"experiments": evidence["experiments"] + [{}]}), ["evidence has an entry without an integer block and order"])
        self.assertEqual(evidence_reasons(profile, {"experiments": evidence["experiments"] + [dict(evidence["experiments"][4])]}),
                         ["evidence does not hold exactly one entry for each experiment"])
        profile, evidence = self.evidence_case()
        evidence["experiments"][4]["stepHistogram"] = [2]
        self.assertEqual(evidence_reasons(profile, evidence), ["order 3 evidence has the wrong shape"])
        profile, evidence = self.evidence_case()
        evidence["experiments"][2]["omitted"] = 1
        self.assertIn("order 1 counts do not add up", evidence_reasons(profile, evidence))
        profile, evidence = self.evidence_case()
        self.assertEqual(evidence_reasons(profile, {"experiments": evidence["experiments"][:-1]}), ["evidence does not hold exactly one entry for each experiment"])
        self.assertEqual(evidence_reasons(profile, None), ["no evidence record"])

    def test_a_nonpositive_signed_total_gives_no_unclipped_difference(self):
        data = {"original": {"latencyNs": 2_000, "visits": 2, "elapsedNs": 1}, **{f"selected-{s}": {"latencyNs": 1_000, "visits": 2, "elapsedNs": 1} for s in (25, 50, 75)}}
        profile, evidence = self.evidence_case()
        values = evidence_values(profile, data, evidence, True)
        self.assertIsNone(evidence_values(profile, data, evidence, False)[75]["unclippedDifference"])
        self.assertAlmostEqual(values[75]["unclippedDifference"], 100 * ((1 - 25_000 / 1_000) - 0.5))
        self.assertEqual((values[75]["clippedPerTransactionNs"], values[75]["estimatorAssumptionsValid"], values[25]["estimatorAssumptionsValid"]), (5_000, False, True))
        for signed in (0, -4_000):
            profile, evidence = self.evidence_case(signed75=signed, clipped75=12_000)
            self.assertEqual(evidence_reasons(profile, evidence), [])
            self.assertIsNone(evidence_values(profile, data, evidence, True)[75]["unclippedDifference"])

    def test_boundary_observations_are_checked_before_use(self):
        good = {"arrivals": 5, "departures": 2, "difference": 4, "effectiveDurationNs": 830, "startArrivals": 1, "startVisits": 0, "stopArrivals": 6, "stopVisits": 2,
                "startNs": 100, "stopReadStartNs": 1000, "stopReadEndNs": 1010, "stopCalledNs": 1020, "globalAtStartNs": 50, "globalAtStopReadStartNs": 120, "reasons": []}
        self.assertEqual(boundary_of({"boundary": good}), (good, None))
        self.assertEqual(boundary_of({"boundary": None}), (None, "no boundary observation"))
        self.assertEqual(boundary_of({}), (None, "no boundary observation"))
        self.assertEqual(boundary_of({"boundary": {**good, "arrivals": None}})[1], "the boundary observation has the wrong shape")
        self.assertEqual(boundary_of({"boundary": {**good, "stopReadEndNs": 999}})[1], "the boundary reads are out of order")
        self.assertEqual(boundary_of({"boundary": {**good, "reasons": ["no arrivals between the boundary observations"]}})[1], "no arrivals between the boundary observations")
        self.assertEqual(boundary_of({"boundary": {**good, "reasons": [1]}})[1], "the boundary observation has the wrong shape")
        self.assertEqual(boundary_of({"boundary": {**good, "reasons": [""]}})[1], "the boundary observation has the wrong shape")
        self.assertEqual(boundary_of({"boundary": {**good, "reasons": ["  "]}})[1], "the boundary observation has the wrong shape")
        idle = {**good, "stopArrivals": 1, "arrivals": 0, "difference": -1}
        self.assertEqual(boundary_of({"boundary": idle})[1], "no arrivals between the boundary observations; the stop counts give a negative occupancy")
        slow = {**good, "globalAtStopReadStartNs": 1000, "effectiveDurationNs": (1000 - 100) - (1000 - 50)}
        self.assertEqual(boundary_of({"boundary": slow})[1], "the effective duration is not positive")
        for key in ("arrivals", "departures", "difference", "effectiveDurationNs"):
            self.assertEqual(boundary_of({"boundary": {**good, key: good[key] + 1}})[1], "the derived boundary values differ from the reads", key)

    def test_the_boundary_summary_pairs_each_treatment_with_its_own_baseline_blocks(self):
        per_block = {1: {0: (2, 10, 100), 75: (1, 10, 100)}, 2: {0: (4, 10, 100), 75: (2, 10, 100)}, 3: {0: (8, 10, 100)}, 4: {75: (1, 10, 100)}, 5: {}}
        out = summarize_boundaries(per_block)
        self.assertEqual(out["75"]["blocks"], [1, 2])
        self.assertEqual(out["75"]["perBlockGain"], {"1": 0.5, "2": 0.5})
        self.assertAlmostEqual(out["75"]["viewerGain"], 0.5)
        self.assertEqual((out["25"]["blocks"], out["25"]["viewerGain"], out["25"]["undefinedPerBlock"]), ([], None, 0))
        zero = summarize_boundaries({1: {0: (0, 10, 100), 50: (1, 10, 100)}})
        self.assertEqual((zero["50"]["perBlockGain"], zero["50"]["undefinedPerBlock"], zero["50"]["viewerGain"]), ({"1": None}, 1, None))

    def test_the_upstream_values_are_undefined_without_arrivals_or_duration(self):
        self.assertAlmostEqual(upstream_latency(4, 5, 830), 664)
        self.assertEqual(upstream_latency(0, 5, 830), 0)
        self.assertIsNone(upstream_latency(4, 0, 830))
        self.assertIsNone(upstream_latency(4, 5, 0))
        self.assertIsNone(upstream_latency(-1, 5, 830))
        self.assertEqual(upstream_gain(0, 10), 1)
        self.assertIsNone(upstream_gain(1, 0))
        self.assertIsNone(upstream_gain(None, 10))

    def test_the_viewer_aggregate_weights_the_difference_by_duration(self):
        items = [(2, 10, 100), (4, 30, 300)]
        self.assertAlmostEqual(viewer_aggregate(items), ((2 * 100 + 4 * 300) / 400) / (40 / 400))
        self.assertAlmostEqual(viewer_aggregate(items + [(9, 0, 50), (9, 3, 0)]), viewer_aggregate(items))
        self.assertIsNone(viewer_aggregate([(1, 0, 10)]))
        self.assertIsNone(viewer_aggregate([]))

    def test_the_outcome_follows_the_predeclared_limits(self):
        self.assertEqual(outcome(-11.0, -2.0, 0.05, 5, 5), "congestion supported")
        self.assertEqual(outcome(-11.0, -10.0, 0.05, 5, 5), "estimator or boundary bias supported")
        self.assertEqual(outcome(-11.0, -6.5, 0.05, 5, 5), "not decisive")
        self.assertEqual(outcome(-11.0, -2.0, 0.15, 5, 5), "not decisive: the ready share of peer 2 stays above 10%")
        self.assertEqual(outcome(-7.0, -2.0, 0.05, 5, 5), "not decisive: the earlier error did not repeat")
        self.assertEqual(outcome(-11.0, -2.0, 0.05, 3, 5), "not decisive: too few valid blocks")


if __name__ == "__main__":
    unittest.main()
