# .NET runtime sources

`Build.ps1` downloads headers, metadata helpers, signature parsing, and the IL rewriter from `dotnet/runtime`.
Revision: `60629d14374c56f1cb51819049ad1fa529307f8d` (`v10.0.0`).
Source: https://github.com/dotnet/runtime/tree/60629d14374c56f1cb51819049ad1fa529307f8d

The sources use the MIT license. See [the complete license](dotnet-runtime-LICENSE.txt).
The build preserves the downloaded files under `artifacts/native/runtime-<revision>/`.
It compiles a local IL rewriter copy with two `ULONG` variables changed to `uint32_t` for MSVC type compatibility.
It generates the callback base from the pinned `corprof.h` interface declarations.
Include this notice and the license when distributing the native binary.
