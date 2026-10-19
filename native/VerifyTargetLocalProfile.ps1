param([ValidateSet('latency', 'throughput', 'pipeline', 'lock', 'spinlock', 'semaphore', 'transaction', 'spintransaction')][string]$Workload = 'latency', [string]$EvidenceDirectory = '', [int]$Blocks = 5, [int]$Seed = 20261018, [string]$WorkerCpus = '', [uint64]$PeriodNs = 0,
    [string]$PythonCommand = $(if ($env:COZNET_PYTHON) { $env:COZNET_PYTHON } elseif ($IsWindows) { 'py' } else { 'python3' }))
$ErrorActionPreference = 'Stop'
if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $PSScriptRoot $(switch ($Workload) { 'throughput' { '../artifacts/target-local-throughput' } 'pipeline' { '../artifacts/target-local-pipeline' } 'lock' { '../artifacts/target-local-lock' } 'spinlock' { '../artifacts/target-local-spinlock' } 'semaphore' { '../artifacts/target-local-semaphore' } 'transaction' { '../artifacts/target-local-transaction' } 'spintransaction' { '../artifacts/target-local-spintransaction' } default { '../artifacts/target-local-profile' } }) }
if ($WorkerCpus) {
    if ($Workload -notin @('lock', 'spinlock', 'transaction', 'spintransaction')) { throw 'Worker pinning supports only the lock and transaction workloads.' }
    if ($WorkerCpus -ne '0,2') { throw 'Worker pinning requires -WorkerCpus ''0,2'', one CPU on each physical core.' }
    $EvidenceDirectory = $EvidenceDirectory.TrimEnd([char[]]'/\') + '-pinned'
}
if ($PeriodNs) {
    if ($Workload -notin @('transaction', 'spintransaction')) { throw 'The diagnostic sample period supports only the transaction workloads.' }
    if ($PeriodNs -ne 250000) { throw 'The diagnostic sample period requires -PeriodNs 250000.' }
    $EvidenceDirectory = $EvidenceDirectory.TrimEnd([char[]]'/\') + '-period'
}
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$app = Join-Path $run 'app'
New-Item -ItemType Directory -Force $app | Out-Null
$compilerImage = & docker image inspect coznet-native-build:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The native compiler image is unavailable.' }
$runtimeImage = & docker image inspect mcr.microsoft.com/dotnet/sdk:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The Linux SDK image is unavailable.' }
$sources = @('native/service/TargetLocalApi.h', 'native/service/TargetLocalApi.Linux.cpp', 'native/service/TargetSampleSession.Linux.h',
    'native/service/TargetSampleDispatch.h', 'native/service/TargetDelayLedger.h', 'native/service/PerfRing.Linux.h', 'native/service/PerfSampleRing.h',
    'src/CozNet/TargetLocalReplay.fs', 'src/CozNet/TargetLocalLinux.fs', 'src/CozNet/TargetLocalProfiler.fs', 'src/CozNet/TargetCausalProfile.fs', 'samples/CozNet.TargetLocal/Work.fs',
    'samples/CozNet.TargetLocal/Program.fs', 'samples/CozNet.TargetLocal/CozNet.TargetLocal.fsproj',
    'native/VerifyTargetLocalProfile.ps1', 'native/AnalyzeTargetProfile.py', 'lode/plans/target-local-causal-profile.md', 'lode/plans/target-local-throughput.md', 'lode/plans/target-local-queue.md', 'lode/plans/target-local-experiment-diagnostics.md', 'lode/plans/target-local-warmup.md', 'lode/plans/target-local-rotation.md', 'lode/plans/target-local-lock.md', 'lode/plans/target-local-lock-handoff.md', 'lode/plans/target-local-spin-lock.md', 'lode/plans/target-local-semaphore.md', 'lode/plans/target-local-failure-records.md', 'lode/plans/target-local-failure-context.md', 'lode/plans/target-local-transactions.md', 'lode/plans/target-local-transaction-delay.md', 'lode/plans/target-local-transaction-handoff.md', 'lode/plans/target-local-spin-queue.md', 'lode/plans/target-local-handoff-parts.md', 'lode/plans/target-local-worker-pinning.md', 'lode/plans/target-local-plain-warmup.md', 'lode/plans/target-local-progress-settlement.md', 'lode/plans/target-local-sample-period.md', 'lode/plans/target-local-handoff-placement.md', 'lode/plans/target-local-wake-idle.md', 'build/build.fs')
$hashes = @{}
function Get-SourceHash([string] $Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $normalized = [Collections.Generic.List[byte]]::new($bytes.Length)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if (-not ($bytes[$i] -eq 13 -and $i + 1 -lt $bytes.Length -and $bytes[$i + 1] -eq 10)) { $normalized.Add($bytes[$i]) }
    }
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($normalized.ToArray()))
}
foreach ($name in $sources) { $hashes[$name] = Get-SourceHash (Join-Path $root $name) }
& dotnet build (Join-Path $root 'samples/CozNet.TargetLocal/CozNet.TargetLocal.fsproj') -c Release *> (Join-Path $run 'managed-build.log')
if ($LASTEXITCODE -ne 0) { throw "The managed build failed. See $run/managed-build.log." }
Copy-Item -Recurse (Join-Path $root 'samples/CozNet.TargetLocal/bin/Release/net10.0/*') $app
& docker run --rm --pull never --network none --cap-drop ALL --read-only --tmpfs /tmp:rw,nosuid,size=64m `
    --mount "type=bind,source=$root,target=/source,readonly" --mount "type=bind,source=$app,target=/build" $compilerImage `
    sh -c 'cd /source/native/service && clang++ -std=c++20 -O2 -Wall -Wextra -Werror -fPIC -shared -fvisibility=hidden -pthread TargetLocalApi.Linux.cpp -o /build/libCozNet.TargetLocal.so' *> (Join-Path $run 'native-build.log')
