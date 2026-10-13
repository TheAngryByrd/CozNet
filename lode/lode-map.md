# Lode index

Read [summary.md](summary.md), [terminology.md](terminology.md), and [practices.md](practices.md) at session start.
For profiling changes, read [the serial engine](profiling/summary.md).
For sampled experiment accounting, read [the delay ledger](profiling/delay-ledger.md).
For live managed delay settlement, read [the cooperative runtime](profiling/cooperative-runtime.md).
For sample-to-ledger conversion, read [sample credits](profiling/sample-credits.md).
For a Linux CPU-event source, read [Linux perf feasibility](profiling/linux-perf.md).
For the Windows CLR prototype, read [runtime probes](profiling/runtime-probes.md).
For build targets and CI, read [the MiniScaffold build](build/summary.md).
For scope expansion, read [the roadmap](plans/roadmap.md).
For the proposed runtime architecture, read [the Coz port and IL rewriting decision](../docsSrc/Research/coz-port-and-il-rewriting.md).
Keep this index aligned with the files. Session scraps belong in ignored `tmp/`.

```powershell
Get-Content lode/profiling/summary.md
```

```mermaid
flowchart TD
    Index --> Summary
    Index --> Terminology
    Index --> Practices
    Index --> Profiling
    Index --> Roadmap
```
