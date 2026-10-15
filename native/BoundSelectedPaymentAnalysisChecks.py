import copy
import json
import pathlib
import tempfile
import unittest

from AnalyzeBoundSelectedPayment import main, verify_case, verify_record, verify_pause_timing, PAUSE_TIMING_FIELDS


def payment(worker, ordinal, budget, waited, managed, native):
    child = dict(Ordinal=1, Valid=1, WaitStatus=0, Worker=worker, CodeVersion=1, Site=2,
                 Requested=budget, Waited=waited, Armed=native, Started=native + 1, Finished=native + waited + 1)
    header = dict(Generation=ordinal, Command=ordinal, Budget=budget, Visibility=3, Stop=1, Error=0,
                  TransportError=0, MayHaveDelivery=0, Children=1, Measured=waited, Captured=native + waited + 2)
    raw = dict(Status=0, Header=header, Children=[child], Clock=dict(Status=0, Nanoseconds=native + waited + 3))
    received = dict(Raw=copy.deepcopy(child), FirstCaptured=header["Captured"],
                    FirstManagedReceiptUpper=raw["Clock"]["Nanoseconds"], Timing=dict(UpperNanoseconds=waited + 3))
    cleanup = dict(Status=0, Header=dict(header, Captured=native + waited + 4), Children=[copy.deepcopy(child)])
    return dict(Command=ordinal, Generation=ordinal, Budget=budget, Started=managed, Finished=managed + 10,
                Observation=dict(Raw=raw, Received=[received]), Cleanup=cleanup,
                ReceiptInterval=dict(Started=managed, Observed=managed + 8, TimestampFrequency=1000000000))


def debt(timestamp, global_delay, credit, peer_wait, selected_wait):
    return dict(Timestamp=timestamp, Global=global_delay, SelectedCredit=credit, PeerWait=peer_wait,
                SelectedWait=selected_wait, SelectedLocal=credit + selected_wait, PeerLocal=peer_wait,
                Selected=max(0, global_delay - credit - selected_wait), Peer=max(0, global_delay - peer_wait))


def valid_record():
    source = []
    cpu = 0
    for index, period in enumerate((1, 3, 7, 1)):
        cpu += period
        source.append(dict(ProcessId=1, ThreadId=2, Timestamp=index + 1, CumulativeCpuNanoseconds=cpu,
                           InstructionPointer=100, Method="selected", PeriodNanoseconds=period))
    epochs = [dict(EligibilityEpoch=1 if index < 2 else 2, Sample=sample) for index, sample in enumerate(source)]
    payments = [payment(2, 1, 1, 4, 10, 100), payment(2, 2, 2, 2, 80, 300)]
    selected = [payment(1, 1, 3, 4, 40, 200)]
    initial = dict(SourceIdentity="source", Units="task-clock-nanoseconds", Enabled=False, Epoch=0,
                   Overflowed=False, UnrecordedCleanup=False, FailedReads=0, FirstReadFailure=None, Transitions=[],
                   LastReading=dict(SourceIdentity="source", Epoch=0, Enabled=False, Before=0, After=0,
                                    TimestampFrequency=1000000000, CpuNanoseconds=0, Error=None))
    final = copy.deepcopy(initial)
    final.update(Epoch=2, Transitions=[dict(Epoch=index // 2 + 1, Enabling=index % 2 == 0, Completed=True,
                                          Before=before, After=before + 1, TimestampFrequency=1000000000, Error=None)
                                     for index, before in enumerate((1, 31, 61, 100))])
    final["LastReading"].update(Epoch=2, Before=102, After=103, CpuNanoseconds=cpu)
    return dict(schemaVersion=3, timestampFrequency=1000000000, selectedPaymentEnabled=True, percent=50, binding=dict(Generation=1),
                nativeRange=dict(Start=100, Size=10), sourceSamples=source, sourceEpochSamples=epochs,
                samples=[dict(Index=index, Sample=sample, EligibilityEpoch=epochs[index]["EligibilityEpoch"],
                              Credit=1 if index in (1, 3) else 0) for index, sample in enumerate(source)],
                credit=2, paid=6, selectedPaid=4, payments=payments, selectedPayments=selected,
                pauses=[dict(Started=30, FirstUnprocessed=2, FinalDrainCount=2, CallbackFinished=60, Resumed=70)],
                selectedPeak=debt(25, 4, 1, 4, 0), peerPeak=debt(75, 6, 2, 4, 4), finalDebt=debt(110, 6, 2, 6, 4),
                passed=True, completed=True, invalidateAfterPayment=False, invalidateAfterSelectedPayment=False,
                invalidated=False, rejectionVerified=False, rejectedAfterPayment=False, failure=None,
                failureBoundary=None, selectedFailureBoundary=None, verificationFailure=None, peerFailure=None,
                cleanupErrors=[], joined=True, controllerJoined=True, closed=True, pending=None, selectedPending=None,
                sourceCleanup=dict(SourceCreated=True, DisableSucceeded=True, SourceDisposed=True, CollectorJoined=True, Errors=[]),
                nativeCleanup=copy.deepcopy(payments[-1]["Cleanup"]), selectedCleanup=copy.deepcopy(selected[-1]["Cleanup"]),
                selectedReceipt=copy.deepcopy(selected[-1]["Observation"]), initialCounters=initial, finalCounters=final,
                batch=dict(Samples=4, LostRecords=0, LostSamples=0, Throttles=0, RepresentedCpuNanoseconds=12,
                           DistinctInstructionPointers=1, SelectedMethodSamples=4, UnmappedSamples=0, SelectedSymbols=["selected"]))


