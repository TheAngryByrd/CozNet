param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/perf-ring'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
$image = & docker image inspect coznet-native-build:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The native compiler image is unavailable.' }
$sources = @('native/service/PerfSampleRing.h', 'native/service/PerfSampleRingChecks.cpp', 'native/service/PerfRing.Linux.h', 'native/service/PerfRingChecks.Linux.cpp',
    'native/service/PerfRingWorkload.Linux.cpp', 'native/service/CMakeLists.txt', 'native/VerifyPerfRing.ps1', 'build/build.fs')
$hashes = @{}
foreach ($name in $sources) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash }
$container = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
    '--tmpfs', '/tmp:rw,nosuid,size=64m', '--mount', "type=bind,source=$root,target=/source,readonly",
    '--mount', "type=bind,source=$run,target=/build")
& docker @container $image uname -a *> (Join-Path $run 'kernel.txt')
if ($LASTEXITCODE -ne 0) { throw 'Kernel identity capture failed.' }
& docker @container $image sh -c 'cat /proc/sys/kernel/perf_event_paranoid' *> (Join-Path $run 'paranoid.txt')
& docker @container $image cmake -S /source/native/service -B /build -G Ninja -DCMAKE_CXX_COMPILER=clang++ -DCMAKE_BUILD_TYPE=Release *> (Join-Path $run 'configure.log')
if ($LASTEXITCODE -ne 0) { throw "Perf ring configuration failed. See $run/configure.log." }
& docker @container $image cmake --build /build --target PerfRingChecks PerfRingWorkload PerfSampleRingChecks *> (Join-Path $run 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Perf ring compilation failed. See $run/build.log." }
$binaries = @{}
foreach ($name in @('PerfSampleRingChecks', 'PerfRingChecks', 'PerfRingWorkload')) { $binaries[$name] = (Get-FileHash -LiteralPath (Join-Path $run $name) -Algorithm SHA256).Hash }
@{ sources = $hashes; binaries = $binaries; image = $image; capabilities = 'none'; seccomp = 'unconfined'; network = 'none' } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'manifest.json')
& docker @container $image ctest --test-dir /build -R '^(perf-ring|perf-sample-ring)$' --output-on-failure *> (Join-Path $run 'contracts.log')
$contractExit = $LASTEXITCODE
$outcomes = @()
foreach ($mode in @('normal', 'overflow', 'epochs')) {
    & docker @container --security-opt seccomp=unconfined $image timeout -k 2s 30s /build/PerfRingWorkload $mode "/build/$mode.json" *> (Join-Path $run "$mode.log")
    $outcomes += @{ mode = $mode; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run "$mode.json") }
}
$unchanged = $true
foreach ($name in $sources) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash -eq $hashes[$name] }
foreach ($name in $binaries.Keys) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $run $name) -Algorithm SHA256).Hash -eq $binaries[$name] }
$passed = $unchanged -and $contractExit -eq 0 -and @($outcomes | Where-Object { $_.exitCode -ne 0 -or -not $_.recordExists }).Count -eq 0
@{ passed = $passed; inputsUnchanged = $unchanged; contractExitCode = $contractExit; outcomes = $outcomes
    targetNotificationEstablished = $false; nativeCreditEstablished = $false; generalClrSafetyEstablished = $false
    serviceCapacityEstablished = $false; predictionAccuracyEstablished = $false
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'summary.json')
Write-Output $run
if (-not $passed) { throw 'Perf ring checks failed. Retain this run.' }
