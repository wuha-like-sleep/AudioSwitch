#ifndef Runtime
  #define Runtime "win-x64"
#endif
#ifndef AppVersion
  #define AppVersion "0.3.4"
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
Source: "..\src\AudioSwitch\Assets\app.ico"; DestDir: "{app}"
Source: "..\native\LoginProvider\NOTICE.txt"; DestDir: "{app}"
Source: "..\LICENSE"; DestDir: "{app}"
[Registry]
Root: {#RegistryRoot}; Subkey: "Software\Classes\CLSID\{#ProviderId}"; ValueType: string; ValueData: "AudioSwitch Login Lab"; Flags: uninsdeletekey
Root: {#RegistryRoot}; Subkey: "Software\Classes\CLSID\{#ProviderId}\InprocServer32"; ValueType: string; ValueData: "{app}\AudioSwitch.LoginProvider.dll"
Root: {#RegistryRoot}; Subkey: "Software\Classes\CLSID\{#ProviderId}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
Root: {#RegistryRoot}; Subkey: "Software\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{#ProviderId}"; ValueType: string; ValueData: "AudioSwitch Login Lab"; Flags: uninsdeletekey
