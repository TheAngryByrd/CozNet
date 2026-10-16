using CozNet;
using Microsoft.FSharp.Core;
using static CooperativeManagedChecks;

static class CooperativeServiceCadenceChecks
{
    public static void Run()
    {
        OnlyCompletedIdleStateCanBeSkipped();
        PendingAndFailedServiceClearReuse();
        DistinctStateRequiresService();
    }

    private static void OnlyCompletedIdleStateCanBeSkipped()
    {
        var state = SignalAccountingModule.create();
        var cadence = new CooperativeServiceCadence();
        Require(cadence.ShouldRun(state, false), "Initial service must execute.");
        Require(cadence.ShouldRun(state, false), "Incomplete service must execute again.");
        cadence.Completed(state, false);
        Require(!cadence.ShouldRun(state, false) && !cadence.ShouldRun(state, false),
            "Completed idle service must permit repeated skips for the exact state.");
        Require(cadence.Checks == 4 && cadence.Executions == 2 && cadence.Skipped == 2,
            "Service counters must retain initial, incomplete, and skipped checks.");
    }

    private static void PendingAndFailedServiceClearReuse()
    {
        var state = SignalAccountingModule.create();
        var cadence = new CooperativeServiceCadence();
        Require(cadence.ShouldRun(state, false), "Initial service must execute.");
        cadence.Completed(state, false);
        Require(cadence.ShouldRun(state, true), "A pending request must override idle reuse.");
        Require(cadence.ShouldRun(state, false), "Failure without completion must clear prior idle eligibility.");
        cadence.Completed(state, true);
        Require(cadence.ShouldRun(state, false), "Completion with a pending request must not permit reuse.");
        cadence.Completed(state, false);
        Require(!cadence.ShouldRun(state, false), "Successful idle completion must restore reuse.");
        Require(cadence.Checks == 5 && cadence.Executions == 4 && cadence.Skipped == 1,
            "Pending and failed service counters must conserve all checks.");
    }

    private static void DistinctStateRequiresService()
    {
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(50), SignalAccountingModule.create()));
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
        var state = Parsed(SignalAccountingModule.prepareService(peer.Item1, peer.Item2)).Item2;
        var equivalent = Parsed(SignalAccountingModule.prepareService(peer.Item1, state)).Item2;
        Require(!ReferenceEquals(state, equivalent) && state.Equals(equivalent),
            "The fixture must contain equal-valued but distinct accounting states.");
        var cadence = new CooperativeServiceCadence();
        Require(cadence.ShouldRun(state, false), "Initial service must execute.");
        cadence.Completed(state, false);
        Require(cadence.ShouldRun(equivalent, false), "Equal values must not replace exact state identity.");
        Require(cadence.ShouldRun(state, false), "A failed new-state service must clear the previous identity.");
        cadence.Completed(state, false);
        var credited = Credit(selected.Item1, 1, 100000, state);
        Require(cadence.ShouldRun(credited, false)
            && CooperativeDebtSnapshot.Capture(selected.Item1, peer.Item1, credited).Peer == 50000,
            "New sample credit must force service with its new peer debt.");
        Require(cadence.Checks == 4 && cadence.Executions == 4 && cadence.Skipped == 0,
            "Distinct-state checks must all execute.");
    }
}
