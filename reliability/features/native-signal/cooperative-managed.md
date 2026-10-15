# Managed cooperative receipt evidence

The controlled workload runs a native cooperative lease from a dedicated managed owner thread.
It records the first managed receipt bounds, applies valid journals to the ledger, and closes native ownership separately.

| Case | Required result |
| --- | --- |
| `paid` | Measured wait settles the 50,000 ns lease. Later reads preserve the first receipt. |
| `deferred` | A mismatched site performs no wait. Settlement leaves 50,000 ns of debt. |
| `cancelled` | Managed cancellation intent permits zero-payment settlement. Debt remains. |
| `late` | A first read delayed by 150 ms fails receipt validation permanently. The ledger reservation remains. |
| `clock-failure` | A real foreign-thread native clock read returns status 2. Later valid reads cannot repair the receipt. |
| `active-cleanup` | Cleanup cancels and polls an unclaimed request, retains its final child, then releases, retires, joins, and closes. |

Both rejection cases copy and release native journals without accepting their payment into the ledger.
Every successful case retires and joins the owner before closing the session.
Each case has its own process and watchdog.
The first five cases also settle a second independent lease on the same slot.
The second generation must have a new tracker and retain the previous journal without repairing any failed ledger reservation.
Cleanup captures include the final child records, not only their aggregate header.

Windows evidence: `artifacts/cooperative-interop/20261015T092015134-1b4f999e`.
Linux evidence: `artifacts/cooperative-interop/20261015T092211845-5c4a4552`.
Both runs pass all six cases, pure managed checks, and eleven native CTest entries.
Both manifests report unchanged inputs. These runs include generation reuse and active cleanup.

Pure checks in `CooperativeManagedChecks` cover these requirements:

| Requirement | Check |
| --- | --- |
| Site fields, generation and budget bounds, record layout, sequence encoding | `SiteIdentityAndLayouts` |
| Immutable raw buffers, stable first bounds, new prefixes, and exact 100 ms limit | `FirstReceiptsAndImmutablePrefixes` |
| Invalid fields, failed clocks and API statuses, permanent rejection, and terminal immutability | `FailedReceiptsRemainFailed` |
| Prefix replay, stale state, concurrent credit, and exact-once settlement | `PrefixCreditAndSettlement` |
| Three site mismatches, explicit cancellation intent, and interrupted payment | `DeferralCancellationAndFailure` |
| Invalid payment, failed journals, and preserved reservations | `InvalidPaymentKeepsReservation` |

The workload uses manual polls. It does not establish injected probe provenance, CLR code lifetime, sampled capacity, instrumentation cost, or prediction accuracy.
See [the reference](../../../docsSrc/Reference/cooperative-managed.md) and [the verification procedure](../../../docsSrc/How_Tos/verify-cooperative-interop.md).
