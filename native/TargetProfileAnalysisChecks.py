import unittest

from AnalyzeTargetProfile import (PEER_NAME, SELECTED_NAME, SPEEDUPS, actual_gain, driver_matches, expected_args, expected_fields, handoff_shares, lock_stats_complete,
                                  order_ratios,
                                  positions_balanced,
                                  prediction, profile_complete, threads_complete, timing_consistent, visits_sufficient, warmup_present, work_ratios)


def experiment(order, candidate, speedup, effective=100, passed=True):
    return {"Block": 1, "Order": order, "Candidate": candidate, "Speedup": speedup, "EffectiveNs": effective, "ElapsedNs": effective + 7,
            "GlobalNs": 7, "Passed": passed}


def record(changes=None):
    arms = [(SELECTED_NAME, 0)] + [(name, s) for name in (SELECTED_NAME, PEER_NAME) for s in SPEEDUPS]
    experiments = [experiment(i, c, s) for i, (c, s) in enumerate(arms)]
    for (candidate, speedup), values in (changes or {}).items():
        next(e for e in experiments if e["Candidate"] == candidate and e["Speedup"] == speedup).update(values)
    value = {"experiments": experiments}
    value["points"] = [{"candidate": c, "speedup": s, "prediction": prediction(value, c, s)} for c in (SELECTED_NAME, PEER_NAME) for s in SPEEDUPS]
    return value


class DesignChecks(unittest.TestCase):
    def test_fixed_design_is_complete(self):
        self.assertTrue(profile_complete(record()))

    def test_missing_experiment_is_incomplete(self):
        value = record()
        value["experiments"].pop()
        self.assertFalse(profile_complete(value))

    def test_second_baseline_is_incomplete(self):
        self.assertFalse(profile_complete(record({(PEER_NAME, 75): {"Speedup": 0}})))

    def test_repeated_order_is_incomplete(self):
        self.assertFalse(profile_complete(record({(PEER_NAME, 75): {"Order": 0}})))

    def test_other_block_is_incomplete(self):
        self.assertFalse(profile_complete(record({(PEER_NAME, 75): {"Block": 2}})))

    def test_arguments_name_each_arm(self):
        args = expected_args(20261018, 2)
        self.assertEqual((args["profile"], args["selected-75"], args["peer-25"]),
                         ("causal-profile 20261018 1", "plain 1500 3000", "plain 6000 2250"))


class PredictionChecks(unittest.TestCase):
    def test_prediction_uses_the_block_baseline(self):
        value = record({(SELECTED_NAME, 0): {"EffectiveNs": 200}, (SELECTED_NAME, 25): {"EffectiveNs": 150}})
        self.assertEqual(prediction(value, SELECTED_NAME, 25), 0.25)

    def test_failed_experiment_has_no_prediction(self):
        self.assertIsNone(prediction(record({(PEER_NAME, 50): {"Passed": False}}), PEER_NAME, 50))

    def test_failed_baseline_has_no_prediction(self):
        self.assertIsNone(prediction(record({(SELECTED_NAME, 0): {"Passed": False}}), PEER_NAME, 50))


class ConsistencyChecks(unittest.TestCase):
    def test_written_predictions_match(self):
        self.assertTrue(driver_matches(record({(SELECTED_NAME, 0): {"EffectiveNs": 200}})))

    def test_changed_written_prediction_fails(self):
        value = record()
        value["points"][0]["prediction"] = 0.3
        self.assertFalse(driver_matches(value))

    def test_missing_written_point_fails(self):
        value = record()
        value["points"].pop()
        self.assertFalse(driver_matches(value))

    def test_duplicate_written_point_fails(self):
        value = record()
        value["points"][1] = dict(value["points"][0])
        self.assertFalse(driver_matches(value))

    def test_unknown_written_point_fails(self):
        value = record()
        value["points"][0] = {"candidate": "other", "speedup": 25, "prediction": None}
        self.assertFalse(driver_matches(value))

    def test_effective_must_be_elapsed_minus_global(self):
        value = record()
        self.assertTrue(timing_consistent(value))
        value["experiments"][2]["GlobalNs"] = 8
        self.assertFalse(timing_consistent(value))

    def test_fields_name_mode_seed_and_rounds(self):
        fields = expected_fields(20261018, 1)
        self.assertEqual((fields["profile"]["seed"], fields["peer-75"]["peerRounds"], fields["selected-50"]["mode"]), (20261018, 750, "plain"))


def threads(wall, payment):
    return [{"WallNs": wall, "PaymentNs": payment}, {"WallNs": 100, "PaymentNs": 0}]


