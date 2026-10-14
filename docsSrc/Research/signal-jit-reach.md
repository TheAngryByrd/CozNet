# Proving signal interruption inside the CoreCLR JIT

## Finding

A fresh `DynamicMethod` plus `RuntimeHelpers.PrepareDelegate` provides a suitable bounded JIT workload on Linux x64 CoreCLR.
Use a new method and a single-target delegate for each iteration.
Preparation compiles an uncompiled target synchronously through the prestub path. Preparation can return without compilation when the target already has code. [1][2]

Require an acknowledgment whose saved interrupted instruction pointer lies inside an executable mapping of the loaded `libclrjit.so`.
Also require the target thread to complete and verify the generated methods.
Together, these observations establish interruption in compiler code and subsequent workload completion for that run.
They do not establish general CLR signal safety.

This report contains source analysis and a proposed verification contract. It contains no executed workload results.

## Primary call chain

The .NET source pin is `60629d14374c56f1cb51819049ad1fa529307f8d`.

1. `RuntimeHelpers.PrepareDelegate` calls `ReflectionInvocation_PrepareDelegate` for a non-null delegate. [1]
2. The native function resolves the delegate method and calls `PrepareMethodHelper`. [2]
3. `PrepareMethodHelper` calls `DoPrestub` when the method still points to the prestub. It also prepares a wrapped method when needed. [2]
4. The ordinary IL path calls `PrepareInitialCode`, then `PrepareCode`, then `PrepareILBasedCode`. [3]
5. When no existing code is available, `PrepareILBasedCode` calls `JitCompileCode`. The compilation path reaches `UnsafeJitFunction`. [3]
6. The JIT interface calls the selected compiler's `compileMethod`. [4]

These are direct calls on the preparing thread. Give each fresh method to that thread only.
Do not infer that every `PrepareDelegate` call enters the compiler.
CoreCLR can reuse existing code, and the helper does not recursively prepare the target's call graph. [2][3]

`COMDelegate::GetMethodDesc` treats multicast delegates differently from single-target delegates.
For a multicast invocation array, it resolves the delegate invoke method. It does not iterate every target. [5]

## Workload shape

Use the explicit module constructor with `skipVisibility: false`. [6]

```csharp
var method = new DynamicMethod(
    $"SignalJit_{iteration}", typeof(int), new[] { typeof(int) },
    typeof(Program).Module, skipVisibility: false);
var il = method.GetILGenerator();
il.Emit(OpCodes.Ldarg_0);
il.Emit(OpCodes.Ldc_I4, iteration);
il.Emit(OpCodes.Add);
il.Emit(OpCodes.Ret);
var target = (Func<int, int>)method.CreateDelegate(typeof(Func<int, int>));
RuntimeHelpers.PrepareDelegate(target);
int actual = target(input);
if (actual != unchecked(input + iteration))
    throw new InvalidOperationException("Dynamic method result mismatch.");
GC.KeepAlive(target);
```

This example explains the contract. A longer, bounded arithmetic IL body can increase the compiler sampling window.
Keep its expected result independent of the generated implementation.
Set fixed limits for method count, IL size, requests, and process duration.
Do not extend the deadline until a hit appears.

Set the workload phase before `CreateDelegate` and clear it after preparation.
`CreateDelegate` explicitly compiles methods when `_restrictedSkipVisibility` is true. [7]
The explicit module constructor avoids that specific branch. The wider phase still avoids an unnecessary dependence on exact eager-compilation behavior. [6][7]

Record the phase separately from instruction ownership.
A signal inside a preparation phase alone does not prove compiler execution.
The phase also includes runtime and delegate work outside `libclrjit.so`.

Use a dynamic-code-enabled CoreCLR configuration. The `DynamicMethod` constructor requires dynamic code. [6]
Record runtime version, loaded compiler identity, environment settings, and target TID with the artifact.
Disable tiering for this bounded workload to reduce unrelated background compilation.

## Reach evidence

Before measured delivery, prepare one warm-up method and read `/proc/self/maps` outside the signal handler.
Parse the actual loaded compiler's executable mappings. Preserve start, end, permissions, device, inode, and pathname.
Linux exposes the mapped address range, access permissions, and backing file in this interface. [8]

Compare addresses as unsigned pointer-width values. Use `start <= ip && ip < end` for each executable segment.
Do not combine segments into one broad range that includes holes.
Match the known loaded compiler file. A substring match against arbitrary pathnames is insufficient evidence of module identity.
Keep the compiler loaded for the full measurement period.

Read the interrupted RIP from the signal's saved context, not from the handler's current instruction pointer.
The Linux x86 signal code stores `regs->ip` in the signal context before it redirects execution to the handler. [9]
On Linux x64, use the ABI's saved general-register field for RIP.

For each acknowledged request, retain these fields:

- Request token and target TID.
- Saved interrupted RIP and workload phase.
- Matching executable mapping, if present.
- Handler disposition: deferred or waited.
- Completion status and bounded handler measurements.

An IP in `libclrjit.so` identifies execution in the compiler image.
It does not identify the generated method's machine code, which has a different allocation.
It also does not identify a particular compiler pass or prove which method caused that compiler execution.
Symbol resolution or correlated runtime events would be needed for those stronger claims.

## Required gates

1. Complete and verify at least the fixed minimum number of fresh dynamic methods on the registered target thread.
2. Obtain at least one valid acknowledgment from that target with saved RIP inside an executable `libclrjit.so` mapping.
3. Require that acknowledgment to belong to the declared JIT workload phase.
4. Preserve the arithmetic, GC, request, retirement, and watchdog bounds from the existing signal experiment.
5. Retire and join the target successfully after the controller stops delivery.

Zero compiler hits fail the reach gate. They do not prove that the workload avoided the JIT or that signal delivery was safe.
Repeated preparation without a compiler hit is insufficient evidence.

The current arithmetic-only wait range should defer a request that interrupts the compiler.
A deferred acknowledgment proves signal-handler reach into a JIT interruption.
It does not prove that the handler waited during JIT execution.
Require a separate explicit gate and safety assessment before making that stronger claim.

An acknowledgment also does not prove signal-handler return. Target completion and retirement provide separate normal-context evidence.
See [signal lifetime](signal-request-lifetime.md) and [native intervention](native-signal-intervention.md).

## Sources

[1]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs#L221-L232
[2]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/reflectioninvocation.cpp#L1248-L1335
[3]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/prestub.cpp
[4]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/jitinterface.cpp#L12813-L12863
[5]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/comdelegate.cpp#L1815-L1875
[6]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/libraries/System.Private.CoreLib/src/System/Reflection/Emit/DynamicMethod.cs#L80-L98
[7]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/DynamicMethod.CoreCLR.cs#L44-L61
[8]: https://man7.org/linux/man-pages/man5/proc_pid_maps.5.html
[9]: https://github.com/torvalds/linux/blob/8bb7eca972ad531c9b149c0a51ab43a417385813/arch/x86/kernel/signal.c#L149-L187

- [Runtime preparation API][1] and [native preparation path][2].
- [Prestub and compilation][3]: lines 297–318, 376–425, 907–921, and 2272–2279.
- [Compiler invocation][4] and [delegate method resolution][5].
- [Dynamic method constructor][6] and [eager delegate compilation][7].
- [Linux mapping format][8] and [saved signal context][9].
