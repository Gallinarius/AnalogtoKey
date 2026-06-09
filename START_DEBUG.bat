@echo off
cd /d "%~dp0src"
dotnet build -c Release -v quiet
start "" "bin\Release\net10.0-windows\AnalogtoKey.exe"
