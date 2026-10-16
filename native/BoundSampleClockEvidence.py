def require(condition, message):
    if not condition:
        raise ValueError(message)


def integer(value):
    require(type(value) is int and -(1 << 63) <= value < (1 << 63), 'Invalid sample clock integer')
    return value


def reading(raw):
    if raw is None:
        return None
    require(isinstance(raw, dict), 'Invalid sample clock reading')
    for field in ('Before', 'NativeNanoseconds', 'After', 'TimestampFrequency'):
        integer(raw[field])
    require(raw['ContextIdentity'] is None or type(raw['ContextIdentity']) is str, 'Invalid clock context')
    if raw['MonotonicOffsetNanoseconds'] is not None:
        integer(raw['MonotonicOffsetNanoseconds'])
    require(raw['Error'] is None or type(raw['Error']) is str, 'Invalid clock error')
    return raw


def consistent(source, start, end, window):
    if source is None or source['Domain'] != 1 or source['UnitsPerSecond'] != 1000000000:
        return False
    if start is None or end is None or window is None or window['Start'] is None or window['End'] is None:
        return False
    for item in (start, end):
        frequency = item['TimestampFrequency']
        if (item['Error'] is not None or not item['ContextIdentity'] or not item['ContextIdentity'].strip()
                or item['MonotonicOffsetNanoseconds'] != 0 or frequency <= 0 or item['Before'] < 0
                or item['NativeNanoseconds'] < 0 or item['After'] < item['Before']
                or not item['Before'] * 1000000000 <= item['NativeNanoseconds'] * frequency <= item['After'] * 1000000000):
            return False
    first, last = window['Start'], window['End']
    for boundary in (first, last):
        for field in ('BeforeTimestamp', 'AfterTimestamp', 'TimestampFrequency'):
            integer(boundary[field])
    return (start['ContextIdentity'] == end['ContextIdentity']
            and start['TimestampFrequency'] == end['TimestampFrequency'] == first['TimestampFrequency'] == last['TimestampFrequency']
            and start['After'] <= end['Before'] and start['NativeNanoseconds'] <= end['NativeNanoseconds']
            and start['After'] <= first['BeforeTimestamp'] and last['AfterTimestamp'] <= end['Before'])


def verify_bound_sample_clock(record):
    fields = ('boundSampleClockEnabled', 'boundSampleClockEvidenceVersion', 'boundSampleClock')
    if not any(field in record for field in fields):
        return None
    require(all(field in record for field in fields), 'Incomplete bound sample clock declaration')
    require(type(record['boundSampleClockEvidenceVersion']) is int and record['boundSampleClockEvidenceVersion'] == 1,
            'Unsupported bound sample clock version')
    enabled, capture = record['boundSampleClockEnabled'], record['boundSampleClock']
    require(type(enabled) is bool, 'Sample clock activation is not Boolean')
    if not enabled:
        require(capture is None, 'Disabled sample clock capture contains evidence')
        return dict(status='disabled', errorBoundEstablished=False, predictionAccuracyEstablished=False)
    require(record.get('finiteMeasurementEnabled') is True and isinstance(capture, dict),
            'Sample clock capture requires finite measurement and evidence')
    status = capture['Status']
    require(status in ('not-started', 'started', 'complete', 'failed', 'unavailable'), 'Unknown sample clock status')
    failure = capture['Failure']
    require((type(failure) is str and bool(failure)) if status == 'failed' else failure is None,
            'Sample clock failure differs from status')
    require(capture['ErrorBoundEstablished'] is False and type(capture['ObservedMappingConsistent']) is bool,
            'Unsupported sample clock consistency or accuracy claim')
    source = capture['Source']
    if source is not None:
        require(isinstance(source, dict), 'Invalid sample clock source')
        require(-(1 << 31) <= integer(source['Domain']) < (1 << 31), 'Invalid sample clock domain')
        integer(source['UnitsPerSecond'])
    start, end = reading(capture['Start']), reading(capture['End'])
    metadata = capture['Metadata']
    if status in ('not-started', 'unavailable'):
        require(source is None and metadata is None and start is None and end is None, 'Inactive clock capture contains evidence')
    elif status == 'started':
        require(start is not None and end is None, 'Started clock capture has invalid endpoints')
    elif status == 'complete':
        require(start is not None and end is not None, 'Completed clock capture lacks endpoints')
    else:
        require(end is None, 'Failed clock capture contains a completed end reading')
    require(record.get('completed') is not True or status not in ('not-started', 'started'),
            'Completed work has incomplete clock capture')
    measurement = record.get('measurement')
    window = measurement.get('Window') if isinstance(measurement, dict) else None
    expected = status == 'complete' and consistent(source, start, end, window)
    require(capture['ObservedMappingConsistent'] is expected, 'Sample clock consistency differs from raw evidence')
    rejection = capture['Rejection']
    require(rejection is None if expected else type(rejection) is str, 'Invalid sample clock rejection')
    require(failure is None or rejection == failure, 'Sample clock rejection lost the backend failure')
    return dict(status='consistent' if expected else 'rejected', captureStatus=status, rejection=rejection,
                errorBoundEstablished=False, predictionAccuracyEstablished=False)
