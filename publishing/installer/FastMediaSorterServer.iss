; Fast Media Sorter & Sharing: Folder Share Server - Inno Setup script (the SERVER edition)
; Built by CI / tools\Build-ServerInstaller.ps1; invoke with:
;   ISCC.exe /DVersion=<x.y.z.w> /DSourceDir=<staged-tree> /O<output-dir> FastMediaSorterServer.iss
;
; See docs/specifications/done/SPECIFICATION_SHARE_SYSTEM_SERVICE.md.
;
; What this is, in one paragraph: the SAME Share Manager and the SAME Go worker as the
; User edition, hosted by the Windows SCM instead of by an interactive session, so the
; selected folders stay reachable from boot with nobody signed in. It is a packaging
; and host-mode distinction - not a fork: one SFTP implementation, one IPC schema, one
; .fmscfg format, one Android reconnect contract, one persistent identity.
;
; Why it is a SEPARATE script rather than a flag on FastMediaSorter.iss: it is a
; different product entry (its own AppId, ARP name, directory and winget package), it
; is always elevated, its wizard asks different questions, and it deliberately carries
; a much smaller payload - no VLC codecs, no OCR models, and the viewer only as an
; optional component that stays unticked (ticket 132). The one thing the
; two installers MUST agree on lives in neither of them: the frozen service name and
; the on-disk state format, both owned by the worker.

#ifndef Version
  #define Version "0.0.0.0"
#endif

#ifndef SourceDir
  #error SourceDir must be defined (path to the staged build output)
#endif

; The OPTIONAL viewer component (ticket 132, Q132-1): the server leads and the viewer rides
; along, unchecked. Its payload is staged under <SourceDir>\viewer\ by
; tools\Stage-ServerViewerPayload.ps1 - the same files the User installer's "compact" type
; installs (both viewer exes and their libraries, no VLC codecs, no OCR models). A stage
; without that folder (a hand compile, a local build with no User stage of the same
; version) compiles a Server installer with no viewer component at all, so the component
; can never be offered with nothing behind it; the packagers that SHIP require the folder.
#if FileExists(AddBackslash(SourceDir) + "viewer\FastMediaSorter_LITE.exe")
  #define HaveViewer
#endif

