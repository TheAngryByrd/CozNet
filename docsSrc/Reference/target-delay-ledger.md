# Target delay ledger

`native/service/TargetDelayLedger.h` defines the portable native ledger for target-local credit and payment.
It makes no OS calls and does not allocate. The caller supplies all journal storage.

## Limits

| Constant | Value | Meaning |
| --- | ---: | --- |
| `ledger_debt_limit_ns` | 2,000,000 | Observed debt above this value fails the experiment. |
| `ledger_child_limit_ns` | 50,000 | Maximum requested wait for one child. |
| `ledger_payment_limit_ns` | 2,000,000 | Maximum requested wait in one settlement. |
| `ledger_handler_limit_ns` | 5,000,000 | Handler bound. |
| `ledger_handler_reserve_ns` | 1,000,000 | Handler time kept free. A child cannot start past `limit - reserve`. |

## Plan

`LedgerPlan::parse(speedup, period, selected, eligible)` returns a plan or no value.

- `speedup` is 0 to 100 percent. `period` is 1 ns to 1 s.
- `selected` and `eligible` are sets of half-open instruction spans `[begin, end)`. Each set holds at most 16 spans.
- A span must have a nonzero `begin` and a positive size. Spans in one set must not overlap.
- `eligible` must not be empty.

`credit()` returns `period * speedup / 100`. A selected sample earns this value. An unselected sample earns zero.

## Experiment and account

`ExperimentDelay` holds the global delay, the first failure, and the event sequence counter. All three are lock-free atomics.
A failure is sticky. The first failure stays, and later failures do not replace it.

`ThreadDelayAccount(experiment, plan, journal)` holds one thread's local delay and its event journal.
The local delay starts at the global delay. Only the owning thread can call `accept` and `settle`.

`accept(sample, generation, dispatch)` adds the sample credit to the local delay and appends a `sample` event.
It returns false and records `journal_full` when the journal cannot keep `settle_reserve` free entries.
`available()` reports the free entries above that reserve, so a dispatcher stops before the journal fills.

`settle(clock, waiter, ip, handler_started)` runs one settlement:

1. If local delay is above global delay, it publishes local delay with a compare-and-swap and appends a `publish` event.
2. If local delay equals global delay, it returns `caught_up`.
3. If global delay is above local delay, the difference is debt. Debt above 2 ms appends a `failure` event once and fails the experiment.
4. If `ip` is not in `eligible`, it appends a `deferred` event and returns `ineligible_deferred`.
5. Otherwise it requests children of at most 50,000 ns. Each child adds its measured wait, including overshoot, to local delay.
6. It stops with `budget_deferred` when 2 ms of children are requested or the next child would pass the handler reserve.

Debt is never forgiven. A deferred debt stays until a later settlement pays it.
A failed wait or clock keeps the measured part, appends a `failure` event, and fails the experiment.

## Events

Each `LedgerEvent` takes one sequence from `ExperimentDelay`. The sequences of all threads form one gapless range.

| Kind | `amount` | Other fields |
| --- | --- | --- |
| `sample` | Credit of the sample | `detail` is 1 for a selected sample |
| `publish` | Published local delay | `observed_global` is the value before the swap |
| `child` | Measured wait | `requested` is the child request, `detail` is the wait status |
| `deferred` | Debt left unpaid | `detail` is 1 for a budget stop and 0 for an ineligible instruction |
| `failure` | Debt or 0 | `detail` is the `LedgerFailure` value |

Every event keeps `observed_global` and `local_before`. A publish takes its sequence after its swap.
Sequence order therefore does not always match swap order between threads. A replay must take global delay as the largest publish.

## Credit sink

`TargetCreditSink<Clock, Waiter>` adapts an account to the dispatcher sink contract.
`LinuxTargetSession<Sink>` calls `sink.settle(ip, handler_started)` after each dispatch with the interrupted instruction pointer.
It does not settle after a `reentered` or `stale_notification` result.
`LinuxSpinWaiter` spins on `CLOCK_MONOTONIC`. It returns a failed wait after 2^20 iterations or a clock error.

The spin consumes thread CPU. A thread CPU budget therefore replaces work with payment instead of adding wall time.

## Verified environment

`TargetDelayLedgerChecks` (`target-delay-ledger`) runs on Windows and Linux.
`LinuxPerfRingVerify` runs `TargetCreditWorkload` in `zero` and `half` modes. See [the procedure](../How_Tos/verify-perf-ring.md#check-native-credit-and-payment).
These checks do not test CLR execution or prediction accuracy.
