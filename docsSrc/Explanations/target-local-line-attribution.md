# Target-local line attribution and its limits

Line attribution maps a sampled native address inside one method to a source line. This page explains where the map comes from, why a result can be a range or no line at all, and what the current code does not do.

## The map belongs to the runtime

The perf map gives only the native range of a whole method. A `MethodInfo` gives no native addresses. The runtime keeps a native-to-IL map for each compiled body, and a profiler can read it with `GetILToNativeMapping3`.
CozNet does not read that map yet. The resolver takes a map that the caller supplies, labeled with its source. See [the research note](../Research/clr-native-to-il-mapping.md).

## How one address becomes a line

1. The address must be inside the one hot code region of the method, and that region must equal the range of the method in a discovery outcome.
2. The resolver finds the map entry that holds the address. The end of each entry is exclusive.
3. The entry gives an IL offset, or it marks the prolog, the epilog, or code without a mapping.
4. The resolver takes the sequence point with the greatest IL offset at or below that offset. The point gives the document and the source span.

A hidden sequence point gives no line. The resolver does not take a neighbor line instead.

## Why a line is approximate

- For optimized code, the runtime keeps map boundaries only where the stack is empty and at calls. So one entry can cover several statements.
- Inlined code maps to the call site in the caller.
- The Portable PDB specification does not say which IL range a sequence point covers. CozNet infers that it covers the IL up to the next point, or to the end of the method body. Each result states this inference.

So each line result keeps its IL range and its source span, and each resolver lists these limits.

## What the resolver rejects

- Cold code, a second code body, and generic methods, which can share one body between instantiations.
- A map entry with an IL offset at or beyond the IL body, or with an unknown special value.
- Overlapping map entries. The runtime source does not prove that they cannot occur, so the resolver reports an unsupported shape, not a malformed map.

A part of the body that no entry covers is reported as uncovered. The resolver does not guess a line for it. For example, when a method ends with calls, the runtime map leaves the code from the start of the last of those calls to the end without an entry.

## What it cannot show

- The map, the PDB, and the samples are only checked against each other. Matching addresses do not prove that the same code stayed at those addresses for the whole run.
- IL rewriting is not excluded. The caller must know that the method was not rewritten.
- No live run has used line attribution.

See [the API reference](../Reference/target-local-api.md#line-attribution) and [candidate discovery and its limits](target-local-discovery.md).
