function Read-BoundSampledOutcome {
    param([string]$Path, [string]$Case, [int]$ExitCode,
        [ValidateSet('', 'continuous', 'qualified-sample')][string]$SelectedCadence = '',
        [int]$SelectedBatches = 5000)
    $passed = $false
    $recordError = $null
    try {
        $record = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        if ($record.passed -isnot [bool]) { throw 'The case record lacks a Boolean outcome.' }
        if ($SelectedCadence) {
            $expectedMode = if ($SelectedCadence -eq 'continuous') { 'Continuous' } else { 'QualifiedSample' }
            $integerSchema = $record.schemaVersion -is [int] -or $record.schemaVersion -is [long]
            $integerBatches = $record.requestedBatches -is [int] -or $record.requestedBatches -is [long]
            if (-not $integerSchema -or $record.schemaVersion -ne 4 -or $record.selectedCadence -cne $expectedMode -or -not $integerBatches -or $record.requestedBatches -ne $SelectedBatches) {
                throw 'Cadence evidence differs from requested settings.'
            }
        }
        $passed = $ExitCode -eq 0 -and $record.passed
    }
    catch { $recordError = $_.Exception.Message }
    return @{ case = $Case; exitCode = $ExitCode; passed = $passed; recordError = $recordError }
}
