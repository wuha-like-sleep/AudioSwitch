# Run only in an isolated Windows CI runner: exercises actual install/startup/uninstall behavior.
$ErrorActionPreference = 'Stop'
$install = Join-Path $env:LOCALAPPDATA 'Programs/AudioSwitch'
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$setup = Join-Path $PSScriptRoot '../artifacts/installer/AudioSwitch-Setup-win-x64.exe'
$process = Start-Process $setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=startup' -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Install failed: $($process.ExitCode)" }
$app = Join-Path $install 'AudioSwitch.exe'
if (-not (Test-Path $app)) { throw 'Installed application missing' }
$value = (Get-ItemProperty $run).AudioSwitch
if ($value -ne "`"$app`" --startup") { throw "Incorrect startup command: $value" }
# Exercise real Windows audio COM interfaces even when the runner has no physical output device.
foreach ($service in @('AudioEndpointBuilder', 'Audiosrv')) {
    Set-Service $service -StartupType Manual
    Start-Service $service
}
$audioResult = Join-Path $PSScriptRoot '../artifacts/audio-interfaces.txt'
$audioCheck = Start-Process $app -ArgumentList '--verify-audio', "`"$audioResult`"" -Wait -PassThru
if ($audioCheck.ExitCode -ne 0) { throw (Get-Content $audioResult -Raw) }
# Render the actual settings window with sample devices, independent of CI audio hardware.
$preview = Start-Process $app -ArgumentList '--preview' -PassThru
try {
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 500
        $preview.Refresh()
        if ($preview.MainWindowHandle -ne 0) { break }
        if ($preview.HasExited) { throw 'Settings preview exited unexpectedly' }
    }
    if ($preview.MainWindowHandle -eq 0) { throw 'Settings preview window did not open' }
    Add-Type -AssemblyName System.Drawing
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public class WindowCapture {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
}
'@
    $rect = New-Object WindowCapture+RECT
    [void][WindowCapture]::GetWindowRect($preview.MainWindowHandle, [ref]$rect)
    $bitmap = New-Object System.Drawing.Bitmap(($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    try { $captured = [WindowCapture]::PrintWindow($preview.MainWindowHandle, $dc, 2) }
    finally { $graphics.ReleaseHdc($dc) }
    if (-not $captured) { throw 'Settings screenshot capture failed' }
    $bitmap.Save((Join-Path $PSScriptRoot '../artifacts/settings-preview.png'))
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
    $bitmap.Save((Join-Path $PSScriptRoot '../artifacts/settings-screen-preview.png'))
    $graphics.Dispose(); $bitmap.Dispose()
} finally {
    if (-not $preview.HasExited) { Stop-Process -Id $preview.Id; $preview.WaitForExit() }
}
$appProcess = Start-Process $app -ArgumentList '--startup' -PassThru
try {
    Start-Sleep -Seconds 5
    if ($appProcess.HasExited) { throw "Application exited unexpectedly: $($appProcess.ExitCode)" }
} finally {
    if (-not $appProcess.HasExited) { Stop-Process -Id $appProcess.Id; $appProcess.WaitForExit() }
}
$process = Start-Process (Join-Path $install 'unins000.exe') -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Uninstall failed: $($process.ExitCode)" }
if ((Get-ItemProperty $run -ErrorAction SilentlyContinue).AudioSwitch) { throw 'Startup command was not removed' }
if (Test-Path $app) { throw 'Application was not removed' }
Write-Host 'Install, startup registration, application launch and uninstall smoke checks passed.'
