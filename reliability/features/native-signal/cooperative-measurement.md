# Finite cooperative measurement evidence

The opt-in mode freezes global delay and progress before source closure and final peer settlement.
It retains residual debt and unknown native ownership. Every measurement remains invalid or unqualified.
The matched physical route remains incomplete.

## First live capture

Retained plan: `artifacts/cooperative-measurement/20261016T011420858-plan/plan.json`.
Retained run: `artifacts/cooperative-measurement/20261016T011420858-plan/20261016T012542198-f3418c54`.
The plan fixes zero speedup, treatment, then invalidation after selected payment, with 50,000 requested batches each.
Owner draining, qualified-sample cadence, and direct readiness are enabled. Handoff is disabled.
All three protocol outcomes pass. All three records replay successfully, and source and binary inputs remain unchanged.

| Case | Completed selected batches | Accepted/source samples | Measurement |
| --- | ---: | ---: | --- |
| Zero speedup | 50,000 | 434/436 | Unqualified |
| Treatment | 50,000 | 566/568 | Unqualified |
| Invalidation | 24 | 40/40 | Invalid |

The treatment freezes with peer command 336, generation 336, and a 499,914 ns budget.
Its frozen accepted prefix contains no child and no wait.
Frozen global delay is 73,245,889 ns. Frozen peer local credit is 72,745,975 ns.
Cleanup later accepts 499,989 ns from that request and raises peer local credit to 73,245,964 ns.
Global delay remains 73,245,889 ns. Cleanup does not publish the 75 ns peer lead.
The frozen record retains the pending request and its earlier local credit.

The end bracket is `[461861676558934, 461861677235480]` in managed timestamp ticks.
Source disable starts at `461861678349002`.
The final peer payment finishes at `461861684342567`, and final debt is observed at `461861684351539`.
The timestamp frequency is 1,000,000,000 Hz.
These records place the frozen end before source closure and final settlement.

The treatment's descriptive effective duration is 639,633,279–645,703,855 ns.
Its frozen progress deltas are 50,000 selected batches and 113,921 peer batches.
The boundary brackets span 6,070,576 ns of duration uncertainty.
Pending ownership, residual debt, source tail, source membership, and instrumentation attribution keep the treatment unqualified.
The intended CLR invalidation preserves the original lifetime error and a frozen endpoint, then reports an invalid measurement.
Its excluded callback stops the producer before failure. The catch path freezes after that pause stop and before cleanup.
The completed-case source shutdown ordering rule does not apply to this failed callback.
No unchanged run was retried. This single capture does not establish stable capacity, overhead bounds, or prediction accuracy.

## Controlled checks

The Windows boundary route passes nine existing boundary checks and nine cooperative measurement checks.
The bound cooperative managed route also passes.
The complete Release solution build passes without warnings or errors.
The documentation build passes.

Eighty-eight selected-payment Python checks pass.
Full replay covers frozen estimator arithmetic, residual completion, owned prefixes, later release, source tails, and failed outcomes.
The unresolved-submission fixture checks `verify_measurement` only. It does not claim full accounting replay for its reduced record.
Outcome fixtures reject mismatched or malformed finite activation.
The Linux run passes all fourteen native checks and all four managed verification routes, including measurement boundaries.

## Coverage limits

Portable checks cover the capture components. They do not inject failures through the complete workload cleanup sequence.
A successful native submit followed by a failed dispatch can make capture fail on inconsistent generation tracking.
That path retains the original failure and reports an invalid measurement.
Settled-but-unreleased ownership has controlled coverage. No live boundary has established that state.
Exact peer progress comes from the frozen atomic reading. Later totals bound it but cannot independently reconstruct it.
Source membership and instrumentation attribution remain unqualified.
These limits are accepted for the default-off prototype. They do not establish general application support or prediction accuracy.

Claude and Codex cleared the implementation before this declared capture, after endpoint and ownership replay corrections.
Both reviewers cleared the final implementation and evidence claims with the stated limits.
Claude checked the raw retained records. Codex reviewed the supplied evidence without independent execution or full artifact inspection.

See [the reference](../../../docsSrc/Reference/cooperative-measurement.md) and [the procedure](../../../docsSrc/How_Tos/capture-cooperative-measurement.md).
