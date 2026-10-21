# Sharing settings windows

The modern Share Manager separates local SFTP sharing, local exchange-server administration and manager preferences. Product names follow [PRODUCT_NAMING](PRODUCT_NAMING.md); component and channel availability follows [POSITIONING](POSITIONING.md) and the actual installed payload. These instructions describe builds with separate settings windows. Earlier builds keep these pages together under Manager settings.

## Choose the task

| Task | Open | Scope |
| --- | --- | --- |
| Change startup, theme or language | Manager settings, General | The signed-in manager and its interface |
| Sign in to a remote server, publish resources, manage this PC's account or clipboard | Manager settings, Exchange account | The selected remote account, without installing a local exchange server |
| Manage scheduled file operations | Manager settings, Scheduled file operations | The signed-in manager, independent of either local server |
| Change SFTP port, connection limit or LAN-only policy | SFTP sharing settings, Network | Local SFTP folder sharing |
| Inspect or maintain SFTP interactive/service hosting | SFTP sharing settings, Hosting | The local SFTP worker and its existing service helper |
| Change local exchange listener, public endpoint, certificate, accounts, devices or invitations | Exchange server settings | The exchange server installed on this PC |
| Inspect exchange service, keep-awake, log retention, remote administration or audit | Exchange server settings, relevant group | The local exchange server, with existing permissions and confirmations |

## Open and switch

The tray offers manager and SFTP settings; its existing exchange-server entry opens local server settings. The manager toolbar opens general settings. Server setup handover opens local exchange settings. Account publishing requests open the client page; a folder schedule request still opens its editor in the manager.

Each settings window has links at the lower left to the other available settings windows. A component link appears only while its payload is installed. Stopped or disabled does not mean uninstalled. Repeated requests activate the existing window instead of creating another one. A targeted request selects its page even if the window is already open.

Windows can stay open beside each other. Closing an editor does not stop a server, disconnect a remote account, stop scheduled work or exit the tray host. Each window has its own size, last page, group folds and scroll context. Old combined-window context seeds each destination on its first use; its original record and server configuration are retained.

## SFTP changes

Network owns the listen port, maximum connections and LAN-only policy. The port field is a draft until Apply. Switching to another settings window retains that draft. Closing with a pending port asks whether to apply, discard or continue editing; a failed apply keeps the editor and draft available. A port change affects previously issued connection codes and router forwarding, so follow the existing warning and replace those codes/rules.

Connection-limit changes retain their existing save/commit behavior. LAN-only retains its live policy push and existing disruptive-change confirmation. Opening settings does not start a worker or create a listener. If sharing is disabled, use the explicit enable button; administrator approval and firewall changes remain in the established enable flow.

Hosting shows the interactive or service mode and its current status. Existing maintenance commands remain explicit. Running as a Windows service and permissions still depend on the installed distribution; a settings-window split grants no new Store or installation capability.

## Exchange changes

The exchange window controls the local installation. Its listener/public endpoint, third-party port range, certificate controls, accounts, invitations, devices, host settings, remote-administration readiness and audit retain their existing validation and explicit apply/actions. A local service and a console process remain different hosting states. A stopped service is still installed and can be inspected.

Client sign-in and publishing remain in Manager settings, Exchange account. That page can use a server on another machine. Its existing authorized remote-administration dialogs retain their target; opening a local-server window does not change that target to localhost. Never substitute the SFTP port for the exchange port during diagnosis.

## Shared preferences, help and troubleshooting

Theme and language have one editor in Manager settings. Their existing propagation applies across open windows. Preference writes reload current persisted values so independently open editors do not save stale copies of unrelated preferences.

Use F1 or the help button for the active page. The shipped offline corpus is also the website help corpus: [manager](../help/en/share-settings.html), [SFTP](../help/en/sftp-settings.html), [local exchange server](../help/en/exchange-server-settings.html). Localized variants live in the same help tree. Old combined-window network, hosting and exchange anchors link to the corresponding new topic.

If a component link disappears, check installation before enabling or restarting anything. If a component is installed but stopped, disabled, inaccessible or outdated, use the status and existing repair instructions for that component. A remote client does not need the local exchange payload. For the connection model see [Network architecture](NETWORK_ARCHITECTURE_AND_SCHEMES.md).
