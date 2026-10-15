# How to analyze cooperative peer progress

Use this procedure for a retained selected-payment record with controller dispatch timing enabled.
The analyzer requires Python 3 and the repository's analysis scripts.

1. Choose the original case JSON file.
2. Run the analyzer with a separate output path:

   ```powershell
   python native/AnalyzeCooperativePeerProgress.py artifacts/run/50.json artifacts/run/50.peer-progress.json
   ```

3. Check `evidenceVerified` before using any timing result.
4. Check `casePassed` and `completed` separately. Diagnostic success does not establish workload success.
5. Read `peer.reason` when `peer.state` is `unavailable`.
6. When bounds exist, interpret them under the recorded clock assumptions.

A positive lower endpoint places the first child timestamp after the debt observation under those assumptions.
A missing child or submit bracket cannot establish whether the peer ran.
Keep the original record and the analysis output together.

See [the field reference](../Reference/cooperative-peer-progress.md) for identities, rounding, and unavailable states.
