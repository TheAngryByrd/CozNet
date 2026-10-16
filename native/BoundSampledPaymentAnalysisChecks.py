import copy
import json
import pathlib
import tempfile
import unittest

from AnalyzeBoundSampledPayment import main, verify_record, verify_receipt_readiness, verify_receipt_preparation, verify_service_timing


class ServiceTimingChecks(unittest.TestCase):
    def record(self):
        buckets = [dict(Activity=i, Calls=2, Failures=0, ElapsedTicks=4, MaximumTicks=3) for i in range(6)]
        measured = [dict(Activity=i, Calls=1, Failures=0, ElapsedTicks=1, MaximumTicks=1) for i in range(6)]
        return dict(serviceTimingEnabled=True, finiteMeasurementEnabled=True, timestampFrequency=1000,
                    completed=True, serviceTiming=dict(All=buckets, Measurement=measured),
                    serviceCadence=dict(Checks=12, Skipped=2, Executions=10),
                    measurement=dict(Window=dict(Start=dict(BeforeTimestamp=10, TimestampFrequency=1000),
                                                 End=dict(AfterTimestamp=16, TimestampFrequency=1000))))

    def test_valid_window_boundary_and_legacy(self):
        verify_service_timing(self.record())
        verify_service_timing({})
        verify_service_timing(dict(serviceTimingEnabled=False, serviceTiming=None))

    def test_rejects_malformed_buckets_and_inconsistent_evidence(self):
        for mutate, message in (
            (lambda r: r.update(serviceTimingEnabled=1), "not Boolean"),
            (lambda r: r.update(serviceTimingEnabled=False), "Disabled"),
            (lambda r: r.pop("serviceTimingEnabled"), "no activation"),
            (lambda r: r.update(finiteMeasurementEnabled=False), "requires finite"),
            (lambda r: r["serviceTiming"]["All"].pop(), "bucket count"),
            (lambda r: r["serviceTiming"]["All"][0].update(Activity=1), "activity order"),
            (lambda r: r["serviceTiming"]["All"][0].update(Calls=True), "unsigned"),
            (lambda r: r["serviceTiming"]["All"][0].update(MaximumTicks=5), "totals"),
            (lambda r: r["serviceTiming"]["All"][0].update(ElapsedTicks=7), "totals"),
            (lambda r: r["serviceTiming"]["Measurement"][0].update(Calls=3), "exceeds lifetime"),
            (lambda r: r["serviceTiming"]["All"][0].update(Failures=1), "Completed"),
            (lambda r: r["serviceCadence"].update(Checks=13), "cadence"),
            (lambda r: r["serviceCadence"].update(Skipped=3), "cadence"),
            (lambda r: r["serviceCadence"].update(Executions=9), "cadence"),
            (lambda r: r["measurement"]["Window"].update(Start=None), "no measurement start"),
            (lambda r: r["measurement"]["Window"]["End"].update(TimestampFrequency=999), "clock differs"),
            (lambda r: r["measurement"]["Window"]["End"].update(AfterTimestamp=15), "exceeds its measurement"),
        ):
            with self.subTest(message=message):
                record = self.record()
                mutate(record)
                with self.assertRaisesRegex(ValueError, message):
                    verify_service_timing(record)

    def test_failed_call_subset_and_open_window(self):
        record = self.record()
        record["completed"] = False
        record["serviceTiming"]["All"][0]["Failures"] = 2
        record["serviceTiming"]["Measurement"][0]["Failures"] = 1
        record["measurement"]["Window"]["End"] = None
        verify_service_timing(record)
        record["serviceTiming"]["Measurement"][0]["Failures"] = 0
        with self.assertRaisesRegex(ValueError, "Unmeasured service failures"):
            verify_service_timing(record)

    def test_full_replay_checks_service_activation(self):
        record = valid_record()
        record.update(serviceTimingEnabled=False, serviceTiming=None)
        verify_record(record)
        record["serviceTiming"] = {}
        with self.assertRaisesRegex(ValueError, "Disabled service timing"):
            verify_record(record)

    def test_measurement_subset_leaves_possible_remaining_calls(self):
        for calls, elapsed, maximum, message in (
            (2, 2, 1, "elapsed exceeds remaining calls"),
            (2, 4, 2, "cannot supply lifetime maximum"),
            (1, 0, 0, "elapsed exceeds remaining calls"),
            (1, 2, 2, "cannot supply lifetime maximum"),
        ):
            with self.subTest(calls=calls, elapsed=elapsed, maximum=maximum):
                record = self.record()
                record["serviceTiming"]["Measurement"][0].update(Calls=calls, ElapsedTicks=elapsed, MaximumTicks=maximum)
                with self.assertRaisesRegex(ValueError, message):
                    verify_service_timing(record)
        record = self.record()
        record["serviceTiming"]["Measurement"] = copy.deepcopy(record["serviceTiming"]["All"])
        record["measurement"]["Window"]["End"]["AfterTimestamp"] = 34
        verify_service_timing(record)


