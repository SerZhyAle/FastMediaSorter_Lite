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
; Elevated helper for the deferred, in-app "enable server features" opt-in (adds /
; removes the SFTP firewall rule via one UAC prompt). Installed next to the exe so
; ServerFeatures.EnableViaElevation prefers it over a direct netsh fallback.
Source: "enable-share-server.ps1"; DestDir: "{app}"; Flags: ignoreversion

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
; The server-features consent marker is written post-install (not tracked by
; [Files]), so remove it explicitly. The firewall rule itself is deleted in
; CurUninstallStepChanged below.
Type: files; Name: "{app}\companion\server-features.enabled"

[Code]
const
  CompanionProjectUrl = 'https://github.com/SerZhyAle/doc-html-translate';
  CompanionSiteUrl = 'https://serzhyale.github.io/doc-html-translate/';
  AndroidGuideUrl = 'https://serzhyale.github.io/FastMediaSorter_Lite/publish-folders-android.html';
  AndroidAppBaseUrl = 'https://serzhyale.github.io/FastMediaSorter_mob_v2/';

var
  InstallOptionsPage: TWizardPage;
  DefaultViewerPromptLabel: TNewStaticText;
  RegisterAssociationsCheckBox: TNewCheckBox;
  AssociationsHintLabel: TNewStaticText;
  ServerFeaturesTitleLabel: TNewStaticText;
  ServerFeaturesCheckBox: TNewCheckBox;
  ServerFeaturesHintLabel: TNewStaticText;
  ShareGuideLinkLabel: TNewStaticText;
  ShareAppLinkLabel: TNewStaticText;
  CompanionTitleLabel: TNewStaticText;
  CompanionBodyLabel: TNewStaticText;
  CompanionSiteLinkLabel: TNewStaticText;
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
    Result := 'Windows 10/11 может дополнительно попросить подтвердить выбор в "Приложениях по умолчанию".'
  else if IsLanguage('ukrainian') then
    Result := 'Windows 10/11 може додатково попросити підтвердити вибір у "Типових програмах".'
  else
    Result := 'Windows 10/11 may still ask you to confirm this once in Default Apps.';
end;

function ServerFeaturesTitleText: String;
begin
  if IsLanguage('russian') then
    Result := 'Общий доступ к папкам для Android (SFTP-сервер)'
  else if IsLanguage('ukrainian') then
    Result := 'Спільний доступ до папок для Android (SFTP-сервер)'
  else
    Result := 'Folder sharing for Android (SFTP server)';
end;

function ServerFeaturesCheckboxText: String;
begin
  if IsLanguage('russian') then
    Result := 'Установить функции сервера общего доступа к папкам'
  else if IsLanguage('ukrainian') then
    Result := 'Встановити функції сервера спільного доступу до папок'
  else
    Result := 'Install folder-sharing server features';
end;

function ServerFeaturesHintText: String;
begin
  if IsLanguage('russian') then
    Result := 'Позволяет телефону Android просматривать папки этого ПК по сети (только чтение, SFTP). Добавляет разрешение в брандмауэр, поэтому нужны права администратора. Можно включить и позже в "Настройки > Поделиться".'
  else if IsLanguage('ukrainian') then
    Result := 'Дозволяє телефону Android переглядати папки цього ПК по мережі (лише читання, SFTP). Додає дозвіл у брандмауер, тож потрібні права адміністратора. Можна ввімкнути й пізніше в "Налаштування > Поділитися".'
  else
    Result := 'Lets an Android phone browse this PC''s folders over the network (read-only, SFTP). Adds a firewall exception, so setup needs administrator rights. Can also be enabled later in Settings > Share.';
end;

function ServerFeaturesAdminHintText: String;
begin
  if IsLanguage('russian') then
    Result := 'Недоступно: установка выполняется без прав администратора. Включить общий доступ можно позже в программе (Настройки > Поделиться).'
  else if IsLanguage('ukrainian') then
    Result := 'Недоступно: встановлення виконується без прав адміністратора. Увімкнути спільний доступ можна пізніше в програмі (Налаштування > Поділитися).'
  else
    Result := 'Unavailable: setup is running without administrator rights. You can enable sharing later inside the app (Settings > Share).';
end;

