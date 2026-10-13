using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text.Json;

if (args.Length != 4) throw new ArgumentException("Pass target, mapping mode, probe placement, and evidence path.");
var target = DiagnosticTarget.Parse(args[0]);
var mode = args[1] switch
{
    "registered" => MappingMode.Registered,
    "raw-diagnostic" => MappingMode.RawDiagnostic,
    _ => throw new ArgumentException("Select registered or raw-diagnostic mapping.")
};
var placement = args[2] switch
{
    "branch" => ProbePlacement.Branch,
    "empty-stack-diagnostic" => ProbePlacement.EmptyStackDiagnostic,
    _ => throw new ArgumentException("Select branch or empty-stack-diagnostic placement.")
};
int interval = Environment.GetEnvironmentVariable("COZNET_PROBE_INTERVAL") switch
{
    null or "1" => 1,
    "64" => 64,
    "256" => 256,
    "1024" => 1024,
    _ => throw new ArgumentException("Select probe interval 1, 64, 256, or 1024.")
};
if (placement == ProbePlacement.EmptyStackDiagnostic && interval == 1)
    throw new ArgumentException("Empty-stack diagnostic placement requires a countdown interval.");
Require(Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0" &&
    Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") == "0", "Disable tiered compilation and ReadyToRun for this diagnostic.");
var assembly = Assembly.Load(target.Assembly);
var type = assembly.GetType(target.Type, throwOnError: true)!;
var method = type.GetMethod(target.Method, BindingFlags.Public | BindingFlags.Static)!;
byte[] originalIL = method.GetMethodBody()!.GetILAsByteArray()!;
long result = (long)method.Invoke(null, target.Arguments)!;
Require(result == 49995000, $"The diagnostic workload changed its checksum: {result}.");
Require(Native.CozGetStatus() == 2 && Native.CozGetInstrumentedMethods() == 1, "The native profiler did not rewrite one method.");
Require(Native.CozGetILMapMode() == (int)mode, "The native and managed mapping modes differ.");

Require(Native.CozGetProbePlacement() == (int)placement, "The native and managed probe placements differ.");
long calls = target.ReadCalls(assembly, type);
Require(calls == 1 + 10001 / interval, $"The diagnostic workload has an incorrect helper count: {calls}.");

var originalInstructions = IL.Decode(originalIL);
var originalOffsets = originalInstructions.Select(x => x.Offset).ToHashSet();
var ilMap = new List<ILMapping>();
uint count = Native.CozGetMapSize();
Require(count == originalInstructions.Count, "The private IL map does not cover the original instructions.");
for (uint i = 0; i < count; i++)
{
    Require(Native.CozGetMapEntry(i, out uint original, out uint rewritten), $"Missing private IL map entry {i}.");
    Require(originalOffsets.Contains(original), $"Private map offset {original} is not an original instruction.");
    ilMap.Add(new(original, rewritten));
}
Require(!Native.CozGetMapEntry(count, out _, out _), "An out-of-range private map entry was accepted.");
uint length = Native.CozGetRewrittenILSize();
Require(length > originalIL.Length && length <= 1024 * 1024, "The rewritten IL size is outside the diagnostic bound.");
var rewrittenIL = new byte[length];
for (uint i = 0; i < length; i++)
{
    int value = Native.CozGetRewrittenILByte(i);
    Require(value is >= 0 and <= 255, $"Missing rewritten IL byte {i}.");
    rewrittenIL[i] = (byte)value;
}
Require(Native.CozGetRewrittenILByte(length) == -1, "An out-of-range IL byte was accepted.");
var privateMap = ilMap.ToDictionary(x => x.Rewritten, x => x.Original);
var rewrittenInstructions = IL.Decode(rewrittenIL).Select(x => new
{
    x.Offset, x.OpCode, x.Size,
    originalOffset = privateMap.TryGetValue(x.Offset, out uint original) ? (uint?)original : null,
    ilOrigin = privateMap.ContainsKey(x.Offset) ? "original" : "injected"
}).ToArray();
Require(privateMap.Keys.All(offset => rewrittenInstructions.Any(x => x.Offset == offset)),
    "A private map entry does not name a rewritten instruction.");
Require(Native.CozGetNativeCodeInfo(out nint address, out nuint size) && address != 0 && size is > 0 and <= 1024 * 1024,
    "The native code range is outside the diagnostic bound.");
var nativeCode = new byte[checked((int)size)];
Marshal.Copy(address, nativeCode, 0, nativeCode.Length);
var nativeMap = new List<NativeMapping>();
count = Native.CozGetNativeMapSize();
Require(count > 0, "The CLR returned no native map entries.");
for (uint i = 0; i < count; i++)
{
    Require(Native.CozGetNativeMapEntry(i, out uint il, out uint start, out uint end), $"Missing native map entry {i}.");
    Require(start <= end && end <= size, $"Native map entry {i} exceeds the code range.");
    string origin = unchecked((int)il) < 0 ? "special" : mode == MappingMode.Registered
        ? originalOffsets.Contains(il) ? "original" : "unknown"
        : rewrittenInstructions.FirstOrDefault(x => x.Offset == il)?.ilOrigin ?? "unknown";
    nativeMap.Add(new(unchecked((int)il), start, end, origin, "unverified"));
}
Require(!Native.CozGetNativeMapEntry(count, out _, out _, out _), "An out-of-range native map entry was accepted.");
if (mode == MappingMode.RawDiagnostic)
    Require(Native.CozStartSampling() == unchecked((int)0x80004001), "Raw diagnostic mode did not reject original-IL sampling.");

File.WriteAllText(args[3], JsonSerializer.Serialize(new
{
    target = args[0], mode = args[1], placement = args[2], interval, result, calls,
    runtime = RuntimeInformation.FrameworkDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    moduleVersionId = method.Module.ModuleVersionId, metadataToken = method.MetadataToken,
    originalIlHex = Convert.ToHexString(originalIL), rewrittenIlHex = Convert.ToHexString(rewrittenIL),
    nativeCodeHex = Convert.ToHexString(nativeCode), nativeCodeSize = nativeCode.Length,
    originalInstructions, rewrittenInstructions, ilMap, nativeMap,
    nativeOwnershipEstablished = false, debuggerSteppingEstablished = false
}, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

enum MappingMode { Registered, RawDiagnostic }
enum ProbePlacement { Branch, EmptyStackDiagnostic }
sealed record ILMapping(uint Original, uint Rewritten);
sealed record NativeMapping(int ILOffset, uint Start, uint End, string MappedILOrigin, string NativeOwnership);
sealed record Instruction(uint Offset, string OpCode, int Size);

sealed record DiagnosticTarget(string Assembly, string Type, string Method, object[]? Arguments)
{
    public static DiagnosticTarget Parse(string value) => value switch
    {
        "instrumentation" => new("CozNet.Instrumentation", "CozNet.Instrumentation.Workload", "Work", [10000]),
        "linux-perf" => new("CozNet.LinuxPerf", "CpuWorkload", "Run", null),
        _ => throw new ArgumentException("Select instrumentation or linux-perf.")
    };
    public long ReadCalls(Assembly assembly, Type type) => Assembly == "CozNet.Instrumentation"
        ? (long)type.GetField("probes", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!
        : (long)assembly.GetType("InjectedProbes", true)!.GetProperty("Calls")!.GetValue(null)!;
}

static class IL
{
    private static readonly Dictionary<ushort, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(x => x.FieldType == typeof(OpCode)).Select(x => (OpCode)x.GetValue(null)!)
        .ToDictionary(x => unchecked((ushort)x.Value));

    public static List<Instruction> Decode(byte[] bytes)
    {
        var result = new List<Instruction>();
        int index = 0;
        while (index < bytes.Length)
        {
            int start = index;
            ushort value = bytes[index++];
            if (value == 0xfe)
            {
                if (index == bytes.Length) throw new InvalidOperationException("Truncated IL opcode.");
                value = (ushort)(0xfe00 | bytes[index++]);
            }
            if (!Opcodes.TryGetValue(value, out var opcode)) throw new InvalidOperationException($"Unknown IL opcode {value:X4}.");
            int operand = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod or
                    OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType or
                    OperandType.ShortInlineR => 4,
                _ => throw new NotSupportedException($"Unsupported diagnostic operand: {opcode.OperandType}.")
            };
            index = checked(index + operand);
            if (index > bytes.Length) throw new InvalidOperationException("Truncated IL operand.");
            result.Add(new((uint)start, opcode.Name!, index - start));
        }
        return result;
    }
}

static class Native
{
    [DllImport("CozNet.Profiler")] public static extern int CozGetStatus();
    [DllImport("CozNet.Profiler")] public static extern int CozGetInstrumentedMethods();
    [DllImport("CozNet.Profiler")] public static extern int CozGetILMapMode();
    [DllImport("CozNet.Profiler")] public static extern int CozGetProbePlacement();
    [DllImport("CozNet.Profiler")] public static extern uint CozGetMapSize();
    [DllImport("CozNet.Profiler")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool CozGetMapEntry(uint index, out uint original, out uint rewritten);
    [DllImport("CozNet.Profiler")] public static extern uint CozGetRewrittenILSize();
    [DllImport("CozNet.Profiler")] public static extern int CozGetRewrittenILByte(uint index);
    [DllImport("CozNet.Profiler")] public static extern uint CozGetNativeMapSize();
    [DllImport("CozNet.Profiler")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool CozGetNativeMapEntry(uint index, out uint il, out uint start, out uint end);
    [DllImport("CozNet.Profiler")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool CozGetNativeCodeInfo(out nint address, out nuint size);
    [DllImport("CozNet.Profiler")] public static extern int CozStartSampling();
}
