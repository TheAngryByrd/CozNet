using System.Text.Json;

static class CalibrationEvidence
{
    public static void RequireNew(string path)
    {
        foreach (string suffix in new[] { "", ".plan.json", ".trials.jsonl", ".blocks.jsonl" })
            if (File.Exists(path + suffix)) throw new IOException("Calibration evidence already exists.");
    }

    public static void WriteNew(string path, object value, JsonSerializerOptions options)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, value, options);
    }

    public static void Append(FileStream journal, object value)
    {
        byte[] row = JsonSerializer.SerializeToUtf8Bytes(value);
        journal.Write(row);
        journal.WriteByte((byte)'\n');
        journal.Flush(flushToDisk: true);
    }
}
