# How to profile your own fork/join program

Use this procedure to predict the effect of an optimization in your own Linux x64 program.
Your program must match the [supported model](../Explanations/target-local-prediction.md#support-boundary): CPU-bound threads that do not wait before the final join, or that wait only at a `TargetBarrier`.

1. Reference the `CozNet` library from your program.
2. Put each worker body in a method marked `[<MethodImpl(MethodImplOptions.NoInlining)>]`.
3. Build `libCozNet.TargetLocal.so` from `native/service/TargetLocalApi.Linux.cpp`, and put it next to your program.
4. Set `DOTNET_TieredCompilation=0`, `DOTNET_ReadyToRun=0`, `DOTNET_PerfMapEnabled=3`, and `DOTNET_PerfMapShowOptimizationTiers=1`.
5. Call each work method once before profiling, so that the JIT writes its optimized range to the perf map.
6. Resolve the ranges and run the profile:

   ```fsharp
   open System.IO
   open CozNet

   let parse, index = "MyApp.Work::parse(int32)", "MyApp.Work::index(int32)"
   let binding =
       CodeBinding.tryCapture (File.ReadAllLines(PerfMap.currentProcess ())) [| parse; index |]
       |> Result.defaultWith failwith
   let workers = [|
       { Work = (fun () -> Work.parse 1000 |> ignore); PaymentIp = fst (binding.Range parse) }
       { Work = (fun () -> Work.index 1000 |> ignore); PaymentIp = fst (binding.Range index) }
   |]
   let run speedup =
       TargetLocalProfiler.runBound binding speedup 1000000UL [| parse |] [| parse; index |] (1u <<< 20) workers ignore
   let zero, half = run 0u, run 50u
   ```

7. Check that `zero.Passed` and `half.Passed` are true. Do not use a result that did not pass. A `BindingError` means that the code moved during the run.
8. Compute the predicted reduction as `1.0 - float half.EffectiveNs / float zero.EffectiveNs`.

To compare several candidates and speedups in one process, call `TargetCausalProfile.run`:

   ```fsharp
   let workerSet (_: TargetProgressCounter) = [|
       { Work = (fun () -> Work.parse 1000 |> ignore); PaymentIp = fst (binding.Range parse) }
       { Work = (fun () -> Work.index 1000 |> ignore); PaymentIp = fst (binding.Range index) }
   |]
   let design =
       TargetCausalDesign.tryCreate binding 1000000UL [| parse; index |] [| parse; index |] [| 25u; 50u; 75u |] 5 42 0 EndToEnd
       |> Result.defaultWith (fun errors -> failwith (errors |> List.map TargetCausalDesign.describe |> String.concat " "))
   let profile = TargetCausalProfile.run design (1u <<< 20) workerSet
   ```

Read `profile.Points`. Use only points whose `ValidBlocks` equals the block count, and keep each failed experiment from `profile.Experiments`.

For a pipeline, pass items between workers through a `TargetQueue`. To guard shared state, use a `TargetLock`. A worker that waits on either one then follows the wake rule.

For a phased program, create one `TargetBarrier` with one participant for each worker. Call `barrier.SignalAndWait()` at the end of each phase inside `Work`.

The selected spans name the candidate code. The eligible spans name every worker method, because a thread pays delay only at an eligible address.
`PaymentIp` must be inside one eligible span, or `run` raises `ArgumentException`. Each worker uses the start of its own work method.
If `monitor` throws, `run` still joins every started thread and closes the experiment. It then raises `AggregateException`.
The run requires the signals `SIGRTMIN + 8` and `SIGIO` to have their default dispositions.
Each journal entry uses about 144 bytes in native memory, for the ledger event and the dispatch log together. A capacity of 2^20 uses about 150 MB for each thread.
Size it from the expected samples: one entry for each sample, plus up to 41 for each payment settlement. A full journal fails the run.
The sample runs the qualified speedups in separate processes. The `cleanup` check runs two profiles in one process, the first with a failing monitor.

Give each profiled thread its own physical core when you can. SMT siblings and a full run queue both bias the result.

Repeat each speedup several times and compare the spread. One pair of runs cannot separate a small effect from noise.
See [the API reference](../Reference/target-local-api.md) and [the tutorial](../Tutorials/Target_Local_Prediction.md).
