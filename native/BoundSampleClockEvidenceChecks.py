import copy
import unittest

from BoundSampleClockEvidence import verify_bound_sample_clock


def fixture():
    def reading(before):
        return dict(Before=before, NativeNanoseconds=before + 1, After=before + 2,
                    TimestampFrequency=1000000000, ContextIdentity='clock-context',
                    MonotonicOffsetNanoseconds=0, Error=None)
    return dict(completed=True, passed=True, finiteMeasurementEnabled=True,
                boundSampleClockEnabled=True, boundSampleClockEvidenceVersion=1,
                boundSampleClock=dict(Status='complete', Source=dict(Domain=1, UnitsPerSecond=1000000000),
                    Metadata={}, Start=reading(10), End=reading(40), Failure=None,
                    ObservedMappingConsistent=True, Rejection=None, ErrorBoundEstablished=False),
                measurement=dict(Window=dict(
                    Start=dict(BeforeTimestamp=12, AfterTimestamp=14, TimestampFrequency=1000000000),
                    End=dict(BeforeTimestamp=38, AfterTimestamp=40, TimestampFrequency=1000000000))))


class BoundSampleClockEvidenceChecks(unittest.TestCase):
    def test_legacy_and_disabled(self):
        self.assertIsNone(verify_bound_sample_clock({}))
        raw = fixture()
        raw.update(boundSampleClockEnabled=False, boundSampleClock=None)
        self.assertEqual('disabled', verify_bound_sample_clock(raw)['status'])

    def test_enclosing_readings_and_failed_work_are_independent(self):
        for completed in (True, False):
            raw = fixture()
            raw.update(completed=completed, passed=completed)
            result = verify_bound_sample_clock(raw)
            self.assertEqual('consistent', result['status'])
            self.assertFalse(result['errorBoundEstablished'])
            self.assertFalse(result['predictionAccuracyEstablished'])

    def test_declarations_are_strict(self):
        for field in ('boundSampleClockEnabled', 'boundSampleClockEvidenceVersion', 'boundSampleClock'):
            raw = fixture()
            del raw[field]
            with self.subTest(missing=field), self.assertRaises(ValueError):
                verify_bound_sample_clock(raw)
        for field, values in (('boundSampleClockEnabled', (None, 1, 'true')),
                              ('boundSampleClockEvidenceVersion', (True, 0, 2, '1')),
                              ('finiteMeasurementEnabled', (False, 1, None))):
            for value in values:
                raw = fixture()
                raw[field] = value
                with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                    verify_bound_sample_clock(raw)

    def test_raw_integers_are_strict(self):
        for field in ('Before', 'NativeNanoseconds', 'After', 'TimestampFrequency', 'MonotonicOffsetNanoseconds'):
            for value in (True, 1.0, '1', 1 << 63, -(1 << 63) - 1):
                raw = fixture()
                raw['boundSampleClock']['Start'][field] = value
                with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                    verify_bound_sample_clock(raw)

    def test_inconsistent_evidence_is_rejected_without_failing_work(self):
        mutations = [
            lambda r: r['boundSampleClock']['End'].update(ContextIdentity='other'),
            lambda r: r['boundSampleClock']['End'].update(TimestampFrequency=1000),
            lambda r: r['boundSampleClock']['Start'].update(MonotonicOffsetNanoseconds=1),
            lambda r: r['boundSampleClock']['Start'].update(Error='backend clock error'),
            lambda r: r['boundSampleClock']['Start'].update(NativeNanoseconds=9),
            lambda r: r['boundSampleClock'].update(End=copy.deepcopy(r['boundSampleClock']['Start'])),
            lambda r: r.update(measurement=None),
            lambda r: r['measurement']['Window'].update(Start=None),
            lambda r: r['measurement']['Window']['Start'].update(BeforeTimestamp=11),
            lambda r: r['measurement']['Window']['End'].update(AfterTimestamp=41),
        ]
        for index, mutate in enumerate(mutations):
            raw = fixture()
            mutate(raw)
            with self.subTest(index=index):
                with self.assertRaises(ValueError):
                    verify_bound_sample_clock(raw)
                raw['boundSampleClock'].update(ObservedMappingConsistent=False, Rejection='inconsistent')
                self.assertEqual('rejected', verify_bound_sample_clock(raw)['status'])
                self.assertTrue(raw['passed'])

    def test_terminal_failure_and_unavailable_are_retained(self):
        for state in ('not-started', 'started', 'failed', 'unavailable'):
            raw = fixture()
            raw['completed'] = False
            capture = raw['boundSampleClock']
            capture.update(Status=state, End=None, ObservedMappingConsistent=False, Rejection='reason')
            if state in ('not-started', 'unavailable'):
                capture.update(Source=None, Metadata=None, Start=None)
            if state == 'failed':
                capture['Failure'] = 'reason'
            self.assertEqual(state, verify_bound_sample_clock(raw)['captureStatus'])
            if state == 'failed':
                capture['Rejection'] = 'lost backend failure'
                with self.assertRaises(ValueError):
                    verify_bound_sample_clock(raw)
            if state in ('not-started', 'started'):
                raw['completed'] = True
                with self.assertRaises(ValueError):
                    verify_bound_sample_clock(raw)

    def test_false_accuracy_claim_is_rejected(self):
        raw = fixture()
        raw['boundSampleClock']['ErrorBoundEstablished'] = True
        with self.assertRaises(ValueError):
            verify_bound_sample_clock(raw)

    def test_empty_backend_error_remains_a_diagnostic_rejection(self):
        raw = fixture()
        raw['boundSampleClock']['Start']['Error'] = ''
        raw['boundSampleClock'].update(ObservedMappingConsistent=False, Rejection='')
        result = verify_bound_sample_clock(raw)
        self.assertEqual('rejected', result['status'])
        self.assertEqual('', result['rejection'])
        self.assertTrue(raw['passed'])


if __name__ == '__main__':
    unittest.main()
