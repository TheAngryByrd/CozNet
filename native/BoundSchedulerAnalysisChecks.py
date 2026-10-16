import json
import pathlib
import tempfile
import unittest
from unittest.mock import patch

from AnalyzeBoundScheduler import analyze_record, main, overlap_bounds
from BoundSchedulerEvidence import native_window, verify_bound_scheduler
import BoundSelectedPaymentAnalysisChecks as fixtures


def scheduler_record():
    record = fixtures.PeerProgressChecks().record()
    owners = [dict(Process=1, Thread=4), dict(Process=1, Thread=2), dict(Process=1, Thread=3)]
    streams = [dict(ProcessId=owner['Process'], ThreadId=owner['Thread'], EnableBefore=1, EnableAfter=1,
                    DisableBefore=150, DisableAfter=151, FinalLost=0, Failures=[], Records=[
                        dict(Kind=1, Flags=8192, TimestampNanoseconds=75, Lost=0),
                        dict(Kind=0, Flags=0, TimestampNanoseconds=95, Lost=0)]) for owner in owners]
    record.update(schedulerTiming=True, schedulerOwners=owners, schedulerCapture=dict(
        Stopped=True, Failures=[], ClockDomain='linux-clock-monotonic-nanoseconds', Captures=streams,
        Begin=dict(Before=5, After=5, Nanoseconds=5, Frequency=1000000000, Failure=None),
        End=dict(Before=140, After=140, Nanoseconds=140, Frequency=1000000000, Failure=None)))
    return record


