<#
.SYNOPSIS
Builds a self-contained Release of Temizlikci: the installer and a portable zip, ready for a GitHub release.

.DESCRIPTION
Runs the tests first, then publishes for win-x64 with the .NET runtime and the Windows App SDK included, so it runs on
a PC with nothing installed. Output in artifacts\:
  Temizlikci-Setup.exe                   the installer (Inno Setup; the name never changes, for "latest" links)
  Temizlikci-<version>-win-x64-portable.zip
  Temizlikci-<version>-win-x64\          the published folder
The version comes from Directory.Build.props. The installer needs Inno Setup 6 (winget install JRSoftware.InnoSetup).

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
$zip = Join-Path $artifacts "$name-portable.zip"
$setup = Join-Path $artifacts 'Temizlikci-Setup.exe'

$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }

if (-not $SkipTests) {
    Write-Host "Testing..."
    dotnet test --project (Join-Path $root 'tests\Temizlikci.Tests')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; nothing was published.' }
}

# Only this script's own output is replaced; anything else in artifacts\ stays.
foreach ($old in @($output, $zip, $setup)) { if (Test-Path $old) { Remove-Item -Recurse -Force $old } }

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

Write-Host "Building the installer..."
& $iscc /Q "/DAppVersion=$version" "/DSourceDir=$output" "/DOutputDir=$artifacts" (Join-Path $root 'installer\Temizlikci.iss')
if ($LASTEXITCODE -ne 0) { throw 'The installer build failed.' }

foreach ($file in @($setup, $zip)) { Write-Host ("Done: {0} ({1:N1} MB)" -f $file, ((Get-Item $file).Length / 1MB)) }
