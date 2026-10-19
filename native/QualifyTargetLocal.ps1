param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/target-local-qualification'),
    [string]$PythonCommand = $(if ($env:COZNET_PYTHON) { $env:COZNET_PYTHON } elseif ($IsWindows) { 'py' } else { 'python3' }))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
function Get-SourceHash([string] $Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $normalized = [Collections.Generic.List[byte]]::new($bytes.Length)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if (-not ($bytes[$i] -eq 13 -and $i + 1 -lt $bytes.Length -and $bytes[$i + 1] -eq 10)) { $normalized.Add($bytes[$i]) }
    }
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($normalized.ToArray()))
}
$commit = (& git -C $root rev-parse HEAD).Trim()
$status = @(& git -C $root status --porcelain)
$start = [ordered]@{}
function Get-PackageFiles {
    @(& git -C $root ls-files --cached --others --exclude-standard -- native src samples lode/plans build) +
        @(& git -C $root ls-files --cached --others --exclude-standard | Where-Object { $_ -notmatch '/' }) | Sort-Object -Unique |
        Where-Object { Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf }
}
foreach ($name in (Get-PackageFiles)) { $start[$name] = Get-SourceHash (Join-Path $root $name) }
$suites = @(
    @{ suite = 'prediction'; script = 'VerifyTargetLocalPrediction.ps1'; args = @(); folder = 'target-local-prediction' }
    @{ suite = 'transitions'; script = 'VerifyTargetLocalTransitions.ps1'; args = @(); folder = 'target-local-transitions' }
    @{ suite = 'barrier'; script = 'VerifyTargetLocalBarrier.ps1'; args = @(); folder = 'target-local-barrier' }
    @{ suite = 'latency'; script = 'VerifyTargetLocalProfile.ps1'; args = @(); folder = 'target-local-profile' }
    @{ suite = 'throughput'; script = 'VerifyTargetLocalProfile.ps1'; args = @('-Workload', 'throughput'); folder = 'target-local-throughput' }
    @{ suite = 'pipeline'; script = 'VerifyTargetLocalProfile.ps1'; args = @('-Workload', 'pipeline'); folder = 'target-local-pipeline' }
    @{ suite = 'lock'; script = 'VerifyTargetLocalProfile.ps1'; args = @('-Workload', 'lock'); folder = 'target-local-lock' }
    @{ suite = 'spinlock'; script = 'VerifyTargetLocalProfile.ps1'; args = @('-Workload', 'spinlock'); folder = 'target-local-spinlock' }
    @{ suite = 'semaphore'; script = 'VerifyTargetLocalProfile.ps1'; args = @('-Workload', 'semaphore'); folder = 'target-local-semaphore' }
    @{ suite = 'transaction'; script = 'VerifyTargetLocalProfile.ps1'; args = @('-Workload', 'transaction'); folder = 'target-local-transaction' }
    @{ suite = 'spintransaction'; script = 'VerifyTargetLocalProfile.ps1'; args = @('-Workload', 'spintransaction'); folder = 'target-local-spintransaction' })
$records = @()
foreach ($suite in $suites) {
    $folder = Join-Path $root "artifacts/$($suite.folder)"
    $before = @(if (Test-Path $folder) { Get-ChildItem -Directory $folder | ForEach-Object Name })
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot $suite.script) @($suite.args) -PythonCommand $PythonCommand *> (Join-Path $run "$($suite.suite).log")
    $exitCode = $LASTEXITCODE
    $after = @(if (Test-Path $folder) { Get-ChildItem -Directory $folder | ForEach-Object Name })
    $new = @($after | Where-Object { $_ -notin $before } | Sort-Object)
    $summary = $null
    $sources = $null
    $readError = $null
    if ($new.Count -eq 1) {
        $directory = Join-Path $folder $new[0]
        $summaryPath = Join-Path $directory 'summary.json'
        $manifestPath = Join-Path $directory 'manifest.json'
        try {
            if (Test-Path $summaryPath) {
                $read = Get-Content -Raw $summaryPath | ConvertFrom-Json -AsHashtable
                $summary = @{ passed = $read['passed']; inputsUnchanged = $read['inputsUnchanged'] }
                $sources = if (Test-Path $manifestPath) { (Get-Content -Raw $manifestPath | ConvertFrom-Json -AsHashtable)['sources'] } else { $read['sources'] }
            }
        } catch {
            $summary = $null
            $sources = $null
            $readError = $_.Exception.Message
        }
    }
    $records += [ordered]@{ suite = $suite.suite; script = $suite.script; args = $suite.args; exitCode = $exitCode
        runs = @($new | ForEach-Object { "artifacts/$($suite.folder)/$_" }); summary = $summary; sources = $sources; readError = $readError }
}
$changed = @($start.Keys | Where-Object { -not (Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf) -or (Get-SourceHash (Join-Path $root $_)) -ne $start[$_] }) +
    @(Get-PackageFiles | Where-Object { -not $start.Contains($_) })
[ordered]@{ commit = $commit; status = $status; startHashes = $start; changedDuringPackage = $changed; suites = $records } |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $run 'package.json')
& $PythonCommand -I (Join-Path $PSScriptRoot 'TargetLocalPackage.py') $run
$verdictExit = $LASTEXITCODE
Write-Output $run
if ($verdictExit -ne 0) { throw "The target-local qualification package did not pass. Retain this package." }
