static class InjectedProbeChecks
{
    public static void Run()
    {
        ParsePlans();
        long initial = InjectedProbes.Calls;
        int selectedCalls = 0;
        using (var binding = InjectedProbes.Bind(() => selectedCalls++))
        {
            InjectedProbes.Hit();
            Reject(() => InjectedProbes.Bind(() => { }).Dispose(), "A nested binding was accepted.");
            var worker = Task.Run(() =>
            {
                Reject(binding.Dispose, "A foreign thread removed the binding.");
                int workerCalls = 0;
                using (InjectedProbes.Bind(() => workerCalls++))
                {
                    InjectedProbes.Hit();
                    InjectedProbes.Hit();
                }
                InjectedProbes.Hit();
                if (workerCalls != 2) throw new Exception("A worker callback ran after disposal.");
            });
            if (!worker.Wait(TimeSpan.FromSeconds(5))) throw new Exception("The binding worker did not finish.");
            worker.GetAwaiter().GetResult();
            InjectedProbes.Hit();
        }
        InjectedProbes.Hit();
        if (selectedCalls != 2 || InjectedProbes.Calls - initial != 3)
            throw new Exception("Probe callbacks or counts crossed thread or disposal boundaries.");
        using (InjectedProbes.Bind(() => selectedCalls++)) InjectedProbes.Hit();
        if (selectedCalls != 3) throw new Exception("The thread could not bind a new callback after disposal.");
    }

    private static void ParsePlans()
    {
        foreach (string? manual in new string?[] { null, "manual" })
        {
            var plan = ProbePlan.Parse(manual, null);
            if (plan.Mode != ProbeMode.Manual || plan.BackedgeInterval != 1 || plan.CallsPerBatch != 0)
                throw new Exception("The default manual plan changed.");
        }
        foreach (var (raw, interval, calls) in new (string?, int, long)[]
            { (null, 1, 10002), ("1", 1, 10002), ("64", 64, 157), ("256", 256, 40), ("1024", 1024, 10) })
        {
            var plan = ProbePlan.Parse("injected", raw);
            if (plan.Mode != ProbeMode.Injected || plan.BackedgeInterval != interval || plan.CallsPerBatch != calls)
                throw new Exception($"The injected plan for interval {interval} has the wrong helper count.");
        }
        foreach (var (mode, interval) in new (string?, string?)[]
            { ("", "1"), ("other", "1"), ("injected", ""), ("injected", "0"), ("injected", "2"),
              ("injected", "-1"), ("injected", "2048"), ("manual", "64"), (null, "256"), ("manual", "1024") })
        {
            try { ProbePlan.Parse(mode, interval); }
            catch (ArgumentException) { continue; }
            throw new Exception($"Invalid probe plan was accepted: mode={mode}, interval={interval}.");
        }
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception(message);
    }
}
