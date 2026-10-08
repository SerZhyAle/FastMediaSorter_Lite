using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualBasic;
using fmsl;

// Do not run the viewer lifecycle: these scenes must never load or save the user's profile.
sealed class SceneViewer : Main_Form
{
    protected override void OnLoad(EventArgs e) { }
    protected override void OnShown(EventArgs e) { }
    protected override void OnFormClosing(FormClosingEventArgs e) { }
}

static class Program
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly App = typeof(Main_Form).Assembly;
    static readonly Type Wizard = App.GetType("fmsl.Welcome_Form", true);
    static readonly Type Common = App.GetType("fmsl.Common_Module", true);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    static object Field(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
    static object Call(object target, string name, params object[] args) =>
        (target is Main_Form ? typeof(Main_Form) : target.GetType()).GetMethod(name, Instance).Invoke(target, args);
    static void SetCommon(string name, object value) => Common.GetField(name, Static).SetValue(null, value);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static IEnumerable<Control> Children(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Children(child)) yield return nested;
        }
    }
    static Form Open(Main_Form viewer, bool packaged = false)
    {
        var form = (Form)Activator.CreateInstance(Wizard, Instance, null, new object[] { viewer, packaged }, null);
        App.GetType("fmsl.ThemeService", true).GetMethod("Attach", Static).Invoke(null, new object[] { form });
        form.Show();
        Application.DoEvents();
        return form;
    }
    static void Capture(Form form, string directory, string name)
    {
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
        image.Save(Path.Combine(directory, name + ".png"), ImageFormat.Png);
    }
    static void CheckTargets(Form form)
    {
        int target = form.LogicalToDeviceUnits(44);
        foreach (var control in Children(form).Where(c => c.Visible && (c is Button || c is CheckBox || c is ComboBox)))
        {
            Assert(control.Height >= target, $"{control.GetType().Name} target {control.Height} < {target}: {control.Text}");
            Assert(control.Parent.ClientRectangle.Contains(control.Bounds), $"Control is clipped by its parent: {control.GetType().Name} {control.Bounds} inside {control.Parent.ClientRectangle}; preferred={control.Parent.PreferredSize}; parent={control.Parent.Parent.Size}; minimum={control.MinimumSize}; comboPreferred={control.PreferredSize}");
        }
        Assert(Screen.FromControl(form).WorkingArea.Contains(form.Bounds), "Wizard extends outside the work area");
        foreach (var heading in (IEnumerable<Label>)Field(form, "headings"))
            if (heading.Visible)
                Assert((double)App.GetType("fmsl.Utils", true).GetMethod("ContrastRatio", Static).Invoke(null,
                    new object[] { heading.ForeColor, heading.BackColor }) >= 4.5, "A step heading does not meet text contrast");
    }

    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetDefaultFont(new Font("Microsoft Sans Serif", 8.25f));
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        bool controlsOnly = args.Contains("--controls-only");
        int rendered = 0;
        string evidence = Path.GetFullPath(args.Length > 0 ? args[0] : "temp/welcome-184/scenes");
        Directory.CreateDirectory(evidence);
        Console.WriteLine($"WelcomeWizardScenes: subject = built modern wizard; desktop DPI; evidence {evidence}");
        string scratchApp = "FmsWelcomeScenes_" + Guid.NewGuid().ToString("N");
        string originalApp = (string)Common.GetField("App_name", Static).GetValue(null);
        string section = (string)Common.GetField("Second_App_Name", Static).GetValue(null);
        SetCommon("App_name", scratchApp);
        var logger = App.GetType("fmsl.AppFileLogger", true);
        logger.GetField("LogDirectoryOverride", Static).SetValue(null, evidence);
        logger.GetMethod("Initialize", Static).Invoke(null, null);
        try
        {
            Localization.CurrentCode = "en";
            SetCommon("Form_Color_Scheme", 1);
            SetCommon("Zoom_Wheel_Zooms", false);
            using var viewer = new SceneViewer();
            typeof(Main_Form).GetField("toolTip", Instance).SetValue(viewer, new ToolTip());
            Call(viewer, "EnsureSettingsWindow");
            Call(viewer, "SaveSettingsNow");
            string originalScheme = Interaction.GetSetting(scratchApp, section, "color_scheme", "absent");
            string originalFolder = Interaction.GetSetting(scratchApp, section, "MoveOn1", "absent");
            using (var form = Open(viewer))
            {
                Console.WriteLine($"UI DPI={form.DeviceDpi}; all languages, every step, light/dark, repeated script changes");
                // Two cycles cross each script boundary, including switching back to the default font.
                foreach (string theme in controlsOnly ? Array.Empty<string>() : new[] { "Light", "Dark" })
                {
                    var service = App.GetType("fmsl.ThemeService", true);
                    service.GetMethod("SetMode", Static).Invoke(null, new[] { Enum.Parse(App.GetType("fmsl.ThemeMode", true), theme) });
                    foreach (string code in Localization.Codes)
                    {
                        Call(form, "PreviewLanguage", code);
                        Assert(Localization.CurrentCode == "en", "Preview leaked into the viewer's language");
                        Assert(form.RightToLeft == (code is "ar" or "ur" ? RightToLeft.Yes : RightToLeft.No), "Reading direction is wrong");
                        Assert(!form.RightToLeftLayout, "The window's device context must never mirror");
                        for (int step = 0; step < 6; step++)
                        {
                            Call(form, "ShowStep", step);
                            Application.DoEvents();
                            form.Refresh();
                            CheckTargets(form);
                            if (step == 0 || step == 2 || step == 5) Capture(form, evidence, $"{theme}-{code}-{step}");
                        }
                        Console.WriteLine($"  {theme} {code}: six steps rendered");
                        rendered++;
                    }
                }
                Assert(((System.Collections.IDictionary)Field(form, "scriptFonts")).Count <= 4, "Script changes grew the owned font cache without bound");
                Console.WriteLine("Checking Skip and completion markers");
                Call(form, "ShowStep", 0);
                Call(form, "PreviewLanguage", "ur");
                Application.DoEvents();
                CheckTargets(form);
                Capture(form, evidence, "rtl-before-skip");
                Set(form, "draftScheme", 2);
                Set(form, "draftWheelZooms", true);
                Set(form, "draftKeyOne", Path.Combine(evidence, "unapplied-folder"));
                Set(form, "draftAssociations", true);
                Call(form, "SkipToFinish", null, EventArgs.Empty);
                Assert(Localization.CurrentCode == "en", "Skip committed a preview");
                Assert(Interaction.GetSetting(scratchApp, section, "color_scheme", "absent") == originalScheme, "Skip wrote an appearance draft");
                Assert(Interaction.GetSetting(scratchApp, section, "MoveOn1", "absent") == originalFolder, "Skip wrote a folder draft");
                form.Close();
                Assert(Interaction.GetSetting(scratchApp, section, "WelcomeDone", "") == "1", "Close did not record completion");
                Assert(Interaction.GetSetting(scratchApp, section, "WelcomePending", "") == "0", "Close did not clear pending");
            }

            if (!controlsOnly)
            {
                foreach (string code in new[] { "ar", "ur", "hi", "bn", "zh" })
                {
                    Localization.CurrentCode = code;
                    using var initial = Open(viewer);
                    for (int step = 0; step < 6; step++)
                    {
                        Call(initial, "ShowStep", step);
                        Application.DoEvents();
                        CheckTargets(initial);
                    }
                    Capture(initial, evidence, "initial-" + code);
                    initial.Close();
                    Console.WriteLine($"  Constructed directly in {code}: six steps rendered");
                }
                Localization.CurrentCode = "en";
            }

            // Actual host writes followed by its close-path writer, in an isolated registry section.
            using (var form = Open(viewer))
            {
                Console.WriteLine("Checking immediate writes and close-path persistence");
                Call(form, "ShowStep", 1);
                Set(form, "draftScheme", 2);
                Set(form, "draftWheelZooms", true);
                Call(form, "GoNext", null, EventArgs.Empty);
                Assert(Interaction.GetSetting(scratchApp, section, "color_scheme", "") == "2", "Next did not flush background");
                Assert(Interaction.GetSetting(scratchApp, section, "WheelZooms", "") == "1", "Next did not flush wheel");
                Set(form, "draftKeyOne", evidence);
                Call(form, "GoNext", null, EventArgs.Empty);
                Assert(Interaction.GetSetting(scratchApp, section, "MoveOn1", "") == evidence, "Next did not flush key 1");
                Assert((bool)Call(viewer, "PersistSettings"), "Close-path settings save failed");
                Assert(Interaction.GetSetting(scratchApp, section, "color_scheme", "") == "2", "Close clobbered background");
                Assert(Interaction.GetSetting(scratchApp, section, "WheelZooms", "") == "1", "Close clobbered wheel");
                Assert(Interaction.GetSetting(scratchApp, section, "MoveOn1", "") == evidence, "Close clobbered key 1");
                form.Close();
            }
            using (var form = Open(viewer, packaged: true))
            {
                Console.WriteLine("Checking packaged steps, Enter, dropdown Escape and preview cancellation");
                Assert(((System.Collections.ICollection)Field(form, "panels")).Count == 5, "MSIX offered associations");
                Call(form, "PreviewLanguage", "de");
                Assert((bool)Call(form, "ProcessDialogKey", Keys.Enter), "Enter did not advance the wizard");
                Assert(Localization.CurrentCode == "de", "Enter did not commit the language");
                Assert(Interaction.GetSetting(scratchApp, section, "UiLanguage", "") == "de", "Language was not flushed");
                Call(form, "ShowStep", 0);
                Call(form, "PreviewLanguage", "ur");
                var combo = Children(form).OfType<ComboBox>().First(c => c.Visible);
                combo.Focus();
                var escape = Message.Create(combo.Handle, 0x100, (IntPtr)(int)Keys.Escape, IntPtr.Zero);
                // Native dropdown tracking pumps a nested loop; queue Escape before entering it.
                PostMessage(combo.Handle, 0x100, (IntPtr)(int)Keys.Escape, IntPtr.Zero);
                combo.DroppedDown = true;
                Assert(form.Visible, "Escape in a dropdown closed the wizard");
                combo.DroppedDown = false;
                Call(form, "ProcessCmdKey", escape, Keys.Escape);
                Assert(!form.Visible && Localization.CurrentCode == "de", "Escape did not close and discard the preview");
            }
            var cardType = App.GetType("fmsl.HelpCardPanel", true);
            Console.WriteLine("Checking F1 link by pointer action and keyboard");
            using (var card = (Control)Activator.CreateInstance(cardType))
            {
                cardType.GetMethod("SetContent", Instance).Invoke(card, new[] {
                    App.GetType("fmsl.HelpCardRows", true).GetMethod("Build", Static).Invoke(null, new object[] { null }) });
                var link = Children(card).OfType<LinkLabel>().Single();
                Assert(link.TabStop, "F1 welcome link is absent from keyboard traversal");
                bool requested = false;
                EventHandler handler = (_, _) => requested = true;
                cardType.GetEvent("WelcomeRequested").AddEventHandler(card, handler);
                typeof(LinkLabel).GetMethod("OnLinkClicked", Instance).Invoke(link, new object[] { new LinkLabelLinkClickedEventArgs(new LinkLabel.Link()) });
                Assert(requested, "F1 link did not request the wizard");
                using var window = new Form { ClientSize = new Size(640, 600) };
                card.Dock = DockStyle.Fill;
                window.Controls.Add(card);
                window.Show();
                card.Visible = true;
                Application.DoEvents();
                var scroll = Children(card).OfType<VScrollBar>().Single();
                Assert(scroll.LargeChange == card.ClientSize.Height - link.Height, "F1 PageDown skips reference text under the pinned link");
                Assert((double)App.GetType("fmsl.Utils", true).GetMethod("ContrastRatio", Static).Invoke(null,
                    new object[] { link.LinkColor, link.BackColor }) >= 4.5, "F1 welcome link does not meet text contrast");
                requested = false;
                Assert(link.Focus(), "F1 welcome link cannot receive keyboard focus");
                Assert((bool)Call(card, "TryWelcomeKey", Keys.Enter) && requested, "Enter did not activate F1 welcome link");
                window.Close();
            }
            Console.WriteLine($"WelcomeWizardScenes: PASS ({rendered} language/theme scenes; isolated persistence, skip, MSIX, Enter/Escape and F1; evidence: {evidence})");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            Console.Error.WriteLine("WelcomeWizardScenes: FAIL");
            return 1;
        }
        finally
        {
            // This exact randomly named section belongs only to this process.
            try { Interaction.DeleteSetting(scratchApp); } catch (ArgumentException) { }
            SetCommon("App_name", originalApp);
        }
    }
}
