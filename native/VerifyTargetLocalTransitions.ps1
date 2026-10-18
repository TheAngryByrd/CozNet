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
    'samples/CozNet.TargetLocal/Native.fs', 'samples/CozNet.TargetLocal/Work.fs', 'samples/CozNet.TargetLocal/Program.fs',
    'native/VerifyTargetLocalTransitions.ps1', 'lode/plans/target-local-transitions.md')
$hashes = @{}
foreach ($name in $sources) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash }
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
$unchanged = $true
foreach ($name in $sources) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash -eq $hashes[$name] }
$passed = $unchanged -and $Runs -eq 10 -and @($outcomes).Count -eq 10 -and @($outcomes | Where-Object {
    $_.exitCode -ne 0 -or -not $_.recordExists -or -not $_.passed -or $_.forcedCollections -lt 10 -or $_.maxHandlerNs -ge 5000000 }).Count -eq 0
@{ passed = $passed; inputsUnchanged = $unchanged; sources = $hashes; runs = $Runs; outcomes = $outcomes
    compilerImage = $compilerImage; runtimeImage = $runtimeImage; generalClrSafetyEstablished = $false } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'summary.json')
Write-Output $run
if (-not $passed) { throw 'Target-local transition checks failed. Retain this run.' }
