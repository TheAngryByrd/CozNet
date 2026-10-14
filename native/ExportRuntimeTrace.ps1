param(
    [Parameter(Mandatory)][string]$TracePath,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$ToolDirectory = (Join-Path $PSScriptRoot '../lode/tmp/diagnostic-tools/.store/dotnet-trace/10.0.750501/dotnet-trace/10.0.750501/tools/net8.0/any')
)
$ErrorActionPreference = 'Stop'
$trace = (Resolve-Path -LiteralPath $TracePath).Path
$output = [IO.Path]::GetFullPath($OutputPath)
$assembly = (Resolve-Path -LiteralPath (Join-Path $ToolDirectory 'Microsoft.Diagnostics.Tracing.TraceEvent.dll')).Path
$scratch = Join-Path $PSScriptRoot ('../lode/tmp/runtime-trace-reader-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $scratch | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RuntimeTraceReader.cs') -Destination (Join-Path $scratch 'Program.cs')
$escaped = [Security.SecurityElement]::Escape($assembly)
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Microsoft.Diagnostics.Tracing.TraceEvent"><HintPath>$escaped</HintPath></Reference>
  </ItemGroup>
</Project>
"@
$projectPath = Join-Path $scratch 'RuntimeTraceReader.csproj'
[IO.File]::WriteAllText($projectPath, $project)
& dotnet run --project $projectPath -- $trace $output
if ($LASTEXITCODE -ne 0) { throw 'Runtime trace decoding failed.' }
