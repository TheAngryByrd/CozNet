using System.Runtime.CompilerServices;
using System.Text.Json;
using static CooperativeManagedChecks;
using Debt = BoundSampledPaymentWorkload.Debt;

static class BoundDebtObservationChecks
{
    private static Debt? retained;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long Observe(long index)
    {
        var debt = new Debt(index, index + 1, index + 2, index + 3, index + 4,
            index + 5, index + 6, index + 7, index + 8);
        retained = debt;
        return debt.Timestamp + debt.Global + debt.SelectedCredit + debt.PeerWait + debt.Selected
            + debt.Peer + debt.SelectedLocal + debt.PeerLocal + debt.SelectedWait;
    }

    public static void Run()
    {
        DebtRecordsAllocateNoCallingThreadMemory();
        NullableDebtEvidencePreservesJsonAndCapturedValues();
        PeakSelectionRetainsFirstMaximumPerDebt();
    }

    private static void DebtRecordsAllocateNoCallingThreadMemory()
    {
        try
        {
            for (int index = 0; index < 1000; index++) Observe(index);
            long before = GC.GetAllocatedBytesForCurrentThread();
            long sum = 0;
            for (int index = 0; index < 10000; index++) sum += Observe(index);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(allocated == 0 && sum == 9L * 10000 * 9999 / 2 + 36L * 10000
                && retained is { Timestamp: 9999, Global: 10000, SelectedWait: 10007 },
                $"Debt construction and nullable retention must preserve every value without allocation; observed {allocated} bytes.");
        }
        finally { retained = null; }
    }

    private static void PeakSelectionRetainsFirstMaximumPerDebt()
    {
        static Debt Snapshot(long timestamp, long selected, long peer)
            => new(timestamp, 10, 10 - selected, 10 - peer, selected, peer, 10 - selected, 10 - peer, 0);
        Debt? selectedPeak = null, peerPeak = null;
        var first = Snapshot(100, 2, 4);
        BoundSampledPaymentWorkload.ObservePeaks(first, ref selectedPeak, ref peerPeak);
        Require(selectedPeak == first && peerPeak == first, "Both absent peaks must retain the first observation.");
        var peerMaximum = Snapshot(200, 1, 5);
        BoundSampledPaymentWorkload.ObservePeaks(peerMaximum, ref selectedPeak, ref peerPeak);
        Require(selectedPeak == first && peerPeak == peerMaximum, "A higher peer debt must not replace the selected peak.");
        var selectedMaximum = Snapshot(300, 3, 4);
        BoundSampledPaymentWorkload.ObservePeaks(selectedMaximum, ref selectedPeak, ref peerPeak);
        Require(selectedPeak == selectedMaximum && peerPeak == peerMaximum, "A higher selected debt must not replace the peer peak.");
        BoundSampledPaymentWorkload.ObservePeaks(Snapshot(400, 3, 5), ref selectedPeak, ref peerPeak);
        BoundSampledPaymentWorkload.ObservePeaks(Snapshot(500, 0, 0), ref selectedPeak, ref peerPeak);
        Require(selectedPeak == selectedMaximum && peerPeak == peerMaximum,
            "Equal or lower debt must preserve each first maximum and its original timestamp.");
    }

    private static void NullableDebtEvidencePreservesJsonAndCapturedValues()
    {
        Debt? current = new Debt(1, 2, 3, 4, 5, 6, 7, 8, 9);
        var captured = current;
        current = new Debt(11, 12, 13, 14, 15, 16, 17, 18, 19);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            new { selectedPeak = captured, lastObservedDebt = current, finalDebt = (Debt?)null },
            new JsonSerializerOptions { IncludeFields = true }));
        string[] names = ["Timestamp", "Global", "SelectedCredit", "PeerWait", "Selected", "Peer",
            "SelectedLocal", "PeerLocal", "SelectedWait"];
        var root = document.RootElement;
        var peak = root.GetProperty("selectedPeak");
        var last = root.GetProperty("lastObservedDebt");
        Require(peak.EnumerateObject().Select(item => item.Name).Order().SequenceEqual(names.Order())
            && names.Select(name => peak.GetProperty(name).GetInt64()).SequenceEqual(Enumerable.Range(1, 9).Select(value => (long)value))
            && names.Select(name => last.GetProperty(name).GetInt64()).SequenceEqual(Enumerable.Range(11, 9).Select(value => (long)value))
            && root.GetProperty("finalDebt").ValueKind == JsonValueKind.Null
            && captured is { Timestamp: 1, SelectedWait: 9 },
            "Debt JSON must preserve all nine fields, null absence, and previously captured observations.");
    }
}
