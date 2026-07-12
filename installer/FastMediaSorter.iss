; FastMediaSorter LITE - Inno Setup script
; Built by CI; invoke with:
;   ISCC.exe /DVersion=<x.y.z.w> /DSourceDir=<staged-tree> /O<output-dir> FastMediaSorter.iss

#ifndef Version
  #define Version "0.0.0.0"
#endif

#ifndef SourceDir
  #error SourceDir must be defined (path to the staged build output)
#endif

; Display name shown throughout the wizard screens, Start-menu shortcuts and the
; setup.exe file properties. The channel/identity name below is kept separate on
; purpose (light rebrand - see SPECIFICATION_RENAME_FAST_MEDIA_SORTER_FOR_WINDOWS.md).
#define AppName       "Fast Media Sorter for Windows"
; FROZEN identity name for the "Add/Remove Programs" (ARP) entry. winget and the
; Store correlate the installed app to their manifests by this ARP DisplayName,
; so it must stay "FastMediaSorter LITE" even though the wizard now shows the new
; display name. Pinned via UninstallDisplayName below - NEVER change it.
#define AppNameArp    "FastMediaSorter LITE"
#define AppPublisher  "SerZhyAle"
#define AppExeName    "FastMediaSorter_LITE.exe"
#define AppURL        "https://github.com/SerZhyAle/FastMediaSorter_Lite"

