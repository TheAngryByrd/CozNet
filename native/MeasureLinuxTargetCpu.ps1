param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/target-cpu-clock'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$native = Join-Path $root 'artifacts/native-signal'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$run = Join-Path $evidence ('run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
[IO.File]::WriteAllText((Join-Path $evidence 'last-run.txt'), $run)
$image = & docker image inspect coznet-native-build:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The native build image is unavailable.' }
$kernel = & docker run --rm --network none --cap-drop ALL --read-only $image uname -r
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the kernel.' }
$files = @('native/signal/TargetCpuWorkload.cpp', 'native/signal/Signal.LinuxX64.cpp', 'native/signal/Signal.h',
    'native/signal/CMakeLists.txt', 'native/MeasureLinuxTargetCpu.ps1', 'artifacts/native-signal/TargetCpuWorkload',
    'artifacts/native-signal/libCozNet.Signal.so', 'artifacts/native-signal/handler-disassembly.txt')
$hashes = @{}
foreach ($file in $files) {
    $path = Join-Path $root $file
    $hashes[$file] = (Get-FileHash $path -Algorithm SHA256).Hash
    $destination = Join-Path $run "snapshot/$file"
    New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath $path -Destination $destination
}
$seed = 20261020
$random = [Random]::new($seed)
$cases = @(for ($block = 1; $block -le 3; $block++) {
    $items = @(foreach ($activity in @('busy', 'blocked')) { foreach ($capture in @('baseline', 'remote')) {
        @{ block=$block; activity=$activity; capture=$capture }
    } })
    for ($i = $items.Length - 1; $i -gt 0; $i--) {
        $j = $random.Next($i + 1)
        $items[$i], $items[$j] = $items[$j], $items[$i]
    }
    $items
})
@{ seed=$seed; imageId=$image; kernel=$kernel; sourceHashes=$hashes; order=$cases;
    readings=10000; warmup=1000; watchdogSeconds=15; sendsSignals=$false; usesClr=$false } |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $run 'environment.json')
$outcomes = [Collections.Generic.List[object]]::new()
foreach ($case in $cases) {
    $name = "block-$($case.block)-$($case.activity)-$($case.capture)"
    & docker run --rm --pull never --network none --cap-drop ALL --read-only --env LD_LIBRARY_PATH=/native `
        --mount "type=bind,source=$native,target=/native,readonly" --mount "type=bind,source=$run,target=/output" `
        $image timeout -k 2s 15s /native/TargetCpuWorkload $case.activity $case.capture "/output/$name" *> (Join-Path $run "$name.log")
    $exitCode = $LASTEXITCODE
    $outcomes.Add(@{ name=$name; exitCode=$exitCode; csvExists=(Test-Path (Join-Path $run "$name.csv"));
        summaryExists=(Test-Path (Join-Path $run "$name.json")) })
    $outcomes | ConvertTo-Json | Set-Content (Join-Path $run 'outcomes.json')
    Write-Host "$name exited $exitCode."
}
$unchanged = $true
foreach ($file in $files) {
    if ((Get-FileHash (Join-Path $root $file) -Algorithm SHA256).Hash -ne $hashes[$file]) { $unchanged = $false }
}
$passed = $unchanged -and @($outcomes | Where-Object { $_.exitCode -ne 0 -or -not $_.csvExists -or -not $_.summaryExists }).Count -eq 0
@{ executionPassed=$passed; sourcesAndBinariesUnchanged=$unchanged; clockAccuracyEstablished=$false;
    clockConsistencyAnalyzed=$false; outcomes=$outcomes.ToArray() } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'summary.json')
if (-not $passed) { throw "Target CPU clock measurement failed. Retain all outcomes: $run" }
$run
