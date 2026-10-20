namespace CozNet.Tests

open System
open System.Buffers.Binary
open System.IO
open CozNet
open Expecto

module TargetMapCaptureTests =
    let private fixture name =
        File.ReadAllBytes(Path.Combine(__SOURCE_DIRECTORY__, "fixtures", "map-capture", name))

    let private parse bytes =
        match TargetMapCapture.tryParse bytes with
        | Ok record -> record
        | Error error -> failtestf "The record must parse, not give %A." error

    let private synthetic =
        match TargetMapCaptureSource.trySynthetic "native map capture fixture" with
        | Ok source -> source
        | Error error -> failtest error

    let private put32 (bytes: byte array) offset (value: uint32) = BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value)
    let private put16 (bytes: byte array) offset (value: uint16) = BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), value)

    let private mutated name (change: byte array -> unit) =
        let bytes = fixture name
        change bytes
        TargetMapCapture.tryParse bytes

    let private fixtureMvid = Guid "11223344-5566-7788-99aa-bbccddeeff01"
    let private symbol = "CozNet.Tests.LineFixture::Loop(int32)"
    let private loop = typeof<LineFixture>.GetMethod("Loop", [| typeof<int> |])

    let private outcomeAt (codeStart: uint64) (size: uint64) =
        let lines = [ $"0x{codeStart:x} {size:x} int64 [CozNet.Tests] {symbol}[Optimized]" ]
        let snapshot = match PerfMapSnapshot.trySupplied "synthetic: map capture fixture" lines with Ok s -> s | Error e -> failtestf "%A" e
        let binding = match CodeBinding.tryCapture lines [| symbol |] with Ok b -> b | Error e -> failtest e
        let run = { TargetDiscoveryTests.run () with Events = Array.ofList (TargetDiscoveryTests.samples 0 codeStart 3) }
        match TargetDiscoveryWorkflow.prepare (TargetDiscoveryTests.scope [ "CozNet.Tests." ] []) binding run snapshot None (TargetDiscoveryTests.limit 1) with
        | Ok outcome -> outcome
        | Error error -> failtestf "The outcome must build: %A" error

    let private hr (value: uint32) = int value

    let private expectedStatus =
        [ "captured.bin", Completed Captured
          "code-info-failed.bin", Completed(CodeInfoFailed(hr 0x80070057u))
          "event-mask-failed.bin", Completed(EventMaskFailed(hr 0x80004005u))
          "flags.bin", Completed Captured
          "info-unavailable.bin", Completed(InfoUnavailable(hr 0x80004002u))
          "insufficient-starts.bin", Completed(StartsFailed(hr 0x8007007Au))
          "initialize-internal-error.bin", Completed(InternalError(hr 0x80004005u))
          "internal-error.bin", Completed(InternalError(hr 0x80004005u))
          "jit-failed.bin", Completed(JitFailed(hr 0x80004005u))
          "map-count-changed.bin", Completed(MapCountChanged 6u)
          "map-empty.bin", Completed MapEmpty
          "map-failed.bin", Completed(MapFailed(hr 0x80131376u))
          "map-too-large.bin", Completed MapTooLarge
          "metadata-failed.bin", Completed(MetadataFailed(hr 0x80070005u))
          "mvid-mismatch.bin", Completed MvidMismatch
          "no-body.bin", Completed NoNativeBody
          "no-region.bin", Completed NoCodeRegion
          "not-seen.bin", NotSeen
          "scope-failed.bin", Completed(MetadataFailed(hr 0x8000FFFFu))
          "second-map-failed.bin", Completed(MapFailed(hr 0x80004005u))
          "selection-invalid.bin", Completed(SelectionInvalid(hr 0x80070057u))
          "starts-failed.bin", Completed(StartsFailed(hr 0x8007000Eu))
          "three-regions.bin", Completed CodeInfoUnexpectedCount
          "two-bodies.bin", Completed MultipleBodies ]

    [<Tests>]
    let tests =
        testList "target map capture" [
            testList "native fixtures" [
                test "every native record parses to its outcome and only a clean capture gives evidence" {
                    let files = Directory.GetFiles(Path.Combine(__SOURCE_DIRECTORY__, "fixtures", "map-capture"), "*.bin") |> Array.map Path.GetFileName |> Array.sort
                    Expect.equal files (expectedStatus |> List.map fst |> List.sort |> List.toArray) "Each native record must have an expected outcome."
                    for name, status in expectedStatus do
                        let r = parse (fixture name)
                        Expect.equal r.Status status $"{name} must keep its outcome and its signed HRESULT."
                        match TargetMapCapture.toEvidence synthetic r, name with
                        | Ok _, "captured.bin" -> ()
                        | Ok _, _ -> failtestf "%s must not give evidence." name
                        | Error error, "captured.bin" -> failtestf "captured.bin must give evidence, not %A." error
                        | Error _, _ -> ()
                    let two = parse (fixture "two-bodies.bin")
                    Expect.equal (two.BodiesReported, two.BodyStarts) (2u, [| 0x7000UL; 0x8000UL |]) "MultipleBodies must keep both starts."
                    let three = parse (fixture "three-regions.bin")
                    Expect.equal (three.RegionsReported, three.Regions.Length) (3u, 2) "CodeInfoUnexpectedCount must keep the reported count and two regions."
                    let large = parse (fixture "map-too-large.bin")
                    Expect.equal (large.EntriesReported, large.Entries.Length) (4097u, 0) "MapTooLarge must keep the count and copy no entry."
                    let insufficient = parse (fixture "insufficient-starts.bin")
                    Expect.equal (insufficient.BodiesReported, insufficient.BodyStarts) (3u, [||]) "StartsFailed must keep the reported body count."
                }

                test "a clean capture keeps its identity, its counts, and its raw entries" {
                    let r = parse (fixture "captured.bin")
                    Expect.equal r.Status (Completed Captured) "The record must be a clean capture."
                    Expect.isEmpty r.Flags "A clean capture has no flag."
                    Expect.equal (r.FunctionId, r.ModuleId, r.MethodToken) (0xF00UL, 0x5000UL, 0x06000012u) "The identity must be kept."
                    Expect.equal (r.ModuleVersionId, r.SelectedModuleVersionId) (fixtureMvid, fixtureMvid) "The MVID must keep the GUID byte order."
                    Expect.equal (r.BodiesReported, r.BodyStarts) (1u, [| 0x7000UL |]) "The body must be kept."
                    Expect.equal (r.RegionsReported, r.Regions) (1u, [ { Start = 0x7000UL; Size = 0x40UL } ]) "The region must be kept."
                    Expect.equal r.EntriesReported 6u "The reported map count must be kept."
                    Expect.equal
                        r.Entries
                        [| struct (0xFFFFFFFEu, 0u, 8u); struct (0u, 8u, 20u); struct (3u, 20u, 20u); struct (7u, 24u, 40u)
                           struct (0xFFFFFFFFu, 40u, 48u); struct (0xFFFFFFFDu, 48u, 64u) |]
                        "Special values, a zero-length entry, and a gap must stay raw and in order."
                    Expect.equal (r.ModuleNameReported, r.ModuleName) (27u, "/app/CozNet.TargetLocal.dll") "The module name must be kept."
                    Expect.equal r.Counters { FunctionInfoFailures = 0u; ModuleInfoFailures = 0u; NameTruncations = 0u; UnsafeCallbacks = 0u } "No notification failed."
                }

                test "a record before any selected compilation is NotSeen" {
                    let r = parse (fixture "not-seen.bin")
                    Expect.equal (r.Status, r.Flags, r.Entries) (NotSeen, Set.empty, [||]) "The record must be NotSeen and hold no entry."
                    Expect.equal r.SelectedModuleVersionId fixtureMvid "The selected MVID must be kept."
                    Expect.equal (TargetMapCapture.toEvidence synthetic r) (Error CaptureNotSeen) "A NotSeen record must not give evidence."
                }

                test "failure records stay diagnostic records with their HRESULT and counts" {
                    let invalid = parse (fixture "selection-invalid.bin")
                    Expect.equal invalid.Status (Completed(SelectionInvalid -2147024809)) "SelectionInvalid must keep E_INVALIDARG as a signed value."
                    Expect.equal (TargetMapCapture.toEvidence synthetic invalid) (Error(CaptureFailed(SelectionInvalid -2147024809))) "A failure must not give evidence."
                    let changed = parse (fixture "map-count-changed.bin")
                    Expect.equal changed.Status (Completed(MapCountChanged 6u)) "MapCountChanged must keep the first count."
                    Expect.equal (changed.EntriesReported, changed.Entries.Length) (5u, 5) "MapCountChanged must keep the second count and its copied entries."
                    Expect.equal (TargetMapCapture.toEvidence synthetic changed) (Error(CaptureFailed(MapCountChanged 6u))) "A count change must not give evidence."
                }

                test "a capture with invalidation flags keeps its outcome and gives no evidence" {
                    let r = parse (fixture "flags.bin")
                    Expect.equal r.Status (Completed Captured) "A flag must not change the primary outcome."
                    Expect.equal r.Flags (Set.ofList [ SecondNotification; UnloadDuringCapture; Closed ]) "Every flag must be kept."
                    Expect.equal (TargetMapCapture.toEvidence synthetic r) (Error(CaptureInvalidated [ SecondNotification; UnloadDuringCapture; Closed ])) "An invalidated capture must not give evidence."
                }

                test "a clean capture gives synthetic evidence that the resolver checks" {
                    let r = parse (fixture "captured.bin")
                    match TargetMapCapture.toEvidence synthetic r with
                    | Error error -> failtestf "A clean capture must give evidence, not %A." error
                    | Ok evidence ->
                        Expect.equal evidence.Label "synthetic: native map capture fixture" "The label must name a synthetic source."
                        Expect.equal (evidence.CodeStart, evidence.NativeBodyCount, evidence.MethodToken) (0x7000UL, 1, 0x06000012) "The evidence must keep the capture identity."
                        match TargetLineAttribution.tryPrepare (outcomeAt 0x7000UL 0x40UL) symbol typeof<LineFixture>.Assembly.Location evidence with
                        | Error errors -> Expect.isTrue (errors |> List.exists (function IdentityMismatch("module version id", _, _) -> true | _ -> false)) "The fixture MVID is not the test assembly MVID."
                        | Ok _ -> failtest "The fixture identity must not match the test assembly."
                    let bytes = fixture "captured.bin"
                    let mvid = typeof<LineFixture>.Module.ModuleVersionId.ToByteArray()
                    Array.blit mvid 0 bytes 48 16
                    Array.blit mvid 0 bytes 64 16
                    put32 bytes 80 (uint32 loop.MetadataToken)
                    match TargetMapCapture.tryParse bytes |> Result.mapError string |> Result.bind (TargetMapCapture.toEvidence synthetic >> Result.mapError string) with
                    | Error error -> failtestf "The retargeted capture must give evidence, not %s." error
                    | Ok evidence ->
                        match TargetLineAttribution.tryPrepare (outcomeAt 0x7000UL 0x40UL) symbol typeof<LineFixture>.Assembly.Location evidence with
                        | Error errors -> failtestf "The retargeted capture must prepare, not give %A." errors
                        | Ok resolver ->
                            Expect.equal resolver.Label "synthetic: native map capture fixture" "The resolver must keep the synthetic label."
                            Expect.equal resolver.CodeRange (0x7000UL, 0x7040UL) "The resolver must use the captured region."
                            Expect.equal resolver.Uncovered [| 0x7014UL, 0x7018UL |] "The gap in the raw map must stay uncovered."
                            Expect.contains resolver.Limitations (SuppliedEvidence "synthetic: native map capture fixture") "The limitation must name the synthetic source."
                }
            ]

            testList "rejection" [
                test "a short, foreign, or unknown header rejects" {
                    Expect.equal (TargetMapCapture.tryParse null) (Error(TooShort 0)) "A null buffer must reject."
                    Expect.equal (TargetMapCapture.tryParse (Array.sub (fixture "captured.bin") 0 703)) (Error(TooShort 703)) "A truncated header must reject."
                    Expect.equal (mutated "captured.bin" (fun b -> b[0] <- 0uy)) (Error(BadMagic 0x504D5A00u)) "Another magic must reject."
                    Expect.equal (mutated "captured.bin" (fun b -> put16 b 4 2us)) (Error(UnsupportedVersion 2us)) "An unknown version must reject."
                    Expect.equal (mutated "captured.bin" (fun b -> put16 b 6 708us)) (Error(UnexpectedHeaderSize 708us)) "Another header size must reject."
                    Expect.equal (mutated "captured.bin" (fun b -> put32 b 108 4095u)) (Error(UnexpectedCapacity 4095u)) "Another entry capacity must reject."
                    Expect.equal (mutated "captured.bin" (fun b -> put32 b 24 16u)) (Error(UnknownFlags 16u)) "An unknown flag must reject."
                    Expect.equal (mutated "captured.bin" (fun b -> put32 b 12 1u)) (Error(UnknownState 1u)) "A Capturing record is never written and must reject."
                    Expect.equal (mutated "captured.bin" (fun b -> put32 b 16 19u)) (Error(UnknownOutcome 19u)) "An unknown outcome must reject."
                }

                test "truncated or inconsistent lengths and counts reject" {
                    let captured = fixture "captured.bin"
                    Expect.equal (TargetMapCapture.tryParse (Array.sub captured 0 (captured.Length - 1))) (Error(LengthMismatch(776u, 775))) "A truncated entry must reject."
                    Expect.equal (TargetMapCapture.tryParse (Array.append captured [| 0uy |])) (Error(LengthMismatch(776u, 777))) "A trailing byte must reject."
                    match mutated "captured.bin" (fun b -> put32 b 104 5u) with
                    | Error(InconsistentRecord _) -> ()
                    | other -> failtestf "A total size that does not hold the entries must reject, not give %A." other
                    Expect.equal (mutated "captured.bin" (fun b -> put32 b 88 2u)) (Error(CountExceeded("bodies", 1u, 2u, 2u))) "A copied count above its reported count must reject."
                    Expect.equal (mutated "captured.bin" (fun b -> put32 b 92 3u; put32 b 96 3u)) (Error(CountExceeded("regions", 3u, 3u, 2u))) "A copied count above its buffer must reject."
                    Expect.equal (mutated "captured.bin" (fun b -> put32 b 176 300u; put32 b 180 261u)) (Error(CountExceeded("module name", 300u, 261u, 260u))) "A name above 260 units must reject."
                    for change, reason in
                        [ (fun (b: byte array) -> put32 b 100 7u), "a Captured record with a missing entry"
                          (fun b -> put32 b 84 2u), "a Captured record with two bodies"
                          (fun b -> b[64] <- 0uy), "a Captured record with another MVID"
                          (fun b -> put32 b 20 0x80004005u), "a Captured record with a failed HRESULT"
                          (fun b -> put32 b 28 1u), "a detail on an outcome without one" ] do
                        match mutated "captured.bin" change with
                        | Error(InconsistentRecord _) -> ()
                        | other -> failtestf "%s must reject, not give %A." reason other
                    match mutated "selection-invalid.bin" (fun b -> put32 b 20 0u) with
                    | Error(InconsistentRecord _) -> ()
                    | other -> failtestf "A failure outcome with HRESULT 0 must reject, not give %A." other
                    match mutated "map-count-changed.bin" (fun b -> put32 b 28 5u) with
                    | Error(InconsistentRecord _) -> ()
                    | other -> failtestf "MapCountChanged with equal counts must reject, not give %A." other
                    match mutated "not-seen.bin" (fun b -> put32 b 16 1u) with
                    | Error(InconsistentRecord _) -> ()
                    | other -> failtestf "A NotSeen record with an outcome must reject, not give %A." other
                }

                test "a field that the outcome does not allow rejects" {
                    for name, change, reason in
                        [ "not-seen.bin", (fun (b: byte array) -> put32 b 176 3u; put32 b 180 3u; b[184] <- 97uy), "a NotSeen record with a module name"
                          "selection-invalid.bin", (fun b -> b[48] <- 1uy), "an Initialize failure with an MVID"
                          "event-mask-failed.bin", (fun b -> put32 b 24 1u), "an Initialize failure with SecondNotification"
                          "two-bodies.bin", (fun b -> put32 b 84 1u; put32 b 88 1u; b[120] <- 0uy; b[121] <- 0uy), "MultipleBodies with one body"
                          "no-body.bin", (fun b -> put32 b 84 1u; put32 b 88 1u; put32 b 112 0x7000u), "NoNativeBody with a body"
                          "jit-failed.bin", (fun b -> b[48] <- 1uy), "JitFailed with an MVID"
                          "map-empty.bin", (fun b -> put32 b 176 0u; put32 b 180 0u; Array.fill b 184 520 0uy), "a claimed record without a module name"
                          "internal-error.bin", (fun b -> put32 b 176 0u; put32 b 180 0u; Array.fill b 184 520 0uy; Array.fill b 32 16 0uy; Array.fill b 80 4 0uy), "an InternalError without a claim that has an MVID"
                          "initialize-internal-error.bin", (fun b -> put32 b 24 2u), "an InternalError without a claim that has UnloadDuringCapture" ] do
                        match mutated name change with
                        | Error(InconsistentRecord _) -> ()
                        | other -> failtestf "%s must reject, not give %A." reason other
                }

                test "a stale byte outside the copied counts rejects" {
                    Expect.equal (mutated "captured.bin" (fun b -> b[120] <- 1uy)) (Error(NonZeroReserved 120)) "A second start beyond the copied count must be zero."
                    Expect.equal (mutated "captured.bin" (fun b -> b[150] <- 1uy)) (Error(NonZeroReserved 150)) "A second region beyond the copied count must be zero."
                    Expect.equal (mutated "captured.bin" (fun b -> b[703] <- 1uy)) (Error(NonZeroReserved 703)) "A name unit beyond the copied count must be zero."
                    Expect.equal (mutated "not-seen.bin" (fun b -> b[33] <- 1uy)) (Error(NonZeroReserved 33)) "A NotSeen record must hold no capture identity."
                    Expect.equal (mutated "selection-invalid.bin" (fun b -> b[33] <- 1uy)) (Error(NonZeroReserved 33)) "An Initialize failure must hold no function id."
                }

                test "a mutation of the input or of a returned array does not change the record" {
                    let bytes = fixture "captured.bin"
                    let r = parse bytes
                    Array.fill bytes 0 bytes.Length 0xFFuy
                    let entries = r.Entries
                    entries[0] <- struct (1u, 1u, 1u)
                    r.BodyStarts[0] <- 0UL
                    Expect.equal (r.Entries[0], r.BodyStarts[0], r.FunctionId) (struct (0xFFFFFFFEu, 0u, 8u), 0x7000UL, 0xF00UL) "The record must own its data."
                    match TargetMapCapture.toEvidence synthetic r with
                    | Ok evidence ->
                        evidence.Entries[0] <- struct (2u, 2u, 2u)
                        Expect.equal r.Entries[0] (struct (0xFFFFFFFEu, 0u, 8u)) "The evidence must not share the record entries."
                    | Error error -> failtestf "A clean capture must give evidence, not %A." error
                }
            ]

            test "a live source label names the process and the library hash" {
                let live = match TargetMapCaptureSource.tryLive 42 (String('a', 64)) with Ok source -> source | Error error -> failtest error
                Expect.equal (TargetMapCaptureSource.label live) ("captured: pid 42 profiler " + String('a', 64)) "A live source must name the process and the library hash."
                Expect.equal (TargetMapCaptureSource.label synthetic) "synthetic: native map capture fixture" "A synthetic source must say so."
            }

            test "a source names its provenance" {
                Expect.isError (TargetMapCaptureSource.trySynthetic " ") "A blank synthetic label must reject."
                Expect.isError (TargetMapCaptureSource.tryLive 0 (String('a', 64))) "A live source needs a positive process id."
                Expect.isError (TargetMapCaptureSource.tryLive 42 (String('A', 64))) "A live source needs a lowercase SHA-256."
                Expect.isError (TargetMapCaptureSource.tryLive 42 (String('a', 63))) "A live source needs 64 hexadecimal characters."
            }
        ]
