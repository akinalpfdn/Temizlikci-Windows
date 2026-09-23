<#
.SYNOPSIS
  Captures a window's own content to a PNG, even when other windows cover it (PrintWindow with
  PW_RENDERFULLCONTENT, which includes WinUI's composition content).
.EXAMPLE
  scripts\screenshot.ps1 -ProcessName Temizlikci -Out C:\temp\shot.png
  scripts\screenshot.ps1 -ProcessId 1234 -Out C:\temp\shot.png
#>
param(
    [string]$ProcessName = "",
    # A specific process, when more than one instance may be open.
    [int]$ProcessId = 0,
    [Parameter(Mandatory = $true)][string]$Out
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WindowCapture {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
}
"@

$candidates = if ($ProcessId) { Get-Process -Id $ProcessId -ErrorAction Stop } else { Get-Process -Name $ProcessName -ErrorAction Stop }
$process = $candidates | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $process) { throw "No window for process $ProcessName$ProcessId." }
$handle = $process.MainWindowHandle
$rect = New-Object WindowCapture+RECT
[WindowCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$dc = $graphics.GetHdc()
[WindowCapture]::PrintWindow($handle, $dc, 2) | Out-Null
$graphics.ReleaseHdc($dc)
$bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()
Write-Output "Saved $width x $height to $Out"
