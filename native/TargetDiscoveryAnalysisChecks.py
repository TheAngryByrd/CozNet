import copy
import hashlib
import unittest

from AnalyzeTargetDiscovery import LEGACY_COMMIT, WORK as WORK_PATH, analyze, body_lines, source_hash

WORK = "module W\n\nopen X\n\n[<A>]\nlet selected (rounds: uint64) =\n    let mutable a = 0UL\n    a\n\n[<A>]\n\nlet peer (rounds: uint64) =\n    let mutable a = 1UL\n    a\n"
SELECTED = "CozNet.TargetLocal.Work::selected(uint64)"
PEER = "CozNet.TargetLocal.Work::peer(uint64)"
MAP = b"0x1000 10 uint64 [CozNet.TargetLocal] CozNet.TargetLocal.Work::selected(uint64)[Optimized]"


def passing():
    manifest = {"inputsUnchanged": True, "linuxTests": {"exitCode": 0, "executed": 27, "failed": 0}, "treeClean": False, "commit": "abc",
                "sources": {WORK_PATH: source_hash(WORK)}}
    ranked = [{"symbol": SELECTED, "start": 4096, "end": 4112, "samples": 900, "eligibility": "InEligibleBinding"},
              {"symbol": PEER, "start": 8192, "end": 8208, "samples": 450, "eligibility": "InEligibleBinding"},
              {"symbol": "CozNet.TargetLocal.Program::other()", "start": 12288, "end": 12304, "samples": 5, "eligibility": "OutsideEligibleBinding"}]
    record = {"schema": "coznet.discovery-check/2", "mapHashKind": "FileBytesSha256",
              "supported": True, "configurationErrors": [], "passed": True, "pid": 7, "mapPath": "/tmp/perf-7.map",
              "threads": [{"accepted": 950}, {"accepted": 450}], "eligible": [SELECTED, PEER],
              "mapSha256": hashlib.sha256(MAP).hexdigest().upper(),
              "discovery": {"sampleEvents": 1400, "rankError": "", "bindingError": "", "designValid": True,
                            "counts": {"samples": 1400, "unmapped": 30, "notMethodLabel": 1, "notOptimized": 0, "notUnique": 2, "outOfScope": 10,
                                       "inScope": 1357, "omittedMethods": 1, "omittedSamples": 2},
                            "ranked": ranked, "boundRanges": [[m["start"], m["end"]] for m in ranked],
                            "outsideBinding": ["CozNet.TargetLocal.Program::other()"], "designEligible": [SELECTED, PEER], "designCandidates": [SELECTED, PEER],
                            "anchors": [{"symbol": SELECTED, "found": True, "document": "C:\\repo\\samples\\CozNet.TargetLocal\\Work.fs", "line": 7},
                                        {"symbol": PEER, "found": True, "document": "/repo/samples/CozNet.TargetLocal/Work.fs", "line": 13},
                                        {"symbol": "CozNet.TargetLocal.Program::other()", "found": False, "document": "", "line": 0}]}}
    return manifest, record


def run(manifest, record, record_exit=0, map_bytes=MAP, requested=None):
    return analyze(manifest, record, record_exit, map_bytes, WORK, requested)


def legacy():
    """The shape of the retained unversioned record of commit a0f650f."""
    manifest, record = passing()
    manifest["commit"] = LEGACY_COMMIT
    for key in ("schema", "mapHashKind"):
        record.pop(key)
    discovery = record["discovery"]
    for key in ("outsideBinding", "designEligible", "designCandidates"):
        discovery.pop(key)
    for method in discovery["ranked"]:
        method.pop("eligibility")
    return manifest, record


