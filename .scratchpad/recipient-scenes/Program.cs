using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using System.Linq;
using fmsl;
class Viewer : Main_Form { protected override void OnLoad(EventArgs e) {} protected override void OnShown(EventArgs e) {} protected override void OnFormClosing(FormClosingEventArgs e) {} }
static class Program {
const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
static object Get(object obj,string name) { var t=obj.GetType(); while(t!=null) { var f=t.GetField(name,F|BindingFlags.DeclaredOnly)??t.GetField("_"+name,F|BindingFlags.DeclaredOnly); if(f!=null)return f.GetValue(obj); var p=t.GetProperty(name,F|BindingFlags.DeclaredOnly); if(p!=null)return p.GetValue(obj); t=t.BaseType; } throw new Exception(name); }
static void Set(object obj,string name,object value) { var t=obj.GetType(); while(t!=null) { var f=t.GetField(name,F|BindingFlags.DeclaredOnly); if(f!=null){f.SetValue(obj,value);return;} t=t.BaseType; } throw new Exception(name); }
static object Call(object obj,string name,params object[] args) { var t=obj.GetType(); while(t!=null) {var m=t.GetMethods(F|BindingFlags.DeclaredOnly).FirstOrDefault(m=>m.Name==name&&m.GetParameters().Length==args.Length); if(m!=null)return m.Invoke(obj,args);t=t.BaseType;}throw new Exception(name); }
static void Assert(bool ok,string msg) {if(!ok)throw new Exception(msg);}
static void Save(Control c,string path) {using var b=new Bitmap(c.Width,c.Height);c.DrawToBitmap(b,c.ClientRectangle);b.Save(path);}
[STAThread]static int Main(string[] args) {try {
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
var asm=typeof(Main_Form).Assembly;var common=asm.GetType("fmsl.Common_Module");var slots=(string[])common.GetField("Hardkeys_to_move_mediafile",F).GetValue(null);
for(int i=1;i<=10;i++)slots[i]=@"C:\Recipients\Folder"+i;
var palette=asm.GetType("fmsl.RecipientPalette");
using var viewer=new Viewer {ClientSize=new Size(1000,700)};Call(viewer,"BuildModernLayout");Call(viewer,"ApplyModernChrome");
using var table=new Table_Form();var grid=(DataGridView)Get(table,"Data_Grid_View");
using var host=new Form {ClientSize=new Size(720,440)};host.Controls.Add(grid);grid.Dock=DockStyle.Fill;grid.Rows.Clear();grid.Rows.Add(11);for(int i=0;i<11;i++){grid[0,i].Value=i==0?"DEL":i==10?"0":i.ToString();grid[1,i].Value=i==0?"Delete":slots[i];}grid.CellPainting+=(s,e)=>Call(table,"Data_Grid_View_CellPainting",s,e);
var my=asm.GetType("fmsl.My.MyProject");var forms=my.GetProperty("Forms",F).GetValue(null);var defaultViewer=forms.GetType().GetProperty("Main_Form",F).GetValue(forms);
for(int i=1;i<=10;i++) { var v=SlotVerdict.ForState(i%3==0?SlotState.Denied:i%3==1?SlotState.Ready:SlotState.WillBeCreated,DateTime.UtcNow);Call(defaultViewer,"StoreSlotVerdict",slots[i],v); }
foreach(bool dark in new[]{false,true}) {
var theme=ThemePalette.ForDark(dark);typeof(ThemeService).GetField("_palette",F).SetValue(null,theme);
Set(table,"settingsDarkMode",dark);Set(table,"SettingsText",theme.TextPrimary);Set(table,"SettingsSurface",theme.SurfaceWindow);Set(table,"SettingsAccent",theme.Accent);Set(table,"SettingsOnAccent",theme.AccentInk);
Call(table,"ApplySlotHealthToGrid");grid.CurrentCell=grid[1,3];host.Show();Application.DoEvents();
for(int i=1;i<=10;i++){var expected=(Color)palette.GetMethod("ColorFor",F).Invoke(null,new object[]{i,dark});Assert(grid[1,i].Style.BackColor==expected,"grid fill");Assert(grid[1,i].Style.SelectionBackColor==expected,"selection fill");Assert(grid[1,i].Style.Padding.Left>0,"padding");Assert((Color)Call(viewer,"RecipientRowFill",i)==expected,"overlay differs from grid");}
Save(grid,args[0]+"/grid-"+(dark?"dark":"light")+".png");
Call(viewer,"BuildRecipientsOverlay");var overlay=(Form)Get(viewer,"recipients_Overlay");var rows=(Control)Get(viewer,"recipients_Overlay_Rows");
foreach(Button b in rows.Controls.OfType<Button>().Where(b=>b.Name=="recipient_move_row")){int slot=(int)b.Tag;Assert(b.BackColor==grid[1,slot].Style.BackColor,"actual overlay button fill");Assert(b.ForeColor==theme.TextPrimary,"actual overlay ink");}
rows.Height=rows.Controls.Cast<Control>().Max(c=>c.Bottom);overlay.Height=rows.Top+rows.Height;overlay.Show();Application.DoEvents();
Save(overlay,args[0]+"/overlay-"+(dark?"dark":"light")+".png");Call(viewer,"DisposeRecipientsOverlayResources");host.Hide();
}
Console.WriteLine("PASS: actual grid selection/padding, overlay buttons, colour parity and theme inks; light/dark images captured.");return 0;
}catch(Exception e){Console.WriteLine(e);return 1;}}
}