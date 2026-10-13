namespace CozNet

open System

[<RequireQualifiedAccess>]
type internal SampleThreadState =
    | Runnable
    | Blocked
    | Delaying
    | Unavailable

[<RequireQualifiedAccess>]
type internal SampleLocation =
    | Selected
    | Other
    | Unmapped

type internal CpuSampleObservation = private {
    Stream: Guid
    Sequence: uint64
    CpuNanoseconds: uint64
    EligibilityEpoch: uint64
    State: SampleThreadState
    Location: SampleLocation
}

[<RequireQualifiedAccess>]
module internal CpuSampleObservation =
    let tryCreate stream sequence cpuNanoseconds eligibilityEpoch state location =
        if sequence = 0UL then
            Error "Sample sequence numbers must start at one."
        else
            Ok {
                Stream = stream
                Sequence = sequence
                CpuNanoseconds = cpuNanoseconds
                EligibilityEpoch = eligibilityEpoch
                State = state
                Location = location
            }

type internal SampleCreditStream = private {
    Identity: Guid
    Frequency: int64
    Percent: int
    LastSequence: uint64
    LastCpu: uint64
    LastEpoch: uint64
    LastState: SampleThreadState
    Remainder: bigint
}

[<RequireQualifiedAccess>]
module internal SampleCreditStream =
    let tryCreate frequency percent initialCpuNanoseconds eligibilityEpoch initialState =
        if
            frequency
            <= 0L
        then
            Error "The ledger clock frequency must be positive."
        elif
            percent < 0
            || percent > 100
        then
            Error "The sampled speedup percentage must be between zero and 100."
        else
            Ok {
                Identity = Guid.NewGuid()
                Frequency = frequency
                Percent = percent
                LastSequence = 0UL
                LastCpu = initialCpuNanoseconds
                LastEpoch = eligibilityEpoch
                LastState = initialState
                Remainder = 0I
            }

    let identity stream = stream.Identity

    let observe sample stream =
        if
            sample.Stream
            <> stream.Identity
        then
            Error "The sample belongs to another credit stream."
        elif
            sample.Sequence
            <= stream.LastSequence
        then
            Error "The sample sequence is stale or duplicated."
        elif sample.CpuNanoseconds < stream.LastCpu then
            Error "The cumulative CPU clock decreased."
        elif sample.EligibilityEpoch < stream.LastEpoch then
            Error "The sample eligibility epoch decreased."
        else
            let eligible =
                sample.Sequence
                - stream.LastSequence = 1UL
                && sample.EligibilityEpoch = stream.LastEpoch
                && sample.State = SampleThreadState.Runnable
                && stream.LastState = SampleThreadState.Runnable
                && sample.Location = SampleLocation.Selected

            let numerator =
                if eligible then
                    bigint (
                        sample.CpuNanoseconds
                        - stream.LastCpu
                    )
                    * bigint stream.Frequency
                    * bigint stream.Percent
                    + stream.Remainder
                else
                    stream.Remainder

            let denominator = 100000000000I

            let ticks =
                numerator
                / denominator

            if ticks > bigint Int64.MaxValue then
                Error "Sample credit exceeds the ledger tick range."
            else
                DelayTicks.tryCreate (int64 ticks)
                |> Result.map (fun credit ->
                    credit,
                    {
                        stream with
                            LastSequence = sample.Sequence
                            LastCpu = sample.CpuNanoseconds
                            LastEpoch = sample.EligibilityEpoch
                            LastState = sample.State
                            Remainder = numerator % denominator
                    }
                )
