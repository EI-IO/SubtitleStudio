; Subtitle Studio 安装程序（Inno Setup 6.5+）
; 用法：先运行 .\publish.ps1 生成发布目录，再运行 .\installer.ps1 [-Mode slim]
; 也可直接：iscc.exe /DAppVersion=1.1.0 installer.iss
;
; 完整版与 slim 版共用同一个 AppId（升级/卸载识别码），因此两者可以互相覆盖升级。

#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif

#ifndef SourceDir
  #define SourceDir "dist\SubtitleStudio"
#endif

#ifndef OutputBase
  #define OutputBase "SubtitleStudio-Setup-" + AppVersion
#endif

#define AppName "Subtitle Studio"
#define AppExeName "SubtitleStudio.exe"
#define AppPublisher "Subtitle Studio"

[Setup]
; AppId 是升级/卸载的识别码，发布后不要再改
AppId={{AE3C3FDD-5FB6-4DAA-940B-D30B4F8E3CC9}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
; slim 不带 .NET 运行时和 ffmpeg，安装说明里要写清楚额外依赖
#ifdef IsSlim
AppComments=Extract audio from video, create editable subtitles, and export alpha overlay videos or subtitle files. Requires .NET 10 Desktop Runtime; ffmpeg is downloaded on first launch.
#else
AppComments=Extract audio from video, create editable subtitles, and export alpha overlay videos or subtitle files
#endif
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\Subtitle Studio
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; 默认按当前用户安装，不弹 UAC；安装向导里也可以改成装给所有用户
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=dist
OutputBaseFilename={#OutputBase}
SetupIconFile=src\SubtitleStudio.App\Assets\icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "tools\setup\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; config\ 是运行期产生的用户设置（可能含 API Key），日志也不该分发；README 只随源码包走
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "config\*,*.log,README.md,README.zh-CN.md"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
