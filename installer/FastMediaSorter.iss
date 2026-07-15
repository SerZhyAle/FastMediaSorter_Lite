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
; LITE instance must not be left holding the old exe open. Setup/Uninstall detect it
; via this single-instance mutex (Main_Form.vb) and close it gracefully before
; touching files. The tray-resident Companion app AND its worker are invisible to
; this mutex (and cannot be closed by the Restart Manager - the Companion autostarts
; to the tray and never exits on a window-close); StopCompanionWorker in [Code]
; terminates both of those explicitly.
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

; Setup type + component labels (see [Types]/[Components]). The wizard appends each
; component's on-disk size after the text automatically, so the package weight is
; shown per part - the "what & why" is the description itself.
[CustomMessages]
english.TypeFull=Full - everything offline-ready (recommended)
english.TypeCompact=Compact - viewer only (codecs & models download on demand)
english.TypeCustom=Custom
english.CompCore=Core - image & video viewer and sorter (required)
english.CompCodecs=Video codecs (VLC) - offline playback of AVI, MKV, VP9 and other formats
english.CompOcr=OCR & translation models - offline on-image text recognition
english.CompShare=Android Folder Share companion - share folders to your phone (bundles its own .NET runtime)

russian.TypeFull=Полная - всё для работы без интернета (рекомендуется)
russian.TypeCompact=Компактная - только просмотрщик (кодеки и модели скачаются по мере надобности)
russian.TypeCustom=Выборочная
russian.CompCore=Ядро - просмотр и сортировка изображений и видео (обязательно)
russian.CompCodecs=Видео-кодеки (VLC) - оффлайн-воспроизведение AVI, MKV, VP9 и других форматов
russian.CompOcr=Модели OCR и перевода - распознавание текста на изображении без интернета
russian.CompShare=Компаньон Android Folder Share - раздача папок на телефон (включает свой рантайм .NET)

