using System.Runtime.ExceptionServices;

readonly record struct CooperativeReceiptRequest(uint Worker, ulong Command, ulong Generation, long Budget);

sealed record CooperativeRejectedReceiptInterval(
    CooperativeReceiptRequest Request, long Started, long Observed, long TimestampFrequency);

sealed class CooperativeReceiptIntervalCapture
{
    private ExceptionDispatchInfo? failure;
    public CooperativeRejectedReceiptInterval? Rejected { get; private set; }

    public CooperativeReceiptInterval Parse(CooperativeReceiptRequest request, long started, long observed, long frequency)
    {
        failure?.Throw();
        try { return CooperativeReceiptInterval.Parse(started, observed, frequency); }
        catch (InvalidDataException error)
        {
            Rejected = new(request, started, observed, frequency);
            failure = ExceptionDispatchInfo.Capture(error);
            throw;
        }
    }
}
