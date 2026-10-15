# Pausable session guard mutations

Scope: the expected source epoch and enabled-state guards in `BoundCpuSession.Advance`.
The tests are the four `BoundCpuSessionChecks` checks from the current working source.
The manual runner copies the parser, its shared validator, the counter journal, and unchanged checks into isolated .NET 10 projects.
The copied journal excludes unrelated endpoint-range code. The assertion helper preserves the existing throwing behavior.
No mutation changes the repository source.

Evidence: `artifacts/pausable-session-mutations/20261015T144931692`.
`summary.json` records source hashes, exact commands, exit codes, and verdicts.
Each case retains its compiled project, source snapshot, removed expression, and process log.
`runtime.log` records the Windows SDK and runtime.

| Case | Mutation | Result |
| --- | --- | --- |
| `baseline` | None | Four checks pass; exit zero |
| `missing-epoch-guard` | Remove `snapshot.Epoch != nextEpoch` | Detected by `TransitionCorruptionCannotAdvance` |
| `missing-enabled-guard` | Remove `snapshot.Enabled != enabling` | Detected by `TransitionCorruptionCannotAdvance` |

Both mutants compiled and reached the parser through the same unchanged checks.
Both failed with `An invalid CPU session transition was accepted.`
The correlated malformed snapshots pass shared reading validation and expose the missing session guard.
The original source hashes remained unchanged throughout execution.

The earlier run `20261015T144826671` stopped because the runner could not locate the enabled-state mutation text.
That incomplete run is a runner failure, not evidence against either parser behavior or test quality.

These two detections establish sensitivity to the removed guards only.
They do not establish complete parser coverage, live perf correctness, or payment accuracy.

See [the source-session reference](../../docsSrc/Reference/bound-cpu-session.md).
