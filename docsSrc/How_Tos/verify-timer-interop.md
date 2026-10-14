# How to verify timer interop

Use this procedure to check the native timer ABI and its managed receipt boundary.
You need the repository SDK, PowerShell, a Linux Docker engine, and these local images:

- `coznet-native-build:10.0.100`
- `mcr.microsoft.com/dotnet/sdk:10.0.100`

The runners use existing images without network access.

## Check the native ABI

1. Run this command from the repository root:

   ```powershell
   dotnet run --project build/build.fsproj -- -t LinuxTimerApi
   ```

2. Open the reported directory under `artifacts/timer-api/`.
3. Check `summary.json` for `passed: true`, unchanged inputs, and successful outcomes for `deferred` and `chain`.
4. Inspect `contracts.log` for the portable lease and mailbox results.
5. Inspect both mode records for four completed commands and `closed: true`.
6. Check each journal's measured total against its child waits.
7. Check `retirementBlocked: true` and zero children in the final command.

The chained mode must complete bounded waits on both targets.
The cancellation and pending-retirement records must retain their own generations and outcomes.

## Check managed layout and receipt

1. Run the host layout check:

   ```powershell
   dotnet run --project build/build.fsproj -- -t TimerLayoutVerify
   ```

   This command also runs on Windows. It does not load the Linux backend.
   It checks portable timer receipts and journal accounting against the real delay ledger.

2. Run the Linux receipt checks:

   ```powershell
   dotnet run --project build/build.fsproj -- -t LinuxTimerInterop
   ```

3. Open the reported directory under `artifacts/timer-interop/`.
4. Check `summary.json` for successful layout, `receipt`, `late`, and `clock-failure` outcomes.
5. Check `receipt.json` for `firstReceiptPreserved: true` and one accepted child.
6. Subtract `firstRaw.Children[0].Armed` from `firstRaw.Clock.Nanoseconds`.
7. Check that the result equals `received[0].Timing.UpperNanoseconds` and does not exceed 100,000,000 ns.
8. Check `late.json` for `rejected: true` and a first receipt interval above 100,000,000 ns.
9. Check `clock-failure.json` for `rejected: true`, first clock status 1, and first clock timestamp zero.
10. Check both rejection records for a later successful clock and no accepted receipt.
11. Check all three records for `joined: true` and `closed: true`.
12. Check `receipt.json` for `accountingSettled: true` and `unpaidDebt: 50000`.
13. Check both rejection records for `reservationRetained: true` and `accountingSettled: false`.

Each managed case must contain one deferred child with zero measured wait.
The fixture uses synthetic selected credit. Deferral must preserve the full 50,000 ns debt after releasing its reservation.
`late` delays the first copy by 150 ms. `receipt` delays only a later copy by 150 ms.

Retain every failed record and log. Do not repeat an unchanged timing case to obtain a pass.
Use `manifest.json` to identify source hashes, binary hashes, and container images.
These checks do not establish CLR intervention safety or prediction accuracy.

See the [timer interop reference](../Reference/timer-interop.md) for ownership and timing contracts.
See [timer journal accounting](../Reference/timer-accounting.md) for settlement rules and sequence mapping.
