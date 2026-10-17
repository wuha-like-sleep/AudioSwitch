# Isolated Windows CI only. Registers/unregisters the optional lab provider; never locks or signs out.
$ErrorActionPreference = 'Stop'
$guid = '{A3AC9286-65AB-4438-B97E-84A4C9A65320}'
$providers = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers'
$classes = "HKLM:\SOFTWARE\Classes\CLSID\$guid"
$before = @(Get-ChildItem $providers | Select-Object -ExpandProperty PSChildName | Sort-Object)
$test = Join-Path $PSScriptRoot '../artifacts/login-win-x64/Release/LoginProviderTests.exe'
$dll = Join-Path $PSScriptRoot '../artifacts/login-win-x64/Release/AudioSwitch.LoginProvider.dll'
& $test $dll | Tee-Object (Join-Path $PSScriptRoot '../artifacts/login-component-checks.txt')
if ($LASTEXITCODE -ne 0) { throw 'Native login provider contract checks failed' }
$setup = Join-Path $PSScriptRoot '../artifacts/installer/AudioSwitch-LoginLab-Setup-win-x64.exe'
$install = Join-Path $env:ProgramFiles 'AudioSwitch Login Lab'
try {
    $process = Start-Process $setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Lab installer exit $($process.ExitCode)" }
    if (!(Test-Path "$providers\$guid") -or !(Test-Path "$classes\InprocServer32")) { throw 'Lab provider registration missing' }
    $service = Get-Service -Name 'AudioSwitchLoginBridge'
    if ($service.Status -ne 'Running') { throw 'Login bridge did not start' }
    $registered = (Get-Item "$classes\InprocServer32").GetValue('')
    if ($registered -ne (Join-Path $install 'AudioSwitch.LoginProvider.dll')) { throw 'COM path is not protected installation directory' }
    $after = @(Get-ChildItem $providers | Where-Object PSChildName -ne $guid | Select-Object -ExpandProperty PSChildName | Sort-Object)
    if (Compare-Object $before $after) { throw 'Existing system provider registrations changed' }
    & $test $registered
    if ($LASTEXITCODE -ne 0) { throw 'Installed provider loading failed' }
    # A synthetic admin-created binding cannot turn a regular process into LogonUI.
    $binding = 'HKLM:\SOFTWARE\AudioSwitch\LoginBridge\Bindings\S-1-5-21-100-200-300-1001'
    try {
        New-Item $binding -Force | Out-Null
        Set-ItemProperty $binding -Name Phone -Value '123456789ABC'
        & $test $registered '--deny-bound'
        if ($LASTEXITCODE -ne 0) { throw 'Broker allowed a non-LogonUI caller' }
        Stop-Service 'AudioSwitchLoginBridge'
        & $test $registered '--deny-bound'
        if ($LASTEXITCODE -ne 0) { throw 'Missing broker did not fail closed' }
        Start-Service 'AudioSwitchLoginBridge'
    } finally { Remove-Item $binding -Recurse -Force -ErrorAction SilentlyContinue }

} finally {
    $uninstall = Join-Path $install 'unins000.exe'
    if (Test-Path $uninstall) { $process = Start-Process $uninstall -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru; if ($process.ExitCode -ne 0) { throw "Uninstall exit $($process.ExitCode)" } }
}
if ((Test-Path "$providers\$guid") -or (Test-Path $classes)) { throw 'Lab registration remained after uninstall' }
$after = @(Get-ChildItem $providers | Select-Object -ExpandProperty PSChildName | Sort-Object)
if (Compare-Object $before $after) { throw 'System provider registrations changed after uninstall' }
Add-Content (Join-Path $PSScriptRoot '../artifacts/login-component-checks.txt') 'Optional installer registration, protected path, installed DLL loading, system provider preservation and uninstall passed. No lock, sign-out or real account authentication performed.'

if (Get-Service -Name 'AudioSwitchLoginBridge' -ErrorAction SilentlyContinue) { throw 'Login bridge service remained after uninstall' }
if (Test-Path 'HKLM:\SOFTWARE\AudioSwitch\LoginBridge') { throw 'Bridge binding registry remained after uninstall' }
Add-Content (Join-Path $PSScriptRoot '../artifacts/login-component-checks.txt') 'Phone gate denied non-LogonUI caller and unavailable service; nonce, phone, session, protocol, expiry checks passed; bridge service installed, started, stopped and removed. No real phone or account authentication performed.'
