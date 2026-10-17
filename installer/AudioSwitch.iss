#ifndef Runtime
  #define Runtime "win-x64"
#endif
#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\" + Runtime
#endif

[Setup]
AppId=AudioSwitch
AppName=AudioSwitch 音频切换
AppVersion={#AppVersion}
AppPublisher=wuha-like-sleep
DefaultDirName={localappdata}\Programs\AudioSwitch
DefaultGroupName=AudioSwitch
PrivilegesRequired=lowest
MinVersion=10.0.19045
#if Runtime == "win-x64"
ArchitecturesAllowed=x64compatible
#elif Runtime == "win-arm64"
ArchitecturesAllowed=arm64
#endif
OutputDir=..\artifacts\installer
OutputBaseFilename=AudioSwitch-Setup-{#Runtime}
SetupIconFile=..\src\AudioSwitch\Assets\app.ico
UninstallDisplayIcon={app}\AudioSwitch.exe
LicenseFile=..\LICENSE
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
AppMutex=Local\AudioSwitch.Tray
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes
UninstallDisplayName=AudioSwitch 音频切换

[Languages]
Name: "chinese"; MessagesFile: "compiler:Default.isl"

[LangOptions]
LanguageName=简体中文
LanguageID=$0804

[Messages]
SetupWindowTitle=安装 — AudioSwitch 音频切换
WelcomeLabel1=让声音切换，变得简单。
WelcomeLabel2=即将安装 AudioSwitch，在 USB 耳机与音箱之间随手切换。%n%n点击“下一步”开始安装。
ButtonBack=< 上一步(&B)
ButtonNext=下一步(&N) >
ButtonInstall=安装(&I)
ButtonCancel=取消
ButtonFinish=完成(&F)
ButtonBrowse=浏览(&B)...
SelectDirLabel3=选择 AudioSwitch 的安装位置。
SelectDirDesc=将 AudioSwitch 安装到哪里？
SelectTasksLabel2=按你的使用习惯选择以下选项，然后点击“下一步”。
SelectTasksDesc=启动与快捷方式
ReadyLabel1=准备就绪
ReadyLabel2a=点击“安装”完成设置。
InstallingLabel=正在安装 AudioSwitch，请稍候…
FinishedHeadingLabel=一切就绪。
FinishedLabelNoIcons=AudioSwitch 已安装。点击“完成”，开始设置你的耳机和音箱。
FinishedLabel=AudioSwitch 已安装。点击“完成”，开始设置你的耳机和音箱。
UninstallAppFullTitle=卸载 AudioSwitch
ConfirmUninstall=确定卸载 AudioSwitch？设备配置将保留，便于下次安装。

[Tasks]
Name: "startup"; Description: "登录 Windows 后自动启动"; GroupDescription: "启动选项："
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\AudioSwitch"; Filename: "{app}\AudioSwitch.exe"
Name: "{userdesktop}\AudioSwitch"; Filename: "{app}\AudioSwitch.exe"; Tasks: desktopicon

[Registry]
; Always record cleanup, including when startup is enabled later from the app.
Root: HKCU32; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "AudioSwitch"; Flags: uninsdeletevalue
Root: HKCU32; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AudioSwitch"; ValueData: """{app}\AudioSwitch.exe"" --startup"; Tasks: startup
Root: HKCU32; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "AudioSwitch"; Flags: deletevalue; Tasks: not startup

[Run]
Filename: "{app}\AudioSwitch.exe"; Description: "启动 AudioSwitch 并选择设备"; Flags: nowait postinstall skipifsilent
