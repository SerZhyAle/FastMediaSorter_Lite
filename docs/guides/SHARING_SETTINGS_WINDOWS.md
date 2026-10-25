# Sharing settings windows

The modern Share Manager has two settings windows: Manager settings, which also holds the local SFTP sharing pages while that component is installed, and the settings of the local exchange server. Product names follow [PRODUCT_NAMING](PRODUCT_NAMING.md); component and channel availability follows [POSITIONING](POSITIONING.md) and the actual installed payload. These instructions describe builds with the two windows. Earlier builds show SFTP sharing in a window of its own, and the earliest keep every page together under Manager settings.

## Choose the task

| Task | Open | Scope |
| --- | --- | --- |
| Change startup, theme or language | Manager settings, General | The signed-in manager and its interface |
| Change SFTP port, connection limit or LAN-only policy | Manager settings, SFTP sharing, Network | Local SFTP folder sharing |
| Inspect or maintain SFTP interactive/service hosting | Manager settings, SFTP sharing, Hosting | The local SFTP worker and its existing service helper |
| Manage scheduled file operations | Manager settings, Scheduled file operations | The signed-in manager, independent of either local server |
| Sign in to a remote server, publish resources, manage this PC's account or clipboard | Manager settings, Exchange account | The selected remote account, without installing a local exchange server |
| Change local exchange listener, public endpoint, certificate, accounts, devices or invitations | Exchange server settings | The exchange server installed on this PC |
| Inspect exchange service, keep-awake, log retention, remote administration or audit | Exchange server settings, relevant group | The local exchange server, with existing permissions and confirmations |

## Open and switch

The tray and the manager toolbar open Manager settings; its existing exchange-server entry opens local server settings. Server setup handover opens local exchange settings. Account publishing requests open the client page; a folder schedule request opens its editor in the manager.

In Manager settings the SFTP pages sit in a group headed "SFTP sharing", between General and Scheduled file operations. The group appears only while the SFTP sharing payload is installed, and each page title and search result repeats the scope ("SFTP sharing: Network"), so the pages cannot be taken for the client account or for the exchange server. A request for a Network or Hosting page while the payload is missing is refused with the usual "component unavailable" message.

Each settings window has a link at the lower left to the other one. The exchange link appears only while its payload is installed. Stopped or disabled does not mean uninstalled. Repeated requests activate the existing window instead of creating another one. A targeted request selects its page even if the window is already open.

The two windows can stay open beside each other. Closing an editor does not stop a server, disconnect a remote account, stop scheduled work or exit the tray host. Each window has its own size, last page, group folds and scroll context. The SFTP pages take their place in the Manager window: the page last used in the earlier SFTP window is offered the first time, and the earlier records are retained, never rewritten or deleted.

## SFTP changes

Network owns the listen port, maximum connections and LAN-only policy. The port field is a draft until Apply. Switching to another settings window or another page retains that draft. Closing with a pending port asks whether to apply, discard or continue editing; a failed apply keeps the editor and draft available. A port change affects previously issued connection codes and router forwarding, so follow the existing warning and replace those codes/rules.

Connection-limit changes retain their existing save/commit behavior. LAN-only retains its live policy push and existing disruptive-change confirmation. Opening settings does not start a worker or create a listener. If sharing is disabled, use the explicit enable button; administrator approval and firewall changes remain in the established enable flow.

Hosting shows the interactive or service mode and its current status. Existing maintenance commands remain explicit. Running as a Windows service and permissions still depend on the installed distribution; merging the windows grants no new Store or installation capability.

## Exchange changes

The exchange window controls the local installation. Its listener/public endpoint, third-party port range, certificate controls, accounts, invitations, devices, host settings, remote-administration readiness and audit retain their existing validation and explicit apply/actions. A local service and a console process remain different hosting states. A stopped service is still installed and can be inspected.

Client sign-in and publishing remain in Manager settings, Exchange account. That page can use a server on another machine. Its existing authorized remote-administration dialogs retain their target; opening a local-server window does not change that target to localhost. Never substitute the SFTP port for the exchange port during diagnosis.

## Shared preferences, help and troubleshooting

Theme and language have one editor in Manager settings. Their existing propagation applies across open windows. Preference writes reload current persisted values so independently open editors do not save stale copies of unrelated preferences.

Use F1 or the help button for the active page. The shipped offline corpus is also the website help corpus: [manager and SFTP sharing](../help/en/share-settings.html), [SFTP sharing in detail](../help/en/sftp-settings.html), [local exchange server](../help/en/exchange-server-settings.html). F1 on a Network or Hosting page opens the SFTP topic at that page. Localized variants live in the same help tree. Old combined-window network, hosting and exchange anchors link to the corresponding topic.

If a component link disappears, check installation before enabling or restarting anything. If a component is installed but stopped, disabled, inaccessible or outdated, use the status and existing repair instructions for that component. A remote client does not need the local exchange payload. For the connection model see [Network architecture](NETWORK_ARCHITECTURE_AND_SCHEMES.md).
