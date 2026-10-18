module CozNet.TargetLocal.Work

open System.IO
open System.Runtime.CompilerServices

[<MethodImpl(MethodImplOptions.NoInlining)>]
let selected (rounds: uint64) =
    let mutable accumulator = 0UL

    for _ in 1UL .. rounds do
        for index in 0..19999 do
            accumulator <- accumulator * 6364136223846793005UL + uint64 index

    accumulator

[<MethodImpl(MethodImplOptions.NoInlining)>]
let peer (rounds: uint64) =
    let mutable accumulator = 1UL

    for _ in 1UL .. rounds do
        for index in 0..19999 do
            accumulator <- accumulator * 2862933555777941757UL + uint64 index

    accumulator

let selectedName = "CozNet.TargetLocal.Work::selected(uint64)"
let peerName = "CozNet.TargetLocal.Work::peer(uint64)"

let private parseLine (line: string) =
    let parts = line.Split(' ', 3)

    if parts.Length = 3 then
        Some(System.Convert.ToUInt64(parts[0], 16), System.Convert.ToUInt64(parts[1], 16), parts[2])
    else
        None

let range (perfMap: string) (name: string) =
    let matches =
        File.ReadAllLines perfMap
        |> Array.choose parseLine
        |> Array.filter (fun (_, size, symbol) -> size > 0UL && symbol.Contains(name) && symbol.Contains("[Optimized]"))

    match matches with
    | [| start, size, _ |] -> start, start + size
    | _ -> failwith $"The perf map must contain one optimized range for {name}, but it contains {matches.Length}."
