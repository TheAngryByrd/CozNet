namespace CozNet

open System

type internal NominalSampleObservation = private {
    Stream: Guid
    Sequence: uint64
    Epoch: uint64
    PeriodNanoseconds: uint64
    State: SampleThreadState
    Location: SampleLocation
}

[<RequireQualifiedAccess>]
module internal NominalSampleObservation =
    let eligibilityEpoch sample = sample.Epoch

    let tryCreate stream sequence epoch periodNanoseconds state location =
        if
            sequence = 0UL
            || epoch = 0UL
            || periodNanoseconds = 0UL
        then
            Error "Nominal samples require positive sequence, epoch, and period."
        else
            Ok {
                Stream = stream
                Sequence = sequence
                Epoch = epoch
                PeriodNanoseconds = periodNanoseconds
                State = state
                Location = location
            }

type internal NominalSampleCreditStream = private {
    Identity: Guid
    Frequency: int64
    Percent: int
    PeriodNanoseconds: uint64
    LastSequence: uint64
    LastEpoch: uint64
    Remainder: bigint
}

[<RequireQualifiedAccess>]
module internal NominalSampleCreditStream =
    let tryCreate frequency percent periodNanoseconds =
        if
            frequency
            <= 0L
            || percent < 0
            || percent > 100
            || periodNanoseconds = 0UL
        then
            Error
                "Nominal credit requires a positive frequency and period, and speedup from zero through 100."
        else
            Ok {
                Identity = Guid.NewGuid()
                Frequency = frequency
                Percent = percent
                PeriodNanoseconds = periodNanoseconds
                LastSequence = 0UL
                LastEpoch = 0UL
                Remainder = 0I
            }

    let identity stream = stream.Identity

    let observe (sample: NominalSampleObservation) stream =
        if
            sample.Stream
            <> stream.Identity
        then
            Error "The nominal sample belongs to another credit stream."
        elif
            sample.Sequence
            <= stream.LastSequence
            || sample.Sequence
               - stream.LastSequence
               <> 1UL
        then
            Error "Nominal sample sequences must be contiguous and increasing."
        elif sample.Epoch < stream.LastEpoch then
            Error "The nominal sample eligibility epoch decreased."
        elif
            sample.PeriodNanoseconds
            <> stream.PeriodNanoseconds
        then
            Error "The nominal sample period changed."
        else
            let numerator =
                if
                    sample.State = SampleThreadState.Runnable
                    && sample.Location = SampleLocation.Selected
                then
                    bigint sample.PeriodNanoseconds
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
                Error "Nominal sample credit exceeds the ledger tick range."
            else
                DelayTicks.tryCreate (int64 ticks)
                |> Result.map (fun credit ->
                    credit,
                    {
                        stream with
                            LastSequence = sample.Sequence
                            LastEpoch = sample.Epoch
                            Remainder = numerator % denominator
                    }
                )