class ReceiptPreparationChecks(unittest.TestCase):
    def test_preparation_requires_mode_settlement_and_premeasurement_order(self):
        record = dict(receiptPreparationEnabled=True, selectedPaymentEnabled=True, finiteMeasurementEnabled=True,
                      passed=True, completed=True, timestampFrequency=1000,
                      receiptPreparation=dict(Started=1, Observed=2, Accepted=3, Children=10,
                                              ConfirmedNanoseconds=500000, Terminal=True, RemainingNanoseconds=0),
                      measurement=dict(Window=dict(Start=dict(BeforeTimestamp=4, TimestampFrequency=1000))))
        verify_receipt_preparation(record)
        for mutate, message in (
            (lambda r: r.update(receiptPreparationEnabled=1), "not Boolean"),
            (lambda r: r.update(receiptPreparationEnabled=False), "Disabled"),
            (lambda r: r.update(selectedPaymentEnabled=False), "requires"),
            (lambda r: r.update(finiteMeasurementEnabled=False), "requires"),
            (lambda r: r.update(receiptPreparation=None), "no receipt preparation"),
            (lambda r: r["receiptPreparation"].update(Observed=4), "clock order"),
            (lambda r: r["receiptPreparation"].update(Children=9), "isolated lease"),
            (lambda r: r["receiptPreparation"].update(ConfirmedNanoseconds=499999), "isolated lease"),
            (lambda r: r["receiptPreparation"].update(Terminal=False), "isolated lease"),
            (lambda r: r["receiptPreparation"].update(RemainingNanoseconds=1), "isolated lease"),
            (lambda r: r["receiptPreparation"].update(Accepted=5), "overlaps measurement"),
            (lambda r: r.pop("receiptPreparationEnabled"), "no activation flag"),
        ):
            with self.subTest(message=message):
                changed = copy.deepcopy(record)
                mutate(changed)
                with self.assertRaisesRegex(ValueError, message):
                    verify_receipt_preparation(changed)

    def test_disabled_legacy_and_failed_preparation(self):
        verify_receipt_preparation({})
        verify_receipt_preparation(dict(receiptPreparationEnabled=False, receiptPreparation=None))
        verify_receipt_preparation(dict(receiptPreparationEnabled=True, selectedPaymentEnabled=True,
                                       finiteMeasurementEnabled=True, passed=False, completed=False, receiptPreparation=None))


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


def failure_record():
    record = valid_record()
    record.update(passed=False, completed=False, failure="debt failure")
    record["failureBoundary"] = dict(
        LastObservedDebt=dict(Timestamp=1, **record["finalDebt"]), Command=1, Generation=1, Budget=500000,
        RequestStarted=1, Capture=dict(Failure="debt failure", Started=2, Finished=3, TimestampFrequency=1000000000, State=1,
                                      Raw=copy.deepcopy(record["payments"][0]["Observation"]["Raw"]), CaptureFailure=None))
    return record


