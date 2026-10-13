using System.Runtime.InteropServices;

enum AttributionMode { Method, RawWholeMethod, RawExcludeCandidates }

static class AttributionPlan
{
    public static AttributionMode Parse(string? value, ProbePlan probes, string? mappingMode, string? placement)
    {
        var mode = value switch
        {
            null or "method" => AttributionMode.Method,
            "raw-whole-method" => AttributionMode.RawWholeMethod,
            "raw-exclude-candidates" => AttributionMode.RawExcludeCandidates,
            _ => throw new ArgumentException("Select method, raw-whole-method, or raw-exclude-candidates attribution.")
        };
        if (mode != AttributionMode.Method && (probes.Mode != ProbeMode.Injected || probes.BackedgeInterval != 1024 ||
            mappingMode != "raw-diagnostic" || placement != "empty-stack-diagnostic"))
            throw new ArgumentException("Raw attribution requires injected interval 1024, raw-diagnostic mapping, and empty-stack-diagnostic placement.");
        return mode;
    }
}

sealed record RawNativeRange(int ILOffset, uint Start, uint End);
sealed record CandidateRange(int ILOffset, uint Start, uint End, string MappedILOrigin);
sealed record RawAttributionEvidence(ulong CodeStart, string NativeCodeHex, string RewrittenIlHex,
    uint[] OriginalRewrittenOffsets, CandidateRange[] Ranges, bool ExcludeCandidates, bool NativeOwnershipEstablished);

sealed class RawIlCandidateMap : IInstructionMap
{
    public const string CandidateSymbol = "[diagnostic raw-IL injected candidate]";
    private readonly IInstructionMap fallback;
    private readonly string selectedSymbol;
    private readonly ulong start;
    private readonly uint size;
    private readonly CandidateRange[] ranges;
    private readonly bool exclude;
    public RawAttributionEvidence Evidence { get; }

    private RawIlCandidateMap(IInstructionMap fallback, string selectedSymbol, ulong start, byte[] nativeCode,
        byte[] rewrittenIL, uint[] originalOffsets, CandidateRange[] ranges, bool exclude)
    {
        this.fallback = fallback;
        this.selectedSymbol = selectedSymbol;
        this.start = start;
        size = (uint)nativeCode.Length;
        this.ranges = ranges;
        this.exclude = exclude;
        Evidence = new(start, Convert.ToHexString(nativeCode), Convert.ToHexString(rewrittenIL),
            originalOffsets, ranges.ToArray(), exclude, false);
    }

    public static RawIlCandidateMap Create(IInstructionMap fallback, string selectedSymbol, ulong start,
        byte[] nativeCode, byte[] rewrittenIL, IReadOnlyList<uint> originalOffsets,
        IReadOnlyList<RawNativeRange> rawRanges, bool exclude)
    {
        if (start == 0 || nativeCode.Length is 0 or > 1048576 || (ulong)nativeCode.Length > ulong.MaxValue - start)
            throw new InvalidDataException("Invalid diagnostic native code range.");
        var instructions = IL.Decode(rewrittenIL).Select(x => x.Offset).ToHashSet();
        var originals = originalOffsets.ToHashSet();
        if (originals.Count == 0 || originals.Count != originalOffsets.Count || !originals.IsSubsetOf(instructions))
            throw new InvalidDataException("The private map does not identify distinct rewritten instructions.");
        var ranges = rawRanges.OrderBy(x => x.Start).ThenBy(x => x.End).Select(x =>
        {
            string origin = x.ILOffset < 0 ? "special" : originals.Contains((uint)x.ILOffset) ? "original" : "injected-candidate";
            if (x.ILOffset >= 0 && !instructions.Contains((uint)x.ILOffset))
                throw new InvalidDataException("A native range does not identify a rewritten instruction.");
            if (x.Start > x.End || x.End > nativeCode.Length)
                throw new InvalidDataException("A native range exceeds the diagnostic code range.");
            return new CandidateRange(x.ILOffset, x.Start, x.End, origin);
        }).Where(x => x.Start != x.End).ToArray();
        uint next = 0;
        foreach (var range in ranges)
        {
            if (range.Start != next) throw new InvalidDataException("Diagnostic native ranges overlap or contain a gap.");
            next = range.End;
        }
        if (next != nativeCode.Length || !ranges.Any(x => x.MappedILOrigin == "original") ||
            !ranges.Any(x => x.MappedILOrigin == "injected-candidate"))
            throw new InvalidDataException("The diagnostic map lacks complete original and candidate ranges.");
        if (fallback.Resolve(start) != selectedSymbol || fallback.Resolve(start + (ulong)nativeCode.Length - 1) != selectedSymbol)
            throw new InvalidDataException("The diagnostic method differs from the selected runtime method.");
        return new(fallback, selectedSymbol, start, nativeCode, rewrittenIL, originals.Order().ToArray(), ranges, exclude);
    }

