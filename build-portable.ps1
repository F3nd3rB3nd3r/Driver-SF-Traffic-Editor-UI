# Builds a standalone Windows executable on a Windows machine with the .NET 10 SDK.
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish .\DriverSFTrafficEditor\DriverSFTrafficEditor.csproj `
        --configuration Release --runtime win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
        --output .\publish\win-x64
    Write-Host 'Executable: publish\win-x64\DriverSFTrafficEditor.exe'
} finally { Pop-Location }
