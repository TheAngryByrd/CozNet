using System.Text.Json;
using static CooperativeManagedChecks;

static class CpuCounterHistoryChecks
{
    public static void Run()
    {
        SnapshotsShareHistoryWithoutChangingPriorValues();
        EquivalentImmutableHistoryRetainsFallbackAcceptance();
        ChangedImmutablePrefixCannotUseAncestry();
        CloseRejectsChangedHistory();
        EnumerationAndSerializationKeepChronologicalOrder();
    }

    private static void SnapshotsShareHistoryWithoutChangingPriorValues()
    {
        var journal = new CpuCounterJournal(TimeProvider.System, () => 0, 4);
        journal.ReadCounter();
        var initial = journal.Snapshot();
        var session = BoundCpuSession.ParseFresh(initial);
        Require(ReferenceEquals(session.Current.Transitions, initial.Transitions),
            "Fresh immutable evidence must preserve its empty history identity.");
        journal.Transition(true, () => { });
        journal.ReadCounter();
        var first = journal.Snapshot();
        var running = session.Enable(first);
        Require(ReferenceEquals(first.Transitions, journal.Snapshot().Transitions)
            && ReferenceEquals(running.Current.Transitions, first.Transitions),
            "Unchanged snapshots and accepted sessions must share immutable history.");
        journal.Transition(false, () => { });
        journal.ReadCounter();
        var second = journal.Snapshot();
        var closed = running.Disable(second, 0).Close(journal.Snapshot(), 0);
        Require(initial.Transitions.Count == 0 && first.Transitions.Count == 1 && second.Transitions.Count == 2
            && first.Transitions[0] == second.Transitions[0] && first.Transitions[0].Enabling
            && !second.Transitions[1].Enabling && ReferenceEquals(closed.Transitions, second.Transitions),
            "Later transitions and closure must not alter earlier history or copy the accepted immutable sequence.");
    }

    private static CpuCounterHistory CopyHistory(IEnumerable<CpuCounterTransition> values)
    {
        var history = CpuCounterHistory.Empty;
        foreach (var value in values) history = history.Append(value);
        return history;
    }

    private static void EquivalentImmutableHistoryRetainsFallbackAcceptance()
    {
        var journal = new CpuCounterJournal(TimeProvider.System, () => 0);
        journal.ReadCounter();
        var session = BoundCpuSession.ParseFresh(journal.Snapshot());
        journal.Transition(true, () => { });
        journal.ReadCounter();
        var running = session.Enable(journal.Snapshot());
        journal.Transition(false, () => { });
        journal.ReadCounter();
        var final = journal.Snapshot();
        var equivalent = CopyHistory(final.Transitions);
        Require(!ReferenceEquals(equivalent, final.Transitions), "The equivalent history fixture must use different nodes.");
        var paused = running.Disable(final with { Transitions = equivalent }, 0);
        var closed = paused.Close(final, 0);
        Require(closed.Epoch == 1 && closed.Transitions.SequenceEqual(equivalent),
            "Equivalent non-ancestral immutable histories must retain full-value acceptance.");
    }

    private static void ChangedImmutablePrefixCannotUseAncestry()
    {
        var journal = new CpuCounterJournal(TimeProvider.System, () => 0);
        journal.ReadCounter();
        var initial = BoundCpuSession.ParseFresh(journal.Snapshot());
        journal.Transition(true, () => { });
        journal.ReadCounter();
        var enabled = journal.Snapshot();
        var running = initial.Enable(enabled);
        journal.Transition(false, () => { });
        journal.ReadCounter();
        var disabled = journal.Snapshot();
        var changed = CopyHistory([enabled.Transitions[0] with { Completed = false }, disabled.Transitions[1]]);
        Exception? failure = null;
        try { running.Disable(disabled with { Transitions = changed }, 0); }
        catch (InvalidDataException error) { failure = error; }
        Require(failure?.Message == "The bound CPU session changed source, counter, or transition ownership.",
            "A same-length immutable branch with a changed prefix must fail the ownership guard.");
        Require(running.Disable(disabled, 0).Current.Transitions.Count == 2,
            "A rejected branch must not prevent the original valid transition.");
        var invalidChild = ((CpuCounterHistory)enabled.Transitions).Append(disabled.Transitions[1] with { Completed = false });
        failure = null;
        try { running.Disable(disabled with { Transitions = invalidChild }, 0); }
        catch (InvalidDataException error) { failure = error; }
        Require(failure?.Message == "The bound CPU session has an invalid transition boundary.",
            "Valid history ancestry must not bypass validation of the new transition.");
    }

    private static void CloseRejectsChangedHistory()
    {
        var journal = new CpuCounterJournal(TimeProvider.System, () => 0);
        journal.ReadCounter();
        var fresh = BoundCpuSession.ParseFresh(journal.Snapshot());
        journal.Transition(true, () => { });
        journal.ReadCounter();
        var running = fresh.Enable(journal.Snapshot());
        journal.Transition(false, () => { });
        journal.ReadCounter();
        var final = journal.Snapshot();
        var paused = running.Disable(final, 0);
        var histories = new (string Name, CpuCounterHistory History)[]
        {
            ("shorter", CopyHistory(final.Transitions.Take(1))),
            ("longer", CopyHistory(final.Transitions).Append(final.Transitions[^1])),
            ("reordered", CopyHistory(final.Transitions.Reverse())),
            ("changed value", CopyHistory([final.Transitions[0] with { Completed = false }, final.Transitions[1]]))
        };
        foreach (var (name, history) in histories)
        {
            Exception? failure = null;
            try { paused.Close(final with { Transitions = history }, 0); }
            catch (InvalidDataException error) { failure = error; }
            Require(failure?.Message == "The bound CPU session did not close its final disabled epoch.",
                $"Closure must reject a {name} history with otherwise unchanged final evidence.");
        }
        Require(ReferenceEquals(paused.Close(final, 0).Transitions, final.Transitions),
            "Rejected closure histories must not prevent closure with the original evidence.");
    }

    private static void EnumerationAndSerializationKeepChronologicalOrder()
    {
        var history = CpuCounterHistory.Empty;
        for (int index = 0; index < 4096; index++)
            history = history.Append(new((ulong)(index / 2 + 1), index % 2 == 0, true, index * 2, index * 2 + 1, 1, null));
        var values = history.ToArray();
        Require(values.Length == 4096 && values[0] == history[0] && values[^1] == history[^1]
            && values.Select((value, index) => value.Before == index * 2 && value.Enabling == (index % 2 == 0)).All(value => value),
            "History enumeration must preserve every transition in chronological order.");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize<IReadOnlyList<CpuCounterTransition>>(history));
        Require(json.RootElement.ValueKind == JsonValueKind.Array && json.RootElement.GetArrayLength() == 4096
            && json.RootElement[0].GetProperty("Before").GetInt64() == 0
            && json.RootElement[4095].GetProperty("Before").GetInt64() == 8190,
            "Immutable history must preserve the existing JSON array representation and order.");
    }
}
