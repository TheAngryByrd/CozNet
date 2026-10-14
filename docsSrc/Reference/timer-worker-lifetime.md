# Timer worker lifecycle

`TimerWorkerLifetime` controls the selected timer worker's registration, retirement, and controller cleanup order.
It contains no platform calls. `TimerSelectedWorker` supplies the native client, managed thread, readiness event, and ownership-retention operation.

## Target operations

The target attempts registration, signals readiness, and runs its arithmetic body.
On exit or failure, it signals readiness again and attempts retirement.
A registration error does not bypass retirement because native ownership can become active before registration returns an error.

Successful retirement sets `Retired` to `true`.
Failed retirement records the error and calls the retention operation.
The live retention operation keeps the target thread alive indefinitely.
If the body or registration already failed, `Error` contains both failures in an `AggregateException`.
The original failure appears first. The retirement failure appears second.

## Controller cleanup

The controller requests stop and attempts a bounded join.
It disposes the readiness event and releases any outstanding journal only after both join and retirement succeed.

| Outcome | `Retired` | `Joined` | `Released` | Cleanup result |
| --- | --- | --- | --- | --- |
| Normal completion | `true` | `true` | `true` | Complete |
| Body failure, successful cleanup | `true` | `true` | `true` | Fails with the original error |
| Failed retirement | `false` | Either | `false` | Retains resources |
| Failed join after retirement | `true` | `false` | `false` | Retains resources |
| Failed event disposal | `true` | `true` | `false` | Does not attempt release |
| Failed journal release | `true` | `true` | `false` | Prevents session close |

Join, disposal, and release exceptions propagate unchanged.
A successful join alone cannot authorize release after failed retirement.
Successful cleanup does not convert a worker failure into a passing run.
After release succeeds, another cleanup call performs no native or thread operations.
It still reports any original worker failure.

## Verification scope

`TimerLayoutVerify` runs nine portable lifecycle checks with controlled callbacks.
They call the same lifecycle component as the live worker.
The checks verify exact operation order, partial-registration cleanup, error retention, and release prerequisites.
They inject registration, body, retirement, join, disposal, and release failures without native calls or blocked background threads.

These checks prove the managed lifecycle decisions for the supplied outcomes.
They do not prove that a kernel syscall has the reported outcome or that all thread interleavings are safe.
Live timer runs separately exercise the real registration, retirement, join, and release operations.
The [retained evidence](../../reliability/features/native-signal/timer-worker-lifetime.md) records nine portable checks, five detected mutations, and eight passing live cases.

See [the transition procedure](../How_Tos/run-timer-transitions.md) and [the timer ABI](timer-interop.md).