def failed_record():
    record = valid_record()
    record.update(passed=False, completed=False, failure="controlled workload failure")
    for key, started in (("failureBoundary", 120), ("selectedFailureBoundary", 122)):
        record[key] = dict(LastObservedDebt=copy.deepcopy(record["finalDebt"]), Command=0, Generation=0, Budget=0,
                           RequestStarted=0, Capture=dict(Failure=record["failure"], Started=started, Finished=started + 1,
                                                         TimestampFrequency=1000000000, State=2, Raw=None,
                                                         CaptureFailure="controlled diagnostic failure"))
    return record


def invalidated_record():
    record = failed_record()
    record.update(passed=True, invalidateAfterSelectedPayment=True, invalidated=True,
                  rejectionVerified=True, rejectedAfterPayment=True,
                  failure="System.IO.InvalidDataException: The CLR probe lifetime is no longer admitted.",
                  credit=1, paid=4, finalDebt=None)
    for key in ("sourceSamples", "sourceEpochSamples", "samples"):
        record[key] = record[key][:2]
    record["payments"] = record["payments"][:1]
    record["nativeCleanup"] = copy.deepcopy(record["selectedCleanup"])
    record["pauses"][0]["Resumed"] = None
    record["peerPeak"] = debt(55, 5, 1, 4, 4)
    record["batch"].update(Samples=2, SelectedMethodSamples=2, RepresentedCpuNanoseconds=4)
    record["finalCounters"].update(Epoch=1, Transitions=record["finalCounters"]["Transitions"][:2])
    record["finalCounters"]["LastReading"].update(Epoch=1, Before=64, After=65, CpuNanoseconds=4)
    for key in ("failureBoundary", "selectedFailureBoundary"):
        record[key]["LastObservedDebt"] = copy.deepcopy(record["peerPeak"])
        record[key]["Capture"].update(Failure=record["failure"], State=0, CaptureFailure=None)
    return record


def with_cpu(record):
    record["childCpuEnabled"] = True
    def visit(value):
        if isinstance(value, list):
            for item in value:
                visit(item)
        elif isinstance(value, dict):
            if all(key in value for key in ("Header", "Children", "Status")) and value["Header"]["Visibility"] == 3:
                header = value["Header"]
                children = []
                for child in value["Children"]:
                    children.append(dict(Generation=header["Generation"], Command=header["Command"],
                        **{key: child[key] for key in ("Ordinal", "Worker", "CodeVersion", "Site", "Requested")},
                        Before=dict(Status=1, Clock=1, Nanoseconds=10, Resolution=1, Error=0),
                        After=dict(Status=1, Clock=1, Nanoseconds=11, Resolution=1, Error=0)))
                value["Cpu"] = dict(Slot=0 if children[0]["Worker"] == 2 else 1, Generation=header["Generation"],
                    Status=0, Required=len(children), Children=children, Failure=None)
            for key, item in value.items():
                if key != "Cpu":
                    visit(item)
    visit(record)
    return record


