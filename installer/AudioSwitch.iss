#ifndef Runtime
  #define Runtime "win-x64"
#endif
#ifndef AppVersion
  #define AppVersion "0.2.1"
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
WizardStyle=modern light windows11 includetitlebar
WizardImageFile=assets\wizard.bmp
WizardSmallImageFile=assets\wizard-small.bmp
DisableWelcomePage=no
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
DialogFontName=Microsoft YaHei UI
DialogFontSize=9
WelcomeFontName=Microsoft YaHei UI
WelcomeFontSize=15

[Messages]
ClickNext=点击“下一步”继续，或点击“取消”退出。
SetupWindowTitle=安装 — AudioSwitch 音频切换
WelcomeLabel1=让声音切换，变得简单。
WelcomeLabel2=即将安装 AudioSwitch，在 USB 耳机与音箱之间随手切换。%n%n安装后可选择设备、自定义快捷键和设置自启动。
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
SetupAppTitle=安装
UninstallAppTitle=卸载
InformationTitle=提示
ButtonOK=确定
ButtonYes=是(&Y)
ButtonNo=否(&N)
ButtonWizardBrowse=浏览(&B)...
WizardLicense=开源许可证
LicenseLabel=AudioSwitch 使用 MIT 开源许可证。
LicenseLabel3=继续安装前，请阅读以下开源许可证。
LicenseAccepted=我接受许可证条款(&A)
LicenseNotAccepted=我不接受许可证条款(&D)
WizardSelectDir=安装位置
SelectDirBrowseLabel=点击“下一步”继续，或点击“浏览”选择其他位置。
WizardSelectTasks=启动与快捷方式
WizardReady=准备安装
ReadyMemoDir=安装位置：
ReadyMemoTasks=选中的选项：
WizardPreparing=准备安装
WizardInstalling=正在安装
StatusCreateDirs=正在创建目录…
StatusExtractFiles=正在复制程序文件…
StatusCreateIcons=正在创建快捷方式…
StatusCreateRegistryEntries=正在保存启动选项…
StatusSavingUninstall=正在配置卸载程序…
StatusRunProgram=正在完成安装…
StatusClosingApplications=正在关闭运行中的应用…
StatusRollback=正在撤销安装…
SetupAppRunningError=AudioSwitch 正在运行。%n%n请从托盘退出应用后，再继续安装。
UninstallAppRunningError=AudioSwitch 正在运行。%n%n请从托盘退出应用后，再继续卸载。
ExitSetupTitle=取消安装
ExitSetupMessage=安装尚未完成。确定退出？

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
