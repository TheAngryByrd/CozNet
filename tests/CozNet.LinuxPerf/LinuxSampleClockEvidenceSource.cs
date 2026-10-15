using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

sealed class LinuxSampleClockEvidenceSource : ISampleClockEvidenceSource
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec { public long Seconds; public long Nanoseconds; }

    [DllImport("libc", EntryPoint = "clock_gettime", SetLastError = true)]
    private static extern int ReadClock(int id, out Timespec value);

    internal sealed record Namespaces(string Current, string Children, string OffsetSource);
    internal sealed record NativeReading(long Before, long Nanoseconds, long After, long Frequency, string? Error = null);

    public SampleClockDescription Description { get; } = new(SampleClockDomain.Monotonic, 1000000000);
    public object Metadata { get; } = new
    {
        backend = "linux-perf", clock = "CLOCK_MONOTONIC", clockId = 1, attributeSize = 96,
        runtime = RuntimeInformation.FrameworkDescription, kernel = ReadKernelRelease(),
        operatingSystem = RuntimeInformation.OSDescription,
        architecture = RuntimeInformation.ProcessArchitecture.ToString(), timestampFrequency = Stopwatch.Frequency
    };

    public SampleClockReading Read() => Capture(ReadNamespaces,
        () => ParseMonotonicOffset(File.ReadAllLines("/proc/self/timens_offsets")), ReadNativeClock);

    internal static SampleClockReading Capture(Func<Namespaces> readNamespaces, Func<long> readOffset,
        Func<NativeReading> readNative)
    {
        long before = 0, native = 0, after = 0;
        long frequency = Stopwatch.Frequency;
        string? context = null;
        long? offset = null;
        try
        {
            var namespaces = readNamespaces();
            context = namespaces.Current;
            // proc timens_offsets reports the namespace for future children.
            if (context != namespaces.Children || context != namespaces.OffsetSource)
                throw new InvalidDataException("Current and offset-source time namespaces differ.");
            offset = readOffset();
            var clock = readNative();
            (before, native, after, frequency) = (clock.Before, clock.Nanoseconds, clock.After, clock.Frequency);
            if (clock.Error is not null) return new(before, native, after, frequency, context, offset, clock.Error);
            if (readNamespaces() != namespaces)
                throw new InvalidDataException("The time namespace changed during clock capture.");
            return new(before, native, after, frequency, context, offset, null);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or
            PlatformNotSupportedException or OverflowException or FormatException)
        {
            return new(before, native, after, frequency, context, offset, error.Message);
        }
    }

    private static Namespaces ReadNamespaces()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Sample clock evidence requires Linux x64.");
        return new(Namespace("time"), Namespace("time_for_children"), OffsetNamespace());
    }

    private static NativeReading ReadNativeClock() => CaptureNative(TimeProvider.System, () =>
    {
        if (ReadClock(1, out var time) != 0)
            throw new InvalidOperationException($"clock_gettime failed: {Marshal.GetLastPInvokeError()}.");
        return (time.Seconds, time.Nanoseconds);
    });

    internal static NativeReading CaptureNative(TimeProvider clock, Func<(long Seconds, long Nanoseconds)> read)
    {
        long before = 0, after = 0, nanoseconds = 0;
        long frequency = clock.TimestampFrequency;
        try
        {
            before = clock.GetTimestamp();
            var time = read();
            after = clock.GetTimestamp();
            if (time.Seconds < 0 || time.Nanoseconds is < 0 or >= 1000000000)
                throw new InvalidDataException("The native monotonic reading is invalid.");
            nanoseconds = checked(time.Seconds * 1000000000 + time.Nanoseconds);
            return new(before, nanoseconds, after, frequency);
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or OverflowException)
        {
            return new(before, nanoseconds, after, frequency, error.Message);
        }
    }

    private static string Namespace(string name) => new FileInfo($"/proc/thread-self/ns/{name}").LinkTarget
        ?? throw new InvalidDataException("Time namespace identity is unavailable.");

    private static string OffsetNamespace() => new FileInfo("/proc/self/ns/time_for_children").LinkTarget
        ?? throw new InvalidDataException("The offset-source namespace identity is unavailable.");

    private static string? ReadKernelRelease()
    {
        try { return File.ReadAllText("/proc/sys/kernel/osrelease").Trim(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    internal static long ParseMonotonicOffset(IEnumerable<string> lines)
    {
        var matches = lines.Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(fields => fields.Length > 0 && fields[0] == "monotonic").ToArray();
        if (matches.Length != 1 || matches[0].Length != 3)
            throw new InvalidDataException("The monotonic namespace offset is missing or ambiguous.");
        long seconds = long.Parse(matches[0][1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        long nanoseconds = long.Parse(matches[0][2], NumberStyles.None, CultureInfo.InvariantCulture);
        if (nanoseconds is < 0 or >= 1000000000) throw new InvalidDataException("The namespace offset fraction is invalid.");
        return checked(seconds * 1000000000 + nanoseconds);
    }
}
