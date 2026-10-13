param([Parameter(Mandatory)][string]$HeaderPath, [Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$header = Get-Content $HeaderPath -Raw
$start = $header.IndexOf('ICorProfilerCallback : public IUnknown')
$end = $header.IndexOf('#else', $start)
$definition = $header.Substring($start, $end - $start)
$methods = [regex]::Matches($definition, 'virtual HRESULT STDMETHODCALLTYPE\s+(\w+)\((.*?)\)\s*=\s*0;', 'Singleline')
if ($methods.Count -ne 69) { throw "Expected 69 CLR callbacks, found $($methods.Count)." }
$start2 = $header.IndexOf('ICorProfilerCallback2 : public ICorProfilerCallback')
$end2 = $header.IndexOf('#else', $start2)
$methods2 = [regex]::Matches($header.Substring($start2, $end2 - $start2), 'virtual HRESULT STDMETHODCALLTYPE\s+(\w+)\((.*?)\)\s*=\s*0;', 'Singleline')
if ($methods2.Count -ne 8) { throw "Expected 8 CLR callback2 methods, found $($methods2.Count)." }
$base = @('#pragma once', '#include <cor.h>', '#include <corprof.h>', 'class CallbackBase : public ICorProfilerCallback2 { public:')
foreach ($method in @($methods) + @($methods2)) {
    $name = $method.Groups[1].Value
    $parameters = [regex]::Replace($method.Groups[2].Value, '/\*.*?\*/', '', 'Singleline')
    $base += "HRESULT STDMETHODCALLTYPE $name($parameters) override { return S_OK; }"
}
$base += '};'
$base | Set-Content $OutputPath
