<#
.SYNOPSIS
Builds a self-contained Release of Temizlikci and zips it for installing or attaching to a GitHub release.

.DESCRIPTION
Runs the tests first, then publishes for win-x64 with the .NET runtime and the Windows App SDK included, so the
folder runs on a PC with nothing installed. Output: artifacts\Temizlikci-<version>-win-x64\ and a zip beside it.
The version comes from Directory.Build.props.

.EXAMPLE
.\scripts\publish.ps1
.\scripts\publish.ps1 -SkipTests
#>
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$props = [xml](Get-Content -Raw -Path (Join-Path $root 'Directory.Build.props'))
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }

$name = "Temizlikci-$version-win-x64"
$artifacts = Join-Path $root 'artifacts'
$output = Join-Path $artifacts $name
$zip = Join-Path $artifacts "$name.zip"

if (-not $SkipTests) {
    Write-Host "Testing..."
    dotnet test --project (Join-Path $root 'tests\Temizlikci.Tests')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; nothing was published.' }
}

# Only this script's own output is replaced; anything else in artifacts\ stays.
if (Test-Path $output) { Remove-Item -Recurse -Force $output }
if (Test-Path $zip) { Remove-Item -Force $zip }

Write-Host "Publishing $name..."
dotnet publish (Join-Path $root 'src\Temizlikci.App\Temizlikci.App.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $output `
    -p:PublishReadyToRun=true `
    -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip
$size = '{0:N1} MB' -f ((Get-Item $zip).Length / 1MB)
Write-Host "Done: $zip ($size)"
Write-Host "Run: $(Join-Path $output 'Temizlikci.exe')"
