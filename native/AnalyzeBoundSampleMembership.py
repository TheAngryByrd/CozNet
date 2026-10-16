import argparse
import hashlib
import json
import pathlib
import sys
from dataclasses import dataclass

from AnalyzeBoundSelectedPayment import verify_record, verify_counters, verify_pauses, verify_source_batch
from AnalyzeCpuCounters import require, timestamp
from AnalyzeSampleMembership import ASSUMPTION, classify, finite_float, reject_constant


POLICY = 'linux-monotonic-bound-membership-v1'


@dataclass(frozen=True)
class BoundObservation:
    index: int
    source: int
    epoch: int
    nominal: int
    selected: bool
    accepted: bool
    credit: int


@dataclass(frozen=True)
class BoundMembership:
    start: tuple
    end: tuple
    samples: tuple
    credit: int


def parse_membership(record):
    require(type(record) is dict, 'Bound membership requires an object record')
    replay = verify_record(record)
    require(record.get('finiteMeasurementEnabled') is True, 'Bound membership requires finite measurement')
    require(replay['boundSampleClock'] is not None and replay['boundSampleClock']['status'] == 'consistent',
            'Bound membership requires consistent clock evidence')
    metadata = record['boundSampleClock']['Metadata']
    require(isinstance(metadata, dict), 'Missing clock metadata')
    require(metadata['backend'] == 'linux-perf' and metadata['clock'] == 'CLOCK_MONOTONIC'
            and type(metadata['clockId']) is int and metadata['clockId'] == 1
            and type(metadata['attributeSize']) is int and metadata['attributeSize'] == 96,
            'Unsupported perf clock configuration')
    require(metadata['runtime'] == '.NET 10.0.0' and metadata['architecture'] == 'X64',
            'Unsupported runtime clock path')
    for field in ('kernel', 'operatingSystem'):
        require(type(metadata[field]) is str and bool(metadata[field].strip()), 'Missing ' + field)
    require(type(metadata['timestampFrequency']) is int and metadata['timestampFrequency'] == 1000000000
            and record['timestampFrequency'] == 1000000000, 'Identity conversion requires nanosecond timestamps')
    window = record['measurement']['Window']
    require(window['Status'] == 2, 'Bound membership requires frozen endpoints')
    boundaries = tuple(tuple(timestamp(raw[field]) for field in ('BeforeTimestamp', 'AfterTimestamp'))
                       for raw in (window['Start'], window['End']))
    require(boundaries[0][0] <= boundaries[0][1] <= boundaries[1][0] <= boundaries[1][1],
            'Measurement brackets overlap or regress')
    require(record['closed'] is True and all(record['sourceCleanup'][key] is True for key in
            ('SourceCreated', 'DisableSucceeded', 'CollectorJoined', 'SourceDisposed')),
            'Bound membership requires closed source evidence')
    source = record['sourceSamples']
    verify_counters(record, source[-1]['CumulativeCpuNanoseconds'] if source else 0, verify_pauses(record))
    verify_source_batch(record)
    last_observation = timestamp(record['finalCounters']['LastReading']['After'])
    accepted = record['samples']
    rows = []
    for index, raw in enumerate(source):
        value = timestamp(raw['Timestamp'])
        require(value <= last_observation, 'Source timestamp exceeds final source observation')
        in_prefix = index < len(accepted)
        rows.append(BoundObservation(index, value, record['sourceEpochSamples'][index]['EligibilityEpoch'],
                    raw['PeriodNanoseconds'], raw['Method'] is not None, in_prefix,
                    accepted[index]['Credit'] if in_prefix else 0))
    return BoundMembership(*boundaries, tuple(rows), record['credit'])


