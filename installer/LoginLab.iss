#ifndef Runtime
  #define Runtime "win-x64"
#endif
#ifndef AppVersion
  #define AppVersion "0.3.7"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\login-" + Runtime + "\Release"
#endif
#if Runtime == "win-x86"
  #define RegistryRoot "HKLM32"
#else
  #define RegistryRoot "HKLM64"
#endif
#define ProviderId "{{A3AC9286-65AB-4438-B97E-84A4C9A65320}"
[Setup]
AppId=AudioSwitch.LoginLab
AppName=AudioSwitch 登录实验组件
AppVersion={#AppVersion}
AppPublisher=wuha-like-sleep
DefaultDirName={autopf}\AudioSwitch Login Lab
UsePreviousAppDir=no
DisableDirPage=yes
PrivilegesRequired=admin
MinVersion=10.0.19045
#if Runtime == "win-x64"
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
#elif Runtime == "win-arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x86os
#endif
OutputDir=..\artifacts\installer
OutputBaseFilename=AudioSwitch-LoginLab-Setup-{#Runtime}
SetupIconFile=..\src\AudioSwitch\Assets\app.ico
UninstallDisplayIcon={app}\app.ico
UninstallDisplayName=AudioSwitch 登录实验组件
WizardStyle=modern light windows11 includetitlebar
WizardImageFile=assets\wizard.bmp
WizardSmallImageFile=assets\wizard-small.bmp
LicenseFile=..\LICENSE
InfoBeforeFile=..\native\LoginProvider\NOTICE.txt
DisableWelcomePage=no
DisableProgramGroupPage=yes
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
[Files]
Source: "{#SourceDir}\AudioSwitch.LoginProvider.dll"; DestDir: "{app}"; Flags: ignoreversion uninsrestartdelete
Source: "{#SourceDir}\AudioSwitch.LoginBridge.exe"; DestDir: "{app}"; Flags: ignoreversion uninsrestartdelete
Source: "..\src\AudioSwitch\Assets\app.ico"; DestDir: "{app}"
Source: "..\native\LoginProvider\NOTICE.txt"; DestDir: "{app}"
Source: "..\LICENSE"; DestDir: "{app}"
[Registry]
Root: {#RegistryRoot}; Subkey: "Software\Classes\CLSID\{#ProviderId}"; ValueType: string; ValueData: "AudioSwitch Login Lab"; Flags: uninsdeletekey
Root: {#RegistryRoot}; Subkey: "Software\Classes\CLSID\{#ProviderId}\InprocServer32"; ValueType: string; ValueData: "{app}\AudioSwitch.LoginProvider.dll"
Root: {#RegistryRoot}; Subkey: "Software\Classes\CLSID\{#ProviderId}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
Root: {#RegistryRoot}; Subkey: "Software\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{#ProviderId}"; ValueType: string; ValueData: "AudioSwitch Login Lab"; Flags: uninsdeletekey

Root: {#RegistryRoot}; Subkey: "Software\AudioSwitch\LoginBridge"; Flags: uninsdeletekey

[Code]
function BridgeCommand(Path, Args: String): Boolean;
var Code: Integer;
begin
  Result := Exec(Path, Args, '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
var Path: String;
begin
  Result := '';
  if CompareText(ExpandConstant('{app}'), ExpandConstant('{autopf}\AudioSwitch Login Lab')) <> 0 then begin
    Result := '登录实验组件必须安装到受保护的 Program Files 目录，不支持自定义位置。';
    Exit;
  end;
  Path := ExpandConstant('{app}\AudioSwitch.LoginBridge.exe');
  if FileExists(Path) and not BridgeCommand(Path, '--stop-service') then
    Result := '无法停止 AudioSwitch 登录桥接服务。请重启后重试。';
end;
procedure CurStepChanged(Step: TSetupStep);
begin
  if (Step = ssPostInstall) and not BridgeCommand(ExpandConstant('{app}\AudioSwitch.LoginBridge.exe'), '--install-service') then
    RaiseException('无法安装或启动 AudioSwitch 登录桥接服务。');
end;
procedure CurUninstallStepChanged(Step: TUninstallStep);
begin
  if (Step = usUninstall) and not BridgeCommand(ExpandConstant('{app}\AudioSwitch.LoginBridge.exe'), '--remove-service') then
    RaiseException('无法移除 AudioSwitch 登录桥接服务。请重启后重试卸载。');
end;
