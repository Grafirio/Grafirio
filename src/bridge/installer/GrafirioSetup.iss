#if VER < EncodeVer(6, 7, 3)
  #error Inno Setup 6.7.3 or later is required.
#endif
#ifndef PublishDirectory
  #error PublishDirectory must point to the self-contained desktop publish output.
#endif
#ifndef WebView2Installer
  #error WebView2Installer must point to the verified offline x64 runtime installer.
#endif
#ifndef OutputDirectory
  #error OutputDirectory is required.
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppExecutable "GrafirioBridge.Desktop.exe"

[Setup]
AppId={{C594931A-BBE0-47CB-A88B-D6C024783C65}
AppName=Grafirio
AppVersion={#AppVersion}
AppPublisher=Grafirio
DefaultDirName={localappdata}\Programs\Grafirio
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.17763
OutputDir={#OutputDirectory}
OutputBaseFilename=GrafirioSetup
SetupIconFile=..\Grafirio.Bridge.Desktop\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExecutable}
UninstallDisplayName=Grafirio
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
#ifdef SigningEnabled
SignTool=grafirio
SignedUninstaller=yes
#endif

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: checkedonce
Name: "startup"; Description: "Start Grafirio when I sign in to Windows"; Flags: unchecked

[Files]
Source: "{#WebView2Installer}"; DestName: "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Flags: dontcopy
Source: "{#PublishDirectory}\*"; DestDir: "{app}"; Excludes: "*.pdb,appsettings*.json"; Flags: ignoreversion recursesubdirs createallsubdirs
; Existing local configuration belongs to the user, including during upgrades and uninstall.
Source: "{#PublishDirectory}\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall

[Icons]
Name: "{userprograms}\Grafirio"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"
Name: "{userdesktop}\Grafirio"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userstartup}\Grafirio"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"; Tasks: startup

[Run]
Filename: "{app}\{#AppExecutable}"; Description: "Launch Grafirio"; Flags: nowait postinstall skipifsilent runasoriginaluser

[InstallDelete]
Type: files; Name: "{userstartup}\Grafirio.lnk"; Check: not WizardIsTaskSelected('startup')
Type: files; Name: "{userdesktop}\Grafirio.lnk"; Check: not WizardIsTaskSelected('desktopicon')

[Code]
const
  WebView2ClientKey = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2FileName = 'MicrosoftEdgeWebView2RuntimeInstallerX64.exe';
  SuccessExitCode = 0;
  RebootRequiredExitCode = 3010;

function HasWebView2Version(RootKey: Integer): Boolean;
var
  Version: String;
  PackedVersion: Int64;
begin
  Result := False;
  if RegQueryStringValue(RootKey, WebView2ClientKey, 'pv', Version) then
    if StrToVersion(Version, PackedVersion) then
      Result := PackedVersion > 0;
end;

function IsWebView2Installed: Boolean;
begin
  Result := HasWebView2Version(HKCU32) or HasWebView2Version(HKLM32);
end;

function InitializeSetup: Boolean;
begin
  // An elevated parent would turn Microsoft's prerequisite into a machine-wide install.
  Result := not IsAdmin;
  if not Result then
    SuppressibleMsgBox('Run Grafirio Setup normally, not as administrator. This installer is for the current user only.',
      mbCriticalError, MB_OK, IDOK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if IsWebView2Installed then
    Exit;

  ExtractTemporaryFile(WebView2FileName);
  if not Exec(ExpandConstant('{tmp}\') + WebView2FileName, '/silent /install', '',
    SW_HIDE, ewWaitUntilTerminated, ExitCode) then
  begin
    Result := Format('Unable to start the offline WebView2 installer (error %d).', [ExitCode]);
    Exit;
  end;

  if (ExitCode <> SuccessExitCode) and (ExitCode <> RebootRequiredExitCode) then
  begin
    Result := Format('WebView2 installation failed (exit code %d). Grafirio has not been installed.', [ExitCode]);
    Exit;
  end;

  if ExitCode = RebootRequiredExitCode then
  begin
    NeedsRestart := True;
    Result := 'Restart Windows, then run Grafirio Setup again to finish installing WebView2.';
    Exit;
  end;

  if not IsWebView2Installed then
    Result := 'WebView2 Runtime was not detected after installation. Check your organization policies and run Setup again.';
end;