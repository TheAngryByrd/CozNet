param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/target-lease-delivery'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
$image = & docker image inspect coznet-native-build:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The native compiler image is unavailable.' }
$hashes = @{}
foreach ($name in @('native/service/TimerDeliveryChecks.Linux.cpp', 'native/service/TargetLease.h', 'native/service/TargetLease.cpp', 'native/service/TargetLeaseChecks.cpp', 'native/service/TimerDeliveryWorkload.LinuxX64.S', 'native/service/CMakeLists.txt', 'native/VerifyTargetLeaseDelivery.ps1', 'build/build.fs')) {
    $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash
}
$container = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
    '--tmpfs', '/tmp:rw,nosuid,size=64m', '--mount', "type=bind,source=$root,target=/source,readonly",
    '--mount', "type=bind,source=$run,target=/build", $image)
& docker @container cmake -S /source/native/service -B /build -G Ninja -DCMAKE_CXX_COMPILER=clang++ -DCMAKE_BUILD_TYPE=Release -DCOZ_SERVICE_LINUX_SIGNAL=ON *> (Join-Path $run 'configure.log')
if ($LASTEXITCODE -ne 0) { throw "Timer fixture configuration failed. See $run/configure.log." }
& docker @container cmake --build /build --target TimerDeliveryChecks TargetLeaseChecks *> (Join-Path $run 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Timer fixture build failed. See $run/build.log." }
$binaryHash = (Get-FileHash -LiteralPath (Join-Path $run 'TimerDeliveryChecks') -Algorithm SHA256).Hash
@{ sources = $hashes; binary = $binaryHash; image = $image; budgetNanoseconds = 500000;
    requestBoundNanoseconds = 50000; handlerBoundNanoseconds = 5000000; receiptBoundNanoseconds = 100000000;
    initialIntervalNanoseconds = 1000000; rearmIntervalNanoseconds = 10000; cases = @('chain', 'snapshot-chain', 'pending-retirement', 'cancel-rearm'); clrWorkload = $false
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'manifest.json')
& docker @container ctest --test-dir /build -R '^target-lease$' --output-on-failure *> (Join-Path $run 'lease-checks.log')
$contractResult = $LASTEXITCODE
$outcomes = @(@{ mode = 'target-lease'; exitCode = $contractResult; recordExists = $true })
$outcomes += foreach ($mode in @('chain', 'snapshot-chain', 'pending-retirement', 'cancel-rearm')) {
    & docker @container timeout -k 2s 10s /build/TimerDeliveryChecks $mode "/build/$mode.json" *> (Join-Path $run "$mode.log")
    @{ mode = $mode; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run "$mode.json") }
}
$unchanged = (Get-FileHash -LiteralPath (Join-Path $run 'TimerDeliveryChecks') -Algorithm SHA256).Hash -eq $binaryHash
foreach ($name in $hashes.Keys) {
    $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash -eq $hashes[$name]
}
$passed = $unchanged -and @($outcomes | Where-Object { $_.exitCode -ne 0 -or -not $_.recordExists }).Count -eq 0
@{ passed = $passed; inputsUnchanged = $unchanged; outcomes = @($outcomes); generalClrSafetyEstablished = $false;
    predictionAccuracyEstablished = $false } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'summary.json')
Write-Output $run
if (-not $passed) { throw 'The target-local timer fixture failed. Retain this run.' }
