param([Parameter(Mandatory)][string]$RuntimeSource, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$corprof = Get-Content -Raw (Join-Path $RuntimeSource 'src/coreclr/pal/prebuilt/inc/corprof.h')
$cor = Get-Content -Raw (Join-Path $RuntimeSource 'src/coreclr/inc/cor.h')
$unknwn = Get-Content -Raw (Join-Path $RuntimeSource 'src/coreclr/pal/inc/rt/unknwn.h')
$guids = Get-Content -Raw (Join-Path $RuntimeSource 'src/tests/profiler/native/guids.cpp')

function Get-Methods([string]$Header, [string]$Interface, [string]$Base) {
    $start = $Header.IndexOf("$Interface : public $Base")
    if ($start -lt 0) { throw "The header has no $Interface." }
    $end = $Header.IndexOf('#else', $start)
    $block = $Header.Substring($start, $end - $start)
    $pattern = 'virtual\s+(\w+)\s+STDMETHODCALLTYPE\s+(\w+)\((.*?)\)\s*=\s*0;'
    $found = [regex]::Matches($block, $pattern, 'Singleline')
    $declared = [regex]::Matches($block, '\bvirtual\b').Count
    if ($found.Count -ne $declared) { throw "$Interface declares $declared methods, but the generator parsed $($found.Count)." }
    $found |
        ForEach-Object { [pscustomobject]@{ Return = $_.Groups[1].Value; Name = $_.Groups[2].Value; Raw = $_.Groups[3].Value; Parameters =[regex]::Replace($_.Groups[3].Value, '/\*.*?\*/', '', 'Singleline') } }
}

function Get-Uuid([string]$Header, [string]$Interface, [string]$Base) {
    $declaration = if ($Base) { "$Interface : public $Base" } else { "$Interface\s*\{" }
    $match = [regex]::Match($Header, "MIDL_INTERFACE\(""([0-9A-Fa-f-]+)""\)\s*$declaration")
    if (-not $match.Success) { throw "The header has no uuid for $Interface." }
    $match.Groups[1].Value.ToUpperInvariant()
}

function Get-DefinedGuid([string]$Text, [string]$Name) {
    $match = [regex]::Match($Text, "(?:DEFINE_GUID|EXTERN_GUID)\(\s*$Name\s*,\s*0x([0-9A-Fa-f]+)\s*,\s*0x([0-9A-Fa-f]+)\s*,\s*0x([0-9A-Fa-f]+)((?:\s*,\s*0x[0-9A-Fa-f]+){8})\s*\)")
    if (-not $match.Success) { throw "No definition of $Name." }
    $bytes = [regex]::Matches($match.Groups[4].Value, '0x([0-9A-Fa-f]+)') | ForEach-Object { $_.Groups[1].Value.PadLeft(2, '0') }
    ('{0}-{1}-{2}-{3}-{4}' -f $match.Groups[1].Value.PadLeft(8, '0'), $match.Groups[2].Value.PadLeft(4, '0'), $match.Groups[3].Value.PadLeft(4, '0'),
        ($bytes[0..1] -join ''), ($bytes[2..7] -join '')).ToUpperInvariant()
}

$used = @(
    @($unknwn, 'IUnknown', '', 'IID_IUnknown'), @($unknwn, 'IClassFactory', 'IUnknown', 'IID_IClassFactory'),
    @($corprof, 'ICorProfilerCallback', 'IUnknown', 'IID_ICorProfilerCallback'), @($corprof, 'ICorProfilerCallback2', 'ICorProfilerCallback', 'IID_ICorProfilerCallback2'),
    @($corprof, 'ICorProfilerCallback3', 'ICorProfilerCallback2', 'IID_ICorProfilerCallback3'), @($corprof, 'ICorProfilerInfo9', 'ICorProfilerInfo8', 'IID_ICorProfilerInfo9'))
foreach ($pair in $used) {
    $expected = Get-Uuid $pair[0] $pair[1] $pair[2]
    $defined = Get-DefinedGuid $guids $pair[3]
    if ($expected -ne $defined) { throw "$($pair[3]) is $defined, but corprof.h declares $expected." }
}
$metadataUuid = [regex]::Match($cor, 'DECLARE_INTERFACE_\(IMetaDataImport, IUnknown\)').Success
if (-not $metadataUuid) { throw 'cor.h has no IMetaDataImport declaration.' }
$null = Get-DefinedGuid $cor 'IID_IMetaDataImport'

$outputs = @('JITInlining', 'JITCachedFunctionSearchStarted')
$callbacks = @(Get-Methods $corprof 'ICorProfilerCallback' 'IUnknown') + @(Get-Methods $corprof 'ICorProfilerCallback2' 'ICorProfilerCallback')
if ($callbacks.Count -ne 77) { throw "Expected 77 callbacks, found $($callbacks.Count)." }
$written = @($callbacks | Where-Object { $_.Raw -match '\[(in, ?)?out\]' } | ForEach-Object Name | Sort-Object)
if (($written -join ',') -ne (($outputs | Sort-Object) -join ',')) { throw "The callbacks with [out] parameters are $($written -join ','), but the overrides are $($outputs -join ',')." }
$lines = @('#pragma once', '#include <cor.h>', '#include <corprof.h>', 'class MapCallbackBase : public ICorProfilerCallback2 { public:')
foreach ($callback in $callbacks | Where-Object { $outputs -notcontains $_.Name }) {
    $lines += "HRESULT STDMETHODCALLTYPE $($callback.Name)($($callback.Parameters)) override { return S_OK; }"
}
$lines += '};'
$lines | Set-Content (Join-Path $OutputDirectory 'MapCallbackBase.h')

$infos = @()
$infos += Get-Methods $corprof 'ICorProfilerInfo' 'IUnknown'
foreach ($n in 2..9) { $infos += Get-Methods $corprof "ICorProfilerInfo$n" $(if ($n -eq 2) { "ICorProfilerInfo" } else { "ICorProfilerInfo$($n - 1)" }) }
$lines = @('#pragma once', '#include <cor.h>', '#include <corprof.h>', 'class FakeInfoBase : public ICorProfilerInfo9 { public:')
foreach ($info in $infos) { $lines += "HRESULT STDMETHODCALLTYPE $($info.Name)($($info.Parameters)) override { return E_NOTIMPL; }" }
$lines += '};'
$lines | Set-Content (Join-Path $OutputDirectory 'FakeInfoBase.h')

$start = $cor.IndexOf('DECLARE_INTERFACE_(IMetaDataImport, IUnknown)')
$end = $cor.IndexOf('};', $start)
$block = [regex]::Replace($cor.Substring($start, $end - $start), '//[^\n]*', '')
$methods = [regex]::Matches($block, 'STDMETHOD\s*(?:_\(\s*(\w+)\s*,\s*|\(\s*)(\w+)\s*\)\s*\((.*?)\)\s*PURE;', 'Singleline')
$declared = [regex]::Matches($block, '\bSTDMETHOD').Count
if ($methods.Count -ne $declared) { throw "IMetaDataImport declares $declared methods, but the generator parsed $($methods.Count)." }
$lines = @('#pragma once', '#include <cor.h>', 'class FakeImportBase : public IMetaDataImport { public:')
foreach ($method in $methods) {
    $returnType = if ($method.Groups[1].Success) { $method.Groups[1].Value } else { 'HRESULT' }
    $body = if ($returnType -eq 'void') { '{}' } else { '{ return E_NOTIMPL; }' }
    $lines += "STDMETHODIMP_($returnType) $($method.Groups[2].Value)($($method.Groups[3].Value)) override $body"
}
$lines += '};'
$lines | Set-Content (Join-Path $OutputDirectory 'FakeImportBase.h')
"callbacks $($callbacks.Count), info methods $($infos.Count), metadata methods $($methods.Count)"
