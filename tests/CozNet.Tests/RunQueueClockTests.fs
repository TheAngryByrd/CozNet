namespace CozNet.Tests

open System.Text
open System.Threading
open CozNet
open Expecto

module RunQueueClockTests =
    let private parse (text: string) =
        let bytes = Encoding.ASCII.GetBytes text
        RunQueueClock.Parse(bytes, bytes.Length)

    let private onThread action =
        let mutable result = Unchecked.defaultof<_>
        let thread = Thread(fun () -> result <- action ())
        thread.Start()
        thread.Join()
        result

    [<Tests>]
    let tests =
        testList "run-queue clock" [
            test "a start trace without a run-queue clock keeps each point in order with an unreadable delay" {
                let trace = StartTrace null
                let delays = [ for index in 0..2 -> trace.Record index ]
                let points = trace.Points()
                let counters (p: TargetStartPoint) = [ p.MinorFaults; p.MajorFaults; p.VoluntarySwitches; p.InvoluntarySwitches ]
                Expect.equal delays [ -1L; -1L; -1L ] "Each record without a clock must return -1."
                Expect.equal points.Length 3 "The trace must hold three points."
                Expect.isTrue (points |> Array.pairwise |> Array.forall (fun (a, b) -> a.Ticks > 0L && a.Ticks <= b.Ticks)) $"The point times must not decrease: %A{points}"
                Expect.isTrue (points |> Array.forall (fun p -> p.RunDelayNs = -1L && (p.CpuTimeNs = -1L || p.CpuTimeNs > 0L))) $"The delay must be -1 and the CPU time unreadable or positive: %A{points}"
                Expect.isTrue (points |> Array.forall (fun p -> counters p |> List.forall ((=) -1L) || counters p |> List.forall (fun v -> v >= 0L)))
                    $"The counters of a point must be all unreadable or all readable: %A{points}"
            }

            test "schedstat fields parse as run time and run-queue delay" {
                Expect.equal (parse "123456 7890 42\n") (struct (123456L, 7890L)) "Both leading fields must parse."
                Expect.equal (parse "0 0 0\n") (struct (0L, 0L)) "Zero values must parse."
            }

            test "a short or malformed schedstat read reports -1" {
                Expect.equal (parse "") (struct (-1L, -1L)) "An empty read has no values."
                Expect.equal (parse "15\n") (struct (15L, -1L)) "A missing delay field must report -1."
                Expect.equal (parse "x 12") (struct (-1L, -1L)) "A non-numeric field must stop the parse."
            }

            test "a thread without a clock records each wait as unreadable" {
                let waits, dropped =
                    onThread (fun () ->
                        TargetCurrent.Track(null, 4)
                        TargetCurrent.RecordWait(TargetCurrent.RunQueue(), TargetCurrent.RunQueue())
                        TargetCurrent.RecordWait(100L, 250L)
                        TargetCurrent.TakeWaits())
                Expect.equal waits [| -1L; 150L |] "The warm-up wait must not count, and a failed read must record -1."
                Expect.equal dropped 0 "No wait may drop below capacity."
            }

            test "waits beyond the capacity are counted as dropped" {
                let waits, dropped =
                    onThread (fun () ->
                        TargetCurrent.Track(null, 2)
                        for value in 1L .. 5L do
                            TargetCurrent.RecordWait(0L, value)
                        TargetCurrent.TakeWaits())
                Expect.equal waits [| 1L; 2L |] "Recorded waits must keep their order."
                Expect.equal dropped 3 "Each wait after the capacity must count as dropped."
            }

            test "taking the waits stops recording on the thread" {
                let waits =
                    onThread (fun () ->
                        TargetCurrent.Track(null, 2)
                        TargetCurrent.TakeWaits() |> ignore
                        TargetCurrent.RecordWait(0L, 1L)
                        TargetCurrent.TakeWaits() |> fst)
                Expect.isEmpty waits "A wait after the take must not record."
            }
        ]
