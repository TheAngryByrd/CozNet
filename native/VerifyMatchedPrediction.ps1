param([Parameter(Mandatory)][string]$CalibrationDirectory, [string]$EvidenceDirectory, [string]$PythonCommand = 'python')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $root 'artifacts/matched-prediction' }
$calibration = (Resolve-Path -LiteralPath $CalibrationDirectory).Path
$calibrationNames = @('calibration.json', 'calibration.json.plan.json', 'calibration.json.blocks.jsonl', 'manifest.json')
foreach ($name in $calibrationNames) {
    if (-not (Test-Path -LiteralPath (Join-Path $calibration $name) -PathType Leaf)) { throw "Missing calibration evidence: $name" }
}
& $PythonCommand (Join-Path $PSScriptRoot 'AnalyzeMatchedPrediction.py') --validate-calibration $calibration
if ($LASTEXITCODE -ne 0) { throw 'Select a complete, internally consistent interleaved calibration before collecting matched cases.' }
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $run | Out-Null
function Save-Json($Name, $Value) {
    ConvertTo-Json -InputObject $Value -Depth 12 | Set-Content -LiteralPath (Join-Path $run $Name) -Encoding utf8
}
$random = [Random]::new(20261014)
$order = @(for ($block = 1; $block -le 8; $block++) {
    $modes = @('profile-zero', 'profile-half', 'physical-baseline', 'physical-optimized')
    for ($index = $modes.Length - 1; $index -gt 0; $index--) {
        $other = $random.Next($index + 1)
        $modes[$index], $modes[$other] = $modes[$other], $modes[$index]
    }
    foreach ($mode in $modes) { @{ block = $block; mode = $mode; name = "block-$block-$mode" } }
})
Save-Json 'plan.json' @{ protocol = 'matched-timer-diagnostic-v1'; seed = 20261014; order = $order;
    orderGenerator = 'Fisher-Yates with System.Random.Next'; planRuntime = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription;
    measurementMilliseconds = 2000; watchdogSeconds = 30; initialIntervalNanoseconds = 10000;
    requestedChildBoundNanoseconds = 50000; handlerBoundNanoseconds = 5000000; receiptBoundNanoseconds = 100000000;
    debtBoundNanoseconds = 2000000; leaseBoundNanoseconds = 2000000; gcCallBoundMilliseconds = 250;
    predictionErrorBound = 0.02; calibrationSource = [IO.Path]::GetFileName($calibration) }
