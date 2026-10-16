using static CooperativeManagedChecks;

static class BoundControllerPlanChecks
{
    public static void Run()
    {
        ModesPreserveDefaultAndRejectUnsupportedCombinations();
        InvalidModesAndReservedSlotsRejectBeforeNativeOpen();
    }

    private static void ModesPreserveDefaultAndRejectUnsupportedCombinations()
    {
        foreach (string? value in new string?[] { null, "0", "1" })
        foreach (bool selected in new[] { false, true })
        foreach (bool scheduler in new[] { false, true })
        foreach (bool dispatch in new[] { false, true })
        {
            BoundControllerPlan? plan = null;
            bool rejected = false;
            try { plan = BoundControllerPlan.Parse(value, selected, scheduler, dispatch); }
            catch (ArgumentException) { rejected = true; }
            bool expectedRejection = value == "1" && (!selected || scheduler || dispatch);
            Require(rejected == expectedRejection && (rejected || plan?.OwnerEnabled == (value == "1")),
                $"Controller selection differs for value={value}, selected={selected}, scheduler={scheduler}, dispatch={dispatch}.");
        }
    }

    private static void InvalidModesAndReservedSlotsRejectBeforeNativeOpen()
    {
        foreach (string value in new[] { "", "true", "2", " 1", "1 " })
        {
            bool rejected = false;
            try { BoundControllerPlan.Parse(value, true, false, false); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "An invalid controller mode was accepted.");
        }
        foreach (uint slot in new[] { 2U, uint.MaxValue })
        {
            bool rejected = false;
            try { _ = new NativeCooperativeClient(1, targets: 2, controllerSlot: slot); }
            catch (ArgumentOutOfRangeException error) when (error.ParamName == "controllerSlot") { rejected = true; }
            Require(rejected, "An invalid reserved slot reached native session creation.");
        }
    }
}
