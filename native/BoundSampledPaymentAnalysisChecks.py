import copy
import json
import pathlib
import tempfile
import unittest

from AnalyzeBoundSampledPayment import main, verify_record


def valid_record():
    children = [dict(Ordinal=i + 1, Valid=1, WaitStatus=0, Worker=2, CodeVersion=1, Site=2,
                     Requested=50000, Waited=50000, Armed=999 + i * 50000,
                     Started=1000 + i * 50000, Finished=51000 + i * 50000) for i in range(10)]
    header = dict(Generation=1, Command=1, Budget=500000, Visibility=3, Stop=1, Error=0,
                  TransportError=0, MayHaveDelivery=0, Children=10, Measured=500000, Captured=501005)
    received = [dict(Raw=child, FirstCaptured=501005, FirstManagedReceiptUpper=501010,
                     Timing=dict(UpperNanoseconds=501010 - child["Armed"])) for child in children]
    source = [dict(ProcessId=1, ThreadId=2, Timestamp=i + 1, CumulativeCpuNanoseconds=(i + 1) * 1000000,
                   InstructionPointer=100, Method="selected", PeriodNanoseconds=1000000) for i in range(2)]
    debt = dict(Global=500000, SelectedCredit=500000, PeerWait=500000, Selected=0, Peer=0)
    return dict(percent=50, binding=dict(Generation=1), nativeRange=dict(Start=100, Size=10), sourceSamples=source,
                samples=[dict(Index=i, Sample=sample, Credit=0 if i == 0 else 500000) for i, sample in enumerate(source)],
                credit=500000, paid=500000,
                payments=[dict(Command=1, Generation=1, Budget=500000, Started=1, Finished=2,
                               Observation=dict(Raw=dict(Header=header, Children=children, Clock=dict(Status=0, Nanoseconds=501010), Status=0), Received=received),
                               Cleanup=dict(Status=0, Header=copy.deepcopy(header), Children=copy.deepcopy(children)))],
                selectedPeak=debt, peerPeak=debt, finalDebt=debt, passed=True, completed=True,
                invalidateAfterPayment=False, invalidated=False, rejectionVerified=False, rejectedAfterPayment=False,
                failure=None, verificationFailure=None, cleanupErrors=[], joined=True, closed=True,
                batch=dict(Samples=2, LostRecords=0, LostSamples=0, Throttles=0),
                sourceCleanup=dict(SourceCreated=True, DisableSucceeded=True, SourceDisposed=True, CollectorJoined=True, Errors=[]),
                nativeCleanup=dict(Status=0, Header=copy.deepcopy(header), Children=copy.deepcopy(children)),
                finalCounters=dict(LastReading=dict(CpuNanoseconds=2000000)))


