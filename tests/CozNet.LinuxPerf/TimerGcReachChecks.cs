static class TimerGcReachChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    public static void Run()
    {
        ModesAreExplicit();
        CompleteCollectionMustFitOneCommand();
        InvalidIntervalsDoNotEstablishReach();
        SampledGcStressChecks.Run();
    }

    private static void ModesAreExplicit()
    {
        Require(TimerGcReach.ParseMode("gc") && !TimerGcReach.ParseMode("no-gc"), "The timer GC mode parser changed its modes.");
        bool rejected = false;
        try { TimerGcReach.ParseMode("GC"); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "Unknown timer GC modes must fail before side effects.");
    }

    private static void CompleteCollectionMustFitOneCommand()
    {
        TimerCommandInterval[] commands = [new(1, 100, 200), new(2, 300, 400)];
        Require(TimerGcReach.ReachedCommand([new(100, 200, 1, [])], commands), "A collection at exact command boundaries must establish reach.");
        Require(TimerGcReach.ReachedCommand([new(310, 390, 2, [])], commands), "A collection inside a later command must establish reach.");
        foreach (var collection in new[] { new GcStressObservation(99, 150, 1, []), new(150, 201, 1, []),
            new(210, 290, 1, []), new(150, 350, 1, []), new(310, 390, 0, []), new(350, 310, 1, []) })
            Require(!TimerGcReach.ReachedCommand([collection], commands), "An incomplete, invalid, or between-command collection established reach.");
        Require(!TimerGcReach.ReachedCommand([], commands), "No GC observations cannot establish reach.");
        Require(!TimerGcReach.ReachedCommand([new(110, 190, 1, [])], []), "No command intervals cannot establish reach.");
    }

    private static void InvalidIntervalsDoNotEstablishReach()
    {
        foreach (var command in new[] { new TimerCommandInterval(0, 100, 200), new(1, 0, 200), new(1, 200, 100) })
            Require(!TimerGcReach.ReachedCommand([new(110, 190, 1, [])], [command]), "An invalid command interval established GC reach.");
    }
}