; The wizard name of this edition (docs/guides/PRODUCT_NAMING.md section 1, "Server
; edition"). A plain "&" is right in the [Setup] directives and message boxes; a wizard
; label would need it doubled (this script builds none).
#define AppName       "Fast Media Sorter & Sharing: Folder Share Server"
; The name of the SHORTCUTS. A shortcut is a file and a colon is not a legal file-name
; character, so the component comes first and the product follows as the suffix, the
; same pattern as the Share Manager's ("Share Manager - Fast Media Sorter & Sharing").
#define AppShortcutName "Folder Share Server - Fast Media Sorter & Sharing"
; The name of the User edition, for the messages that speak about it.
#define UserEditionName "Fast Media Sorter & Sharing for Windows"
; ARP DisplayName - FROZEN, and spelled out on purpose instead of following AppName: the
; installed copies already carry this exact string and winget correlates on it. NOT the
; User edition's frozen ARP name either: the two must correlate to two DIFFERENT winget
; packages, and a shared DisplayName would make "winget upgrade" hand a machine the
; wrong edition.
#define AppNameArp    "Fast Media Sorter Folder Share Server"
; Start-menu GROUP - deliberately NOT {#AppName}: both editions live under ONE product
; folder in the Start menu, so this must stay byte-identical to the User edition's
; DefaultGroupName in FastMediaSorter.iss (its {#AppName}). The two installers write
; into the same group without touching each other's shortcuts - Inno removes only the
; entries it created, and the emptied folder goes with whichever uninstaller runs last.
; This is a shortcut-placement name only, NOT an identity anchor: AppId, the ARP
; DisplayName and the winget package stay distinct per edition.
#define AppGroupName  "Fast Media Sorter & Sharing for Windows"
; The optional viewer's shortcut. NOT {#AppGroupName}: that is the User edition's own
; shortcut name in the same shared group, and two different targets must never overwrite
; each other's entry. Component first, product as suffix - the Share Manager's pattern.
#define ViewerShortcutName "Viewer - Fast Media Sorter & Sharing"
#define ViewerExe     "FastMediaSorter_LITE.exe"
; The exchange server's name (docs/guides/PRODUCT_NAMING.md section 1; src/ProductNames.vb
; ExchangeServer): never shortened, not translated, Latin in every setup language.
#define ExchangeServerName "Media Exchange Server"
; THE SHIPPING GATE (specification 133 section 8, ticket 141): the packagers pass /DNoExchange
; unless -IncludeExchange / INCLUDE_EXCHANGE opened it. The `exchange` component, the
; `exchangeservice` task, their [Files], messages and code then compile out (#ifndef NoExchange).
#define AppPublisher  "SerZhyAle"
#define CompanionExe  "FastMediaSorterCompanion.exe"
#define WorkerExe     "fms-share-worker.exe"
#define AppURL        "https://github.com/SerZhyAle/FastMediaSorter_Lite"

[Setup]
; Distinct from the User edition's {{7371E7F1-...}: the two are separate ARP entries and
; separate upgrade lines. NEVER reuse the other one - a shared AppId would let either
; installer silently uninstall the other.
AppId={{A9F3C61B-4D2E-4F58-9C7A-1E6B0D3F82A4}
AppName={#AppName}
AppVersion={#Version}
AppVerName={#AppName} {#Version}
UninstallDisplayName={#AppNameArp} {#Version}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\FastMediaSorter_Server
DefaultGroupName={#AppGroupName}
DisableProgramGroupPage=yes
; MUST stay "no" while the page above is disabled. Inno's default is to reuse the group
; name a PREVIOUS install of this AppId recorded, and DefaultGroupName is then only a
; fallback nobody reaches - so up to 26.9.1 the move into the shared product folder
; never happened on an upgrade: the [InstallDelete] below removed the old standalone
; folder and Inno recreated the shortcuts straight back into it, because that name was
; still what "Inno Setup: Icon Group" said. The group is not a user choice here (the
; page is off), so there is nothing to remember and this directive is what makes
; DefaultGroupName mean what it says.
UsePreviousGroup=no
LicenseFile={#SourceDir}\LICENSE
OutputBaseFilename=FastMediaSorter-{#Version}-windows-x64-server-setup
; This .iss lives at <repo>\publishing\installer, so the repo-root assets tree is two levels up.
SetupIconFile=..\..\assets\icons\Fast_Media_Sorter.ico
UninstallDisplayIcon={app}\{#CompanionExe}
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
; ALWAYS elevated, with no per-user override offered: a Windows service, a machine
; state directory with its own ACL, folder ACL grants and a firewall rule are all
; machine-scope. There is no meaningful "just for me" variant of this product, and
; offering one would only produce an install that cannot do its job.
PrivilegesRequired=admin
; The Share Manager and the worker are .NET 10 x64 / a 64-bit Go binary, and a service
; that survives logoff is the whole point - so the Windows 7/8.1 floor the User
; installer keeps (for its 32-bit viewer) does not apply here.
MinVersion=10.0.14393
; Setup replaces the Companion exe, so a running instance must be gone first - and
; that is done by stop-companion.ps1 from PrepareToInstall (service through the SCM
; first, then the tray app, then any surviving worker). Deliberately NO AppMutex on
; FastMediaSorterCompanionSingleInstanceMutex: Inno checks it at startup, BEFORE
; PrepareToInstall ever runs, and the Share Manager sits in the tray from logon, so it
; is practically always held. Interactively that is a pointless "close it" prompt the
; user cannot satisfy (a window-close only returns it to the tray); silently -
; /SUPPRESSMSGBOXES, i.e. winget and reinstall-server.ps1 - the prompt is answered
; Cancel and every upgrade aborts with exit code 1 before touching a file.
VersionInfoVersion={#Version}
VersionInfoCompany={#AppPublisher}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#Version}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"

[Tasks]
Name: "openmanager"; Description: "{cm:OpenManagerTask}"
#ifndef NoExchange
; The Media Exchange Server as a Windows service is its OWN choice, unticked: the component alone
; only lays the files down (like the User edition), and nothing is registered, started or allowed
; through the firewall unless this box is ticked (or /TASKS=exchangeservice is passed). Offered
; only with the component (Components: exchange).
Name: "exchangeservice"; Description: "{cm:ExchangeServiceTask}"; Components: exchange; Flags: unchecked
#endif

[CustomMessages]
; Role order (ticket 132 section 3.3): the server leads and is the only required part; the
; two secondary roles are one-line, optional, and say they are off until chosen.
english.CompServer=Folder Share Server - Share Manager, SFTP worker and the Windows service (required)
russian.CompServer=Сервер раздачи папок - Менеджер общего доступа, SFTP-воркер и служба Windows (обязательно)
ukrainian.CompServer=Сервер роздачі тек - Менеджер спільного доступу, SFTP-воркер і служба Windows (обов’язково)
english.CompFileDO=FileDO virtual disk host - optional extra (disk sharing stays off until you enable it)
russian.CompFileDO=Хост виртуальных дисков FileDO - необязательное дополнение (общий доступ к дискам включается отдельно)
ukrainian.CompFileDO=Хост віртуальних дисків FileDO - необов’язкове доповнення (спільний доступ до дисків вмикається окремо)
english.CompViewer=Media viewer and sorter - optional extra, not installed unless ticked (codecs and OCR models download on demand)
russian.CompViewer=Просмотрщик и сортировщик медиа - необязательное дополнение, не ставится, пока вы его не отметите (кодеки и модели OCR скачаются по мере надобности)
ukrainian.CompViewer=Переглядач і сортувальник медіа - необов’язкове доповнення, не ставиться, доки ви його не позначите (кодеки та моделі OCR завантажаться за потреби)
#ifndef NoExchange
english.CompExchange={#ExchangeServerName} - optional extra; a relay that lets share servers and clients reach each other (not installed unless ticked; the Windows service is a separate choice on the tasks page)
russian.CompExchange={#ExchangeServerName} - необязательное дополнение: сервер обмена, через который серверы раздачи и клиенты находят друг друга (не ставится, пока вы его не отметите; служба Windows - отдельный выбор на странице задач)
ukrainian.CompExchange={#ExchangeServerName} - необов’язкове доповнення: сервер обміну, через який сервери роздачі та клієнти знаходять одне одного (не ставиться, доки ви його не позначите; служба Windows - окремий вибір на сторінці завдань)
english.ExchangeServiceTask=Run the {#ExchangeServerName} as a Windows service (starts at boot; adds one program-scoped firewall rule)
russian.ExchangeServiceTask=Запускать {#ExchangeServerName} как службу Windows (стартует при загрузке; добавляет одно правило брандмауэра для этой программы)
ukrainian.ExchangeServiceTask=Запускати {#ExchangeServerName} як службу Windows (стартує під час завантаження; додає одне правило брандмауера для цієї програми)
english.CollectStateIcon=Collect host state for support
russian.CollectStateIcon=Собрать состояние сервера для поддержки
ukrainian.CollectStateIcon=Зібрати стан сервера для підтримки
#endif
english.ViewerBlockedSuffix=- not offered here: the User edition is already installed
russian.ViewerBlockedSuffix=- здесь не предлагается: пользовательская редакция уже установлена
ukrainian.ViewerBlockedSuffix=- тут не пропонується: користувацьку редакцію вже встановлено
english.TypeServerCustom=Server - choose the optional extras
russian.TypeServerCustom=Сервер - выберите необязательные дополнения
ukrainian.TypeServerCustom=Сервер - виберіть необов’язкові доповнення
english.OpenManagerTask=Open the Share Manager after installation to choose the folders to publish
russian.OpenManagerTask=Открыть Менеджер общего доступа после установки, чтобы выбрать публикуемые папки
ukrainian.OpenManagerTask=Відкрити Менеджер спільного доступу після встановлення, щоб вибрати публіковані теки

; The wizard's first screen and the components page lead with the server role and name the
; two secondary roles once, as optional and off until chosen. [name/ver] is Inno's own token.
[Messages]
english.WelcomeLabel2=This will install [name/ver] on your computer.%n%nThe server leads: it publishes the folders you choose over SFTP from boot, with nobody signed in, and it shares nothing until you choose folders. The next page offers two optional extras: FileDO virtual disk hosting (ticked - clear it if you do not need it) and the media viewer and sorter (unticked - tick it to add it).%n%nIt is recommended that you close all other applications before continuing.
russian.WelcomeLabel2=Это установит [name/ver] на ваш компьютер.%n%nГлавная роль здесь у сервера: он раздаёт выбранные вами папки по SFTP с загрузки Windows, когда никто не вошёл в систему, и ничего не раздаёт, пока вы не выберете папки. На следующей странице два необязательных дополнения: хостинг виртуальных дисков FileDO (отмечено - снимите галочку, если он не нужен) и просмотрщик и сортировщик медиа (не отмечено - отметьте, чтобы добавить).%n%nРекомендуется закрыть все остальные приложения перед продолжением.
ukrainian.WelcomeLabel2=Це встановить [name/ver] на ваш комп’ютер.%n%nГоловна роль тут у сервера: він роздає вибрані вами теки по SFTP із завантаження Windows, коли ніхто не ввійшов у систему, і нічого не роздає, доки ви не виберете теки. На наступній сторінці два необов’язкові доповнення: хостинг віртуальних дисків FileDO (позначено - зніміть позначку, якщо він не потрібен) і переглядач та сортувальник медіа (не позначено - позначте, щоб додати).%n%nРекомендовано закрити всі інші програми перед продовженням.
english.SelectComponentsLabel2=The Folder Share Server is always installed. FileDO virtual disk hosting (ticked) and the media viewer and sorter (unticked) are optional extras; sharing stays off until you choose folders. Click Next when you are ready to continue.
russian.SelectComponentsLabel2=Сервер раздачи папок устанавливается всегда. Хостинг виртуальных дисков FileDO (отмечен) и просмотрщик и сортировщик медиа (не отмечен) - необязательные дополнения; раздача остаётся выключенной, пока вы не выберете папки. Нажмите "Далее", когда будете готовы продолжить.
ukrainian.SelectComponentsLabel2=Сервер роздачі тек встановлюється завжди. Хостинг віртуальних дисків FileDO (позначено) і переглядач та сортувальник медіа (не позначено) - необов’язкові доповнення; роздача лишається вимкненою, доки ви не виберете теки. Натисніть "Далі", коли будете готові продовжити.

[Types]
Name: "custom"; Description: "{cm:TypeServerCustom}"; Flags: iscustom

[Components]
; Types: custom - a custom-only list starts with the component ticked (a component with no
; Types starts unticked, and the service then reports "bundled FileDO is missing"). The user can
; still untick it; sharing disks stays off until enabled either way.
; "server" is the package itself: fixed, first, and its files carry no Components clause (they
; are always installed), so the entry exists to lead the page and say what the package IS.
; "viewer" has NO Types on purpose: a component without one starts unticked, which is the whole
; rule of the secondary role - the default payload of this package stays exactly the server's,
; and a silent install (winget) never gets a viewer it did not ask for (/COMPONENTS=viewer does).
Name: "server"; Description: "{cm:CompServer}"; Types: custom; Flags: fixed
Name: "filedo"; Description: "{cm:CompFileDO}"; Types: custom
#ifdef HaveViewer
Name: "viewer"; Description: "{cm:CompViewer}"
#endif
#ifndef NoExchange
; Same rule as the viewer: NO Types, so it starts unticked and the default payload of this package
; stays exactly the server's. It is listed AFTER the viewer on purpose - InitializeWizard addresses
; the viewer row by index. dontinheritcheck: not a child of anything.
Name: "exchange"; Description: "{cm:CompExchange}"; Flags: dontinheritcheck
#endif

[Files]
; The Share Manager (self-contained .NET 10) + the headless Go worker. Deliberately
; no VLC codecs and no OCR models, and the viewer only as the optional component
; below: this package's job is an always-on file share on a machine that may well
; have no interactive user at all.
Source: "{#SourceDir}\{#CompanionExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\companion\*"; DestDir: "{app}\companion"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#SourceDir}\filedo\*"; DestDir: "{app}\filedo"; Flags: recursesubdirs createallsubdirs ignoreversion; Components: filedo
#ifdef HaveViewer
; The OPTIONAL viewer (unticked by default, ticket 132): the mainline exe and the libraries it
; loads, laid down NEXT TO the Share Manager exactly as the User edition lays them down - the
; viewer's "Share this folder" finds FastMediaSorterCompanion.exe beside itself. No codecs and
; no OCR models: those download on first use, as in the User edition's "compact" type.
Source: "{#SourceDir}\viewer\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion; Components: viewer
#endif
#ifndef NoExchange
; The Media Exchange Server tree (exe, .sha256, the two helpers, README.txt). Without
; skipifsourcedoesntexist: a setup that claims the component carries it. The service helper
; stages the exe into ProgramData at registration; the firewall helper is run by the Share Manager.
Source: "{#SourceDir}\exchange\*"; DestDir: "{app}\exchange"; Flags: recursesubdirs createallsubdirs ignoreversion; Components: exchange
; The offline state collector (ticket 182 L-9): read-only, works when the Share Manager does not start
; and no mail client exists. Gated with the exchange tree, so a gate-shut build carries nothing new.
Source: "collect-host-state.ps1"; DestDir: "{app}"; Flags: ignoreversion
#endif
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourceDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
; The worker statically links a dozen Go packages (SFTP, ed25519, mDNS, UPnP/NAT-PMP);
; their notices ship WITH the package, not only in the repository.
Source: "{#SourceDir}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; The elevated management helper - the ONE place that registers, repairs, starts,
; stops, removes and migrates the service. Both as an installed file (the Share
; Manager's Hosting console runs it later) and as a temp extract (Setup itself runs it
; before {app} is populated on a repair path).
Source: "install-share-service.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "install-share-service.ps1"; DestDir: "{tmp}"; Flags: dontcopy
; Stops the service, the Companion and the worker before files are replaced.
Source: "stop-companion.ps1"; DestDir: "{tmp}"; Flags: dontcopy
Source: "stop-companion.ps1"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; Up to and including 26.8.15 this edition owned a Start-menu group of its own, named
; after the product. Inno never removes shortcuts it created in a previous install, so
; an upgrade would leave that folder standing next to the shared one with a stale copy
; of both entries. It only ever held this product's own shortcuts, and it is always
; {commonprograms} here (PrivilegesRequired=admin), so removing it outright is safe.
; Harmless on a fresh install, where the folder does not exist.
Type: filesandordirs; Name: "{commonprograms}\Fast Media Sorter Folder Share Server"
; The shared group followed the User edition's rename: it used to be "Fast Media Sorter
; for Windows" and is now the current AppGroupName. Remove only THIS edition's own
; entries from the old folder (the shortcut under its former name, the uninstall
; shortcut in each setup language, the former desktop shortcut) and then the folder if
; it is empty - never the folder outright, because a User edition upgraded earlier or
; later may still keep its own shortcuts in it until its installer clears them.
Type: files; Name: "{commonprograms}\Fast Media Sorter for Windows\Fast Media Sorter Folder Share Server.lnk"
Type: files; Name: "{commonprograms}\Fast Media Sorter for Windows\* Fast Media Sorter Folder Share Server.lnk"
Type: dirifempty; Name: "{commonprograms}\Fast Media Sorter for Windows"
; The desktop shortcut under its former name: Inno never removes a shortcut that an
; earlier install created under a name this one no longer writes.
Type: files; Name: "{autodesktop}\Fast Media Sorter Folder Share Server.lnk"

; The uninstall shortcut is named in the language the install ran in ("Uninstall .." /
; "Удалить .." / "Видалити .."), and Inno leaves the previous language's copy behind when
; that changes - so a machine reinstalled in two languages ends up with two of them. The
; leading space is deliberate: it matches only the "<verb> <AppName>.lnk" forms and never
; "{#AppShortcutName}.lnk" itself, so the product's own shortcut is not deleted and recreated
; (which would break a taskbar pin resolving through it). [InstallDelete] runs before
; [Icons], so the current language's copy is written back immediately.
Type: files; Name: "{group}\* {#AppShortcutName}.lnk"

[UninstallDelete]
; The Share Manager writes companion.log next to its exe when that folder is writable;
; nothing else removes it (ticket 055 I-03, the same entries as the User installer).
Type: files; Name: "{app}\companion.log"
Type: files; Name: "{app}\companion.log.old"
#ifndef NoExchange
; The exchange tree is wholly ours. Its DATA (certificate identity, settings, log) lives under
; %ProgramData%\FastMediaSorterExchange and is NEVER listed: it holds the identity paired
; producers pinned, and deleting it is a separate explicit choice (the service helper's remove
; verb keeps it, ticket 144 T-6).
Type: filesandordirs; Name: "{app}\exchange"
#endif
#ifdef HaveViewer
; The optional viewer's own run-time logs next to its exe (AppFileLogger) - not [Files]
; entries, so without these the install folder survives an uninstall that removed the viewer.
Type: files; Name: "{app}\current.log"
Type: files; Name: "{app}\previous.log"
#endif

[Icons]
; "--show": a bare launch obeys the manager's own "open the window at startup" option,
; which is OFF by default, so without the flag this Start-menu item would start a
; tray-only process and read as a click that did nothing. Clicking a shortcut IS the
; explicit "open it" gesture, the same one the viewer's Share Manager button makes.
Name: "{group}\{#AppShortcutName}"; Filename: "{app}\{#CompanionExe}"; Parameters: "--show"
Name: "{group}\{cm:UninstallProgram,{#AppShortcutName}}"; Filename: "{uninstallexe}"
; Same "--show" reasoning. A desktop icon matters more here than on the viewer: the
; console has no file association, nothing opens it by double-clicking a document, and
; the Start-menu entry sits under a product folder named after the VIEWER (the shared
; group), which is not where someone looks for a server they just installed. Create it
; on every install, including upgrades that remembered an unchecked desktop task.
Name: "{autodesktop}\{#AppShortcutName}"; Filename: "{app}\{#CompanionExe}"; Parameters: "--show"
#ifndef NoExchange
; Ticket 182 L-9: a Start-menu entry for the offline state collector (never the desktop). It runs
; with -Pause so the window stays readable, and it only reads: no service is touched.
Name: "{group}\{cm:CollectStateIcon}"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\collect-host-state.ps1"" -Pause"; WorkingDir: "{app}"; IconFilename: "{app}\exchange\fms-exchange-server.exe"
#endif
#ifdef HaveViewer
; The secondary role gets an entry ONLY when its component was ticked, and only in the Start
; menu - never the desktop: the server's own icon is the one that leads (ticket 132 section
; 3.3). Inno removes it with the rest of this installation's entries.
Name: "{group}\{#ViewerShortcutName}"; Filename: "{app}\{#ViewerExe}"; Components: viewer
#endif

[Code]
const
  ShareServiceName = 'FastMediaSorterCompanionSFTP';
  { The User edition's frozen AppId, used only to DETECT it (spec §1.4 - the two
    editions never coexist as independent live installations). }
  UserEditionAppId = '{7371E7F1-B8A8-4786-8173-5F5B2B6E6AC9}_is1';

function IsLanguage(const Lang: String): Boolean;
begin
  Result := CompareText(ActiveLanguage, Lang) = 0;
end;

function UninstallKey: String;
begin
  Result := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + UserEditionAppId;
end;

{ Is a User edition installed on this machine? Checked in both hives because that
  installer offers a per-user and an all-users mode. }
function UserEditionInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, UninstallKey) or RegKeyExists(HKCU, UninstallKey);
end;

function ServiceAlreadyRegistered: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\' + ShareServiceName);
end;

function MigrationNoticeText: String;
begin
  if IsLanguage('russian') then
    Result := 'На этом компьютере уже установлена пользовательская редакция {#UserEditionName}.' + #13#10#13#10 +
              'Серверная редакция не работает рядом с ней: обе редакции используют один и тот же канал управления, порт и ключ узла. Установка ПЕРЕНЕСЁТ текущее состояние (ключ узла, пароль, список папок, порт) в машинное хранилище и передаст раздачу службе Windows.' + #13#10#13#10 +
              'Отпечаток ключа узла сохранится - привязанные телефоны продолжат подключаться без повторной привязки. Просмотрщик останется установленным и продолжит работать.' + #13#10#13#10 +
              'Продолжить?'
  else if IsLanguage('ukrainian') then
    Result := 'На цьому комп''ютері вже встановлено користувацьку редакцію {#UserEditionName}.' + #13#10#13#10 +
              'Серверна редакція не працює поруч із нею: обидві редакції використовують один канал керування, порт і ключ вузла. Встановлення ПЕРЕНЕСЕ поточний стан (ключ вузла, пароль, список тек, порт) у машинне сховище й передасть роздачу службі Windows.' + #13#10#13#10 +
              'Відбиток ключа вузла збережеться - прив''язані телефони продовжать підключатися без повторної прив''язки. Переглядач залишиться встановленим і працюватиме далі.' + #13#10#13#10 +
              'Продовжити?'
  else
    Result := 'A User edition of {#UserEditionName} is already installed on this computer.' + #13#10#13#10 +
              'The Server edition cannot run alongside it: both editions use the same control channel, the same port and the same host key. Setup will MIGRATE the current state (host key, password, folder list, port) into the machine store and hand the sharing over to a Windows service.' + #13#10#13#10 +
              'The host-key fingerprint is preserved, so paired phones keep connecting without re-pairing. The viewer stays installed and keeps working.' + #13#10#13#10 +
              'Continue?';
end;

function SilentConflictText: String;
begin
  if IsLanguage('russian') then
    Result := 'Обнаружена пользовательская редакция {#UserEditionName}. Тихая установка серверной редакции поверх неё остановлена: она выполнила бы перенос состояния без вашего подтверждения. Запустите установщик в обычном режиме или передайте /MIGRATEFROMUSER, чтобы явно разрешить перенос.'
  else if IsLanguage('ukrainian') then
    Result := 'Виявлено користувацьку редакцію {#UserEditionName}. Тиху установку серверної редакції поверх неї зупинено: вона виконала б перенесення стану без вашого підтвердження. Запустіть установник у звичайному режимі або передайте /MIGRATEFROMUSER, щоб явно дозволити перенесення.'
  else
    Result := 'A User edition of {#UserEditionName} was detected. This silent Server installation was stopped because it would migrate your state without confirmation. Run Setup interactively, or pass /MIGRATEFROMUSER to allow the migration explicitly.';
end;

function ServiceFailedText: String;
begin
  if IsLanguage('russian') then
    Result := 'Не удалось зарегистрировать или запустить службу общего доступа. Файлы установлены; откройте Менеджер общего доступа и нажмите «Настройки менеджера..» -> «Хостинг» -> «Восстановить регистрацию службы». Подробности - в файле install-share-service.log в каталоге ProgramData\FastMediaSorterCompanion.'
  else if IsLanguage('ukrainian') then
    Result := 'Не вдалося зареєструвати або запустити службу спільного доступу. Файли встановлено; відкрийте Менеджер спільного доступу й натисніть «Налаштування менеджера..» -> «Хостинг» -> «Відновити реєстрацію служби». Подробиці - у файлі install-share-service.log у каталозі ProgramData\FastMediaSorterCompanion.'
  else
    Result := 'The folder-share service could not be registered or started. The files are installed; open the Share Manager and use "Manager settings.." -> "Hosting" -> "Repair the service registration". Details are in install-share-service.log under ProgramData\FastMediaSorterCompanion.';
end;

function MachineDataDir: String;
begin
  Result := ExpandConstant('{commonappdata}\FastMediaSorterCompanion');
end;

{ The per-user store of the User edition. In an elevated install this expands to the
  ELEVATING administrator's profile, which is usually NOT where the state lives - so
  it is passed only as a hint. The helper falls back to scanning the user profiles and
  refuses to guess when several hold an identity. }
function UserDataDirHint: String;
begin
  Result := ExpandConstant('{localappdata}\FastMediaSorterCompanion');
end;

function WorkerPath: String;
begin
  Result := ExpandConstant('{app}\companion\{#WorkerExe}');
end;

function MigrationAllowed: Boolean;
begin
  Result := (not UserEditionInstalled) or (not WizardSilent) or
            (CompareText(ExpandConstant('{param:MIGRATEFROMUSER|no}'), 'no') <> 0);
end;

#ifdef HaveViewer
{ The optional viewer is not offered next to an installed User edition: that edition already
  carries the viewer (its own directory, shortcut and file associations), and a second copy
  would race it for the single-instance mutex and the "default app" choice. The row stays on
  the page - nothing is hidden from the person who looks (ticket 132 section 3.3) - ticked
  off, disabled, and saying why. Index 2 = server, filedo, viewer in [Components] order. }
procedure InitializeWizard;
begin
  if UserEditionInstalled then
  begin
    WizardForm.ComponentsList.ItemCaption[2] :=
      WizardForm.ComponentsList.ItemCaption[2] + ' ' + CustomMessage('ViewerBlockedSuffix');
    WizardForm.ComponentsList.Checked[2] := False;
    WizardForm.ComponentsList.ItemEnabled[2] := False;
  end;
end;
#endif

function InitializeSetup: Boolean;
begin
  Result := True;
  if not UserEditionInstalled then
    exit;

  { Silent installs must fail clearly rather than migrate state unattended (spec §1.4). }
  if WizardSilent and not MigrationAllowed then
  begin
    SuppressibleMsgBox(SilentConflictText, mbError, MB_OK, IDOK);
    Result := False;
    exit;
  end;

  if not WizardSilent then
    Result := SuppressibleMsgBox(MigrationNoticeText, mbConfirmation, MB_YESNO, IDYES) = IDYES;
end;

procedure RunHelperScript(const ScriptPath, Action: String; var ResultCode: Integer);
var
  Params: String;
begin
  ResultCode := -1;
  if not FileExists(ScriptPath) then
    exit;
  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '"' +
            ' -Action ' + Action +
            ' -ExePath "' + WorkerPath + '"' +
            { The service must look for the bundled FileDO in THIS Program Files installation,
              not beside its staged copy under ProgramData (its default), which the worker's
              bundle check refuses for a service. Applies to install, repair, migrate-to-server
              and the in-place update alike - they all register through the one helper. }
            ' -FileDoRoot "' + RemoveBackslash(ExpandConstant('{app}')) + '"' +
            ' -DataDir "' + MachineDataDir + '"' +
            ' -UserDataDir "' + UserDataDirHint + '"';
  Exec('powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

#ifndef NoExchange
{ --- the Media Exchange Server (tickets 141 and 144) ------------------------------------
  Everything machine-affecting goes through the ONE helper, install-exchange-service.ps1, and
  only when the `exchangeservice` task was ticked or the service already exists: an upgrade where
  the task was not ticked never registers anything, and a machine that never had the service
  never gets one from an upgrade. The data directory is never touched by Setup. }
const
  ExchangeServiceName = 'FastMediaSorterExchangeServer';

function ExchangeServiceRegistered: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\' + ExchangeServiceName);
end;

function ExchangeHelperPath: String;
begin
  Result := ExpandConstant('{app}\exchange\install-exchange-service.ps1');
end;

function ExchangeExePath: String;
begin
  Result := ExpandConstant('{app}\exchange\fms-exchange-server.exe');
end;

function ExchangeMachineDataDir: String;
begin
  Result := ExpandConstant('{commonappdata}\FastMediaSorterExchange');
end;

{ The per-user store of a User-edition exchange server. In an elevated install this expands to
  the ELEVATING administrator's profile - a hint only, like UserDataDirHint above. }
function ExchangeUserDataDirHint: String;
begin
  Result := ExpandConstant('{localappdata}\FastMediaSorterExchange');
end;

{ The enrolled management user (Q144-1): the account that runs this Setup, whose SID the helper
  puts in the service's security descriptor so that user can start and stop without UAC. Read by
  PowerShell into a temp file (Inno has no SID API). Empty when it cannot be read or does not
  look like a SID - the helper then resolves it its own way, and never from a malformed value. }
function ExchangeManageSid: String;
var
  ResultCode: Integer;
  OutFile: String;
  Raw: AnsiString;
begin
  Result := '';
  OutFile := ExpandConstant('{tmp}\fms-exchange-manage-sid.txt');
  if not Exec('powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -Command "[System.IO.File]::WriteAllText(''' + OutFile +
    ''', [Security.Principal.WindowsIdentity]::GetCurrent().User.Value)"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    exit;
  if (ResultCode = 0) and LoadStringFromFile(OutFile, Raw) then
  begin
    Result := Trim(String(Raw));
    if Pos('S-1-', Result) <> 1 then
      Result := '';
  end;
end;

procedure RunExchangeHelper(const Verb: String; var ResultCode: Integer);
var
  Params: String;
  Sid: String;
begin
  ResultCode := -1;
  if not FileExists(ExchangeHelperPath) then
    exit;
  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ExchangeHelperPath + '"' +
            ' -Action ' + Verb +
            ' -ExePath "' + ExchangeExePath + '"' +
            ' -DataDir "' + ExchangeMachineDataDir + '"' +
            ' -UserDataDir "' + ExchangeUserDataDirHint + '"';
  Sid := ExchangeManageSid;
  if Sid <> '' then
    Params := Params + ' -ManageSid "' + Sid + '"';
  Exec('powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function ExchangeServiceFailedText: String;
begin
  if IsLanguage('russian') then
    Result := 'Не удалось зарегистрировать или запустить службу {#ExchangeServerName}. Файлы установлены; откройте Менеджер общего доступа и повторите регистрацию службы в группе {#ExchangeServerName}. Подробности - в журнале в каталоге ProgramData\FastMediaSorterExchange.'
  else if IsLanguage('ukrainian') then
    Result := 'Не вдалося зареєструвати або запустити службу {#ExchangeServerName}. Файли встановлено; відкрийте Менеджер спільного доступу й повторіть реєстрацію служби в групі {#ExchangeServerName}. Подробиці - у журналі в каталозі ProgramData\FastMediaSorterExchange.'
  else
    Result := 'The {#ExchangeServerName} service could not be registered or started. The files are installed; open the Share Manager and register the service again from its {#ExchangeServerName} group. Details are in the log under ProgramData\FastMediaSorterExchange.';
end;

{ ssPostInstall: an existing service is REPAIRED (re-staged from the files just installed,
  certificate identity untouched); a new one is registered only when the task was ticked, taking
  the user's per-user store with it when one exists (migrate-to-service is the whole transaction -
  it validates the certificate fingerprint BEFORE registering, as migrate-to-server does for the
  share worker). Nothing else ever registers it. }
procedure ApplyExchangeService;
var
  ResultCode: Integer;
begin
  if ExchangeServiceRegistered then
  begin
    RunExchangeHelper('repair', ResultCode);
    if (ResultCode <> 0) and (ResultCode <> -1) then
      SuppressibleMsgBox(ExchangeServiceFailedText, mbError, MB_OK, IDOK);
    exit;
  end;
  if not WizardIsTaskSelected('exchangeservice') then
    exit;
  if DirExists(ExchangeUserDataDirHint) then
    RunExchangeHelper('migrate-to-service', ResultCode)
  else
    RunExchangeHelper('install', ResultCode);
  if ResultCode <> 0 then
    SuppressibleMsgBox(ExchangeServiceFailedText, mbError, MB_OK, IDOK);
end;

{ Uninstall: remove the service and its staged rule (the helper's remove verb) and then - this
  Setup being elevated - any program-scoped rule the Share Manager added for the installed exe.
  The data directory is KEPT: it holds the certificate identity paired producers pinned. }
procedure RemoveExchangeServiceAndRule;
var
  ResultCode: Integer;
begin
  if FileExists(ExchangeHelperPath) and ExchangeServiceRegistered then
    RunExchangeHelper('remove', ResultCode);
  if FileExists(ExchangeExePath) then
    Exec('netsh', 'advfirewall firewall delete rule name=all program="' + ExchangeExePath + '"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
#endif

{ Stops the service, the Companion and the worker before Setup/Uninstall replaces or
  removes their files. Best-effort: a hiccup here must not abort Setup. -AppDir scopes
  the kill to THIS installation's processes, so another account's own sharing from
  another installation is left running (ticket 054 H-26). }
procedure StopEverything(const ScriptPath: String);
var
  ResultCode: Integer;
begin
  if not FileExists(ScriptPath) then
    exit;
  Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '"' +
    ' -AppDir "' + ExpandConstant('{app}') + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  ExtractTemporaryFile('stop-companion.ps1');
  StopEverything(ExpandConstant('{tmp}\stop-companion.ps1'));
end;

procedure LaunchShareManager;
var
  ResultCode: Integer;
  Exe: String;
begin
  Exe := ExpandConstant('{app}\{#CompanionExe}');
  if not FileExists(Exe) then
    exit;
  { --show: a bare launch obeys the Manager's own "open the window at startup" option
    (off by default). Here the user explicitly asked to configure the roots now. }
  ShellExec('open', Exe, '--show', ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    { ONE call does the whole transaction in the documented order: stop the User
      worker, copy and validate the state (aborting before registration on any
      fingerprint mismatch), lock down the machine directory, register the service,
      add the firewall rule, start and verify. With nothing to migrate it degrades to
      a plain fresh install - so there is one code path, not two. }
    RunHelperScript(ExpandConstant('{app}\install-share-service.ps1'), 'migrate-to-server', ResultCode);
    if ResultCode <> 0 then
      SuppressibleMsgBox(ServiceFailedText, mbError, MB_OK, IDOK);
#ifndef NoExchange
    ApplyExchangeService;
#endif
  end;

  { The service is up but shares nothing until roots are chosen - it must never guess
    which folders to publish (spec §0.1). So the last step is to open the console. }
  if (CurStep = ssDone) and (not WizardSilent) and WizardIsTaskSelected('openmanager') then
    LaunchShareManager;
end;

{ The Share Manager's logon autostart (HKCU Run value "FastMediaSorterShare", written by
  AutostartManager at run time) is not removed by any [Registry] entry, so uninstall left
  a dead Startup-apps entry Windows tried at every logon (ticket 055 I-03). Removed only
  when it points into THIS installation: the User edition's Share Manager, installed
  elsewhere, writes the same value name. Mirrors RemoveShareManagerAutostart in
  FastMediaSorter.iss. }
procedure RemoveShareManagerAutostart;
var
  RunValue: String;
begin
  if not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'FastMediaSorterShare', RunValue) then
    exit;
  if Pos(Uppercase(ExpandConstant('{app}\FastMediaSorterCompanion.exe')), Uppercase(RunValue)) = 0 then
    exit;
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'FastMediaSorterShare');
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'FastMediaSorterShare');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep <> usUninstall then
    exit;

  { Stop and DELETE the service, drop the folder ACEs we recorded and the firewall
    rule - before any file is removed, so nothing is left holding a listening port or
    an orphaned SCM registration. The state directory is deliberately kept: it holds
    the identity paired phones pinned, and deleting it is a separate explicit choice
    documented on the Server page. }
  RunHelperScript(ExpandConstant('{app}\install-share-service.ps1'), 'remove', ResultCode);
#ifndef NoExchange
  RemoveExchangeServiceAndRule;
#endif
  StopEverything(ExpandConstant('{app}\stop-companion.ps1'));
  RemoveShareManagerAutostart;
end;
