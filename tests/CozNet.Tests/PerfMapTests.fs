namespace CozNet.Tests

open CozNet
open Expecto

module PerfMapTests =
    let private symbol = "CozNet.TargetLocal.Work::selected(uint64)"

    let private expectError lines (reason: string) message =
        match PerfMap.tryRange lines symbol with
        | Ok value -> failtestf "%s Got %A." message value
        | Error actual -> Expect.stringContains actual reason message

    [<Tests>]
    let tests =
        testList "perf map" [
            test "unprefixed hexadecimal also resolves" {
                let lines = [ "7f0000002000 4f uint64 [CozNet.TargetLocal] CozNet.TargetLocal.Work::selected(uint64)[Optimized]" ]
                Expect.equal (PerfMap.tryRange lines symbol) (Ok(0x7F0000002000UL, 0x7F000000204FUL)) "An address without 0x must parse."
            }

            test "one optimized range resolves to a half-open span" {
                let lines = [
                    "0x7f0000001000 40 void [CozNet.TargetLocal] CozNet.TargetLocal.Work::peer(uint64)[Optimized]"
                    "0x7f0000002000 4f uint64 [CozNet.TargetLocal] CozNet.TargetLocal.Work::selected(uint64)[Optimized]"
                ]
                Expect.equal (PerfMap.tryRange lines symbol) (Ok(0x7F0000002000UL, 0x7F000000204FUL)) "The range must be start to start plus size."
            }

            test "no range rejects" {
                expectError [ "0x7f0000001000 40 void [Other] Other::run()[Optimized]" ] "contains 0" "A missing symbol must reject."
            }

            test "two ranges reject" {
                let lines = [
                    "0x7f0000002000 4f uint64 [CozNet.TargetLocal] CozNet.TargetLocal.Work::selected(uint64)[Optimized]"
                    "0x7f0000003000 4f uint64 [CozNet.TargetLocal] CozNet.TargetLocal.Work::selected(uint64)[Optimized]"
                ]
                expectError lines "contains 2" "A duplicate symbol must reject."
            }

            test "unoptimized tiers are ignored" {
                let lines = [
                    "0x7f0000002000 4f uint64 [CozNet.TargetLocal] CozNet.TargetLocal.Work::selected(uint64)[QuickJitted]"
                    "0x7f0000003000 4f uint64 [CozNet.TargetLocal] CozNet.TargetLocal.Work::selected(uint64)[Optimized]"
                ]
                Expect.equal (PerfMap.tryRange lines symbol) (Ok(0x7F0000003000UL, 0x7F000000304FUL)) "Only the optimized tier must count."
            }

            test "malformed and empty entries are ignored" {
                let lines = [
                    "not-a-number 4f uint64 [CozNet.TargetLocal] CozNet.TargetLocal.Work::selected(uint64)[Optimized]"
                    "0x7f0000002000 0 uint64 [CozNet.TargetLocal] CozNet.TargetLocal.Work::selected(uint64)[Optimized]"
                    "0xfffffffffffffff0 20 uint64 [CozNet.TargetLocal] CozNet.TargetLocal.Work::selected(uint64)[Optimized]"
                    "short line"
                ]
                expectError lines "contains 0" "Unparsable, empty, and overflowing ranges must not count."
            }
        ]
