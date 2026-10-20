# How to verify the map capture component

Use this procedure to check the native map capture library and its parser without a CLR.
Run commands from the repository root with the .NET 10 SDK.
The native checks also require PowerShell 7, a Linux Docker engine, and the local image `coznet-native-build:10.0.100`.

1. Build the compiler image and obtain the pinned runtime sources, and then run the native checks:

   ```sh
   dotnet run --project build/build.fsproj -- -t MapProfilerVerify
   ```

   The runner stops when the runtime checkout is not commit `60629d14374c56f1cb51819049ad1fa529307f8d` or has local changes.
   It generates the base classes from the official headers into `artifacts/native-linux/mapprofiler/generated`.
   The generator stops when a parsed method count differs from the declared count, when a used IID differs from its `MIDL_INTERFACE` uuid, or when a callback with an `[out]` parameter has no override.

2. Read the runner output. Each variant must show `0 failed` and `24 fixtures are identical`:

   ```text
   map capture checks: 74 passed, 0 failed
   release: 24 fixtures are identical
   map capture checks: 74 passed, 0 failed
   thread: 24 fixtures are identical
   ```

   The `release` variant also checks that the library exports only `DllGetClassObject` and `coz_map_read`.
   The `thread` variant runs the same checks under ThreadSanitizer and stops at the first report.
   It also runs a seeded race, and it stops when ThreadSanitizer does not report that race.

3. Inspect `artifacts/native-linux/mapprofiler/thread/evidence.txt`.
   Confirm that both objects reference `__tsan_` symbols and that the checks load `libtsan.so.2`.
   Confirm that it holds `seeded race: reported with exit code 66`.
   The file also holds the compile and link commands, the clang version, the image id, and the `libtsan.so.2` hash.

4. Run the parser checks in Release:

   ```sh
   dotnet build tests/CozNet.Tests/CozNet.Tests.fsproj -c Release
   dotnet tests/CozNet.Tests/bin/Release/net10.0/CozNet.Tests.dll --filter "target map capture"
   ```

   The checks parse the committed records in `tests/CozNet.Tests/fixtures/map-capture`.

A passing run shows that the native writer and the F# parser use the same bytes.
It does not show that the runtime gives a correct map, because no CLR loads the library.

See [the map capture reference](../Reference/target-local-api.md#map-capture) and [the limits of line attribution](../Explanations/target-local-line-attribution.md).
