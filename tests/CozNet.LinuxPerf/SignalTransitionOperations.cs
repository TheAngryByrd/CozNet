using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

interface ISignalTransitionOperations
{
    void WaitInNative();
    bool NativeWaitEntered { get; }
    void ReleaseNativeWait();
    long NativeHop();
    string ClassifyAddress(ulong address);
    object DescribeAddressMap();
}

sealed class LinuxSignalTransitionOperations : ISignalTransitionOperations
{
    private sealed record Range(ulong Start, ulong End, string Kind, string Image, string Permissions, string Device, string Inode, string? ImageSha256);
    private readonly Range[] ranges;
    public LinuxSignalTransitionOperations()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Signal transition operations require Linux x64.");
        if (Hop() <= 0) throw new InvalidOperationException("The native transition fixture did not load.");
        ranges = File.ReadAllLines("/proc/self/maps").Select(line =>
        {
            var fields = line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);
            var addresses = fields[0].Split('-');
            if (fields.Length < 5 || addresses.Length != 2) throw new InvalidDataException("Malformed process mapping.");
            ulong start = ulong.Parse(addresses[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            ulong end = ulong.Parse(addresses[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (start >= end) throw new InvalidDataException("Invalid process mapping range.");
            string name = fields.Length == 6 ? Path.GetFileName(fields[5]) : "";
            string kind = fields[1].Contains('x') ? name switch
            {
                "libclrjit.so" => "jit",
                "libCozNet.SignalWorkload.so" => "native-fixture",
                "libcoreclr.so" => "runtime",
                _ => "other"
            } : "other";
            string? hash = null;
            if (kind != "other")
            {
                using var image = File.OpenRead(fields[5]);
                hash = Convert.ToHexString(SHA256.HashData(image));
            }
            return new Range(start, end, kind, name, fields[1], fields[3], fields[4], hash);
        }).OrderBy(range => range.Start).ToArray();
        if (ranges.Zip(ranges.Skip(1), (left, right) => left.End > right.Start).Any(overlap => overlap))
            throw new InvalidDataException("Process mapping ranges overlap.");
    }
    public string ClassifyAddress(ulong address) => ranges.FirstOrDefault(range => address >= range.Start && address < range.End)?.Kind ?? "unmapped";
    public object DescribeAddressMap() => ranges.Where(range => range.Kind != "other").ToArray();
    public bool NativeWaitEntered => WaitState() == 1;
    public void WaitInNative() => Check(Wait(), "wait");
    public void ReleaseNativeWait() => Check(Release(), "release");
    public long NativeHop() => Hop();
    private static void Check(int error, string operation)
    {
        if (error != 0) throw new InvalidOperationException($"Native fixture {operation} failed with error {error}.");
    }
    [DllImport("CozNet.SignalWorkload", EntryPoint = "coz_fixture_wait_state", CallingConvention = CallingConvention.Cdecl)]
    private static extern int WaitState();
    [DllImport("CozNet.SignalWorkload", EntryPoint = "coz_fixture_wait", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Wait();
    [DllImport("CozNet.SignalWorkload", EntryPoint = "coz_fixture_release", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Release();
    [DllImport("CozNet.SignalWorkload", EntryPoint = "coz_fixture_hop", CallingConvention = CallingConvention.Cdecl)]
    private static extern long Hop();
}
