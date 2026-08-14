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
; The viewer ships as TWO exes in one folder (CLAUDE.md "Project identity"):
;   AppExeName    - the .NET 10 x64 mainline. FROZEN name: it replaces the exe of
;                   an existing installation in place. Needs Windows 10 1607+.
;   AppExeNameX86 - the lean net48 32-bit sibling, for Windows 7/8.1 where the
;                   mainline's runtime cannot run at all.
; Shortcuts, the post-install launch and the file associations are wired to
; whichever of the two can actually run on THIS machine - see UseModernExe below.
#define AppExeName    "FastMediaSorter_LITE.exe"
#define AppExeNameX86 "FastMediaSorter_x86.exe"
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
; This .iss lives at <repo>\publishing\installer, so the repo-root assets tree is two levels up.
SetupIconFile=..\..\assets\icons\Fast_Media_Sorter.ico
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
; can weigh it and decide before any UAC prompt. Both modes now install the SAME
; feature set - the optional components stopped being admin-only (see the comment above
; the optional [Files] entries); administrator rights buy exactly one thing here, the
; ability to switch folder sharing on during setup, because that writes a Windows
; Firewall rule. %1 = application name, %n = line break. Text1 is shown when "all users"
; is the default and Text2 when "you only" is (our default is lowest -> Text2), so both
; carry the same explanation.
english.PrivilegesRequiredOverrideText1=%1 can be installed for all users of this computer (requires administrator rights) or for you only (no administrator rights).%n%nBoth modes install the same features, including the offline video codecs (VLC), the OCR / translation models and the Android folder-sharing companion. Folder sharing can be switched on in either mode - Windows asks for approval once for its firewall rule. The real difference is only where the program lands and whether the other accounts on this PC see it.
english.PrivilegesRequiredOverrideText2=%1 can be installed for you only (no administrator rights) or for all users of this computer (requires administrator rights).%n%nBoth modes install the same features, including the offline video codecs (VLC), the OCR / translation models and the Android folder-sharing companion. Folder sharing can be switched on in either mode - Windows asks for approval once for its firewall rule. The real difference is only where the program lands and whether the other accounts on this PC see it.
russian.PrivilegesRequiredOverrideText1=%1 можно установить для всех пользователей этого компьютера (нужны права администратора) или только для вас (без прав администратора).%n%nОба режима ставят одинаковый набор возможностей, включая оффлайн видео-кодеки (VLC), модели OCR / перевода и компаньон для раздачи папок на Android. Раздачу папок можно включить в любом из них - Windows один раз попросит подтверждение для правила брандмауэра. Разница только в том, куда попадёт программа и увидят ли её другие учётные записи этого ПК.
russian.PrivilegesRequiredOverrideText2=%1 можно установить только для вас (без прав администратора) или для всех пользователей этого компьютера (нужны права администратора).%n%nОба режима ставят одинаковый набор возможностей, включая оффлайн видео-кодеки (VLC), модели OCR / перевода и компаньон для раздачи папок на Android. Раздачу папок можно включить в любом из них - Windows один раз попросит подтверждение для правила брандмауэра. Разница только в том, куда попадёт программа и увидят ли её другие учётные записи этого ПК.
ukrainian.PrivilegesRequiredOverrideText1=%1 можна встановити для всіх користувачів цього комп'ютера (потрібні права адміністратора) або лише для вас (без прав адміністратора).%n%nОбидва режими встановлюють однаковий набір можливостей, включно з офлайн відеокодеками (VLC), моделями OCR / перекладу та компаньйоном для роздачі тек на Android. Роздачу тек можна ввімкнути в будь-якому з них - Windows один раз попросить підтвердження для правила брандмауера. Різниця лише в тому, куди потрапить програма й чи побачать її інші облікові записи цього ПК.
ukrainian.PrivilegesRequiredOverrideText2=%1 можна встановити лише для вас (без прав адміністратора) або для всіх користувачів цього комп'ютера (потрібні права адміністратора).%n%nОбидва режими встановлюють однаковий набір можливостей, включно з офлайн відеокодеками (VLC), моделями OCR / перекладу та компаньйоном для роздачі тек на Android. Роздачу тек можна ввімкнути в будь-якому з них - Windows один раз попросить підтвердження для правила брандмауера. Різниця лише в тому, куди потрапить програма й чи побачать її інші облікові записи цього ПК.

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
; Share is offered ONLY where it can actually run. The Share Manager companion is
; .NET 10 x64, so on Windows 7/8.1 - which the setup-wide MinVersion=6.1 still lets
; Setup run on - it cannot start at all, and installing it there would put a button in
; the viewer leading to an exe the machine refuses to launch.
; 10.0.14393 is the same floor as the UseModernExe function below (keep the two in
; step). It is spelled as MinVersion and NOT as "Check: UseModernExe" because Check is
; not among the parameters [Components] accepts - only Name/Description/Types/
; ExtraDiskSpaceRequired/Flags plus the common Languages/MinVersion/OnlyBelowVersion.
Name: "share";  Description: "{cm:CompShare}";  Types: full; MinVersion: 10.0.14393

