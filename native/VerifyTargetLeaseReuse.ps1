param(
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/target-lease-reuse'),
    [ValidateSet('reuse', 'cancel-before-delivery', 'reject-initial-arm', 'cancel-active-handler', 'disarm-failure', 'drain-failure')][string]$Mode = 'reuse'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
$image = & docker image inspect coznet-native-build:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The native compiler image is unavailable.' }
$hashes = @{}
foreach ($name in @('native/service/TimerReuseChecks.Linux.cpp', 'native/service/LeaseMailbox.h', 'native/service/LeaseMailboxChecks.cpp', 'native/service/TargetLease.h', 'native/service/TargetLease.cpp', 'native/service/TargetLeaseChecks.cpp', 'native/service/TimerDeliveryWorkload.LinuxX64.S', 'native/service/CMakeLists.txt', 'native/VerifyTargetLeaseReuse.ps1', 'build/build.fs')) {
    $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash
}
$container = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
    '--tmpfs', '/tmp:rw,nosuid,size=64m', '--mount', "type=bind,source=$root,target=/source,readonly",
    '--mount', "type=bind,source=$run,target=/build", $image)
& docker @container uname -a *> (Join-Path $run 'kernel.txt')
if ($LASTEXITCODE -ne 0) { throw 'Kernel identity capture failed.' }
& docker @container cmake -S /source/native/service -B /build -G Ninja -DCMAKE_CXX_COMPILER=clang++ -DCMAKE_BUILD_TYPE=Release -DCOZ_SERVICE_LINUX_SIGNAL=ON *> (Join-Path $run 'configure.log')
if ($LASTEXITCODE -ne 0) { throw "Timer reuse configuration failed. See $run/configure.log." }
& docker @container cmake --build /build --target TimerReuseChecks TargetLeaseChecks LeaseMailboxChecks *> (Join-Path $run 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Timer reuse build failed. See $run/build.log." }
$binaryHash = (Get-FileHash -LiteralPath (Join-Path $run 'TimerReuseChecks') -Algorithm SHA256).Hash
$generationCount = if ($Mode -in @('disarm-failure', 'drain-failure')) { 1 } else { 16 }
@{ sources = $hashes; binary = $binaryHash; image = $image; generations = $generationCount; budgetNanoseconds = 50000;
    requestBoundNanoseconds = 50000; handlerBoundNanoseconds = 5000000; receiptBoundNanoseconds = 100000000;
    intervalNanoseconds = 1000000; rejectedInitialArmNanoseconds = 1000000000;
    cancellationHandshakeNanoseconds = 1000000; mode = $Mode; clrWorkload = $false
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'manifest.json')
& docker @container ctest --test-dir /build -R '^(target-lease|lease-mailbox)$' --output-on-failure *> (Join-Path $run 'contracts.log')
$contractResult = $LASTEXITCODE
& docker @container timeout -k 2s 10s /build/TimerReuseChecks $Mode /build/reuse.json *> (Join-Path $run 'reuse.log')
$reuseResult = $LASTEXITCODE
$unchanged = (Get-FileHash -LiteralPath (Join-Path $run 'TimerReuseChecks') -Algorithm SHA256).Hash -eq $binaryHash
foreach ($name in $hashes.Keys) {
    $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash -eq $hashes[$name]
}
$passed = $unchanged -and $contractResult -eq 0 -and $reuseResult -eq 0 -and (Test-Path -LiteralPath (Join-Path $run 'reuse.json'))
@{ passed = $passed; inputsUnchanged = $unchanged; contractExitCode = $contractResult; reuseExitCode = $reuseResult;
    generalClrSafetyEstablished = $false; predictionAccuracyEstablished = $false
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'summary.json')
Write-Output $run
if (-not $passed) { throw 'The target-local timer reuse fixture failed. Retain this run.' }
