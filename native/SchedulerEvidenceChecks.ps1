param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/scheduler-gate-checks'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SchedulerEvidence.ps1')
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ('run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
New-Item -ItemType Directory -Force $run | Out-Null
$results = @()
foreach ($mode in @('valid', 'process-failure', 'missing-record', 'changed-snapshot', 'analyzer-failure', 'added-snapshot-file')) {
    $case = Join-Path $run $mode
    New-Item -ItemType Directory -Force (Join-Path $case 'snapshot/native'), (Join-Path $case 'workload') | Out-Null
    [IO.File]::WriteAllText((Join-Path $case 'snapshot/native/analyzer.py'), 'import sys; sys.exit(0)')
    [IO.File]::WriteAllText((Join-Path $case 'workload/executable'), 'original')
    if ($mode -ne 'missing-record') { [IO.File]::WriteAllText((Join-Path $case 'record.json'), '{}') }
    if ($mode -eq 'analyzer-failure') { [IO.File]::WriteAllText((Join-Path $case 'snapshot/native/analyzer.py'), 'import sys; sys.exit(7)') }
    $hashes = Get-SchedulerSnapshotHashes $case
    if ($mode -eq 'changed-snapshot') { [IO.File]::WriteAllText((Join-Path $case 'workload/executable'), 'changed') }
    if ($mode -eq 'added-snapshot-file') { [IO.File]::WriteAllText((Join-Path $case 'workload/addition'), 'added') }
    $outcomes = @(@{ name='fixture'; exitCode=$(if ($mode -eq 'process-failure') { 1 } else { 0 }) })
    $threw = $false
    try { Complete-SchedulerEvidence -Run $case -Hashes $hashes -Outcomes $outcomes -Records @('record.json') -Analyzer 'analyzer.py' }
    catch { $threw = $true }
    $summary = Get-Content (Join-Path $case 'summary.json') -Raw | ConvertFrom-Json
    $expected = $mode -eq 'valid'
    if ($threw -eq $expected -or $summary.passed -ne $expected) { throw "Scheduler gate accepted the wrong outcome for $mode." }
    if ($mode -eq 'process-failure' -and ($summary.processesPassed -or $summary.analysisExit -ne 0)) { throw 'Failed processes must fail even when analysis succeeds.' }
    if ($mode -eq 'missing-record' -and ($summary.recordsPresent -or $null -ne $summary.analysisExit)) { throw 'Missing records must prevent analysis.' }
    if ($mode -in @('changed-snapshot', 'added-snapshot-file') -and ($summary.snapshotUnchanged -or $null -ne $summary.analysisExit)) { throw 'Changed snapshots must prevent analysis.' }
    if ($mode -eq 'analyzer-failure' -and $summary.analysisExit -ne 7) { throw 'The analyzer exit code was not retained.' }
    $results += @{ mode=$mode; passed=$true }
}
$results | ConvertTo-Json | Set-Content (Join-Path $run 'checks.json')
$run
