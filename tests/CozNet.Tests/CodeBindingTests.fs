namespace CozNet.Tests

open System.Collections.Generic
open CozNet
open Expecto

module CodeBindingTests =
    let private selected = "CozNet.TargetLocal.Work::selected(uint64)"
    let private peer = "CozNet.TargetLocal.Work::peer(uint64)"

    let private entry address tier symbol =
        $"0x{address:x} 4f uint64 [CozNet.TargetLocal] {symbol}[{tier}]"

    let private baseline = [
        entry 0x7f0000001000UL "Optimized" selected
        entry 0x7f0000002000UL "Optimized" peer
    ]

    let private errors (values: (string * string) list) switch =
        let table = Dictionary<string, string>()
        for name, value in values do table[name] <- value
        CodeBinding.configurationErrorsWith (fun name -> match table.TryGetValue name with | true, value -> value | _ -> null) switch

    let private qualified = [ "DOTNET_TieredCompilation", "0"; "DOTNET_ReadyToRun", "0"; "DOTNET_PerfMapEnabled", "3" ]

    [<Tests>]
    let tests =
        testList "code binding" [
            test "one optimized entry for each symbol binds" {
                let binding = CodeBinding.tryCapture baseline [| selected; peer |] |> Result.defaultWith failwith
                Expect.equal (binding.Range selected) (0x7f0000001000UL, 0x7f000000104fUL) "The selected range must bind."
                Expect.equal (CodeBinding.verify baseline binding) (Ok()) "An unchanged map must verify."
            }

            test "every non-optimized tier rejects" {
                for tier in [ "QuickJitted"; "OptimizedTier1"; "OptimizedTier1OSR" ] do
                    let lines = [ entry 0x7f0000001000UL tier selected; baseline[1] ]
                    Expect.isError (CodeBinding.tryCapture lines [| selected; peer |]) $"Tier {tier} must reject."
            }

            test "a second code version rejects" {
                let lines = baseline @ [ entry 0x7f0000003000UL "OptimizedTier1" selected ]
                Expect.isError (CodeBinding.tryCapture lines [| selected; peer |]) "Two entries for one symbol must reject."
            }

            test "a missing symbol and duplicate symbols reject" {
                Expect.isError (CodeBinding.tryCapture [ baseline[0] ] [| selected; peer |]) "A missing symbol must reject."
                Expect.isError (CodeBinding.tryCapture baseline [| selected; selected |]) "Duplicate symbols must reject."
                Expect.isError (CodeBinding.tryCapture baseline [||]) "An empty binding must reject."
            }

            test "verify rejects code that changes during the run" {
                let binding = CodeBinding.tryCapture baseline [| selected; peer |] |> Result.defaultWith failwith
                let added = baseline @ [ entry 0x7f0000004000UL "OptimizedTier1OSR" selected ]
                let moved = [ entry 0x7f0000005000UL "Optimized" selected; baseline[1] ]
                Expect.isError (CodeBinding.verify added binding) "A new code version during the run must reject."
                match CodeBinding.verify moved binding with
                | Ok() -> failtest "A moved range must reject."
                | Error message -> Expect.stringContains message "moved" "The error must name the moved range."
            }

            test "configuration requires tiering, ReadyToRun, and perf map settings" {
                Expect.isEmpty (errors qualified None) "The qualified settings must pass."
                Expect.isEmpty (errors [ "DOTNET_ReadyToRun", "0"; "DOTNET_PerfMapEnabled", "1" ] (Some false)) "A false switch must count as tiering off."
                Expect.equal (errors [ "DOTNET_ReadyToRun", "0"; "DOTNET_PerfMapEnabled", "1" ] None |> List.length) 1 "An unset tiering setting must reject."
                Expect.equal (errors [ "DOTNET_TieredCompilation", "1"; "DOTNET_ReadyToRun", "0"; "DOTNET_PerfMapEnabled", "3" ] (Some false) |> List.length) 1
                    "An environment value must override the switch."
                Expect.isEmpty (errors [ "COMPlus_TieredCompilation", "0"; "COMPlus_ReadyToRun", "0"; "COMPlus_PerfMapEnabled", "3" ] None) "COMPlus names must count."
                Expect.equal (errors [ "DOTNET_TieredCompilation", "0" ] None |> List.length) 2 "Missing ReadyToRun and perf map settings must each reject."
                Expect.equal (errors [ "DOTNET_TieredCompilation", "0"; "DOTNET_ReadyToRun", "0"; "DOTNET_PerfMapEnabled", "0" ] None |> List.length) 1
                    "A disabled perf map must reject."
                Expect.equal (errors [ "DOTNET_TieredCompilation", "0"; "DOTNET_ReadyToRun", "0"; "DOTNET_PerfMapEnabled", "2" ] None |> List.length) 1
                    "A jitdump-only setting writes no perf map and must reject."
                Expect.equal (errors [ "DOTNET_TieredCompilation", "0"; "DOTNET_ReadyToRun", "0"; "DOTNET_PerfMapEnabled", "" ] None |> List.length) 1
                    "An empty perf map setting must reject."
            }

            test "a checked run refuses a bad configuration or a moved binding before it runs" {
                let binding = CodeBinding.tryCapture baseline [| selected; peer |] |> Result.defaultWith failwith
                let moved = [ entry 0x7f0000005000UL "Optimized" selected; baseline[1] ]
                let mutable runs = 0
                let runRanges _ _ = runs <- runs + 1

                Expect.throwsT<System.InvalidOperationException>
                    (fun () -> TargetLocalProfiler.checkedRun [ "tiering" ] (fun () -> Array.ofList baseline) binding [| selected |] [| selected |] runRanges |> ignore)
                    "A configuration error must raise."
                Expect.throwsT<System.InvalidOperationException>
                    (fun () -> TargetLocalProfiler.checkedRun [] (fun () -> Array.ofList moved) binding [| selected |] [| selected |] runRanges |> ignore)
                    "A binding that moved before the run must raise."
                Expect.equal runs 0 "A refused run must not start."
            }

            test "a checked run passes bound ranges and reports a move during the run" {
                let binding = CodeBinding.tryCapture baseline [| selected; peer |] |> Result.defaultWith failwith
                let moved = [ entry 0x7f0000005000UL "Optimized" selected; baseline[1] ]
                let mutable map = baseline
                let runRanges selectedRanges eligibleRanges =
                    map <- moved
                    selectedRanges, eligibleRanges

                let ranges, error =
                    TargetLocalProfiler.checkedRun [] (fun () -> Array.ofList map) binding [| selected |] [| selected; peer |] runRanges

                Expect.equal ranges ([| binding.Range selected |], [| binding.Range selected; binding.Range peer |]) "The run must receive the bound ranges."
                Expect.isSome error "A move during the run must be reported."
                let _, unchanged =
                    TargetLocalProfiler.checkedRun [] (fun () -> Array.ofList baseline) binding [| selected |] [| selected |] (fun _ _ -> ())
                Expect.isNone unchanged "An unchanged binding must report no error."
            }
        ]