[Files]
; Core (always installed): BOTH viewer exes - the .NET 10 x64 mainline
; (FastMediaSorter_LITE.exe) and its lean net48 sibling (FastMediaSorter_x86.exe)
; for machines the mainline cannot run on - plus the native Tesseract/OCR engine.
; They are picked up by this one wildcard; the big optional subtrees below are
; excluded so an unchecked component is genuinely not written to disk. "*.log" is
; excluded too: the staging scripts already drop it, but a hand compile against a
; tree the app has been RUN from would otherwise install our own current.log into
; {app} and the user's log would start with our sessions and our paths.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "libvlc\*,tessdata\*,tessdata-best\*,companion\*,FastMediaSorterCompanion.exe,*.log"; Flags: recursesubdirs createallsubdirs ignoreversion; Components: core
; The three optional components below are NOT gated on an elevated install, and
; must not be gated on one again. They were (Check: OptionalPayloadAllowed =
; IsAdminInstallMode), and that silently reduced EVERY unattended install to the
; viewer alone: Inno runs in non administrative install mode whenever
; PrivilegesRequired=lowest and no /ALLUSERS was passed, which is exactly how
; winget invokes this setup (/VERYSILENT, no scope switch). The user downloaded a
; ~370 MB package and got none of its payload - no codecs, no OCR models, and no
; Share Manager at all, a feature that has no on-demand download path to recover
; through (the codecs and models at least self-heal via OptionalRuntimeManager).
; Nothing in these components needs admin: a per-user install writes them under
; %LocalAppData%\Programs like every other file. Administrator rights are still
; required for the ONE privileged step, the SFTP firewall rule, which remains an
; explicit opt-in - the installer checkbox (ShouldInstallServerFeatures below) or
; the deferred in-app opt-in via enable-share-server.ps1.
;
; Video codecs (LibVLC) - offline playback of AVI/MKV/VP9/etc. Absent = those
; formats fall back to on-demand runtime download (OptionalRuntimeManager).
; Whatever arch trees the staged payload holds are shipped as-is: the decision of
; which to carry lives in ONE place, Prepare-OcrOfflinePayload.ps1 (-KeepX86). By
; default it trims win-x86, so this x64 package ships win-x64 only and the x86
; viewer downloads its 32-bit codecs on first use.
Source: "{#SourceDir}\libvlc\*"; DestDir: "{app}\libvlc"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: codecs
; OCR/translation language models (fast + best). Absent = packs download on first
; OCR use instead of shipping in the installer.
Source: "{#SourceDir}\tessdata\*"; DestDir: "{app}\tessdata"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: ocr
Source: "{#SourceDir}\tessdata-best\*"; DestDir: "{app}\tessdata-best"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: ocr
; Android Folder Share - the self-contained .NET companion app + its SFTP worker.
; The companion carries its own .NET runtime, which is the bulk of this component.
Source: "{#SourceDir}\FastMediaSorterCompanion.exe"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist; Components: share
Source: "{#SourceDir}\companion\*"; DestDir: "{app}\companion"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: share
; Helper Setup/Uninstall use to stop the running Companion app and its worker (see
; StopCompanionWorker in [Code]). Kept both as a dontcopy temp extract (Setup runs
; this before any app files exist yet, so it cannot read one from {app}) and as a
; normal installed file (Uninstall only has access to already-installed files).
Source: "stop-companion.ps1"; DestDir: "{tmp}"; Flags: dontcopy
Source: "stop-companion.ps1"; DestDir: "{app}"; Flags: ignoreversion
; Elevated helper for the deferred, in-app "enable server features" opt-in (adds /
; removes the SFTP firewall rule via one UAC prompt). Installed next to the exe so
; ServerFeatures.EnableViaElevation prefers it over a direct netsh fallback.
Source: "enable-share-server.ps1"; DestDir: "{app}"; Flags: ignoreversion; Components: share
; Elevated management helper for the in-app switch to always-on hosting: Share Manager
; -> "Manage hosting.." -> switch to the Windows service. It is the SAME script the
; Server edition installer drives, so every machine-affecting step still lives in one
; auditable place; shipping it here is what lets an ordinary installation take that role
; on (behind one visible UAC prompt) instead of requiring a second download. It never
; runs by itself: nothing in this installer calls it during a normal install.
Source: "install-share-service.ps1"; DestDir: "{app}"; Flags: ignoreversion; Components: share

