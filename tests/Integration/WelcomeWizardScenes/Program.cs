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
    // Reflection does not surface a base class's PRIVATE members through a derived type, so the
    // member walk goes up the hierarchy; a VB WithEvents declaration keeps its name as a PROPERTY
    // (the backing field is renamed), hence the property fallback.
    static MemberInfo Member(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Instance | BindingFlags.DeclaredOnly);
            if (field != null) return field;
            var property = type.GetProperty(name, Instance | BindingFlags.DeclaredOnly);
            if (property != null) return property;
        }
        return null;
    }
    static object Field(object target, string name)
    {
        switch (Member(target, name))
        {
            case FieldInfo field: return field.GetValue(target);
            case PropertyInfo property: return property.GetValue(target);
            default: return null;
        }
    }
    static object Call(object target, string name, params object[] args) =>
        (target is Main_Form ? typeof(Main_Form) : target.GetType()).GetMethod(name, Instance).Invoke(target, args);
    static void SetCommon(string name, object value) => Common.GetField(name, Static).SetValue(null, value);
    static void Set(object target, string name, object value)
    {
        switch (Member(target, name))
        {
            case FieldInfo field: field.SetValue(target, value); break;
            case PropertyInfo property: property.SetValue(target, value); break;
        }
    }
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
                // Spec 211 section 4.1: every step carries the four named parts - what, why, how, where.
                // Plus the installed-servers block of spec 215: its title and one name per prepared server row also
                // register as headings (the contrast check below reads them), whether or not the row is shown.
                int namedParts = 4 * ((System.Collections.ICollection)Field(form, "panels")).Count
                    + 1 + ((System.Collections.ICollection)Field(form, "serverRows")).Count;
                Assert(((System.Collections.ICollection)Field(form, "headings")).Count == namedParts,
                    "The steps do not carry the four named parts");
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
                            if (step == 0 || step == 2 || step == 3 || step == 5) Capture(form, evidence, $"{theme}-{code}-{step}");
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
            using (var form = Open(viewer))
            {
                Console.WriteLine("Checking the W-4 delete question (spec 211 section 4.2)");
                Call(form, "ShowStep", 3);
                var deleteCheck = Children(form).OfType<CheckBox>().Single(c => c.Visible);
                Assert(deleteCheck.Checked, "W-4 does not open on the code default (the Bin)");
                string deleteBefore = Interaction.GetSetting(scratchApp, section, "DeleteToRecycleBin", "absent");
                Call(form, "GoNext", null, EventArgs.Empty);
                Assert(Interaction.GetSetting(scratchApp, section, "DeleteToRecycleBin", "absent") == deleteBefore,
                    "Walking through the delete question wrote a value");
                form.Close();
            }
            using (var form = Open(viewer))
            {
                Call(form, "ShowStep", 3);
                var deleteCheck = Children(form).OfType<CheckBox>().Single(c => c.Visible);
                deleteCheck.Checked = false;
                Call(form, "GoNext", null, EventArgs.Empty);
                Assert(Interaction.GetSetting(scratchApp, section, "DeleteToRecycleBin", "") == "0", "Next did not flush the delete choice");
                Assert((bool)Call(viewer, "PersistSettings"), "Close-path settings save failed");
                Assert(Interaction.GetSetting(scratchApp, section, "DeleteToRecycleBin", "") == "0", "Close clobbered the delete choice");
                form.Close();
            }
            // Spec 215 section 7: the finish screen's installed-servers block, from facts the scene supplies (the host's
            // seam WelcomeServerFactsOverride: share installed, share up, exchange installed, exchange up; null = could not tell).
            Console.WriteLine("Checking the installed-servers block on the finish screen (spec 215 section 7)");
            string pillRunning = Localization.T("Работает");
            string pillStopped = Localization.T("Не запущен - менеджер включит его по вашему выбору");
            string actAdd = Localization.T("Добавить ресурс");
            string actOpen = Localization.T("Открыть менеджер");
            var serverCases = new (string Name, bool?[] Facts, (bool Visible, string Word, string Button)[] Rows)[]
            {
                ("both-green", new bool?[] { true, true, true, true },
                    new[] { (true, pillRunning, actAdd), (true, pillRunning, actAdd) }),
                ("one-amber", new bool?[] { true, true, true, false },
                    new[] { (true, pillRunning, actAdd), (true, pillStopped, actOpen) }),
                ("unknown-is-amber", new bool?[] { true, null, true, null },
                    new[] { (true, pillStopped, actOpen), (true, pillStopped, actOpen) }),
                ("share-only", new bool?[] { true, true, false, null },
                    new[] { (true, pillRunning, actAdd), (false, (string)null, (string)null) }),
                ("exchange-only", new bool?[] { false, null, true, false },
                    new[] { (false, (string)null, (string)null), (true, pillStopped, actOpen) }),
                ("none", new bool?[] { false, null, false, null },
                    new[] { (false, (string)null, (string)null), (false, (string)null, (string)null) }),
            };
            int serverScenes = 0;
            try
            {
                foreach (string themeName in new[] { "Light", "Dark" })
                {
                    App.GetType("fmsl.ThemeService", true).GetMethod("SetMode", Static)
                        .Invoke(null, new[] { Enum.Parse(App.GetType("fmsl.ThemeMode", true), themeName) });
                    foreach (var serverCase in serverCases)
                    {
                        Set(viewer, "WelcomeServerFactsOverride", serverCase.Facts);
                        foreach (string code in new[] { "en", "ar" })
                        {
                            string label = $"{serverCase.Name}/{themeName}/{code}";
                            using var form = Open(viewer);
                            Call(form, "PreviewLanguage", code);
                            Call(form, "ShowStep", (int)Field(form, "finishIndex"));
                            Application.DoEvents();
                            form.Refresh();

                            var block = (Control)Field(form, "serversBlock");
                            bool expectBlock = serverCase.Rows.Any(row => row.Visible);
                            Assert(block.Visible == expectBlock,
                                $"[{label}] block visible={block.Visible}, expected {expectBlock}");
                            Assert(form.RightToLeft == (code == "ar" ? RightToLeft.Yes : RightToLeft.No),
                                $"[{label}] reading direction is wrong");

                            var views = ((System.Collections.IEnumerable)Field(form, "serverRows")).Cast<object>().ToList();
                            Assert(views.Count == 2, $"[{label}] expected two prepared rows, found {views.Count}");
                            for (int index = 0; index < views.Count; index++)
                            {
                                var expected = serverCase.Rows[index];
                                var container = (Control)Field(views[index], "Container");
                                Assert(container.Visible == expected.Visible,
                                    $"[{label}] row {index} visible={container.Visible}, expected {expected.Visible}");
                                if (!expected.Visible) continue;

                                var word = (Label)Field(views[index], "Word");
                                var dot = (Label)Field(views[index], "Dot");
                                var button = (Button)Field(views[index], "ActButton");
                                // The word is the meaning and the dot only decoration (section 5): text compared in
                                // English, structure and reach compared in every language.
                                if (code == "en")
                                {
                                    Assert(word.Text == expected.Word, $"[{label}] row {index} pill word '{word.Text}', expected '{expected.Word}'");
                                    Assert(button.Text == expected.Button, $"[{label}] row {index} button '{button.Text}', expected '{expected.Button}'");
                                }
                                else
                                {
                                    Assert(!string.IsNullOrWhiteSpace(word.Text) && !string.IsNullOrWhiteSpace(button.Text),
                                        $"[{label}] row {index} has an empty pill word or button");
                                }
                                Assert(button.Visible && button.Enabled && button.TabStop && button.CanSelect,
                                    $"[{label}] row {index} button is not keyboard reachable");
                                Assert((double)App.GetType("fmsl.Utils", true).GetMethod("ContrastRatio", Static).Invoke(null,
                                    new object[] { dot.ForeColor, form.BackColor }) >= 4.5,
                                    $"[{label}] row {index} pill dot does not meet 4.5:1 on the window");
                            }
                            CheckTargets(form);
                            Capture(form, evidence, $"servers-{serverCase.Name}-{themeName}-{code}");
                            form.Close();
                            serverScenes++;
                        }
                    }
                }
            }
            finally
            {
                Set(viewer, "WelcomeServerFactsOverride", null);
            }
            Console.WriteLine($"  {serverScenes} server-block scenes at {viewer.DeviceDpi} DPI (both themes, left-to-right and right-to-left)");

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
            Console.WriteLine("Checking the invitation button on the empty surface (spec 211 section 4.3)");
            Call(viewer, "BuildModernLayout");
            viewer.Show();
            Application.DoEvents();
            try
            {
                var media = (Control)Field(viewer, "panel_Media");
                Assert(media != null, "The media surface does not exist after BuildModernLayout");
                // The designer's children start visible; an empty surface is the state the invitation answers to.
                foreach (Control child in media.Controls) child.Visible = false;
                // The launch-time eligibility (CaptureWelcomeStartState) is supplied directly: the scenes never run the lifecycle.
                Set(viewer, "welcome_Invite_Eligible", true);
                Call(viewer, "UpdateWelcomeInvite");
                var invite = (Control)Field(viewer, "welcome_Invite_Button");
                Assert(invite != null, "The invitation button was never built");
                Assert(invite.Visible, "An eligible profile shows no invitation button");
                Assert(invite.Parent == media, "The invitation does not live on the media surface");
                Assert(invite.Height >= viewer.LogicalToDeviceUnits(44), "The invitation is not a 44 px target");
                Assert(!string.IsNullOrEmpty(invite.Text), "The invitation has no caption");
                Assert(invite.TabStop && invite.CanSelect, "The invitation is not keyboard reachable");
                // The real first-run placeholder is visible behind the invitation.
                var placeholder = (Control)Field(viewer, "lbl_Help_Info");
                placeholder.Visible = true;
                Assert(invite.Visible && media.Controls.GetChildIndex(invite) < media.Controls.GetChildIndex(placeholder),
                    "The first-run placeholder hides the invitation");
                placeholder.Visible = false;
                Call(viewer, "ShowEmptyState");
                using (var bitmap = new Bitmap(media.Width, media.Height))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(media.BackColor);
                    Call(viewer, "PaintEmptyState", media, new PaintEventArgs(graphics, media.ClientRectangle));
                    bool painted = false;
                    for (int y = 0; y < bitmap.Height && !painted; y++)
                        for (int x = 0; x < bitmap.Width && !painted; x++)
                            painted = bitmap.GetPixel(x, y).ToArgb() != media.BackColor.ToArgb();
                    Assert(painted, "The invitation suppresses the empty-state explanation");
                }
                var originalSize = media.Size;
                media.Size = new Size(420, 260);
                Assert(media.ClientRectangle.Contains(invite.Bounds), "Resizing clipped the invitation");
                Assert(Math.Abs(invite.Left * 2 + invite.Width - media.ClientSize.Width) <= 1,
                    "Resizing did not recenter the invitation");
                media.Size = originalSize;
                foreach (string code in new[] { "ar", "ur", "en" })
                {
                    Call(viewer, "SetUiLanguage", code);
                    Assert(invite.Text == Localization.T("Первоначальная настройка"), "Language change left a stale invitation caption");
                    Assert(invite.RightToLeft == (code == "en" ? RightToLeft.No : RightToLeft.Yes), "Invitation reading direction is wrong");
                    if (code != "en") Assert(invite.Font.FontFamily.Name == Localization.FontFamily(), "Invitation lacks its script font");
                    Capture(viewer, evidence, $"invite-{code}");
                }
                Assert(((System.Collections.ICollection)Field(viewer, "script_Fonts")).Count <= 4,
                    "Invitation language changes grew the viewer's font cache without bound");
                // A visible child takes the surface: the picture is never covered (spec 211 section 4.3).
                var pictureBox = (Control)Field(viewer, "Picture_Box_1");
                pictureBox.Visible = true;
                Assert(!invite.Visible, "The invitation covered the picture");
                pictureBox.Visible = false;
                Assert(invite.Visible, "The invitation did not return when the surface emptied");
                using (var overlay = new Panel())
                {
                    media.Controls.Add(overlay);
                    Assert(!invite.Visible, "A newly added visible surface did not hide the invitation");
                    media.Controls.Remove(overlay);
                    Assert(invite.Visible, "Removing a surface did not restore the invitation");
                }
                // An ineligible profile - an upgrade, values and no note - is never offered it.
                Set(viewer, "welcome_Invite_Eligible", false);
                Call(viewer, "UpdateWelcomeInvite");
                Assert(!invite.Visible, "An ineligible profile still shows the invitation");
                Set(viewer, "welcome_Invite_Eligible", true);
                Call(viewer, "UpdateWelcomeInvite");
                Assert(invite.Visible, "The invitation did not come back for an eligible profile");
                // Any exit of the wizard retires it: WelcomeRecordExit clears the eligibility itself.
                bool openedByClick = false;
                using (var closeWizard = new System.Windows.Forms.Timer { Interval = 50 })
                {
                    closeWizard.Tick += (_, _) =>
                    {
                        var form = Application.OpenForms.Cast<Form>().FirstOrDefault(f => Wizard.IsInstanceOfType(f));
                        if (form == null) return;
                        openedByClick = true;
                        closeWizard.Stop();
                        form.Close();
                    };
                    closeWizard.Start();
                    ((Button)invite).PerformClick();
                }
                Assert(openedByClick, "Clicking the invitation did not open the wizard");
                Call(viewer, "UpdateWelcomeInvite");
                Assert(!invite.Visible, "The wizard's exit did not retire the invitation");
            }
            finally
            {
                viewer.Hide();
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
            Console.WriteLine($"WelcomeWizardScenes: PASS ({rendered} language/theme scenes; isolated persistence, the W-4 delete question, the installed-servers block ({serverScenes} scenes), the invitation button, skip, MSIX, Enter/Escape and F1; evidence: {evidence})");
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
