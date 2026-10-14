sealed class SignalPhaseTiming
{
    public ulong RequestStartedNanoseconds { get; }
    public ulong QueueReturnedNanoseconds { get; }
    public ulong HandlerStartedNanoseconds { get; }
    public ulong HandlerFinishedNanoseconds { get; }
    public ulong ObservedNanoseconds { get; }
    public ulong BeforeHandlerNanoseconds => HandlerStartedNanoseconds - RequestStartedNanoseconds;
    public ulong MeasuredHandlerNanoseconds => HandlerFinishedNanoseconds - HandlerStartedNanoseconds;
    public ulong AfterHandlerNanoseconds => ObservedNanoseconds - HandlerFinishedNanoseconds;
    public ulong QueueCallNanoseconds => QueueReturnedNanoseconds - RequestStartedNanoseconds;

    private SignalPhaseTiming(ulong requestStarted, ulong queueReturned, ulong handlerStarted, ulong handlerFinished, ulong observed) =>
        (RequestStartedNanoseconds, QueueReturnedNanoseconds, HandlerStartedNanoseconds, HandlerFinishedNanoseconds, ObservedNanoseconds) =
        (requestStarted, queueReturned, handlerStarted, handlerFinished, observed);

    public static SignalPhaseTiming Parse(ulong requestStarted, ulong queueReturned, ulong handlerStarted,
        ulong handlerFinished, ulong observed, ulong measuredHandler)
    {
        if (requestStarted == 0 || queueReturned < requestStarted || handlerStarted < requestStarted
            || handlerFinished < handlerStarted || observed < handlerFinished || observed < queueReturned
            || handlerFinished - handlerStarted != measuredHandler)
            throw new InvalidDataException($"Invalid signal phase timing: request={requestStarted}, queue={queueReturned}, start={handlerStarted}, end={handlerFinished}, observed={observed}, measuredHandler={measuredHandler}.");
        return new(requestStarted, queueReturned, handlerStarted, handlerFinished, observed);
    }
}
