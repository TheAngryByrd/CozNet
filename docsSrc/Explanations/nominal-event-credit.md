# Nominal events and interval credit

Native Coz assigns a fixed delay weight to each sample that matches the selected location.
It also credits the first event after sampling resumes.
CozNet's interval API instead requires consecutive runnable observations within one eligibility epoch.
These are different estimators.

The bound workload originally accumulated nominal periods into a synthetic cumulative CPU value.
Passing that value through the interval API discarded the first event in each epoch.
The optional nominal-event path removes that estimator mismatch without changing the interval API.
It does not establish prediction accuracy.

## A period can cross a payment pause

Linux task-clock preserves its remaining timer period across disable and enable.
A one-millisecond period can contain 0.9 ms of enabled CPU before payment and 0.1 ms after payment.
The disabled payment interval does not become enabled CPU through that preserved phase.
Native Coz still assigns the emitted event its nominal weight.
The first resumed event posts its full credit after resume, including the weight associated with enabled CPU before the pause.

The sampled instruction pointer estimates location statistically.
It does not prove that the complete period executed in the sampled method.
This limitation applies to initial, resumed, and ordinary events.
Discarding every first event introduces an intervention-dependent selection rule that native Coz does not use.
Neither retaining nor discarding those events proves an unbiased estimator on this workload.

## What the new path preserves

The source still owns event admission, eligibility epochs, loss checks, and CLR lifetime validation.
The nominal stream rejects changed periods, sequence gaps, duplicates, and decreasing epochs.
It does not infer missing events from cumulative counters.
The ledger still rejects credit during pending intervention or incompatible thread phases.
All payment and debt bounds remain unchanged. Actual measured wall time remains the payment amount.

The first accepted event can represent enabled CPU on both sides of a finite measurement boundary.
The capture retains that approximation and remains unqualified for boundary membership.
The new policy does not relabel source events or treat exact CPU attribution as established.

See [the reference](../Reference/nominal-event-credit.md) and [capture procedure](../How_Tos/run-nominal-event-credit.md).

## Current evidence

The Linux diagnostic passes zero speedup, 50% speedup, and expected CLR invalidation with full replay.
The separate four-arm comparison completes without collection or integrity errors.
Its sampled estimate is a 0.9–1.3% slowdown, while physical optimization reduces elapsed time by about 53.9%.
The treatment records 50 selected pauses. This block does not isolate their cost or establish the cause of the prediction gap.

Matching the upstream event rule therefore does not establish measurement accuracy.
The policy remains opt-in. Observer cost, sample attribution, and finite measurement boundaries still need investigation.
See [the retained evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/nominal-event-credit.md) for exact ranges and limits.

## Timer comparison

The timer workload still uses interval credit and excludes first events. Its intervention mechanism does not need inline loop probes.
Comparing it with nominal-event cooperative results therefore mixes a credit-policy difference with an intervention-mechanism difference.

An accounting replay of nine retained timer treatments confirms that their recorded credit follows the interval rule.
The three completed treatments exclude 11.5–18 ms of selected first-event weight at 50% speedup.
The other six workloads remain failed. Alternate event weights do not simulate how a nominal policy would change execution.
An opt-in nominal timer policy is the next planned comparison step. It is not implemented yet.
See [the retained audit](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/timer-estimator-audit.md) for all cases and replay limits.

## Primary sources

- [Coz delay calculation](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L155-L165) uses nominal period and requested speedup.
- [Coz sample processing](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L736-L757) credits selected events before the delay check.
- [Coz payment](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L707-L727) stops sampling, accounts measured wait, and resumes sampling.
- [Linux timer stop/start](https://github.com/microsoft/WSL2-Linux-Kernel/blob/linux-msft-wsl-5.15.153.1/kernel/events/core.c#L10838-L10868) preserves the remaining period.
- [Linux sample emission](https://github.com/microsoft/WSL2-Linux-Kernel/blob/linux-msft-wsl-5.15.153.1/kernel/events/core.c#L10808-L10835) uses `last_period` independently of cumulative count.

The pinned kernel source does not identify the running kernel binary.
