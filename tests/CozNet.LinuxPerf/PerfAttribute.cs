using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Explicit, Size = 96)]
struct PerfAttribute
{
    [FieldOffset(0)] public uint Type;
    [FieldOffset(4)] public uint Size;
    [FieldOffset(8)] public ulong Config;
    [FieldOffset(16)] public ulong SamplePeriod;
    [FieldOffset(24)] public ulong SampleType;
    [FieldOffset(32)] public ulong ReadFormat;
    [FieldOffset(40)] public ulong Flags;
    [FieldOffset(48)] public uint WakeupEvents;
    [FieldOffset(92)] public int ClockId;

    public static PerfAttribute TaskClock() => new()
    {
        Type = 1,
        Size = 96,
        Config = 1,
        SamplePeriod = 1000000,
        SampleType = 1 | 2 | 4 | 16 | 256,
        Flags = 1UL | (1UL << 5) | (1UL << 6) | (1UL << 25),
        WakeupEvents = 1,
        ClockId = 1
    };
}
