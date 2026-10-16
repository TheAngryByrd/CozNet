static class SelectedCpuCaptureChecks
{
    private sealed class Clock(params long[] values) : IThreadCpuClock
    {
        private int index;
        public long ReadNanoseconds() => values[index++];
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    public static void Run()
    {
        Require(SelectedCpuCapture.Parse(null, false, null) is null && SelectedCpuCapture.Parse("0", false, null) is null,
            "Disabled capture requires no platform clock.");
        foreach (var value in new[] { "", "true", " 1", "2" })
            Reject<ArgumentException>(() => SelectedCpuCapture.Parse(value, true, () => new Clock(0)));
        Reject<ArgumentException>(() => SelectedCpuCapture.Parse("1", false, () => new Clock(0)));
        Reject<PlatformNotSupportedException>(() => SelectedCpuCapture.Parse("1", true, null));
        Reject<InvalidOperationException>(() => SelectedCpuCapture.Parse("1", true, () => new Clock(-1)));

        var capture = SelectedCpuCapture.Parse("1", true, () => new Clock(10, 20, 50))!;
        Require(capture.Status == "prepared" && capture.StartReading is null && capture.EndReading is null,
            "Clock preparation must not become a work endpoint.");
        capture.Start();
        Require(capture.Status == "running" && capture.StartReading!.CpuNanoseconds == 20 && capture.EndReading is null,
            "Start must retain its fresh CPU reading.");
        Exception? foreign = null;
        var thread = new Thread(() => { try { capture.Finish(); } catch (Exception error) { foreign = error; } });
        thread.Start();
        Require(thread.Join(TimeSpan.FromSeconds(5)), "Foreign owner check did not finish.");
        Require(foreign is InvalidOperationException && capture.Status == "running" && capture.Failure is null,
            "A foreign thread must not read or mutate capture state.");
        capture.Finish();
        Require(capture.Status == "complete" && capture.EndReading!.CpuNanoseconds - capture.StartReading!.CpuNanoseconds == 30,
            "Work CPU must exclude the preparation reading.");
        Require(capture.StartReading!.BeforeTimestamp <= capture.StartReading.AfterTimestamp &&
            capture.StartReading.AfterTimestamp <= capture.EndReading!.BeforeTimestamp &&
            capture.EndReading.BeforeTimestamp <= capture.EndReading.AfterTimestamp, "CPU readings need ordered wall brackets.");
        Reject<InvalidOperationException>(capture.Finish);
        Require(capture.Status == "failed" && capture.Failure is not null, "Repeated completion must invalidate capture.");

        var early = SelectedCpuCapture.Parse("1", true, () => new Clock(0))!;
        Reject<InvalidOperationException>(early.Finish);
        Reject<InvalidOperationException>(early.Start);
        Require(early.Status == "failed" && early.StartReading is null, "Failed transitions must remain terminal.");
        var repeated = SelectedCpuCapture.Parse("1", true, () => new Clock(0, 10))!;
        repeated.Start();
        Reject<InvalidOperationException>(repeated.Start);
        foreach (long last in new[] { -1L, 9L })
        {
            var invalid = SelectedCpuCapture.Parse("1", true, () => new Clock(0, 10, last))!;
            invalid.Start();
            Reject<InvalidOperationException>(invalid.Finish);
            Require(invalid.Status == "failed" && invalid.EndReading is null && invalid.Failure is not null,
                "Invalid CPU readings must not produce an accepted endpoint.");
        }
        var equal = SelectedCpuCapture.Parse("1", true, () => new Clock(0, 10, 10))!;
        equal.Start();
        equal.Finish();
        Require(equal.Status == "complete", "A coarse CPU clock can report zero CPU advance.");
    }
}
