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
$settingsResult = Join-Path $PSScriptRoot '../artifacts/settings-checks.txt'
Write-Host 'Starting settings and dialog verification'
$settingsCheck = Start-Process $app -ArgumentList '--verify-settings', "`"$settingsResult`"" -PassThru
if (-not $settingsCheck.WaitForExit(60000)) {
    if (Test-Path "$settingsResult.trace") { Get-Content "$settingsResult.trace" | Write-Host }
    $settingsCheck.Kill($true)
    throw 'Settings / dialog verification exceeded 60 seconds'
}
if ($settingsCheck.ExitCode -ne 0) { throw (Get-Content $settingsResult -Raw) }
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
    private delegate bool EnumWindowProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    public static IntPtr FindVisibleWindow(int processId) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, data) => {
            uint id; GetWindowThreadProcessId(window, out id);
            if (id == (uint)processId && IsWindowVisible(window)) { found = window; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
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
function Save-Preview($Process, [string]$Name) {
    Write-Host "Capturing $Name window"
    $windowHandle = [IntPtr]::Zero
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 500
        $Process.Refresh()
        $windowHandle = [WindowCapture]::FindVisibleWindow($Process.Id)
        if ($windowHandle -ne [IntPtr]::Zero) { break }
        if ($Process.HasExited) { throw "$Name exited before its window appeared" }
    }
    if ($windowHandle -eq [IntPtr]::Zero) { throw "$Name window did not open" }
    $rect = New-Object WindowCapture+RECT
    [void][WindowCapture]::GetWindowRect($windowHandle, [ref]$rect)
    $image = New-Object System.Drawing.Bitmap(($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
    $canvas = [System.Drawing.Graphics]::FromImage($image)
    $dc = $canvas.GetHdc()
    try { $ok = [WindowCapture]::PrintWindow($windowHandle, $dc, 2) }
    finally { $canvas.ReleaseHdc($dc) }
    if (-not $ok) { throw "$Name capture failed" }
    $image.Save((Join-Path $PSScriptRoot "../artifacts/$Name-preview.png"))
    $canvas.Dispose(); $image.Dispose()
}
foreach ($kind in @('update', 'dialog', 'toast', 'style', 'devices', 'proximity', 'unlock', 'login')) {
    $window = Start-Process $app -ArgumentList "--preview-$kind" -PassThru
    try { Save-Preview $window $kind }
    finally { if (-not $window.HasExited) { Stop-Process -Id $window.Id; $window.WaitForExit() } }
}
# Measure the real installed settings path (not the sample preview path).
$launchTimer = [Diagnostics.Stopwatch]::StartNew()
$interactive = Start-Process $app -PassThru
try {
    do {
        Start-Sleep -Milliseconds 25
        $interactive.Refresh()
        if ($interactive.HasExited) { throw 'Normal application launch exited before showing settings' }
        $handle = [WindowCapture]::FindVisibleWindow($interactive.Id)
    } while ($handle -eq [IntPtr]::Zero -and $launchTimer.ElapsedMilliseconds -lt 15000)
    if ($handle -eq [IntPtr]::Zero) { throw 'Normal settings startup exceeded 15 seconds' }
    "Installed settings visible: $($launchTimer.ElapsedMilliseconds) ms (warm CI runner; not a user-hardware benchmark)" |
        Set-Content (Join-Path $PSScriptRoot '../artifacts/startup-timing.txt')
    Write-Host (Get-Content (Join-Path $PSScriptRoot '../artifacts/startup-timing.txt'))
} finally {
    if (-not $interactive.HasExited) { Stop-Process -Id $interactive.Id; $interactive.WaitForExit() }
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

# Inspect the real installer welcome page after uninstall, without completing a second installation.
$wizard = Start-Process $setup -ArgumentList '/NORESTART', '/LANG=chinese' -PassThru
$wizardWindow = $null
try {
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 500
        $wizardWindow = Get-Process | Where-Object { $_.MainWindowTitle -eq '安装 — AudioSwitch 音频切换' } | Select-Object -First 1
        if ($wizardWindow) { break }
    }
    if (-not $wizardWindow) { throw 'Installer welcome window did not appear' }
    Save-Preview $wizardWindow 'installer'
} finally {
    if ($wizardWindow -and -not $wizardWindow.HasExited) { Stop-Process -Id $wizardWindow.Id }
    if (-not $wizard.HasExited) { Stop-Process -Id $wizard.Id }
}
