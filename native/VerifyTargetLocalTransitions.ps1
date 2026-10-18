param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/target-local-transitions'), [int]$Runs = 10)
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
    'native/service/TargetSampleDispatch.h', 'native/service/TargetDelayLedger.h', 'src/CozNet/TargetLocalReplay.fs',
    'src/CozNet/TargetLocalProfiler.fs', 'samples/CozNet.TargetLocal/Work.fs', 'samples/CozNet.TargetLocal/Program.fs',
    'native/VerifyTargetLocalTransitions.ps1', 'lode/plans/target-local-transitions.md', 'lode/plans/target-local-lifecycle.md', 'lode/plans/target-local-wait-delay.md')
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
$container = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only', '--security-opt', 'seccomp=unconfined',
    '--tmpfs', '/tmp:rw,nosuid,size=64m', '--env', 'DOTNET_TieredCompilation=0', '--env', 'DOTNET_ReadyToRun=0',
    '--env', 'DOTNET_PerfMapEnabled=3', '--env', 'DOTNET_PerfMapShowOptimizationTiers=1', '--env', 'DOTNET_CLI_TELEMETRY_OPTOUT=1',
    '--mount', "type=bind,source=$app,target=/app,readonly", '--mount', "type=bind,source=$run,target=/out", $runtimeImage)
& docker @container sh -c 'uname -a > /out/kernel.txt && nproc > /out/cpus.txt'
$outcomes = @()
for ($index = 1; $index -le $Runs; $index++) {
    $file = "gc-$index.json"
    & docker @container sh -c "cd /app && timeout -k 2s 60s dotnet CozNet.TargetLocal.dll profile-gc 50 6000 3000 5 /out/$file" *> (Join-Path $run "$file.log")
    $exit = $LASTEXITCODE
    $record = if (Test-Path -LiteralPath (Join-Path $run $file)) { Get-Content (Join-Path $run $file) -Raw | ConvertFrom-Json } else { $null }
    $outcomes += [ordered]@{ run = $index; exitCode = $exit; recordExists = $null -ne $record; passed = $record.passed
        gen2Collections = $record.gen2Collections; forcedCollections = $record.forcedCollections
        maxHandlerNs = if ($record) { ($record.threads | Measure-Object -Property maxHandlerNs -Maximum).Maximum } else { $null }
        replayError = $record.replayError }
}
& docker @container sh -c "cd /app && timeout -k 2s 60s dotnet CozNet.TargetLocal.dll cleanup /out/cleanup.json" *> (Join-Path $run 'cleanup.json.log')
$cleanupExit = $LASTEXITCODE
& docker @container sh -c "cd /app && timeout -k 2s 60s dotnet CozNet.TargetLocal.dll barrier-abort /out/barrier-abort.json" *> (Join-Path $run 'barrier-abort.json.log')
$abortExit = $LASTEXITCODE
$abort = if (Test-Path -LiteralPath (Join-Path $run 'barrier-abort.json')) { Get-Content (Join-Path $run 'barrier-abort.json') -Raw | ConvertFrom-Json } else { $null }
$cleanup = if (Test-Path -LiteralPath (Join-Path $run 'cleanup.json')) { Get-Content (Join-Path $run 'cleanup.json') -Raw | ConvertFrom-Json } else { $null }
$tiered = $container | ForEach-Object { if ($_ -eq 'DOTNET_TieredCompilation=0') { 'DOTNET_TieredCompilation=1' } else { $_ } }
& docker @tiered sh -c "cd /app && timeout -k 2s 60s dotnet CozNet.TargetLocal.dll binding /out/binding-tiered.json" *> (Join-Path $run 'binding-tiered.json.log')
$tieredExit = $LASTEXITCODE
& docker @container sh -c "cd /app && timeout -k 2s 60s dotnet CozNet.TargetLocal.dll binding /out/binding-default.json" *> (Join-Path $run 'binding-default.json.log')
$defaultExit = $LASTEXITCODE
$shutdownWatch = [Diagnostics.Stopwatch]::StartNew()
& docker @container sh -c "cd /app && timeout -k 2s 30s dotnet CozNet.TargetLocal.dll shutdown /out/shutdown.json" *> (Join-Path $run 'shutdown.log')
$shutdownExit = $LASTEXITCODE
$shutdownSeconds = $shutdownWatch.Elapsed.TotalSeconds
$read = { param($name) $path = Join-Path $run $name; if (Test-Path -LiteralPath $path) { Get-Content $path -Raw | ConvertFrom-Json } else { $null } }
$bindingTiered = & $read 'binding-tiered.json'
$bindingDefault = & $read 'binding-default.json'
$lifecycle = [ordered]@{
    bindingTieredPassed = $tieredExit -eq 0 -and $null -ne $bindingTiered -and $bindingTiered.passed -and $bindingTiered.tiered
    bindingDefaultPassed = $defaultExit -eq 0 -and $null -ne $bindingDefault -and $bindingDefault.passed -and -not $bindingDefault.tiered
    shutdownPassed = $shutdownExit -eq 0 -and $shutdownSeconds -lt 10 -and (Test-Path -LiteralPath (Join-Path $run 'shutdown.json.marker')) -and -not (Test-Path -LiteralPath (Join-Path $run 'shutdown.json')) -and (Get-Content (Join-Path $run 'shutdown.json.marker') -Raw | ConvertFrom-Json).working
    shutdownExitCode = $shutdownExit; shutdownSeconds = $shutdownSeconds }
$unchanged = $true
foreach ($name in $sources) { $unchanged = $unchanged -and (Get-SourceHash (Join-Path $root $name)) -eq $hashes[$name] }
$passed = $unchanged -and $lifecycle.bindingTieredPassed -and $lifecycle.bindingDefaultPassed -and $lifecycle.shutdownPassed -and $cleanupExit -eq 0 -and $null -ne $cleanup -and $cleanup.passed -and $abortExit -eq 0 -and $null -ne $abort -and $abort.passed -and $Runs -eq 10 -and @($outcomes).Count -eq 10 -and @($outcomes | Where-Object {
    $_.exitCode -ne 0 -or -not $_.recordExists -or -not $_.passed -or $_.forcedCollections -lt 10 -or $_.maxHandlerNs -ge 5000000 }).Count -eq 0
@{ passed = $passed; inputsUnchanged = $unchanged; cleanupExitCode = $cleanupExit; cleanupPassed = [bool]$cleanup.passed; barrierAbortPassed = [bool]$abort.passed; lifecycle = $lifecycle; sourceHash = 'sha256 after CRLF to LF'; sources = $hashes; runs = $Runs; outcomes = $outcomes
    compilerImage = $compilerImage; runtimeImage = $runtimeImage; generalClrSafetyEstablished = $false } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'summary.json')
Write-Output $run
if (-not $passed) { throw 'Target-local transition checks failed. Retain this run.' }
