enum SampleClockDomain { Unknown, Monotonic }

sealed record SampleClockDescription(SampleClockDomain Domain, long UnitsPerSecond);
sealed record SampleClockReading(long Before, long NativeNanoseconds, long After,
    long TimestampFrequency, string? ContextIdentity, long? MonotonicOffsetNanoseconds, string? Error);

interface ISampleClockEvidenceSource
{
    SampleClockDescription Description { get; }
    object Metadata { get; }
    SampleClockReading Read();
}

sealed class ConsistentSampleClockReadings
{
    public SampleClockReading Start { get; }
    public SampleClockReading End { get; }

    private ConsistentSampleClockReadings(SampleClockReading start, SampleClockReading end)
    {
        Start = start;
        End = end;
    }

    public static (ConsistentSampleClockReadings? Value, string? Error) Parse(
        SampleClockDescription? source, SampleClockReading? start, SampleClockReading? end,
        MeasurementWindowCapture.Boundary? measurementStart, MeasurementWindowCapture.Boundary? measurementEnd)
    {
        if (source is not { Domain: SampleClockDomain.Monotonic, UnitsPerSecond: 1000000000 })
            return (null, "The source clock domain or units are unknown or incompatible.");
        if (start is null || end is null || measurementStart is null || measurementEnd is null)
            return (null, "Clock readings or measurement endpoints are missing.");
        foreach (var reading in new[] { start, end })
        {
            if (reading.Error is not null) return (null, reading.Error);
            if (string.IsNullOrWhiteSpace(reading.ContextIdentity) || reading.MonotonicOffsetNanoseconds != 0)
                return (null, "Clock context or zero-offset evidence is missing.");
            if (reading.TimestampFrequency <= 0 || reading.Before < 0 || reading.NativeNanoseconds < 0 || reading.After < reading.Before)
                return (null, "Clock units or ordering are invalid.");
            Int128 native = (Int128)reading.NativeNanoseconds * reading.TimestampFrequency;
            if (native < (Int128)reading.Before * 1000000000 || native > (Int128)reading.After * 1000000000)
                return (null, "The native clock reading lies outside its managed bracket.");
        }
        if (start.ContextIdentity != end.ContextIdentity)
            return (null, "The clock context changed during measurement.");
        if (start.TimestampFrequency != end.TimestampFrequency || start.TimestampFrequency != measurementStart.TimestampFrequency ||
            start.TimestampFrequency != measurementEnd.TimestampFrequency)
            return (null, "Clock frequencies differ across the measurement.");
        if (end.Before < start.After || end.NativeNanoseconds < start.NativeNanoseconds)
            return (null, "The clock regressed across the measurement.");
        if (start.After > measurementStart.BeforeTimestamp || end.Before < measurementEnd.AfterTimestamp)
            return (null, "Clock readings do not enclose the measurement endpoints.");
        return (new(start, end), null);
    }
}
