#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
dotnet tool restore
dotnet fantomas --check src tests samples
dotnet test CozNet.slnx -c Release
dotnet build samples/CozNet.Serial/CozNet.Serial.fsproj -c Release --no-restore
