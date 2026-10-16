param(
    [string]$EvidenceDirectory,
    [ValidateRange(1, 2147483647)][int]$SelectedBatches = 50000,
    [string]$PythonCommand = 'python',
    [ValidateSet(1024, 8192)][int]$ProbeInterval = 1024,
    [switch]$PrepareReceipts,
    [switch]$OwnerController,
    [switch]$NominalSampleCredit
)
$ErrorActionPreference = 'Stop'
$creditPolicy = if ($NominalSampleCredit) { 'nominal-event-v1' } else { 'same-epoch-interval-v1' }
$PSNativeCommandUseErrorActionPreference = $false
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $root 'artifacts/cooperative-comparison' }
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
function Save-Json($Name, $Value) {
    ConvertTo-Json -InputObject $Value -Depth 20 | Set-Content -LiteralPath (Join-Path $run $Name) -Encoding utf8
}
$modes = @('physical-baseline', 'physical-optimized', 'sampled-zero', 'sampled-half')
Save-Json 'plan.json' @{
    protocol = 'cooperative-matched-v1'; order = $modes; selectedBatches = $SelectedBatches
    workloadProtocol = 'cooperative-checksum-v1'; predictionErrorBound = 0.02; localOptimizationCalibrated = $false
    backend = 'LinuxContainer'; watchdogSeconds = 30; killGraceSeconds = 2
    tieredCompilation = '0'; readyToRun = '0'; perfMapEnabled = '0'; probeInterval = $ProbeInterval; probeIntervalEvidenceVersion = 1
    selectedCadence = 'qualified-sample'; ownerDrain = $true; directReceiptReady = $true
    selectedHandoff = $false; finiteMeasurement = $true
    prepareReceipts = [bool]$PrepareReceipts; receiptPreparationEvidenceVersion = 1
    ownerController = [bool]$OwnerController; controllerOwnershipEvidenceVersion = 1
    sampleCreditPolicy = $creditPolicy; sampleCreditEvidenceVersion = 1
    debtBoundNanoseconds = 2000000; leaseBoundNanoseconds = 2000000; requestedChildBoundNanoseconds = 50000
    handlerBoundNanoseconds = 5000000; receiptBoundNanoseconds = 100000000
}
$outcomes = @($modes | ForEach-Object {
    @{ mode = $_; attempted = $false; executed = $false; recordExists = $false; exitCode = $null }
})
Save-Json 'outcomes.json' $outcomes
$failures = [Collections.Generic.List[string]]::new()
$files = @{}
$originals = @{}
$runtimeImage = $null
$compilerImage = $null
function Capture-File($Relative, $Destination) {
    $original = Join-Path $root $Relative
    $hash = (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash
    $copy = Join-Path $run $Destination
    New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($copy)) | Out-Null
    Copy-Item -LiteralPath $original -Destination $copy
    if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $hash) { throw "Input changed during snapshot: $Relative" }
    $originals[$Relative] = $hash
    $files[$Destination] = $hash
}
function Get-ImageIdentity($Name) {
    $identity = & docker image inspect $Name --format '{{.Id}}'
    if ($LASTEXITCODE -ne 0 -or $identity -isnot [string] -or $identity -cnotmatch '^sha256:[0-9a-f]{64}$') {
        throw "Cannot resolve the retained image: $Name"
    }
    return $identity
}
try {
    foreach ($directory in @('native', 'native/service', 'native/signal', 'tests/CozNet.LinuxPerf', 'tests/Shared', 'src/CozNet')) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root $directory) -File) {
            $relative = "$directory/$($file.Name)"
            Capture-File $relative "source/$relative"
        }
    }
    foreach ($relative in @('build/build.fs', 'global.json')) { Capture-File $relative "source/$relative" }
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0') -File) {
        Capture-File "tests/CozNet.LinuxPerf/bin/Release/net10.0/$($file.Name)" "workload/$($file.Name)"
    }
    Capture-File 'artifacts/native-linux/integrated/libCozNet.Profiler.so' 'profiler/libCozNet.Profiler.so'
    $files['plan.json'] = (Get-FileHash -LiteralPath (Join-Path $run 'plan.json') -Algorithm SHA256).Hash
    $runtimeImage = Get-ImageIdentity 'mcr.microsoft.com/dotnet/sdk:10.0.100'
    $compilerImage = Get-ImageIdentity 'coznet-native-build:10.0.100'
    $source = Join-Path $run 'source'
    $native = Join-Path $run 'native'
    New-Item -ItemType Directory -Path $native | Out-Null
    $compiler = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
        '--tmpfs', '/tmp:rw,nosuid,size=64m', '--mount', "type=bind,source=$source,target=/source,readonly",
        '--mount', "type=bind,source=$native,target=/build", $compilerImage)
    & docker @compiler cmake -S /source/native/service -B /build -G Ninja -DCMAKE_CXX_COMPILER=clang++ -DCMAKE_BUILD_TYPE=Release -DCOZ_SERVICE_LINUX_SIGNAL=OFF *> (Join-Path $run 'configure.log')
    if ($LASTEXITCODE -ne 0) { throw 'Cooperative native configuration failed.' }
    & docker @compiler cmake --build /build *> (Join-Path $run 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Cooperative native compilation failed.' }
    & docker @compiler ctest --test-dir /build --output-on-failure *> (Join-Path $run 'native-checks.log')
    if ($LASTEXITCODE -ne 0) { throw 'Cooperative native checks failed.' }
    $files['native/libCozNet.Cooperative.so'] = (Get-FileHash -LiteralPath (Join-Path $native 'libCozNet.Cooperative.so') -Algorithm SHA256).Hash
    Save-Json 'manifest.json' @{
        files = $files; originalInputs = $originals; runtimeImage = $runtimeImage; compilerImage = $compilerImage
        capturedBeforeExecution = [DateTime]::UtcNow.ToString('O'); sourceSnapshotIsStandaloneCheckout = $false
    }
    foreach ($check in @('CooperativeComparisonChecks.py', 'BoundSelectedPaymentAnalysisChecks.py')) {
        & $PythonCommand (Join-Path $source "native/$check") *> (Join-Path $run "$check.log")
        if ($LASTEXITCODE -ne 0) { throw "Retained analyzer check failed: $check" }
    }
    $workload = Join-Path $run 'workload'
    $profiler = Join-Path $run 'profiler'
    $container = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
        '--tmpfs', '/tmp:rw,noexec,nosuid,size=64m', '--security-opt', 'seccomp=unconfined',
        '--env', 'DOTNET_TieredCompilation=0', '--env', 'DOTNET_ReadyToRun=0', '--env', 'DOTNET_PerfMapEnabled=0',
        '--env', 'LD_LIBRARY_PATH=/profiler:/native',
        '--mount', "type=bind,source=$workload,target=/workload,readonly",
        '--mount', "type=bind,source=$profiler,target=/profiler,readonly",
        '--mount', "type=bind,source=$native,target=/native,readonly")
    & docker @container $runtimeImage uname -a *> (Join-Path $run 'kernel.log')
    if ($LASTEXITCODE -ne 0) { throw 'Kernel identity capture failed.' }
    foreach ($check in @('--verify-cooperative-work', '--verify-bound-cooperative-managed', '--verify-measurement-boundaries')) {
        & docker @container --env CORECLR_ENABLE_PROFILING=0 --env DOTNET_ENABLE_PROFILING=0 $runtimeImage timeout -k 2s 30s dotnet /workload/CozNet.LinuxPerf.dll $check *> (Join-Path $run "$check.log")
        if ($LASTEXITCODE -ne 0) { throw "Linux managed check failed: $check" }
    }
    for ($index = 0; $index -lt $modes.Count; $index++) {
        $mode = $modes[$index]
        $arm = Join-Path $run "arms/$mode"
        New-Item -ItemType Directory -Path $arm | Out-Null
        $activation = @('--env', 'CORECLR_ENABLE_PROFILING=0', '--env', 'DOTNET_ENABLE_PROFILING=0')
        if ($mode.StartsWith('physical-')) {
            $arguments = @('--cooperative-physical', '/evidence/result.json', $mode.Substring(9), [string]$SelectedBatches)
        } else {
            $activation = @('--env', 'CORECLR_ENABLE_PROFILING=1', '--env', 'DOTNET_ENABLE_PROFILING=1',
                '--env', 'CORECLR_PROFILER={6E51C528-23F1-49EA-9346-2A7A8D911B61}',
                '--env', 'CORECLR_PROFILER_PATH=/profiler/libCozNet.Profiler.so',
                '--env', 'COZNET_PROBE_TARGET=linux-perf', '--env', 'COZNET_PROBE_IDENTITY=bound',
                '--env', 'COZNET_IL_MAP_MODE=registered', '--env', ('COZNET_PROBE_INTERVAL=' + $ProbeInterval),
                '--env', 'COZNET_OWNER_DRAIN=1', '--env', 'COZNET_DIRECT_RECEIPT_READY=1',
                '--env', 'COZNET_FINITE_MEASUREMENT=1', '--env', 'COZNET_SELECTED_HANDOFF=0',
                '--env', 'COZNET_PAUSE_TIMING=0', '--env', 'COZNET_CHILD_CPU=0', '--env', 'COZNET_BATCH_SERVICE=0',
                '--env', 'COZNET_DISPATCH_TIMING=0', '--env', 'COZNET_BOUND_SCHEDULER=0', '--env', 'COZNET_PEER_PROGRESS=0',
                '--env', ('COZNET_PREPARE_RECEIPTS=' + [int][bool]$PrepareReceipts),
                '--env', ('COZNET_OWNER_CONTROLLER=' + [int][bool]$OwnerController),
                '--env', ('COZNET_SAMPLE_CREDIT=' + $(if ($NominalSampleCredit) { 'nominal-event' } else { 'interval' })))
            $percent = if ($mode -eq 'sampled-zero') { '0' } else { '50' }
            $arguments = @('--sampled-bound-selected-cadence', '/evidence/result.json', $percent,
                '/native/libCozNet.Cooperative.so', 'qualified-sample', [string]$SelectedBatches)
        }
        $outcome = $outcomes[$index]
        $outcome.attempted = $true
        $outcome.arguments = $arguments
        $outcome.activation = $activation
        $outcome.containerArguments = $container
        Save-Json 'outcomes.json' $outcomes
        try {
            & docker @container @activation --mount "type=bind,source=$arm,target=/evidence" $runtimeImage timeout -k 2s 30s dotnet /workload/CozNet.LinuxPerf.dll @arguments *> (Join-Path $arm 'process.log')
            $outcome.exitCode = $LASTEXITCODE
            $outcome.executed = $true
            $result = Join-Path $arm 'result.json'
            if (Test-Path -LiteralPath $result -PathType Leaf) {
                Copy-Item -LiteralPath $result -Destination (Join-Path $run "$mode.json")
                $outcome.recordExists = $true
            }
        } catch {
            $failures.Add("${mode}: $_")
        } finally {
            Save-Json 'outcomes.json' $outcomes
        }
    }
} catch {
    $failures.Add($_.ToString())
} finally {
    $changes = [Collections.Generic.List[string]]::new()
    foreach ($relative in $originals.Keys) {
        try {
            if ((Get-FileHash -LiteralPath (Join-Path $root $relative) -Algorithm SHA256).Hash -ne $originals[$relative]) {
                $changes.Add($relative)
            }
        } catch { $changes.Add($relative) }
    }
    Save-Json 'integrity.json' @{ originalInputsUnchanged = ($changes.Count -eq 0); changedInputs = @($changes.ToArray()) }
    Save-Json 'runner-errors.json' @($failures.ToArray())
    Save-Json 'outcomes.json' $outcomes
}
$analyzer = Join-Path $run 'source/native/AnalyzeCooperativeComparison.py'
try {
    if (-not (Test-Path -LiteralPath $analyzer -PathType Leaf)) { throw 'The retained analyzer is unavailable.' }
    foreach ($relative in $files.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $run $relative) -Algorithm SHA256).Hash -ne $files[$relative]) {
            throw "Retained input changed before analysis: $relative"
        }
    }
    & $PythonCommand $analyzer $run *> (Join-Path $run 'analysis.log')
    if (-not (Test-Path -LiteralPath (Join-Path $run 'analysis.json') -PathType Leaf)) { throw 'The retained analyzer produced no report.' }
} catch {
    $failures.Add($_.ToString())
    Save-Json 'runner-errors.json' @($failures.ToArray())
    Save-Json 'analysis.json' @{ status = 'invalid'; passed = $false; predictionAccuracyEstablished = $false; error = $_.ToString() }
}
Write-Output $run
throw 'Cooperative prediction accuracy remains unqualified. Retain this capture and every failed arm.'
