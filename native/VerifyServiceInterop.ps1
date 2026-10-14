param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/service-interop'), [switch]$SnapshotCostOnly, [switch]$SelectedOnly)
$ErrorActionPreference = 'Stop'
if ($SnapshotCostOnly -and $SelectedOnly) { throw 'Select snapshot costs or selected interop, not both.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$native = Join-Path $run 'native'
New-Item -ItemType Directory -Force $native | Out-Null
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
$compilerImage = & docker image inspect coznet-native-build:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The native compiler image is unavailable.' }
$runtimeImage = & docker image inspect mcr.microsoft.com/dotnet/sdk:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The Linux SDK image is unavailable.' }
$compiler = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only', '--tmpfs', '/tmp:rw,nosuid,size=64m', '--mount', "type=bind,source=$root,target=/source,readonly", '--mount', "type=bind,source=$native,target=/build", $compilerImage)

$sources = @('native/service/ServiceApi.h', 'native/service/ServiceApi.Linux.cpp', 'native/service/ApiChecks.cpp',
    'native/service/ServiceOwner.cpp', 'native/service/ServiceOwner.h', 'native/service/SignalTransport.Linux.cpp',
    'native/service/SignalTransport.Linux.h', 'native/service/CMakeLists.txt', 'native/signal/Signal.LinuxX64.cpp',
    'tests/CozNet.LinuxPerf/NativeServiceClient.cs', 'tests/CozNet.LinuxPerf/ServiceReceiptTiming.cs',
    'tests/CozNet.LinuxPerf/ServiceReceiptTimingChecks.cs', 'tests/CozNet.LinuxPerf/ServiceInteropChecks.cs',
    'tests/CozNet.LinuxPerf/ServiceAccountingAdapter.cs', 'tests/CozNet.LinuxPerf/ServiceAccountingAdapterChecks.cs',
    'tests/CozNet.LinuxPerf/ServiceSnapshotCosts.cs',
    'tests/CozNet.LinuxPerf/ServiceSelectedAdapter.cs', 'tests/CozNet.LinuxPerf/ServiceSelectedAdapterChecks.cs',
    'src/CozNet/SignalAccounting.fs', 'src/CozNet/SignalService.fs',
    'tests/CozNet.LinuxPerf/Program.cs', 'tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj', 'native/VerifyServiceInterop.ps1')
$hashes = @{}
foreach ($source in $sources) { $hashes[$source] = (Get-FileHash -LiteralPath (Join-Path $root $source) -Algorithm SHA256).Hash }
& docker @compiler cmake -S /source/native/service -B /build -G Ninja -DCMAKE_CXX_COMPILER=clang++ -DCMAKE_BUILD_TYPE=Release -DCOZ_SERVICE_LINUX_SIGNAL=ON *> (Join-Path $run 'configure.log')
if ($LASTEXITCODE -ne 0) { throw "Service ABI configuration failed. See $run/configure.log." }
& docker @compiler cmake --build /build --target ServiceApiChecks ServiceOwnerChecks *> (Join-Path $run 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Service ABI compilation failed. See $run/build.log." }
$binaries = @{}
$binaryPaths = @{}
foreach ($path in @((Join-Path $native 'libCozNet.Service.so'), (Join-Path $native 'signal/libCozNet.Signal.so'),
    (Join-Path $native 'ServiceApiChecks'), (Join-Path $workload 'CozNet.LinuxPerf.dll'), (Join-Path $workload 'CozNet.dll'))) {
    $binaries[[IO.Path]::GetFileName($path)] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $binaryPaths[[IO.Path]::GetFileName($path)] = $path
}
@{ compilerImage = $compilerImage; runtimeImage = $runtimeImage; sources = $hashes; binaries = $binaries;
    managedReceiptBoundNanoseconds = 100000000; delayedReceiptMilliseconds = 150; watchdogSeconds = 30 } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'manifest.json')

$outcomes = [Collections.Generic.List[object]]::new()
& docker @compiler ctest --test-dir /build -R '^service-' --output-on-failure *> (Join-Path $run 'native-checks.log')
$outcomes.Add(@{ name = 'native'; exitCode = $LASTEXITCODE; recordExists = $true })
$runtime = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
    '--tmpfs', '/tmp:rw,nosuid,size=64m', '--env', 'LD_LIBRARY_PATH=/native:/native/signal',
    '--mount', "type=bind,source=$native,target=/native,readonly", '--mount', "type=bind,source=$workload,target=/work,readonly",
    '--mount', "type=bind,source=$run,target=/output", $runtimeImage, 'timeout', '-k', '2s', '30s', 'dotnet', '/work/CozNet.LinuxPerf.dll')
& docker @runtime --verify-service-receipt *> (Join-Path $run 'receipt-parser.log')
$outcomes.Add(@{ name = 'receipt-parser'; exitCode = $LASTEXITCODE; recordExists = $true })
& docker @runtime --verify-service-accounting *> (Join-Path $run 'accounting-adapter.log')
$outcomes.Add(@{ name = 'accounting-adapter'; exitCode = $LASTEXITCODE; recordExists = $true })
foreach ($mode in $(if ($SnapshotCostOnly) { @('snapshot-cost') } else { @('receipt', 'late') })) {
    if ($SnapshotCostOnly) {
        & docker @runtime --service-snapshot-cost "/output/$mode.json" *> (Join-Path $run "$mode.log")
    } elseif ($SelectedOnly) {
        & docker @runtime --verify-selected-service-interop "/output/$mode.json" $mode *> (Join-Path $run "$mode.log")
    } else {
        & docker @runtime --verify-service-interop "/output/$mode.json" $mode *> (Join-Path $run "$mode.log")
    }
    $outcomes.Add(@{ name = $mode; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run "$mode.json") })
    $outcomes | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $run 'outcomes.json')
}
$unchanged = $true
foreach ($source in $sources) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $root $source) -Algorithm SHA256).Hash -eq $hashes[$source] }
$binariesUnchanged = $true
foreach ($name in $binaryPaths.Keys) { $binariesUnchanged = $binariesUnchanged -and (Get-FileHash -LiteralPath $binaryPaths[$name] -Algorithm SHA256).Hash -eq $binaries[$name] }
$passed = $unchanged -and $binariesUnchanged -and @($outcomes | Where-Object { $_.exitCode -ne 0 -or -not $_.recordExists }).Count -eq 0
@{ passed = $passed; sourcesUnchanged = $unchanged; binariesUnchanged = $binariesUnchanged; outcomes = $outcomes.ToArray(); generalClrSafetyEstablished = $false;
    sampledCapacityEstablished = $false; predictionAccuracyEstablished = $false } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'summary.json')
Write-Output $run
if (-not $passed) { throw 'Service interop checks failed. Retain this run.' }
