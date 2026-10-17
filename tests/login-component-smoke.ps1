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
$unsafeInstall = Join-Path $env:TEMP 'AudioSwitch-Unsafe-Lab-Test'
$rejected = Start-Process $setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=$unsafeInstall" -Wait -PassThru
if ($rejected.ExitCode -eq 0 -or (Test-Path (Join-Path $unsafeInstall 'AudioSwitch.LoginBridge.exe'))) { throw 'Lab installer accepted a custom writable directory' }
$looseBridge = Join-Path $PSScriptRoot '../artifacts/login-win-x64/Release/AudioSwitch.LoginBridge.exe'
& $looseBridge '--install-service'
if ($LASTEXITCODE -eq 0) { throw 'Loose helper registered itself outside Program Files' }

try {
    $process = Start-Process $setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Lab installer exit $($process.ExitCode)" }
    if (!(Test-Path "$providers\$guid") -or !(Test-Path "$classes\InprocServer32")) { throw 'Lab provider registration missing' }
    # Reinstall exercises stopping/replacing/restarting an existing service.
    $upgrade = Start-Process $setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru
    if ($upgrade.ExitCode -ne 0) { throw "Lab upgrade exit $($upgrade.ExitCode)" }
    $expectedVersion = ([xml](Get-Content (Join-Path $PSScriptRoot '../src/AudioSwitch/AudioSwitch.csproj') -Raw)).Project.PropertyGroup.InformationalVersion
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $install 'AudioSwitch.LoginBridge.exe')).ProductVersion -ne $expectedVersion) { throw 'Installed helper product version does not match the app' }
    $service = Get-Service -Name 'AudioSwitchLoginBridge'
    if ($service.Status -ne 'Running') { throw 'Login bridge did not start' }
    $registered = (Get-Item "$classes\InprocServer32").GetValue('')
    if ($registered -ne (Join-Path $install 'AudioSwitch.LoginProvider.dll')) { throw 'COM path is not protected installation directory' }
    $after = @(Get-ChildItem $providers | Where-Object PSChildName -ne $guid | Select-Object -ExpandProperty PSChildName | Sort-Object)
    if (Compare-Object $before $after) { throw 'Existing system provider registrations changed' }
    & $test $registered
    if ($LASTEXITCODE -ne 0) { throw 'Installed provider loading failed' }
    & $test $registered '--vault-tests'
    if ($LASTEXITCODE -ne 0) { throw 'Protected vault and automatic policy checks failed' }
    Add-Content (Join-Path $PSScriptRoot '../artifacts/login-component-checks.txt') 'DPAPI vault roundtrip, restricted-token read denial, tamper rejection, identity mismatch, revocation and automatic single-attempt policy passed with fictional credentials.'
    # A synthetic admin-created binding cannot turn a regular process into LogonUI.
    $binding = 'HKLM:\SOFTWARE\AudioSwitch\LoginBridge\Bindings\S-1-5-21-100-200-300-1001'
    try {
        New-Item $binding -Force | Out-Null
        & $test $registered '--deny-bound'
        if ($LASTEXITCODE -ne 0) { throw 'Missing phone value bypassed the phone gate' }
        New-ItemProperty $binding -Name Phone -Value '123456789ABC' -PropertyType String -Force | Out-Null
        Set-ItemProperty $binding -Name Automatic -Value 1
        & $test $registered '--deny-bound'
        if ($LASTEXITCODE -ne 0) { throw 'Broker allowed a non-LogonUI caller' }
        Write-Host "Synthetic registry phone: $((Get-ItemProperty $binding).Phone)"
        & $test $registered '--check-binding'
        if ($LASTEXITCODE -ne 0) { throw 'Native fixture binding is not readable before the launch test' }
        $probeStart = [Diagnostics.ProcessStartInfo]::new((Join-Path $install 'AudioSwitch.LoginBridge.exe'))
        $probeStart.UseShellExecute = $true
        $probeStart.Verb = 'runas'
        $probeStart.WindowStyle = 'Hidden'
        $probeStart.ArgumentList.Add('--probe-service')
        $probeStart.ArgumentList.Add('S-1-5-21-100-200-300-1001')
        $probeProcess = [Diagnostics.Process]::Start($probeStart)
        $probeProcess.WaitForExit()
        if ($probeProcess.ExitCode -ne 5) { throw "Desktop service probe did not reach the SID gate: $($probeProcess.ExitCode)" }
        $probeProcess.Dispose()
        if ((Get-ItemProperty $binding).Automatic -ne 1) { throw 'Service probe changed automatic enrollment' }
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
