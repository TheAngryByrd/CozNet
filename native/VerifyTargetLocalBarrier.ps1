param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/target-local-barrier'), [int]$Blocks = 5, [int]$Seed = 20261018, [string]$CpuSet = '',
    [string]$PythonCommand = $(if ($env:COZNET_PYTHON) { $env:COZNET_PYTHON } elseif ($IsWindows) { 'py' } else { 'python3' }))
$ErrorActionPreference = 'Stop'
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
    'src/CozNet/TargetLocalReplay.fs', 'src/CozNet/TargetLocalProfiler.fs', 'samples/CozNet.TargetLocal/Work.fs',
    'samples/CozNet.TargetLocal/Program.fs', 'samples/CozNet.TargetLocal/CozNet.TargetLocal.fsproj',
    'native/VerifyTargetLocalBarrier.ps1', 'native/AnalyzeTargetPrediction.py', 'lode/plans/target-local-prediction.md', 'lode/plans/target-local-wait-delay.md', 'lode/plans/target-local-start-delay.md', 'lode/plans/target-local-start-trace.md', 'lode/plans/target-local-blocking.md', 'build/build.fs')
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
    '--env', 'DOTNET_PerfMapEnabled=3', '--env', 'DOTNET_PerfMapShowOptimizationTiers=1', '--env', 'DOTNET_CLI_TELEMETRY_OPTOUT=1',
    '--mount', "type=bind,source=$app,target=/app,readonly", '--mount', "type=bind,source=$run,target=/out")
if ($CpuSet) { $container += @('--cpuset-cpus', $CpuSet) }
$container += $runtimeImage
& docker @container sh -c 'uname -a > /out/kernel.txt && nproc > /out/cpus.txt'
$configurations = @(
    @{ name = 'barrier-alternating'; pattern = 'alternating' },
    @{ name = 'barrier-selected-dominant'; pattern = 'selected-dominant' },
    @{ name = 'barrier-peer-bound'; pattern = 'peer-bound' })
$speedup = 50
$random = [Random]::new($Seed)
$runs = @()
foreach ($configuration in $configurations) {
    for ($block = 1; $block -le $Blocks; $block++) {
        $arms = @(
            @{ arm = 'profile-zero'; args = "profile-phases 0 $($configuration.pattern)" },
            @{ arm = 'profile-speedup'; args = "profile-phases $speedup $($configuration.pattern)" },
            @{ arm = 'original'; args = "plain-phases $($configuration.pattern) 100" },
            @{ arm = 'optimized'; args = "plain-phases $($configuration.pattern) $(100 - $speedup)" })
        $order = $arms | Sort-Object { $random.Next() }
        foreach ($arm in $order) {
            $file = "$($configuration.name)-$block-$($arm.arm).json"
            & docker @container sh -c "cd /app && timeout -k 2s 60s dotnet CozNet.TargetLocal.dll $($arm.args) /out/$file" *> (Join-Path $run "$file.log")
            $runs += @{ configuration = $configuration.name; block = $block; arm = $arm.arm; args = $arm.args; file = $file; exitCode = $LASTEXITCODE
                recordExists = Test-Path -LiteralPath (Join-Path $run $file) }
        }
    }
}
$unchanged = $true
foreach ($name in $sources) { $unchanged = $unchanged -and (Get-SourceHash (Join-Path $root $name)) -eq $hashes[$name] }
foreach ($name in $binaries.Keys) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $app $name) -Algorithm SHA256).Hash -eq $binaries[$name] }
@{ suite = 'barrier'; cpuSet = $CpuSet; sourceHash = 'sha256 after CRLF to LF'; sources = $hashes; binaries = $binaries; compilerImage = $compilerImage; runtimeImage = $runtimeImage; seed = $Seed; blocks = $Blocks
    speedup = $speedup; configurations = $configurations; runs = $runs; inputsUnchanged = $unchanged } |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $run 'manifest.json')
& $PythonCommand -I (Join-Path $PSScriptRoot 'AnalyzeTargetPrediction.py') $run
$analysisExit = $LASTEXITCODE
Write-Output $run
if (-not $unchanged -or $analysisExit -ne 0) { throw 'Target-local barrier qualification failed. Retain this run.' }