function ShareGuideUrl: String;
begin
  { The guide page reads a ?lang= query param (added alongside this feature) that
    overrides its default browser/localStorage language detection, so the link can
    land the reader on the section matching the installer's own language. }
  if IsLanguage('russian') then
    Result := AndroidGuideUrl + '?lang=ru'
  else if IsLanguage('ukrainian') then
    Result := AndroidGuideUrl + '?lang=uk'
  else
    Result := AndroidGuideUrl;
end;

function ShareAppUrl: String;
begin
  { Mirrors the language-specific pages the guide page itself links to in its
    footer (index-ru.html / index-uk.html) - no query-param support needed here,
    the Android app's own site already ships per-language pages. }
  if IsLanguage('russian') then
    Result := AndroidAppBaseUrl + 'index-ru.html'
  else if IsLanguage('ukrainian') then
    Result := AndroidAppBaseUrl + 'index-uk.html'
  else
    Result := AndroidAppBaseUrl;
end;

function ShareGuideLinkText: String;
begin
  if IsLanguage('russian') then
    Result := 'Инструкция: как открыть папки для Android-телефона'
  else if IsLanguage('ukrainian') then
    Result := 'Інструкція: як відкрити папки для Android-телефону'
  else
    Result := 'Guide: how to share folders with an Android phone';
end;

function ShareAppLinkText: String;
begin
  if IsLanguage('russian') then
    Result := 'Скачать приложение FastMediaSorter для Android'
  else if IsLanguage('ukrainian') then
    Result := 'Завантажити застосунок FastMediaSorter для Android'
  else
    Result := 'Get the FastMediaSorter Android app';
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
    Result := 'Преобразует EPUB, PDF, FB2, MOBI, TXT или HTML в локальный HTML для чтения. Доступен через winget - подробности и загрузка:'
  else if IsLanguage('ukrainian') then
    Result := 'Перетворює EPUB, PDF, FB2, MOBI, TXT або HTML у локальний HTML для читання. Доступний через winget - докладніше та завантаження:'
  else
    Result := 'Converts EPUB, PDF, FB2, MOBI, TXT, or HTML into clean local HTML for reading. Available via winget - learn more and download here:';
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

procedure OpenCompanionSite(Sender: TObject);
var
  ResultCode: Integer;
begin
  ShellExec('open', CompanionSiteUrl, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

procedure OpenShareGuide(Sender: TObject);
var
  ResultCode: Integer;
begin
  ShellExec('open', ShareGuideUrl, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

procedure OpenShareApp(Sender: TObject);
var
  ResultCode: Integer;
begin
  ShellExec('open', ShareAppUrl, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

function ShouldRegisterAssociations: Boolean;
begin
  Result := (RegisterAssociationsCheckBox <> nil) and RegisterAssociationsCheckBox.Checked;
end;

function ShouldInstallServerFeatures: Boolean;
begin
  { Only when explicitly ticked AND Setup is elevated - the firewall step needs
    admin. In silent/winget installs the page is never shown, so Checked stays
    False and this is a no-op (viewer-only). }
  Result := (ServerFeaturesCheckBox <> nil) and ServerFeaturesCheckBox.Checked and IsAdminInstallMode;
end;

procedure AddServerFirewallRule;
var
  ResultCode: Integer;
  Exe: String;
begin
  Exe := ExpandConstant('{app}\companion\fms-share-worker.exe');
  { Idempotent: drop any stale rule of this name, then add a program-scoped inbound
    allow (survives the worker's dynamic listen port). profile=domain,private,public
    so a dedicated server on a Public network is covered. }
  Exec('netsh', 'advfirewall firewall delete rule name="FastMediaSorter Companion SFTP"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('netsh', 'advfirewall firewall add rule name="FastMediaSorter Companion SFTP" dir=in action=allow program="' + Exe + '" protocol=TCP profile=domain,private,public enable=yes',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure WriteServerFeaturesMarker;
begin
  { Machine-side consent record (hive-safe): the non-elevated app reads this as the
    server-features gate. An install-time HKCU write would land in the elevating
    admin's hive, so a marker file is used instead. Removed via [UninstallDelete]. }
  SaveStringToFile(ExpandConstant('{app}\companion\server-features.enabled'),
    'enabled ' + GetDateTimeString('yyyy/mm/dd hh:nn:ss', '-', ':') + #13#10, False);
end;

procedure RemoveServerFirewallRule;
var
  ResultCode: Integer;
begin
  Exec('netsh', 'advfirewall firewall delete rule name="FastMediaSorter Companion SFTP"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
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
  RegisterAssociationsCheckBox.Top := DefaultViewerPromptLabel.Top + DefaultViewerPromptLabel.Height + ScaleY(4);
  RegisterAssociationsCheckBox.Width := InstallOptionsPage.SurfaceWidth;
  RegisterAssociationsCheckBox.Height := ScaleY(22);
  RegisterAssociationsCheckBox.Checked := False;
  RegisterAssociationsCheckBox.Caption := RegisterAssociationsText;

  AssociationsHintLabel := TNewStaticText.Create(WizardForm);
  ConfigureWrappedLabel(
    AssociationsHintLabel,
    RegisterAssociationsCheckBox.Top + RegisterAssociationsCheckBox.Height + ScaleY(4),
    AssociationsHintText,
    False
  );

  { Opt-in "SFTP server features" - the one privileged step (firewall rule) is
    only performed when this is ticked AND Setup is elevated
    (SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md §3.5). Default OFF; skipped in
    silent/winget installs (the page is not shown, so Checked stays False). }
  ServerFeaturesTitleLabel := TNewStaticText.Create(WizardForm);
  ConfigureWrappedLabel(
    ServerFeaturesTitleLabel,
    AssociationsHintLabel.Top + AssociationsHintLabel.Height + ScaleY(12),
    ServerFeaturesTitleText,
    True
  );

  ServerFeaturesCheckBox := TNewCheckBox.Create(WizardForm);
  ServerFeaturesCheckBox.Parent := InstallOptionsPage.Surface;
  ServerFeaturesCheckBox.Left := 0;
  ServerFeaturesCheckBox.Top := ServerFeaturesTitleLabel.Top + ServerFeaturesTitleLabel.Height + ScaleY(4);
  ServerFeaturesCheckBox.Width := InstallOptionsPage.SurfaceWidth;
  ServerFeaturesCheckBox.Height := ScaleY(20);
  ServerFeaturesCheckBox.Checked := False;
  ServerFeaturesCheckBox.Caption := ServerFeaturesCheckboxText;

  ServerFeaturesHintLabel := TNewStaticText.Create(WizardForm);
  ConfigureWrappedLabel(
    ServerFeaturesHintLabel,
    ServerFeaturesCheckBox.Top + ServerFeaturesCheckBox.Height + ScaleY(4),
    ServerFeaturesHintText,
    False
  );

  { Companion links for the Android side of folder sharing: the step-by-step guide
    (language-matched via ?lang=) and the Android app's own site (its per-language
    pages, same as the guide page links to in its footer). }
  ShareGuideLinkLabel := TNewStaticText.Create(WizardForm);
  ConfigureWrappedLabel(
    ShareGuideLinkLabel,
    ServerFeaturesHintLabel.Top + ServerFeaturesHintLabel.Height + ScaleY(6),
    ShareGuideLinkText,
    False
  );
  ShareGuideLinkLabel.Cursor := crHand;
  ShareGuideLinkLabel.Font.Style := [fsUnderline];
  ShareGuideLinkLabel.Font.Color := clBlue;
  ShareGuideLinkLabel.OnClick := @OpenShareGuide;

  ShareAppLinkLabel := TNewStaticText.Create(WizardForm);
  ConfigureWrappedLabel(
    ShareAppLinkLabel,
    ShareGuideLinkLabel.Top + ShareGuideLinkLabel.Height + ScaleY(4),
    ShareAppLinkText,
    False
  );
  ShareAppLinkLabel.Cursor := crHand;
  ShareAppLinkLabel.Font.Style := [fsUnderline];
  ShareAppLinkLabel.Font.Color := clBlue;
  ShareAppLinkLabel.OnClick := @OpenShareApp;

  CompanionTitleLabel := TNewStaticText.Create(WizardForm);
  ConfigureWrappedLabel(
    CompanionTitleLabel,
    ShareAppLinkLabel.Top + ShareAppLinkLabel.Height + ScaleY(12),
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

  { Clickable hyperlink to the companion site - a static label styled as a link.
    Replaces the old non-selectable winget command line, which could not be
    copied out of the wizard. }
  CompanionSiteLinkLabel := TNewStaticText.Create(WizardForm);
  CompanionSiteLinkLabel.Parent := InstallOptionsPage.Surface;
  CompanionSiteLinkLabel.Left := 0;
  CompanionSiteLinkLabel.Top := CompanionBodyLabel.Top + CompanionBodyLabel.Height + ScaleY(6);
  CompanionSiteLinkLabel.AutoSize := True;
  CompanionSiteLinkLabel.Caption := CompanionSiteUrl;
  CompanionSiteLinkLabel.Cursor := crHand;
  CompanionSiteLinkLabel.Font.Style := [fsUnderline];
  CompanionSiteLinkLabel.Font.Color := clBlue;
  CompanionSiteLinkLabel.OnClick := @OpenCompanionSite;

  CompanionProjectButton := TNewButton.Create(WizardForm);
  CompanionProjectButton.Parent := InstallOptionsPage.Surface;
  CompanionProjectButton.Left := 0;
  CompanionProjectButton.Top := CompanionSiteLinkLabel.Top + CompanionSiteLinkLabel.Height + ScaleY(8);
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

procedure CurPageChanged(CurPageID: Integer);
begin
  { Decision B: the firewall rule needs elevation. On a per-user (non-admin)
    install, grey the checkbox and explain the deferred in-app path instead. }
  if (InstallOptionsPage <> nil) and (CurPageID = InstallOptionsPage.ID) then
  begin
    if ServerFeaturesCheckBox <> nil then
    begin
      ServerFeaturesCheckBox.Enabled := IsAdminInstallMode;
      if not IsAdminInstallMode then
        ServerFeaturesCheckBox.Checked := False;
    end;
    if ServerFeaturesHintLabel <> nil then
    begin
      if IsAdminInstallMode then
        ServerFeaturesHintLabel.Caption := ServerFeaturesHintText
      else
        ServerFeaturesHintLabel.Caption := ServerFeaturesAdminHintText;
      WizardForm.AdjustLabelHeight(ServerFeaturesHintLabel);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and ShouldRegisterAssociations then
    RegisterRequestedImageAssociations;
  if (CurStep = ssPostInstall) and ShouldInstallServerFeatures then
  begin
    AddServerFirewallRule;
    WriteServerFeaturesMarker;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usUninstall then
    exit;

  StopCompanionWorker(ExpandConstant('{app}\stop-companion.ps1'));

  { Remove the SFTP firewall rule this app may have added (no-op if absent). The
    consent marker is removed by the [UninstallDelete] entry. }
  RemoveServerFirewallRule;

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
