namespace CozNet.Tests

open System
open System.IO
open System.Reflection
open CozNet
open Expecto

type DiscoveryShapes =
    static member Selected(r: uint64) = r + 1UL
    static member Over(x: int) = x
    static member Over(x: int64) = x
    static member Over(x: string) = x.Length
    static member Over(x: obj) = isNull x
    static member Prims(a: bool, b: char, c: sbyte, d: byte, e: int16, f: uint16, g: uint32, h: float32, i: float, j: nativeint, k: unativeint) = ()
    static member Arrays(a: int[], b: string[], c: obj[], d: uint32[]) = a.Length
    static member ByRef(a: byref<int>, b: outref<int64>, c: inref<float>) = b <- 0L
    static member Pointer(p: nativeptr<int>) = ()
    static member NoArgs() = 1
    static member Gen<'T>(x: 'T) = x
    static member ValueType(d: DateTime) = d
    static member Class(s: DiscoveryShapes) = s
    static member Rank2(a: int[,]) = a.Length
    static member Composed(a: byref<int[]>) = ()
    static member AnchorTarget(value: int) =
        value + 41

type DiscoveryInstance() =
    member _.Me(x: int) = x

type DiscoveryBox<'T>() =
    member _.Get(x: 'T) = x

type IDiscoveryAbstract =
    abstract Run: int -> int

module DiscoveryModule =
    type Marker = class end

    let selected (rounds: uint64) = rounds + 1UL

    module Inner =
        type Marker = class end

        let nested (x: int) = x

module TargetDiscoveryTests =
    let private labels = [
        "0x1000 40 uint64 [Probe] PerfProbe.Shapes::Selected(uint64)[Optimized]"
        "0x1040 20 instance int32 [Probe] PerfProbe.Inst::Me(int32)[Optimized]"
        "1060 20 int32 [Probe] PerfProbe.Shapes+Inner::Nested(int32)[Optimized]"
        "0x1080 10 !!0 [Probe] PerfProbe.Shapes::Gen(!!0)[Optimized]"
        "0x1090 10 !!0 [Probe] PerfProbe.Shapes::Gen(!!0)[Optimized]"
        "0x10a0 10 instance !0 [Probe] PerfProbe.Box`1[System.Int32]::Get(!0)[Optimized]"
        "0x10b0 10 stub<1> PreStub"
        "0x10c0 10 int32 [Probe] PerfProbe.Shapes::Over(int32)[QuickJitted]"
        "0x10d0 10 void [CozNet] CozNet.TargetWait::post()[Optimized]"
        "0x10e0 10 uint64 [CozNet.TargetLocal] CozNet.TargetLocal.Work::selected(uint64)[Optimized]"
        "0x10f0 10 void [Probe] PerfProbe.Shapes::Structs(valuetype PerfProbe.Pair,valuetype PerfProbe.Color,valuetype [System.Runtime]System.DateTime)[Optimized]"
        "0x1100 10 valuetype [System.Runtime]System.DateTime [Probe] PerfProbe.Shapes::Now()[Optimized]"
        "0x2000 10 int32 [Probe] PerfProbe.Shapes::Arr(int32[],string[0...,0...])[Optimized]"
    ]

    let private index () =
        match PerfMapIndex.tryCreate labels with
        | Ok index -> index
        | Error error -> failtestf "The labels must index: %s" (PerfMapIndex.describe error)

    let private scope includes excludes =
        match TargetDiscoveryScope.tryCreate includes excludes with
        | Ok scope -> scope
        | Error error -> failtestf "The scope must parse: %A" error

    let private limit count =
        match TargetDiscoveryLimit.tryCreate count with
        | Ok limit -> limit
        | Error message -> failtest message

    let private event kind worker ip amount : TargetLedgerEvent =
        { Worker = worker; Sequence = 0UL; Kind = kind; ObservedGlobal = 0UL; LocalBefore = 0UL; Amount = amount; Requested = 0UL; Ip = ip; Detail = 0u; Time = 0UL }

    let private sample worker ip = event TargetLedgerEventKind.Sample worker ip 0UL

    let private samples worker ip count = List.replicate count (sample worker ip)

    let private classEvents =
        [ yield! samples 0 0x1000UL 3
          yield! samples 1 0x103FUL 2
          yield! samples 0 0x1040UL 4
          yield! samples 1 0x1060UL 1
          yield! samples 0 0x1085UL 2
          yield! samples 0 0x1090UL 1
          yield! samples 0 0x10a0UL 1
          yield! samples 0 0x10b5UL 1
          yield! samples 0 0x10c0UL 1
          yield! samples 0 0x10d0UL 1
          yield! samples 0 0x10e0UL 2
          yield! samples 0 0x0FFFUL 1
          yield! samples 0 0x1110UL 1
          yield! samples 0 0x2010UL 1
          event TargetLedgerEventKind.Publish 0 0x1000UL 750UL
          event TargetLedgerEventKind.Child 1 0x1040UL 50UL ]
        |> List.toArray

    let private rankOf includes events count =
        TargetDiscovery.rankEventsUnchecked (scope includes []) (index ()) events (limit count)

    let private expectOk result =
        match result with
        | Ok value -> value
        | Error error -> failtestf "The ranking must succeed, but it gave %A." error

    let private methodOf (t: Type) name (types: Type array) =
        match t.GetMethod(name, BindingFlags.Public ||| BindingFlags.Static ||| BindingFlags.Instance, null, types, null) with
        | null -> failtestf "The test method %s.%s must exist." t.FullName name
        | method -> method

    let private lineOf (path: string) (text: string) =
        let lines = File.ReadAllLines path
        match lines |> Array.tryFindIndex (fun line -> line.Contains(text, StringComparison.Ordinal)) with
        | Some index -> index + 1
        | None -> failtestf "The source %s must hold %s." path text

    let private run () : TargetProfileResult =
        { ElapsedNs = 1UL; GlobalNs = 0UL; EffectiveNs = 1UL; Experiment = TargetExperimentResult(); CloseCode = 0
          Threads = [| { RunDelayNs = 0L; StartRunDelayNs = 0L; StartTrace = [||]; WaitRunDelayNs = [||]; WaitRunDelaysDropped = 0
                         Result = TargetThreadResult(); FinishCode = 0; Error = null; StartedTicks = 0L; FinishedTicks = 0L } |]
          Events = Array.ofList (samples 0 0x1000UL 2)
          Replay = Ok { GlobalDelay = 0UL; Locals = [| 0UL |]; SelectedSamples = 0UL; Credited = 0UL; Publishes = 0; Children = 0; MeasuredPayment = 0UL
                        Deferred = 0; Failures = 0; MaxObservedDebt = 0UL; Blocks = 0; DependencyWakes = 0; SkippedDelay = 0UL }
          BindingError = None; Passed = true }

    let private inTemporaryFolder (action: string -> unit) =
        let folder = Path.Combine(Path.GetTempPath(), "coznet-discovery-" + Guid.NewGuid().ToString("N"))
        try
            Directory.CreateDirectory folder |> ignore
            action folder
        finally
            if Directory.Exists folder then Directory.Delete(folder, true)

    [<Tests>]
    let tests =
        testList "target discovery" [
            testList "perf map index" [
                test "real labels give their assembly, canonical symbol, and tier" {
                    let methods = (index ()).Entries |> Array.map (fun e -> e.Start, e.Method)
                    let methodAt start = methods |> Array.find (fst >> (=) start) |> snd
                    Expect.equal (methodAt 0x1000UL) (Some { Assembly = "Probe"; Symbol = "PerfProbe.Shapes::Selected(uint64)"; Tier = "[Optimized]" }) "A static method must parse."
                    Expect.equal (methodAt 0x1040UL |> Option.map _.Symbol) (Some "PerfProbe.Inst::Me(int32)") "The word instance must stay out of the symbol."
                    Expect.equal (methodAt 0x1060UL |> Option.map _.Symbol) (Some "PerfProbe.Shapes+Inner::Nested(int32)") "A nested type must keep its plus sign."
                    Expect.equal (methodAt 0x10a0UL |> Option.map _.Symbol) (Some "PerfProbe.Box`1[System.Int32]::Get(!0)") "A generic type must keep its arguments."
                    Expect.equal (methodAt 0x10c0UL |> Option.map _.Tier) (Some "[QuickJitted]") "The tier must be kept."
                    Expect.equal (methodAt 0x10e0UL |> Option.map _.Assembly) (Some "CozNet.TargetLocal") "The assembly must be kept."
                    Expect.equal (methodAt 0x10f0UL |> Option.map _.Symbol)
                        (Some "PerfProbe.Shapes::Structs(valuetype PerfProbe.Pair,valuetype PerfProbe.Color,valuetype [System.Runtime]System.DateTime)") "A bracket inside the parameters must stay in the symbol."
                    Expect.equal (methodAt 0x1100UL |> Option.map (fun m -> m.Assembly, m.Symbol)) (Some("Probe", "PerfProbe.Shapes::Now()")) "A bracket in the return type must not be taken as the assembly."
                    Expect.equal (methodAt 0x10b0UL) None "A stub label is not a method."
                }

                test "addresses resolve with an inclusive start and an exclusive end" {
                    let index = index ()
                    let startAt ip = index.TryFind ip |> Option.map _.Start
                    Expect.equal (startAt 0x1000UL, startAt 0x103FUL, startAt 0x1040UL) (Some 0x1000UL, Some 0x1000UL, Some 0x1040UL) "The last byte belongs to its entry, and the end belongs to the next."
                    Expect.equal (startAt 0x1060UL) (Some 0x1060UL) "An address without 0x must parse."
                    Expect.equal (startAt 0x1110UL, startAt 0x0FFFUL, startAt 0x2010UL) (None, None, None) "Addresses between, before, and after the entries are unmapped."
                }

                test "a malformed line gives its line number and an empty line is skipped" {
                    for bad in [ "zz 10 label"; "0x10 0 label"; "0x10 10"; "0x10 10  " ] do
                        Expect.equal (PerfMapIndex.tryCreate [ labels[0]; ""; bad ]) (Error(MalformedLine(3, bad))) $"The line '{bad}' must reject with its number."
                    Expect.isOk (PerfMapIndex.tryCreate [ ""; labels[0] ]) "An empty line must be skipped."
                }

                test "overlapping ranges reject" {
                    match PerfMapIndex.tryCreate [ "0x1000 40 void [P] A::a()[Optimized]"; "0x1020 40 void [P] B::b()[Optimized]" ] with
                    | Error(OverlappingRanges(first, second)) -> Expect.equal (first.Start, second.Start) (0x1000UL, 0x1020UL) "Both entries must be named."
                    | other -> failtestf "Overlapping ranges must reject, not give %A." other
                }
            ]

            testList "scope" [
                test "the caller must give a non-blank include prefix" {
                    Expect.equal (TargetDiscoveryScope.tryCreate [] []) (Error NoInclude) "A scope needs an include prefix."
                    Expect.equal (TargetDiscoveryScope.tryCreate [ " " ] []) (Error BlankPrefix) "A blank include prefix must reject."
                    Expect.equal (TargetDiscoveryScope.tryCreate [ "A" ] [ "" ]) (Error BlankPrefix) "A blank exclude prefix must reject."
                }

                test "an exclude prefix wins, matching is ordinal, and only the profiler assembly is always out" {
                    let method assembly symbol = { Assembly = assembly; Symbol = symbol; Tier = "[Optimized]" }
                    let wide = scope [ "PerfProbe."; "CozNet." ] [ "PerfProbe.Shapes+" ]
                    Expect.isTrue (TargetDiscoveryScope.contains wide (method "Probe" "PerfProbe.Shapes::Selected(uint64)")) "An included symbol must be in scope."
                    Expect.isFalse (TargetDiscoveryScope.contains wide (method "Probe" "PerfProbe.Shapes+Inner::Nested(int32)")) "An exclude prefix must win."
                    Expect.isFalse (TargetDiscoveryScope.contains wide (method "Probe" "perfprobe.Shapes::Selected(uint64)")) "Matching must be ordinal."
                    Expect.isFalse (TargetDiscoveryScope.contains wide (method "CozNet" "CozNet.TargetWait::post()")) "A profiler method must stay out of scope."
                    Expect.isTrue (TargetDiscoveryScope.contains wide (method "CozNet.TargetLocal" "CozNet.TargetLocal.Work::selected(uint64)")) "A CozNet.TargetLocal method can be in scope."
                }
            ]

            testList "ranking" [
                test "each sample falls in one class and the classes add up" {
                    let result = rankOf [ "PerfProbe."; "CozNet." ] classEvents 10 |> expectOk
                    let counts = result.Counts
                    Expect.equal counts.Samples 22L "Only sample events count."
                    Expect.equal (counts.Unmapped, counts.NotBindable, counts.OutOfScope, counts.InScope)
                        (3L, { NotMethodLabel = 1L; NotOptimized = 1L; NotUnique = 3L }, 1L, 13L) "Each class must hold its samples."
                    Expect.equal (counts.Unmapped + counts.NotBindable.NotMethodLabel + counts.NotBindable.NotOptimized + counts.NotBindable.NotUnique + counts.OutOfScope + counts.InScope)
                        counts.Samples "The classes must add up to the sample count."
                    Expect.equal (result.Ranked |> Array.map (fun m -> m.Symbol, m.Samples))
                        [| "PerfProbe.Shapes::Selected(uint64)", 5L; "PerfProbe.Inst::Me(int32)", 4L; "CozNet.TargetLocal.Work::selected(uint64)", 2L
                           "PerfProbe.Box`1[System.Int32]::Get(!0)", 1L; "PerfProbe.Shapes+Inner::Nested(int32)", 1L |]
                        "Methods must rank by count, then by ordinal symbol."
                    Expect.equal result.Ranked[0].WorkerSamples [| 0, 3L; 1, 2L |] "The worker counts must add up to the method count."
                    Expect.equal (result.OmittedMethods, result.OmittedSamples) (0, 0L) "A large limit omits nothing."
                }

                test "two optimized entries with one symbol are not bindable and are not joined" {
                    let result = rankOf [ "PerfProbe." ] (Array.ofList (samples 0 0x1080UL 2 @ samples 0 0x1090UL 3 @ samples 0 0x1000UL 1)) 10 |> expectOk
                    Expect.equal result.Counts.NotBindable.NotUnique 5L "Both generic code versions must be not bindable."
                    Expect.isFalse (result.Ranked |> Array.exists (fun m -> m.Symbol.Contains "Gen")) "A shared symbol must not be ranked."
                }

                test "an optimized entry with a second tier of the same symbol is not bindable" {
                    let lines = [ "0x1000 10 void [P] X::M()[QuickJitted]"; "0x2000 10 void [P] X::M()[Optimized]" ]
                    let index = match PerfMapIndex.tryCreate lines with Ok index -> index | Error error -> failtestf "%A" error
                    match TargetDiscovery.rankEventsUnchecked (scope [ "X::" ] []) index (Array.ofList (samples 0 0x2000UL 2 @ samples 0 0x1000UL 1)) (limit 5) with
                    | Error(NoSamplesInScope counts) ->
                        Expect.equal (counts.NotBindable.NotUnique, counts.NotBindable.NotOptimized) (2L, 1L) "CodeBinding rejects a symbol with two tiers, so discovery must not rank it."
                        Expect.isError (CodeBinding.tryCapture lines [| "X::M()" |]) "The binding must reject the symbol too."
                    | other -> failtestf "A symbol with two tiers must not rank, not give %A." other
                }

                test "a symbol inside a longer optimized label is not bindable" {
                    let lines = [ "0x1000 10 void [P] X.A::m()[Optimized]"; "0x2000 10 void [P] Y.X.A::m()[Optimized]" ]
                    let index = match PerfMapIndex.tryCreate lines with Ok index -> index | Error error -> failtestf "%A" error
                    match TargetDiscovery.rankEventsUnchecked (scope [ "X."; "Y." ] []) index (Array.ofList (samples 0 0x1000UL 1 @ samples 0 0x2000UL 1)) (limit 5) with
                    | Ok result ->
                        Expect.equal result.Counts.NotBindable.NotUnique 1L "The shorter symbol matches two labels, as CodeBinding would find."
                        Expect.equal (result.Ranked |> Array.map _.Symbol) [| "Y.X.A::m()" |] "The longer symbol stays bindable."
                    | Error error -> failtestf "The ranking must succeed, not give %A." error
                }

                test "a fixed set of permutations gives the same result" {
                    let events = classEvents
                    let expected = rankOf [ "PerfProbe."; "CozNet." ] events 3
                    let rotate n (a: 'a array) = Array.append (Array.skip n a) (Array.take n a)
                    let byParity parity = events |> Array.indexed |> Array.filter (fun (i, _) -> i % 2 = parity) |> Array.map snd
                    for permuted in [ Array.rev events; rotate 7 events; Array.append (byParity 0) (byParity 1) ] do
                        Expect.equal (rankOf [ "PerfProbe."; "CozNet." ] permuted 3) expected "The order of the events must not change the result."
                }

                test "the limit reports the omitted methods and samples" {
                    let result = rankOf [ "PerfProbe."; "CozNet." ] classEvents 2 |> expectOk
                    Expect.equal (result.Ranked |> Array.map _.Symbol) [| "PerfProbe.Shapes::Selected(uint64)"; "PerfProbe.Inst::Me(int32)" |] "The limit must keep the first methods."
                    Expect.equal (result.OmittedMethods, result.OmittedSamples) (3, 4L) "The limit must report what it removes."
                    Expect.equal (result.Counts.InScope) ((result.Ranked |> Array.sumBy _.Samples) + result.OmittedSamples) "In scope must equal ranked plus omitted samples."
                }

                test "ties rank by ordinal symbol" {
                    let result = rankOf [ "PerfProbe." ] (Array.ofList (samples 0 0x1060UL 2 @ samples 0 0x1040UL 2)) 5 |> expectOk
                    Expect.equal (result.Ranked |> Array.map _.Symbol) [| "PerfProbe.Inst::Me(int32)"; "PerfProbe.Shapes+Inner::Nested(int32)" |] "Equal counts must sort by ordinal symbol."
                }

                test "missing samples and missing in-scope samples give their own errors" {
                    Expect.equal (rankOf [ "PerfProbe." ] [| event TargetLedgerEventKind.Publish 0 0x1000UL 5UL |] 1) (Error NoSamples) "Events without samples must reject."
                    match rankOf [ "Other." ] classEvents 1 with
                    | Error(NoSamplesInScope counts) -> Expect.equal (counts.Samples, counts.InScope) (22L, 0L) "The error must carry the counts."
                    | other -> failtestf "A scope without samples must reject, not give %A." other
                    Expect.isError (TargetDiscoveryLimit.tryCreate 0) "A limit below 1 must reject."
                }
            ]

            testList "run checks" [
                test "a complete run without delay is ranked" {
                    let result = TargetDiscovery.rank (scope [ "PerfProbe." ] []) (index ()) (run ()) (limit 1) |> expectOk
                    Expect.equal (result.Ranked |> Array.map _.Symbol) [| "PerfProbe.Shapes::Selected(uint64)" |] "A complete run must rank."
                }

                test "each failure field makes the run incomplete" {
                    let withThread (result: TargetThreadResult) =
                        let r = run ()
                        { r with Threads = [| { r.Threads[0] with Result = result } |] }
                    let mutable failed = TargetThreadResult()
                    failed.Failure <- 2
                    let mutable lost = TargetThreadResult()
                    lost.Lost <- 1UL
                    let mutable full = TargetExperimentResult()
                    full.Failure <- 2u
                    let cases = [
                        "not passed", { run () with Passed = false }
                        "binding", { run () with BindingError = Some "moved" }
                        "close", { run () with CloseCode = 5 }
                        "journal_full", { run () with Experiment = full }
                        "thread failure", withThread failed
                        "lost samples", withThread lost
                        "finish", { run () with Threads = [| { (run ()).Threads[0] with FinishCode = 1 } |] }
                        "replay", { run () with Replay = Error "gap" } ]
                    for name, value in cases do
                        match TargetDiscovery.rank (scope [ "PerfProbe." ] []) (index ()) value (limit 1) with
                        | Error(RunIncomplete reasons) -> Expect.isNonEmpty reasons $"The case {name} must give a reason."
                        | other -> failtestf "The case %s must be incomplete, not give %A." name other
                }

                test "a run that added delay is rejected" {
                    let credited = { run () with Events = [| event TargetLedgerEventKind.Sample 0 0x1000UL 500UL |] }
                    Expect.equal (TargetDiscovery.rank (scope [ "PerfProbe." ] []) (index ()) { run () with GlobalNs = 7UL } (limit 1)) (Error(DelayAdded(7UL, 0))) "Global delay must reject."
                    Expect.equal (TargetDiscovery.rank (scope [ "PerfProbe." ] []) (index ()) credited (limit 1)) (Error(DelayAdded(0UL, 1))) "A credited sample must reject."
                }

                test "a ranked symbol binds and builds a design" {
                    let lines = [ "0x7f0000002000 4f uint64 [CozNet.TargetLocal] CozNet.TargetLocal.Work::selected(uint64)[Optimized]"
                                  "0x7f0000003000 40 void [CozNet.TargetLocal] CozNet.TargetLocal.Work::peer(uint64)[Optimized]" ]
                    let index = match PerfMapIndex.tryCreate lines with Ok index -> index | Error error -> failtestf "%A" error
                    let events = Array.ofList (samples 0 0x7f0000002010UL 3 @ samples 1 0x7f0000003000UL 1)
                    let result = TargetDiscovery.rankEventsUnchecked (scope [ "CozNet.TargetLocal." ] []) index events (limit 2) |> expectOk
                    let symbols = result.Ranked |> Array.map _.Symbol
                    match CodeBinding.tryCapture lines symbols with
                    | Error message -> failtestf "The ranked symbols must bind: %s" message
                    | Ok binding ->
                        Expect.equal (symbols |> Array.map binding.Range) (result.Ranked |> Array.map (fun m -> m.Start, m.End)) "The binding must give the ranked ranges."
                        Expect.isOk (TargetCausalDesign.tryCreate binding 1000000UL symbols symbols [| 50u |] 1 1 0 EndToEnd) "The ranked symbols must build a design."
                }
            ]

            testList "symbols" [
                test "verified shapes format as the runtime names them" {
                    let t = typeof<DiscoveryShapes>
                    let name = t.FullName
                    let cases = [
                        methodOf t "Selected" [| typeof<uint64> |], $"{name}::Selected(uint64)"
                        methodOf t "Over" [| typeof<int> |], $"{name}::Over(int32)"
                        methodOf t "Over" [| typeof<int64> |], $"{name}::Over(int64)"
                        methodOf t "Over" [| typeof<string> |], $"{name}::Over(string)"
                        methodOf t "Over" [| typeof<obj> |], $"{name}::Over(object)"
                        methodOf t "Prims" [| typeof<bool>; typeof<char>; typeof<sbyte>; typeof<byte>; typeof<int16>; typeof<uint16>; typeof<uint32>; typeof<float32>; typeof<float>; typeof<nativeint>; typeof<unativeint> |],
                            $"{name}::Prims(bool,char,int8,uint8,int16,uint16,uint32,float32,float64,native int,native uint)"
                        methodOf t "Arrays" [| typeof<int[]>; typeof<string[]>; typeof<obj[]>; typeof<uint32[]> |], $"{name}::Arrays(int32[],string[],object[],uint32[])"
                        methodOf t "ByRef" [| typeof<int>.MakeByRefType(); typeof<int64>.MakeByRefType(); typeof<float>.MakeByRefType() |], $"{name}::ByRef(int32&,int64&,float64&)"
                        methodOf t "Pointer" [| typeof<int>.MakePointerType() |], $"{name}::Pointer(int32*)"
                        methodOf t "NoArgs" [||], $"{name}::NoArgs()"
                        methodOf typeof<DiscoveryInstance> "Me" [| typeof<int> |], $"{typeof<DiscoveryInstance>.FullName}::Me(int32)"
                        methodOf typeof<DiscoveryModule.Inner.Marker>.DeclaringType "nested" [| typeof<int> |], $"{typeof<DiscoveryModule.Inner.Marker>.DeclaringType.FullName}::nested(int32)"
                        methodOf typeof<DiscoveryModule.Marker>.DeclaringType "selected" [| typeof<uint64> |], $"{typeof<DiscoveryModule.Marker>.DeclaringType.FullName}::selected(uint64)" ]
                    for method, expected in cases do
                        Expect.equal (TargetSymbol.tryFormat method) (Ok expected) $"The method {method.Name} must format as {expected}."
                    Expect.stringContains (typeof<DiscoveryModule.Inner.Marker>.DeclaringType.FullName) "+Inner" "A nested module must be a nested type."
                }

                test "shapes outside the verified subset reject" {
                    let t = typeof<DiscoveryShapes>
                    Expect.equal (TargetSymbol.tryFormat (t.GetMethod "Gen")) (Error GenericMethod) "A generic method must reject."
                    match TargetSymbol.tryFormat (methodOf typeof<DiscoveryBox<int>> "Get" [| typeof<int> |]) with
                    | Error(GenericType _) -> ()
                    | other -> failtestf "A generic type must reject, not give %A." other
                    for name, types in [ "ValueType", [| typeof<DateTime> |]; "Class", [| t |]; "Rank2", [| typeof<int[,]> |]; "Composed", [| typeof<int[]>.MakeByRefType() |] ] do
                        match TargetSymbol.tryFormat (methodOf t name types) with
                        | Error(UnsupportedParameter(0, _)) -> ()
                        | other -> failtestf "The method %s must reject its parameter, not give %A." name other
                }
            ]

            testList "source anchors" [
                test "an external PDB gives the first visible line of the method" {
                    let method = methodOf typeof<DiscoveryShapes> "AnchorTarget" [| typeof<int> |]
                    let source = Path.Combine(__SOURCE_DIRECTORY__, __SOURCE_FILE__)
                    match TargetSourceAnchor.tryFind method with
                    | Ok anchor ->
                        Expect.stringEnds anchor.Document "TargetDiscoveryTests.fs" "The document must be this file."
                        Expect.equal anchor.Line (lineOf source "value + 41") "The line must be the first line of the body."
                    | Error error -> failtestf "The external PDB must give an anchor, not %A." error
                }

                test "an embedded PDB gives the first visible line of the method" {
                    let method = typeof<CozNet.DiscoveryFixture.Embedded.Marker>.DeclaringType.GetMethod "anchored"
                    let source = Path.Combine(__SOURCE_DIRECTORY__, "..", "CozNet.DiscoveryFixture", "Embedded.fs")
                    let pdb = Path.ChangeExtension(method.Module.Assembly.Location, ".pdb")
                    Expect.isFalse (File.Exists pdb) "The fixture must have no external PDB."
                    match TargetSourceAnchor.tryFind method with
                    | Ok anchor ->
                        Expect.stringEnds anchor.Document "Embedded.fs" "The document must be the fixture source."
                        Expect.equal anchor.Line (lineOf source "value + 1") "The line must be the first line of the body."
                    | Error error -> failtestf "The embedded PDB must give an anchor, not %A." error
                }

                test "a missing, mismatched, or malformed PDB gives its own error" {
                    let token = (methodOf typeof<DiscoveryShapes> "AnchorTarget" [| typeof<int> |]).MetadataToken
                    let name = Path.GetFileName typeof<DiscoveryShapes>.Assembly.Location
                    inTemporaryFolder (fun folder ->
                        let assembly = Path.Combine(folder, name)
                        File.Copy(typeof<DiscoveryShapes>.Assembly.Location, assembly)
                        match TargetSourceAnchor.tryFindIn assembly token with
                        | Error(PdbNotFound [ path ]) -> Expect.equal (Path.GetDirectoryName path) folder "Only the file next to the assembly must be searched."
                        | other -> failtestf "A copy without its PDB must give PdbNotFound, not %A." other
                        let pdb = Path.ChangeExtension(assembly, ".pdb")
                        File.Copy(Path.ChangeExtension(typeof<CodeBinding>.Assembly.Location, ".pdb"), pdb)
                        Expect.equal (TargetSourceAnchor.tryFindIn assembly token) (Error(PdbMismatch pdb)) "A PDB of another assembly must not be used."
                        File.WriteAllBytes(pdb, [| 1uy; 2uy; 3uy; 4uy |])
                        match TargetSourceAnchor.tryFindIn assembly token with
                        | Error(PdbMalformed(path, _)) -> Expect.equal path pdb "The malformed file must be named."
                        | other -> failtestf "A malformed PDB must give PdbMalformed, not %A." other)
                }

                test "an assembly that is not a PE image gives an error, not an exception" {
                    inTemporaryFolder (fun folder ->
                        let assembly = Path.Combine(folder, "bad.dll")
                        File.WriteAllBytes(assembly, [| 1uy; 2uy; 3uy |])
                        match TargetSourceAnchor.tryFindIn assembly 0x06000001 with
                        | Error(AssemblyMalformed(path, _)) -> Expect.equal path assembly "The file must be named."
                        | other -> failtestf "A file that is not a PE image must give AssemblyMalformed, not %A." other)
                }

                test "a locked assembly gives an error, not an exception" {
                    if not (OperatingSystem.IsWindows()) then skiptest "Only Windows locks a file against other readers."
                    inTemporaryFolder (fun folder ->
                        let assembly = Path.Combine(folder, "locked.dll")
                        File.Copy(typeof<DiscoveryShapes>.Assembly.Location, assembly)
                        use _lock = new FileStream(assembly, FileMode.Open, FileAccess.Read, FileShare.None)
                        match TargetSourceAnchor.tryFindIn assembly 0x06000001 with
                        | Error(AssemblyUnreadable(path, _)) -> Expect.equal path assembly "The file must be named."
                        | other -> failtestf "A locked file must give AssemblyUnreadable, not %A." other)
                }

                test "a method of a dynamic assembly has no assembly file" {
                    let builder = Emit.AssemblyBuilder.DefineDynamicAssembly(AssemblyName "CozNet.DiscoveryDynamic", Emit.AssemblyBuilderAccess.Run)
                    let typeBuilder = builder.DefineDynamicModule("main").DefineType("Dynamic", TypeAttributes.Public)
                    let methodBuilder = typeBuilder.DefineMethod("run", MethodAttributes.Public ||| MethodAttributes.Static, typeof<Void>, [||])
                    methodBuilder.GetILGenerator().Emit(Emit.OpCodes.Ret)
                    let method = typeBuilder.CreateType().GetMethod "run"
                    Expect.equal (TargetSourceAnchor.tryFind method) (Error NoAssemblyFile) "A dynamic assembly has no file."
                    let dynamicMethod = Emit.DynamicMethod("hosted", typeof<Void>, [||], typeof<DiscoveryShapes>.Module)
                    dynamicMethod.GetILGenerator().Emit(Emit.OpCodes.Ret)
                    Expect.equal (TargetSourceAnchor.tryFind dynamicMethod) (Error NoAssemblyFile) "A dynamic method in an ordinary module has no metadata token."
                }

                test "methods without visible sequence points and missing files give their own errors" {
                    let run = typeof<IDiscoveryAbstract>.GetMethod "Run"
                    Expect.equal (TargetSourceAnchor.tryFind run) (Error(MethodNotInPdb run.MetadataToken)) "An abstract method has no sequence points."
                    Expect.equal (TargetSourceAnchor.tryFindIn "" 0x06000001) (Error NoAssemblyFile) "An empty path must reject."
                    Expect.equal (TargetSourceAnchor.tryFindIn typeof<DiscoveryShapes>.Assembly.Location 0x02000001) (Error(MethodNotInPdb 0x02000001)) "A token that is not a method must reject."
                    Expect.equal (TargetSourceAnchor.tryFindIn typeof<DiscoveryShapes>.Assembly.Location 0x06000000) (Error(MethodNotInPdb 0x06000000)) "A method token with row 0 must reject."
                    let point hidden line : TargetSourceAnchor.Point = { Hidden = hidden; Document = "a.fs"; Line = line; Column = 1 }
                    Expect.equal (TargetSourceAnchor.firstVisible [ point true 0xFEEFEE; point false 7; point false 9 ]) (Some { Document = "a.fs"; Line = 7; Column = 1 }) "A hidden sequence point must be skipped."
                    Expect.equal (TargetSourceAnchor.firstVisible [ point true 0xFEEFEE ]) None "Only hidden points give no anchor."
                }
            ]
        ]
