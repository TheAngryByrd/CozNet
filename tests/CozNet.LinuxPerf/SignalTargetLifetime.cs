static class SignalTargetLifetime
{
    public static void Run(IInterventionBackend backend, int slot, ManualResetEventSlim deliveryStopped,
        Func<bool> drained, Action body, Action<Exception> failure)
    {
        bool registered = false;
        try
        {
            backend.RegisterCurrentThread(slot);
            registered = true;
            body();
        }
        catch (Exception error) { failure(error); }
        finally
        {
            if (registered)
            {
                deliveryStopped.Wait();
                while (!drained()) Thread.Sleep(10);
                try { backend.RetireCurrentThread(slot); }
                catch (Exception error) { failure(error); }
            }
        }
    }
}
