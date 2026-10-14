static class TargetLeaseReceiptsChecks
{
    private static readonly TargetLeaseRawChild Child = new(1, 50000, 1000, 2000, 53000, 50000, 100, 0, true);
    private static readonly TargetLeaseRawHeader Active = new(1, 2, 110000, 1, 50000, 0, 0, 0, 0, true, 54000);
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid target lease receipt evidence was accepted.");
    }
    public static void Run()
    {
        foreach (var identity in new[] { (Generation: 0UL, Command: 2UL, Budget: 110000UL),
            (Generation: 1UL, Command: 0UL, Budget: 110000UL), (Generation: 1UL, Command: 2UL, Budget: 0UL),
            (Generation: 1UL, Command: 2UL, Budget: 2000001UL) })
        {
            bool rejected = false;
            try { TargetLeaseReceipts.Create(identity.Generation, identity.Command, identity.Budget); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "Invalid target lease settings must not create a receipt tracker.");
        }
        var tracker = TargetLeaseReceipts.Create(1, 2, 110000);
        var source = new[] { Child };
        var first = tracker.Observe(Active, source, 55000, 0);
        source[0] = Child with { Waited = 1 };
        Require(tracker.LastRaw!.Children[0] == Child && first[0].Raw == Child,
            "A caller buffer mutation must not change retained native evidence.");
        var repeated = tracker.Observe(Active with { Captured = 200000000 }, new[] { Child }, 200000001, 0);
        Require(repeated[0].FirstCaptured == 54000 && repeated[0].FirstManagedReceiptUpper == 55000
            && repeated[0].Timing.UpperNanoseconds == 54000,
            "A later copy must preserve the first managed receipt and its original 100 ms check.");

        foreach (var header in new[] { Active with { Generation = 2 }, Active with { Command = 1 },
            Active with { Budget = 110001 }, Active with { Children = 0 }, Active with { Captured = 0 },
            Active with { Visibility = 4 }, Active with { Visibility = 1 }, Active with { Stop = 1 },
            Active with { Error = 1 }, Active with { MayHaveDelivery = false } })
        {
            var invalid = TargetLeaseReceipts.Create(1, 2, 110000);
            Reject(() => invalid.Observe(header, new[] { Child }, 55000, 0));
            Require(invalid.FirstFailure?.Header == header, "Rejected headers must remain available as raw evidence.");
        }
        foreach (var receipt in new[] { (Upper: 53999UL, Status: 0), (Upper: 100001001UL, Status: 0), (Upper: 55000UL, Status: 5) })
        {
            var invalid = TargetLeaseReceipts.Create(1, 2, 110000);
            Reject(() => invalid.Observe(Active, new[] { Child }, receipt.Upper, receipt.Status));
            var failure = invalid.FirstFailure;
            Reject(() => invalid.Observe(Active, new[] { Child }, 55000, 0));
            Require(invalid.FirstFailure == failure && failure!.ManagedReceiptUpper == receipt.Upper
                && failure.ClockStatus == receipt.Status, "A later read must not repair or overwrite the first failed receipt.");
        }
        var exact = TargetLeaseReceipts.Create(1, 2, 110000).Observe(Active, new[] { Child }, 100001000, 0);
        Require(exact[0].Timing.UpperNanoseconds == 100000000, "The exact 100 ms receipt bound must remain inclusive.");
        var growing = TargetLeaseReceipts.Create(1, 2, 110000);
        var oldPrefix = growing.Observe(Active, new[] { Child }, 55000, 0);
        var secondChild = Child with { Ordinal = 2, Armed = 54000, Started = 55000, Finished = 106000 };
        var newPrefix = growing.Observe(Active with { Children = 2, Measured = 100000, Captured = 107000 },
            new[] { Child, secondChild }, 108000, 0);
        Require(oldPrefix.Count == 1 && newPrefix.Count == 2 && newPrefix[0].FirstManagedReceiptUpper == 55000
            && newPrefix[1].FirstManagedReceiptUpper == 108000,
            "New children must not grow an older returned prefix or replace earlier receipt times.");

        foreach (var mutation in new[] { Child with { Ordinal = 2 }, Child with { Armed = 999 },
            Child with { Started = 2001 }, Child with { Finished = 53001 }, Child with { Waited = 49999 },
            Child with { InstructionPointer = 101 }, Child with { WaitStatus = 1 }, Child with { Valid = false },
            Child with { Requested = 49999 } })
        {
            var invalid = TargetLeaseReceipts.Create(1, 2, 110000);
            invalid.Observe(Active, new[] { Child }, 55000, 0);
            Reject(() => invalid.Observe(Active with { Captured = 55001 }, new[] { mutation }, 55002, 0));
        }
        var sealedTracker = TargetLeaseReceipts.Create(1, 2, 110000);
        var terminal = Active with { Visibility = 1, Stop = 3 };
        sealedTracker.Observe(terminal, new[] { Child }, 55000, 0);
        sealedTracker.Observe(terminal with { Visibility = 2, MayHaveDelivery = false, Captured = 56000 }, new[] { Child }, 57000, 0);
        sealedTracker.Observe(terminal with { Visibility = 3, MayHaveDelivery = false, Captured = 58000 }, new[] { Child }, 59000, 0);
        Reject(() => sealedTracker.Observe(terminal with { Visibility = 3, MayHaveDelivery = false, Measured = 50001, Captured = 60000 }, new[] { Child }, 61000, 0));

        var shrinking = TargetLeaseReceipts.Create(1, 2, 110000);
        shrinking.Observe(Active, new[] { Child }, 55000, 0);
        Reject(() => shrinking.Observe(Active with { Children = 0, Captured = 56000 }, Array.Empty<TargetLeaseRawChild>(), 57000, 0));
        var noncontiguous = TargetLeaseReceipts.Create(1, 2, 110000);
        Reject(() => noncontiguous.Observe(Active, new[] { Child with { Ordinal = 2 } }, 55000, 0));
        var appended = TargetLeaseReceipts.Create(1, 2, 110000);
        appended.Observe(terminal, new[] { Child }, 55000, 0);
        Reject(() => appended.Observe(terminal with { Children = 2, Captured = 107000 }, new[] { Child, secondChild }, 108000, 0));
        var backwards = TargetLeaseReceipts.Create(1, 2, 110000);
        backwards.Observe(Active, new[] { Child }, 55000, 0);
        Reject(() => backwards.Observe(Active with { Captured = 53999 }, new[] { Child }, 55001, 0));
        var visibility = TargetLeaseReceipts.Create(1, 2, 110000);
        visibility.Observe(terminal, new[] { Child }, 55000, 0);
        Reject(() => visibility.Observe(Active with { Captured = 56000 }, new[] { Child }, 57000, 0));
        var backwardsReceipt = TargetLeaseReceipts.Create(1, 2, 110000);
        backwardsReceipt.Observe(Active, new[] { Child }, 100001000, 0);
        Reject(() => backwardsReceipt.Observe(Active with { Captured = 56000 }, new[] { Child }, 57000, 0));
    }
}
