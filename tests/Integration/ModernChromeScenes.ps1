<# Exercises the actual Main_Form without reading/saving user settings.
   The subclass suppresses Load, Shown and FormClosing. Evidence stays in WorkDir. #>
param([string]$WorkDir = (Join-Path $env:TEMP 'fms-modern-chrome-scenes'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repo 'tools/lib/Verdict.ps1')
trap { Exit-Verdict 'ModernChromeScenes' 2 $_.Exception.Message }
Write-Subject 'ModernChromeScenes' 'the floating chrome of the actual Main_Form of the modern viewer, user settings neither read nor saved'
New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
$project = [System.Security.SecurityElement]::Escape((Join-Path $repo 'src/Modern/FastMediaSorter.Modern.vbproj'))
@"
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><PlatformTarget>x64</PlatformTarget><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
 <ItemGroup><Compile Include="ChromeScenes.cs"/><ProjectReference Include="$project" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $WorkDir 'ChromeScenes.csproj') -Encoding utf8
@'
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using fmsl;
class SceneWindow : Main_Form {
 protected override void OnLoad(EventArgs e) { }
 protected override void OnShown(EventArgs e) { }
 protected override void OnFormClosing(FormClosingEventArgs e) { }
}
static class Scenes {
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
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
 static object Call(object instance, string name, params object[] args) {
  var method = typeof(Main_Form).GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length);
  try { return method.Invoke(instance,args); } catch(TargetInvocationException e) { throw e.InnerException; }
 }
 [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam);
 static void Click(Control surface, Rectangle bounds) {
  var point=new Point(bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2);
  Cursor.Position=surface.PointToScreen(point);
  var packed=(IntPtr)((point.Y<<16)|(point.X&0xffff));
  SendMessage(surface.Handle,0x201,(IntPtr)1,packed); SendMessage(surface.Handle,0x202,IntPtr.Zero,packed); Pump();
 }
 static void Assert(bool condition,string message) { if (!condition) throw new Exception(message); }
 static void Pump(int ms=160) { var end=Environment.TickCount64+ms; do { Application.DoEvents(); Thread.Sleep(10); } while(Environment.TickCount64<end); }
 static Form[] Hosts(Main_Form form) => form.OwnedForms.Where(f=>f.Name.StartsWith("chrome_Row") || f.Name=="chrome_Status").ToArray();
 [STAThread] static int Main(string[] args) {
  Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
  try {
   using var form=new SceneWindow { StartPosition=FormStartPosition.Manual, Location=new Point(40,40), ClientSize=new Size(1000,680) };
   Call(form,"BuildModernLayout");
   var prefsField=typeof(Main_Form).GetField("modern_Preferences",Flags);
   var prefs=Activator.CreateInstance(prefsField.FieldType);
   prefsField.SetValue(form,prefs);
   prefs.GetType().GetProperty("ChromePinned").SetValue(prefs,true);
   prefs.GetType().GetProperty("ReduceMotion").SetValue(prefs,true);
   Call(form,"InitializeTooltips"); Call(form,"InitializeFolderMenu"); Call(form,"LngCh");
   ((ComboBox)Get(form,"cmbox_Sort")).SelectedIndex=0;
   ((ComboBox)Get(form,"cmbox_Media_Folder")).Text=args[0];
   ((Label)Get(form,"lbl_Current_File")).Text="chrome-scene.png";
   ((Label)Get(form,"lbl_Status")).Text="1000 × 680";
   ((Label)Get(form,"lbl_Info")).Text="Fast Media Sorter";
   var media=(Control)Get(form,"panel_Media");
   var picture=(PictureBox)Get(form,"Picture_Box_1");
   ((Control)Get(form,"lbl_Help_Info")).Hide(); ((Control)Get(form,"Picture_Box_2")).Hide();
   using var image=new Bitmap(640,480);
   using(var g=Graphics.FromImage(image)) { g.Clear(Color.SteelBlue); g.FillEllipse(Brushes.Gold,180,80,280,280); }
   picture.Image=image; picture.Show(); picture.BringToFront();
   form.Show(); form.Activate(); Call(form,"SyncMediaSurface"); Call(form,"ApplyModernChrome"); Pump();
   var common=typeof(Main_Form).Assembly.GetType("fmsl.Common_Module");
   common.GetField("Is_Show_Recipients_Overlay",BindingFlags.Public|BindingFlags.Static).SetValue(null,true);
   var destinations=(string[])common.GetField("Hardkeys_to_move_mediafile",BindingFlags.Public|BindingFlags.Static).GetValue(null);
   destinations[1]=args[0];
   Call(form,"ApplyRecipientsOverlay");
   var recipients=(Form)Get(form,"recipients_Overlay");
   Assert(recipients.GetType().BaseType.Name=="ChromeHost","Recipients did not use the shared host");
   var verdicts=(System.Collections.IDictionary)Get(form,"slot_Verdicts");
   verdicts[args[0]]=new SlotVerdict { State=SlotState.Missing, CheckedUtc=DateTime.UtcNow };
   Call(form,"ApplyRecipientsOverlayHealth");
   var move=recipients.Controls.Cast<Control>().First(c=>c.Name=="recipient_move_row");
   var unavailableInk=move.ForeColor;
   Call(form,"ChromeApplyScheme",Color.Navy,Color.White);
   var theme=Get(form,"chrome_Theme");
   Assert(move.ForeColor==(Color)Get(theme,"DisabledForeground"),"Theme changes erased recipient health styling");
   verdicts.Clear(); Call(form,"ChromeApplyScheme",Color.Black,Color.White);
   var hosts=Hosts(form);
   Assert(hosts.Length==3 && hosts.All(h=>h.Visible),"Expected two toolbar rows and the status strip");
   Assert(!((Control)Get(form,"flow_Toolbar")).Visible && !((Control)Get(form,"panel_Status")).Visible,"Legacy strips were shown");
   Assert(media.Bounds==form.ClientRectangle,"Docked chrome reduced the media rectangle");
   for(int a=0;a<hosts.Length;a++) for(int b=a+1;b<hosts.Length;b++) Assert(!hosts[a].Bounds.IntersectsWith(hosts[b].Bounds),"Chrome rows overlap");
   var bounds=media.Bounds;
   using var before=new Bitmap(media.Width,media.Height); media.DrawToBitmap(before,media.ClientRectangle);
   foreach(var host in hosts) host.Hide();
   using var after=new Bitmap(media.Width,media.Height); media.DrawToBitmap(after,media.ClientRectangle);
   Assert(bounds==media.Bounds,"Hiding chrome changed media geometry");
   for(int y=0;y<before.Height;y++) for(int x=0;x<before.Width;x++) Assert(before.GetPixel(x,y)==after.GetPixel(x,y),"Chrome altered the rendered media frame");
   Call(form,"Chrome_Timer_Tick",null,EventArgs.Empty); Pump();
   Assert((bool)Call(form,"ChromeHandleKey",Keys.F10),"F10 did not enter chrome mode");
   Assert((bool)Get(form,"chrome_Keyboard_Mode"),"Focus mode did not stay active");
   Call(form,"ChromeHandleKey",Keys.Enter);
   Assert(((CheckBox)Get(form,"chkbox_Top_Most")).Checked,"Enter did not activate the focused pin toggle");
   Call(form,"ChromeHandleKey",Keys.Tab); Call(form,"ChromeHandleKey",Keys.Escape);
   Assert(!(bool)Get(form,"chrome_Keyboard_Mode"),"Escape did not return viewer keys");
   Assert(!(bool)Call(form,"ChromeHandleKey",Keys.D3),"Chrome consumed a sorting key after Escape");
   var top=Hosts(form).Single(h=>h.Name=="chrome_Row1");
   Assert(SendMessage(top.Handle,0x21,IntPtr.Zero,IntPtr.Zero).ToInt32()==3,"Chrome host activated on mouse input");
   var surface=(Control)Get(top,"Surface");
   var first=((System.Collections.IEnumerable)Get(surface,"Items")).Cast<object>().First();
   var target=(Rectangle)Get(first,"Bounds");
   var click=new IntPtr((target.Top+target.Height/2)*65536+target.Left+target.Width/2);
   SendMessage(surface.Handle,0x201,new IntPtr(1),click); SendMessage(surface.Handle,0x202,IntPtr.Zero,click); Pump();
   Assert(!((CheckBox)Get(form,"chkbox_Top_Most")).Checked,"Pointer click did not invoke the pin carrier");
   Assert(form.ContainsFocus,"A chrome click stole viewer focus");
   foreach(var host in Hosts(form)) foreach(var item in ((System.Collections.IEnumerable)Get(Get(host,"Surface"),"Items")).Cast<object>())
    if((bool)Get(item,"Visible")) Assert(!string.IsNullOrWhiteSpace((string)Get(item,"Tooltip")),"Missing tooltip: "+Get(item,"Id"));
   first.GetType().GetProperty("Enabled").SetValue(first,false);
   SendMessage(surface.Handle,0x200,IntPtr.Zero,click); Pump();
   Assert((string)Get(surface,"shownTip")== (string)Get(first,"Tooltip"),"Disabled control lost its tooltip");
   first.GetType().GetProperty("Enabled").SetValue(first,true);
   var sortItem=((System.Collections.IEnumerable)Get(surface,"Items")).Cast<object>().Single(item=>(string)Get(item,"Id")=="cmbox_Sort");
   Call(form,"ChromeShowSort",sortItem); Pump();
   var popup=form.OwnedForms.Single(h=>h.GetType().Name=="ChromeMenu");
   Assert(popup.Visible,"Sort popup did not show");
   Assert(((System.Collections.IEnumerable)Get(Get(popup,"Surface"),"Items")).Cast<object>().Count()==9,"Sort popup lost modes");
   Assert(form.ContainsFocus,"A popup stole viewer focus");
   var popupType=popup.GetType();
   var route=popupType.GetMethod("HandlePopupKey",BindingFlags.Public|BindingFlags.Static);
   route.Invoke(null,new object[]{Keys.Down}); route.Invoke(null,new object[]{Keys.Enter}); Pump();
   Assert(((ComboBox)Get(form,"cmbox_Sort")).SelectedIndex==1,"Popup keyboard selection did not reach the sort carrier");
   Click((Control)surface,(Rectangle)Get(sortItem,"Bounds"));
   popup=form.OwnedForms.Single(h=>h.GetType().Name=="ChromeMenu" && h.Visible);
   var popupSurface=(Control)Get(popup,"Surface");
   var options=((System.Collections.IEnumerable)Get(popupSurface,"Items")).Cast<object>().ToArray();
   Click(popupSurface,(Rectangle)Get(options[0],"Bounds"));
   Assert(((ComboBox)Get(form,"cmbox_Sort")).SelectedIndex==0,"Mouse selection did not apply ABC sorting");
   var sortDir=System.IO.Path.Combine(args[0],"sort-files"); System.IO.Directory.CreateDirectory(sortDir);
   image.Save(System.IO.Path.Combine(sortDir,"z.png")); image.Save(System.IO.Path.Combine(sortDir,"a.png"));
   ((System.Collections.Generic.HashSet<string>)Get(form,"all_Supported_Extensions")).Add(".png");
   form.Current_Folder_Path=sortDir;
   Call(form,"ChromeApplySort",1); Pump();
   Assert(((System.Collections.IEnumerable)Get(form,"files_List")).Cast<string>().Select(System.IO.Path.GetFileName).SequenceEqual(new[]{"z.png","a.png"}),"XYZ did not reorder the folder");
   Call(form,"ChromeApplySort",0); Pump();
   Assert(((System.Collections.IEnumerable)Get(form,"files_List")).Cast<string>().Select(System.IO.Path.GetFileName).SequenceEqual(new[]{"a.png","z.png"}),"ABC did not reorder the folder");
   Call(form,"ChromeApplySort",0); Pump();
   Assert(((System.Collections.IEnumerable)Get(form,"files_List")).Cast<string>().First().EndsWith("a.png"),"Reapplying ABC failed");
   form.Current_Folder_Path="";
   var path=Get(form,"chrome_Path"); path.GetType().GetMethod("BeginEditing").Invoke(path,null); Pump();
   Assert(((Control)Get(path,"Editor")).Focused,"Path editor did not get deliberate keyboard focus");
   path.GetType().GetMethod("EndEditing").Invoke(path,new object[]{true}); Pump();
   Assert(form.ContainsFocus && !((Control)path).ContainsFocus,"Leaving path editing did not restore viewer focus: active="+Form.ActiveForm?.Name+" main="+form.Name+" focused="+form.ContainsFocus+" path="+((Control)path).ContainsFocus);
   foreach(int percent in new[]{100,125,150,100,100}) {
    prefs.GetType().GetProperty("InterfaceScalePercent").SetValue(prefs,percent); Call(form,"ChromeConfigure"); Pump();
    var editor=(Control)Get(path,"Editor");
    typeof(Control).GetMethod("RecreateHandle",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,null); Pump();
    Assert(media.Bounds==bounds,"Changing interface scale changed media bounds");
    Assert(Hosts(form).All(h=>h.Width<=media.Width),"Scaled panel escaped the media width");
   }
   prefs.GetType().GetProperty("InterfaceScalePercent").SetValue(prefs,0); Call(form,"ChromeConfigure"); Pump();
   using(var screenshot=new Bitmap(form.Width,form.Height)) {
    form.DrawToBitmap(screenshot,new Rectangle(Point.Empty,form.Size));
    using(var g=Graphics.FromImage(screenshot)) foreach(var host in Hosts(form)) {
     using(var panel=new Bitmap(host.Width,host.Height)) {
      host.DrawToBitmap(panel,new Rectangle(Point.Empty,host.Size));
      g.DrawImageUnscaled(panel,new Point(host.Left-form.Left,host.Top-form.Top));
     }
    }
    screenshot.Save(System.IO.Path.Combine(args[0],"modern-chrome.png"));
   }
   var statusHost=Hosts(form).Single(h=>h.Name=="chrome_Status");
   ((Label)Get(form,"lbl_Status")).Text="Internal error: Unexpected error. The details are in the log.";
   Call(form,"Chrome_Timer_Tick",null,EventArgs.Empty); Pump();
   var statusItems=((System.Collections.IEnumerable)Get(Get(statusHost,"Surface"),"Items")).Cast<object>().ToArray();
   Assert(statusItems.All(item=>statusHost.ClientRectangle.Contains((Rectangle)Get(item,"Bounds"))),"Status content escaped the strip");
   Assert(media.RectangleToScreen(media.ClientRectangle).Contains(statusHost.Bounds),"Status strip escaped the media area");
   form.ClientSize=new Size(520,680); Call(form,"PlaceChromePanels"); Pump();
   Assert(Hosts(form).Any(h=>((System.Collections.IEnumerable)Get(Get(h,"Surface"),"OverflowItems")).Cast<object>().Any()),"Narrow window did not expose overflow");
   form.ClientSize=new Size(1000,680); Call(form,"PlaceChromePanels"); Pump();
   // L0281: the movable panels. The secondary-instance mark turns every Save into a no-op, so no drop reaches the user's registry.
   typeof(Main_Form).Assembly.GetType("fmsl.MultiWindowPolicy").GetMethod("MarkInstanceRole",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Invoke(null,new object[]{false});
   var placeProperty=prefs.GetType().GetProperty("ChromeRow1Place");
   var row1=Hosts(form).Single(h=>h.Name=="chrome_Row1");
   var row1Surface=(Control)Get(row1,"Surface");
   var handle=Get(row1Surface,"PanelHandleItem");
   Assert(handle!=null && (bool)Get(handle,"IsPanelHandle") && !((Rectangle)Get(handle,"Bounds")).IsEmpty,"Row 1 has no laid-out move handle");
   Assert(!string.IsNullOrWhiteSpace((string)Get(handle,"Tooltip")),"The move handle has no name");
   var anchoredRow1=row1.Bounds;
   Func<Rectangle> allowedNow=()=>(Rectangle)Call(form,"ChromeAllowedArea",Call(form,"ChromeScale"));
   var pointer=Point.Empty;
   var pointerHook=row1Surface.GetType().GetProperty("PointerScreenPosition",Flags);
   pointerHook.SetValue(row1Surface,(Func<Point>)(()=>pointer));
   var handleBounds=(Rectangle)Get(handle,"Bounds");
   var press=new Point(handleBounds.Left+handleBounds.Width/2,handleBounds.Top+handleBounds.Height/2);
   var packedPress=(IntPtr)((press.Y<<16)|(press.X&0xffff));
   var pressScreen=row1Surface.PointToScreen(press);
   pointer=pressScreen;
   SendMessage(row1Surface.Handle,0x201,(IntPtr)1,packedPress);
   foreach(var travel in new[]{new Size(40,40),new Size(400,300),new Size(3000,2000),new Size(-3000,-2000),new Size(-3000,2000),new Size(250,180)}) {
    pointer=pressScreen+travel;
    SendMessage(row1Surface.Handle,0x200,(IntPtr)1,packedPress); Application.DoEvents();
    Assert(allowedNow().Contains(row1.Bounds),"Row 1 left the allowed area mid-drag at "+travel+": "+row1.Bounds);
   }
   SendMessage(row1Surface.Handle,0x202,IntPtr.Zero,packedPress); Pump();
   var place=(string)placeProperty.GetValue(prefs);
   var dropped=row1.Bounds;
   Assert(!string.IsNullOrEmpty(place) && dropped!=anchoredRow1,"A drop did not store a place for row 1");
   foreach(var size in new[]{new Size(600,420),new Size(420,300)}) {
    form.ClientSize=size; Call(form,"PlaceChromePanels"); Pump();
    var area=allowedNow();
    Assert(area.Contains(row1.Bounds) || row1.Width>area.Width,"Row 1 left the window after a resize to "+size+": "+row1.Bounds);
    Assert((string)placeProperty.GetValue(prefs)==place,"A resize wrote the place back");
   }
   form.ClientSize=new Size(1000,680); Call(form,"PlaceChromePanels"); Pump();
   Assert(row1.Bounds==dropped,"Row 1 did not return to its place when the window grew again");
   // What a restart reads: the stored text alone puts the panel where it was dropped.
   placeProperty.SetValue(prefs,""); Call(form,"PlaceChromePanels"); Pump();
   Assert(row1.Bounds==anchoredRow1,"An empty place did not anchor row 1");
   placeProperty.SetValue(prefs,place); Call(form,"PlaceChromePanels"); Pump();
   Assert(row1.Bounds==dropped,"The stored place did not put row 1 back where it was dropped");
   Assert((bool)Call(form,"ResetChromePanelPlaces"),"Returning the panels reported nothing to do"); Pump();
   Assert(string.IsNullOrEmpty((string)placeProperty.GetValue(prefs)) && row1.Bounds==anchoredRow1,"Returning the panels did not anchor row 1");
   Assert(!(bool)Call(form,"ResetChromePanelPlaces"),"A second return was not idempotent");
   Assert(form.ContainsFocus,"A panel move stole viewer focus");
   pointerHook.SetValue(row1Surface,null);
   prefs.GetType().GetProperty("ChromePinned").SetValue(prefs,false);
   prefs.GetType().GetProperty("VideoControlsHideDelaySec").SetValue(prefs,1);
   Cursor.Position=form.PointToScreen(new Point(form.ClientSize.Width/2,form.ClientSize.Height/2)); Pump(1300);
   Assert(Hosts(form).All(h=>!h.Visible),"Chrome did not hide on idle");
   Assert(bounds==media.Bounds,"Auto-hide changed media geometry");
   prefs.GetType().GetProperty("ReduceMotion").SetValue(prefs,false); Call(form,"ChromeConfigure");
   Cursor.Position=form.PointToScreen(new Point(form.ClientSize.Width/2,20));
   Call(form,"Chrome_Timer_Tick",null,EventArgs.Empty); Pump(65);
   if((bool)Call(form,"ChromeFadeAllowed")) Assert(Hosts(form).Any(h=>h.Visible && h.Opacity>0 && h.Opacity<0.86),"Reveal did not produce intermediate fade opacity");
   Pump(220); Assert(Hosts(form).Any(h=>h.Visible),"Pointer hot zone did not reveal the chrome");
   Call(form,"DisposeRecipientsOverlayResources"); Call(form,"DisposeChromeResources"); form.Hide();
   Console.WriteLine("Geometry, tooltips, ABC/XYZ folder order, mouse/keyboard menus, font handle recreation, scale, panel move (drag inside, resize, stored place, return) and idle hide passed; DPI="+form.DeviceDpi);
   return 0;
  } catch(Exception e) { Console.WriteLine(e); return 1; }
 }
}
'@ | Set-Content -LiteralPath (Join-Path $WorkDir 'ChromeScenes.cs') -Encoding utf8
& dotnet build (Join-Path $WorkDir 'ChromeScenes.csproj') -v:q
if ($LASTEXITCODE -ne 0) { Exit-Verdict 'ModernChromeScenes' 2 'scene harness could not build' }
& (Join-Path $WorkDir 'bin/Debug/net10.0-windows/ChromeScenes.exe') $WorkDir
Exit-Verdict 'ModernChromeScenes' $LASTEXITCODE "evidence: $WorkDir"
