using static CooperativeManagedChecks;

static class BoundCpuEpochPrefixChecks
{
    private sealed record Fixture(BoundCpuEpochStart[] Starts, BoundCpuSession Session,
        CpuCounterSourceSnapshot Final, BoundEpochSample[] Samples);

    private static BoundEpochSample Sample(ulong epoch, ulong? count)
        => new(epoch, CpuSample.Create(1, 2, 1, 1000, 100, OverheadWorkload.SelectedSymbol, count));

    private static Fixture Create()
    {
        ulong cpu = 0;
        var journal = new CpuCounterJournal(TimeProvider.System, () => cpu);
        journal.ReadCounter();
        var session = BoundCpuSession.ParseFresh(journal.Snapshot());
        var starts = new List<BoundCpuEpochStart>();
        foreach (var (enabledCount, endCount) in new (ulong, ulong)[] { (80, 100), (100, 100), (180, 250) })
        {
            journal.Transition(true, () => { });
            cpu = enabledCount;
            journal.ReadCounter();
            var parsed = BoundCpuEpochStart.Parse(session, journal.Snapshot());
            starts.Add(parsed.Start);
            session = parsed.Session;
            cpu = endCount;
            journal.Transition(false, () => { });
            journal.ReadCounter();
            session = session.Disable(journal.Snapshot(), cpu);
        }
        return new(starts.ToArray(), session, journal.Snapshot(),
            new[] { Sample(1, 40), Sample(1, 100), Sample(3, 110), Sample(3, 250) });
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid epoch prefix evidence was accepted.");
    }

    public static void Run()
    {
        DisabledBaselinesPreservePartialAndEmptyEpochs();
        InvalidSampleIntervalsReject();
        InvalidSourceEpochsReject();
        FailedEnableCannotProduceStart();
    }

    private static void DisabledBaselinesPreservePartialAndEmptyEpochs()
    {
        var data = Create();
        var prefixes = BoundCpuEpochPrefixes.Parse(data.Starts, data.Samples, data.Session, data.Final);
        Require(prefixes.Count == 3
            && prefixes.Select(item => item.Start.DisabledBaseline.CpuNanoseconds).SequenceEqual(new ulong?[] { 0, 100, 100 })
            && prefixes.Select(item => item.EnabledPrefixCpuNanoseconds).SequenceEqual(new ulong?[] { 40, null, 10 })
            && prefixes.Select(item => item.FirstSampleIndex).SequenceEqual(new int?[] { 0, null, 2 })
            && prefixes[0].FirstSample == data.Samples[0].Sample && prefixes[1].FirstSample is null
            && prefixes[2].FirstSample == data.Samples[2].Sample,
            "Prefixes must use disabled counts, preserve empty epochs, and retain exact first source samples.");
        data.Starts[0] = data.Starts[2];
        data.Samples[0] = Sample(3, 200);
        Require(prefixes[0].Start.Epoch == 1 && prefixes[0].FirstSample!.CumulativeCpuNanoseconds == 40,
            "Input array replacement must not change completed prefix evidence.");
        var zero = Create();
        zero.Samples[0] = Sample(1, 0);
        Require(BoundCpuEpochPrefixes.Parse(zero.Starts, zero.Samples, zero.Session, zero.Final)[0].EnabledPrefixCpuNanoseconds == 0,
            "A sample equal to its disabled baseline must retain a zero prefix.");
    }

    private static void InvalidSampleIntervalsReject()
    {
        var data = Create();
        foreach (var samples in new[]
        {
            new[] { Sample(1, 101) },
            new[] { Sample(3, 99) },
            new[] { Sample(3, 110), Sample(3, 109) },
            new[] { Sample(3, 251) },
            new[] { Sample(1, null) },
            new[] { Sample(0, 0) },
            new[] { Sample(4, 250) },
            new[] { Sample(3, 110), Sample(1, 100) }
        }) Reject(() => BoundCpuEpochPrefixes.Parse(data.Starts, samples, data.Session, data.Final));
        Require(BoundCpuEpochPrefixes.Parse(data.Starts, data.Samples, data.Session, data.Final)[2].EnabledPrefixCpuNanoseconds == 10,
            "Rejected sample evidence must not alter valid prefix inputs.");
    }

    private static void InvalidSourceEpochsReject()
    {
        var data = Create();
        foreach (var starts in new[]
        {
            Array.Empty<BoundCpuEpochStart>(), data.Starts[..2],
            new[] { data.Starts[0], data.Starts[0], data.Starts[2] },
            new[] { data.Starts[1], data.Starts[0], data.Starts[2] },
            Create().Starts
        }) Reject(() => BoundCpuEpochPrefixes.Parse(starts, data.Samples, data.Session, data.Final));
        var changed = data.Final.Transitions.ToArray();
        changed[^1] = changed[^1] with { Completed = false };
        Reject(() => BoundCpuEpochPrefixes.Parse(data.Starts, data.Samples, data.Session,
            data.Final with { Transitions = changed }));
        Reject(() => BoundCpuEpochPrefixes.Parse(data.Starts, data.Samples, data.Session,
            data.Final with { LastReading = data.Final.LastReading!.Value with { CpuNanoseconds = 251 } }));
    }

    private static void FailedEnableCannotProduceStart()
    {
        var journal = new CpuCounterJournal(TimeProvider.System, () => 0);
        journal.ReadCounter();
        var session = BoundCpuSession.ParseFresh(journal.Snapshot());
        try { journal.Transition(true, () => throw new IOException("Controlled enable failure.")); }
        catch (IOException) { }
        journal.ReadCounter();
        Reject(() => BoundCpuEpochStart.Parse(session, journal.Snapshot()));
        Require(session.Current.Epoch == 0 && !session.Current.Enabled,
            "A failed start must preserve the disabled session.");
    }
}
