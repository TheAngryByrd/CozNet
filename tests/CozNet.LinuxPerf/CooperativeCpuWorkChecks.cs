using static CooperativeManagedChecks;

static class CooperativeCpuWorkChecks
{
    private sealed class Calls { public int Count; }
    private readonly struct Arithmetic(Calls calls, CooperativeCpuWork? work = null, int stopAfter = 1,
        long checksum = CooperativeCpuWork.ExpectedChecksum, Exception? failure = null) : IChecksumArithmetic
    {
        public long Run()
        {
            calls.Count++;
            if (calls.Count == stopAfter) work?.RequestPeerStop();
            if (failure is not null) throw failure;
            return checksum;
        }
    }

    public static void Run()
    {
        PlanRejectsInvalidWorkBeforeExecution();
        OnlySelectedWorkChangesArithmetic();
        VerifiedBatchesAccumulateExactlyOnce();
        StoppingPeerPreventsFurtherWork();
        InvalidChecksumCannotPublishProgress();
        ArithmeticFailurePreservesIdentityAndProgress();
    }

    private static void PlanRejectsInvalidWorkBeforeExecution()
    {
        foreach (string count in new[] { "1", "50000", "2147483647" })
        {
            var plan = CooperativeWorkPlan.Parse(PhysicalWorkMode.Baseline, count);
            Require(plan.Batches == int.Parse(count) && !plan.Mode.Optimized, "Work plan changed its count or baseline mode.");
        }
        foreach (string count in new[] { "", "0", "-1", "+1", " 1", "1 ", "1.0", "2147483648" })
        {
            Exception? observed = null;
            try { CooperativeWorkPlan.Parse(PhysicalWorkMode.Baseline, count); } catch (Exception error) { observed = error; }
            Require(observed is ArgumentException, $"Invalid fixed-work count {count} passed parsing.");
        }
    }

    private static void OnlySelectedWorkChangesArithmetic()
    {
        foreach (string mode in new[] { "baseline", "optimized" })
        {
            var work = new CooperativeCpuWork(PhysicalWorkMode.Parse(mode));
            var original = new Calls();
            var optimized = new Calls();
            work.RunSelected(new Arithmetic(original), new Arithmetic(optimized));
            Require(original.Count == (mode == "baseline" ? 1 : 0) && optimized.Count == (mode == "optimized" ? 1 : 0)
                && work.SelectedProgress == 1 && work.SelectedChecksum == 49995000 && work.PeerProgress == 0,
                $"Mode {mode} changed the wrong arithmetic or counter.");
            var peer = new Calls();
            work.RunPeer(new Arithmetic(peer, work));
            Require(peer.Count == 1 && work.PeerProgress == 1 && work.PeerChecksum == 49995000
                && original.Count == (mode == "baseline" ? 1 : 0) && optimized.Count == (mode == "optimized" ? 1 : 0),
                "Peer work changed selected arithmetic or progress.");
        }
    }

    private static void VerifiedBatchesAccumulateExactlyOnce()
    {
        var work = new CooperativeCpuWork(PhysicalWorkMode.Baseline);
        var selected = new Calls();
        var peer = new Calls();
        for (int index = 0; index < 3; ++index) work.RunSelected(new Arithmetic(selected), new OptimizedChecksumArithmetic());
        work.RunPeer(new Arithmetic(peer, work, 4));
        Require(selected.Count == 3 && work.SelectedProgress == 3 && work.SelectedChecksum == 149985000
            && peer.Count == 4 && work.PeerProgress == 4 && work.PeerChecksum == 199980000,
            "Shared work lost, duplicated, or crossed a completed batch.");
    }

    private static void StoppingPeerPreventsFurtherWork()
    {
        var work = new CooperativeCpuWork(PhysicalWorkMode.Baseline);
        var calls = new Calls();
        work.RequestPeerStop();
        work.RunPeer(new Arithmetic(calls));
        Require(calls.Count == 0 && work.PeerProgress == 0 && work.PeerChecksum == 0,
            "A stopped peer performed work or published progress.");
    }

    private static void InvalidChecksumCannotPublishProgress()
    {
        foreach (bool selected in new[] { false, true })
        {
            var work = new CooperativeCpuWork(PhysicalWorkMode.Baseline);
            var calls = new Calls();
            Exception? observed = null;
            try
            {
                if (selected) work.RunSelected(new Arithmetic(calls, checksum: 0), new OptimizedChecksumArithmetic());
                else work.RunPeer(new Arithmetic(calls, work, checksum: 0));
            }
            catch (Exception error) { observed = error; }
            Require(observed is InvalidDataException && calls.Count == 1 && work.SelectedProgress == 0
                && work.PeerProgress == 0 && work.SelectedChecksum == 0 && work.PeerChecksum == 0,
                "Invalid checksum work was counted as completed progress.");
        }
    }

    private static void ArithmeticFailurePreservesIdentityAndProgress()
    {
        var work = new CooperativeCpuWork(PhysicalWorkMode.Baseline);
        var original = new IOException("Controlled arithmetic failure.");
        var calls = new Calls();
        Exception? observed = null;
        try { work.RunSelected(new Arithmetic(calls, failure: original), new OptimizedChecksumArithmetic()); }
        catch (Exception error) { observed = error; }
        Require(ReferenceEquals(original, observed) && calls.Count == 1 && work.SelectedProgress == 0 && work.SelectedChecksum == 0,
            "Arithmetic failure changed identity, retried, or published progress.");
    }
}
