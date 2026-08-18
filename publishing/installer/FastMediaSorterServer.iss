; Fast Media Sorter - Folder Share SERVER edition - Inno Setup script
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

#define AppName       "Fast Media Sorter Folder Share Server"
; ARP DisplayName. NOT the User edition's frozen "FastMediaSorter LITE": the two must
; correlate to two DIFFERENT winget packages, and a shared DisplayName would make
; "winget upgrade" hand a machine the wrong edition.
#define AppNameArp    "Fast Media Sorter Folder Share Server"
; Start-menu GROUP - deliberately NOT {#AppName}: both editions live under ONE product
; folder in the Start menu, so this must stay byte-identical to the User edition's
; DefaultGroupName in FastMediaSorter.iss (its {#AppName}). The two installers write
; into the same group without touching each other's shortcuts - Inno removes only the
; entries it created, and the emptied folder goes with whichever uninstaller runs last.
; This is a shortcut-placement name only, NOT an identity anchor: AppId, the ARP
; DisplayName and the winget package stay distinct per edition.
#define AppGroupName  "Fast Media Sorter for Windows"
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
; Setup replaces the Companion exe, so a running instance must be gone first. The
; SERVICE is stopped separately and first, by stop-companion.ps1 - killing its process
; would only make the configured recovery action restart it straight back onto the
; file Setup is about to replace.
AppMutex=FastMediaSorterCompanionSingleInstanceMutex
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
english.OpenManagerTask=Open the Share Manager after installation to choose the folders to publish
russian.OpenManagerTask=Открыть Менеджер общего доступа после установки, чтобы выбрать публикуемые папки
ukrainian.OpenManagerTask=Відкрити Менеджер спільного доступу після встановлення, щоб вибрати публіковані теки

[Files]
; The Share Manager (self-contained .NET 10) + the headless Go worker. Deliberately
; NO viewer, no VLC codecs and no OCR models: this package's job is an always-on file
; share on a machine that may well have no interactive user at all.
Source: "{#SourceDir}\{#CompanionExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\companion\*"; DestDir: "{app}\companion"; Flags: recursesubdirs createallsubdirs ignoreversion
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

[Icons]
; "--show": a bare launch obeys the manager's own "open the window at startup" option,
; which is OFF by default, so without the flag this Start-menu item would start a
; tray-only process and read as a click that did nothing. Clicking a shortcut IS the
; explicit "open it" gesture, the same one LITE's Share Manager button makes.
Name: "{group}\{#AppName}"; Filename: "{app}\{#CompanionExe}"; Parameters: "--show"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"

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
    Result := 'На этом компьютере уже установлена пользовательская редакция Fast Media Sorter.' + #13#10#13#10 +
              'Серверная редакция не работает рядом с ней: обе редакции используют один и тот же канал управления, порт и ключ узла. Установка ПЕРЕНЕСЁТ текущее состояние (ключ узла, пароль, список папок, порт) в машинное хранилище и передаст раздачу службе Windows.' + #13#10#13#10 +
              'Отпечаток ключа узла сохранится - привязанные телефоны продолжат подключаться без повторной привязки. Просмотрщик останется установленным и продолжит работать.' + #13#10#13#10 +
              'Продолжить?'
  else if IsLanguage('ukrainian') then
    Result := 'На цьому комп''ютері вже встановлено користувацьку редакцію Fast Media Sorter.' + #13#10#13#10 +
              'Серверна редакція не працює поруч із нею: обидві редакції використовують один канал керування, порт і ключ вузла. Встановлення ПЕРЕНЕСЕ поточний стан (ключ вузла, пароль, список тек, порт) у машинне сховище й передасть роздачу службі Windows.' + #13#10#13#10 +
              'Відбиток ключа вузла збережеться - прив''язані телефони продовжать підключатися без повторної прив''язки. Переглядач залишиться встановленим і працюватиме далі.' + #13#10#13#10 +
              'Продовжити?'
  else
    Result := 'A User edition of Fast Media Sorter is already installed on this computer.' + #13#10#13#10 +
              'The Server edition cannot run alongside it: both editions use the same control channel, the same port and the same host key. Setup will MIGRATE the current state (host key, password, folder list, port) into the machine store and hand the sharing over to a Windows service.' + #13#10#13#10 +
              'The host-key fingerprint is preserved, so paired phones keep connecting without re-pairing. The viewer stays installed and keeps working.' + #13#10#13#10 +
              'Continue?';
end;

function SilentConflictText: String;
begin
  if IsLanguage('russian') then
    Result := 'Обнаружена пользовательская редакция Fast Media Sorter. Тихая установка серверной редакции поверх неё остановлена: она выполнила бы перенос состояния без вашего подтверждения. Запустите установщик в обычном режиме или передайте /MIGRATEFROMUSER, чтобы явно разрешить перенос.'
  else if IsLanguage('ukrainian') then
    Result := 'Виявлено користувацьку редакцію Fast Media Sorter. Тиху установку серверної редакції поверх неї зупинено: вона виконала б перенесення стану без вашого підтвердження. Запустіть установник у звичайному режимі або передайте /MIGRATEFROMUSER, щоб явно дозволити перенесення.'
  else
    Result := 'A User edition of Fast Media Sorter was detected. This silent Server installation was stopped because it would migrate your state without confirmation. Run Setup interactively, or pass /MIGRATEFROMUSER to allow the migration explicitly.';
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
            ' -DataDir "' + MachineDataDir + '"' +
            ' -UserDataDir "' + UserDataDirHint + '"';
  Exec('powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

{ Stops the service, the Companion and the worker before Setup/Uninstall replaces or
  removes their files. Best-effort: a hiccup here must not abort Setup. }
procedure StopEverything(const ScriptPath: String);
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
end;
