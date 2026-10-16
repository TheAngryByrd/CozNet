import json
import pathlib
import subprocess
import sys
import tempfile
import unittest

import AnalyzeBoundSampleMembership as subject
import BoundSelectedPaymentAnalysisChecks as fixtures


def fixture():
    raw = fixtures.CooperativeMeasurementEvidenceChecks().finite_record()
    def reading(value):
        return dict(Before=value, NativeNanoseconds=value, After=value, TimestampFrequency=1000000000,
                    ContextIdentity='context', MonotonicOffsetNanoseconds=0, Error=None)
    raw.update(boundSampleClockEnabled=True, boundSampleClockEvidenceVersion=1,
               boundSampleClock=dict(Status='complete', Source=dict(Domain=1, UnitsPerSecond=1000000000),
                   Metadata=dict(backend='linux-perf', clock='CLOCK_MONOTONIC', clockId=1, attributeSize=96,
                                 runtime='.NET 10.0.0', architecture='X64', kernel='fixture',
                                 operatingSystem='fixture', timestampFrequency=1000000000),
                   Start=reading(0), End=reading(100), Failure=None, ObservedMappingConsistent=True,
                   Rejection=None, ErrorBoundEstablished=False))
    return raw


class BoundSampleMembershipChecks(unittest.TestCase):
    def test_replays_credit_and_preserves_unqualified_outcome(self):
        result = subject.analyze_membership(fixture())
        self.assertTrue(result['comparisonDomainAccepted'], result['reasons'])
        self.assertEqual({'Before': 0, 'Inside': 4, 'After': 0, 'BoundaryAmbiguous': 0}, result['membershipCounts'])
        self.assertEqual(dict(lower=2, upper=2), result['membershipTotals']['recordedCreditNanoseconds'])
        self.assertEqual(dict(lower=12, upper=12), result['membershipTotals']['selectedNominalNanoseconds'])
        self.assertTrue(result['workloadCompleted'])
        for name in ('predictionAccuracyEstablished', 'exactCpuAttributionEstablished',
                     'physicalClockAccuracyEstablished', 'unreceivedCreditBoundEstablished'):
            self.assertFalse(result[name])

    def test_half_open_brackets_and_ambiguity(self):
        evidence = subject.BoundMembership((10, 20), (30, 40), tuple(
            subject.BoundObservation(i, stamp, 1, 5, True, True, 2)
            for i, stamp in enumerate((9, 10, 19, 20, 29, 30, 39, 40))), 16)
        report = subject.report_membership(evidence)
        self.assertEqual(['Before', 'BoundaryAmbiguous', 'BoundaryAmbiguous', 'Inside', 'Inside',
                          'BoundaryAmbiguous', 'BoundaryAmbiguous', 'After'],
                         [r['sourceMembership'] for r in report['observations']])
        self.assertEqual(dict(lower=4, upper=12), report['membershipTotals']['recordedCreditNanoseconds'])
        exact = subject.BoundMembership((10, 10), (20, 20), (
            subject.BoundObservation(0, 10, 1, 1, True, False, 0),
            subject.BoundObservation(1, 20, 1, 1, True, False, 0)), 0)
        self.assertEqual(['Inside', 'After'], [r['sourceMembership'] for r in subject.report_membership(exact)['observations']])

    def test_accepted_credit_before_start_stays_recorded(self):
        raw = fixture()
        raw['measurement']['Window']['Start'].update(BeforeTimestamp=3, AfterTimestamp=3)
        raw['selectedWorkStarted'] = 3
        raw['measurementAssessment']['DescriptiveEstimate'].update(ElapsedLowerNanoseconds=96,
            ElapsedUpperNanoseconds=96, EffectiveLowerNanoseconds=90, EffectiveUpperNanoseconds=90)
        result = subject.analyze_membership(raw)
        self.assertTrue(result['comparisonDomainAccepted'], result['reasons'])
        self.assertEqual(1, result['recordedCreditBySourceMembership']['Before'])
        self.assertEqual(2, result['replayedCreditNanoseconds'])
        self.assertEqual(dict(lower=1, upper=1), result['membershipTotals']['recordedCreditNanoseconds'])
        self.assertEqual(2, raw['credit'])

    def test_inside_source_tail_does_not_gain_credit(self):
        raw = fixture()
        tail = dict(raw['sourceSamples'][-1], Timestamp=98, PeriodNanoseconds=1, CumulativeCpuNanoseconds=13)
        raw['sourceSamples'].append(tail)
        raw['sourceEpochSamples'].append(dict(EligibilityEpoch=2, Sample=tail))
        raw['finalCounters']['LastReading']['CpuNanoseconds'] = 13
        raw['batch'].update(Samples=5, RepresentedCpuNanoseconds=13, SelectedMethodSamples=5)
        raw['measurementAssessment']['Reasons'].append('source-tail-outside-frozen-prefix')
        result = subject.analyze_membership(raw)
        self.assertTrue(result['comparisonDomainAccepted'], result['reasons'])
        self.assertEqual(dict(lower=1, upper=1), result['membershipTotals']['unacceptedSelectedNominalNanoseconds'])
        self.assertEqual(0, result['observations'][-1]['recordedCreditNanoseconds'])
        self.assertFalse(result['observations'][-1]['acceptedInFrozenPrefix'])
        self.assertEqual(2, result['replayedCreditNanoseconds'])

    def test_unknown_clock_paths_are_rejected(self):
        for field, value in (('runtime', '.NET 11.0.0'), ('architecture', 'Arm64'), ('clockId', True),
                             ('attributeSize', 95), ('timestampFrequency', 1000), ('kernel', '')):
            raw = fixture()
            raw['boundSampleClock']['Metadata'][field] = value
            with self.subTest(field=field):
                result = subject.analyze_membership(raw)
                self.assertFalse(result['comparisonDomainAccepted'])
                self.assertIsNone(result['membershipTotals'])
                self.assertEqual([], result['observations'])

    def test_missing_inconsistent_and_disabled_clock_are_rejected(self):
        variants = [fixture() for _ in range(3)]
        variants[0].pop('boundSampleClock')
        variants[1]['boundSampleClock'].update(ObservedMappingConsistent=False, Rejection='bad clock')
        variants[2].update(boundSampleClockEnabled=False, boundSampleClock=None)
        for raw in variants:
            self.assertFalse(subject.analyze_membership(raw)['comparisonDomainAccepted'])

    def test_replay_rejects_changed_credit_loss_and_open_source(self):
        for mutate in (lambda r: r['samples'][1].update(Credit=2),
                       lambda r: r['batch'].update(LostRecords=1),
                       lambda r: r.update(closed=False),
                       lambda r: r['sourceCleanup'].update(SourceDisposed=False),
                       lambda r: r['finalCounters'].update(UnrecordedCleanup=True)):
            raw = fixture()
            mutate(raw)
            self.assertFalse(subject.analyze_membership(raw)['comparisonDomainAccepted'])

    def test_source_time_after_final_observation_is_rejected(self):
        raw = fixture()
        raw['sourceSamples'][-1]['Timestamp'] = 104
        self.assertFalse(subject.analyze_membership(raw)['comparisonDomainAccepted'])

    def test_failed_workload_diagnostics_are_retained_on_rejection(self):
        raw = fixture()
        raw.update(passed=False, completed=False, failure='retained workload failure')
        result = subject.analyze_membership(raw)
        self.assertFalse(result['comparisonDomainAccepted'])
        self.assertFalse(result['workloadPassed'])
        self.assertEqual('retained workload failure', result['workloadFailure'])
        self.assertEqual(raw['sourceSamples'], result['rawSourceSamples'])

    def test_valid_failed_workload_keeps_membership_but_not_success(self):
        raw = fixture()
        raw.update(passed=False, completed=False, verificationFailure='later verification failure')
        raw['measurement']['Failure'] = raw['verificationFailure']
        raw['measurementAssessment']['Status'] = 'invalid'
        result = subject.analyze_membership(raw)
        self.assertTrue(result['comparisonDomainAccepted'], result['reasons'])
        self.assertFalse(result['workloadPassed'])
        self.assertEqual('later verification failure', result['verificationFailure'])
        self.assertEqual(2, result['replayedCreditNanoseconds'])
        raw['batch']['LostRecords'] = 1
        self.assertFalse(subject.analyze_membership(raw)['comparisonDomainAccepted'])

    def test_malformed_shapes_produce_rejection(self):
        for raw in (None, [], '', {}, {'measurement': []}):
            self.assertFalse(subject.analyze_membership(raw)['comparisonDomainAccepted'])

    def test_cli_preserves_input_hashes_imports_and_existing_output(self):
        with tempfile.TemporaryDirectory() as directory:
            source, output = pathlib.Path(directory) / 'input.json', pathlib.Path(directory) / 'output.json'
            content = json.dumps(fixture()).encode()
            source.write_bytes(content)
            args = [sys.executable, str(pathlib.Path(subject.__file__)), str(source), str(output)]
            completed = subprocess.run(args, capture_output=True, text=True)
            self.assertEqual(0, completed.returncode, completed.stderr)
            report = json.loads(output.read_text())
            self.assertEqual(subject.hashlib.sha256(content).hexdigest(), report['inputSha256'])
            for name in ('AnalyzeBoundSampleMembership.py', 'AnalyzeBoundSelectedPayment.py',
                         'BoundSampleClockEvidence.py', 'AnalyzeSampleMembership.py'):
                self.assertEqual(subject.hashlib.sha256(pathlib.Path(subject.__file__).with_name(name).read_bytes()).hexdigest(),
                                 report['analyzerSources'][name])
            original = output.read_bytes()
            self.assertEqual(1, subprocess.run(args, capture_output=True).returncode)
            self.assertEqual(original, output.read_bytes())
            self.assertEqual(content, source.read_bytes())

    def test_cli_rejects_nonfinite_and_duplicate_json(self):
        with tempfile.TemporaryDirectory() as directory:
            source = pathlib.Path(directory) / 'input.json'
            for index, text in enumerate(('{"a":1,"a":2}', '{"a":NaN}', '{"a":1e999}', '[')):
                source.write_text(text)
                output = pathlib.Path(directory) / f'{index}.json'
                completed = subprocess.run([sys.executable, str(pathlib.Path(subject.__file__)), str(source), str(output)],
                                           capture_output=True, text=True)
                self.assertEqual(1, completed.returncode, completed.stderr)
                result = json.loads(output.read_text())
                self.assertFalse(result['comparisonDomainAccepted'])
                self.assertTrue(result['reasons'])

    def test_cli_retains_rejection_for_zero_frequency_arithmetic(self):
        raw = fixture()
        raw['timestampFrequency'] = 0
        for name in ('Start', 'End'):
            raw['measurement']['Window'][name]['TimestampFrequency'] = 0
        raw['boundSampleClock'].update(ObservedMappingConsistent=False, Rejection='invalid frequency')
        with tempfile.TemporaryDirectory() as directory:
            source, output = pathlib.Path(directory) / 'input.json', pathlib.Path(directory) / 'output.json'
            source.write_text(json.dumps(raw))
            completed = subprocess.run([sys.executable, str(pathlib.Path(subject.__file__)), str(source), str(output)],
                                       capture_output=True, text=True)
            self.assertEqual(1, completed.returncode)
            self.assertEqual('', completed.stderr)
            report = json.loads(output.read_text())
            self.assertFalse(report['comparisonDomainAccepted'])
            self.assertTrue(report['reasons'])
            self.assertIsNone(report['membershipTotals'])
            self.assertEqual(raw['boundSampleClock'], report['clockEvidence'])


if __name__ == '__main__':
    unittest.main()
