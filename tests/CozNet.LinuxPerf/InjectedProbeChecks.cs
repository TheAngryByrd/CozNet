static class InjectedProbeChecks
{
    public static void Run()
    {
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

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception(message);
    }
}
