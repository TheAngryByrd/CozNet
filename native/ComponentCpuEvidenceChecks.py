import copy
import unittest

from AnalyzeBoundSampledPayment import verify_component_cpu


class ComponentCpuChecks(unittest.TestCase):
    def record(self):
        all_buckets = [dict(Activity=i, Calls=2, Failures=0, CpuNanoseconds=5, MaximumNanoseconds=3) for i in range(7)]
        measured = [dict(Activity=i, Calls=1, Failures=0, CpuNanoseconds=2, MaximumNanoseconds=2) for i in range(7)]
        return dict(componentCpuEnabled=True, componentCpuEvidenceVersion=1, finiteMeasurementEnabled=True,
                    completed=True, serviceCadence=dict(Checks=12, Skipped=2, Executions=10),
                    componentCpu=dict(All=all_buckets, Measurement=measured, Active=False, Failure=None))

    def test_valid_and_legacy(self):
        verify_component_cpu(self.record())
        verify_component_cpu({})
        verify_component_cpu(dict(componentCpuEnabled=False, componentCpuEvidenceVersion=1, componentCpu=None))

    def test_corruption(self):
        mutations = [
            lambda r: r.pop('componentCpuEnabled'),
            lambda r: r.update(componentCpuEnabled=1),
            lambda r: r.update(componentCpuEvidenceVersion=True),
            lambda r: r.update(componentCpuEnabled=False),
            lambda r: r.update(finiteMeasurementEnabled=False),
            lambda r: r['componentCpu'].update(Active=True),
            lambda r: r['componentCpu'].update(Failure='clock failed'),
            lambda r: r['componentCpu']['All'].pop(),
            lambda r: r['componentCpu']['All'][0].update(Activity=True),
            lambda r: r['componentCpu']['All'][0].update(Calls=True),
            lambda r: r['componentCpu']['All'][0].update(CpuNanoseconds=7),
            lambda r: r['componentCpu']['All'][0].update(Failures=3),
            lambda r: r['componentCpu']['Measurement'][0].update(Calls=3),
            lambda r: r['componentCpu']['Measurement'][0].update(CpuNanoseconds=4, MaximumNanoseconds=4),
            lambda r: r['serviceCadence'].update(Checks=11),
            lambda r: r['serviceCadence'].update(Skipped=3),
            lambda r: r['serviceCadence'].update(Executions=11),
        ]
        for mutate in mutations:
            with self.subTest(mutation=mutations.index(mutate)):
                record = copy.deepcopy(self.record())
                verify_component_cpu(record)
                mutate(record)
                with self.assertRaises(ValueError):
                    verify_component_cpu(record)

    def test_failed_capture_retains_partial_counts(self):
        record = self.record()
        record['completed'] = False
        record['componentCpu'].update(Failure='clock failed', Active=True)
        record['serviceCadence']['Checks'] = 13
        verify_component_cpu(record)
        record['serviceCadence']['Checks'] = 11
        with self.assertRaises(ValueError):
            verify_component_cpu(record)

    def test_completed_rejects_failed_body_calls(self):
        for activity in range(7):
            with self.subTest(activity=activity):
                record = self.record()
                record['componentCpu']['All'][activity]['Failures'] = 1
                with self.assertRaisesRegex(ValueError, 'Completed workload has failed component calls'):
                    verify_component_cpu(record)
                record['completed'] = False
                verify_component_cpu(record)


if __name__ == '__main__':
    unittest.main()
