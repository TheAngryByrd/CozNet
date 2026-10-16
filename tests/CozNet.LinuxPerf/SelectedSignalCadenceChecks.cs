static class SelectedSignalCadenceChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        OneCheckPerQualifiedBatch();
        BoundarySamplesCannotTriggerRepeatedPauses();
        NewEpochDiscardsPriorReadiness();
        DecreasingEpochDoesNotChangeReadiness();
        ContinuousModeRemainsExplicit();
        NominalEventsPermitOneCheckInEveryEpoch();
    }

    private static void OneCheckPerQualifiedBatch()
    {
        var cadence = new SelectedSignalCadence(SelectedSignalCadenceMode.QualifiedSample);
        Require(!cadence.TryCheck(), "No sample must mean no selected debt check.");
        cadence.Observe(0);
        cadence.Observe(0);
        Require(cadence.TryCheck(), "A qualified sample batch must permit a check.");
        Require(!cadence.TryCheck(), "A batch must not permit repeated selected checks.");
        cadence.Observe(0);
        Require(cadence.TryCheck(), "A later qualified sample must permit the next check.");
    }

    private static void BoundarySamplesCannotTriggerRepeatedPauses()
    {
        var cadence = new SelectedSignalCadence(SelectedSignalCadenceMode.QualifiedSample);
        for (ulong epoch = 1; epoch <= 4; epoch++)
        {
            cadence.Observe(epoch);
            Require(!cadence.TryCheck(), $"The first sample in epoch {epoch} must establish a baseline without another pause.");
        }
        cadence.Observe(4);
        Require(cadence.TryCheck(), "A complete interval after the last boundary must permit a check.");
    }

    private static void NewEpochDiscardsPriorReadiness()
    {
        var cadence = new SelectedSignalCadence(SelectedSignalCadenceMode.QualifiedSample);
        cadence.Observe(0);
        cadence.Observe(1);
        Require(!cadence.TryCheck(), "Readiness from an earlier epoch cannot survive another sampling boundary.");
    }

    private static void DecreasingEpochDoesNotChangeReadiness()
    {
        var cadence = new SelectedSignalCadence(SelectedSignalCadenceMode.QualifiedSample);
        cadence.Observe(2);
        cadence.Observe(2);
        bool rejected = false;
        try { cadence.Observe(1); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "A decreasing epoch must be rejected.");
        Require(cadence.TryCheck(), "A rejected observation must preserve existing readiness.");
        cadence.Observe(2);
        Require(cadence.TryCheck(), "A rejected observation must not move the previous epoch backwards.");
    }

    private static void ContinuousModeRemainsExplicit()
    {
        var cadence = new SelectedSignalCadence(SelectedSignalCadenceMode.Continuous);
        Require(cadence.TryCheck() && cadence.TryCheck(), "Continuous mode must preserve the original control path.");
        Require(SampledSignalPlan.Parse("50", "qualified-sample").Cadence == SelectedSignalCadenceMode.QualifiedSample,
            "The parsed plan must select sample cadence.");
    }

    private static void NominalEventsPermitOneCheckInEveryEpoch()
    {
        var cadence = new SelectedSignalCadence(SelectedSignalCadenceMode.NominalSample);
        Require(!cadence.TryCheck(), "Nominal cadence cannot run before an event.");
        foreach (ulong epoch in new ulong[] { 1, 1, 2, 4 })
        {
            cadence.Observe(epoch);
            Require(cadence.TryCheck(), "Each nominal event must permit a check, including first and resumed events.");
            Require(!cadence.TryCheck(), "A nominal event must not permit repeated checks.");
        }
        cadence.Observe(4);
        bool rejected = false;
        try { cadence.Observe(3); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected && cadence.TryCheck(), "A rejected epoch must preserve the pending nominal check.");
    }
}
