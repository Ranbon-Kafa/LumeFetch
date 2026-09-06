#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #error PublishDir must point to the self-contained publish directory
#endif
#ifndef ArtifactsDir
  #error ArtifactsDir must point to the artifact directory
#endif

[Setup]
AppId={{8E531BAA-09C0-4376-B5CE-4B021991669C}
AppName=LumeFetch
AppVersion={#AppVersion}
AppPublisher=ruzgarefe.com
AppPublisherURL=https://ruzgarefe.com
AppSupportURL=https://github.com/Ranbon-Kafa/LumeFetch/issues
AppUpdatesURL=https://github.com/Ranbon-Kafa/LumeFetch/releases
DefaultDirName={localappdata}\Programs\LumeFetch
DefaultGroupName=LumeFetch
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
OutputDir={#ArtifactsDir}
OutputBaseFilename=LumeFetch-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\..\src\LumeFetch.Desktop\Assets\lumefetch.ico
LicenseFile=..\..\LICENSE
UninstallDisplayIcon={app}\LumeFetch.exe
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "portable.flag,data\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\LumeFetch"; Filename: "{app}\LumeFetch.exe"
Name: "{autodesktop}\LumeFetch"; Filename: "{app}\LumeFetch.exe"; Tasks: desktopicon

; User downloads and settings are deliberately NOT removed by the uninstaller.