[InstallDelete]
; The Start-menu group used to be named "FastMediaSorter LITE"; it is now the new
; display name. Remove the stale folder on upgrade so a user is not left with two
; Start-menu folders. The install dir, exe, AppId and ARP name are unchanged, so
; this leftover shortcut folder is the only thing to clean.
Type: filesandordirs; Name: "{autoprograms}\FastMediaSorter LITE"

; Shortcuts point at the exe that RUNS on this machine: the x64 mainline on
; Windows 10 1607+, the 32-bit sibling on Windows 7/8.1 (both are installed).
[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Check: UseModernExe
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeNameX86}"; Check: UseLegacyExe
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon; Check: UseModernExe
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeNameX86}"; Tasks: desktopicon; Check: UseLegacyExe

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent; Check: UseModernExe
Filename: "{app}\{#AppExeNameX86}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent; Check: UseLegacyExe

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
  { The Server edition's SCM service name (frozen). Detected here only - this
    installer never registers, starts or removes it. }
  ShareServiceName = 'FastMediaSorterCompanionSFTP';

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

{ --- Server edition conflict detection (SPECIFICATION_SHARE_SYSTEM_SERVICE.md §1.4) --
  The two editions must never coexist as independent live installations: they would
  compete for the frozen control pipe, the service name, the listen port and the
  persistent host key. This installer only DETECTS the Server edition - it never
  registers, removes or repairs a service. Its job here is to stop and say what to do,
  because the alternative is a machine where sharing intermittently half-works and
  nothing explains why. }

function ShareServiceRegistered: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\' + ShareServiceName);
end;

