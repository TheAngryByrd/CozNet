# Bound range publication mutation

The native `competingCodePublishers` check detected removal of the single-writer publication guard.
This result demonstrates sensitivity to one defect. It does not prove all concurrent schedules or general CLR safety.

## Source and method

The baseline used the working tree based on `9f03c1b` with the bound native range changes.
The source fingerprints were:

| File | SHA-256 |
| --- | --- |
| `native/ProbeLifetime.h` | `A196CECFE3BB53DAB416C6FC5409C503E700AF80B37AAC53F71C3A1C2BB7D898` |
| `native/service/ProbeLifetimeChecks.cpp` | `4729A85C36CDB85F9976F790ED87D4F46D66BE7024B55A9FFE4679FCD9BB2E42` |

Baseline and mutant snapshots were separate copies under ignored `lode/tmp/bound-map-publisher-mutation`.
The production tree retained the guard.
Both snapshots used container image `sha256:c27e64a12aad901398696208b037059a1a48946d1c1b425c89f08bff5ec1ccea`.
The container had no network, no capabilities, and a read-only root filesystem.
The source snapshot mounted at `/source` was read-only.
An executable temporary filesystem provided the compiler output directory.

Both snapshots used the same command:

```sh
clang++ -std=c++20 -O2 -Wall -Wextra -Werror -pthread /source/service/ProbeLifetimeChecks.cpp -o /tmp/probe-check && /tmp/probe-check
```

The mutation removed these three lines from the copied header:

```cpp
std::atomic<bool> finishing_{};
bool expectedFinishing = false;
if (!finishing_.compare_exchange_strong(expectedFinishing, true, std::memory_order_acq_rel)) return false;
```

The check releases two publishers through an atomic arrival barrier for 1,024 iterations.
Each publisher proposes a different native range.
Exactly one publication must succeed. The returned range must equal that publisher's range.
A later completion must not replace the range.

## Results

| Run | Exit | Verdict |
| --- | --- | --- |
| Initial baseline attempt | 126 | Infrastructure failure. The temporary filesystem denied execution. No mutant ran. |
| Baseline with executable temporary filesystem | 0 | All checks passed. |
| Guard-removal mutant with the same filesystem | 44 | Detected. The returned range did not equal the successful publisher's range. |

Both successful compilations produced no diagnostics.
The check reports failure through its exit code and does not print test output.
The observed mutant failure establishes that the competing publication path executed.
This stress check does not guarantee that every future run will expose the mutant.

The raw logs and result JSON remain in the ignored snapshot directory.
The initial infrastructure failure remains separate from the defect verdict.

See [the identity reference](../../docsSrc/Reference/probe-identity.md) and [the mapping explanation](../../docsSrc/Research/bound-native-mapping.md).