ukrainian.TypeFull=Повна - усе для роботи без інтернету (рекомендовано)
ukrainian.TypeCompact=Компактна - лише переглядач (кодеки та моделі завантажаться за потреби)
ukrainian.TypeCustom=Вибіркова
ukrainian.CompCore=Ядро - перегляд і сортування зображень та відео (обов'язково)
ukrainian.CompCodecs=Відео-кодеки (VLC) - офлайн-відтворення AVI, MKV, VP9 та інших форматів
ukrainian.CompOcr=Моделі OCR та перекладу - розпізнавання тексту на зображенні без інтернету
ukrainian.CompShare=Компаньйон Android Folder Share - роздача тек на телефон (містить власний рантайм .NET)

; Prepend an honest one-liner to the component-selection page: the viewer is light,
; the weight is optional offline payload the user can trim.
[Messages]
english.SelectComponentsLabel2=The viewer itself is small - most of the size is optional, offline-ready payload. Keep everything for full offline use, or clear what you don't need (it can download later on demand). Click Next when you are ready to continue.
russian.SelectComponentsLabel2=Сам просмотрщик весит мало - основной объём это опциональная оффлайн-начинка. Оставьте всё для полной работы без интернета или снимите ненужное (при необходимости оно скачается позже). Нажмите "Далее", когда будете готовы продолжить.
ukrainian.SelectComponentsLabel2=Сам переглядач важить мало - основний обсяг це опційна офлайн-начинка. Залиште все для повної роботи без інтернету або зніміть непотрібне (за потреби воно завантажиться пізніше). Натисніть "Далі", коли будете готові продовжити.

; Explain the admin choice at the exact spot it is made (the built-in "Select Setup
; Install Mode" screen shown by PrivilegesRequiredOverridesAllowed=dialog), so the user
; can weigh it and decide before any UAC prompt: admin (all users) unlocks the offline
; codecs, OCR models and Android folder-sharing (which needs a firewall rule); without
; admin (you only) they get just the lightweight viewer, which is a perfectly fine
; choice. %1 = application name, %n = line break. Text1 is shown when "all users" is the
; default and Text2 when "you only" is (our default is lowest -> Text2), so both carry
; the same explanation.
english.PrivilegesRequiredOverrideText1=%1 can be installed for all users of this computer (requires administrator rights) or for you only (no administrator rights).%n%nInstalling for all users additionally sets up the offline video codecs (VLC), the OCR / translation models and Android folder-sharing over SFTP (which adds a Windows Firewall rule). Installing for you only keeps it lightweight - just the image and video viewer.
english.PrivilegesRequiredOverrideText2=%1 can be installed for you only (no administrator rights) or for all users of this computer (requires administrator rights).%n%nFor you only keeps it lightweight - just the image and video viewer. For all users additionally sets up the offline video codecs (VLC), the OCR / translation models and Android folder-sharing over SFTP (which adds a Windows Firewall rule).
russian.PrivilegesRequiredOverrideText1=%1 можно установить для всех пользователей этого компьютера (нужны права администратора) или только для вас (без прав администратора).%n%nУстановка для всех пользователей дополнительно ставит оффлайн видео-кодеки (VLC), модели OCR и перевода, а также раздачу папок на Android по SFTP (для этого добавляется правило брандмауэра Windows). Установка только для вас оставляет всё лёгким - только просмотрщик изображений и видео.
russian.PrivilegesRequiredOverrideText2=%1 можно установить только для вас (без прав администратора) или для всех пользователей этого компьютера (нужны права администратора).%n%nТолько для вас - оставляет всё лёгким, ставится только просмотрщик изображений и видео. Для всех пользователей - дополнительно ставит оффлайн видео-кодеки (VLC), модели OCR и перевода, а также раздачу папок на Android по SFTP (для этого добавляется правило брандмауэра Windows).
ukrainian.PrivilegesRequiredOverrideText1=%1 можна встановити для всіх користувачів цього комп'ютера (потрібні права адміністратора) або лише для вас (без прав адміністратора).%n%nВстановлення для всіх користувачів додатково ставить офлайн відео-кодеки (VLC), моделі OCR і перекладу та роздачу тек на Android через SFTP (для цього додається правило брандмауера Windows). Встановлення лише для вас залишає все легким - тільки переглядач зображень та відео.
ukrainian.PrivilegesRequiredOverrideText2=%1 можна встановити лише для вас (без прав адміністратора) або для всіх користувачів цього комп'ютера (потрібні права адміністратора).%n%nЛише для вас - залишає все легким, ставиться тільки переглядач зображень та відео. Для всіх користувачів - додатково ставить офлайн відео-кодеки (VLC), моделі OCR і перекладу та роздачу тек на Android через SFTP (для цього додається правило брандмауера Windows).

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; The viewer itself is tiny; almost all of the package weight is optional,
; offline-ready payload (VLC codecs, OCR models, the Share companion). Exposing it
; as selectable components lets the wizard show each part's size + purpose and lets
; the user leave out what they do not need (those parts then download on demand, or
; the feature is simply absent). Silent installs (winget / Store) pick the first
; type = "full", so unattended flows are unchanged.
[Types]
Name: "full";    Description: "{cm:TypeFull}"
Name: "compact"; Description: "{cm:TypeCompact}"
Name: "custom";  Description: "{cm:TypeCustom}"; Flags: iscustom

[Components]
Name: "core";   Description: "{cm:CompCore}";   Types: full compact custom; Flags: fixed
Name: "codecs"; Description: "{cm:CompCodecs}"; Types: full
Name: "ocr";    Description: "{cm:CompOcr}";    Types: full
Name: "share";  Description: "{cm:CompShare}";  Types: full

[Files]
; Core (always installed): the LITE viewer, its bundled managed payload and the
; native Tesseract/OCR engine. Excludes the big optional subtrees below so an
; unchecked component is genuinely not written to disk.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "libvlc\*,tessdata\*,tessdata-best\*,companion\*,FastMediaSorterCompanion.exe"; Flags: recursesubdirs createallsubdirs ignoreversion; Components: core
; Video codecs (LibVLC) - offline playback of AVI/MKV/VP9/etc. Absent = those
; formats fall back to on-demand runtime download (OptionalRuntimeManager).
; This is the x64-only mainline package (ArchitecturesAllowed=x64compatible); the
; AnyCPU app runs 64-bit here, so the win-x86 plugin tree is never loaded. It is
; already trimmed upstream by Prepare-OcrOfflinePayload.ps1; excluded here too as a
; belt-and-suspenders guarantee now that 32-bit support is a separate standalone
; product (see SPECIFICATION_DOTNET10_MODERN_BUILD.md, legacy x86 viewer).
Source: "{#SourceDir}\libvlc\*"; DestDir: "{app}\libvlc"; Excludes: "win-x86\*"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: codecs; Check: OptionalPayloadAllowed
; OCR/translation language models (fast + best). Absent = packs download on first
; OCR use instead of shipping in the installer.
Source: "{#SourceDir}\tessdata\*"; DestDir: "{app}\tessdata"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: ocr; Check: OptionalPayloadAllowed
Source: "{#SourceDir}\tessdata-best\*"; DestDir: "{app}\tessdata-best"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: ocr; Check: OptionalPayloadAllowed
; Android Folder Share - the self-contained .NET companion app + its SFTP worker.
; The companion carries its own .NET runtime, which is the bulk of this component.
Source: "{#SourceDir}\FastMediaSorterCompanion.exe"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist; Components: share; Check: OptionalPayloadAllowed
Source: "{#SourceDir}\companion\*"; DestDir: "{app}\companion"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: share; Check: OptionalPayloadAllowed
; Helper Setup/Uninstall use to stop the running Companion app and its worker (see
; StopCompanionWorker in [Code]). Kept both as a dontcopy temp extract (Setup runs
; this before any app files exist yet, so it cannot read one from {app}) and as a
; normal installed file (Uninstall only has access to already-installed files).
Source: "stop-companion.ps1"; DestDir: "{tmp}"; Flags: dontcopy
Source: "stop-companion.ps1"; DestDir: "{app}"; Flags: ignoreversion
; Elevated helper for the deferred, in-app "enable server features" opt-in (adds /
; removes the SFTP firewall rule via one UAC prompt). Installed next to the exe so
; ServerFeatures.EnableViaElevation prefers it over a direct netsh fallback.
Source: "enable-share-server.ps1"; DestDir: "{app}"; Flags: ignoreversion; Components: share; Check: OptionalPayloadAllowed

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
    Result := 'Включить общий доступ и открыть Менеджер общего доступа сразу после установки'
  else if IsLanguage('ukrainian') then
    Result := 'Увімкнути спільний доступ і відкрити Менеджер спільного доступу одразу після встановлення'
  else
    Result := 'Turn on folder sharing and open the Share Manager right after installation';
end;

function ServerFeaturesHintText: String;
begin
  if IsLanguage('russian') then
    Result := 'Позволяет телефону Android просматривать папки этого ПК по сети (только чтение, SFTP). Добавляет разрешение в брандмауэр, поэтому нужны права администратора. Менеджер общего доступа откроется по завершении установки, чтобы вы выбрали папку и запустили сервер. Можно включить и позже в "Настройки > Поделиться".'
  else if IsLanguage('ukrainian') then
    Result := 'Дозволяє телефону Android переглядати папки цього ПК по мережі (лише читання, SFTP). Додає дозвіл у брандмауер, тож потрібні права адміністратора. Менеджер спільного доступу відкриється після завершення встановлення, щоб ви вибрали теку й запустили сервер. Можна ввімкнути й пізніше в "Налаштування > Поділитися".'
  else
    Result := 'Lets an Android phone browse this PC''s folders over the network (read-only, SFTP). Adds a firewall exception, so setup needs administrator rights. The Share Manager opens when setup finishes so you can pick a folder and start the server. Can also be enabled later in Settings > Share.';
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

function ServerFeaturesNeedsShareHintText: String;
begin
  if IsLanguage('russian') then
    Result := 'Недоступно: компонент "Компаньон Android Folder Share" не выбран на странице компонентов. Выберите его там, чтобы включить эту опцию.'
  else if IsLanguage('ukrainian') then
    Result := 'Недоступно: компонент "Компаньйон Android Folder Share" не вибрано на сторінці компонентів. Виберіть його там, щоб увімкнути цю опцію.'
  else
    Result := 'Unavailable: the Android Folder Share companion is not selected on the components page. Select it there to enable this option.';
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
    Result := 'Перевод текста на картинках: doc-html-translate'
  else if IsLanguage('ukrainian') then
    Result := 'Переклад тексту на зображеннях: doc-html-translate'
  else
    Result := 'Translate text in images: doc-html-translate';
end;

function CompanionBodyText: String;
begin
  if IsLanguage('russian') then
    Result := 'Распознаёт и переводит текст на изображениях и фотографиях. Также умеет конвертировать документы (EPUB, PDF и другие) в локальный HTML. Доступно через winget:'
  else if IsLanguage('ukrainian') then
    Result := 'Розпізнає та перекладає текст на зображеннях і фотографіях. Також може конвертувати документи (EPUB, PDF та інші) у локальний HTML. Доступно через winget:'
  else
    Result := 'Recognizes and translates the text inside images and photos. It can also convert documents (EPUB, PDF and more) into clean local HTML. Available via winget:';
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

function OptionalPayloadAllowed: Boolean;
begin
  { Codecs, OCR models and the Share companion are offered ONLY in an all-users
    (elevated) install - a per-user install without admin is the lightweight viewer
    alone (owner decision). Used as the [Files] Check on those components so they are
    never written without elevation, in EVERY path - including silent installs where
    the components page (which also greys them out) is never shown. }
  Result := IsAdminInstallMode;
end;

function ShouldRegisterAssociations: Boolean;
begin
  Result := (RegisterAssociationsCheckBox <> nil) and RegisterAssociationsCheckBox.Checked;
end;

function ShouldInstallServerFeatures: Boolean;
begin
  { Only when explicitly ticked AND Setup is elevated - the firewall step needs
    admin. In silent/winget installs the page is never shown, so Checked stays
    False and this is a no-op (viewer-only). Also require the Share component to be
    installed - there is no worker exe to allow through the firewall otherwise. }
  Result := (ServerFeaturesCheckBox <> nil) and ServerFeaturesCheckBox.Checked and IsAdminInstallMode and WizardIsComponentSelected('share');
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

{ Opt-in "run the SFTP server right after installation": launch the Share Manager
  (Companion) so the user can pick a folder and start serving. On a fresh install
  nothing is shared yet, so this opens the window rather than the silent --tray
  autostart (which would sit idle). Best-effort - a missing exe is a no-op. }
procedure LaunchShareManager;
var
  ResultCode: Integer;
  Exe: String;
begin
  Exe := ExpandConstant('{app}\FastMediaSorterCompanion.exe');
  if not FileExists(Exe) then
    exit;
  ShellExec('open', Exe, '', ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ResultCode);
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
end;

{ Stops the tray-resident Companion app (FastMediaSorterCompanion.exe) AND its
  headless worker (fms-share-worker.exe) before Setup/Uninstall touch their files.
  Both are invisible to the AppMutex check above and cannot be closed by the Restart
  Manager - the Companion autostarts to the tray and never exits on a window-close,
  and the worker is windowless - so the file replace fails unless they are killed
  here. Runs the bundled helper script, which asks the worker to stop over its
  control pipe (clean SFTP + UPnP teardown), then terminates the Companion, then
  force-kills any surviving worker. Best-effort: a missing or failing script must
  never abort Setup. }
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
var
  ShareSelected: Boolean;
  i: Integer;
begin
  { Per-user (no-admin) install = the lightweight viewer only. The offline codecs,
    OCR models and the Share companion are offered ONLY in an all-users (elevated)
    install: without the firewall rule (admin) the SFTP worker cannot accept a single
    connection, so shipping it would be pointless (owner decision). So grey those rows
    out and force viewer-only here; the [Files] Check (OptionalPayloadAllowed) is the
    hard guarantee that also covers silent installs where this page never shows. The
    all-users vs me-only choice was made (and explained) on the install-mode screen. }
  if (CurPageID = wpSelectComponents) and (not IsAdminInstallMode) then
  begin
    if WizardForm.TypesCombo <> nil then
    begin
      WizardForm.TypesCombo.ItemIndex := 1;   { [Types]: 0 full, 1 compact (viewer only), 2 custom }
      WizardForm.TypesCombo.Enabled := False;
    end;
    { [Components] order: 0 core (fixed), 1 codecs, 2 ocr, 3 share - lock every
      optional row off. }
    for i := 1 to WizardForm.ComponentsList.Items.Count - 1 do
    begin
      WizardForm.ComponentsList.Checked[i] := False;
      WizardForm.ComponentsList.ItemEnabled[i] := False;
    end;
  end;

  { The "run the SFTP server after install" opt-in requires two things: the Share
    component must be installed (screen 1 - otherwise there is no worker exe to run)
    AND setup must be elevated (the firewall rule needs admin). Grey the checkbox
    and explain the relevant reason when either is missing. }
  if (InstallOptionsPage <> nil) and (CurPageID = InstallOptionsPage.ID) then
  begin
    ShareSelected := WizardIsComponentSelected('share');
    if ServerFeaturesCheckBox <> nil then
    begin
      ServerFeaturesCheckBox.Enabled := ShareSelected and IsAdminInstallMode;
      if not ServerFeaturesCheckBox.Enabled then
        ServerFeaturesCheckBox.Checked := False;
    end;
    if ServerFeaturesHintLabel <> nil then
    begin
      { Admin first: without elevation the whole Share feature (and its component) is
        unavailable, so the "enable it later in the app" note is the honest one - never
        tell the user to tick a Share component they cannot select without admin. }
      if not IsAdminInstallMode then
        ServerFeaturesHintLabel.Caption := ServerFeaturesAdminHintText
      else if not ShareSelected then
        ServerFeaturesHintLabel.Caption := ServerFeaturesNeedsShareHintText
      else
        ServerFeaturesHintLabel.Caption := ServerFeaturesHintText;
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
  { Launch the Share Manager once the install is fully done (after the Finished
    page), so the user lands in the UI to pick a folder and start the server -
    the "run it right after installation" opt-in. Guarded to interactive installs
    (silent/winget never show the page, so ShouldInstallServerFeatures is False). }
  if (CurStep = ssDone) and ShouldInstallServerFeatures and (not WizardSilent) then
    LaunchShareManager;
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
