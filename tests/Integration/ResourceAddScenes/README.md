# Resource add scenes

Run from the repository root:

```powershell
dotnet run --project tests/Integration/ResourceAddScenes/ResourceAddScenes.csproj -- temp/resource-214-scenes
```

The harness shows the wizard before picking, as production does, and renders four steps,
expanded advanced options and the committed finish for both plain and exchange entries in all
13 languages and both themes (312 PNGs). It checks 44 logical pixel targets, glyphs, text layout,
heading contrast, RTL, working-area bounds, advanced options collapsed by default, no writes on
render/question Next, one commit and no automatic QR dialog. The final verdict is PASS/FAIL
with exit 0/1; the output reports the actual desktop DPI.

A process-local developer gate, a scratch VB settings hive and scripted actions isolate the run.
It does not launch a worker, browse real folders, mount disks, publish resources or elevate.
Physical folder picking/UNC access, FileDO mounts, a paired phone, live publication and actual
175% DPI remain specification 214 T-6 acceptance.
