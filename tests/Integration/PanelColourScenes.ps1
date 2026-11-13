<# L0303 M-5: on-screen reference scenes of the panel colours, hand-run (never part of a default run).
   Drives the actual Main_Form of the modern viewer (user settings neither read nor saved; the application
   theme is switched in memory only), one scene per row of the matrix written by PanelColourSceneMatrix.ps1:
   the picture full-window, the colour scheme, the panels' set opacity, the application theme and the
   interface scale. Each scene is captured from the screen (so translucent panels and their ink windows are
   composed as the eye gets them) and every visible text caption box is probed: the ink and backdrop
   luminance clusters and their contrast; per scene the lowest and the P05 contrast are written to a CSV.

   pwsh -NoProfile -File tests/Integration/PanelColourScenes.ps1 -Matrix <scenes.csv> -OutDir <new dir> [-Filter <SceneId wildcard>]
   pwsh -NoProfile -File tests/Integration/PanelColourScenes.ps1 -SelfTest   (the probe only, about 2 s)

   Exit 0: scenes written or self-test passed; 1: a scene or the self-test failed; 2: could not verify. #>
[CmdletBinding(DefaultParameterSetName = 'Run')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Run')] [string] $Matrix,
    [Parameter(Mandatory, ParameterSetName = 'Run')] [string] $OutDir,
    [Parameter(ParameterSetName = 'Run')] [string] $Filter = '*',
    [Parameter(Mandatory, ParameterSetName = 'SelfTest')] [switch] $SelfTest
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$probeSource = Join-Path $PSScriptRoot 'PanelColourProbe.cs'

function Invoke-ProbeSelfTest {
    Add-Type -Path $probeSource -ReferencedAssemblies System.Drawing.Common, System.Drawing.Primitives, System.Collections
    Add-Type -AssemblyName System.Drawing
    $fill = [System.Drawing.Color]::FromArgb(32, 32, 32)
    $ink = [System.Drawing.Color]::FromArgb(240, 240, 240)
    $expected = [PanelColourProbe]::Contrast([PanelColourProbe]::Luminance($ink), [PanelColourProbe]::Luminance($fill))
    $bitmap = [System.Drawing.Bitmap]::new(40, 20)
    try {
        $g = [System.Drawing.Graphics]::FromImage($bitmap)
        $g.Clear($fill)
        $brush = [System.Drawing.SolidBrush]::new($ink)
        foreach ($x in 4, 12, 20, 28) { $g.FillRectangle($brush, $x, 4, 2, 12) }
        $brush.Dispose(); $g.Dispose()
        $box = [System.Drawing.Rectangle]::new(0, 0, 40, 20)
        $measured = [PanelColourProbe]::MeasureBox($bitmap, $box)
        if ([math]::Abs($measured - $expected) -gt 0.001) { throw "Stroke box: expected $expected, measured $measured." }
        # Light strokes on the ink side swapped: dark text on a light backdrop gives the same ratio.
        $g = [System.Drawing.Graphics]::FromImage($bitmap); $g.Clear($ink)
        $brush = [System.Drawing.SolidBrush]::new($fill)
        foreach ($x in 4, 12, 20, 28) { $g.FillRectangle($brush, $x, 4, 2, 12) }
        $brush.Dispose(); $g.Dispose()
        $swapped = [PanelColourProbe]::MeasureBox($bitmap, $box)
        if ([math]::Abs($swapped - $expected) -gt 0.001) { throw "Swapped box: expected $expected, measured $swapped." }
        $g = [System.Drawing.Graphics]::FromImage($bitmap); $g.Clear($fill); $g.Dispose()
        if (-not [double]::IsNaN([PanelColourProbe]::MeasureBox($bitmap, $box))) { throw 'A uniform box was not reported as nothing drawn.' }
        if (-not [double]::IsNaN([PanelColourProbe]::MeasureBox($bitmap, [System.Drawing.Rectangle]::new(50, 50, 4, 4)))) { throw 'A box outside the capture was measured.' }
    } finally { $bitmap.Dispose() }
    $p05 = [PanelColourProbe]::Percentile([double[]](1..100), 0.05)
    if ($p05 -ne 5) { throw "P05 of 1..100: expected 5, got $p05." }
    $root = Join-Path $repo ('temp/L0303/scenes-selftest-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    $refused = 0
    try { Assert-RunInputs (Join-Path $root 'missing.csv') (Join-Path $root 'out') } catch { $refused++ }
    $csv = Join-Path $root 'scenes.csv'; Set-Content -LiteralPath $csv -Value 'SceneId,ImagePath'
    try { Assert-RunInputs $csv (Join-Path $root 'out') } catch { $refused++ }
    try { Assert-RunInputs $csv $root } catch { $refused++ }
    if ($refused -ne 3) { throw "Input refusals: expected 3, got $refused." }
    Remove-Item -LiteralPath $csv
    Remove-Item -LiteralPath $root
    'PanelColourScenes self-test: PASS (stroke and swapped boxes at {0:0.00}:1, uniform and outside boxes refused, P05, input refusals)' -f $expected
}

function Assert-RunInputs([string] $MatrixPath, [string] $OutPath) {
    if (-not (Test-Path -LiteralPath $MatrixPath -PathType Leaf)) { throw "Scene matrix not found: $MatrixPath" }
    $first = Get-Content -LiteralPath $MatrixPath -TotalCount 1
    foreach ($column in 'SceneId', 'ImagePath', 'Scheme', 'Opacity', 'Theme', 'ScalePercent') {
        if ($first -notmatch "(^|,)`"?$column`"?(,|$)") { throw "Scene matrix lacks the column $column." }
    }
    if (Test-Path -LiteralPath $OutPath) { throw "Output directory exists; choose a new one: $OutPath" }
}

if ($SelfTest) {
    try { Invoke-ProbeSelfTest; exit 0 } catch { "PanelColourScenes self-test: FAIL - $($_.Exception.Message)"; exit 1 }
}

. (Join-Path $repo 'tools/lib/Verdict.ps1')
trap { Exit-Verdict 'PanelColourScenes' 2 $_.Exception.Message }
Write-Subject 'PanelColourScenes' 'the panel colours of the actual Main_Form of the modern viewer on screen, user settings neither read nor saved'
$Matrix = [IO.Path]::GetFullPath($Matrix); $OutDir = [IO.Path]::GetFullPath($OutDir)
Assert-RunInputs $Matrix $OutDir
New-Item -ItemType Directory -Path $OutDir | Out-Null
$work = Join-Path $OutDir 'harness'
New-Item -ItemType Directory -Path $work | Out-Null
$project = [System.Security.SecurityElement]::Escape((Join-Path $repo 'src/Modern/FastMediaSorter.Modern.vbproj'))
$probeItem = [System.Security.SecurityElement]::Escape($probeSource)
@"
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><PlatformTarget>x64</PlatformTarget><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
 <ItemGroup><Compile Include="ColourScenes.cs"/><Compile Include="$probeItem"/><ProjectReference Include="$project" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $work 'ColourScenes.csproj') -Encoding utf8
@'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using fmsl;
class SceneWindow : Main_Form {
 protected override void OnLoad(EventArgs e) { }
 protected override void OnShown(EventArgs e) { }
 protected override void OnFormClosing(FormClosingEventArgs e) { }
}
static class Scenes {
 const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
 static object Get(object instance, string name) {
  for (Type type = instance.GetType(); type != null; type = type.BaseType) {
   var property = type.GetProperty(name, Flags | BindingFlags.DeclaredOnly);
   if (property != null) return property.GetValue(instance);
   var field = type.GetField(name, Flags | BindingFlags.DeclaredOnly) ?? type.GetField("_" + name, Flags | BindingFlags.DeclaredOnly);
   if (field != null) return field.GetValue(instance);
  }
  throw new Exception("Missing member " + name);
 }
 static void Set(object instance, string name, object value) {
  for (Type type = instance.GetType(); type != null; type = type.BaseType) {
   var field = type.GetField(name, Flags | BindingFlags.DeclaredOnly);
   if (field != null) { field.SetValue(instance, value); return; }
  }
  throw new Exception("Missing field " + name);
 }
 static object Call(object instance, string name, params object[] args) {
  var method = typeof(Main_Form).GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length);
  try { return method.Invoke(instance, args); } catch (TargetInvocationException e) { throw e.InnerException; }
 }
 static void Pump(int ms = 250) { var end = Environment.TickCount64 + ms; do { Application.DoEvents(); Thread.Sleep(10); } while (Environment.TickCount64 < end); }
 static string[] SplitCsv(string line) {
  var cells = new List<string>(); var cell = new System.Text.StringBuilder(); bool quoted = false;
  for (int i = 0; i < line.Length; i++) {
   char c = line[i];
   if (quoted) { if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; } else if (c == '"') quoted = false; else cell.Append(c); }
   else if (c == '"') quoted = true; else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); } else cell.Append(c);
  }
  cells.Add(cell.ToString()); return cells.ToArray();
 }
 static void SetTheme(string theme) {
  var service = typeof(Main_Form).Assembly.GetType("fmsl.ThemeService");
  var modeType = typeof(Main_Form).Assembly.GetType("fmsl.ThemeMode");
  service.GetField("_mode", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, Enum.Parse(modeType, theme == "light" ? "Light" : "Dark"));
  service.GetMethod("Refresh", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { true });
 }
 [STAThread] static int Main(string[] args) {
  Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
  var matrix = File.ReadAllLines(args[0]); var outDir = args[1]; var filter = args[2];
  var header = SplitCsv(matrix[0]).ToList();
  Func<string[], string, string> cell = (row, name) => row[header.IndexOf(name)];
  var pattern = new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(filter).Replace("\\*", ".*").Replace("\\?", ".") + "$");
  var results = new List<string> { "SceneId,Boxes,Measured,Lowest,P05,Capture" };
  int failed = 0;
  try {
   typeof(Main_Form).Assembly.GetType("fmsl.ThemeService").GetMethod("Initialize").Invoke(null, null);
   using var form = new SceneWindow { StartPosition = FormStartPosition.Manual, Location = new Point(40, 40), ClientSize = new Size(1000, 680), TopMost = true };
   Call(form, "BuildModernLayout");
   var prefsField = typeof(Main_Form).GetField("modern_Preferences", Flags);
   var prefs = Activator.CreateInstance(prefsField.FieldType);
   prefsField.SetValue(form, prefs);
   prefs.GetType().GetProperty("ChromePinned").SetValue(prefs, true);
   prefs.GetType().GetProperty("ReduceMotion").SetValue(prefs, true);
   Call(form, "InitializeTooltips"); Call(form, "InitializeFolderMenu"); Call(form, "LngCh");
   ((ComboBox)Get(form, "cmbox_Sort")).SelectedIndex = 0;
   ((ComboBox)Get(form, "cmbox_Media_Folder")).Text = outDir;
   ((Label)Get(form, "lbl_Current_File")).Text = "panel-colour-scene.png";
   ((Label)Get(form, "lbl_Status")).Text = "1000 x 680";
   ((Label)Get(form, "lbl_Info")).Text = "Fast Media Sorter";
   var picture = (PictureBox)Get(form, "Picture_Box_1");
   ((Control)Get(form, "lbl_Help_Info")).Hide(); ((Control)Get(form, "Picture_Box_2")).Hide();
   picture.SizeMode = PictureBoxSizeMode.Zoom;
   form.Show(); form.Activate(); Call(form, "SyncMediaSurface"); Call(form, "ApplyModernChrome"); Pump();
   var common = typeof(Main_Form).Assembly.GetType("fmsl.Common_Module");
   var schemeField = common.GetField("Form_Color_Scheme", BindingFlags.Public | BindingFlags.Static);
   foreach (var line in matrix.Skip(1)) {
    if (string.IsNullOrWhiteSpace(line)) continue;
    var row = SplitCsv(line); var id = cell(row, "SceneId");
    if (!pattern.IsMatch(id)) continue;
    try {
     var scheme = cell(row, "Scheme");
     schemeField.SetValue(null, scheme == "black" ? 1 : scheme == "white" ? 2 : 0);
     SetTheme(cell(row, "Theme"));
     prefs.GetType().GetProperty("ChromeOpacityPercent").SetValue(prefs, int.Parse(cell(row, "Opacity"), CultureInfo.InvariantCulture));
     prefs.GetType().GetProperty("InterfaceScalePercent").SetValue(prefs, int.Parse(cell(row, "ScalePercent"), CultureInfo.InvariantCulture));
     using var image = (Bitmap)Image.FromStream(new MemoryStream(File.ReadAllBytes(cell(row, "ImagePath"))));
     var old = picture.Image; picture.Image = (Bitmap)image.Clone(); old?.Dispose();
     picture.Show(); picture.BringToFront();
     Set(form, "is_PictureBox1_Visible", true); Set(form, "is_PictureBox2_Visible", false);
     form.BackColor = Color.Black;            // what the fixed black scheme keeps (Lifecycle at load)
     Set(form, "last_Back_Color", Color.Empty); // force today's colour step for every scene
     Call(form, "ChromeConfigure");
     Call(form, "UpdateControlVisibility");
     Call(form, "Chrome_Timer_Tick", null, EventArgs.Empty); Pump();
     var hosts = form.OwnedForms.Where(f => f.Visible && f.Name.StartsWith("chrome_")).ToArray();
     var screen = Rectangle.Union(form.Bounds, hosts.Aggregate(Rectangle.Empty, (a, h) => a.IsEmpty ? h.Bounds : Rectangle.Union(a, h.Bounds)));
     using var capture = new Bitmap(screen.Width, screen.Height);
     using (var g = Graphics.FromImage(capture)) g.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
     var contrasts = new List<double>(); int boxes = 0;
     foreach (var host in hosts) {
      var surface = (Control)Get(host, "Surface");
      foreach (var item in ((System.Collections.IEnumerable)Get(surface, "Items")).Cast<object>()) {
       if (!(bool)Get(item, "Visible") || (bool)Get(item, "CaptionIsSymbol") || string.IsNullOrWhiteSpace((string)Get(item, "Caption"))) continue;
       var bounds = surface.RectangleToScreen((Rectangle)Get(item, "Bounds"));
       if (bounds.IsEmpty) continue;
       bounds.Offset(-screen.Left, -screen.Top); boxes++;
       contrasts.Add(PanelColourProbe.MeasureBox(capture, bounds));
      }
     }
     var file = id + ".png"; capture.Save(Path.Combine(outDir, file));
     var measured = contrasts.Where(c => !double.IsNaN(c)).ToList();
     string F(double v) => double.IsNaN(v) ? "" : v.ToString("0.00", CultureInfo.InvariantCulture);
     results.Add(string.Join(",", id, boxes, measured.Count, F(measured.Count == 0 ? double.NaN : measured.Min()), F(PanelColourProbe.Percentile(measured, 0.05)), file));
    } catch (Exception e) { failed++; results.Add(id + ",,,,,error: " + e.GetType().Name + " " + e.Message.Replace(',', ';').Replace('\n', ' ')); }
   }
   Call(form, "DisposeChromeResources"); form.Hide();
  } catch (Exception e) { Console.WriteLine(e); failed++; }
  File.WriteAllLines(Path.Combine(outDir, "scene-contrast.csv"), results);
  Console.WriteLine((results.Count - 1) + " scenes, " + failed + " failed; table: " + Path.Combine(outDir, "scene-contrast.csv"));
  return failed == 0 && results.Count > 1 ? 0 : 1;
 }
}
'@ | Set-Content -LiteralPath (Join-Path $work 'ColourScenes.cs') -Encoding utf8
& dotnet build (Join-Path $work 'ColourScenes.csproj') -v:q
if ($LASTEXITCODE -ne 0) { Exit-Verdict 'PanelColourScenes' 2 'scene harness could not build' }
& (Join-Path $work 'bin/Debug/net10.0-windows/ColourScenes.exe') $Matrix $OutDir $Filter
Exit-Verdict 'PanelColourScenes' $LASTEXITCODE "evidence: $OutDir"