class BoundSchedulerAnalysisChecks(unittest.TestCase):
    def test_full_replay_binds_marker_query_and_closed_overlap(self):
        result = analyze_record(scheduler_record())
        self.assertTrue(result['evidenceVerified'])
        self.assertTrue(result['casePassed'])
        self.assertEqual(result['state'], 'bounded')
        window, = result['windows']
        self.assertEqual((window['markerQueryStarted'], window['markerQueryFinished']), (81, 82))
        self.assertEqual(window['marker'], dict(Generation=2, Ordinal=0, Phase=1))
        self.assertEqual([role['threadId'] for role in window['roles']], [4, 2, 3])
        for role in window['roles']:
            self.assertEqual(role['observedSwitchedOutLowerNanoseconds'], 1)
            self.assertEqual(role['observedSwitchedOutUpperNanoseconds'], 1)
            self.assertTrue(role['outerWindowInsideObservedSwitchOut'])

    def test_failed_workload_stays_failed(self):
        record = scheduler_record()
        failed = fixtures.failed_record()
        for key in ('passed', 'completed', 'failure', 'failureBoundary', 'selectedFailureBoundary'):
            record[key] = failed[key]
        result = analyze_record(record)
        self.assertTrue(result['evidenceVerified'])
        self.assertFalse(result['casePassed'])
        self.assertFalse(result['completed'])

    def test_owner_swaps_duplicates_and_sample_identity_reject(self):
        mutations = [lambda r: r['schedulerOwners'].reverse(),
                     lambda r: r['schedulerCapture']['Captures'].reverse(),
                     lambda r: r['schedulerOwners'][0].update(Thread=3),
                     lambda r: r['schedulerCapture']['Captures'][0].update(ThreadId=3),
                     lambda r: r['peerOwner'].update(Thread=9),
                     lambda r: r['sourceSamples'][0].update(ThreadId=9)]
        for index, mutate in enumerate(mutations):
            record = scheduler_record()
            mutate(record)
            with self.subTest(index=index), self.assertRaises(ValueError):
                verify_bound_scheduler(record)

    def test_loss_types_missing_anchors_and_clock_inconsistency_reject(self):
        mutations = [lambda r: r.update(schedulerTiming=1),
                     lambda r: r['schedulerCapture'].update(Stopped=False),
                     lambda r: r['schedulerCapture'].update(Begin=None),
                     lambda r: r['schedulerCapture']['End'].update(Nanoseconds=141),
                     lambda r: r['schedulerCapture']['Begin'].update(Frequency=True),
                     lambda r: r['schedulerCapture']['Captures'][0].update(FinalLost=1),
                     lambda r: r['schedulerCapture']['Captures'][0].update(ThreadId=True),
                     lambda r: r['schedulerCapture']['Captures'][0]['Records'][0].update(Lost=1),
                     lambda r: r['schedulerCapture']['Captures'][0]['Records'][0].update(Kind=True)]
        for index, mutate in enumerate(mutations):
            record = scheduler_record()
            mutate(record)
            with self.subTest(index=index), self.assertRaises(ValueError):
                verify_bound_scheduler(record)

    def test_source_samples_outside_collection_reject(self):
        for timestamp in (0, 151):
            record = scheduler_record()
            record['sourceSamples'][0]['Timestamp'] = timestamp
            with self.subTest(timestamp=timestamp), self.assertRaisesRegex(ValueError, 'Source sample lies outside'):
                verify_bound_scheduler(record)

    def test_query_outside_anchors_rejects(self):
        record = scheduler_record()
        record['schedulerCapture']['End'].update(Before=80, After=80, Nanoseconds=80)
        with self.assertRaisesRegex(ValueError, 'outside managed clock anchors'):
            analyze_record(record)

    def test_uncertain_offset_bounds_and_half_open_end(self):
        clock = dict(frequency=1000000000, offsetLowerNumerator=-2000000000,
                     offsetUpperNumerator=2000000000)
        window = native_window(80, 90, clock)
        self.assertEqual(window, dict(earliestStart=78, latestStart=82, earliestEnd=88,
                                     latestEnd=92, durationUpperNanoseconds=10))
        result = overlap_bounds([dict(start=80, end=90)], window)
        self.assertEqual(result['observedSwitchedOutLowerNanoseconds'], 6)
        self.assertEqual(result['observedSwitchedOutUpperNanoseconds'], 10)
        self.assertFalse(result['outerWindowInsideObservedSwitchOut'])
        clock.update(offsetLowerNumerator=0, offsetUpperNumerator=0)
        self.assertTrue(overlap_bounds([dict(start=80, end=90)], native_window(80, 90, clock))[
            'outerWindowInsideObservedSwitchOut'])
        point = overlap_bounds([dict(start=80, end=90)], native_window(90, 90, clock))
        self.assertFalse(point['outerWindowInsideObservedSwitchOut'])
        self.assertEqual(point['observedSwitchedOutUpperNanoseconds'], 0)

    def test_censored_stream_has_no_closed_overlap(self):
        record = scheduler_record()
        record['schedulerCapture']['Captures'][2]['Records'].pop()
        peer = analyze_record(record)['windows'][0]['roles'][2]
        self.assertTrue(peer['rightCensored'])
        self.assertEqual(peer['observedSwitchedOutLowerNanoseconds'], 0)
        self.assertEqual(peer['observedSwitchedOutUpperNanoseconds'], 0)
        self.assertFalse(peer['outerWindowInsideObservedSwitchOut'])

    def test_fractional_conversion_rounds_outward(self):
        clock = dict(frequency=3, offsetLowerNumerator=-1, offsetUpperNumerator=2)
        self.assertEqual(native_window(3, 4, clock), dict(
            earliestStart=999999999, latestStart=1000000001,
            earliestEnd=1333333333, latestEnd=1333333334, durationUpperNanoseconds=333333334))

    def test_uncertain_anchors_intersect_instead_of_union(self):
        record = scheduler_record()
        record['schedulerCapture']['Begin'].update(Before=3, After=7, Nanoseconds=5)
        record['schedulerCapture']['End'].update(Before=138, After=142, Nanoseconds=141)
        clock = verify_bound_scheduler(record)
        self.assertEqual(clock['offsetLowerNumerator'], -1000000000)
        self.assertEqual(clock['offsetUpperNumerator'], 2000000000)
        self.assertEqual(clock['offsetDenominator'], 1000000000)

    def test_failed_debt_windows_keep_later_marker_times(self):
        record = fixtures.PeerProgressChecks().failed_debt_record()
        for key in ('schedulerTiming', 'schedulerCapture', 'schedulerOwners'):
            record[key] = scheduler_record()[key]
        fixtures.verify_peer_progress(record)
        verified = dict(scheduler=verify_bound_scheduler(record), casePassed=False, completed=False)
        with patch('AnalyzeBoundScheduler.verify_record', return_value=verified) as replay:
            result = analyze_record(record)
        replay.assert_called_once_with(record)
        windows = {window['name']: window for window in result['windows']}
        self.assertEqual(set(windows), {'submitQuery', 'debtQuery', 'requestToDebt', 'submitQueryToDebtQuery'})
        earlier = windows['requestToDebt']
        self.assertEqual((earlier['managedStart'], earlier['managedFinish']), (90, 100))
        self.assertEqual((earlier['markerQueryStarted'], earlier['markerQueryFinished']), (101, 102))
        self.assertEqual(earlier['marker'], dict(Generation=3, Ordinal=1, Phase=5))
        between = windows['submitQueryToDebtQuery']
        self.assertEqual((between['managedStart'], between['managedFinish']), (92, 101))

    def test_different_requests_have_no_cross_request_window(self):
        record = fixtures.PeerProgressChecks().failed_debt_record()
        for key in ('schedulerTiming', 'schedulerCapture', 'schedulerOwners'):
            record[key] = scheduler_record()[key]
        record['peerSubmitProgress'] = fixtures.PeerProgressChecks().record()['peerSubmitProgress']
        with self.assertRaisesRegex(ValueError, 'Peer submit progress is not the latest request'):
            fixtures.verify_peer_progress(record)
        verified = dict(scheduler=verify_bound_scheduler(record), casePassed=False, completed=False)
        with patch('AnalyzeBoundScheduler.verify_record', return_value=verified):
            result = analyze_record(record)
        self.assertEqual([window['name'] for window in result['windows']],
                         ['submitQuery', 'debtQuery', 'requestToDebt'])
        self.assertEqual([window['command'] for window in result['windows']], [2, 3, 3])

    def test_disabled_capture_rejects_hidden_evidence(self):
        record = scheduler_record()
        record['schedulerTiming'] = False
        with self.assertRaisesRegex(ValueError, 'Disabled scheduler contains evidence'):
            verify_bound_scheduler(record)
        record.update(schedulerCapture=None, schedulerOwners=None)
        result = analyze_record(record)
        self.assertEqual(result['reason'], 'scheduler-disabled')
        self.assertEqual(result['windows'], [])

    def test_zero_speedup_reports_no_query_windows(self):
        record = scheduler_record()
        record.update(percent=0, credit=0, paid=0, selectedPaid=0, payments=[], selectedPayments=[], pauses=[],
                      selectedReceipt=None, selectedCleanup=None, nativeCleanup=None, peerSubmitProgress=None)
        for item in record['sourceEpochSamples']:
            item['EligibilityEpoch'] = 1
        for item in record['samples']:
            item.update(EligibilityEpoch=1, Credit=0)
        for key in ('selectedPeak', 'peerPeak', 'finalDebt'):
            record[key] = fixtures.debt(110, 0, 0, 0, 0)
        final = record['finalCounters']
        final.update(Epoch=1, Transitions=[final['Transitions'][0], dict(final['Transitions'][-1], Epoch=1)])
        final['LastReading']['Epoch'] = 1
        result = analyze_record(record)
        self.assertTrue(result['casePassed'])
        self.assertTrue(result['evidenceVerified'])
        self.assertEqual(result['state'], 'unavailable')
        self.assertEqual(result['reason'], 'no-peer-query-windows')
        self.assertEqual(result['windows'], [])

    def test_cli_retains_invalid_evidence_as_unavailable(self):
        record = scheduler_record()
        record['schedulerCapture']['Captures'][0]['FinalLost'] = 1
        with tempfile.TemporaryDirectory() as directory:
            source, output = (pathlib.Path(directory) / name for name in ('source.json', 'output.json'))
            source.write_text(json.dumps(record), encoding='utf-8')
            self.assertEqual(main(source, output), 1)
            result = json.loads(output.read_text(encoding='utf-8'))
            self.assertFalse(result['evidenceVerified'])
            self.assertEqual(result['state'], 'unavailable')


if __name__ == '__main__':
    unittest.main()
