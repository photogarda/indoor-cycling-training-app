# Builds a single, self-contained Trainer.exe (no .NET install needed on the target PC).
#   powershell -ExecutionPolicy Bypass -File build\publish.ps1
# Output: publish\Trainer.exe. Then optionally compile installer\Trainer.iss with Inno Setup.
param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
dotnet test "$root\tests\Trainer.Tests" -c $Configuration
dotnet publish "$root\src\Trainer.App\Trainer.App.csproj" -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o "$root\publish"
Write-Host "Done: $root\publish\Trainer.exe"