class BoundPaymentAnalysisChecks(unittest.TestCase):
    def test_full_replay_checks_readiness_activation(self):
        record = valid_record()
        record.update(directReceiptReadyEnabled=False, receiptReadyCalls=0)
        verify_record(record)
        record["receiptReadyCalls"] = 1
        with self.assertRaisesRegex(ValueError, "Disabled receipt readiness contains calls"):
            verify_record(record)

    def test_readiness_activation_and_attempt_count(self):
        verify_receipt_readiness({})
        verify_receipt_readiness(dict(directReceiptReadyEnabled=False, receiptReadyCalls=0))
        for calls in (0, 1, 9223372036854775807):
            verify_receipt_readiness(dict(directReceiptReadyEnabled=True, receiptReadyCalls=calls, selectedPaymentEnabled=True))
        for record in (
            dict(receiptReadyCalls=0),
            dict(directReceiptReadyEnabled=1, receiptReadyCalls=0),
            dict(directReceiptReadyEnabled=False, receiptReadyCalls=1),
            *(dict(directReceiptReadyEnabled=True, selectedPaymentEnabled=True, receiptReadyCalls=value)
              for value in (-1, True, 1.5, 9223372036854775808)),
        ):
            with self.subTest(record=record), self.assertRaises(ValueError):
                verify_receipt_readiness(record)

    def test_direct_readiness_requires_selected_payment(self):
        for selected in (None, False, 1):
            record = dict(directReceiptReadyEnabled=True, receiptReadyCalls=0)
            if selected is not None:
                record["selectedPaymentEnabled"] = selected
            with self.subTest(selected=selected), self.assertRaisesRegex(ValueError, "Receipt readiness requires selected payment"):
                verify_receipt_readiness(record)

    def test_direct_readiness_excludes_retained_header_dispatches(self):
        record = dict(directReceiptReadyEnabled=True, receiptReadyCalls=1, selectedPaymentEnabled=True,
                      dispatchTimingEnabled=True, dispatchTiming=dict(Recent=[dict(Operation=3)]))
        verify_receipt_readiness(record)
        for operation in (2, 6):
            for key in ("Recent", "Prefix", "Longest", "LongestQueue", "LongestAction", "LongestReturn"):
                changed = copy.deepcopy(record)
                changed["dispatchTiming"][key] = [dict(Operation=operation)] if key in ("Recent", "Prefix") else dict(Operation=operation)
                with self.subTest(operation=operation, key=key), self.assertRaisesRegex(ValueError, "normal header"):
                    verify_receipt_readiness(changed)

    def test_publication_mode_and_service_count_are_checked(self):
        for batch in (False, True):
            record = valid_record()
            record.update(batchServiceEnabled=batch, sampleServiceCalls=1 if batch else len(record["samples"]))
            self.assertTrue(verify_record(record)["casePassed"])
            for value in (0, True, -1, len(record["samples"]) + 1):
                changed = copy.deepcopy(record)
                changed["sampleServiceCalls"] = value
                with self.assertRaises(ValueError):
                    verify_record(changed)
        record["batchServiceEnabled"] = 1
        with self.assertRaisesRegex(ValueError, "not Boolean"):
            verify_record(record)

    def test_rejected_receipt_frequency_matches_failure_capture(self):
        record = failure_record()
        message = "System.IO.InvalidDataException: The cooperative request-to-receipt interval exceeds 100 ms or has invalid clock evidence."
        record.update(failure=message, timestampFrequency=1000000000, receiptIntervalCaptureVersion=1,
                      receiptIntervalRejection=dict(Request=dict(Worker=2, Command=2, Generation=2, Budget=500000),
                                                    Started=1, Observed=100000002, TimestampFrequency=1000000000))
        record["failureBoundary"].update(Command=2, Generation=2)
        record["failureBoundary"]["Capture"].update(Failure=message, Started=200000000, Finished=200000001,
                                                  State=2, Raw=None, CaptureFailure="snapshot failed")
        self.assertFalse(verify_record(record)["casePassed"])
        changed = copy.deepcopy(record)
        changed["failureBoundary"]["Capture"]["TimestampFrequency"] = 10000000
        with self.assertRaisesRegex(ValueError, "Rejected receipt clock differs"):
            verify_record(changed)
        for frequency in (0, -1, True, 100000000):
            with self.subTest(frequency=frequency):
                changed = copy.deepcopy(record)
                changed["timestampFrequency"] = frequency
                changed["receiptIntervalRejection"]["TimestampFrequency"] = frequency
                with self.assertRaisesRegex(ValueError, "Rejected receipt clock differs"):
                    verify_record(changed)

    def test_new_receipt_capture_is_checked_in_legacy_workload(self):
        record = valid_record()
        record.update(receiptIntervalCaptureVersion=1, receiptIntervalRejection=None)
        self.assertTrue(verify_record(record)["completed"])
        record["failure"] = "System.IO.InvalidDataException: The cooperative request-to-receipt interval exceeds 100 ms or has invalid clock evidence."
        with self.assertRaisesRegex(ValueError, "rejection evidence"):
            verify_record(record)

    def test_schema_versions_do_not_use_legacy_replay_silently(self):
        record = valid_record()
        verify_record(record)
        record["schemaVersion"] = 1
        verify_record(record)
        for version in (2, 0, True, "1", None):
            with self.subTest(version=version):
                record["schemaVersion"] = version
                with self.assertRaisesRegex(ValueError, "schema version 1"):
                    verify_record(record)

    def test_failure_capture_outcomes_preserve_runtime_failure(self):
        for state in (0, 1, 2):
            with self.subTest(state=state):
                record = failure_record()
                capture = record["failureBoundary"]["Capture"]
                capture.update(State=state, Raw=capture["Raw"] if state == 1 else None,
                               CaptureFailure="snapshot error" if state == 2 else None)
                result = verify_record(record)
                self.assertFalse(result["casePassed"])
                self.assertFalse(result["completed"])

    def test_failure_capture_corruption_is_rejected(self):
        for mutate, message in (
            (lambda r: r.update(failureBoundary=None), "no boundary capture"),
            (lambda r: r["failureBoundary"]["Capture"].update(Failure="replacement"), "original failure"),
            (lambda r: r["failureBoundary"]["Capture"].update(Finished=0), "clock regressed"),
            (lambda r: r["failureBoundary"]["Capture"].update(TimestampFrequency=0), "frequency is zero"),
            (lambda r: r["failureBoundary"]["LastObservedDebt"].update(Timestamp=4), "debt follows"),
            (lambda r: r["failureBoundary"]["Capture"].update(State=2), "diagnostic error"),
            (lambda r: r["failureBoundary"].update(Generation=2), "identity differs"),
            (lambda r: r["nativeCleanup"]["Header"].update(Captured=1), "follows cleanup"),
            (lambda r: r["failureBoundary"]["Capture"]["Raw"]["Children"][0].update(Waited=1), "prefix changed"),
            (lambda r: r["failureBoundary"]["Capture"]["Raw"]["Clock"].update(Nanoseconds=1), "clock precedes"),
        ):
            with self.subTest(message=message):
                record = failure_record()
                mutate(record)
                with self.assertRaisesRegex(ValueError, message):
                    verify_record(record)

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
        for key in ("selectedPeak", "peerPeak", "finalDebt"):
            record[key].update(PeerWait=0, Peer=record[key]["Global"])
        with self.assertRaisesRegex(ValueError, "Expected rejection was not verified"):
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


class BoundClockIntegrationChecks(unittest.TestCase):
    def test_clock_declaration_is_checked_and_returned(self):
        record = valid_record()
        record.update(boundSampleClockEnabled=False, boundSampleClockEvidenceVersion=1, boundSampleClock=None)
        self.assertEqual('disabled', verify_record(record)['boundSampleClock']['status'])
        record['boundSampleClockEvidenceVersion'] = 2
        with self.assertRaisesRegex(ValueError, 'Unsupported bound sample clock version'):
            verify_record(record)


if __name__ == "__main__":
    unittest.main()
