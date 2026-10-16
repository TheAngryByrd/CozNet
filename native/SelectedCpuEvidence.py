from AnalyzeBoundSampledPayment import count, require


def verify_selected_cpu(record):
    fields = ('selectedCpuEnabled', 'selectedCpuEvidenceVersion', 'selectedCpu')
    if not any(key in record for key in fields):
        return None
    require(all(key in record for key in fields), 'Selected CPU evidence is incomplete')
    require(type(record['selectedCpuEvidenceVersion']) is int and record['selectedCpuEvidenceVersion'] == 1,
            'Unsupported selected CPU evidence version')
    enabled, capture = record['selectedCpuEnabled'], record['selectedCpu']
    require(type(enabled) is bool, 'Selected CPU activation is not Boolean')
    if not enabled:
        require(capture is None, 'Disabled selected CPU capture contains readings')
        return None
    require(isinstance(capture, dict), 'Selected CPU capture is missing')
    status = capture['Status']
    require(status in ('prepared', 'running', 'complete', 'failed'), 'Unknown selected CPU capture status')
    failure = capture['Failure']
    require((isinstance(failure, str) and bool(failure)) if status == 'failed' else failure is None,
            'Selected CPU failure state differs')
    start, end = capture['StartReading'], capture['EndReading']
    require(status != 'prepared' or start is None and end is None, 'Prepared selected CPU capture has readings')
    require(status != 'running' or start is not None and end is None, 'Running selected CPU capture has invalid endpoints')
    require(status != 'complete' or start is not None and end is not None, 'Complete selected CPU capture lacks endpoints')
    require(record['completed'] is not True or status == 'complete', 'Completed work lacks complete selected CPU capture')
    for item in (start, end):
        if item is not None:
            require(count(item['BeforeTimestamp']) <= count(item['AfterTimestamp']), 'Selected CPU timestamp bracket regressed')
            count(item['CpuNanoseconds'])
    if start is not None:
        require(count(record['selectedWorkStarted']) <= start['BeforeTimestamp'], 'Selected CPU start precedes selected work')
    if end is None:
        return None
    require(start is not None and start['AfterTimestamp'] <= end['BeforeTimestamp'] and
            start['CpuNanoseconds'] <= end['CpuNanoseconds'], 'Selected CPU observations regressed')
    require(record['selectedWorkFinished'] is not None and end['AfterTimestamp'] <= count(record['selectedWorkFinished']),
            'Selected CPU end exceeds selected work')
    frequency = count(record['timestampFrequency'])
    require(frequency > 0, 'Selected CPU timestamp frequency is zero')
    return dict(cpuNanoseconds=end['CpuNanoseconds'] - start['CpuNanoseconds'],
                elapsedLowerNanoseconds=(end['BeforeTimestamp'] - start['AfterTimestamp']) * 1000000000 // frequency,
                elapsedUpperNanoseconds=((end['AfterTimestamp'] - start['BeforeTimestamp']) * 1000000000 + frequency - 1) // frequency)