class DiagnosticChecks(unittest.TestCase):
    def test_passed_experiments_need_two_threads(self):
        value = record()
        for e in value["experiments"]:
            e["Threads"] = threads(100, 0)
        self.assertTrue(threads_complete(value))
        value["experiments"][3]["Threads"] = threads(100, 0)[:1]
        self.assertFalse(threads_complete(value))

    def test_work_ratio_removes_payment_and_divides_by_visits(self):
        value = record({(SELECTED_NAME, 0): {"Visits": 100}, (PEER_NAME, 50): {"Visits": 80}})
        for e in value["experiments"]:
            e["Threads"] = threads(100, 0)
        next(e for e in value["experiments"] if e["Candidate"] == PEER_NAME and e["Speedup"] == 50)["Threads"] = threads(120, 40)
        self.assertEqual(work_ratios(value, PEER_NAME, 50), [1.0, 1.25])


class WarmupChecks(unittest.TestCase):
    def test_warmup_must_be_present_at_speedup_zero(self):
        value = record()
        self.assertFalse(warmup_present(value))
        value["warmup"] = {"Block": 0, "Order": -1, "Speedup": 0}
        self.assertFalse(warmup_present(value), "A placeholder warm-up must not count.")
        value["warmup"] = {"Block": 0, "Order": -1, "Candidate": SELECTED_NAME, "Speedup": 0, "ElapsedNs": 1, "GlobalNs": 0, "EffectiveNs": 1,
                           "Visits": 1, "Threads": [], "Passed": False, "Error": "debt"}
        self.assertTrue(warmup_present(value), "A failed but complete warm-up must count.")
        value["warmup"]["Speedup"] = 25
        self.assertFalse(warmup_present(value))

    def test_order_ratios_compare_each_experiment_with_the_baseline(self):
        value = record({(SELECTED_NAME, 0): {"Visits": 100}, (PEER_NAME, 50): {"Visits": 100}})
        for e in value["experiments"]:
            e["Threads"] = threads(100, 0)
            e.setdefault("Visits", 100)
        peer = next(e for e in value["experiments"] if e["Candidate"] == PEER_NAME and e["Speedup"] == 50)
        peer["Threads"] = threads(110, 0)
        ratios = order_ratios(value)
        self.assertEqual((ratios[0], ratios[peer["Order"]]), ([1.0, 1.0], [1.1, 1.0]))


class RotationChecks(unittest.TestCase):
    arms = [(SELECTED_NAME, 0)] + [(n, s) for n in (SELECTED_NAME, PEER_NAME) for s in SPEEDUPS]

    def blocks(self, shifts):
        return [{"positions": [[c, s, (i - shift) % 7] for i, (c, s) in enumerate(self.arms)]} for shift in shifts]

    def test_five_rotations_balance_positions(self):
        self.assertTrue(positions_balanced(self.blocks([0, 1, 2, 3, 4])))

    def test_a_repeated_rotation_is_not_balanced(self):
        self.assertFalse(positions_balanced(self.blocks([0, 1, 2, 3, 3])))

    def test_a_missing_block_is_not_balanced(self):
        self.assertFalse(positions_balanced(self.blocks([0, 1, 2, 3]) + [{"positions": None}]))

    def test_profile_arguments_and_fields_carry_seed_and_rotation(self):
        self.assertEqual(expected_args(20261018, 3, "pipeline")["profile"], "pipeline-profile 20261018 2")
        fields = expected_fields(20261018, 3, "latency")["profile"]
        self.assertEqual((fields["seed"], fields["rotation"]), (20261018, 2))

    def test_order_ratios_skip_only_incomplete_experiments(self):
        value = record()
        for e in value["experiments"]:
            e["Threads"] = threads(100, 0)
            e.setdefault("Visits", 1)
        value["experiments"][3]["Threads"] = []
        ratios = order_ratios(value)
        self.assertEqual((len(ratios), value["experiments"][3]["Order"] in ratios), (6, False))


class HandoffChecks(unittest.TestCase):
    def test_handoff_share_divides_by_elapsed_time(self):
        value = record()
        for e in value["experiments"]:
            e["ElapsedNs"] = 1000
        value["lockStats"] = [{"handoffNs": 900}] + [{"handoffNs": 10 * (e["Order"] + 1)} for e in sorted(value["experiments"], key=lambda e: e["Order"])]
        point = next(e for e in value["experiments"] if e["Candidate"] == SELECTED_NAME and e["Speedup"] == 50)
        shares = handoff_shares(value, {"elapsedNs": 1000, "handoffNs": 30}, SELECTED_NAME, 50)
        self.assertEqual(shares, [10 * (point["Order"] + 1) / 1000, 0.03])

    def test_lock_stats_must_cover_warmup_experiments_and_plain_arms(self):
        value = record()
        stat = {"entries": 1, "waitedEntries": 0, "handoffNs": 0}
        value["lockStats"] = [dict(stat) for _ in range(8)]
        arms = {"profile": value, "original": dict(stat), "selected-25": dict(stat)}
        self.assertTrue(lock_stats_complete(value, arms))
        value["lockStats"].pop()
        self.assertFalse(lock_stats_complete(value, arms), "One entry per experiment and the warm-up is required.")
        value["lockStats"].append(dict(stat))
        del arms["original"]["handoffNs"]
        self.assertFalse(lock_stats_complete(value, arms), "Each plain arm must hold its handoff counts.")

    def test_a_treatment_stat_without_handoff_fails_the_check_that_guards_the_share(self):
        value = record()
        value["lockStats"] = [{"entries": 1, "waitedEntries": 0, "handoffNs": 0} for _ in range(8)]
        del value["lockStats"][3]["handoffNs"]
        self.assertFalse(lock_stats_complete(value, {"profile": value}))
        value["lockStats"][3]["handoffNs"] = "x"
        self.assertFalse(lock_stats_complete(value, {"profile": value}))

    def test_missing_lock_stats_give_no_share(self):
        self.assertIsNone(handoff_shares(record(), {"elapsedNs": 1, "handoffNs": 1}, SELECTED_NAME, 50))


