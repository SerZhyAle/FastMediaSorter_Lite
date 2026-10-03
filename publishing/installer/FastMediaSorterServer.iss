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
; a much smaller payload - no viewer, no VLC codecs, no OCR models. The one thing the
; two installers MUST agree on lives in neither of them: the frozen service name and
; the on-disk state format, both owned by the worker.

#ifndef Version
  #define Version "0.0.0.0"
#endif

#ifndef SourceDir
  #error SourceDir must be defined (path to the staged build output)
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

[CustomMessages]
english.CompFileDO=FileDO virtual disk support (optional; sharing stays off until enabled)
russian.CompFileDO=Поддержка виртуальных дисков FileDO (необязательно; общий доступ включается отдельно)
ukrainian.CompFileDO=Підтримка віртуальних дисків FileDO (необов’язково; спільний доступ вмикається окремо)
english.OpenManagerTask=Open the Share Manager after installation to choose the folders to publish
russian.OpenManagerTask=Открыть Менеджер общего доступа после установки, чтобы выбрать публикуемые папки
ukrainian.OpenManagerTask=Відкрити Менеджер спільного доступу після встановлення, щоб вибрати публіковані теки

[Types]
Name: "custom"; Description: "Choose optional disk support"; Flags: iscustom

[Components]
; Types: custom - a custom-only list starts with the component ticked (a component with no
; Types starts unticked, and the service then reports "bundled FileDO is missing"). The user can
; still untick it; sharing disks stays off until enabled either way.
Name: "filedo"; Description: "{cm:CompFileDO}"; Types: custom

[Files]
; The Share Manager (self-contained .NET 10) + the headless Go worker. Deliberately
; NO viewer, no VLC codecs and no OCR models: this package's job is an always-on file
; share on a machine that may well have no interactive user at all.
Source: "{#SourceDir}\{#CompanionExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\companion\*"; DestDir: "{app}\companion"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#SourceDir}\filedo\*"; DestDir: "{app}\filedo"; Flags: recursesubdirs createallsubdirs ignoreversion; Components: filedo
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
    Result := 'Не удалось зарегистрировать или запустить службу общего доступа. Файлы установлены; откройте Менеджер общего доступа и нажмите «Управление хостингом..» -> «Восстановить регистрацию службы». Подробности - в файле install-share-service.log в каталоге ProgramData\FastMediaSorterCompanion.'
  else if IsLanguage('ukrainian') then
    Result := 'Не вдалося зареєструвати або запустити службу спільного доступу. Файли встановлено; відкрийте Менеджер спільного доступу й натисніть «Керування хостингом..» -> «Відновити реєстрацію служби». Подробиці - у файлі install-share-service.log у каталозі ProgramData\FastMediaSorterCompanion.'
  else
    Result := 'The folder-share service could not be registered or started. The files are installed; open the Share Manager and use "Manage hosting.." -> "Repair the service registration". Details are in install-share-service.log under ProgramData\FastMediaSorterCompanion.';
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
  StopEverything(ExpandConstant('{app}\stop-companion.ps1'));
  RemoveShareManagerAutostart;
end;
