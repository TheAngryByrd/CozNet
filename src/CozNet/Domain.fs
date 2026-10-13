namespace CozNet

open System

[<StructuralEquality; StructuralComparison>]
type Region = private Region of string

[<RequireQualifiedAccess>]
module Region =
    let tryCreate name =
        if String.IsNullOrWhiteSpace name then
            Error "A region name must contain a non-whitespace character."
        else
            Ok(Region name)

    let name (Region name) = name

[<StructuralEquality; StructuralComparison>]
type VirtualSpeedup = private VirtualSpeedup of float

[<RequireQualifiedAccess>]
module VirtualSpeedup =
    let tryCreate fraction =
        if
            not (Double.IsFinite fraction)
            || fraction < 0.0
            || fraction > 0.9
        then
            Error "A virtual speedup must be finite and between 0.0 and 0.9."
        else
            Ok(VirtualSpeedup fraction)

    let fraction (VirtualSpeedup fraction) = fraction

type ExperimentSettings = private {
    Target: Region
    Speedups: VirtualSpeedup list
    Blocks: int
    WarmupRuns: int
    Seed: int
}

[<RequireQualifiedAccess>]
module ExperimentSettings =
    let tryCreate target speedups blocks warmupRuns seed =
        if
            List.isEmpty speedups
            || List.length speedups > 16
        then
            Error "An experiment needs between 1 and 16 speedups."
        elif
            List.distinct speedups
            <> speedups
        then
            Error "Speedups must be distinct."
        elif
            blocks < 3
            || blocks > 10000
        then
            Error "An experiment needs between 3 and 10000 blocks."
        elif
            warmupRuns < 0
            || warmupRuns > 100
        then
            Error "Warmup runs must be between 0 and 100."
        else
            Ok {
                Target = target
                Speedups = speedups
                Blocks = blocks
                WarmupRuns = warmupRuns
                Seed = seed
            }

    let target settings = settings.Target
    let speedups settings = settings.Speedups
    let blocks settings = settings.Blocks
    let warmupRuns settings = settings.WarmupRuns
    let seed settings = settings.Seed

[<RequireQualifiedAccess>]
type Intervention =
    | Control of VirtualSpeedup
    | Treatment of VirtualSpeedup

type Observation = {
    Block: int
    Order: int
    Intervention: Intervention
    Elapsed: TimeSpan
    RegionTime: TimeSpan
    TargetTime: TimeSpan
    InjectedDelay: TimeSpan
    Progress: int64
    RegionCalls: int64
    TargetCalls: int64
    GcCollections: int list
}

type Estimate = {
    Speedup: VirtualSpeedup
    MedianElapsedReduction: float
    MinimumElapsedReduction: float
    MaximumElapsedReduction: float
    Blocks: int
}

type ExperimentResult = {
    Settings: ExperimentSettings
    Observations: Observation list
    Estimates: Estimate list
    Runtime: string
    OperatingSystem: string
    ProcessorCount: int
    TimestampFrequency: int64
}
