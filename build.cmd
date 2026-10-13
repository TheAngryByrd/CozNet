@echo off
cd /d "%~dp0"
dotnet tool restore || exit /b 1
dotnet fantomas --check src tests samples || exit /b 1
dotnet test CozNet.slnx -c Release || exit /b 1
dotnet build samples/CozNet.Serial/CozNet.Serial.fsproj -c Release --no-restore || exit /b 1
