$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('coz-comparison-runner-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$runnerSource = Join-Path $PSScriptRoot 'VerifyCooperativeComparison.ps1'

function Invoke-Fixture([string]$Scenario, [int]$ProbeInterval = 1024, [bool]$PrepareReceipts = $false, [bool]$OwnerController = $false) {
    $requestedPreparation = $PrepareReceipts
    $requestedOwnership = $OwnerController
    $fixtureRoot = Join-Path $testRoot $Scenario
    foreach ($directory in @('native/service', 'native/signal', 'tests/CozNet.LinuxPerf/bin/Release/net10.0',
        'tests/Shared', 'src/CozNet', 'build', 'artifacts/native-linux/integrated')) {
        New-Item -ItemType Directory -Force (Join-Path $fixtureRoot $directory) | Out-Null
    }
    Copy-Item -LiteralPath $runnerSource -Destination (Join-Path $fixtureRoot 'native/VerifyCooperativeComparison.ps1')
    foreach ($relative in @('native/AnalyzeCooperativeComparison.py', 'native/CooperativeComparisonChecks.py',
        'native/BoundSelectedPaymentAnalysisChecks.py', 'native/service/CMakeLists.txt', 'src/CozNet/fixture.fs',
        'build/build.fs', 'global.json', 'tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll',
        'artifacts/native-linux/integrated/libCozNet.Profiler.so')) {
        Set-Content -LiteralPath (Join-Path $fixtureRoot $relative) -Value 'fixture' -Encoding utf8
    }
    $calls = [Collections.Generic.List[object]]::new()
    $analyzerCalls = [Collections.Generic.List[string]]::new()
    function docker {
        $global:LASTEXITCODE = 0
        $calls.Add(@($args))
        if ($args[0] -eq 'image') { return ('sha256:' + ('a' * 64)) }
        $buildMount = @($args | Where-Object { $_ -like 'type=bind,source=*,target=/build' })
        if ($args -contains '--build') {
            $buildPath = $buildMount[0] -replace '^type=bind,source=', '' -replace ',target=/build$', ''
            Set-Content -LiteralPath (Join-Path $buildPath 'libCozNet.Cooperative.so') -Value 'native fixture' -Encoding utf8
        }
        if ($Scenario -eq 'preflight-failure' -and $args -contains 'ctest') { $global:LASTEXITCODE = 1; return }
        $mounts = @($args | Where-Object { $_ -like 'type=bind,source=*,target=/evidence' })
        if ($mounts.Count -eq 0) { return }
        $armPath = $mounts[0] -replace '^type=bind,source=', '' -replace ',target=/evidence$', ''
        $mode = Split-Path $armPath -Leaf
        $captureRoot = Split-Path (Split-Path $armPath)
        $plan = Get-Content -Raw -LiteralPath (Join-Path $captureRoot 'plan.json') | ConvertFrom-Json
        $manifest = Get-Content -Raw -LiteralPath (Join-Path $captureRoot 'manifest.json') | ConvertFrom-Json
        $before = @(Get-Content -Raw -LiteralPath (Join-Path $captureRoot 'outcomes.json') | ConvertFrom-Json)
        if ($plan.selectedBatches -ne 5 -or $manifest.runtimeImage -ne ('sha256:' + ('a' * 64))) { throw 'Plan or image was not retained before execution.' }
        if ($plan.probeInterval -ne $ProbeInterval -or $plan.probeIntervalEvidenceVersion -ne 1) { throw 'The declared interval contract differs from the request.' }
        if ($plan.prepareReceipts -isnot [bool] -or $plan.prepareReceipts -ne $requestedPreparation -or $plan.receiptPreparationEvidenceVersion -ne 1) { throw 'Preparation plan differs from request.' }
        if ($plan.ownerController -isnot [bool] -or $plan.ownerController -ne $requestedOwnership -or $plan.controllerOwnershipEvidenceVersion -ne 1) { throw 'Controller ownership plan differs from request.' }
        $current = @($before | Where-Object mode -eq $mode)[0]
        if (-not $current.attempted -or $current.executed) { throw 'The pending attempt was not saved before launch.' }
        foreach ($target in @('/workload', '/profiler', '/native')) {
            if (@($args | Where-Object { $_ -like "type=bind,source=*,target=$target,readonly" }).Count -ne 1) {
                throw "The $target input mount is not read-only."
            }
        }
        foreach ($setting in @('DOTNET_TieredCompilation=0', 'DOTNET_ReadyToRun=0', 'DOTNET_PerfMapEnabled=0')) {
            if ($args -notcontains $setting -or $current.containerArguments -notcontains $setting) {
                throw "The actual or retained common runtime setting is missing: $setting"
            }
        }
        if ($Scenario -eq 'failed-arms' -and $mode -eq 'physical-optimized') { throw 'controlled launch failure' }
        if ($mode.StartsWith('sampled-') -and ($args -notcontains "COZNET_PROBE_INTERVAL=$ProbeInterval" -or $current.activation -notcontains "COZNET_PROBE_INTERVAL=$ProbeInterval")) {
            throw 'The sampled process or retained activation changed its probe interval.'
        }
        if ($mode.StartsWith('sampled-') -and ($args -notcontains ("COZNET_PREPARE_RECEIPTS=" + [int]$requestedPreparation) -or $current.activation -notcontains ("COZNET_PREPARE_RECEIPTS=" + [int]$requestedPreparation))) { throw 'Preparation activation differs from request.' }
        if ($mode.StartsWith('sampled-') -and ($args -notcontains ("COZNET_OWNER_CONTROLLER=" + [int]$requestedOwnership) -or $current.activation -notcontains ("COZNET_OWNER_CONTROLLER=" + [int]$requestedOwnership))) { throw 'Controller ownership activation differs from request.' }
        if ($mode.StartsWith('physical-') -and @($args | Where-Object { $_ -like 'COZNET_OWNER_CONTROLLER=*' }).Count) { throw 'Physical arm received controller activation.' }
        if ($Scenario -eq 'failed-arms' -and $mode -eq 'sampled-zero') { $global:LASTEXITCODE = 137; return }
        @{ mode = $mode } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $armPath 'result.json') -Encoding utf8
        if ($mode -eq 'sampled-half') {
            if ($Scenario -eq 'changed-original') {
                Set-Content -LiteralPath (Join-Path $fixtureRoot 'src/CozNet/fixture.fs') -Value 'changed' -Encoding utf8
            }
            if ($Scenario -eq 'changed-snapshot') {
                Set-Content -LiteralPath (Join-Path $captureRoot 'source/native/AnalyzeCooperativeComparison.py') -Value 'changed' -Encoding utf8
            }
        }
    }
    function Invoke-FixturePython {
        $global:LASTEXITCODE = 0
        if ((Split-Path $args[0] -Leaf) -ne 'AnalyzeCooperativeComparison.py') { return }
        $analyzerCalls.Add($args[0])
        @{ status = 'unqualified'; passed = $false; predictionAccuracyEstablished = $false } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $args[1] 'analysis.json') -Encoding utf8
        $global:LASTEXITCODE = 1
    }
    $evidence = Join-Path $fixtureRoot 'evidence'
    $failure = $null
    $options = @{}
    if ($PrepareReceipts) { $options.PrepareReceipts = $true }
    if ($OwnerController) { $options.OwnerController = $true }
    if ($ProbeInterval -ne 1024) { $options.ProbeInterval = $ProbeInterval }
    try {
        if ($Scenario -eq 'positional') {
            & (Join-Path $fixtureRoot 'native/VerifyCooperativeComparison.ps1') $evidence 5 Invoke-FixturePython | Out-Null
        } else {
            & (Join-Path $fixtureRoot 'native/VerifyCooperativeComparison.ps1') -EvidenceDirectory $evidence -SelectedBatches 5 -PythonCommand Invoke-FixturePython @options | Out-Null
        }
    } catch { $failure = $_.ToString() }
    if ($failure -notlike '*prediction accuracy remains unqualified*') { throw "The runner did not retain its qualification failure: $failure" }
    $runs = @(Get-ChildItem -LiteralPath $evidence -Directory)
    if ($runs.Count -ne 1) { throw 'The fixture must retain exactly one capture.' }
    $capture = $runs[0].FullName
    $outcomes = @(Get-Content -Raw -LiteralPath (Join-Path $capture 'outcomes.json') | ConvertFrom-Json)
    $integrity = Get-Content -Raw -LiteralPath (Join-Path $capture 'integrity.json') | ConvertFrom-Json
    $errors = @(Get-Content -Raw -LiteralPath (Join-Path $capture 'runner-errors.json') | ConvertFrom-Json)
    $analysis = Get-Content -Raw -LiteralPath (Join-Path $capture 'analysis.json') | ConvertFrom-Json
    if (($outcomes.mode -join ',') -ne 'physical-baseline,physical-optimized,sampled-zero,sampled-half') { throw 'The declared arm order changed.' }
    if ($analysis.passed -or $analysis.predictionAccuracyEstablished) { throw 'The runner claimed qualification.' }
    if ($Scenario -eq 'preflight-failure') {
        if (@($outcomes | Where-Object attempted).Count -ne 0 -or $errors.Count -eq 0) { throw 'Preflight failure executed work or lost its error.' }
        return
    }
    if (@($outcomes | Where-Object attempted).Count -ne 4) { throw 'The runner did not attempt every arm after a case failure.' }
    if ($Scenario -eq 'failed-arms') {
        if ($outcomes[1].executed -or $null -ne $outcomes[1].exitCode -or $outcomes[1].recordExists) { throw 'Launch failure acquired a fabricated process result.' }
        if (-not $outcomes[2].executed -or $outcomes[2].exitCode -ne 137 -or $outcomes[2].recordExists) { throw 'The failed process lost its exit code or acquired a result.' }
        if (-not $outcomes[3].executed -or -not $outcomes[3].recordExists -or $errors.Count -ne 1) { throw 'The last arm did not survive earlier failures.' }
    } elseif (@($outcomes | Where-Object { -not $_.executed -or -not $_.recordExists -or $_.exitCode -ne 0 }).Count) {
        throw ("The controlled successful process lost its result: " + ($errors | ConvertTo-Json -Compress))
    }
    if ($Scenario -eq 'changed-snapshot') {
        if ($analyzerCalls.Count -ne 0 -or $analysis.status -ne 'invalid' -or $errors.Count -eq 0) { throw 'The runner executed a changed analyzer.' }
    } elseif ($analyzerCalls.Count -ne 1 -or $analyzerCalls[0] -ne (Join-Path $capture 'source/native/AnalyzeCooperativeComparison.py')) {
        throw 'The runner did not execute exactly one retained analyzer.'
    }
    if ($integrity.originalInputsUnchanged -ne ($Scenario -ne 'changed-original')) { throw 'Original input integrity differs from the fixture.' }
    if ($Scenario -eq 'changed-original' -and $integrity.changedInputs -notcontains 'src/CozNet/fixture.fs') { throw 'The changed original input is missing.' }
    foreach ($outcome in $outcomes) {
        if ($outcome.recordExists -and -not (Test-Path -LiteralPath (Join-Path $capture "$($outcome.mode).json"))) { throw 'The retained case copy is absent.' }
        $physical = $outcome.mode.StartsWith('physical-')
        $expectedActivation = if ($physical) { 'CORECLR_ENABLE_PROFILING=0' } else { 'CORECLR_ENABLE_PROFILING=1' }
        if ($outcome.activation -notcontains $expectedActivation) { throw 'Profiler activation does not match the arm.' }
        if (-not $physical -and ($outcome.activation -notcontains 'COZNET_FINITE_MEASUREMENT=1' -or $outcome.activation -notcontains 'COZNET_OWNER_DRAIN=1')) {
            throw 'A sampled arm lost its finite owner-drain policy.'
        }
        $expectedArguments = if ($physical) {
            @('--cooperative-physical', '/evidence/result.json', $outcome.mode.Substring(9), '5')
        } else {
            $percent = if ($outcome.mode -eq 'sampled-zero') { '0' } else { '50' }
            @('--sampled-bound-selected-cadence', '/evidence/result.json', $percent, '/native/libCozNet.Cooperative.so', 'qualified-sample', '5')
        }
        if (($outcome.arguments -join '|') -ne ($expectedArguments -join '|')) { throw 'An arm changed its declared work or intervention.' }
    }
}

try {
    Invoke-Fixture 'owner-default' 1024 $false $true
    Invoke-Fixture 'owner-prepared-sparse' 8192 $true $true
    Invoke-Fixture 'prepared-default' 1024 $true
    Invoke-Fixture 'prepared-sparse' 8192 $true
    Invoke-Fixture 'sparse-interval' 8192
    Invoke-Fixture 'positional'
    foreach ($scenario in @('complete', 'failed-arms', 'preflight-failure', 'changed-original', 'changed-snapshot')) {
        Invoke-Fixture $scenario
    }
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::GetDirectoryName($resolved) -ne $parent -or [IO.Path]::GetFileName($resolved) -notlike 'coz-comparison-runner-*') {
        throw 'The fixture cleanup path is outside its temporary parent.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
