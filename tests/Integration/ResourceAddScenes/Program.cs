using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic;
using FastMediaSorterCompanion;

static class Program
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Field(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    static void Assert(bool ok, string text) { if (!ok) throw new Exception(text); }
    static IEnumerable<Control> Children(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var nested in Children(child)) yield return nested;
        }
    }
    sealed class Ports : IResourceAddActions
    {
        public int Commits;
        public int Pairs;
        public bool CanPickDisks => true;
        public string Pick(Form owner, bool disk) => "C:\\SceneFolder";
        public bool Duplicate(string path) => false;
        public void WarnNetwork(Form owner, string path) { }
        public ShareRootParams Parameters(string path) => new() { Profile = "all_files" };
        public string FolderName(string path) => "SceneFolder";
        public Task<ResourceAddResult> CommitAsync(Form owner, string path, ShareRootParams value, bool exchange, bool publish)
        {
            Commits++;
            return Task.FromResult(new ResourceAddResult { Added = true, Delivery = ResourceAddDelivery.NoPhone,
                Publication = exchange ? ExchangePublishText.StateBlocked(ExchangePublishBlock.PinLocked) : "" });
        }
        public void Pair(Form owner) { Pairs++; }
        public void Settings(Form owner) { }
    }
    static void Check(Form form)
    {
        Assert(Screen.FromControl(form).WorkingArea.Contains(form.Bounds), "Outside working area");
        foreach (var control in Children(form).Where(c => c.Visible && (c is ButtonBase || c is ComboBox || c is NumericUpDown || (c is TextBox && !(c.Parent is NumericUpDown)))))
        {
            Assert(control.Height >= form.LogicalToDeviceUnits(44), $"Small target: {control.GetType().Name} {control.Text} ({control.Height} < {form.LogicalToDeviceUnits(44)})");
            Assert(control.Parent.ClientRectangle.Contains(control.Bounds), $"Clipped target: {control.Text} {control.Bounds} in {control.Parent.ClientRectangle}");
            if (control is Button button) Assert(button.Image != null, $"No glyph: {button.Text}");
        }
        foreach (var label in Children(form).OfType<Label>().Where(c => c.Visible))
            Assert(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height, $"Clipped text: {label.Text}");
        foreach (var label in (IEnumerable<Label>)Field(form, "headings"))
            if (label.Visible) Assert(AccentColors.Contrast(label.ForeColor, label.BackColor) >= 4.5, $"Unreadable heading: {label.Text}");
    }
    static void Capture(Form form, string dir, string name)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
    }
    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetDefaultFont(new Font("Segoe UI", 9f));
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        string gate = Environment.GetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName);
        string priorApp = CompanionGlobals.SettingsAppNameOverride;
        string scratch = "FmsResourceAddScenes_" + Guid.NewGuid().ToString("N");
        string evidence = Path.GetFullPath(args.Length == 0 ? "temp/resource-214-scenes" : args[0]);
        Directory.CreateDirectory(evidence);
        Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, "1");
        CompanionGlobals.SettingsAppNameOverride = scratch;
        ButtonGlyphs.AssetOpener = CompanionAssets.OpenBundledAsset;
        int count = 0;
        try
        {
            foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
            foreach (var code in Localization.Codes)
            foreach (var entry in new[] { ResourceAddEntry.AddButton, ResourceAddEntry.Exchange })
            {
                ThemeService.SetMode(mode);
                Localization.CurrentCode = code;
                var baselineSettings = Interaction.GetAllSettings(scratch, CompanionGlobals.Second_App_Name);
                string baseline = baselineSettings == null ? "" : string.Join("|", baselineSettings.Cast<string>());
                var ports = new Ports();
                using var form = new Resource_Add_Form(ports, entry);
                form.Show();
                Application.DoEvents();
                Assert(form.RightToLeft == (code is "ar" or "ur" ? RightToLeft.Yes : RightToLeft.No), "RTL mismatch");
                Assert(!form.RightToLeftLayout, "Mirrored device context");
                // Match production: the editor is constructed after the wizard is already shown.
                Call(form, "AcceptPick", "C:\\SceneFolder");
                for (int i = 0; i < 4; i++)
                {
                    if (i == 3)
                    {
                        Call(form, "ShowStep", ResourceAddStep.Questions);
                        ((Task)Call(form, "AdvanceAsync")).GetAwaiter().GetResult();
                    }
                    else Call(form, "ShowStep", (ResourceAddStep)i);
                    Application.DoEvents();
                    Check(form);
                    Capture(form, evidence, $"{mode}-{code}-{entry}-{i}");
                    count++;
                }
                Assert(ports.Commits == 0 && ports.Pairs == 0, "Rendering performed acts");
                var afterSettings = Interaction.GetAllSettings(scratch, CompanionGlobals.Second_App_Name);
                Assert((afterSettings == null ? "" : string.Join("|", afterSettings.Cast<string>())) == baseline, "Rendering changed the hive");
                Call(form, "ShowStep", ResourceAddStep.Questions);
                var editor = (Share_Root_Params_Form)Field(form, "editor");
                Assert(!((TextBox)Field(editor, "txtComment")).Visible, "Advanced options open by default");
                Children(form).OfType<Button>().Single(b => b.Text == Localization.T("Дополнительные параметры")).PerformClick();
                Application.DoEvents();
                Check(form);
                Children(form).OfType<Panel>().First(p => p.AutoScroll && p.Dock == DockStyle.Fill).ScrollControlIntoView((TextBox)Field(editor, "txtPin"));
                Application.DoEvents();
                Capture(form, evidence, $"{mode}-{code}-{entry}-advanced");
                count++;
                ((TextBox)Field(editor, "txtPin")).Text = "ScenePin214";
                ((Task)Call(form, "AdvanceAsync")).GetAwaiter().GetResult();
                Assert(ports.Commits == 0, "Questions Next committed");
                ((Task)Call(form, "AdvanceAsync")).GetAwaiter().GetResult();
                Application.DoEvents();
                Check(form);
                Assert(ports.Commits == 1 && ports.Pairs == 0, "Commit or pairing count");
                Assert(!((Label)Field(form, "facts")).Text.Contains("ScenePin214"), "PIN in finish");
                Assert(((Button)Field(form, "pairButton")).Visible, "QR pointer absent");
                Capture(form, evidence, $"{mode}-{code}-{entry}-done");
                count++;
                Console.WriteLine($"{mode} {code} {entry}: four steps + done, DPI={form.DeviceDpi}");
                form.Close();
            }
            Console.WriteLine($"ResourceAddScenes: PASS ({count} captures; scripted ports; no worker, publication, disk mount or elevation)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Console.Error.WriteLine("ResourceAddScenes: FAIL");
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, gate);
            CompanionGlobals.SettingsAppNameOverride = priorApp;
            try { Interaction.DeleteSetting(scratch); } catch { }
        }
    }
}
