using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.VisualBasic;
using FastMediaSorterCompanion;

// Only UI rendering and isolated settings. Never show the main window, start a worker, or invoke enable/folder acts.
static class Program
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly Assembly App = typeof(MainWindow).Assembly;
    static readonly Type Wizard = App.GetType("FastMediaSorterCompanion.Manager_Welcome_Form", true);
    static readonly Type Step = App.GetType("FastMediaSorterCompanion.ManagerWelcomeStep", true);
    static object Field(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Instance).Invoke(target, args);
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static IEnumerable<Control> Children(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Children(child)) yield return nested;
        }
    }
    static Form Create(MainWindow host) => (Form)Activator.CreateInstance(Wizard, Instance, null, new object[] { host }, null);
    static void ShowStep(Form form, int index) => Call(form, "ShowStep", Enum.ToObject(Step, index));
    static void Capture(Form form, string directory, string name)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(directory, name + ".png"), ImageFormat.Png);
    }
    static void Check(Form form)
    {
        Assert(Screen.FromControl(form).WorkingArea.Contains(form.Bounds), "Wizard extends outside working area");
        foreach (var control in Children(form).Where(c => c.Visible && (c is ButtonBase || c is ComboBox || c is NumericUpDown)))
        {
            Assert(control.Height >= form.LogicalToDeviceUnits(44), $"Small target: {control.GetType().Name} {control.Height}: {control.Text}");
            Assert(control.Parent.ClientRectangle.Contains(control.Bounds), $"Clipped target: {control.Text}: {control.Bounds} in {control.Parent.ClientRectangle}");
            if (control is Button button) Assert(button.Image != null, $"Missing command glyph: {button.Text}");
        }
        foreach (var label in Children(form).OfType<Label>().Where(c => c.Visible))
            Assert(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height, $"Clipped text: {label.Text}");
        var contrast = App.GetType("FastMediaSorterCompanion.AccentColors", true).GetMethod("Contrast", Static);
        foreach (var label in (IEnumerable<Label>)Field(form, "headings"))
            if (label.Visible) Assert((double)contrast.Invoke(null, new object[] { label.ForeColor, label.BackColor }) >= 4.5,
                $"Unreadable step heading: {label.Text}");
        var stateLabel = (Label)Field(form, "stateLabel");
        if (stateLabel.Visible && stateLabel.Text.Length > 0)
            Assert((double)contrast.Invoke(null, new object[] { stateLabel.ForeColor, stateLabel.BackColor }) >= 4.5,
                "Unreadable finish state");
    }

    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetDefaultFont(new Font("Segoe UI", 9f));
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        var globals = typeof(CompanionGlobals).GetProperty("SettingsAppNameOverride", Static);
        string scratch = "FmsManagerWelcomeScenes_" + Guid.NewGuid().ToString("N");
        string evidence = Path.GetFullPath(args.Length == 0 ? "temp/welcome-212-scenes" : args[0]);
        Directory.CreateDirectory(evidence);
        globals.SetValue(null, scratch);
        var glyphs = App.GetType("FastMediaSorterCompanion.ButtonGlyphs", true);
        var assets = App.GetType("FastMediaSorterCompanion.CompanionAssets", true);
        glyphs.GetProperty("AssetOpener", Static).SetValue(null, Delegate.CreateDelegate(typeof(Func<string, Stream>), assets.GetMethod("OpenBundledAsset", Static)));
        var theme = App.GetType("FastMediaSorterCompanion.ThemeService", true);
        var mode = App.GetType("FastMediaSorterCompanion.ThemeMode", true);
        int rendered = 0;
        try
        {
            Console.WriteLine($"ManagerWelcomeScenes: isolated hive; local UI only; evidence {evidence}");
            foreach (string themeName in new[] { "Light", "Dark" })
            {
                theme.GetMethod("SetMode", Static).Invoke(null, new[] { Enum.Parse(mode, themeName) });
                foreach (string code in Localization.Codes)
                {
                    Localization.CurrentCode = code;
                    using var host = new MainWindow();
                    using var form = Create(host);
                    ShowStep(form, 0);
                    theme.GetMethod("Attach", Static).Invoke(null, new object[] { form });
                    form.Show();
                    Application.DoEvents();
                    Assert(form.RightToLeft == (code is "ar" or "ur" ? RightToLeft.Yes : RightToLeft.No), "Reading direction mismatch");
                    Assert(!form.RightToLeftLayout, "Wizard must not mirror its device context");
                    Assert(((System.Collections.ICollection)Field(form, "panels")).Count == 6, "Not six steps");
                    Assert(((System.Collections.ICollection)Field(form, "headings")).Count == 24, "Steps lack what/why/how/where");
                    Assert(Interaction.GetSetting(scratch, CompanionGlobals.Second_App_Name, "Welcome_ManagerDone", "") == "", "Rendering wrote a done marker");
                    for (int index = 0; index < 6; index++)
                    {
                        ShowStep(form, index);
                        Application.DoEvents();
                        Check(form);
                        Capture(form, evidence, $"{themeName}-{code}-{index}");
                    }
                    Console.WriteLine($"  {themeName} {code}: six steps; DPI={form.DeviceDpi}");
                    rendered++;
                    ShowStep(form, 4);
                    var language = (ComboBox)Field(form, "language");
                    language.Focus();
                    language.DroppedDown = true;
                    Call(form, "ProcessCmdKey", Message.Create(form.Handle, 0x100, (IntPtr)Keys.Escape, IntPtr.Zero), Keys.Escape);
                    Assert(form.Visible && !language.DroppedDown, "Dropdown Escape closed the wizard");
                    Call(form, "ProcessCmdKey", Message.Create(form.Handle, 0x100, (IntPtr)Keys.Escape, IntPtr.Zero), Keys.Escape);
                    Assert(!form.Visible, "Escape did not leave the wizard");
                    Assert(Interaction.GetSetting(scratch, CompanionGlobals.Second_App_Name, "Welcome_ManagerDone", "") == "", "Escape recorded completion");
                }
            }
            Localization.CurrentCode = "en";
            using (var host = new MainWindow())
            using (var form = Create(host))
            {
                ShowStep(form, 0);
                theme.GetMethod("Attach", Static).Invoke(null, new object[] { form });
                form.Show();
                Application.DoEvents();
                foreach (bool access in new[] { true, false })
                {
                    typeof(MainWindow).GetField("_status", Instance).SetValue(host, new WorkerStatus
                    {
                        Running = true, ListenPort = 2222, PortSupported = true,
                        Roots = new List<ShareFolder> { new() { hostPath = "fixture", name = "fixture" } }
                    });
                    typeof(MainWindow).GetField("_rootsWithoutAccess", Instance).SetValue(host, access ? 0 : 1);
                    ShowStep(form, 5);
                    var snapshot = Call(host, "WelcomeStatusSnapshot");
                    Call(form, "RenderFinish", snapshot);
                    Application.DoEvents();
                    Check(form);
                    Assert(((Button)Field(form, "pairButton")).Enabled == access,
                        $"Pairing mismatch: access={access}, enabled={((Button)Field(form, "pairButton")).Enabled}, green={snapshot.GetType().GetProperty("Green", Instance).GetValue(snapshot)}, state={snapshot.GetType().GetProperty("Text", Instance).GetValue(snapshot)}");
                    Capture(form, evidence, access ? "finish-green" : "finish-attention");
                }
                form.Close();
            }
            Console.WriteLine($"ManagerWelcomeScenes: PASS ({rendered} language/theme scenes; six steps; honest finish; no live worker or elevation)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Console.Error.WriteLine("ManagerWelcomeScenes: FAIL");
            return 1;
        }
        finally
        {
            globals.SetValue(null, null);
            try { Interaction.DeleteSetting(scratch); } catch (ArgumentException) { }
        }
    }
}
