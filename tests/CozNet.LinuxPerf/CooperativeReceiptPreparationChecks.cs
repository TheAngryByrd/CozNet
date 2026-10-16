static class CooperativeReceiptPreparationChecks
{
    public static void Run()
    {
        DisabledPreparationDoesNotDispatch();
        PreparationSettlesOnController();
        InvalidModesAndFailedDispatchRejectPreparation();
    }

    private static void DisabledPreparationDoesNotDispatch()
    {
        foreach (string? value in new string?[] { null, "0" })
        {
            var plan = CooperativeReceiptPreparation.Parse(value, false, false);
            var result = plan.Execute(_ => throw new InvalidOperationException("Disabled preparation dispatched."));
            CooperativeManagedChecks.Require(!plan.Enabled && result is null, "Disabled preparation retained evidence.");
        }
    }

    private static void PreparationSettlesOnController()
    {
        var worker = new CooperativeInteropChecks.Worker();
        try
        {
            int dispatches = 0;
            int owner = Environment.CurrentManagedThreadId, actual = owner;
            var result = CooperativeReceiptPreparation.Parse("1", true, true).Execute(action =>
                worker.Run(() => { dispatches++; actual = Environment.CurrentManagedThreadId; return action(); }));
            CooperativeManagedChecks.Require(result is not null && dispatches == 1 && actual != owner
                && result.Started <= result.Observed && result.Observed <= result.Accepted,
                "Preparation did not complete one ordered controller exercise.");
            result!.RequireSettled();
        }
        finally { CooperativeManagedChecks.Require(worker.Join(), "Preparation controller did not join."); }
    }

    private static void InvalidModesAndFailedDispatchRejectPreparation()
    {
        foreach (var input in new[] { ("", true, true), ("true", true, true), (" 1", true, true),
            ("2", true, true), ("1", false, true), ("1", true, false) })
        {
            bool rejected = false;
            try { CooperativeReceiptPreparation.Parse(input.Item1, input.Item2, input.Item3); }
            catch (ArgumentException) { rejected = true; }
            CooperativeManagedChecks.Require(rejected, "Invalid receipt preparation settings were accepted.");
        }
        var plan = CooperativeReceiptPreparation.Parse("1", true, true);
        var failure = new InvalidOperationException("Controller failure.");
        Exception? observed = null;
        try { plan.Execute(_ => throw failure); }
        catch (Exception error) { observed = error; }
        CooperativeManagedChecks.Require(ReferenceEquals(failure, observed), "Preparation changed its dispatch failure.");
        bool invalidResult = false;
        try { plan.Execute(_ => new(1, 2, 3, 9, 500000, true, 0)); }
        catch (InvalidDataException) { invalidResult = true; }
        CooperativeManagedChecks.Require(invalidResult && plan.LastResult is { Children: 9 },
            "Preparation accepted an incomplete settlement or lost its failed evidence.");
        try { plan.Execute(_ => throw failure); }
        catch (InvalidOperationException error) when (ReferenceEquals(error, failure)) { }
        CooperativeManagedChecks.Require(plan.LastResult is null, "A later dispatch failure retained stale preparation evidence.");
    }
}