    public string? Resolve(ulong instructionPointer)
    {
        string? symbol = fallback.Resolve(instructionPointer);
        bool inside = instructionPointer >= start && instructionPointer - start < size;
        if ((symbol == selectedSymbol) != inside)
            throw new InvalidDataException("The selected runtime method changed outside the diagnostic snapshot.");
        if (!inside || !exclude) return symbol;
        uint offset = (uint)(instructionPointer - start);
        var range = ranges.First(x => offset >= x.Start && offset < x.End);
        return range.MappedILOrigin == "injected-candidate" ? CandidateSymbol : symbol;
    }

    public static RawIlCandidateMap Read(IInstructionMap fallback, string selectedSymbol, bool exclude)
    {
        if (NativeMap.CozGetILMapMode() != 1 || NativeMap.CozGetProbePlacement() != 1)
            throw new InvalidOperationException("The profiler did not select raw mapping and empty-stack placement.");
        if (!NativeMap.CozGetNativeCodeInfo(out nint address, out nuint size) || address == 0 || size is 0 or > 1048576)
            throw new InvalidDataException("Missing diagnostic native code.");
        var nativeCode = new byte[checked((int)size)];
        Marshal.Copy(address, nativeCode, 0, nativeCode.Length);
        uint length = NativeMap.CozGetRewrittenILSize();
        if (length is 0 or > 1048576) throw new InvalidDataException("Invalid diagnostic rewritten IL size.");
        var rewrittenIL = new byte[length];
        for (uint i = 0; i < length; i++)
        {
            int value = NativeMap.CozGetRewrittenILByte(i);
            if (value is < 0 or > 255) throw new InvalidDataException("Missing rewritten IL byte.");
            rewrittenIL[i] = (byte)value;
        }
        uint count = NativeMap.CozGetMapSize();
        if (count is 0 or > 1048576) throw new InvalidDataException("Invalid private IL map size.");
        var originals = new uint[count];
        for (uint i = 0; i < count; i++)
            if (!NativeMap.CozGetMapEntry(i, out _, out originals[i])) throw new InvalidDataException("Missing private IL map entry.");
        count = NativeMap.CozGetNativeMapSize();
        if (count is 0 or > 1048576) throw new InvalidDataException("Invalid native map size.");
        var ranges = new RawNativeRange[count];
        for (uint i = 0; i < count; i++)
        {
            if (!NativeMap.CozGetNativeMapEntry(i, out uint il, out uint first, out uint last))
                throw new InvalidDataException("Missing native map entry.");
            ranges[i] = new(unchecked((int)il), first, last);
        }
        return Create(fallback, selectedSymbol, (ulong)address, nativeCode, rewrittenIL, originals, ranges, exclude);
    }

    private static class NativeMap
    {
        [DllImport("CozNet.Profiler")] internal static extern int CozGetILMapMode();
        [DllImport("CozNet.Profiler")] internal static extern int CozGetProbePlacement();
        [DllImport("CozNet.Profiler")] internal static extern uint CozGetMapSize();
        [DllImport("CozNet.Profiler")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CozGetMapEntry(uint index, out uint original, out uint rewritten);
        [DllImport("CozNet.Profiler")] internal static extern uint CozGetRewrittenILSize();
        [DllImport("CozNet.Profiler")] internal static extern int CozGetRewrittenILByte(uint index);
        [DllImport("CozNet.Profiler")] internal static extern uint CozGetNativeMapSize();
        [DllImport("CozNet.Profiler")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CozGetNativeMapEntry(uint index, out uint il, out uint start, out uint end);
        [DllImport("CozNet.Profiler")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CozGetNativeCodeInfo(out nint address, out nuint size);
    }
}
