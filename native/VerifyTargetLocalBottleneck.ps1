param([string]$EvidenceDirectory = '', [int]$Blocks = 5, [int]$Seed = 20261018, [switch]$Probe,
    [string[]]$CompareRuns = @('artifacts/target-local-bottleneck/20261019T213806586-069c2adf', 'artifacts/target-local-bottleneck/20261019T222832093-272334f5'),
    [string]$PythonCommand = $(if ($env:COZNET_PYTHON) { $env:COZNET_PYTHON } elseif ($IsWindows) { 'py' } else { 'python3' }))
$ErrorActionPreference = 'Stop'
if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $PSScriptRoot $(if ($Probe) { '../artifacts/target-local-bottleneck-probe' } else { '../artifacts/target-local-bottleneck' }) }
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
    'native/VerifyTargetLocalBottleneck.ps1', 'native/AnalyzeTargetBottleneck.py', 'native/AnalyzeTargetProfile.py',
    'lode/plans/target-local-transactions.md', 'lode/plans/target-local-transaction-delay.md', 'lode/plans/target-local-plain-warmup.md',
    'lode/plans/target-local-progress-settlement.md', 'lode/plans/target-local-wake-idle.md', 'lode/plans/target-local-latency-estimator.md', 'lode/plans/target-local-clipping-export.md', 'lode/plans/target-local-probe-diagnostic.md', 'lode/plans/target-local-boundary-export.md', 'native/AnalyzeTargetProbe.py', 'src/CozNet/CozNet.fsproj', 'build/build.fs')
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
    '--env', 'COZNET_WORKER_CPUS=', '--env', 'COZNET_SAMPLE_PERIOD_NS=', '--env', 'DOTNET_PerfMapEnabled=3', '--env', 'DOTNET_PerfMapShowOptimizationTiers=1', '--env', 'DOTNET_CLI_TELEMETRY_OPTOUT=1',
    '--mount', "type=bind,source=$app,target=/app,readonly", '--mount', "type=bind,source=$run,target=/out", $runtimeImage)
& docker @container sh -c 'uname -a > /out/kernel.txt && nproc > /out/cpus.txt'
$selected = 40
$snapshot = 'snap() { echo "utc $(date -u +%s%N)"; echo "uptime $(cat /proc/uptime 2>/dev/null || echo unsupported)"; echo "== stat"; cat /proc/stat 2>/dev/null || echo unsupported; echo "== pressure"; cat /proc/pressure/cpu 2>/dev/null || echo unsupported; echo "== loadavg"; cat /proc/loadavg 2>/dev/null || echo unsupported; }'
$peers = @(8, 2)
$speedups = @(25, 50, 75)
$random = [Random]::new($Seed)
$runs = @()
$stopped = $null
for ($block = 1; $block -le $Blocks -and -not $stopped; $block++) {
    $arms = @()
    foreach ($peer in $peers) {
        $arms += @{ peer = $peer; arm = 'profile'; args = "transaction-selected-profile $peer $Seed $(($block - 1) % 4)" }
        $arms += @{ peer = $peer; arm = 'original'; args = "plain-transaction $selected $peer" }
        foreach ($speedup in $speedups) { $arms += @{ peer = $peer; arm = "selected-$speedup"; args = "plain-transaction $([int]($selected * (100 - $speedup) / 100)) $peer" } }
    }
    foreach ($arm in ($arms | Sort-Object { $random.Next() })) {
        if ($stopped) { break }
        $file = "$block-p$($arm.peer)-$($arm.arm).json"
        $probeExit = $null
        if ($Probe) {
            & docker @container sh -c "$snapshot; snap > /out/$file.probe-before.txt; cd /app && timeout -k 2s 60s dotnet CozNet.TargetLocal.dll probe /out/$file.probe.json; e=`$?; snap > /out/$file.probe-after.txt; exit `$e" *> (Join-Path $run "$file.probe.log")
            $probeExit = $LASTEXITCODE
            if ($probeExit -ne 0) {
                $stopped = "The probe before $file exited with $probeExit, so the capture stopped before that arm."
                break
            }
            & docker @container sh -c "$snapshot; snap > /out/$file.arm-before.txt; cd /app && timeout -k 2s 120s dotnet CozNet.TargetLocal.dll $($arm.args) /out/$file; e=`$?; snap > /out/$file.arm-after.txt; exit `$e" *> (Join-Path $run "$file.log")
        } else {
            & docker @container sh -c "cd /app && timeout -k 2s 120s dotnet CozNet.TargetLocal.dll $($arm.args) /out/$file" *> (Join-Path $run "$file.log")
        }
        $runs += @{ block = $block; peer = $arm.peer; arm = $arm.arm; args = $arm.args; file = $file; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run $file); probeExitCode = $probeExit }
    }
}
$unchanged = $true
foreach ($name in $sources) { $unchanged = $unchanged -and (Get-SourceHash (Join-Path $root $name)) -eq $hashes[$name] }
foreach ($name in $binaries.Keys) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $app $name) -Algorithm SHA256).Hash -eq $binaries[$name] }
@{ suite = 'bottleneck'; diagnostic = $true; sourceHash = 'sha256 after CRLF to LF'; sources = $hashes; binaries = $binaries; compilerImage = $compilerImage; runtimeImage = $runtimeImage
    seed = $Seed; blocks = $Blocks; selected = $selected; peers = $peers; speedups = $speedups; runs = $runs; inputsUnchanged = $unchanged; probe = [bool]$Probe; stopped = $stopped } |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $run 'manifest.json')
& $PythonCommand -I (Join-Path $PSScriptRoot 'AnalyzeTargetBottleneck.py') $run
$analysisExit = $LASTEXITCODE
if ($Probe) {
    & $PythonCommand -I (Join-Path $PSScriptRoot 'AnalyzeTargetProbe.py') $run @($CompareRuns | ForEach-Object { Join-Path $root $_ })
    if ($LASTEXITCODE -ne 0) { $analysisExit = $LASTEXITCODE }
}
Write-Output $run
if ($stopped) { throw "The target-local bottleneck diagnostic stopped: $stopped Retain this run." }
if (-not $unchanged -or $analysisExit -ne 0) { throw "The target-local bottleneck diagnostic is incomplete. Retain this run." }
