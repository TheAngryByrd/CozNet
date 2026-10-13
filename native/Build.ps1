param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/native'))

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The native profiler prototype currently requires Windows x64.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$revision = '60629d14374c56f1cb51819049ad1fa529307f8d'
$include = Join-Path $output "runtime-$revision"
New-Item -ItemType Directory -Force $include | Out-Null
$files = @{
    'cor.h' = 'src/coreclr/inc/cor.h'
    'corhdr.h' = 'src/coreclr/inc/corhdr.h'
    'corprof.h' = 'src/coreclr/pal/prebuilt/inc/corprof.h'
    'corerror.h' = 'src/coreclr/pal/prebuilt/inc/corerror.h'
    'corhlpr.h' = 'src/coreclr/inc/corhlpr.h'
    'corhlpr.cpp' = 'src/coreclr/inc/corhlpr.cpp'
    'unreachable.h' = 'src/coreclr/inc/unreachable.h'
    'opcode.def' = 'src/coreclr/inc/opcode.def'
    'ilrewriter.h' = 'src/tests/profiler/native/rejitprofiler/ilrewriter.h'
    'ilrewriter.cpp' = 'src/tests/profiler/native/rejitprofiler/ilrewriter.cpp'
    'sigparse.h' = 'src/tests/profiler/native/rejitprofiler/sigparse.h'
    'sigparse.cpp' = 'src/tests/profiler/native/rejitprofiler/sigparse.cpp'
    'LICENSE.TXT' = 'LICENSE.TXT'
}
foreach ($name in $files.Keys) {
    $destination = Join-Path $include $name
    if (-not (Test-Path -LiteralPath $destination)) {
        Invoke-WebRequest "https://raw.githubusercontent.com/dotnet/runtime/$revision/$($files[$name])" -OutFile $destination
    }
}

& (Join-Path $PSScriptRoot 'GenerateCallbacks.ps1') -HeaderPath (Join-Path $include 'corprof.h') -OutputPath (Join-Path $include 'CallbackBase.h')
$rewriter = Get-Content (Join-Path $include 'ilrewriter.cpp') -Raw
$rewriter = $rewriter.Replace('ULONG cOrigLocals;', 'uint32_t cOrigLocals;').Replace('ULONG cbOrigLocals;', 'uint32_t cbOrigLocals;')
$rewriter | Set-Content (Join-Path $output 'ILRewriter.cpp')

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio C++ x64 build tools.' }
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Install Visual Studio C++ x64 build tools.' }
$developerCommand = Join-Path $visualStudio 'Common7/Tools/VsDevCmd.bat'
$source = Join-Path $PSScriptRoot 'Profiler.cpp'
$compilerArguments = @(
    '/nologo', '/LD', '/O2', '/EHsc', '/std:c++17', '/DWIN32', '/DWIN64', '/DSOS_INCLUDE', '/DNOMINMAX',
    "/I`"$include`"", "`"$source`"", "`"$PSScriptRoot/SnapshotBackend.Windows.cpp`"", "`"$output/ILRewriter.cpp`"", "`"$include/sigparse.cpp`"",
    '/link', 'ole32.lib', "/DEF:`"$PSScriptRoot/Profiler.def`"", "/OUT:`"$output/CozNet.Profiler.dll`""
)
$response = Join-Path $output 'compile.rsp'
($compilerArguments -join ' ') | Set-Content $response
$build = Join-Path $output 'compile.cmd'
@("@call `"$developerCommand`" -arch=x64 -host_arch=x64 >nul", '@if errorlevel 1 exit /b %errorlevel%', "@cl @`"$response`"") | Set-Content $build
Push-Location $output
try {
    & $env:ComSpec /d /c $build
    if ($LASTEXITCODE -ne 0) { throw "Native build failed with exit code $LASTEXITCODE." }
} finally { Pop-Location }