def report_membership(evidence):
    categories = ('Before', 'Inside', 'After', 'BoundaryAmbiguous')
    counts = dict.fromkeys(categories, 0)
    credits = dict.fromkeys(categories, 0)
    groups = {}
    observations = []
    totals = {name: dict(lower=0, upper=0) for name in
              ('samples', 'nominalNanoseconds', 'selectedSamples', 'selectedNominalNanoseconds',
               'acceptedSamples', 'unacceptedSelectedNominalNanoseconds', 'recordedCreditNanoseconds')}
    for row in evidence.samples:
        membership = classify(evidence, row.source)
        counts[membership] += 1
        credits[membership] += row.credit
        key = membership, row.accepted, row.selected
        group = groups.setdefault(key, dict(sourceMembership=membership, acceptedInFrozenPrefix=row.accepted,
                                 selected=row.selected, samples=0, nominalNanoseconds=0, recordedCreditNanoseconds=0))
        group['samples'] += 1
        group['nominalNanoseconds'] += row.nominal
        group['recordedCreditNanoseconds'] += row.credit
        observations.append(dict(sourceIndex=row.index, sourceTimestamp=row.source, eligibilityEpoch=row.epoch,
                                 nominalNanoseconds=row.nominal, selected=row.selected,
                                 acceptedInFrozenPrefix=row.accepted, recordedCreditNanoseconds=row.credit,
                                 sourceMembership=membership))
        if membership in ('Inside', 'BoundaryAmbiguous'):
            amounts = (1, row.nominal, int(row.selected), row.nominal if row.selected else 0,
                       int(row.accepted), row.nominal if row.selected and not row.accepted else 0, row.credit)
            for name, amount in zip(totals, amounts):
                totals[name]['upper'] += amount
                if membership == 'Inside':
                    totals[name]['lower'] += amount
    require(sum(credits.values()) == evidence.credit, 'Membership partition changed recorded credit')
    return dict(startBracket=list(evidence.start), endBracket=list(evidence.end), observations=observations,
                membershipCounts=counts, membershipTotals=totals, sampleAccountingGroups=list(groups.values()),
                recordedCreditBySourceMembership=credits, replayedCreditNanoseconds=evidence.credit)


def analyze_membership(record):
    result = dict(policy=POLICY, assumption=ASSUMPTION, comparisonDomainAccepted=False, reasons=[],
                  observations=[], membershipCounts=None, membershipTotals=None,
                  physicalClockAccuracyEstablished=False, exactEventOrderingEstablished=False,
                  exactCpuAttributionEstablished=False, unreceivedCreditBoundEstablished=False,
                  predictionAccuracyEstablished=False)
    try:
        evidence = parse_membership(record)
        result.update(report_membership(evidence), comparisonDomainAccepted=True)
    except (ValueError, KeyError, TypeError, IndexError, AttributeError, ArithmeticError) as error:
        result['reasons'] = [str(error)]
    if type(record) is dict:
        result.update(clockEvidence=record.get('boundSampleClock'), measurementEvidence=record.get('measurement'),
                      rawSourceSamples=record.get('sourceSamples'), rawAcceptedSamples=record.get('samples'),
                      rawSourceEpochSamples=record.get('sourceEpochSamples'), workloadCompleted=record.get('completed'),
                      workloadPassed=record.get('passed'), sourceClosed=record.get('closed'),
                      workloadFailure=record.get('failure'), verificationFailure=record.get('verificationFailure'),
                      cleanupErrors=record.get('cleanupErrors'))
    return result


def unique_fields(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'Duplicate JSON field: ' + key)
        result[key] = value
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description='Classify bound source timestamps without changing recorded credit.')
    parser.add_argument('capture', type=pathlib.Path)
    parser.add_argument('output', type=pathlib.Path)
    args = parser.parse_args(argv)
    content = None
    try:
        content = args.capture.read_bytes()
        record = json.loads(content.decode('utf-8-sig'), parse_constant=reject_constant,
                            parse_float=finite_float, object_pairs_hook=unique_fields)
        result = analyze_membership(record)
    except (OSError, ValueError, ArithmeticError) as error:
        result = analyze_membership(None)
        result['reasons'] = [str(error)]
    root = pathlib.Path(__file__).resolve().parent
    sources = {pathlib.Path(module.__file__).resolve() for module in tuple(sys.modules.values())
               if getattr(module, '__file__', None) and pathlib.Path(module.__file__).resolve().parent == root}
    result.update(inputFile=args.capture.name,
                  inputSha256=hashlib.sha256(content).hexdigest() if content is not None else None,
                  analyzerSources={path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(sources)})
    try:
        with args.output.open('x', encoding='utf-8') as stream:
            json.dump(result, stream, indent=2, allow_nan=False)
            stream.write('\n')
    except OSError as error:
        parser.exit(1, f'Cannot create membership report: {error}\n')
    return 0 if result['comparisonDomainAccepted'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
