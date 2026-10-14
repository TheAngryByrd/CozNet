param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/target-cpu'))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'CollectLinuxRuntimeTraces.ps1') -TargetCpu -EvidenceDirectory $EvidenceDirectory