class DiscoveryAnalysisTests(unittest.TestCase):
    def test_the_body_line_follows_each_fixture_header(self):
        self.assertEqual(body_lines(WORK), {SELECTED: 7, PEER: 13})
        self.assertEqual(body_lines("let other () = 1\n"), {SELECTED: None, PEER: None})

    def test_a_complete_record_passes_without_an_order_or_ratio_rule(self):
        manifest, record = passing()
        record["discovery"]["ranked"].reverse()
        record["discovery"]["boundRanges"].reverse()
        self.assertEqual(run(manifest, record)["outcome"], "passed")
        self.assertEqual(run(*passing())["reasons"], [])

    def test_each_failure_gives_its_reason(self):
        cases = {
            "the inputs changed": lambda m, r: m.update(inputsUnchanged=False),
            "Linux discovery tests": lambda m, r: m["linuxTests"].update(executed=0),
            "not supported": lambda m, r: r.update(supported=False),
            "configuration has errors": lambda m, r: r.update(configurationErrors=["tiered"]),
            "did not pass": lambda m, r: r.update(passed=False),
            "process id": lambda m, r: r.update(mapPath="/tmp/perf-8.map"),
            "recorded hash": lambda m, r: r.update(mapSha256="00"),
            "add up to the sample count": lambda m, r: r["discovery"]["counts"].update(unmapped=31),
            "ranked plus omitted": lambda m, r: r["discovery"]["counts"].update(omittedSamples=3),
            "sample events of the run": lambda m, r: r["discovery"].update(sampleEvents=1399),
            "not unique": lambda m, r: r["discovery"]["ranked"].append(dict(r["discovery"]["ranked"][0], samples=0)),
            f"fixture {PEER} is not ranked": lambda m, r: r["discovery"]["ranked"][1].update(samples=0),
            "do not bind": lambda m, r: r["discovery"].update(bindingError="two entries"),
            f"anchor of {SELECTED}": lambda m, r: r["discovery"]["anchors"][0].update(line=8),
            "design is not valid": lambda m, r: r["discovery"].update(designValid=False),
            "missing or negative": lambda m, r: r["discovery"]["counts"].update(unmapped=-1, outOfScope=41),
            "ranked method has the wrong shape": lambda m, r: r["discovery"]["ranked"].__setitem__(0, None),
            "bound range has the wrong shape": lambda m, r: r["discovery"].update(boundRanges=[[1]]),
            "an anchor has the wrong shape": lambda m, r: r["discovery"].update(anchors=[None]),
            "ranked symbols do not bind": lambda m, r: r["discovery"]["boundRanges"].__setitem__(0, [1, 2]),
            "threads accepted": lambda m, r: r["threads"].__setitem__(0, {"accepted": 949}),
            "eligible methods did not bind": lambda m, r: r.update(bindError="no entry"),
            "differs from the hashed Work.fs": lambda m, r: m["sources"].update({WORK_PATH: "00"}),
            "no discovery record": lambda m, r: r.update(discovery=None),
            f"fixture {PEER} is not in the eligible binding": lambda m, r: r["discovery"]["ranked"][1].update(eligibility="OutsideEligibleBinding"),
            "perf map was not captured": lambda m, r: r.update(mapError="Unsupported"),
            "outside-binding list differs": lambda m, r: r["discovery"].update(outsideBinding=[]),
            "eligibility or design lists": lambda m, r: r["discovery"].pop("outsideBinding"),
            "in-binding candidates": lambda m, r: r["discovery"].update(designCandidates=[SELECTED, PEER, "CozNet.TargetLocal.Program::other()"]),
            "method catalog failed": lambda m, r: r.update(catalogError="DuplicateSymbol"),
        }
        for reason, change in cases.items():
            manifest, record = passing()
            change(manifest, record)
            result = run(manifest, record)
            with self.subTest(reason=reason):
                self.assertEqual(result["outcome"], "failed")
                self.assertTrue(any(reason in r for r in result["reasons"]), result["reasons"])

    def test_an_exit_code_a_missing_record_a_missing_map_and_a_rank_error_fail(self):
        manifest, record = passing()
        self.assertIn("the capture exited with 1", run(manifest, record, record_exit=1)["reasons"])
        self.assertIn("no capture record", run(manifest, None)["reasons"])
        self.assertIn("no retained perf map", run(manifest, record, map_bytes=None)["reasons"])
        bogus = copy.deepcopy(record)
        bogus["discovery"]["ranked"][0]["eligibility"] = "bogus"
        self.assertIn("a ranked method has the wrong shape", run(manifest, bogus)["reasons"])
        incomplete = copy.deepcopy(record)
        incomplete["discovery"] = {"rankError": "RunIncomplete [\"lost\"]"}
        reasons = run(manifest, incomplete)["reasons"]
        self.assertTrue(any(r.startswith("rank gave an error") for r in reasons), reasons)

    def test_a_valid_run_without_the_expected_samples_fails(self):
        manifest, record = passing()
        record["discovery"]["ranked"] = [m for m in record["discovery"]["ranked"] if m["symbol"] != PEER]
        record["discovery"]["boundRanges"] = [[m["start"], m["end"]] for m in record["discovery"]["ranked"]]
        record["discovery"]["designCandidates"] = [SELECTED]
        record["discovery"]["counts"]["inScope"] -= 450
        record["discovery"]["counts"]["outOfScope"] += 450
        result = run(manifest, record)
        self.assertEqual(result["outcome"], "failed")
        self.assertEqual(result["reasons"], [f"the fixture {PEER} is not ranked with samples"])

    def test_the_summary_keeps_the_tree_state(self):
        self.assertEqual((run(*passing())["treeClean"], run(*passing())["commit"]), (False, "abc"))