class BoundPaymentAnalysisChecks(unittest.TestCase):
    def test_zero_arm_clock_cannot_pass(self):
        record = valid_record()
        payment = record["payments"][0]
        payment["Observation"]["Raw"]["Children"][0]["Armed"] = 0
        receipt = payment["Observation"]["Received"][0]
        receipt["Timing"]["UpperNanoseconds"] = receipt["FirstManagedReceiptUpper"]
        payment["Cleanup"]["Children"][0]["Armed"] = 0
        record["nativeCleanup"]["Children"][0]["Armed"] = 0
        with self.assertRaisesRegex(ValueError, "arm clock failed"):
            verify_record(record)

    def test_receipt_capture_cannot_exceed_snapshot(self):
        record = valid_record()
        record["payments"][0]["Observation"]["Received"][0]["FirstCaptured"] += 1
        with self.assertRaisesRegex(ValueError, "capture exceeds snapshot"):
            verify_record(record)

    def test_child_request_must_match_remaining_budget(self):
        for budget in (1, 50000, 499999):
            with self.subTest(budget=budget):
                record = valid_record()
                payment = record["payments"][0]
                payment["Budget"] = budget
                payment["Observation"]["Raw"]["Header"]["Budget"] = budget
                payment["Cleanup"]["Header"]["Budget"] = budget
                record["nativeCleanup"]["Header"]["Budget"] = budget
                with self.assertRaisesRegex(ValueError, "remaining budget"):
                    verify_record(record)

    def test_final_cleanup_must_match_released_payment(self):
        record = valid_record()
        record["nativeCleanup"]["Header"]["Generation"] = 2
        with self.assertRaisesRegex(ValueError, "last released payment"):
            verify_record(record)

    def test_final_cleanup_must_match_pending_request(self):
        record = valid_record()
        record.update(completed=False, invalidateAfterPayment=True, invalidated=True,
                      rejectionVerified=True, rejectedAfterPayment=True,
                      failure="The CLR probe lifetime is no longer admitted.",
                      pending=dict(command=2, generation=2, budget=500000))
        record["nativeCleanup"]["Header"].update(Command=2, Generation=2)
        self.assertTrue(verify_record(record)["casePassed"])
        record["nativeCleanup"]["Header"]["Generation"] = 3
        with self.assertRaisesRegex(ValueError, "pending request"):
            verify_record(record)

    def test_valid_payment_replays_credit_and_native_wait(self):
        result = verify_record(valid_record())
        self.assertEqual((result["credit"], result["paid"], result["payments"]), (500000, 500000, 1))

    def test_missing_native_fields_cannot_pass(self):
        record = valid_record()
        record["payments"][0]["Observation"]["Raw"]["Header"] = {}
        with self.assertRaises(KeyError):
            verify_record(record)

    def test_credit_and_payment_corruption_are_rejected(self):
        for mutate in (
            lambda r: r["samples"][0].update(Credit=500000),
            lambda r: r["samples"][1].update(Index=0),
            lambda r: r["payments"][0]["Observation"]["Raw"]["Header"].update(Measured=500001),
            lambda r: r["payments"][0]["Cleanup"]["Children"][0].update(Waited=50001),
            lambda r: r.update(paid=500001),
            lambda r: r["finalCounters"]["LastReading"].update(CpuNanoseconds=1999999),
        ):
            with self.subTest(mutate=mutate):
                record = valid_record()
                mutate(record)
                with self.assertRaises(ValueError):
                    verify_record(record)

    def test_runtime_failure_is_not_promoted_to_success(self):
        record = valid_record()
        record.update(passed=False, completed=False, failure="capacity exceeded")
        result = verify_record(record)
        self.assertFalse(result["casePassed"])
        self.assertFalse(result["completed"])

    def test_rejection_requires_real_prior_payment(self):
        record = valid_record()
        record.update(completed=False, invalidateAfterPayment=True, invalidated=True, rejectionVerified=True, rejectedAfterPayment=True,
                      failure="The CLR probe lifetime is no longer admitted.")
        self.assertFalse(verify_record(record)["completed"])
        record.update(payments=[], paid=0)
        with self.assertRaises(ValueError):
            verify_record(record)

    def test_completed_case_requires_each_debt_record(self):
        for name in ("selectedPeak", "peerPeak", "finalDebt"):
            with self.subTest(name=name):
                record = valid_record()
                record[name] = None
                with self.assertRaises(ValueError):
                    verify_record(record)

    def test_duplicate_zero_cases_cannot_satisfy_the_matrix(self):
        record = valid_record()
        record.update(percent=0, credit=0, paid=0, payments=[])
        for sample in record["samples"]:
            sample["Credit"] = 0
        record["finalDebt"].update(Global=0, SelectedCredit=0, PeerWait=0)
        with tempfile.TemporaryDirectory() as temporary:
            directory = pathlib.Path(temporary)
            for name in ("0", "50", "invalidate-after-payment"):
                (directory / f"{name}.json").write_text(json.dumps(record), encoding="utf-8")
            self.assertEqual(main(directory), 1)
            analysis = json.loads((directory / "analysis.json").read_text(encoding="utf-8"))
            self.assertFalse(analysis["passed"])
            self.assertTrue(analysis["cases"][0]["evidenceVerified"])
            self.assertFalse(analysis["cases"][1]["evidenceVerified"])
            self.assertFalse(analysis["cases"][2]["evidenceVerified"])

    def test_child_continuation_cannot_overlap_its_predecessor(self):
        record = valid_record()
        payment = record["payments"][0]
        children = payment["Observation"]["Raw"]["Children"]
        children[1]["Started"] = children[0]["Finished"] - 1
        payment["Cleanup"]["Children"] = copy.deepcopy(children)
        with self.assertRaisesRegex(ValueError, "continuation"):
            verify_record(record)

    def test_accepted_invalidation_requires_complete_cleanup_evidence(self):
        for mutate in (
            lambda r: r.update(joined=False),
            lambda r: r.update(closed=False),
            lambda r: r["sourceCleanup"].update(SourceDisposed=False),
            lambda r: r["sourceCleanup"].update(CollectorJoined=False),
            lambda r: r["nativeCleanup"].update(Status=1),
        ):
            record = valid_record()
            record.update(completed=False, invalidateAfterPayment=True, invalidated=True, rejectionVerified=True,
                          rejectedAfterPayment=True, failure="The CLR probe lifetime is no longer admitted.")
            mutate(record)
            with self.assertRaisesRegex(ValueError, "cleanup"):
                verify_record(record)

    def test_final_debt_must_reconcile_with_credit_and_payment(self):
        record = valid_record()
        record["finalDebt"] = dict(Global=0, SelectedCredit=0, PeerWait=0, Selected=0, Peer=0)
        with self.assertRaisesRegex(ValueError, "reconcile"):
            verify_record(record)


if __name__ == "__main__":
    unittest.main()
