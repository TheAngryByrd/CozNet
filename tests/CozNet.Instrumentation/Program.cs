using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace CozNet.Instrumentation;

public static class Workload
{
    private static long probes;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Probe() => probes++;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long Work(int count)
    {
        long sum = 0;
        for (int i = 0; i < count; i++)
            sum += i;
        return sum;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed record SourceLocation(string Document, int StartLine, int StartColumn, int EndLine, int EndColumn, bool InLoop);

    private static SourceLocation? ResolveSource(uint ilOffset, [CallerFilePath] string sourcePath = "")
    {
        using var stream = File.OpenRead(Path.ChangeExtension(typeof(Workload).Assembly.Location, ".pdb"));
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        var reader = provider.GetMetadataReader();
        int token = typeof(Workload).GetMethod(nameof(Work))!.MetadataToken;
        var handle = MetadataTokens.MethodDefinitionHandle(token & 0x00ffffff);
        var points = reader.GetMethodDebugInformation(handle.ToDebugInformationHandle()).GetSequencePoints();
        var candidates = points.Where(point => point.Offset <= ilOffset).ToArray();
        if (candidates.Length == 0) return null;
        var point = candidates[^1];
        if (point.IsHidden) return null;
        string document = reader.GetString(reader.GetDocument(point.Document).Name);
        Require(Path.GetFileName(document) == "Program.cs", $"Unexpected source document for IL {ilOffset}.");
        string[] source = File.ReadAllLines(sourcePath);
        int loopLine = Array.FindIndex(source, line => line.Trim() == "for (int i = 0; i < count; i++)") + 1;
        Require(loopLine > 0, "Cannot find the workload loop in the source file.");
        return new SourceLocation(Path.GetFileName(document), point.StartLine, point.StartColumn,
            point.EndLine, point.EndColumn, point.StartLine >= loopLine && point.StartLine <= loopLine + 1);
    }

    public static int Main(string[] args)
    {
        Require(args.Length == 2 && (args[0] == "baseline" || args[0] == "profiled" || args[0] == "sampled"),
            "Expected baseline, profiled, or sampled, followed by an evidence file path.");
        bool profiled = args[0] != "baseline";
        var observations = new List<object>();
        foreach (int count in new[] { 0, 1, 1000000 })
        {
            probes = 0;
            long result = Work(count);
            if (profiled)
                Require(Native.CozGetStatus() == 2, $"Profiler status after Work: {Native.CozGetStatus()}.");
            long expected = (long)count * (count - 1) / 2;
            Require(result == expected, $"Arithmetic changed for count {count}: expected {expected}, got {result}.");
            Require(probes == (profiled ? (long)count + 2 : 0),
                $"Unexpected probe count {probes} in {args[0]} for count {count}.");
            observations.Add(new { count, result, probes });
        }

        var mapping = new List<object>();
        if (profiled)
        {
            Require(Native.CozGetStatus() == 2, $"Profiler status: {Native.CozGetStatus()}.");
            Require(Native.CozGetInstrumentedMethods() == 1, "Expected one instrumented method.");
            uint size = Native.CozGetMapSize();
            Require(size == 20, $"Expected 20 original IL instructions, got {size}.");
            uint previousOriginal = 0;
            uint previousRewritten = 0;
            for (uint i = 0; i < size; i++)
            {
                Require(Native.CozGetMapEntry(i, out uint original, out uint rewritten), $"Missing map entry {i}.");
                Require(rewritten > original, $"Instruction {original} did not move after instrumentation.");
                Require(i == 0 || (original > previousOriginal && rewritten > previousRewritten), "IL map is not monotonic.");
                mapping.Add(new { original, rewritten });
                previousOriginal = original;
                previousRewritten = rewritten;
            }
            Require(!Native.CozGetMapEntry(size, out _, out _), "Out-of-range map entry succeeded.");
        }
        object? sampling = null;
        if (args[0] == "sampled")
        {
            int startResult = Native.CozStartSampling();
            Require(startResult == 0, $"Sampler start failed: {startResult:X8}.");
            int completed = 0;
            try
            {
                var elapsed = Stopwatch.StartNew();
                while (elapsed.ElapsedMilliseconds < 500)
                {
                    long result = Work(1000000);
                    Require(result == 499999500000, $"Sampled arithmetic changed: {result}.");
                    completed++;
                }
            }
            finally { Native.CozStopSampling(); }
            uint attempts = Native.CozGetSampleAttempts();
            uint failures = Native.CozGetSampleFailures();
            uint selected = Native.CozGetSelectedStacks();
            int lastFailure = Native.CozGetLastSampleFailure();
            uint mapped = Native.CozGetMappedStacks();
            uint lastIL = Native.CozGetLastSampleIL();
            Require(completed > 0, "Sampling workload did not run.");
            Require(attempts > 0, "Sampler did not attempt a stack walk.");
            Require(selected > 0, $"No selected frames: attempts={attempts}, failures={failures}, last={lastFailure:X8}.");
            Require(selected + failures <= attempts, "Sample counters exceed attempts.");
            Require(mapped > 0 && mapped <= selected, $"Invalid mapped sample count: {mapped} of {selected}.");
            Require(lastIL < 22, $"Sample IL offset {lastIL} is outside the original method body.");
            var sourceSamples = new List<object>();
            uint accounted = 0;
            uint loopSamples = 0;
            uint locationCount = Native.CozGetSampleLocationCount();
            for (uint i = 0; i < locationCount; i++)
            {
                Require(Native.CozGetSampleLocation(i, out uint il, out uint count), $"Missing sample location {i}.");
                if (count == 0) continue;
                Require(il < 22, $"Mapped IL offset {il} is outside the original method.");
                SourceLocation? source = ResolveSource(il);
                accounted = checked(accounted + count);
                if (source?.InLoop == true) loopSamples = checked(loopSamples + count);
                sourceSamples.Add(new { il, count, source });
            }
            Require(accounted == mapped, $"Location counts {accounted} differ from mapped samples {mapped}.");
            Require(loopSamples > 0, "No mapped sample reached the loop source span.");
            Require(!Native.CozGetSampleLocation(locationCount, out _, out _), "Out-of-range sample location succeeded.");
            sampling = new { completed, attempts, failures, selected, mapped, lastIL, loopSamples, sourceSamples, lastFailure };
        }
        File.WriteAllText(args[1], JsonSerializer.Serialize(new
        {
            mode = args[0],
            runtime = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            observations,
            mapping,
            sampling
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}

internal static class Native
{
    [DllImport("CozNet.Profiler")]
    internal static extern uint CozGetSampleLocationCount();

    [DllImport("CozNet.Profiler")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CozGetSampleLocation(uint index, out uint il, out uint count);

    [DllImport("CozNet.Profiler")]
    internal static extern uint CozGetMappedStacks();

    [DllImport("CozNet.Profiler")]
    internal static extern uint CozGetLastSampleIL();

    [DllImport("CozNet.Profiler")]
    internal static extern int CozStartSampling();

    [DllImport("CozNet.Profiler")]
    internal static extern void CozStopSampling();

    [DllImport("CozNet.Profiler")]
    internal static extern uint CozGetSampleAttempts();

    [DllImport("CozNet.Profiler")]
    internal static extern uint CozGetSampleFailures();

    [DllImport("CozNet.Profiler")]
    internal static extern uint CozGetSelectedStacks();

    [DllImport("CozNet.Profiler")]
    internal static extern int CozGetLastSampleFailure();

    [DllImport("CozNet.Profiler")]
    internal static extern int CozGetStatus();

    [DllImport("CozNet.Profiler")]
    internal static extern int CozGetInstrumentedMethods();

    [DllImport("CozNet.Profiler")]
    internal static extern uint CozGetMapSize();

    [DllImport("CozNet.Profiler")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CozGetMapEntry(uint index, out uint original, out uint rewritten);
}
