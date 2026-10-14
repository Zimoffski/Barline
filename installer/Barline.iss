#define AppName "Barline"
#ifndef AppVersion
  #error AppVersion is passed by scripts\Build-Release.ps1 from Barline.csproj.
#endif
#define AppPublisher "Zimoffski"
#define AppExecutable "Barline.exe"
#define PawnIOVersion "2.2.0"
#define PawnIOInstaller "PawnIO_setup.exe"

[Setup]
AppId={{BC8A7C84-58E7-4927-A48E-37718130DE51}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright=© 2026 {#AppPublisher}
AppComments=Компактный монитор CPU, GPU, RAM, FPS и сети для панели задач Windows.
DefaultDirName={autopf}\{#AppName}
; Upgrades from TaskbarMetrics keep the AppId but move to the new folder.
UsePreviousAppDir=no
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
PrivilegesRequired=admin
OutputDir=..
OutputBaseFilename={#AppName}-{#AppVersion}-Setup
SetupIconFile=..\Assets\Barline.ico
UninstallDisplayIcon={app}\{#AppExecutable}
Compression=lzma2/max
SolidCompression=yes
; Windows 11 look that follows the system light/dark theme, without the classic bevels.
WizardStyle=modern dynamic windows11 hidebevels
; Logo in the page header and on the finish page, a file for every DPI. The images are
; transparent, so they suit both the light and the dark appearance.
WizardSmallImageFile=images\wizard-small-*.png
WizardImageFile=images\wizard-large-*.png
; One checkbox does not need the default 120 % window.
WizardSizePercent=100
; Two steps: options, then progress and finish. Folder and "ready" pages add nothing here.
DisableWelcomePage=yes
DisableDirPage=yes
DisableReadyPage=yes
; The language is taken from Windows; the dialog appears only when it cannot be matched.
ShowLanguageDialog=auto
; PrepareToInstall stops the running monitor (and the former TaskbarMetrics) itself before any
; file is touched, so the Restart Manager page "applications are using files" is not needed.
CloseApplications=no
RestartApplications=no
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Установщик {#AppName}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIconTask}"; Flags: checkedonce

[Messages]
; Short wording instead of the generic wizard texts.
russian.WizardSelectTasks=Параметры
russian.SelectTasksDesc=Перед установкой Barline
russian.SelectTasksLabel2=Barline показывает загрузку CPU, GPU и памяти, сеть и FPS прямо на панели задач.
russian.FinishedHeadingLabel=Barline установлен
russian.FinishedLabelNoIcons=Строка мониторинга появится на панели задач рядом с треем. Управление — через значок в трее.
russian.FinishedLabel=Строка мониторинга появится на панели задач рядом с треем. Управление — через значок в трее.
russian.ClickFinish=
english.WizardSelectTasks=Options
english.SelectTasksDesc=Before installing Barline
english.SelectTasksLabel2=Barline shows CPU, GPU and memory load, network and FPS right on the taskbar.
english.FinishedHeadingLabel=Barline is installed
english.FinishedLabelNoIcons=The monitoring strip appears on the taskbar next to the tray. Use the tray icon to control it.
english.FinishedLabel=The monitoring strip appears on the taskbar next to the tray. Use the tray icon to control it.
english.ClickFinish=

[CustomMessages]
russian.DesktopIconTask=Создать ярлык на рабочем столе
english.DesktopIconTask=Create a desktop shortcut
russian.PawnIOPromptTitle=Доступ к температуре CPU
russian.PawnIOPromptText=Для отображения температуры CPU требуется официальный системный драйвер PawnIO {#PawnIOVersion}.%n%nУстановить его сейчас?%n%nДрайвер обеспечивает низкоуровневый доступ к аппаратным датчикам. Если выбрать «Нет», Barline всё равно установится и будет работать, но температура CPU может не отображаться.
russian.PawnIOInstallFailed=Не удалось установить драйвер PawnIO (код ошибки: %1).%n%nУстановка Barline продолжится, но температура CPU может не отображаться. Драйвер можно установить позже.
english.PawnIOPromptTitle=CPU temperature access
english.PawnIOPromptText=Displaying the CPU temperature requires the official PawnIO {#PawnIOVersion} system driver.%n%nInstall it now?%n%nThe driver provides low-level access to hardware sensors. If you choose No, Barline will still be installed and work, but the CPU temperature may be unavailable.
english.PawnIOInstallFailed=PawnIO could not be installed (error code: %1).%n%nBarline installation will continue, but the CPU temperature may be unavailable. You can install the driver later.

[Files]
Source: "dependencies\{#PawnIOInstaller}"; Flags: dontcopy
; The whole self-contained publish folder (exe, .NET runtime, WPF and PresentMon). It is not a
; single-file bundle on purpose: see Properties/PublishProfiles/Windows-x64.pubxml.
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\licenses\*.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExecutable}"; Parameters: "--configure-sensor-access"; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExecutable}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent; Check: CanLaunchBarline

[InstallDelete]
; Leftovers of the former name TaskbarMetrics (up to 3.2.1).
Type: filesandordirs; Name: "{autopf}\TaskbarMetrics"
Type: files; Name: "{autoprograms}\TaskbarMetrics.lnk"
Type: files; Name: "{autodesktop}\TaskbarMetrics.lnk"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\logs"
Type: filesandordirs; Name: "{localappdata}\{#AppName}"

[UninstallRun]
Filename: "{app}\{#AppExecutable}"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "BarlineCleanup"

[Code]
const
  PawnIOUninstallKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO';
  PawnIOServiceKey = 'SYSTEM\CurrentControlSet\Services\PawnIO';

var
  PawnIODecisionMade: Boolean;
  PawnIOInstallRequested: Boolean;
  PawnIOInstallAttempted: Boolean;
  PawnIORestartRequired: Boolean;

function PawnIORequiresInstall: Boolean;
var
  InstalledVersionText: String;
  InstalledVersion: Int64;
  RequiredVersion: Int64;
begin
  if not RegKeyExists(HKLM64, PawnIOServiceKey) then
  begin
    Result := True;
    Exit;
  end;

  if RegQueryStringValue(HKLM64, PawnIOUninstallKey, 'DisplayVersion',
      InstalledVersionText) and
      StrToVersion(InstalledVersionText, InstalledVersion) then
  begin
    RequiredVersion := PackVersionComponents(2, 2, 0, 0);
    Result := ComparePackedVersion(InstalledVersion, RequiredVersion) < 0;
    Exit;
  end;

  { A service without version metadata is treated as an existing installation. }
  Result := False;
end;

procedure ChoosePawnIOInstallation;
begin
  if PawnIODecisionMade then
    Exit;

  PawnIODecisionMade := True;
  PawnIOInstallRequested := False;

  if not PawnIORequiresInstall then
  begin
    Log('PawnIO 2.2.0 or newer is already installed.');
    Exit;
  end;

  { A driver is never installed silently without an explicit user decision. }
  if WizardSilent then
  begin
    Log('Silent setup: PawnIO installation was skipped because consent cannot be requested.');
    Exit;
  end;

  PawnIOInstallRequested :=
    TaskDialogMsgBox(CustomMessage('PawnIOPromptTitle'),
      CustomMessage('PawnIOPromptText'), mbConfirmation, MB_YESNO, [], IDYES) = IDYES;

  if PawnIOInstallRequested then
    Log('The user approved PawnIO installation.')
  else
    Log('The user declined PawnIO installation.');
end;

procedure InstallPawnIO;
var
  ResultCode: Integer;
begin
  if not PawnIOInstallRequested or PawnIOInstallAttempted then
    Exit;

  PawnIOInstallAttempted := True;
  ExtractTemporaryFile('{#PawnIOInstaller}');

  if not Exec(ExpandConstant('{tmp}\{#PawnIOInstaller}'), '-install -silent',
      ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;

  if ResultCode = 0 then
    Log('PawnIO installation completed successfully.')
  else if ResultCode = 3010 then
  begin
    PawnIORestartRequired := True;
    Log('PawnIO installation completed and requires a restart.');
  end
  else
  begin
    Log(Format('PawnIO installation failed with exit code %d.', [ResultCode]));
    MsgBox(FmtMessage(CustomMessage('PawnIOInstallFailed'), [IntToStr(ResultCode)]),
      mbError, MB_OK);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  ChoosePawnIOInstallation;

  Exec(ExpandConstant('{sys}\schtasks.exe'),
    '/End /TN "Barline sensor access"', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'),
    '/F /T /IM Barline.exe', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
  { A forced stop leaves the FPS trace session running; close it explicitly. }
  Exec(ExpandConstant('{sys}\logman.exe'),
    'stop BarlineFPS -ets', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);

  { The same for the former name TaskbarMetrics (up to 3.2.1). The old logon task is
    deleted later by the program's elevated configuration step. }
  Exec(ExpandConstant('{sys}\schtasks.exe'),
    '/End /TN "TaskbarMetrics sensor access"', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}	askkill.exe'),
    '/F /T /IM TaskbarMetrics.exe', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\logman.exe'),
    'stop TaskbarMetricsFPS -ets', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);

  InstallPawnIO;
  Result := '';
end;

function NeedRestart: Boolean;
begin
  Result := PawnIORestartRequired;
end;

function CanLaunchBarline: Boolean;
begin
  Result := not PawnIORestartRequired;
end;
