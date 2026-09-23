<#
.SYNOPSIS
  Starts the Debug build, waits, captures its window, and closes it. For checking a change by eye.
.EXAMPLE
  scripts\preview.ps1 -Out C:\temp\dark.png -Arguments "--theme=dark" -Seconds 6
#>
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Arguments = "",
    [int]$Seconds = 5,
    [switch]$KeepOpen,
    # Another build to run instead of Debug, such as a published folder's Temizlikci.exe.
    [string]$Exe = ""
)

$root = Split-Path -Parent $PSScriptRoot
$exe = if ($Exe) { $Exe } else { Join-Path $root "src\Temizlikci.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\Temizlikci.exe" }
if (-not (Test-Path $exe)) { throw "Build the app first: $exe not found." }
# Only earlier previews of this same build are closed: an instance someone is using (or one running as administrator,
# whose path isn't readable from here) is left alone.
Get-Process -Name Temizlikci -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process -Force
$process = if ($Arguments) { Start-Process $exe -ArgumentList $Arguments -PassThru } else { Start-Process $exe -PassThru }
Start-Sleep -Seconds $Seconds
$process.Refresh()
if ($process.HasExited) { throw "Temizlikci exited with code $($process.ExitCode)." }
& (Join-Path $PSScriptRoot "screenshot.ps1") -ProcessId $process.Id -Out $Out
if (-not $KeepOpen) { Stop-Process -Id $process.Id -Force }