[Setup]
AppId={{7371E7F1-B8A8-4786-8173-5F5B2B6E6AC9}
AppName={#AppName}
AppVersion={#Version}
AppVerName={#AppName} {#Version}
; Pin the ARP DisplayName to the frozen channel name (byte-identical to the value
; AppVerName produced before this rename) so winget-upgrade correlation and the
; Store listing stay intact while every wizard screen shows the new display name.
UninstallDisplayName={#AppNameArp} {#Version}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\FastMediaSorter_LITE
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile={#SourceDir}\LICENSE
OutputBaseFilename=FastMediaSorter-{#Version}-windows-x64-setup
SetupIconFile=..\assets\icons\Fast_Media_Sorter.ico
UninstallDisplayIcon={app}\{#AppExeName}
; Published artifacts use max ratio (lzma2/ultra). Local convenience builds can
; pass /DFastCompression for a much faster compile at the cost of a larger file -
; only the compression setting changes, every install-identity anchor is untouched.
#ifdef FastCompression
Compression=lzma2/fast
SolidCompression=no
#else
Compression=lzma2/ultra
SolidCompression=yes
#endif
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
; Installing/uninstalling is at minimum a version replacement - an already-running
; instance (possibly tray-resident, so its window may be hidden) must not be left
; holding the old exe open. Setup/Uninstall detect it via this single-instance
; mutex (Main_Form.vb) and close it before touching files (see also
; StopCompanionWorker in [Code] for the separate companion worker process, which
; this mutex cannot see).
AppMutex=FastMediaSorterSingleInstanceMutex
ChangesAssociations=yes
MinVersion=6.1
VersionInfoVersion={#Version}
VersionInfoCompany={#AppPublisher}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#Version}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
; Helper Setup/Uninstall use to gracefully stop a running companion worker (see
; StopCompanionWorker in [Code]). Kept both as a dontcopy temp extract (Setup runs
; this before any app files exist yet, so it cannot read one from {app}) and as a
; normal installed file (Uninstall only has access to already-installed files).
Source: "stop-companion.ps1"; DestDir: "{tmp}"; Flags: dontcopy
Source: "stop-companion.ps1"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; The Start-menu group used to be named "FastMediaSorter LITE"; it is now the new
; display name. Remove the stale folder on upgrade so a user is not left with two
; Start-menu folders. The install dir, exe, AppId and ARP name are unchanged, so
; this leftover shortcut folder is the only thing to clean.
Type: filesandordirs; Name: "{autoprograms}\FastMediaSorter LITE"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\FastMediaSorter_LITE"

[Code]
const
  CompanionProjectUrl = 'https://github.com/SerZhyAle/doc-html-translate';
  CompanionWingetId = 'SerZhyAle.DocHtmlTranslate';
  CompanionWingetCommand = 'winget install ' + CompanionWingetId;

var
  InstallOptionsPage: TWizardPage;
  DefaultViewerPromptLabel: TNewStaticText;
  RegisterAssociationsCheckBox: TNewCheckBox;
  AssociationsHintLabel: TNewStaticText;
  CompanionTitleLabel: TNewStaticText;
  CompanionBodyLabel: TNewStaticText;
  CompanionCommandLabel: TNewStaticText;
  CompanionProjectButton: TNewButton;

function IsLanguage(const Lang: String): Boolean;
begin
  Result := CompareText(ActiveLanguage, Lang) = 0;
end;

function OptionsPageTitleText: String;
begin
  if IsLanguage('russian') then
    Result := 'Параметры установки'
  else if IsLanguage('ukrainian') then
    Result := 'Параметри встановлення'
  else
    Result := 'Installation options';
end;

function OptionsPageDescriptionText: String;
begin
  if IsLanguage('russian') then
    Result := 'Выберите, нужно ли зарегистрировать {#AppName} для изображений и обратите внимание на рекомендуемый companion-проект.'
  else if IsLanguage('ukrainian') then
    Result := 'Виберіть, чи потрібно зареєструвати {#AppName} для зображень, і зверніть увагу на рекомендований companion-проєкт.'
  else
    Result := 'Choose whether {#AppName} should register for image files, and see the recommended companion project.';
end;

function DefaultViewerPromptText: String;
begin
  if IsLanguage('russian') then
    Result := 'Сделать {#AppName} просмотрщиком изображений по умолчанию?'
  else if IsLanguage('ukrainian') then
    Result := 'Зробити {#AppName} типовим переглядачем зображень?'
  else
    Result := 'Make {#AppName} the default viewer for common image formats?';
end;

function RegisterAssociationsText: String;
begin
  if IsLanguage('russian') then
    Result := 'Да, зарегистрировать для JPG, PNG, GIF, BMP, TIFF, WEBP, HEIC, AVIF и SVG'
  else if IsLanguage('ukrainian') then
    Result := 'Так, зареєструвати для JPG, PNG, GIF, BMP, TIFF, WEBP, HEIC, AVIF і SVG'
  else
    Result := 'Yes, register FastMediaSorter for JPG, PNG, GIF, BMP, TIFF, WEBP, HEIC, AVIF, and SVG';
end;

function AssociationsHintText: String;
begin
  if IsLanguage('russian') then
    Result := 'На Windows 10/11 система может дополнительно попросить подтвердить выбор в разделе "Приложения по умолчанию". Тихая установка winget этот шаг пропускает.'
  else if IsLanguage('ukrainian') then
    Result := 'На Windows 10/11 система може додатково попросити підтвердити вибір у розділі "Типові програми". Тиха установка winget цей крок пропускає.'
  else
    Result := 'Windows 10/11 may still ask you to confirm the choice once in Default Apps. Silent winget installs skip this step.';
end;

function CompanionTitleText: String;
begin
  if IsLanguage('russian') then
    Result := 'Рекомендуемый проект: doc-html-translate'
  else if IsLanguage('ukrainian') then
    Result := 'Рекомендований проєкт: doc-html-translate'
  else
    Result := 'Recommended companion app: doc-html-translate';
end;

function CompanionBodyText: String;
begin
  if IsLanguage('russian') then
    Result := 'Если вам нужно переводить EPUB, PDF, FB2, MOBI, TXT или HTML в локальный HTML для чтения, установите companion-проект через winget или откройте страницу проекта.'
  else if IsLanguage('ukrainian') then
    Result := 'Якщо вам потрібно переводити EPUB, PDF, FB2, MOBI, TXT або HTML у локальний HTML для читання, встановіть companion-проєкт через winget або відкрийте сторінку проєкту.'
  else
    Result := 'If you need to convert EPUB, PDF, FB2, MOBI, TXT, or HTML documents into clean local HTML for reading, install the companion project via winget or open its project page.';
end;

function OpenProjectButtonText: String;
begin
  if IsLanguage('russian') then
    Result := 'Открыть страницу проекта'
  else if IsLanguage('ukrainian') then
    Result := 'Відкрити сторінку проєкту'
  else
    Result := 'Open project page';
end;

function AssociationWriteErrorText: String;
begin
  if IsLanguage('russian') then
    Result := 'Не удалось полностью зарегистрировать {#AppName} для всех форматов изображений.'
  else if IsLanguage('ukrainian') then
    Result := 'Не вдалося повністю зареєструвати {#AppName} для всіх форматів зображень.'
  else
    Result := '{#AppName} could not be fully registered for all image formats.';
end;

function BuildAppCommand: String;
begin
  Result := AddQuotes(ExpandConstant('{app}\{#AppExeName}')) + ' "%1"';
end;

function BuildAppIcon: String;
begin
  Result := AddQuotes(ExpandConstant('{app}\{#AppExeName}')) + ',0';
end;

procedure ConfigureWrappedLabel(LabelControl: TNewStaticText; ATop: Integer; const ACaption: String; ABold: Boolean);
begin
  LabelControl.Parent := InstallOptionsPage.Surface;
  LabelControl.Left := 0;
  LabelControl.Top := ATop;
  LabelControl.Width := InstallOptionsPage.SurfaceWidth;
  LabelControl.AutoSize := False;
  LabelControl.WordWrap := True;
  LabelControl.Caption := ACaption;
  if ABold then
    LabelControl.Font.Style := [fsBold];
  WizardForm.AdjustLabelHeight(LabelControl);
end;

procedure OpenCompanionProject(Sender: TObject);
var
  ResultCode: Integer;
begin
  ShellExec('open', CompanionProjectUrl, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

function ShouldRegisterAssociations: Boolean;
begin
  Result := (RegisterAssociationsCheckBox <> nil) and RegisterAssociationsCheckBox.Checked;
end;

procedure RegisterOpenWithSupport(const Ext: String);
begin
  RegWriteStringValue(HKCU, 'Software\Classes\Applications\{#AppExeName}', 'FriendlyAppName', '{#AppName}');
  RegWriteStringValue(HKCU, 'Software\Classes\Applications\{#AppExeName}\DefaultIcon', '', BuildAppIcon);
  RegWriteStringValue(HKCU, 'Software\Classes\Applications\{#AppExeName}\shell\open\command', '', BuildAppCommand);
  RegWriteStringValue(HKCU, 'Software\Classes\Applications\{#AppExeName}\SupportedTypes', Ext, '');
  RegWriteStringValue(HKCU, 'Software\Classes\' + Ext + '\OpenWithProgids', 'FastMediaSorter.' + Copy(Ext, 2, MaxInt), '');
end;

function RegisterImageAssociation(const Ext, ProgId, Description: String): Boolean;
begin
  Result :=
    RegWriteStringValue(HKCU, 'Software\Classes\' + ProgId, '', Description) and
    RegWriteStringValue(HKCU, 'Software\Classes\' + ProgId + '\DefaultIcon', '', BuildAppIcon) and
    RegWriteStringValue(HKCU, 'Software\Classes\' + ProgId + '\shell\open\command', '', BuildAppCommand) and
    RegWriteStringValue(HKCU, 'Software\Classes\' + Ext, '', ProgId);
  RegisterOpenWithSupport(Ext);
end;

procedure RegisterRequestedImageAssociations;
var
  FailedCount: Integer;
begin
  FailedCount := 0;

  if not RegisterImageAssociation('.jpg', 'FastMediaSorter.jpg', 'JPEG Image - FastMediaSorter') then FailedCount := FailedCount + 1;
  if not RegisterImageAssociation('.jpeg', 'FastMediaSorter.jpeg', 'JPEG Image - FastMediaSorter') then FailedCount := FailedCount + 1;
  if not RegisterImageAssociation('.gif', 'FastMediaSorter.gif', 'GIF Image - FastMediaSorter') then FailedCount := FailedCount + 1;
  if not RegisterImageAssociation('.png', 'FastMediaSorter.png', 'PNG Image - FastMediaSorter') then FailedCount := FailedCount + 1;
  if not RegisterImageAssociation('.bmp', 'FastMediaSorter.bmp', 'BMP Image - FastMediaSorter') then FailedCount := FailedCount + 1;
  if not RegisterImageAssociation('.tiff', 'FastMediaSorter.tiff', 'TIFF Image - FastMediaSorter') then FailedCount := FailedCount + 1;
  if not RegisterImageAssociation('.ico', 'FastMediaSorter.ico', 'ICO Image - FastMediaSorter') then FailedCount := FailedCount + 1;
  if not RegisterImageAssociation('.wmf', 'FastMediaSorter.wmf', 'WMF Image - FastMediaSorter') then FailedCount := FailedCount + 1;
  if not RegisterImageAssociation('.emf', 'FastMediaSorter.emf', 'EMF Image - FastMediaSorter') then FailedCount := FailedCount + 1;
  if not RegisterImageAssociation('.exif', 'FastMediaSorter.exif', 'EXIF Image - FastMediaSorter') then FailedCount := FailedCount + 1;
  if not RegisterImageAssociation('.webp', 'FastMediaSorter.webp', 'WEBP Image - FastMediaSorter') then FailedCount := FailedCount + 1;
  if not RegisterImageAssociation('.heic', 'FastMediaSorter.heic', 'HEIC Image - FastMediaSorter') then FailedCount := FailedCount + 1;
  if not RegisterImageAssociation('.avif', 'FastMediaSorter.avif', 'AVIF Image - FastMediaSorter') then FailedCount := FailedCount + 1;
  if not RegisterImageAssociation('.svg', 'FastMediaSorter.svg', 'SVG Image - FastMediaSorter') then FailedCount := FailedCount + 1;

  if FailedCount > 0 then
    SuppressibleMsgBox(AssociationWriteErrorText, mbError, MB_OK, IDOK);
end;

procedure RemoveImageAssociation(const Ext, ProgId: String);
var
  CurrentProgId: String;
begin
  if RegQueryStringValue(HKCU, 'Software\Classes\' + Ext, '', CurrentProgId) then
    if CompareText(CurrentProgId, ProgId) = 0 then
      RegDeleteValue(HKCU, 'Software\Classes\' + Ext, '');

  RegDeleteValue(HKCU, 'Software\Classes\' + Ext + '\OpenWithProgids', ProgId);
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\' + ProgId);
end;

procedure InitializeWizard;
begin
  InstallOptionsPage := CreateCustomPage(
    wpSelectTasks,
    OptionsPageTitleText,
    OptionsPageDescriptionText
  );

  DefaultViewerPromptLabel := TNewStaticText.Create(WizardForm);
  ConfigureWrappedLabel(DefaultViewerPromptLabel, 0, DefaultViewerPromptText, True);

  RegisterAssociationsCheckBox := TNewCheckBox.Create(WizardForm);
  RegisterAssociationsCheckBox.Parent := InstallOptionsPage.Surface;
  RegisterAssociationsCheckBox.Left := 0;
  RegisterAssociationsCheckBox.Top := DefaultViewerPromptLabel.Top + DefaultViewerPromptLabel.Height + ScaleY(6);
  RegisterAssociationsCheckBox.Width := InstallOptionsPage.SurfaceWidth;
  RegisterAssociationsCheckBox.Height := ScaleY(32);
  RegisterAssociationsCheckBox.Checked := False;
  RegisterAssociationsCheckBox.Caption := RegisterAssociationsText;

  AssociationsHintLabel := TNewStaticText.Create(WizardForm);
  ConfigureWrappedLabel(
    AssociationsHintLabel,
    RegisterAssociationsCheckBox.Top + RegisterAssociationsCheckBox.Height + ScaleY(4),
    AssociationsHintText,
    False
  );

  CompanionTitleLabel := TNewStaticText.Create(WizardForm);
  ConfigureWrappedLabel(
    CompanionTitleLabel,
    AssociationsHintLabel.Top + AssociationsHintLabel.Height + ScaleY(18),
    CompanionTitleText,
    True
  );

  CompanionBodyLabel := TNewStaticText.Create(WizardForm);
  ConfigureWrappedLabel(
    CompanionBodyLabel,
    CompanionTitleLabel.Top + CompanionTitleLabel.Height + ScaleY(6),
    CompanionBodyText,
    False
  );

  CompanionCommandLabel := TNewStaticText.Create(WizardForm);
  ConfigureWrappedLabel(
    CompanionCommandLabel,
    CompanionBodyLabel.Top + CompanionBodyLabel.Height + ScaleY(8),
    CompanionWingetCommand,
    True
  );
  CompanionCommandLabel.Font.Name := 'Consolas';

  CompanionProjectButton := TNewButton.Create(WizardForm);
  CompanionProjectButton.Parent := InstallOptionsPage.Surface;
  CompanionProjectButton.Left := 0;
  CompanionProjectButton.Top := CompanionCommandLabel.Top + CompanionCommandLabel.Height + ScaleY(10);
  CompanionProjectButton.Width := ScaleX(180);
  CompanionProjectButton.Height := ScaleY(26);
  CompanionProjectButton.Caption := OpenProjectButtonText;
  CompanionProjectButton.OnClick := @OpenCompanionProject;
end;

{ Stops a running companion worker (fms-share-worker.exe) before Setup/Uninstall
  touch its exe - it is a separate background process (survives the main app
  closing by design) that the AppMutex check above cannot see. Runs the bundled
  helper script, which asks the worker to stop over its control pipe (releases
  the SFTP server + UPnP port mapping cleanly) before force-killing any
  survivor. Best-effort: a missing or failing script must never abort Setup. }
procedure StopCompanionWorker(const ScriptPath: String);
var
  ResultCode: Integer;
begin
  if not FileExists(ScriptPath) then
    exit;
  Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  ExtractTemporaryFile('stop-companion.ps1');
  StopCompanionWorker(ExpandConstant('{tmp}\stop-companion.ps1'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and ShouldRegisterAssociations then
    RegisterRequestedImageAssociations;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usUninstall then
    exit;

  StopCompanionWorker(ExpandConstant('{app}\stop-companion.ps1'));

  RemoveImageAssociation('.jpg', 'FastMediaSorter.jpg');
  RemoveImageAssociation('.jpeg', 'FastMediaSorter.jpeg');
  RemoveImageAssociation('.gif', 'FastMediaSorter.gif');
  RemoveImageAssociation('.png', 'FastMediaSorter.png');
  RemoveImageAssociation('.bmp', 'FastMediaSorter.bmp');
  RemoveImageAssociation('.tiff', 'FastMediaSorter.tiff');
  RemoveImageAssociation('.ico', 'FastMediaSorter.ico');
  RemoveImageAssociation('.wmf', 'FastMediaSorter.wmf');
  RemoveImageAssociation('.emf', 'FastMediaSorter.emf');
  RemoveImageAssociation('.exif', 'FastMediaSorter.exif');
  RemoveImageAssociation('.webp', 'FastMediaSorter.webp');
  RemoveImageAssociation('.heic', 'FastMediaSorter.heic');
  RemoveImageAssociation('.avif', 'FastMediaSorter.avif');
  RemoveImageAssociation('.svg', 'FastMediaSorter.svg');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Applications\{#AppExeName}');
end;
