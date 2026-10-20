# How to rank candidates in your own program

Use this procedure to rank the methods that a zero-delay target-local run sampled, and to build a design from your choice.
It requires Linux x64 for the run and the capture. Tiered compilation and ReadyToRun must be off, and the perf map must be on.

1. Bind the payment-eligible methods of your work with `CodeBinding.tryCapture`.
2. Run the work with `TargetLocalProfiler.runBound` at speedup 0 and with no selected method. Use the binding of step 1.
3. In the same process, after the run, call `PerfMapSnapshot.tryCaptureCurrentProcess ()`.
4. Optional: build a catalog of known methods with `TargetMethodCatalog.tryCreate`. Name each `MethodInfo`. Do not pass every method of an assembly.
   Handle a catalog error. To have no anchors, pass `None`.
5. Create a scope with `TargetDiscoveryScope.tryCreate` and a limit with `TargetDiscoveryLimit.tryCreate`.
6. Call `TargetDiscoveryWorkflow.prepare scope binding result snapshot catalog limit`.

   ```fsharp
   match TargetDiscoveryWorkflow.prepare scope binding result snapshot catalog limit with
   | Error error -> printfn "No outcome: %A" error
   | Ok outcome ->
       printfn "%A %A" outcome.Counts outcome.Limitations
       let candidates = outcome.Candidates |> Array.filter (fun c -> c.Eligibility = InEligibleBinding) |> Array.map _.Method.Symbol
       match TargetDiscoveryWorkflow.tryDesign outcome binding.Symbols candidates period [| 50u |] 1 0 0 EndToEnd with
       | Ok design -> printfn "Design with %d candidates" design.Candidates.Length
       | Error errors -> printfn "No design: %A" errors
   ```

7. Read `outcome.Counts` and `outcome.Limitations` inside the `Ok outcome` case. The limitations state what the workflow cannot check.
8. To use a candidate outside the eligible binding, add its symbol to the eligible list of `tryDesign`. That admits it as a payment-eligible method.

Run the design in the same process. `TargetCausalProfile.run` checks the binding again against the perf map of the process.

See [the API reference](../Reference/target-local-api.md#discovery-workflow) and [the discovery limits](../Explanations/target-local-discovery.md).