{ Where the machine-wide share state lives, and - under \bin - the worker copy the
  service runs when always-on hosting was switched on from inside the app. Must match
  ServiceControl.MachineDataDir() and the helper's $BinDirName. }
function MachineShareDataDir: String;
begin
  Result := ExpandConstant('{commonappdata}\FastMediaSorterCompanion');
end;

function ShareServiceImagePath: String;
var
  value: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM, 'SYSTEM\CurrentControlSet\Services\' + ShareServiceName, 'ImagePath', value) then
    Result := value;
end;

{ A registered service is not automatically "the Server edition". Since the Share
  Manager can switch an ordinary installation into always-on hosting, there are two
  kinds of registration to tell apart, and confusing them is what would either block a
  harmless update or delete somebody else's server:
    * ours - the service runs the staged copy under %ProgramData%\..\bin, put there by
      this application's own opt-in. A viewer install/update over it is routine, and an
      uninstall may offer to take the role away again;
    * the separate Server edition - it runs the worker from its own program folder,
      has its own AppId and uninstaller, and this installer must never touch it. }
{ Is the separate Server edition installed as a product? Its own frozen Inno AppId is
  the only reliable answer. The staged-path test below cannot carry this on its own:
  the Server installer stages its worker into the SAME %ProgramData%\..\bin directory
  (that is what lets an update replace a file the service is running), so after one
  Server update the path alone would report somebody else's server as ours. }
function ServerEditionProductInstalled: Boolean;
var
  ServerUninstallKey: String;
begin
  ServerUninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A9F3C61B-4D2E-4F58-9C7A-1E6B0D3F82A4}_is1';
  Result := RegKeyExists(HKLM, ServerUninstallKey) or
            RegKeyExists(HKLM, 'Software\WOW6432Node\' + ServerUninstallKey) or
            RegKeyExists(HKCU, ServerUninstallKey);
end;

function ShareServiceIsAppOwned: Boolean;
begin
  Result := (ShareServiceImagePath <> '') and
            (Pos(Uppercase(MachineShareDataDir + '\' + 'bin'), Uppercase(ShareServiceImagePath)) > 0) and
            (not ServerEditionProductInstalled);
end;

function ServerEditionInstalled: Boolean;
begin
  Result := ShareServiceRegistered and (not ShareServiceIsAppOwned);
end;

{ Is this an update of an installation that is already here, rather than a first one? }
function PreviousInstallationExists: Boolean;
var
  AppUninstallKey: String;
begin
  { The AppId above, as Inno records it: HKCU for a per-user install, HKLM for an
    all-users one (and the 32-bit view on some upgrade paths). }
  AppUninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7371E7F1-B8A8-4786-8173-5F5B2B6E6AC9}_is1';
  Result := RegKeyExists(HKCU, AppUninstallKey) or
            RegKeyExists(HKLM, AppUninstallKey) or
            RegKeyExists(HKLM, 'Software\WOW6432Node\' + AppUninstallKey);
end;

function ServerEditionConflictText: String;
begin
  if IsLanguage('russian') then
    Result := 'На этом компьютере установлена СЕРВЕРНАЯ редакция общего доступа: папки раздаёт служба Windows, которая работает без входа в систему.' + #13#10#13#10 +
              'Установка обычной (пользовательской) редакции поверх неё не остановлена - просмотрщик поставится как обычно. Но раздачей по-прежнему будет управлять служба: Менеджер общего доступа откроется как пульт управления и не станет запускать второй сервер.' + #13#10#13#10 +
              'Чтобы вернуться к пользовательскому режиму, откройте Менеджер общего доступа -> «Управление хостингом..» -> «Вернуться к пользовательской редакции..» (потребуются права администратора).'
  else if IsLanguage('ukrainian') then
    Result := 'На цьому комп''ютері встановлено СЕРВЕРНУ редакцію спільного доступу: теки роздає служба Windows, яка працює без входу в систему.' + #13#10#13#10 +
              'Встановлення звичайної (користувацької) редакції поверх неї не зупинено - переглядач встановиться як завжди. Але роздачею й далі керуватиме служба: Менеджер спільного доступу відкриється як пульт керування й не запускатиме другий сервер.' + #13#10#13#10 +
              'Щоб повернутися до користувацького режиму, відкрийте Менеджер спільного доступу -> «Керування хостингом..» -> «Повернутися до користувацької редакції..» (потрібні права адміністратора).'
  else
    Result := 'The SERVER edition of folder sharing is installed on this computer: a Windows service serves the folders and keeps running with nobody signed in.' + #13#10#13#10 +
              'Installing the regular (User) edition over it is not blocked - the viewer installs as usual. But the service stays in charge of sharing: the Share Manager opens as a management console and will not start a second server.' + #13#10#13#10 +
              'To go back to User mode, open the Share Manager -> "Manage hosting.." -> "Return to the User edition.." (administrator rights are required).';
end;

function ServerEditionSilentText: String;
begin
  if IsLanguage('russian') then
    Result := 'Обнаружена серверная редакция общего доступа (служба Windows). Тихая установка остановлена, чтобы не менять работающую конфигурацию сервера без подтверждения. Запустите установщик в обычном режиме или передайте /ALLOWSERVEREDITION, чтобы продолжить.'
  else if IsLanguage('ukrainian') then
    Result := 'Виявлено серверну редакцію спільного доступу (служба Windows). Тиху установку зупинено, щоб не змінювати робочу конфігурацію сервера без підтвердження. Запустіть установник у звичайному режимі або передайте /ALLOWSERVEREDITION, щоб продовжити.'
  else
    Result := 'The Server edition of folder sharing (a Windows service) was detected. This silent installation was stopped so a working server configuration is not changed without confirmation. Run Setup interactively, or pass /ALLOWSERVEREDITION to continue.';
end;

function ShareServiceRemovalPromptText: String;
begin
  if IsLanguage('russian') then
    Result := 'На этом компьютере раздача папок включена как служба Windows: она работает без входа в систему и продолжит раздавать выбранные папки даже после удаления программы.' + #13#10#13#10 +
              'Убрать службу вместе с программой? Понадобятся права администратора.' + #13#10#13#10 +
              'Настройки и ключ узла при этом сохраняются, так что после повторной установки раздачу можно включить снова, и уже подключённые телефоны не придётся подключать заново.'
  else if IsLanguage('ukrainian') then
    Result := 'На цьому комп''ютері роздачу тек увімкнено як службу Windows: вона працює без входу в систему й продовжить роздавати вибрані теки навіть після видалення програми.' + #13#10#13#10 +
              'Прибрати службу разом із програмою? Знадобляться права адміністратора.' + #13#10#13#10 +
              'Налаштування та ключ вузла при цьому зберігаються, тож після повторного встановлення роздачу можна ввімкнути знову, і вже підключені телефони не доведеться підключати наново.'
  else
    Result := 'Folder sharing is switched on as a Windows service on this computer: it runs with nobody signed in and would keep serving the selected folders after the program is removed.' + #13#10#13#10 +
              'Remove the service together with the program? Administrator rights are required.' + #13#10#13#10 +
              'Your settings and the host key are kept either way, so a later reinstall can turn sharing back on without re-pairing the phones that are already connected.';
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not ServerEditionInstalled then
    exit;

  { Silent (winget / scripted) installs must fail CLEARLY rather than quietly land a
    second sharing host on a server. An explicit, documented flag opts out. }
  if WizardSilent then
  begin
    { ..but an UPDATE of an installation that is already on this machine is not that
      case, and blocking it was a real trap: winget passes no such flag, so on every
      machine running the Server edition `winget upgrade` failed permanently and the
      viewer could never be updated again through the channel that installed it. An
      update replaces viewer files and touches nothing the service owns - the Share
      Manager already connects to the service instead of spawning a second worker.
      A FIRST silent install still stops. }
    if PreviousInstallationExists then
      exit;
    if CompareText(ExpandConstant('{param:ALLOWSERVEREDITION|no}'), 'no') = 0 then
    begin
      SuppressibleMsgBox(ServerEditionSilentText, mbError, MB_OK, IDOK);
      Result := False;
    end;
    exit;
  end;

  { Interactive: explain the consequence and let the user decide. Installing the
    viewer over a Server edition is legitimate - the two only collide over HOSTING,
    and the Share Manager already resolves that by connecting instead of spawning. }
  Result := SuppressibleMsgBox(ServerEditionConflictText, mbInformation, MB_OKCANCEL, IDOK) = IDOK;
end;

{ Can the x64 mainline actually run here? Its bundled .NET 10 runtime requires
  Windows 10 1607 (build 14393) or newer, while this installer still accepts
  Windows 7 (MinVersion=6.1). On anything older the 32-bit net48 sibling is the
  working viewer, so shortcuts, the post-install launch and the file associations
  are pointed at it instead. Both exes are always installed. Windows 11 reports
  Major=10 with a higher build, so the >= 14393 test covers it. }
function UseModernExe: Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result := (Version.Major > 10) or ((Version.Major = 10) and (Version.Build >= 14393));
end;

function UseLegacyExe: Boolean;
begin
  Result := not UseModernExe;
end;

{ Name of the viewer exe this machine should actually launch. }
function PrimaryExeName: String;
begin
  if UseModernExe then
    Result := '{#AppExeName}'
  else
    Result := '{#AppExeNameX86}';
end;

function PrimaryExePath: String;
begin
  Result := ExpandConstant('{app}\') + PrimaryExeName;
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
    Result := 'Позволяет телефону Android просматривать папки этого ПК по сети (только чтение, SFTP). Для этого добавляется разрешение в брандмауэр Windows. Менеджер общего доступа откроется по завершении установки, чтобы вы выбрали папку и запустили сервер. Можно включить и позже, в самом Менеджере общего доступа.'
  else if IsLanguage('ukrainian') then
    Result := 'Дозволяє телефону Android переглядати папки цього ПК по мережі (лише читання, SFTP). Для цього додається дозвіл у брандмауер Windows. Менеджер спільного доступу відкриється після завершення встановлення, щоб ви вибрали теку й запустили сервер. Можна ввімкнути й пізніше, у самому Менеджері спільного доступу.'
  else
    Result := 'Lets an Android phone browse this PC''s folders over the network (read-only, SFTP). This adds one Windows Firewall exception. The Share Manager opens when setup finishes so you can pick a folder and start the server. Can also be enabled later from the Share Manager itself.';
end;

{ Shown when Setup itself is not elevated. The option is NOT withdrawn there: the whole
  privileged part of it is one firewall rule, so instead of sending the user away to do
  it later, Setup asks Windows for that single step at the end - one UAC prompt, nothing
  else elevated. }
function ServerFeaturesElevationHintText: String;
begin
  if IsLanguage('russian') then
    Result := 'Установка идёт без прав администратора, поэтому в конце Windows один раз спросит подтверждение - только чтобы добавить правило брандмауэра. Всё остальное ставится без него. Откажетесь - программа установится как обычно, а раздачу можно будет включить позже в Менеджере общего доступа.'
  else if IsLanguage('ukrainian') then
    Result := 'Встановлення йде без прав адміністратора, тож наприкінці Windows один раз запитає підтвердження - лише щоб додати правило брандмауера. Усе інше встановлюється без нього. Відмовитеся - програма встановиться як звичайно, а роздачу можна буде ввімкнути пізніше в Менеджері спільного доступу.'
  else
    Result := 'Setup is running without administrator rights, so at the end Windows will ask for approval once - only to add the firewall rule. Everything else installs without it. Decline and the program still installs normally; sharing can be turned on later from the Share Manager.';
end;

function ServerFeaturesElevationFailedText: String;
begin
  if IsLanguage('russian') then
    Result := 'Общий доступ не включён: подтверждение администратора не получено.' + #13#10#13#10 +
              'Программа установлена полностью, включая компонент раздачи. Включить общий доступ можно в любой момент: Менеджер общего доступа -> «Управление хостингом..».'
  else if IsLanguage('ukrainian') then
    Result := 'Спільний доступ не ввімкнено: підтвердження адміністратора не отримано.' + #13#10#13#10 +
              'Програму встановлено повністю, включно з компонентом роздачі. Увімкнути спільний доступ можна будь-коли: Менеджер спільного доступу -> «Керування хостингом..».'
  else
    Result := 'Folder sharing was not enabled: administrator approval was not given.' + #13#10#13#10 +
              'The program is fully installed, the sharing component included. You can turn sharing on at any time: Share Manager -> "Manage hosting..".';
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
  Result := AddQuotes(PrimaryExePath) + ' "%1"';
end;

function BuildAppIcon: String;
begin
  Result := AddQuotes(PrimaryExePath) + ',0';
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

function ShouldRegisterAssociations: Boolean;
begin
  Result := (RegisterAssociationsCheckBox <> nil) and RegisterAssociationsCheckBox.Checked;
end;

function ShouldInstallServerFeatures: Boolean;
begin
  { Only when explicitly ticked, and only with the Share component installed - there
    is no worker exe to allow through the firewall otherwise. Elevation is NOT a
    condition any more: a non-elevated Setup asks Windows for the one privileged step
    (see EnableServerFeaturesElevated). In silent/winget installs the page is never
    shown, so Checked stays False and this is a no-op - nothing is ever enabled
    without someone ticking the box. }
  Result := (ServerFeaturesCheckBox <> nil) and ServerFeaturesCheckBox.Checked and WizardIsComponentSelected('share');
end;

{ The single privileged step of the opt-in, performed by the same helper the in-app
  deferred opt-in uses (ServerFeatures.EnableViaElevation), through one ShellExecute
  "runas". Inno cannot elevate a Setup that is already running - the alternative would
  be relaunching the wizard from the start - and it does not need to: only the firewall
  rule needs administrator rights, and everything else this installer writes goes to a
  per-user location. Returns False when the user dismisses the UAC prompt. }
function EnableServerFeaturesElevated: Boolean;
var
  ResultCode: Integer;
  ScriptPath: String;
  Params: String;
begin
  Result := False;
  ScriptPath := ExpandConstant('{app}\enable-share-server.ps1');
  if not FileExists(ScriptPath) then
    exit;
  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '"' +
            ' -ExePath "' + ExpandConstant('{app}\companion\fms-share-worker.exe') + '"';
  if not ShellExec('runas', 'powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    exit;
  Result := (ResultCode = 0);
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
  { Deleting by NAME alone leaves rules behind for good. The named one is ours, but
    the worker also collects rules Windows itself creates: the first time it listens,
    the firewall prompt appears and an "Allow" writes a rule named after the program
    (fms-share-worker), which no uninstall of ours has ever touched. Those accumulate
    one per install location and outlive the files they point at. Deleting by PROGRAM
    path catches every rule aimed at the exe being removed, whatever its name, and is
    precisely scoped: the path is inside the directory this uninstall is deleting, so
    another installation's rule cannot match. }
  Exec('netsh', 'advfirewall firewall delete rule name=all program="' +
    ExpandConstant('{app}\companion\fms-share-worker.exe') + '"',
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
  { --show: a bare launch obeys the Share Manager's own "open the manager window at
    startup" option (off by default) and would stay tray-only - here the user has just
    ticked "start sharing after installation", so the window is what was asked for. }
  ShellExec('open', Exe, '--show', ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

procedure RegisterOpenWithSupport(const Ext: String);
var
  AppKey: String;
begin
  { Keyed by the exe that runs here, so Explorer's "Open with" offers a working
    program on Windows 7/8.1 too (see UseModernExe). }
  AppKey := 'Software\Classes\Applications\' + PrimaryExeName;
  RegWriteStringValue(HKCU, AppKey, 'FriendlyAppName', '{#AppName}');
  RegWriteStringValue(HKCU, AppKey + '\DefaultIcon', '', BuildAppIcon);
  RegWriteStringValue(HKCU, AppKey + '\shell\open\command', '', BuildAppCommand);
  RegWriteStringValue(HKCU, AppKey + '\SupportedTypes', Ext, '');
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
begin
  { A per-user (no-admin) install used to be forced to "compact" here, with every
    optional row greyed out, to match the [Files] admin gate. Both are gone - see the
    comment above the optional [Files] entries: those components install fine without
    elevation, and gating them turned every winget install into a viewer-only one.
    The components page is now the same free choice in both install modes. }

  { The "run the SFTP server after install" opt-in requires two things: the Share
    component must be installed (screen 1 - otherwise there is no worker exe to run)
    AND setup must be elevated (the firewall rule needs admin). Grey the checkbox
    and explain the relevant reason when either is missing. }
  if (InstallOptionsPage <> nil) and (CurPageID = InstallOptionsPage.ID) then
  begin
    ShareSelected := WizardIsComponentSelected('share');
    if ServerFeaturesCheckBox <> nil then
    begin
      { The component decides, not elevation: a non-elevated Setup can still turn this
        on, it just asks Windows for the firewall step at the end. }
      ServerFeaturesCheckBox.Enabled := ShareSelected;
      if not ServerFeaturesCheckBox.Enabled then
        ServerFeaturesCheckBox.Checked := False;
    end;
    if ServerFeaturesHintLabel <> nil then
    begin
      if not IsAdminInstallMode and ShareSelected then
        ServerFeaturesHintLabel.Caption := ServerFeaturesHintText + #13#10#13#10 + ServerFeaturesElevationHintText
      else if not ShareSelected then
        ServerFeaturesHintLabel.Caption := ServerFeaturesNeedsShareHintText
      else
        ServerFeaturesHintLabel.Caption := ServerFeaturesHintText;
      WizardForm.AdjustLabelHeight(ServerFeaturesHintLabel);
    end;
  end;
end;

{ PrepareToInstall stops the Server edition's service (stop-companion.ps1) so its
  worker releases the files Setup is about to replace - but nothing ever started it
  again, so every viewer update silently left a server machine not serving until the
  next reboot. Only an Automatic service is restarted: that start type IS the
  statement "this must be running", whereas a Manual/Disabled one was stopped by its
  owner and must stay stopped. Best-effort and silent - a viewer install must not
  fail over the sharing service. }
procedure RestartShareServiceIfAutomatic;
var
  ResultCode: Integer;
begin
  Exec('powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -Command "Get-Service -Name ''' + ShareServiceName +
    ''' -ErrorAction SilentlyContinue | Where-Object { $_.StartType -eq ''Automatic'' -and $_.Status -ne ''Running'' } | Start-Service -ErrorAction SilentlyContinue"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and ShouldRegisterAssociations then
    RegisterRequestedImageAssociations;
  if CurStep = ssPostInstall then
    RestartShareServiceIfAutomatic;
  if (CurStep = ssPostInstall) and ShouldInstallServerFeatures then
  begin
    { The marker is the gate the non-elevated app reads, so it is written ONLY once the
      firewall rule really is in place - otherwise the feature would look enabled while
      no connection could ever reach the worker. }
    if IsAdminInstallMode then
    begin
      AddServerFirewallRule;
      WriteServerFeaturesMarker;
    end
    else if EnableServerFeaturesElevated then
      WriteServerFeaturesMarker
    else
      SuppressibleMsgBox(ServerFeaturesElevationFailedText, mbInformation, MB_OK, IDOK);
  end;
  { Launch the Share Manager once the install is fully done (after the Finished
    page), so the user lands in the UI to pick a folder and start the server -
    the "run it right after installation" opt-in. Guarded to interactive installs
    (silent/winget never show the page, so ShouldInstallServerFeatures is False). }
  if (CurStep = ssDone) and ShouldInstallServerFeatures and (not WizardSilent) then
    LaunchShareManager;
end;

{ Always-on hosting outlives the app on purpose - the service runs its own staged copy
  of the worker from %ProgramData% - so uninstalling the viewer would otherwise leave a
  listening SFTP server behind with nothing left on the machine to manage it. Offer to
  take the role away in the same breath. Only OUR registration (see ShareServiceIsAppOwned):
  the separate Server edition has its own uninstaller and is none of this one's business. }
procedure OfferShareServiceRemoval;
var
  ResultCode: Integer;
  ScriptPath: String;
  Params: String;
begin
  if not ShareServiceIsAppOwned then
    exit;
  ScriptPath := ExpandConstant('{app}\install-share-service.ps1');
  if not FileExists(ScriptPath) then
    exit;
  { A silent uninstall must never raise a UAC prompt, so the role is left in place -
    the safe half: it keeps serving exactly what was explicitly published, and the
    Share Manager (or a reinstall) can still remove it. }
  if UninstallSilent then
    exit;
  if SuppressibleMsgBox(ShareServiceRemovalPromptText, mbConfirmation, MB_YESNO, IDYES) <> IDYES then
    exit;

  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '"' +
            ' -Action remove' +
            ' -ExePath "' + ExpandConstant('{app}\companion\fms-share-worker.exe') + '"' +
            ' -DataDir "' + MachineShareDataDir + '"' +
            ' -UserDataDir "' + ExpandConstant('{localappdata}\FastMediaSorterCompanion') + '"';
  ShellExec('runas', 'powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usUninstall then
    exit;

  { Before the files go: the service holds its own copy, but the roots ACL ledger and
    the firewall rule are removed by the same helper call. }
  OfferShareServiceRemoval;
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
  { Both names: which one was registered depends on the OS this was installed on. }
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Applications\{#AppExeName}');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Applications\{#AppExeNameX86}');
end;