class ContractTests(unittest.TestCase):
    def test_a_current_record_passes_under_the_current_contract(self):
        result = run(*passing())
        self.assertEqual((result["assessment"], result["contract"], result["workflowEvaluated"], result["notEvaluated"]), ("passed", "discovery-check/2", True, []))

    def test_a_current_record_without_a_current_field_fails_and_does_not_fall_back(self):
        changes = {
            "mapHashKind": lambda r: r.pop("mapHashKind"),
            "eligibility": lambda r: r["discovery"]["ranked"][0].pop("eligibility"),
            "outsideBinding": lambda r: r["discovery"].pop("outsideBinding"),
            "designEligible": lambda r: r["discovery"].pop("designEligible"),
            "designCandidates": lambda r: r["discovery"].pop("designCandidates"),
        }
        for name, change in changes.items():
            manifest, record = passing()
            manifest["commit"] = LEGACY_COMMIT
            change(record)
            result = run(manifest, record)
            with self.subTest(field=name):
                self.assertEqual((result["assessment"], result["contract"]), ("failed", "discovery-check/2"), result["reasons"])

    def test_an_unknown_schema_is_unsupported(self):
        manifest, record = passing()
        record["schema"] = "coznet.discovery-check/9"
        result = run(manifest, record)
        self.assertEqual((result["assessment"], result["passed"], result["contract"]), ("unsupported", False, None))
        self.assertEqual(result["reasons"], ["unsupported schema 'coznet.discovery-check/9'"])

    def test_a_present_schema_other_than_the_current_one_is_unsupported_even_when_null(self):
        for value in (None, "", 2):
            manifest, record = legacy()
            record["schema"] = value
            with self.subTest(schema=value):
                self.assertEqual(run(manifest, record)["assessment"], "unsupported")
                self.assertEqual(run(manifest, record, requested="legacy-1")["assessment"], "unsupported")

    def test_a_commit_that_is_not_a_string_does_not_select_the_legacy_contract(self):
        for commit in ([LEGACY_COMMIT], {"sha": LEGACY_COMMIT}, None):
            manifest, record = legacy()
            manifest["commit"] = commit
            with self.subTest(commit=commit):
                self.assertEqual(run(manifest, record)["assessment"], "unsupported")

    def test_the_current_contract_cannot_be_requested_by_name(self):
        result = run(*passing(), requested="discovery-check/2")
        self.assertEqual(result["assessment"], "unsupported")
        self.assertIn("selected by the record schema", result["reasons"][0])

    def test_an_unversioned_record_of_another_commit_is_unsupported(self):
        manifest, record = legacy()
        manifest["commit"] = "0000000000000000000000000000000000000000"
        result = run(manifest, record)
        self.assertEqual((result["assessment"], result["passed"]), ("unsupported", False))
        self.assertIn("--contract legacy-1", result["reasons"][0])

    def test_the_retained_shape_passes_only_under_the_legacy_contract(self):
        result = run(*legacy())
        self.assertEqual((result["assessment"], result["contract"], result["workflowEvaluated"]), ("passed", "legacy-1", False), result["reasons"])
        self.assertIn("the provenance of the library workflow", result["notEvaluated"])
        self.assertIn("pinned legacy commit", result["selection"])

    def test_an_explicit_legacy_selection_applies_and_conflicts_with_a_schema(self):
        manifest, record = legacy()
        manifest["commit"] = "0000000000000000000000000000000000000000"
        self.assertEqual(run(manifest, record, requested="legacy-1")["assessment"], "passed")
        self.assertEqual(run(*passing(), requested="legacy-1")["assessment"], "unsupported")
        self.assertEqual(run(*passing(), requested="legacy-9")["assessment"], "unsupported")

    def test_the_legacy_contract_keeps_the_common_checks(self):
        manifest, record = legacy()
        record["discovery"]["anchors"][0]["line"] = 8
        result = run(manifest, record)
        self.assertEqual(result["assessment"], "failed")
        self.assertTrue(any("anchor of" in r for r in result["reasons"]), result["reasons"])


class HashTests(unittest.TestCase):
    def check(self, retained, recorded):
        manifest, record = passing()
        record["mapSha256"] = hashlib.sha256(recorded).hexdigest().upper()
        return run(manifest, record, map_bytes=retained)["reasons"]

    def test_a_raw_byte_hash_keeps_a_final_newline_and_a_byte_order_mark(self):
        for retained in (MAP + b"\n", b"\xef\xbb\xbf" + MAP + b"\n", MAP + b"\r\n"):
            with self.subTest(retained=retained):
                self.assertNotIn("the retained perf map differs from the recorded hash", self.check(retained, retained))
                self.assertIn("the retained perf map differs from the recorded hash", self.check(retained, MAP))

    def test_the_legacy_hash_is_the_hash_of_its_retained_joined_lines(self):
        manifest, record = legacy()
        record["mapSha256"] = hashlib.sha256(MAP).hexdigest().upper()
        self.assertEqual(run(manifest, record, map_bytes=MAP)["assessment"], "passed")
        self.assertIn("the retained perf map differs from the recorded hash", run(manifest, record, map_bytes=MAP + b"\n")["reasons"])


if __name__ == "__main__":
    unittest.main()