class ChildCpuAnalysisChecks(unittest.TestCase):
    def test_enabled_capture_preserves_case_outcome(self):
        self.assertEqual(verify_record(with_cpu(valid_record()))["childCpu"]["generations"], 3)
        self.assertFalse(verify_record(with_cpu(failed_record()))["casePassed"])

    def test_missing_or_disabled_capture_rejected_through_record(self):
        record = with_cpu(valid_record())
        record["payments"][0]["Observation"]["Raw"]["Cpu"] = None
        with self.assertRaisesRegex(ValueError, "lost child CPU"):
            verify_record(record)
        record = with_cpu(valid_record())
        record["childCpuEnabled"] = False
        with self.assertRaisesRegex(ValueError, "Unexpected child CPU"):
            verify_record(record)

    def test_identity_clock_and_count_corruption(self):
        for field, value in (("Generation", 99), ("Worker", 1), ("Ordinal", 2), ("Requested", 99)):
            record = with_cpu(valid_record())
            record["payments"][0]["Observation"]["Raw"]["Cpu"]["Children"][0][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                verify_record(record)
        for field, value in (("Nanoseconds", 9), ("Nanoseconds", 100), ("Status", True), ("Clock", 0), ("Resolution", 0)):
            record = with_cpu(valid_record())
            record["payments"][0]["Observation"]["Raw"]["Cpu"]["Children"][0]["After"][field] = value
            with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                verify_record(record)
        record = with_cpu(valid_record())
        record["payments"][0]["Observation"]["Raw"]["Cpu"]["Required"] = 0
        with self.assertRaisesRegex(ValueError, "count differs"):
            verify_record(record)

    def test_read_failures_and_unsupported_are_not_zero_measurements(self):
        for status, clock, error, field in ((0, 0, 0, "unsupportedReadings"), (2, 1, 5, "failedReadings")):
            record = with_cpu(valid_record())
            def replace(value):
                if isinstance(value, list):
                    for item in value:
                        replace(item)
                elif isinstance(value, dict):
                    if "Before" in value and "After" in value and "Requested" in value:
                        for side in ("Before", "After"):
                            value[side] = dict(Status=status, Clock=clock, Nanoseconds=0, Resolution=0, Error=error)
                    for item in value.values():
                        replace(item)
            replace(record)
            self.assertEqual(verify_record(record)["childCpu"][field], 6)

    def test_failed_snapshot_does_not_pass_replay(self):
        record = with_cpu(failed_record())
        record["payments"][0]["Observation"]["Raw"]["Cpu"]["Status"] = 7
        with self.assertRaisesRegex(ValueError, "original service outcome"):
            verify_record(record)
        self.assertEqual(record["failure"], "controlled workload failure")


class SelectedPaymentAnalysisChecks(unittest.TestCase):
    def cadence_record(self):
        record = valid_record()
        record.update(schemaVersion=4, selectedCadence="QualifiedSample", requestedBatches=5,
                      selectedBatches=5, selectedChecks=1, checksum=5 * 49995000,
                      selectedWorkStarted=1, selectedWorkFinished=99)
        record["pauses"][0]["Admission"] = dict(SampleCount=2, Epoch=1, Qualified=True)
        return record

    def test_fixed_work_qualified_cadence(self):
        self.assertTrue(verify_record(self.cadence_record())["casePassed"])

    def two_pause_cadence_record(self):
        record = self.cadence_record()
        record["selectedChecks"] = 2
        record["pauses"].append(dict(Started=91, FirstUnprocessed=4, FinalDrainCount=4,
                                     CallbackFinished=95, Resumed=100,
                                     Admission=dict(SampleCount=4, Epoch=2, Qualified=True)))
        final = record["finalCounters"]
        final["Epoch"] = 3
        final["Transitions"][-1].update(Before=92, After=93)
        final["Transitions"].extend([
            dict(Epoch=3, Enabling=True, Completed=True, Before=98, After=99,
                 TimestampFrequency=1000000000, Error=None),
            dict(Epoch=3, Enabling=False, Completed=True, Before=110, After=111,
                 TimestampFrequency=1000000000, Error=None)])
        final["LastReading"].update(Epoch=3, Before=112, After=113)
        record["finalDebt"]["Timestamp"] = 120
        return record

    def test_resumed_pause_requires_new_complete_interval(self):
        self.assertTrue(verify_record(self.two_pause_cadence_record())["casePassed"])
        for prefix in (2, 3):
            record = self.two_pause_cadence_record()
            record["pauses"][1]["FirstUnprocessed"] = prefix
            record["pauses"][1]["Admission"]["SampleCount"] = prefix
            self.reject(record, "qualification differs from original sample epochs")

    def test_cadence_requires_original_epoch_and_qualified_prefix(self):
        for field, value, error in (("Epoch", 2, "admission changed"),
                                    ("SampleCount", 1, "admission changed"),
                                    ("Qualified", False, "lacks a complete"),
                                    ("Qualified", 1, "Invalid cadence qualification")):
            record = self.cadence_record()
            record["pauses"][0]["Admission"][field] = value
            self.reject(record, error)

    def test_cadence_completion_requires_fixed_work(self):
        for field, value, error in (("selectedBatches", 4, "changed fixed selected work"),
                                    ("requestedBatches", 0, "Invalid fixed selected work"),
                                    ("requestedBatches", True, "unsigned"),
                                    ("selectedChecks", 0, "Pauses exceed"),
                                    ("checksum", 1, "Cadence checksum"),
                                    ("selectedWorkFinished", 0, "selected work interval"),
                                    ("selectedCadence", "other", "Unknown bound")):
            record = self.cadence_record()
            record[field] = value
            self.reject(record, error)

    def test_continuous_control_can_pause_without_qualification(self):
        record = self.cadence_record()
        record["selectedCadence"] = "Continuous"
        record["pauses"][0]["Admission"]["Qualified"] = False
        self.assertTrue(verify_record(record)["casePassed"])

    def reject(self, record, expected):
        with self.assertRaisesRegex(ValueError, expected):
            verify_record(record)

    def test_selected_settlement_and_epoch_remainder(self):
        result = verify_record(valid_record())
        self.assertEqual((result["credit"], result["paid"], result["selectedPaid"], result["selectedPayments"]), (2, 6, 4, 1))
        self.assertTrue(result["casePassed"])

    def test_zero_speedup_completes_without_payment_or_pause(self):
        record = valid_record()
        record.update(percent=0, credit=0, paid=0, selectedPaid=0, payments=[], selectedPayments=[], pauses=[],
                      selectedReceipt=None, selectedCleanup=None, nativeCleanup=None)
        for item in record["sourceEpochSamples"]:
            item["EligibilityEpoch"] = 1
        for item in record["samples"]:
            item.update(EligibilityEpoch=1, Credit=0)
        for key in ("selectedPeak", "peerPeak", "finalDebt"):
            record[key] = debt(110, 0, 0, 0, 0)
        final = record["finalCounters"]
        final.update(Epoch=1, Transitions=[final["Transitions"][0], dict(final["Transitions"][-1], Epoch=1)])
        final["LastReading"]["Epoch"] = 1
        result = verify_case(record, "0")
        self.assertTrue(result["completed"])
        self.assertEqual((result["credit"], result["paid"], result["selectedPaid"], result["pauses"]), (0, 0, 0, 0))

    def test_observed_debt_bound_rejects_valid_large_overshoot(self):
        record = valid_record()
        record["payments"][0] = payment(2, 1, 1, 2000002, 10, 100)
        record["paid"] = 2000004
        record["selectedPeak"] = debt(25, 2000002, 1, 2000002, 0)
        self.reject(record, "Accepted debt exceeds 2 ms")

    def test_final_ledger_must_reconcile_all_settled_wait(self):
        record = valid_record()
        record["finalDebt"] = debt(110, 6, 2, 6, 3)
        self.reject(record, "Final ledger does not reconcile")

    def test_trailing_samples_require_the_resumed_epoch(self):
        record = valid_record()
        record["sourceEpochSamples"][-1]["EligibilityEpoch"] = 3
        record["samples"][-1].update(EligibilityEpoch=3, Credit=0)
        record["credit"] = 1
        self.reject(record, "Resumed source assigned the wrong eligibility epoch")

    def test_pause_cannot_start_before_source_enable(self):
        record = valid_record()
        record["pauses"][0]["Started"] = 1
        self.reject(record, "Pause is not enclosed")

    def test_accepted_case_cannot_retain_selected_reservation(self):
        record = valid_record()
        record["selectedPending"] = dict(command=2, generation=2, budget=1, started=58)
        self.reject(record, "unexpected ledger lease")

    def test_runtime_failure_is_not_promoted(self):
        result = verify_record(failed_record())
        self.assertFalse(result["casePassed"])
        self.assertFalse(result["completed"])

    def test_invalidation_after_settlement_preserves_wait_and_rejects_resume(self):
        result = verify_case(invalidated_record(), "invalidate-after-selected-payment")
        self.assertTrue(result["casePassed"])
        self.assertFalse(result["completed"])
        self.assertEqual(result["selectedPaid"], 4)
        record = invalidated_record()
        record["pauses"][0]["Resumed"] = 70
        self.reject(record, "Counter transitions")

    def test_cancelled_peer_cleanup_is_not_settled_payment(self):
        record = invalidated_record()
        record["pending"] = dict(command=2, generation=2, budget=1, started=58)
        header = dict(Generation=2, Command=2, Budget=1, Visibility=3, Stop=3, Error=0,
                      TransportError=0, MayHaveDelivery=0, Children=0, Measured=0, Captured=400)
        record["nativeCleanup"] = dict(Status=0, Header=header, Children=[])
        boundary = record["failureBoundary"]
        boundary.update(Command=2, Generation=2, Budget=1, RequestStarted=58)
        raw = dict(Status=0, Header=dict(header, Visibility=0, Stop=0, MayHaveDelivery=1, Captured=350),
                   Children=[], Clock=dict(Status=0, Nanoseconds=351))
        boundary["Capture"].update(State=1, Raw=raw)
        result = verify_record(record)
        self.assertEqual(result["paid"], 4)
        record["nativeCleanup"]["Header"]["Generation"] = 1
        self.reject(record, "Cancelled peer lost retired ownership")

    def test_epoch_baseline_and_remainder_corruption(self):
        for index, credit in ((0, 1), (2, 3), (3, 0)):
            record = valid_record()
            record["samples"][index]["Credit"] = credit
            self.reject(record, "baseline or fractional")

    def test_drain_labels_cannot_move_to_resume_epoch(self):
        record = valid_record()
        record["sourceEpochSamples"][1]["EligibilityEpoch"] = 2
        record["samples"][1].update(EligibilityEpoch=2, Credit=0)
        record["samples"][2]["Credit"] = 3
        record["samples"][3]["Credit"] = 1
        record["credit"] = 4
        self.reject(record, "Final drain changed")

    def test_pause_order_and_callback_admission(self):
        for field, value, error in (("FirstUnprocessed", 3, "Final drain prefix"),
                                    ("FinalDrainCount", None, "acknowledged final drain"),
                                    ("Resumed", 59, "before callback return"),
                                    ("Resumed", None, "incomplete pause")):
            with self.subTest(field=field, value=value):
                record = valid_record()
                record["pauses"][0][field] = value
                self.reject(record, error)

    def test_selected_payment_must_stay_inside_one_callback(self):
        for started, finished in ((29, 50), (40, 61)):
            record = valid_record()
            record["selectedPayments"][0].update(Started=started, Finished=finished)
            record["selectedPayments"][0]["ReceiptInterval"]["Started"] = started
            self.reject(record, "escaped its excluded callback")
        record = valid_record()
        record["selectedPayments"].append(payment(1, 2, 3, 4, 45, 250))
        record["selectedPaid"] = 8
        self.reject(record, "unique pause")

    def test_native_receipts_keep_slot_identity_and_bounds(self):
        for field, value, error in (("Worker", 2, "Child site differs"), ("Requested", 50001, "remaining budget"),
                                    ("Waited", 0, "timing exceeds"), ("Finished", 5001000, "timing exceeds")):
            record = valid_record()
            record["selectedPayments"][0]["Observation"]["Raw"]["Children"][0][field] = value
            self.reject(record, error)

    def test_ledger_locals_cannot_be_replaced_by_credit_only(self):
        for field in ("SelectedLocal", "PeerLocal", "SelectedWait", "Selected", "Peer"):
            record = valid_record()
            record["finalDebt"][field] += 1
            self.reject(record, "Ledger")

    def test_counter_epoch_and_failure_evidence(self):
        for field, value in (("Epoch", 1), ("Enabled", True), ("FailedReads", 1), ("Overflowed", True)):
            record = valid_record()
            record["finalCounters"][field] = value
            self.reject(record, "Counter transitions|counter evidence")
        record = valid_record()
        record["finalCounters"]["Transitions"][2]["Completed"] = False
        self.reject(record, "Counter transition failed")
        record = valid_record()
        record["initialCounters"]["LastReading"]["After"] = 2
        self.reject(record, "Counter transition clock regressed")

    def test_payment_requires_disabled_source_and_resume_follows_callback(self):
        record = valid_record()
        record["finalCounters"]["Transitions"][1].update(Before=49, After=50)
        self.reject(record, "precedes producer stop")
        record = valid_record()
        record["finalCounters"]["Transitions"][2].update(Before=58, After=59)
        self.reject(record, "enable precedes excluded callback")

    def test_native_numeric_fields_reject_booleans(self):
        record = valid_record()
        record["selectedPayments"][0]["Observation"]["Raw"]["Children"][0]["Valid"] = True
        self.reject(record, "Invalid unsigned evidence")

    def test_signed_transport_failure_remains_raw_diagnostic_evidence(self):
        record = failed_record()
        raw = copy.deepcopy(record["payments"][0]["Observation"]["Raw"])
        raw["Header"]["TransportError"] = -5
        record["failureBoundary"]["Capture"].update(State=1, Raw=raw, CaptureFailure=None)
        self.assertFalse(verify_record(record)["casePassed"])
        raw["Header"]["TransportError"] = -9223372036854775809
        self.reject(record, "Invalid signed evidence")

    def test_counter_batch_and_raw_child_numeric_fields_reject_booleans(self):
        for container, field in (("finalCounters", "Epoch"), ("finalCounters", "FailedReads"), ("batch", "LostRecords")):
            record = valid_record()
            record[container][field] = False
            self.reject(record, "Invalid unsigned evidence")
        record = failed_record()
        raw = copy.deepcopy(record["payments"][0]["Observation"]["Raw"])
        raw["Children"][0]["Ordinal"] = True
        record["failureBoundary"]["Capture"].update(State=1, Raw=raw, CaptureFailure=None)
        self.reject(record, "Invalid unsigned evidence")

    def test_final_cleanup_requires_latest_release(self):
        record = valid_record()
        record["nativeCleanup"] = copy.deepcopy(record["selectedCleanup"])
        self.reject(record, "latest released slot")

    def test_receipt_deadline_and_pause_clock_frequency_are_explicit(self):
        record = valid_record()
        interval = record["selectedPayments"][0]["ReceiptInterval"]
        interval["Observed"] = interval["Started"] + 100000001
        record["selectedPayments"][0]["Finished"] = interval["Observed"] + 1
        self.reject(record, "Request-to-receipt interval exceeds 100 ms")
        record = valid_record()
        for key in ("initialCounters", "finalCounters"):
            record[key]["LastReading"]["TimestampFrequency"] = 500000000
            for transition in record[key]["Transitions"]:
                transition["TimestampFrequency"] = 500000000
        self.reject(record, "frequency differs from pause clock")

    def test_both_failure_captures_are_required_and_ordered(self):
        record = failed_record()
        record["selectedFailureBoundary"] = None
        self.reject(record, "lost one slot")
        record = failed_record()
        record["selectedFailureBoundary"]["Capture"]["Started"] = 120
        self.reject(record, "Slot capture order")
        record = failed_record()
        record["selectedFailureBoundary"]["Capture"]["Failure"] = "diagnostic failure"
        self.reject(record, "replaced the workload failure")
        record = failed_record()
        for key in ("failureBoundary", "selectedFailureBoundary"):
            record[key]["Capture"]["TimestampFrequency"] = 500000000
        self.reject(record, "Invalid capture clock")

    def test_accepted_case_requires_controller_join_and_release(self):
        record = valid_record()
        record["controllerJoined"] = False
        self.reject(record, "native cleanup")
        record = valid_record()
        record["selectedCleanup"] = None
        self.reject(record, "Final selected receipt or cleanup")

    def test_source_batch_and_final_native_copy_are_required(self):
        record = valid_record()
        record["batch"]["UnmappedSamples"] = 1
        self.reject(record, "sample batch differs")
        record = valid_record()
        record["nativeCleanup"] = None
        self.reject(record, "Final native copy differs")

    def test_version_and_case_identity(self):
        for version in (1, 2, True, "3"):
            record = valid_record()
            record["schemaVersion"] = version
            self.reject(record, "schema version 3")
        with self.assertRaisesRegex(ValueError, "case identity"):
            verify_case(valid_record(), "0")

    def test_missing_cases_and_runtime_failure_fail_report(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = pathlib.Path(temp)
            (directory / "50.json").write_text(json.dumps(failed_record()))
            self.assertEqual(main(directory), 1)
            result = json.loads((directory / "analysis.json").read_text())
            self.assertFalse(result["passed"])
            case = next(item for item in result["cases"] if item["case"] == "50")
            self.assertTrue(case["evidenceVerified"])
            self.assertFalse(case["casePassed"])
            self.assertFalse(result["cases"][0]["evidenceVerified"])


class PauseTimingChecks(unittest.TestCase):
    def test_record_verification_rejects_incomplete_timing(self):
        record = valid_record()
        record['pauseTimingEnabled'] = True
        record['pauses'][0]['Timing'] = dict(zip(PAUSE_TIMING_FIELDS,
            (31, 32, 33, 34, 35, 36, 37, 61, 62, 63, 64, 65)))
        verify_record(record)
        record['pauses'][0]['Timing']['Completed'] = None
        with self.assertRaises(ValueError):
            verify_record(record)

    def record(self):
        timing = {key: index + 10 for index, key in enumerate(PAUSE_TIMING_FIELDS)}
        return dict(pauseTimingEnabled=True, pauses=[dict(Started=9, CallbackFinished=16,
            Resumed=22, Timing=timing)])

    def test_complete_and_legacy_timing(self):
        verify_pause_timing(self.record())
        verify_pause_timing(dict(pauses=[dict(Timing=None)]))

    def test_failed_pause_retains_only_reached_prefix(self):
        record = self.record()
        pause = record['pauses'][0]
        pause.update(CallbackFinished=None, Resumed=None)
        for key in PAUSE_TIMING_FIELDS[6:]:
            pause['Timing'][key] = None
        verify_pause_timing(record)

    def test_corrupted_phase_order_is_rejected(self):
        for field, value in [('Started', None), ('ProducerStopped', None), ('DrainAcknowledged', 1),
                             ('Completed', 23), ('SourceEnabled', True)]:
            with self.subTest(field=field):
                record = self.record()
                record['pauses'][0]['Timing'][field] = value
                with self.assertRaises(ValueError):
                    verify_pause_timing(record)

    def test_missing_or_disabled_timing_is_rejected(self):
        for enabled, timing in [(True, None), (False, self.record()['pauses'][0]['Timing'])]:
            record = self.record()
            record['pauseTimingEnabled'] = enabled
            record['pauses'][0]['Timing'] = timing
            with self.assertRaises(ValueError):
                verify_pause_timing(record)

    def test_incomplete_timing_cannot_claim_resume(self):
        record = self.record()
        record['pauses'][0]['Timing']['Completed'] = None
        with self.assertRaises(ValueError):
            verify_pause_timing(record)


class DispatchTimingAnalysisChecks(unittest.TestCase):
    @staticmethod
    def record():
        record = valid_record()
        first = dict(Sequence=1, Operation=3, Command=1, Generation=1, Submitted=10, Started=20, Finished=40, Returned=70, Failed=False)
        record.update(dispatchTimingEnabled=True, dispatchTiming=dict(Calls=1, Failed=0, Incomplete=0, Recent=[first],
            Longest=copy.deepcopy(first), LongestQueue=copy.deepcopy(first), LongestAction=copy.deepcopy(first), LongestReturn=copy.deepcopy(first)))
        return record

    def test_dispatch_phases_and_maxima_are_consistent(self):
        self.assertTrue(verify_record(self.record())["casePassed"])
        for mutate, message in (
            (lambda r: r.update(dispatchTimingEnabled=1), "not Boolean"),
            (lambda r: r["dispatchTiming"].update(Calls=0), "no dispatch observations"),
            (lambda r: r["dispatchTiming"].update(Failed=2), "counts are inconsistent"),
            (lambda r: r["dispatchTiming"].update(Failed=1), "Completed workload has failed"),
            (lambda r: r["dispatchTiming"].update(Recent=[]), "history length"),
            (lambda r: r["dispatchTiming"]["Recent"][0].update(Started=9), "start is outside"),
            (lambda r: r["dispatchTiming"]["Recent"][0].update(Finished=71), "finish is outside"),
            (lambda r: r["dispatchTiming"]["Recent"][0].update(Finished=None), "no completed action"),
            (lambda r: r["dispatchTiming"].update(LongestQueue=None), "maximum is missing"),
            (lambda r: r["dispatchTiming"]["Longest"].update(Returned=71), "changed its observation"),
        ):
            with self.subTest(message=message):
                record = self.record()
                mutate(record)
                with self.assertRaisesRegex(ValueError, message):
                    verify_record(record)

    def test_incomplete_dispatch_keeps_failure_without_inventing_action(self):
        record = self.record()
        record.update(passed=False, completed=False, failure="dispatch failed")
        timing = record["dispatchTiming"]
        timing.update(Failed=1, Incomplete=1, LongestQueue=None, LongestAction=None, LongestReturn=None)
        timing["Recent"][0].update(Started=None, Finished=None, Failed=True)
        timing["Longest"] = copy.deepcopy(timing["Recent"][0])
        from AnalyzeBoundSampledPayment import verify_dispatch_timing
        verify_dispatch_timing(record)
        timing["Incomplete"] = 0
        with self.assertRaisesRegex(ValueError, "totals differ"):
            verify_dispatch_timing(record)

    def test_disabled_dispatch_timing_has_no_observations(self):
        record = valid_record()
        record.update(dispatchTimingEnabled=False, dispatchTiming=None)
        self.assertTrue(verify_record(record)["casePassed"])
        record["dispatchTiming"] = self.record()["dispatchTiming"]
        with self.assertRaisesRegex(ValueError, "Disabled dispatch timing"):
            verify_record(record)

    def test_evicted_failure_maximum_contributes_to_totals(self):
        record = self.record()
        record.update(passed=False, completed=False, failure="dispatch failed")
        from AnalyzeBoundSampledPayment import verify_dispatch_timing
        timing = record["dispatchTiming"]
        timing.update(Calls=129, Failed=1, Incomplete=1)
        timing["Recent"] = [dict(Sequence=i, Operation=2, Command=1, Generation=1,
            Submitted=i * 1000, Started=i * 1000 + 1, Finished=i * 1000 + 2, Returned=i * 1000 + 3, Failed=False)
            for i in range(2, 130)]
        timing["Longest"] = dict(Sequence=1, Operation=2, Command=1, Generation=1,
            Submitted=1000, Started=None, Finished=None, Returned=1100, Failed=True)
        for key in ("LongestQueue", "LongestAction", "LongestReturn"):
            timing[key] = copy.deepcopy(timing["Recent"][0])
        verify_dispatch_timing(record)
        timing.update(Failed=0, Incomplete=0)
        with self.assertRaisesRegex(ValueError, "totals differ from retained observations"):
            verify_dispatch_timing(record)

    def test_evicted_started_failure_requires_queue_maximum(self):
        record = self.record()
        record.update(passed=False, completed=False, failure="dispatch failed")
        from AnalyzeBoundSampledPayment import verify_dispatch_timing
        timing = record["dispatchTiming"]
        timing.update(Calls=129, Failed=129, Incomplete=129, LongestAction=None, LongestReturn=None)
        timing["Recent"] = [dict(Sequence=i, Operation=2, Command=1, Generation=1,
            Submitted=i * 1000, Started=None, Finished=None, Returned=i * 1000 + 3, Failed=True)
            for i in range(2, 130)]
        timing["Longest"] = dict(Sequence=1, Operation=2, Command=1, Generation=1,
            Submitted=1000, Started=1001, Finished=None, Returned=1100, Failed=True)
        timing["LongestQueue"] = copy.deepcopy(timing["Longest"])
        verify_dispatch_timing(record)
        timing["LongestQueue"] = None
        with self.assertRaisesRegex(ValueError, "maximum is missing for retained observations"):
            verify_dispatch_timing(record)

    def test_unretained_incomplete_calls_must_also_be_failures(self):
        from AnalyzeBoundSampledPayment import verify_dispatch_timing
        record = self.record()
        record.update(passed=False, completed=False, failure="dispatch failed")
        timing = record["dispatchTiming"]
        timing.update(Calls=130, Failed=2, Incomplete=1)
        timing["Recent"] = [dict(Sequence=i, Operation=2, Command=1, Generation=1,
            Submitted=i * 1000, Started=i * 1000 + 1, Finished=i * 1000 + 2, Returned=i * 1000 + 3, Failed=False)
            for i in range(3, 131)]
        maximum = dict(Sequence=1, Operation=2, Command=1, Generation=1,
            Submitted=1000, Started=1001, Finished=1099, Returned=1100, Failed=True)
        for key in ("Longest", "LongestQueue", "LongestAction", "LongestReturn"):
            timing[key] = copy.deepcopy(maximum)
        verify_dispatch_timing(record)
        timing["Failed"] = 1
        with self.assertRaisesRegex(ValueError, "Unretained incomplete dispatches exceed failures"):
            verify_dispatch_timing(record)


class RejectedReceiptAnalysisChecks(unittest.TestCase):
    def test_selected_replay_checks_publication_mode_and_count(self):
        record = valid_record()
        record.update(batchServiceEnabled=False, sampleServiceCalls=len(record["samples"]))
        self.assertTrue(verify_record(record)["casePassed"])
        record["sampleServiceCalls"] += 1
        with self.assertRaisesRegex(ValueError, "exceed published samples"):
            verify_record(record)

    @staticmethod
    def record(worker):
        record = failed_record()
        record["failure"] = "System.IO.InvalidDataException: The cooperative request-to-receipt interval exceeds 100 ms or has invalid clock evidence."
        for key, started in (("failureBoundary", 200000000), ("selectedFailureBoundary", 200000002)):
            record[key]["Capture"].update(Failure=record["failure"], Started=started, Finished=started + 1)
        boundary = record["selectedFailureBoundary" if worker == 1 else "failureBoundary"]
        boundary.update(Command=8, Generation=9, Budget=500000, RequestStarted=1)
        record.update(receiptIntervalCaptureVersion=1, receiptIntervalRejection=dict(
            Request=dict(Worker=worker, Command=8, Generation=9, Budget=500000),
            Started=1, Observed=100000002, TimestampFrequency=1000000000))
        return record

    def test_rejected_receipts_verify_both_request_slots_without_acceptance(self):
        for worker in (1, 2):
            with self.subTest(worker=worker):
                self.assertFalse(verify_record(self.record(worker))["casePassed"])

    def test_rejected_receipt_precedes_first_failure_capture(self):
        for worker in (1, 2):
            with self.subTest(worker=worker):
                record = self.record(worker)
                record["receiptIntervalRejection"]["Observed"] = record["failureBoundary"]["Capture"]["Started"]
                self.assertFalse(verify_record(record)["casePassed"])
                record["receiptIntervalRejection"]["Observed"] += 1
                with self.assertRaisesRegex(ValueError, "Rejected receipt follows its failure capture"):
                    verify_record(record)

    def test_missing_rejection_and_changed_request_are_rejected(self):
        for worker in (1, 2):
            for mutate in (
                lambda r: r.update(receiptIntervalRejection=None),
                lambda r: r["receiptIntervalRejection"]["Request"].update(Worker=3),
                lambda r: r["receiptIntervalRejection"]["Request"].update(Command=7),
                lambda r: r["receiptIntervalRejection"]["Request"].update(Generation=10),
                lambda r: r["receiptIntervalRejection"]["Request"].update(Budget=1),
                lambda r: r["receiptIntervalRejection"].update(Started=2),
                lambda r: r["receiptIntervalRejection"].update(TimestampFrequency=1),
                lambda r: r["receiptIntervalRejection"].update(Observed=400000000),
                lambda r: r.update(receiptIntervalCaptureVersion=True),
                lambda r: r.update(failure="a different failure"),
            ):
                record = self.record(worker)
                mutate(record)
                with self.assertRaises(ValueError):
                    verify_record(record)

    def test_valid_interval_cannot_be_reported_as_rejected(self):
        record = self.record(2)
        record["receiptIntervalRejection"]["Observed"] = 100000001
        with self.assertRaisesRegex(ValueError, "acceptance bound"):
            verify_record(record)

    def test_rejected_request_cannot_enter_accepted_payments(self):
        for worker in (1, 2):
            record = self.record(worker)
            record["selectedPayments" if worker == 1 else "payments"].append(dict(Command=8, Generation=9))
            with self.assertRaisesRegex(ValueError, "accepted payments"):
                verify_record(record)

    def test_completed_run_cannot_carry_a_rejection(self):
        record = self.record(2)
        record.update(passed=True, completed=True)
        with self.assertRaisesRegex(ValueError, "accepted result"):
            verify_record(record)

    def test_successful_capture_version_requires_no_rejection(self):
        record = valid_record()
        record.update(receiptIntervalCaptureVersion=1, receiptIntervalRejection=None)
        self.assertTrue(verify_record(record)["casePassed"])


if __name__ == "__main__":
    unittest.main()