class ThroughputChecks(unittest.TestCase):
    def test_arguments_name_each_throughput_arm(self):
        args = expected_args(20261018, 1, "throughput")
        self.assertEqual((args["profile"], args["original"], args["selected-25"], args["peer-75"]),
                         ("throughput-profile 20261018 0", "plain-throughput 40 20", "plain-throughput 30 20", "plain-throughput 40 5"))

    def test_throughput_fields_hold_the_duration(self):
        fields = expected_fields(20261018, 1, "throughput")
        self.assertEqual((fields["profile"]["durationMs"], fields["peer-50"]["peerRounds"], fields["original"]["mode"]), (120, 10, "plain-throughput"))

    def test_throughput_actual_gain_compares_time_for_each_visit(self):
        original = {"elapsedNs": 1000, "visits": 100}
        self.assertEqual(actual_gain(original, {"elapsedNs": 1000, "visits": 200}, "throughput"), 0.5)
        self.assertIsNone(actual_gain(original, {"elapsedNs": 1000, "visits": 0}, "throughput"))

    def test_latency_actual_gain_compares_elapsed_time(self):
        self.assertEqual(actual_gain({"elapsedNs": 1000}, {"elapsedNs": 750}, "latency"), 0.25)

    def test_prediction_divides_by_visits(self):
        value = record({(SELECTED_NAME, 0): {"Visits": 100}, (SELECTED_NAME, 50): {"Visits": 200}})
        self.assertEqual(prediction(value, SELECTED_NAME, 50), 0.5)

    def test_prediction_needs_visits(self):
        self.assertIsNone(prediction(record({(PEER_NAME, 25): {"Visits": 0}}), PEER_NAME, 25))

    def test_throughput_records_need_100_visits(self):
        self.assertFalse(visits_sufficient({"visits": 99}, "throughput"))
        self.assertTrue(visits_sufficient({"visits": 100}, "throughput"))
        self.assertFalse(visits_sufficient(record({(PEER_NAME, 25): {"Visits": 99}}), "throughput"))
        self.assertTrue(visits_sufficient({"visits": 0}, "latency"))

    def test_pipeline_arms_use_the_pipeline_modes(self):
        args = expected_args(20261018, 1, "pipeline")
        fields = expected_fields(20261018, 1, "pipeline")
        self.assertEqual((args["profile"], args["selected-75"], fields["peer-25"]["mode"], fields["original"]["durationMs"]),
                         ("pipeline-profile 20261018 0", "plain-pipeline 10 20", "plain-pipeline", 120))
        self.assertEqual(actual_gain({"elapsedNs": 1000, "visits": 100}, {"elapsedNs": 1000, "visits": 200}, "pipeline"), 0.5)
        self.assertFalse(visits_sufficient({"visits": 99}, "pipeline"))

    def test_lock_arms_use_the_lock_modes(self):
        args = expected_args(20261018, 2, "lock")
        self.assertEqual((args["profile"], args["selected-50"], args["peer-75"]), ("lock-profile 20261018 1", "plain-lock 20 20", "plain-lock 40 5"))
        self.assertEqual(expected_fields(20261018, 1, "lock")["original"]["mode"], "plain-lock")

    def test_spin_lock_arms_use_the_spin_lock_modes(self):
        args = expected_args(20261018, 1, "spinlock")
        self.assertEqual((args["profile"], args["selected-25"]), ("spin-lock-profile 20261018 0", "plain-spin-lock 30 20"))

    def test_semaphore_arms_use_the_semaphore_rounds(self):
        args = expected_args(20261018, 1, "semaphore")
        self.assertEqual((args["profile"], args["selected-25"], args["peer-75"]), ("semaphore-profile 20261018 0", "plain-semaphore 18 12", "plain-semaphore 24 3"))

    def test_latency_experiments_need_one_visit(self):
        self.assertTrue(visits_sufficient(record({(PEER_NAME, 25): {"Visits": 1}}), "latency"))
        self.assertFalse(visits_sufficient(record({(PEER_NAME, 25): {"Visits": 2}}), "latency"))


if __name__ == "__main__":
    unittest.main()