if ($LASTEXITCODE -ne 0) { throw "The native build failed. See $run/native-build.log." }
$binaries = @{}
foreach ($name in @('libCozNet.TargetLocal.so', 'CozNet.TargetLocal.dll', 'CozNet.dll')) { $binaries[$name] = (Get-FileHash -LiteralPath (Join-Path $app $name) -Algorithm SHA256).Hash }
$container = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only', '--security-opt', 'seccomp=unconfined',
    '--tmpfs', '/tmp:rw,nosuid,size=64m', '--env', 'DOTNET_TieredCompilation=0', '--env', 'DOTNET_ReadyToRun=0',
    '--env', "COZNET_WORKER_CPUS=$WorkerCpus", '--env', "COZNET_SAMPLE_PERIOD_NS=$(if ($PeriodNs) { $PeriodNs } else { '' })", '--env', 'DOTNET_PerfMapEnabled=3', '--env', 'DOTNET_PerfMapShowOptimizationTiers=1', '--env', 'DOTNET_CLI_TELEMETRY_OPTOUT=1',
    '--mount', "type=bind,source=$app,target=/app,readonly", '--mount', "type=bind,source=$run,target=/out", $runtimeImage)
& docker @container sh -c 'uname -a > /out/kernel.txt && nproc > /out/cpus.txt'
$selected = switch ($Workload) { 'latency' { 6000 } 'semaphore' { 24 } default { 40 } }
$peer = switch ($Workload) { 'latency' { 3000 } 'semaphore' { 12 } 'transaction' { 8 } 'spintransaction' { 8 } default { 20 } }
$profileMode = switch ($Workload) { 'throughput' { 'throughput-profile' } 'pipeline' { 'pipeline-profile' } 'lock' { 'lock-profile' } 'spinlock' { 'spin-lock-profile' } 'semaphore' { 'semaphore-profile' } 'transaction' { 'transaction-profile' } 'spintransaction' { 'spin-transaction-profile' } default { 'causal-profile' } }
$plainMode = switch ($Workload) { 'throughput' { 'plain-throughput' } 'pipeline' { 'plain-pipeline' } 'lock' { 'plain-lock' } 'spinlock' { 'plain-spin-lock' } 'semaphore' { 'plain-semaphore' } 'transaction' { 'plain-transaction' } 'spintransaction' { 'plain-spin-transaction' } default { 'plain' } }
$speedups = @(25, 50, 75)
$random = [Random]::new($Seed)
$runs = @()
for ($block = 1; $block -le $Blocks; $block++) {
    $arms = @(@{ arm = 'profile'; args = "$profileMode $Seed $($block - 1)" }, @{ arm = 'original'; args = "$plainMode $selected $peer" })
    foreach ($speedup in $speedups) {
        $arms += @{ arm = "selected-$speedup"; args = "$plainMode $([int]($selected * (100 - $speedup) / 100)) $peer" }
        $arms += @{ arm = "peer-$speedup"; args = "$plainMode $selected $([int]($peer * (100 - $speedup) / 100))" }
    }
    foreach ($arm in ($arms | Sort-Object { $random.Next() })) {
        $file = "$block-$($arm.arm).json"
        & docker @container sh -c "cd /app && timeout -k 2s 120s dotnet CozNet.TargetLocal.dll $($arm.args) /out/$file" *> (Join-Path $run "$file.log")
        $runs += @{ block = $block; arm = $arm.arm; args = $arm.args; file = $file; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run $file) }
    }
}
$unchanged = $true
foreach ($name in $sources) { $unchanged = $unchanged -and (Get-SourceHash (Join-Path $root $name)) -eq $hashes[$name] }
foreach ($name in $binaries.Keys) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $app $name) -Algorithm SHA256).Hash -eq $binaries[$name] }
@{ suite = 'profile'; workload = $Workload; sourceHash = 'sha256 after CRLF to LF'; sources = $hashes; binaries = $binaries; compilerImage = $compilerImage; runtimeImage = $runtimeImage
    seed = $Seed; blocks = $Blocks; selected = $selected; peer = $peer; speedups = $speedups; runs = $runs; inputsUnchanged = $unchanged
    workerCpus = $(if ($WorkerCpus) { @($WorkerCpus.Split(',') | ForEach-Object { [int]$_ }) } else { $null }); periodNs = $(if ($PeriodNs) { $PeriodNs } else { $null }) } |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $run 'manifest.json')
& $PythonCommand -I (Join-Path $PSScriptRoot 'AnalyzeTargetProfile.py') $run
$analysisExit = $LASTEXITCODE
Write-Output $run
if (-not $unchanged -or $analysisExit -ne 0) { throw "Target-local $Workload profile qualification failed. Retain this run." }
