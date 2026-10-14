static class SignalTargetLifetime
{
    public static void Run(IBoundedInterventionBackend backend, int slot, ManualResetEventSlim deliveryStopped,
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
                if (drained())
                {
                    try { backend.RetireCurrentThread(slot); }
                    catch (Exception error) { failure(error); }
                }
            }
        }
    }
}
