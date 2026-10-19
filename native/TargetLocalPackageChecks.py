import unittest

from TargetLocalPackage import SUITES, verdict


def package(**changes):
    start = {"a.fs": "1", "b.fs": "2"}
    suites = [{"suite": name, "exitCode": 0, "runs": [f"{name}/run"], "summary": {"passed": True, "inputsUnchanged": True}, "sources": dict(start)} for name in SUITES]
    value = {"commit": "abc", "status": [], "startHashes": start, "suites": suites}
    value.update(changes)
    return value


def with_suite(index, **changes):
    value = package()
    value["suites"][index] = {**value["suites"][index], **changes}
    return value


class PackageVerdictTests(unittest.TestCase):
    def test_a_package_of_passed_suites_passes(self):
        result = verdict(package())
        self.assertTrue(result["passed"])
        self.assertTrue(result["treeClean"])

    def test_a_failed_suite_fails_the_package(self):
        result = verdict(with_suite(3, summary={"passed": False, "inputsUnchanged": True}))
        self.assertFalse(result["passed"])
        self.assertEqual(result["suites"][3]["reasons"], ["not passed"])

    def test_a_suite_without_a_summary_fails_the_package(self):
        self.assertEqual(verdict(with_suite(0, summary=None))["suites"][0]["reasons"], ["no summary"])
        self.assertFalse(verdict(with_suite(0, summary=None))["passed"])

    def test_a_suite_needs_exactly_one_new_run(self):
        self.assertEqual(verdict(with_suite(1, runs=[]))["suites"][1]["reasons"], ["0 new run folders"])
        self.assertEqual(verdict(with_suite(1, runs=["x", "y"]))["suites"][1]["reasons"], ["2 new run folders"])
        self.assertFalse(verdict(with_suite(1, runs=[]))["passed"])

    def test_changed_inputs_fail_the_package(self):
        result = verdict(with_suite(2, summary={"passed": True, "inputsUnchanged": False}))
        self.assertEqual(result["suites"][2]["reasons"], ["inputs changed"])
        self.assertFalse(result["passed"])

    def test_a_source_hash_must_equal_the_start_hash(self):
        result = verdict(with_suite(4, sources={"a.fs": "1", "b.fs": "9"}))
        self.assertEqual(result["suites"][4]["reasons"], ["b.fs differs from the start"])
        self.assertEqual(verdict(with_suite(4, sources={"c.fs": "3"}))["suites"][4]["reasons"], ["c.fs has no start hash"])
        self.assertEqual(verdict(with_suite(4, sources={"d.fs": None}))["suites"][4]["reasons"], ["d.fs has no start hash"])
        self.assertEqual(verdict(with_suite(4, sources={}))["suites"][4]["reasons"], ["no source hashes"])
        self.assertFalse(result["passed"])

    def test_a_runner_must_exit_with_zero(self):
        result = verdict(with_suite(5, exitCode=1))
        self.assertEqual(result["suites"][5]["reasons"], ["exit code 1"])
        self.assertFalse(result["passed"])

    def test_a_read_error_fails_its_suite(self):
        result = verdict(with_suite(6, readError="bad json", summary=None, sources=None))
        self.assertEqual(result["suites"][6]["reasons"], ["read error: bad json", "no summary"])
        self.assertFalse(result["passed"])

    def test_a_file_changed_during_the_package_fails_it(self):
        result = verdict(package(changedDuringPackage=["src/CozNet/CozNet.fsproj"]))
        self.assertEqual(result["changedDuringPackage"], ["src/CozNet/CozNet.fsproj"])
        self.assertFalse(result["passed"])

    def test_every_suite_must_run_in_order(self):
        value = package()
        value["suites"] = value["suites"][:-1]
        self.assertFalse(verdict(value)["passed"])
        value = package()
        value["suites"][0], value["suites"][1] = value["suites"][1], value["suites"][0]
        self.assertFalse(verdict(value)["suitesComplete"])

    def test_tree_clean_follows_the_status_and_not_the_verdict(self):
        result = verdict(package(status=[" M src/Directory.Build.targets"]))
        self.assertFalse(result["treeClean"])
        self.assertTrue(result["passed"])


if __name__ == "__main__":
    unittest.main()
