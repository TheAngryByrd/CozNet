using static CooperativeManagedChecks;

static class BoundCpuSessionChecks
{
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("An invalid CPU session transition was accepted.");
    }

    public static void Run()
    {
        CpuCounterHistoryChecks.Run();
        MultipleEpochsPreserveSourceAndCounter();
        TransitionCorruptionCannotAdvance();
        FailedOperationsCannotBecomeAnEpoch();
        MutableJournalInputsCannotChangeAcceptedEvidence();
    }

    private static void MultipleEpochsPreserveSourceAndCounter()
    {
        ulong cpu = 0;
        var journal = new CpuCounterJournal(TimeProvider.System, () => cpu);
        journal.ReadCounter();
        var initial = BoundCpuSession.ParseFresh(journal.Snapshot());
        Reject(() => initial.Close(journal.Snapshot(), 0));
        journal.Transition(true, () => { });
        journal.ReadCounter();
        var running = initial.Enable(journal.Snapshot());
        Reject(() => running.Enable(journal.Snapshot()));
        Reject(() => running.Close(journal.Snapshot(), 0));
        cpu = 100;
        journal.Transition(false, () => { });
        journal.ReadCounter();
        var paused = running.Disable(journal.Snapshot(), cpu);
        Require(paused.Close(journal.Snapshot(), cpu).Epoch == 1, "The first disabled epoch could not close.");
        journal.Transition(true, () => { });
        journal.ReadCounter();
        var resumed = paused.Enable(journal.Snapshot());
        cpu = 250;
        journal.Transition(false, () => { });
        journal.ReadCounter();
        var final = resumed.Disable(journal.Snapshot(), cpu).Close(journal.Snapshot(), cpu);
        Require(final.Epoch == 2 && final.Transitions.Count == 4 && final.LastReading!.Value.CpuNanoseconds == 250
            && initial.Current.Epoch == 0 && paused.Current.Epoch == 1 && paused.Current.LastReading!.Value.CpuNanoseconds == 100,
            "Source epochs reset cumulative CPU or mutated prior session evidence.");
        Reject(() => paused.Enable(final));
        Reject(() => paused.Close(final, cpu));
    }

    private static void TransitionCorruptionCannotAdvance()
    {
        var journal = new CpuCounterJournal(TimeProvider.System, () => 0);
        journal.ReadCounter();
        var initial = BoundCpuSession.ParseFresh(journal.Snapshot());
        journal.Transition(true, () => { });
        journal.ReadCounter();
        var enabled = journal.Snapshot();
        foreach (var altered in new[]
        {
            enabled with { SourceIdentity = "foreign" },
            enabled with { Epoch = 2 },
            enabled with { Enabled = false },
            enabled with { Epoch = 2, LastReading = enabled.LastReading!.Value with { Epoch = 2 } },
            enabled with { Enabled = false, LastReading = enabled.LastReading!.Value with { Enabled = false } },
            enabled with { Transitions = Array.Empty<CpuCounterTransition>() },
            enabled with { Transitions = new[] { enabled.Transitions[0] with { Completed = false } } },
            enabled with { Transitions = new[] { enabled.Transitions[0] with { Before = -1 } } },
            enabled with { LastReading = enabled.LastReading!.Value with { Before = enabled.Transitions[0].Before - 1 } }
        }) Reject(() => initial.Enable(altered));
        var running = initial.Enable(enabled);
        journal.Transition(false, () => { });
        journal.ReadCounter();
        var disabled = journal.Snapshot();
        var changedPrefix = disabled.Transitions.ToArray();
        changedPrefix[0] = changedPrefix[0] with { After = changedPrefix[0].After + 1 };
        Reject(() => running.Disable(disabled with { Transitions = changedPrefix }, 0));
        Reject(() => running.Disable(disabled, 1));
    }

    private static void FailedOperationsCannotBecomeAnEpoch()
    {
        var journal = new CpuCounterJournal(TimeProvider.System, () => 0);
        journal.ReadCounter();
        var initial = BoundCpuSession.ParseFresh(journal.Snapshot());
        try { journal.Transition(true, () => throw new IOException("Enable failure.")); }
        catch (IOException) { }
        journal.ReadCounter();
        Reject(() => initial.Enable(journal.Snapshot()));
        journal.Transition(true, () => { });
        journal.ReadCounter();
        Reject(() => initial.Enable(journal.Snapshot()));
    }

    private static void MutableJournalInputsCannotChangeAcceptedEvidence()
    {
        var journal = new CpuCounterJournal(TimeProvider.System, () => 0);
        journal.ReadCounter();
        var initialInput = new List<CpuCounterTransition>();
        var initial = BoundCpuSession.ParseFresh(journal.Snapshot() with { Transitions = initialInput });
        journal.Transition(true, () => { });
        journal.ReadCounter();
        var enabledInput = journal.Snapshot().Transitions.ToList();
        var running = initial.Enable(journal.Snapshot() with { Transitions = enabledInput });
        initialInput.Add(enabledInput[0]);
        enabledInput.Clear();
        Require(initial.Initial.Transitions.Count == 0 && running.Current.Transitions.Count == 1,
            "Mutable input journals changed accepted initial or enabled evidence.");
        journal.Transition(false, () => { });
        journal.ReadCounter();
        var disabledInput = journal.Snapshot().Transitions.ToList();
        var paused = running.Disable(journal.Snapshot() with { Transitions = disabledInput }, 0);
        var closed = paused.Close(journal.Snapshot() with { Transitions = disabledInput }, 0);
        disabledInput.Clear();
        Require(paused.Current.Transitions.Count == 2 && closed.Transitions.Count == 2,
            "Mutable input journals changed accepted disabled or closed evidence.");
    }
}
