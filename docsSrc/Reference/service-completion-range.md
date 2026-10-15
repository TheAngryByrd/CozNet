# Conditional service completion range

`SignalAccounting.completionRange thread state` returns `Result<ConditionalSignalCompletion, string>`.
The function is internal and uses immutable accounting state.
It makes no native call and does not change accounting.

| Property | Meaning |
| --- | --- |
| `Thread` | The registered ledger thread. |
| `Global` | Global delay in the input state. The function does not publish local credit. |
| `MinimumLocal` | Minimum local credit after valid completion of the existing service. |
| `MaximumLocal` | Maximum local credit after valid completion of the existing service. |
| `RequiresServiceCompletion` | True while a service owns the ledger, including an accepted terminal journal. |

Runnable idle state returns an exact local value and no service dependency.
A service uses its fixed budget and accepted prefix.
A stopped service returns an exact prospective value but still requires settlement.
Individual requests and blocked states are unsupported.
Foreign registrations return the signal-registration error.
A range that exceeds `Int64.MaxValue` returns an error without changing ownership.

The values use nanoseconds through `DelayTicks`.
`SignalServiceChild.maximumHandlerNanoseconds` supplies the same 5 ms limit used by the child parser.
The private result representation prevents independent construction of contradictory endpoints.

The range assumes valid completion and no new accounting work.
It does not prove native quiescence, successful future execution, or a bound on prediction error.
It is not yet part of measurement output or prediction eligibility.
See [the derivation and limits](../Explanations/service-completion-limits.md).
