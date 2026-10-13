# Exchange server first-run scenes

Run from the repository root:

```powershell
dotnet run --project tests/Integration/ExchangeFirstRunScenes/ExchangeFirstRunScenes.csproj -- temp/welcome-213-scenes
```

The harness renders six desktop steps and seven Server-edition steps in all shipped languages
and both themes (338 PNGs). It checks the working-area bounds, targets of at least 44 logical
pixels, command glyphs, label layout, heading contrast, RTL, the full operator statement,
unchecked elevated opt-ins, no writes during rendering and dismissal on close. The output
reports the actual desktop DPI. The final verdict is PASS/FAIL with exit 0/1.

The developer gate is enabled for this process only. The harness uses a temporary VB settings
hive and scripted actions; no real account, server, firewall rule or service is created.
Physical fresh-install/upgrade, declined UAC, service migration, phone enrollment and actual
175% DPI remain manual acceptance under specification 213 T-6 and the standing 185 hand row.
