# Manager welcome scenes

Run from the repository root:

```powershell
dotnet run --project tests/Integration/ManagerWelcomeScenes/ManagerWelcomeScenes.csproj -- temp/welcome-212-scenes
```

The harness renders all six steps in all shipped languages and both themes, writes PNG evidence,
checks pointer targets, text layout, heading contrast, RTL, dropdown Escape and wizard Escape, and
renders the main window's green and no-access status. It uses a temporary VB settings hive and
scripted status snapshots. It never shows the main window, invokes consent or folder actions,
starts a worker, changes firewall rules, or pairs a real phone. The final line is PASS/FAIL;
exit 0/1 respectively. The output states the actual desktop DPI.

Physical first-install/upgrade, declined elevation, Store behavior, a real folder-to-phone run and
actual 175% DPI remain manual acceptance under specification 212 T-6.
