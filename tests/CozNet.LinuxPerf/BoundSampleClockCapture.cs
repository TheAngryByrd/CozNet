sealed record BoundSampleClockEvidence(string Status, SampleClockDescription? Source, object? Metadata,
    SampleClockReading? Start, SampleClockReading? End, string? Failure,
    bool ObservedMappingConsistent, string? Rejection, bool ErrorBoundEstablished);

sealed class BoundSampleClockCapture
{
    private enum State { New, Active, Complete, Failed, Unavailable, NotStarted }
    private readonly int owner = Environment.CurrentManagedThreadId;
    private State state;
    private ISampleClockEvidenceSource? source;
    private SampleClockDescription? description;
    private object? metadata;
    private SampleClockReading? start, end;
    private string? failure;

    private BoundSampleClockCapture() { }

    public static BoundSampleClockCapture? Parse(string? value, bool finiteMeasurement) => value switch
    {
        null or "0" => null,
        "1" when finiteMeasurement => new(),
        "1" => throw new ArgumentException("Sample clock capture requires finite measurement."),
        _ => throw new ArgumentException("COZNET_BOUND_SAMPLE_CLOCK must be 0 or 1.")
    };

    private void RequireOwner()
    {
        if (Environment.CurrentManagedThreadId != owner)
            throw new InvalidOperationException("Sample clock capture requires its owning thread.");
    }

    public Func<IInstructionMap, ICpuSampleSink, IOwnerStoppedCpuEventSession> Wrap(
        Func<IInstructionMap, ICpuSampleSink, IOwnerStoppedCpuEventSession> createSource)
    {
        ArgumentNullException.ThrowIfNull(createSource);
        RequireOwner();
        return (map, sink) =>
        {
            RequireNew();
            var created = createSource(map, sink);
            Begin(() => created.ClockEvidence);
            return created;
        };
    }

    public void Begin(Func<ISampleClockEvidenceSource?> readSource)
    {
        ArgumentNullException.ThrowIfNull(readSource);
        RequireNew();
        state = State.Active;
        try
        {
            source = readSource();
            if (source is null) { state = State.Unavailable; return; }
            description = source.Description;
            metadata = source.Metadata;
            start = source.Read() ?? throw new InvalidDataException("The source returned no clock reading.");
        }
        catch (Exception error) { failure = error.ToString(); state = State.Failed; }
    }

    private void RequireNew()
    {
        RequireOwner();
        if (state != State.New) throw new InvalidOperationException("Sample clock capture can start only once.");
    }

    public void Finish()
    {
        RequireOwner();
        if (state == State.New) { state = State.NotStarted; return; }
        if (state != State.Active) return;
        try
        {
            end = source!.Read() ?? throw new InvalidDataException("The source returned no clock reading.");
            state = State.Complete;
        }
        catch (Exception error) { failure = error.ToString(); state = State.Failed; }
    }

    public BoundSampleClockEvidence Describe(MeasurementWindowCapture.Boundary? measurementStart,
        MeasurementWindowCapture.Boundary? measurementEnd)
    {
        RequireOwner();
        var parsed = ConsistentSampleClockReadings.Parse(description, start, end, measurementStart, measurementEnd);
        string status = state switch
        {
            State.New or State.NotStarted => "not-started",
            State.Active => "started",
            State.Complete => "complete",
            State.Failed => "failed",
            State.Unavailable => "unavailable",
            _ => throw new InvalidOperationException("Unknown sample clock capture state.")
        };
        return new(status, description, metadata, start, end, failure,
            state == State.Complete && parsed.Value is not null, failure ?? parsed.Error, false);
    }
}
