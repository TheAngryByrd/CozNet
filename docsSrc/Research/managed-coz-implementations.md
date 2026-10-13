# Managed-runtime Coz implementations

Date: 2026-10-13.

This report compares the three repositories named by the user. The duplicate Swift URL identifies one repository.
This was a source review. No implementation was built or executed.

## Decision

Use JCoz as a reference for runtime integration and delay accounting.
Use Swift Coz and cozgo as examples of marker bindings only.
Neither binding implements a replacement profiling engine.
These findings support a .NET runtime adapter plus an experiment engine, rather than a marker wrapper alone.
The evidence and limits appear below.

## Source versions

The review used the default-branch revisions returned by GitHub on the report date.
`Decave/JCoz` redirects to `ByteLab-David/JCoz`.

| Repository | Revision | License | Implemented role |
|---|---|---|---|
| [JCoz](https://github.com/ByteLab-David/JCoz/tree/1df02b0f6e319bd7df05ba5451b1814791d1849f) | `1df02b0f6e319bd7df05ba5451b1814791d1849f` | Apache-2.0 | Native JVM agent and causal experiment engine |
| [Swift Coz](https://github.com/funcmike/swift-coz/tree/fe4bedf70edd89a49601ea1e48f2af660365e085) | `fe4bedf70edd89a49601ea1e48f2af660365e085` | MIT | Swift calls into native Coz marker macros |
| [cozgo](https://github.com/urjitbhatia/cozgo/tree/d33f1dc723ec035e65022331b6677f5013da596a) | `d33f1dc723ec035e65022331b6677f5013da596a` | MIT | Go calls into native Coz marker macros |

## JCoz

JCoz contains its own profiling engine. Its native directory explicitly owns the JVMTI integration and causal profiling.
It is not a Java wrapper for `libcoz`.
See [native ownership](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/README#L1-L3) and [engine](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L198-L270).

### Sampling and source identity

- The agent sends `SIGPROF` to registered application threads with `pthread_kill`.
  The handler obtains JVM frames through `AsyncGetCallTrace`.
  Sources: [signal dispatch](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L156-L165), [stack capture](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L573-L609).
- The selection phase randomizes sample timing, shuffles captured frames, and obtains a JVMTI line-number table.
  An experiment identifies a method and bytecode-location ranges for one source line.
  Sources: [selection](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L289-L373), [matching](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L468-L481).
- Experiment sampling uses a nominal 1 ms interval. The selected speedup determines the delay credit per matching sample.
  Speedup selection gives zero an explicit baseline probability and otherwise selects 5% increments through 100%.
  Sources: [interval](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L51-L52), [selection and loop](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L178-L216).

### Delay accounting

Each registered thread has a local delay counter. The process has a global delay counter.
A sample that contains the selected location increases the local counter.
Every ten signals, the thread compares its counter with the global counter.
A thread behind the global counter sleeps and adds the measured sleep duration.
A thread ahead increases the global counter.
Source: [intervention handler](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L642-L666).

New registered threads inherit the global delay counter.
Thread removal reconciles pending delay and accumulated progress before removal.
Source: [thread lifecycle](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L422-L465).

These are useful contracts for a .NET design.
They differ from CozNet's current serial control/treatment model, which delays marked regions directly.
See [current model](../Explanations/Background.md).

### Synchronization and runtime limits

The registered JVMTI callbacks cover thread lifecycle, VM lifecycle, class loading, class preparation, and breakpoints.
They do not register monitor-enter, monitor-wait, or monitor-waited callbacks.
The delay handler reconciles counters at sample boundaries.
This inspected path does not establish synchronization-aware credit transfer for all JVM waits.
Do not treat it as proof of parity with native Coz synchronization handling.
Sources: [callbacks](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/entry.cc#L339-L372), [handler](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L642-L666).

The thread filter accepts only threads whose immediate thread-group name equals `main`.
That condition limits which threads receive profiling signals.
Source: [thread filter](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L398-L440).

The agent resolves the JVM-specific `AsyncGetCallTrace` symbol at load time.
The repository requires JDK 1.8 and reports Maven failures with newer JDKs.
These facts make JCoz an architectural reference, not a portable runtime component for .NET.
Sources: [agent loading](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/entry.cc#L405-L439), [dependencies](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/README.md#L11-L17).

### Progress and results

A configured source line becomes a JVMTI breakpoint.
The breakpoint callback counts hits during an experiment.
Sources: [breakpoint installation](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L508-L537), [hit counter](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L794-L803).

The engine exports the selected source line, virtual speedup, elapsed duration minus global delay, and progress count.
It adjusts experiment duration when progress counts are low or high.
The README describes Coz-format output and use of the existing Coz viewer.
Sources: [result calculation](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L230-L257), [viewer instructions](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/README.md#L82-L86).

The inspected implementation uses one configured progress point and an end-to-end mode.
It does not show the separate named begin/end counters exposed by the Swift and Go bindings.
Sources: [progress configuration](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L88-L91), [end-to-end loop](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L198-L216).

### Implications for IL rewriting

JCoz's inspected path combines asynchronous samples, source mapping, and a breakpoint for progress.
It does not rewrite application bytecode to insert a delay at every selected line.
The registered callbacks contain no `ClassFileLoadHook`.
The requested bytecode-reading capability does not establish bytecode rewriting.
Sources: [capabilities and callbacks](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/entry.cc#L299-L372), [progress breakpoint](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L508-L537), [sample intervention](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L573-L666).

This provides a useful design distinction for .NET.
Instrumentation can supply progress points without implementing target selection or global delay accounting.
IL rewriting could serve that instrumentation role, but these projects do not prove that it solves managed sampling or synchronization.
The .NET decision needs separate evidence for CLR APIs and runtime safety.

## Swift Coz and cozgo

Both projects expose begin, end, named progress, and anonymous progress calls.
Each call reaches a C function that expands the corresponding native Coz macro.
Neither shim contains sampling, delay counters, synchronization interception, or an experiment loop.
Sources: [Swift API](https://github.com/funcmike/swift-coz/blob/fe4bedf70edd89a49601ea1e48f2af660365e085/Sources/Coz/Coz.swift#L1-L22), [Swift shim](https://github.com/funcmike/swift-coz/blob/fe4bedf70edd89a49601ea1e48f2af660365e085/Sources/CCoz/shim.h#L1-L17), [Go API](https://github.com/urjitbhatia/cozgo/blob/d33f1dc723ec035e65022331b6677f5013da596a/coz.go#L1-L34), [Go shim](https://github.com/urjitbhatia/cozgo/blob/d33f1dc723ec035e65022331b6677f5013da596a/cozgo.h#L1-L17).

Both require a separate Coz installation and launch the application through `coz run`.
Both use native Coz output and its viewer.
Swift explicitly limits usage to Linux and recommends a debug build.
Go recommends disabling DWARF compression, optimization, and inlining in its build command.
Sources: [Swift usage](https://github.com/funcmike/swift-coz/blob/fe4bedf70edd89a49601ea1e48f2af660365e085/README.md#L23-L55), [Go usage](https://github.com/urjitbhatia/cozgo/blob/d33f1dc723ec035e65022331b6677f5013da596a/README.md#L24-L36).

The Go API allocates and frees a C string on each named marker call.
The Swift API converts each name through `NSString.utf8String`.
A .NET marker API should register stable identifiers once and measure hot-path overhead.
This is a design recommendation, not a measured performance result.
Sources: [Go conversion](https://github.com/urjitbhatia/cozgo/blob/d33f1dc723ec035e65022331b6677f5013da596a/coz.go#L9-L28), [Swift conversion](https://github.com/funcmike/swift-coz/blob/fe4bedf70edd89a49601ea1e48f2af660365e085/Sources/Coz/Extensions.swift#L1-L7).

The shims funnel all names through one macro expansion per marker type.
They also expand anonymous progress inside the shim.
A .NET adapter must verify name identity and caller-source identity against the native macro contract.
Do not copy this arrangement without a test that uses two different names and two caller locations.
Sources: [Swift shim](https://github.com/funcmike/swift-coz/blob/fe4bedf70edd89a49601ea1e48f2af660365e085/Sources/CCoz/shim.h#L1-L17), [Go shim](https://github.com/urjitbhatia/cozgo/blob/d33f1dc723ec035e65022331b6677f5013da596a/cozgo.h#L1-L17).

Neither shim contains hooks for goroutine migration or Swift task scheduling.
Their existence therefore does not prove causal correctness for managed tasks or .NET `async` methods.
This conclusion concerns the inspected binding code. It is not a claim that every workload produces incorrect results.
Sources: [Swift binding](https://github.com/funcmike/swift-coz/blob/fe4bedf70edd89a49601ea1e48f2af660365e085/Sources/Coz/Coz.swift#L1-L22), [Go binding](https://github.com/urjitbhatia/cozgo/blob/d33f1dc723ec035e65022331b6677f5013da596a/coz.go#L1-L34).

## Reuse obligations

JCoz uses Apache-2.0.
Redistribution requires a license copy, prominent notices in modified files, and retention of applicable source notices.
If the distribution includes a `NOTICE` file, its applicable attribution notices must accompany derivative distributions.
The license also contains a patent grant and patent-litigation termination terms.
Source: [JCoz license, sections 3 and 4](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/LICENSE#L73-L128).

Swift Coz and cozgo use MIT.
Their copyright and permission notices must remain in all copies or substantial portions of the software.
The notices name Krzysztof Majk and Urjit Singh Bhatia, respectively.
Sources: [Swift license](https://github.com/funcmike/swift-coz/blob/fe4bedf70edd89a49601ea1e48f2af660365e085/LICENSE#L1-L21), [Go license](https://github.com/urjitbhatia/cozgo/blob/d33f1dc723ec035e65022331b6677f5013da596a/LICENSE#L1-L21).

These licenses govern the reviewed projects. They do not replace the license obligations of a separately included native Coz distribution.

## Port recommendations

The following recommendations derive from the implementation evidence above. They are proposed work, not current CozNet capabilities.

1. Separate the experiment engine from runtime-specific sampling and source mapping.
2. Define global and per-thread delay counters, actual-delay measurement, and thread lifecycle rules before parallel profiling.
3. Specify synchronization credit transfer independently. JCoz's sample-boundary accounting does not establish this contract for .NET.
4. Add stable progress identifiers and separate throughput and latency marker semantics.
5. Preserve source identity, requested speedup, raw elapsed duration, injected delay, and progress counts in results.
6. Use Coz-format export for comparison, but retain richer raw records for diagnosis.
7. Prove CPU-thread behavior before extending claims to tasks, continuations, GC pauses, or blocking operations.
8. Validate predictions against real optimizations. Marker bindings and a compatible file format do not demonstrate causal accuracy.

See [the original research](causal-profiling-dotnet.md) and [the current roadmap](../../lode/plans/roadmap.md).
