@echo off
setlocal
cd /d "%~dp0"
set "CAFEORDER_DOTNET=%~dp0..\..\work\dotnet\dotnet.exe"
if not exist "%CAFEORDER_DOTNET%" set "CAFEORDER_DOTNET=dotnet"
if exist "%~dp0..\..\work\nuget" set "NUGET_PACKAGES=%~dp0..\..\work\nuget"
"%CAFEORDER_DOTNET%" run --project "%~dp0src\CafeOrder\CafeOrder.csproj" -c Release -p:Platform=x64
if errorlevel 1 pause
