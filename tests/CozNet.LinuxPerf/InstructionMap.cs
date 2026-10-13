using System.Globalization;

interface IInstructionMap
{
    string? Resolve(ulong instructionPointer);
}

sealed class PerfMapSnapshot : IInstructionMap
{
    private sealed record CodeRange(ulong Start, ulong Size, string Symbol)
    {
        public bool Contains(ulong ip) => ip >= Start && ip - Start < Size;
    }

    private readonly CodeRange[] ranges;

    private PerfMapSnapshot(CodeRange[] ranges) => this.ranges = ranges;

    public static PerfMapSnapshot Read(string path)
    {
        string snapshot = File.ReadAllText(path);
        if (snapshot.Length == 0 || !snapshot.EndsWith('\n'))
            throw new InvalidDataException("The runtime perf map snapshot is empty or incomplete.");
        var ranges = snapshot[..^1].Split('\n').Select(line =>
        {
            string[] fields = line.TrimEnd('\r').Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 3 || string.IsNullOrWhiteSpace(fields[2]))
                throw new InvalidDataException("Malformed runtime perf map entry.");
            string address = fields[0].StartsWith("0x", StringComparison.Ordinal) ? fields[0][2..] : fields[0];
            ulong start = ulong.Parse(address, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            ulong size = ulong.Parse(fields[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (size == 0 || size > ulong.MaxValue - start)
                throw new InvalidDataException("Invalid runtime code range.");
            return new CodeRange(start, size, fields[2]);
        }).ToArray();
        return new PerfMapSnapshot(ranges);
    }

    public string? Resolve(ulong instructionPointer)
    {
        string? symbol = null;
        foreach (var range in ranges)
        {
            if (!range.Contains(instructionPointer)) continue;
            if (symbol is not null)
                throw new InvalidDataException("A sampled address maps to overlapping runtime code ranges.");
            symbol = range.Symbol;
        }
        return symbol;
    }
}