$outcomes = @($order | ForEach-Object { @{ name = $_.name; executed = $false; recordExists = $false; exitCode = $null } })
Save-Json 'outcomes.json' $outcomes
$failures = @()
$files = @{}
function Capture-File($Source, $Relative) {
    $destination = Join-Path $run $Relative
    New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($destination)) | Out-Null
    Copy-Item -LiteralPath $Source -Destination $destination
    $files[$Relative] = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
}
try {
    foreach ($name in $calibrationNames) { Capture-File (Join-Path $calibration $name) "calibration/$name" }
    foreach ($directory in @('native/service', 'native/signal', 'tests/CozNet.LinuxPerf', 'tests/Shared', 'src/CozNet')) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root $directory) -File) {
            Capture-File $file.FullName "source/$directory/$($file.Name)"
        }
    }
    foreach ($name in @('native/VerifyMatchedPrediction.ps1', 'native/AnalyzeMatchedPrediction.py', 'native/MatchedPredictionChecks.py',
            'native/AnalyzeCpuCounters.py', 'native/CpuCounterChecks.py',
            'build/build.fs', 'global.json')) { Capture-File (Join-Path $root $name) "source/$name" }
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0') -File) {
        Capture-File $file.FullName "workload/$($file.Name)"
    }
    $files['plan.json'] = (Get-FileHash -LiteralPath (Join-Path $run 'plan.json') -Algorithm SHA256).Hash
    $compilerImage = & docker image inspect coznet-native-build:10.0.100 --format '{{.Id}}'
    if ($LASTEXITCODE -ne 0) { throw 'The native compiler image is unavailable.' }
    $runtimeImage = & docker image inspect mcr.microsoft.com/dotnet/sdk:10.0.100 --format '{{.Id}}'
    if ($LASTEXITCODE -ne 0) { throw 'The Linux SDK image is unavailable.' }
    $source = Join-Path $run 'source'
    $native = Join-Path $run 'native'
    New-Item -ItemType Directory -Path $native | Out-Null
    $compiler = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only', '--tmpfs', '/tmp:rw,nosuid,size=64m',
        '--mount', "type=bind,source=$source,target=/source,readonly", '--mount', "type=bind,source=$native,target=/build", $compilerImage)
    & docker @compiler uname -a *> (Join-Path $run 'kernel.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Kernel identity capture failed.' }
    & docker @compiler cmake -S /source/native/service -B /build -G Ninja -DCMAKE_CXX_COMPILER=clang++ -DCMAKE_BUILD_TYPE=Release -DCOZ_SERVICE_LINUX_SIGNAL=ON *> (Join-Path $run 'configure.log')
    if ($LASTEXITCODE -ne 0) { throw 'Native configuration failed.' }
    & docker @compiler cmake --build /build --target CozNet.Timer *> (Join-Path $run 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Native compilation failed.' }
    $files['native/libCozNet.Timer.so'] = (Get-FileHash -LiteralPath (Join-Path $native 'libCozNet.Timer.so') -Algorithm SHA256).Hash
    Save-Json 'manifest.json' @{ files = $files; capturedBeforeExecution = [DateTime]::UtcNow.ToString('O');
        compilerImage = $compilerImage; runtimeImage = $runtimeImage; tieredCompilation = '0'; readyToRun = '0';
        startupProfiler = '0'; sourceSnapshotIsStandaloneCheckout = $false }
    $workload = Join-Path $run 'workload'
    $runtime = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only', '--tmpfs', '/tmp:rw,nosuid,size=64m',
        '--security-opt', 'seccomp=unconfined', '--env', 'LD_LIBRARY_PATH=/native', '--env', 'DOTNET_TieredCompilation=0',
        '--env', 'DOTNET_ReadyToRun=0', '--env', 'CORECLR_ENABLE_PROFILING=0', '--env', 'DOTNET_PerfMapEnabled=3',
        '--env', 'DOTNET_PerfMapShowOptimizationTiers=1', '--mount', "type=bind,source=$native,target=/native,readonly",
        '--mount', "type=bind,source=$workload,target=/work,readonly", '--mount', "type=bind,source=$run,target=/output",
        $runtimeImage, 'timeout', '-k', '2s', '30s', 'dotnet', '/work/CozNet.LinuxPerf.dll')
    & $PythonCommand (Join-Path $source 'native/CpuCounterChecks.py') *> (Join-Path $run 'counter-checks.log')
    if ($LASTEXITCODE -ne 0) { throw 'Portable counter checks failed.' }
    & $PythonCommand (Join-Path $source 'native/MatchedPredictionChecks.py') *> (Join-Path $run 'analyzer-checks.log')
    if ($LASTEXITCODE -ne 0) { throw 'Portable analyzer checks failed.' }
    & docker @runtime --verify-timer-layout *> (Join-Path $run 'layout.log')
    if ($LASTEXITCODE -ne 0) { throw 'Linux layout checks failed.' }
    for ($index = 0; $index -lt $order.Length; $index++) {
        $case = $order[$index]
        $output = "/output/$($case.name).json"
        $arguments = if ($case.mode.StartsWith('physical-')) {
            @('--timer-physical', $output, $case.mode.Substring(9))
        } else {
            @('--sampled-timer-capacity', $output, $(if ($case.mode -eq 'profile-zero') { '0' } else { '50' }), '10us')
        }
        & docker @runtime @arguments *> (Join-Path $run "$($case.name).log")
        $outcomes[$index] = @{ name = $case.name; executed = $true; arguments = $arguments; exitCode = $LASTEXITCODE;
            recordExists = Test-Path -LiteralPath (Join-Path $run "$($case.name).json") }
        Save-Json 'outcomes.json' $outcomes
    }
} catch {
    $failures += $_.ToString()
} finally {
    Save-Json 'runner-errors.json' @($failures)
    Save-Json 'outcomes.json' $outcomes
}
$analyzer = Join-Path $run 'source/native/AnalyzeMatchedPrediction.py'
if (Test-Path -LiteralPath $analyzer) {
    & $PythonCommand $analyzer $run *> (Join-Path $run 'analysis.log')
    $analysisExit = $LASTEXITCODE
} else { $analysisExit = -1 }
Write-Output $run
if ($failures.Count -gt 0 -or $analysisExit -ne 0) { throw 'Matched prediction is not established. Retain all diagnostic evidence.' }
