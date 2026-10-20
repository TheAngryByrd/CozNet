# Target-local discovery evidence contract

Status: approved on 2026-10-19. Plan and criteria recorded before the implementation, and implemented. No capture ran for it.

## Gap

`native/AnalyzeTargetDiscovery.py` at `806846b` validates only the current record shape. The record has no version. So the retained capture `20261020T015420785-8c8adf21`, which passed under its own contract, is now reported as malformed. It is not identified as an older contract.

## Contract versions

| Contract | Records | Selection |
| --- | --- | --- |
| `discovery-check/2` | Records of the workflow sample from this change on. Each record has `schema: "coznet.discovery-check/2"` and `mapHashKind: "FileBytesSha256"`, also in the error and unsupported records. | The `schema` field of the record. |
| `legacy-1` | Unversioned records of the harness at `a0f650f`, which ran before the workflow existed. | The caller passes `--contract legacy-1`, or the record has no `schema` and the manifest commit is `a0f650f5ef37b4a665c365eb975b724ea44af6c9`. |

The analyzer selects the contract before it checks any field that depends on the version:
- A record with the current `schema` is checked only by the current contract. A missing current field is a failure. The analyzer never falls back to the legacy contract.
- A record with another `schema` value gives the assessment `unsupported`, with the reason `unsupported schema`. It never passes. A `schema` key with the value `null` is present, so it is unsupported too. Review round 1 added this rule.
- A manifest commit that is not a string never selects the legacy contract.
- `--contract` accepts only `legacy-1`. The current contract is selected only by the record schema.
- An unversioned record with another commit, and without `--contract legacy-1`, gives `unsupported`. The analyzer does not infer the legacy contract from missing fields.
- `--contract legacy-1` with a versioned record gives `unsupported`, because the two selections conflict.

## The legacy contract

The legacy path applies the checks of the analyzer at `a0f650f`. It reuses the common checks of the current contract and does not weaken them:
- the run, the Linux tests, the map path, the retained map hash, the class conservation, the accepted samples, the fixture samples, the binding of the ranked ranges, the anchors, the hashed `Work.fs`, and a valid design.

It does not apply the checks that the old record cannot meet: the eligibility values, the outside-binding list, the design lists, and the hash kind.
The legacy path uses the common checks of the current analyzer. A later change to a common check can change a legacy assessment. To reproduce the exact original assessment, run the analyzer of `a0f650f` (`git show a0f650f:native/AnalyzeTargetDiscovery.py`).
Its summary states these as `notEvaluated`. The assessment of a legacy record never counts as validation of the library workflow.

## Hashes

- `FileBytesSha256`: the recorded hash is the SHA-256 of the raw bytes of the retained map file. A final newline and a byte-order mark are part of those bytes.
- The legacy record wrote the lines joined with `\n` without a final newline, and recorded the SHA-256 of those bytes. The retained file holds exactly those bytes.
- For both contracts, the analyzer hashes the retained file bytes without any normalization, and compares them with the recorded hash.

## Summary

Each summary keeps these fields apart:
- `contract`: `discovery-check/2`, `legacy-1`, or none.
- `selection`: how the contract was selected.
- `assessment`: `passed`, `failed`, or `unsupported`.
- `notEvaluated`: the checks that the contract does not apply.
- `workflowEvaluated`: true only for the current contract.
- The exit code is 0 for a current pass, 3 for a legacy pass, and 1 for a failure or an unsupported record. So a script that reads only the exit code cannot take a legacy pass for a current pass.

The analyzer takes `--output <path>`. Without it, it writes `summary.json` in the run folder, as the runner needs. A retained run is analyzed with `--output` outside its folder, so its summary and manifest stay unchanged.

## Acceptance criteria

These criteria are fixed before the implementation. They add to criteria 1 to 200 and change none.

201. Every record of the sample mode `discovery-check` has `schema` and `mapHashKind`, including the unsupported, configuration, binding, and map error records.
202. The checks cover a current record that passes, a current record without each current field, an unknown schema, an unversioned record with another commit, a conflict between `--contract legacy-1` and a versioned record, and a legacy record with the shape of the retained record.
203. A current record without its current fields fails under the current contract. It does not pass under the legacy contract.
204. The hash checks cover a retained file with a final newline and with a byte-order mark. A raw-byte hash must equal the file bytes as they are, and a hash of normalized lines must not pass.
205. The analyzer reads a copy of the retained capture `20261020T015420785-8c8adf21`, and its summary, manifest, and record stay unchanged. The result is reported as the legacy assessment, apart from any workflow claim.
206. The current eligibility and design checks stay as they are.
207. No live capture runs.

## Result

- `native/AnalyzeTargetDiscovery.py` selects the contract with `select_contract` before it checks any field that depends on the version. It takes `--contract` and `--output`.
- The sample writes `schema` and `mapHashKind` in every record. The main record takes `mapHashKind` from the hash of its snapshot.
- 18 analyzer checks pass, 12 of them new: the contract cases, including a null schema and a commit that is not a string, and the hash cases. One new F# check covers the line hash and the byte hash with a final newline and a byte-order mark.
- The retained capture `20261020T015420785-8c8adf21` was analyzed with `--output` outside its folder. Its summary, manifest, record, and map have the same SHA-256 before and after.
  - Compatibility: contract `legacy-1`, selected by the pinned legacy commit `a0f650f`.
  - Historical assessment: `passed`, with no reason, and exit code 3.
  - `workflowEvaluated` is false, and `notEvaluated` lists the eligibility, the outside-binding list, the design lists, the hash kind, and the provenance of the library workflow.
  - This result says nothing about the library workflow. No live run has used the workflow.

See [the discovery check](target-local-discovery-check.md) and [the discovery workflow](target-local-discovery-workflow.md).
