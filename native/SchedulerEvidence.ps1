function Get-SchedulerSnapshotHashes {
    param([string]$Run)
    $hashes = @{}
    foreach ($directory in @('snapshot', 'workload')) {
        Get-ChildItem -LiteralPath (Join-Path $Run $directory) -Recurse -File | ForEach-Object {
            $relative = [IO.Path]::GetRelativePath($Run, $_.FullName).Replace('\', '/')
            $hashes[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    }
    return $hashes
}

function Complete-SchedulerEvidence {
    param([string]$Run, [hashtable]$Hashes, [object[]]$Outcomes, [string[]]$Records, [string]$Analyzer)
    $current = Get-SchedulerSnapshotHashes $Run
    $unchanged = $current.Count -eq $Hashes.Count
    foreach ($key in $Hashes.Keys) {
        if (-not $current.ContainsKey($key) -or $current[$key] -ne $Hashes[$key]) { $unchanged = $false }
    }
    $recordsPresent = $Records.Count -gt 0
    foreach ($record in $Records) {
        if (-not (Test-Path -LiteralPath (Join-Path $Run $record) -PathType Leaf)) { $recordsPresent = $false }
    }
    $processesPassed = $Outcomes.Count -eq $Records.Count -and @($Outcomes | Where-Object exitCode -ne 0).Count -eq 0
    $analysisExit = $null
    $analysisError = $null
    if ($unchanged -and $recordsPresent) {
        try {
            & python -B (Join-Path $Run "snapshot/native/$Analyzer") $Run *> (Join-Path $Run 'analysis.log')
            $analysisExit = $LASTEXITCODE
        }
        catch { $analysisError = $_.Exception.Message }
    }
    $passed = $unchanged -and $recordsPresent -and $processesPassed -and $analysisExit -eq 0 -and $null -eq $analysisError
    @{ passed=$passed; snapshotUnchanged=$unchanged; recordsPresent=$recordsPresent; processesPassed=$processesPassed;
       analysisExit=$analysisExit; analysisError=$analysisError; outcomes=$Outcomes } |
        ConvertTo-Json -Depth 6 | Set-Content (Join-Path $Run 'summary.json')
    if (-not $passed) { throw "Scheduler evidence checks failed. Retain all outcomes: $Run" }
}
