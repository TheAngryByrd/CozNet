param([string]$EvidenceDirectory = '',
    [string]$PythonCommand = $(if ($env:COZNET_PYTHON) { $env:COZNET_PYTHON } elseif ($IsWindows) { 'py' } else { 'python3' }),
    [string]$PackageDirectory = $(if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }))
$ErrorActionPreference = 'Stop'
if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $PSScriptRoot '../artifacts/target-local-discovery' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$app = Join-Path $run 'app'
$tests = Join-Path $run 'linux-tests'
New-Item -ItemType Directory -Force $app, $tests | Out-Null
$compilerImage = & docker image inspect coznet-native-build:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The native compiler image is unavailable.' }
$runtimeImage = & docker image inspect mcr.microsoft.com/dotnet/sdk:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The Linux SDK image is unavailable.' }
$sources = @('native/service/TargetLocalApi.h', 'native/service/TargetLocalApi.Linux.cpp', 'native/service/TargetSampleSession.Linux.h',
    'native/service/TargetSampleDispatch.h', 'native/service/TargetDelayLedger.h', 'native/service/PerfRing.Linux.h', 'native/service/PerfSampleRing.h',
    'src/CozNet/TargetLocalReplay.fs', 'src/CozNet/TargetLocalLinux.fs', 'src/CozNet/TargetLocalProfiler.fs', 'src/CozNet/TargetCausalProfile.fs', 'src/CozNet/TargetDiscovery.fs',
    'src/CozNet/CozNet.fsproj', 'samples/CozNet.TargetLocal/Work.fs', 'samples/CozNet.TargetLocal/DiscoveryCheck.fs', 'samples/CozNet.TargetLocal/Program.fs',
    'samples/CozNet.TargetLocal/CozNet.TargetLocal.fsproj', 'tests/CozNet.Tests/TargetDiscoveryTests.fs', 'tests/CozNet.DiscoveryFixture/Embedded.fs',
    'native/VerifyTargetLocalDiscovery.ps1', 'native/AnalyzeTargetDiscovery.py', 'lode/plans/target-local-discovery.md', 'lode/plans/target-local-discovery-check.md', 'lode/plans/target-local-discovery-workflow.md', 'build/build.fs',
    'global.json', 'NuGet.config', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'src/Directory.Build.props', 'src/Directory.Build.targets',
    'src/CozNet/packages.lock.json', 'samples/CozNet.TargetLocal/packages.lock.json', 'tests/Directory.Build.props', 'tests/CozNet.Tests/CozNet.Tests.fsproj', 'tests/CozNet.Tests/packages.lock.json',
    'tests/CozNet.DiscoveryFixture/CozNet.DiscoveryFixture.fsproj', 'tests/CozNet.DiscoveryFixture/packages.lock.json')
$sources += @(Get-ChildItem -LiteralPath (Join-Path $root 'src/CozNet') -Filter '*.fs' | ForEach-Object { "src/CozNet/$($_.Name)" })
$sources += @(Get-ChildItem -LiteralPath (Join-Path $root 'tests/CozNet.Tests') -Filter '*.fs' | ForEach-Object { "tests/CozNet.Tests/$($_.Name)" })
$sources = @($sources | Sort-Object -Unique)
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
$commit = (& git -C $root rev-parse HEAD).Trim()
$status = @(& git -C $root status --porcelain)
& dotnet build (Join-Path $root 'samples/CozNet.TargetLocal/CozNet.TargetLocal.fsproj') -c Release *> (Join-Path $run 'managed-build.log')
if ($LASTEXITCODE -ne 0) { throw "The managed build failed. See $run/managed-build.log." }
Copy-Item -Recurse (Join-Path $root 'samples/CozNet.TargetLocal/bin/Release/net10.0/*') $app
& docker run --rm --pull never --network none --cap-drop ALL --read-only --tmpfs /tmp:rw,nosuid,size=64m `
    --mount "type=bind,source=$root,target=/source,readonly" --mount "type=bind,source=$app,target=/build" $compilerImage `
    sh -c 'cd /source/native/service && clang++ -std=c++20 -O2 -Wall -Wextra -Werror -fPIC -shared -fvisibility=hidden -pthread TargetLocalApi.Linux.cpp -o /build/libCozNet.TargetLocal.so' *> (Join-Path $run 'native-build.log')
if ($LASTEXITCODE -ne 0) { throw "The native build failed. See $run/native-build.log." }
$binaries = @{}
foreach ($name in @('libCozNet.TargetLocal.so', 'CozNet.TargetLocal.dll', 'CozNet.TargetLocal.pdb', 'CozNet.dll', 'CozNet.pdb')) { $binaries[$name] = (Get-FileHash -LiteralPath (Join-Path $app $name) -Algorithm SHA256).Hash }

