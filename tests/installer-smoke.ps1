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
