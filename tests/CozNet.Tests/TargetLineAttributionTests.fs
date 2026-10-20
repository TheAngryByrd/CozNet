namespace CozNet.Tests

open System
open System.IO
open System.Text.Json
open CozNet
open Expecto

type LineFixture =
    static member Loop(rounds: int) =
        let mutable total = 0L
        for index in 1 .. rounds do
            total <- total + int64 index
        if total > 100L then total else total * 2L

    static member Generic<'T>(value: 'T) = value

    static member Loop(rounds: int64) = rounds

    static member Nested(values: byref<int[]>) = values.Length

module TargetLineAttributionTests =
    let private symbol = "CozNet.Tests.LineFixture::Loop(int32)"
    let private start = 0x5000UL
    let private size = 78UL
    let private assemblyPath = typeof<LineFixture>.Assembly.Location
    let private source = Path.Combine(__SOURCE_DIRECTORY__, __SOURCE_FILE__)
    let private loop = typeof<LineFixture>.GetMethod("Loop", [| typeof<int> |])
    let private noMapping, prolog, epilog = 0xFFFFFFFFu, 0xFFFFFFFEu, 0xFFFFFFFDu

    let private record () =
        match TargetSourceAnchor.tryReadMethod assemblyPath loop.MetadataToken with
        | Ok value -> value
        | Error error -> failtestf "The fixture method must read: %A" error

    let private outcomeFor (assembly: string) (codeStart: uint64) =
        let lines = [ $"0x{codeStart:x} {size:x} int64 [{assembly}] {symbol}[Optimized]" ]
        let snapshot = match PerfMapSnapshot.trySupplied "synthetic: line fixture" lines with Ok s -> s | Error e -> failtestf "%A" e
        let binding = match CodeBinding.tryCapture lines [| symbol |] with Ok b -> b | Error e -> failtest e
        let run = { TargetDiscoveryTests.run () with Events = Array.ofList (TargetDiscoveryTests.samples 0 codeStart 3) }
        match TargetDiscoveryWorkflow.prepare (TargetDiscoveryTests.scope [ "CozNet.Tests." ] []) binding run snapshot None (TargetDiscoveryTests.limit 1) with
        | Ok outcome -> outcome
        | Error error -> failtestf "The outcome must build: %A" error

    let private outcomeAt codeStart = outcomeFor "CozNet.Tests" codeStart

    let private evidence entries : TargetCodeEvidence =
        { Label = "synthetic: line fixture map"; ModuleVersionId = (record ()).ModuleVersionId; MethodToken = loop.MetadataToken; CodeStart = start
          Regions = [ { Start = start; Size = size } ]; NativeBodyCount = 1; Entries = entries }

    let private synthetic =
        [| struct (prolog, 0u, 8u); struct (0u, 8u, 20u); struct (11u, 20u, 30u); struct (27u, 30u, 40u)
           struct (35u, 40u, 50u); struct (11u, 50u, 60u); struct (noMapping, 60u, 66u); struct (epilog, 66u, 78u) |]

    let private prepare (e: TargetCodeEvidence) = TargetLineAttribution.tryPrepare (outcomeAt start) symbol assemblyPath e

    let private resolver entries =
        match prepare (evidence entries) with
        | Ok value -> value
        | Error errors -> failtestf "The resolver must prepare, not give %A." errors

    let private lineOf text = TargetDiscoveryTests.lineOf source text

    let private inTemporaryFolder action = TargetDiscoveryTests.inTemporaryFolder action

    let private lineAt r offset =
        match TargetLineAttribution.resolve r (start + offset) with
        | Line location -> location
        | other -> failtestf "Native offset %d must give a line, not %A." offset other

    let private retained () =
        let path = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "reliability", "features", "runtime-probes", "data", "native-mapping", "linux", "linux-perf-registered-branch.json")
        use document = JsonDocument.Parse(File.ReadAllText path)
        let raw (value: int) = if value < 0 then uint32 (int64 value + 0x100000000L) else uint32 value
        document.RootElement.GetProperty("nativeMap").EnumerateArray()
        |> Seq.map (fun e -> struct (raw (e.GetProperty("ilOffset").GetInt32()), e.GetProperty("start").GetUInt32(), e.GetProperty("end").GetUInt32()))
        |> Seq.toArray

    [<Tests>]
    let tests =
        testList "target line attribution" [
            test "the fixture method has the expected PDB shape" {
                let r = record ()
                Expect.equal (r.IlBodySize, r.Generic, r.TypeName, r.MethodName) (Some 40, false, "CozNet.Tests.LineFixture", "Loop") "The fixture method must read from its PE and PDB."
                Expect.isTrue (r.Points |> Array.exists _.Hidden) "The fixture method must have a hidden sequence point."
            }

            testList "preparation" [
                test "an undiscovered symbol, a range, a region, and a body count reject" {
                    Expect.equal (TargetLineAttribution.tryPrepare (outcomeAt start) "Other::m()" assemblyPath (evidence synthetic)) (Error [ CandidateNotDiscovered "Other::m()" ]) "An undiscovered symbol must reject."
                    match TargetLineAttribution.tryPrepare (outcomeAt 0x6000UL) symbol assemblyPath (evidence synthetic) with
                    | Error [ RangeMismatch((0x6000UL, 0x604EUL), (0x5000UL, 0x504EUL)) ] -> ()
                    | other -> failtestf "A range that differs from the discovered range must reject, not give %A." other
                    let e = evidence synthetic
                    Expect.equal (prepare { e with Regions = [ { Start = start; Size = 60UL }; { Start = 0x9000UL; Size = 18UL } ] }) (Error [ ColdCodeUnsupported 2 ]) "Cold code must reject."
                    Expect.equal (prepare { e with NativeBodyCount = 2 }) (Error [ MultipleBodiesUnsupported 2 ]) "A second body must reject."
                    Expect.equal (prepare { e with Label = " " }) (Error [ EvidenceLabelBlank ]) "The evidence must name its source."
                    Expect.equal (prepare { e with CodeStart = UInt64.MaxValue - 10UL; Regions = [ { Start = UInt64.MaxValue - 10UL; Size = size } ] }) (Error [ RangeOverflow ]) "An address overflow must reject."
                }

                test "a module, an assembly, a method, and a generic method that do not match reject" {
                    let e = evidence synthetic
                    match prepare { e with ModuleVersionId = Guid.Empty } with
                    | Error [ IdentityMismatch("module version id", _, _) ] -> ()
                    | other -> failtestf "Another module version id must reject, not give %A." other
                    let generic = typeof<LineFixture>.GetMethod "Generic"
                    match prepare { e with MethodToken = generic.MetadataToken } with
                    | Error errors ->
                        Expect.contains errors SharedGenericUnsupported "A generic method must reject."
                        Expect.isTrue (errors |> List.exists (function IdentityMismatch("method", _, _) -> true | _ -> false)) "Another method row must give a method mismatch."
                    | Ok _ -> failtest "Another method must not prepare."
                    match TargetLineAttribution.tryPrepare (outcomeFor "OtherAssembly" start) symbol assemblyPath e with
                    | Error [ IdentityMismatch("assembly", "OtherAssembly", "CozNet.Tests") ] -> ()
                    | other -> failtestf "Another assembly must reject, not give %A." other
                    let overload = typeof<LineFixture>.GetMethod("Loop", [| typeof<int64> |])
                    match prepare { e with MethodToken = overload.MetadataToken } with
                    | Error [ IdentityMismatch("signature", "int32", "int64") ] -> ()
                    | other -> failtestf "An overload with the same name must reject, not give %A." other
                    for code in [ Reflection.Metadata.PrimitiveTypeCode.Void; Reflection.Metadata.PrimitiveTypeCode.TypedReference ] do
                        Expect.equal (TargetSourceAnchor.primitiveName code) TargetSourceAnchor.unsupportedType $"{code} is not a supported parameter type."
                    let nested = typeof<LineFixture>.GetMethod "Nested"
                    match prepare { e with MethodToken = nested.MetadataToken } with
                    | Error errors -> Expect.contains errors (IdentityMismatch("signature", "int32", "?")) "A second wrapper must not decode as a supported type."
                    | Ok _ -> failtest "A method with a nested wrapper must not prepare."
                    match prepare { e with MethodToken = 0x06FFFFFF } with
                    | Error [ SourceError(MethodNotInPdb 0x06FFFFFF) ] -> ()
                    | other -> failtestf "A token without a row must reject, not give %A." other
                }

                test "a PE file without metadata gives an error, not an exception" {
                    if not (OperatingSystem.IsWindows()) then skiptest "The fixture copies kernel32.dll, which only Windows has."
                    inTemporaryFolder (fun folder ->
                        let path = Path.Combine(folder, "nometadata.dll")
                        File.WriteAllBytes(path, File.ReadAllBytes(Path.Combine(Environment.SystemDirectory, "kernel32.dll")))
                        match TargetSourceAnchor.tryReadMethod path loop.MetadataToken with
                        | Error(AssemblyMalformed(file, _)) -> Expect.equal file path "The file must be named."
                        | other -> failtestf "A PE file without metadata must give AssemblyMalformed, not %A." other)
                }
            ]

            testList "decoding" [
                test "special values, IL bounds, inverted ranges, ends, and overlaps" {
                    let e = evidence synthetic
                    let check entries expected message =
                        Expect.equal (prepare { e with Entries = entries }) (Error expected) message
                    check [| struct (40u, 0u, 78u) |] [ IlOffsetBeyondBody(0, 40u) ] "An IL offset at the body size must reject."
                    check [| struct (0xFFFFFFFCu, 0u, 78u) |] [ IlOffsetBeyondBody(0, 0xFFFFFFFCu) ] "An unknown special value must reject, not resolve to a line."
                    check [| struct (0u, 10u, 5u) |] [ InvertedRange 0 ] "An inverted range must reject."
                    match prepare { e with Entries = [| struct (0u, 0u, 79u) |] } with
                    | Error [ UnsupportedMapShape reason ] -> Expect.stringContains reason "above the code size" "The end must stay inside the body."
                    | other -> failtestf "An end above the code size must reject, not give %A." other
                    match prepare { e with Entries = [| struct (0u, 0u, 40u); struct (3u, 30u, 78u) |] } with
                    | Error [ UnsupportedMapShape reason ] -> Expect.stringContains reason "overlap" "An overlap is an unsupported shape."
                    | other -> failtestf "An overlap must reject, not give %A." other
                    check [||] [ EmptyMap ] "An empty map must reject."
                }

                test "an unsorted map with a zero-length entry and an uncovered start prepares" {
                    let entries = [| struct (35u, 40u, 78u); struct (3u, 20u, 20u); struct (0u, 4u, 40u) |]
                    let r = resolver entries
                    Expect.equal r.Uncovered [| start, start + 4UL |] "The start before the first entry must be uncovered."
                    Expect.equal (TargetLineAttribution.resolve r (start + 2UL)) Uncovered "An uncovered address must not take a neighbor line."
                    Expect.equal (lineAt r 20UL).IlStart 0 "A zero-length entry must hold no address."
                    entries[0] <- struct (0u, 40u, 78u)
                    (r.Uncovered)[0] <- (0UL, 0UL)
                    Expect.equal ((lineAt r 40UL).IlStart, r.Uncovered) (35, [| start, start + 4UL |]) "Caller and returned arrays must not change the resolver."
                }
            ]

            testList "resolution" [
                test "edges, special entries, and the body bounds" {
                    let r = resolver synthetic
                    Expect.equal (TargetLineAttribution.resolve r start, TargetLineAttribution.resolve r (start + 7UL)) (InProlog, InProlog) "The prolog holds its first and last address."
                    Expect.equal (lineAt r 8UL).StartLine (lineOf "let mutable total = 0L") "The end of the prolog belongs to the next entry."
                    Expect.equal (TargetLineAttribution.resolve r (start + 60UL), TargetLineAttribution.resolve r (start + 66UL)) (Unmapped, InEpilog) "Special entries must not give a line."
                    Expect.equal (TargetLineAttribution.resolve r (start - 1UL), TargetLineAttribution.resolve r (start + size)) (OutsideBody, OutsideBody) "Addresses outside the body must be outside."
                    Expect.equal (TargetLineAttribution.resolve r (start + 77UL)) InEpilog "The last address belongs to the body."
                }

                test "sequence points: exact, between, hidden, and the last interval" {
                    let r = resolver synthetic
                    let points = (record ()).Points |> Array.sortBy _.Offset
                    let at11 = lineAt r 20UL
                    Expect.equal (at11.StartLine, at11.IlStart) (lineOf "total <- total + int64 index", 11) "An exact offset must give its point."
                    Expect.equal (at11.IlEnd, (lineAt r 55UL).StartLine) (points |> Array.find (fun p -> p.Offset > 11) |> _.Offset, at11.StartLine) "One IL offset can have two native ranges."
                    let hidden = points |> Array.find _.Hidden
                    Expect.equal hidden.Offset 27 "The fixture has its hidden point at IL offset 27."
                    Expect.equal (TargetLineAttribution.resolve r (start + 30UL)) (HiddenSequencePoint 27u) "A hidden point must not fall to a neighbor line."
                    let last = lineAt r 45UL
                    Expect.equal (last.IlStart, last.IlEnd, last.StartLine) (35, 40, lineOf "if total > 100L then total else total * 2L") "The last interval must end at the IL body size."
                    Expect.equal (lineAt (resolver [| struct (1u, 0u, 78u) |]) 0UL).IlStart 0 "An offset between points gives the point below it."
                    let point offset : TargetSourceAnchor.Point = { Offset = offset; Hidden = false; Document = "a.fs"; Line = offset; Column = 1; EndLine = offset; EndColumn = 2 }
                    Expect.equal (TargetLineAttribution.lineFor [| point 4; point 9 |] 12 2u) (NoSequencePoint 2u) "An offset before the first point has no point."
                    match TargetLineAttribution.lineFor [| point 4; point 9 |] 12 10u with
                    | Line location -> Expect.equal (location.IlStart, location.IlEnd) (9, 12) "The last interval ends at the IL body size."
                    | other -> failtestf "An offset after the last point must give it, not %A." other
                }

                test "every resolver carries its limitations" {
                    Expect.equal (resolver synthetic).Limitations
                        [ SuppliedEvidence "synthetic: line fixture map"; OptimizedBoundariesAreCoarse; SequencePointRangeInferred; InlineesAttributedToCallSite
                          IlRewriteNotExcluded; MapEntryTimingNotProven; CodeLifetimeNotProven; NoCallchainAttribution ] "The limitations must be stated."
                }

                test "a retained runtime map has a shape that the resolver accepts" {
                    let entries = retained ()
                    let r = match prepare { evidence entries with Label = "retained: runtime-probes linux-perf-registered-branch" } with
                            | Ok value -> value
                            | Error errors -> failtestf "The retained map must prepare, not give %A." errors
                    Expect.equal r.Uncovered [||] "The retained map tiles the body."
                    for struct (il, s, _) in entries do
                        match TargetLineAttribution.resolve r (start + uint64 s), il with
                        | InProlog, il when il = prolog -> ()
                        | InEpilog, il when il = epilog -> ()
                        | Line location, il when il < 40u -> Expect.isLessThanOrEqual location.IlStart (int il) "The map belongs to another method, so only the IL bound is checked."
                        | HiddenSequencePoint offset, il -> Expect.equal offset il "A hidden point must keep the IL offset."
                        | other, il -> failtestf "Entry %d gave %A." il other
                }
            ]
        ]