$testCommand = 'dotnet test tests/CozNet.Tests/CozNet.Tests.fsproj -c Release -p:NuGetAudit=false --filter "FullyQualifiedName~target discovery" --logger "trx;LogFileName=linux-discovery.trx" --results-directory /work/results'
$script = "set -e`nmkdir -p /work/src /tmp/home`ncd /source`ntar --exclude=bin --exclude=obj -cf - global.json NuGet.config Directory.Build.props Directory.Build.targets Directory.Packages.props README.md LICENSE.md src tests/Directory.Build.props tests/CozNet.Tests tests/CozNet.DiscoveryFixture | tar -xf - -C /work/src`ncd /work/src`n$testCommand`n"
[IO.File]::WriteAllText((Join-Path $tests 'run.sh'), $script)
& docker run --rm --pull never --network none --cap-drop ALL --read-only --tmpfs /tmp:rw,nosuid,exec,size=512m `
    --env HOME=/tmp/home --env DOTNET_CLI_HOME=/tmp/home --env NUGET_PACKAGES=/nuget --env DOTNET_CLI_TELEMETRY_OPTOUT=1 --env DOTNET_NOLOGO=1 --env DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 `
    --mount "type=bind,source=$root,target=/source,readonly" --mount "type=bind,source=$PackageDirectory,target=/nuget,readonly" --mount "type=bind,source=$tests,target=/work" `
    $runtimeImage sh /work/run.sh *> (Join-Path $run 'linux-tests.log')
$testExit = $LASTEXITCODE
$counters = $null
$trx = Join-Path $tests 'results/linux-discovery.trx'
if (Test-Path -LiteralPath $trx) {
    try { $counters = ([xml](Get-Content -Raw -LiteralPath $trx)).TestRun.ResultSummary.Counters } catch { $counters = $null }
}
$linuxTests = @{ command = $testCommand; exitCode = $testExit
    total = $(if ($counters) { [int]$counters.total } else { $null }); executed = $(if ($counters) { [int]$counters.executed } else { $null })
    passed = $(if ($counters) { [int]$counters.passed } else { $null }); failed = $(if ($counters) { [int]$counters.failed } else { $null }); notExecuted = $(if ($counters) { [int]$counters.notExecuted } else { $null }) }
Remove-Item -Recurse -Force (Join-Path $tests 'src') -ErrorAction SilentlyContinue

$capture = $null
$stopped = $null
if ($testExit -ne 0 -or -not $counters -or [int]$counters.executed -lt 1 -or [int]$counters.failed -ne 0) {
    $stopped = 'The Linux discovery tests did not pass with an executed count above 0, so the capture did not run.'
} else {
    $container = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only', '--security-opt', 'seccomp=unconfined',
        '--tmpfs', '/tmp:rw,nosuid,size=64m', '--env', 'DOTNET_TieredCompilation=0', '--env', 'DOTNET_ReadyToRun=0',
        '--env', 'COZNET_WORKER_CPUS=', '--env', 'COZNET_SAMPLE_PERIOD_NS=', '--env', 'DOTNET_PerfMapEnabled=3', '--env', 'DOTNET_PerfMapShowOptimizationTiers=1', '--env', 'DOTNET_CLI_TELEMETRY_OPTOUT=1',
        '--mount', "type=bind,source=$app,target=/app,readonly", '--mount', "type=bind,source=$run,target=/out", $runtimeImage)
    & docker @container sh -c 'uname -a > /out/kernel.txt && nproc > /out/cpus.txt'
    $captureCommand = 'cd /app && timeout -k 2s 120s dotnet CozNet.TargetLocal.dll discovery-check /out/discovery.json'
    & docker @container sh -c $captureCommand *> (Join-Path $run 'discovery.json.log')
    $capture = @{ command = $captureCommand; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run 'discovery.json') }
}

$unchanged = $true
foreach ($name in $sources) { $unchanged = $unchanged -and (Get-SourceHash (Join-Path $root $name)) -eq $hashes[$name] }
foreach ($name in $binaries.Keys) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $app $name) -Algorithm SHA256).Hash -eq $binaries[$name] }
@{ suite = 'discovery'; diagnostic = $true; sourceHash = 'sha256 after CRLF to LF'; sources = $hashes; binaries = $binaries; compilerImage = $compilerImage; runtimeImage = $runtimeImage
    commit = $commit; gitStatus = $status; treeClean = ($status.Count -eq 0); linuxTests = $linuxTests; capture = $capture; inputsUnchanged = $unchanged; stopped = $stopped } |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $run 'manifest.json')
& $PythonCommand -I (Join-Path $PSScriptRoot 'AnalyzeTargetDiscovery.py') $run
$analysisExit = $LASTEXITCODE
Write-Output $run
if ($stopped) { throw "The target-local discovery check stopped: $stopped Retain this run." }
if (-not $unchanged -or $analysisExit -ne 0) { throw "The target-local discovery check did not pass. Retain this run." }
