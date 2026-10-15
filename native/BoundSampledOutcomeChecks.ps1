$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReadBoundSampledOutcome.ps1')
$directory = Join-Path ([IO.Path]::GetTempPath()) ('coz-outcome-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$path = Join-Path $directory 'case.json'
function Read-Fixture {
    param($Record, [int]$ExitCode = 0, [string]$Cadence = 'qualified-sample')
    $Record | ConvertTo-Json | Set-Content -LiteralPath $path
    return Read-BoundSampledOutcome -Path $path -Case '50' -ExitCode $ExitCode -SelectedCadence $Cadence -SelectedBatches 5
}
try {
    $valid = @{ passed = $true; schemaVersion = 4; selectedCadence = 'QualifiedSample'; requestedBatches = 5 }
    $outcome = Read-Fixture $valid
    if (-not $outcome.passed -or $outcome.recordError -or $outcome.case -ne '50' -or $outcome.exitCode -ne 0) { throw 'A valid matching case must pass with its identity.' }
    foreach ($change in @(
        @{ field = 'schemaVersion'; value = 3 },
        @{ field = 'schemaVersion'; value = '4' },
        @{ field = 'selectedCadence'; value = 'Continuous' },
        @{ field = 'requestedBatches'; value = 6 },
        @{ field = 'requestedBatches'; value = '5' },
        @{ field = 'passed'; value = 'true' }
    )) {
        $record = $valid.Clone()
        $record[$change.field] = $change.value
        $outcome = Read-Fixture $record
        if ($outcome.passed -or -not $outcome.recordError) { throw "Invalid $($change.field) retained a successful outcome." }
    }
    $outcome = Read-Fixture $valid -ExitCode 1
    if ($outcome.passed -or $outcome.exitCode -ne 1) { throw 'A failed process cannot pass because its record reports success.' }
    $failed = $valid.Clone()
    $failed.passed = $false
    if ((Read-Fixture $failed).passed) { throw 'A failed runtime record cannot pass.' }
    if (-not (Read-Fixture @{ passed = $true; schemaVersion = 1 } -Cadence '').passed) { throw 'A legacy record must not require cadence fields.' }
    Set-Content -LiteralPath $path -Value '{'
    $outcome = Read-BoundSampledOutcome -Path $path -Case '50' -ExitCode 0
    if ($outcome.passed -or -not $outcome.recordError) { throw 'Malformed JSON cannot pass.' }
    Remove-Item -LiteralPath $path
    $outcome = Read-BoundSampledOutcome -Path $path -Case '50' -ExitCode 0
    if ($outcome.passed -or -not $outcome.recordError) { throw 'A missing record cannot pass.' }
}
finally {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    Remove-Item -LiteralPath $directory
}
