using System.Reflection;
using System.Reflection.Emit;

sealed record Instruction(uint Offset, string OpCode, int Size);

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
