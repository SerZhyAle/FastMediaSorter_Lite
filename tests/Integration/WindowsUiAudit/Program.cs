using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
class Probe
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static string Output = Path.Combine(Environment.CurrentDirectory, "stage", "windows-ui-audit");
    static IEnumerable<Control> Tree(Control c) { yield return c; foreach (Control x in c.Controls) foreach (var y in Tree(x)) yield return y; }
    static object Value(Type t, string name, int depth = 0)
    {
        if (t == typeof(string)) return name.Contains("Path", StringComparison.OrdinalIgnoreCase) ? Path.Combine(Output, "sample.png") : "UI audit";
        if (t == typeof(bool)) return false; if (t == typeof(int)) return name == "cancelIndex" || name == "defaultIndex" ? 1 : name == "port" ? 22 : name == "clientSide" ? 100 : 0;
        if (t.IsEnum) return Enum.ToObject(t, 0); if (t.IsValueType) return Activator.CreateInstance(t);
        if (typeof(Image).IsAssignableFrom(t)) return new Bitmap(48, 48);
        if (t.Name.StartsWith("Func") || t.Name.StartsWith("Action") || t.Name == "IResourceSecretStore") return null;
        if (t.IsGenericType) { var g = t.GetGenericArguments(); if (t.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return Activator.CreateInstance(t); if (g.Length == 1) { var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(g[0])); if (g[0].Name == "AskButton") { list.Add(Value(g[0], "", depth + 1)); list.Add(Value(g[0], "", depth + 1)); } return list; } }
        if (depth > 5 || t.IsInterface) return null;
        var ctor = t.GetConstructors(Flags).OrderBy(c => c.GetParameters().Length).FirstOrDefault(); if (ctor == null) return null;
        return ctor.Invoke(ctor.GetParameters().Select(p => Value(p.ParameterType, p.Name, depth + 1)).ToArray());
    }
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.SetDefaultFont(new Font("Microsoft Sans Serif", 8.25f)); Application.EnableVisualStyles();
        Directory.CreateDirectory(Output); using (var img = new Bitmap(64, 64)) img.Save(Path.Combine(Output, "sample.png"));
        var records = new List<object>(); int forms = 0, checks = 0; var issues = new List<string>();
        foreach (var assembly in new[] { typeof(fmsl.Table_Form).Assembly, typeof(FastMediaSorterCompanion.Share_Settings_Form).Assembly })
        {
            var service = assembly.GetTypes().Single(t => t.Name == "ThemeService"); var palettes = assembly.GetTypes().Single(t => t.Name == "ThemePalette");
            var field = service.GetField("_palette", BindingFlags.Static | BindingFlags.NonPublic);
            var attach = service.GetMethod("Attach"); var orig = field.GetValue(null); service.GetField("_originalRenderer", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, ToolStripManager.Renderer);
            foreach (var type in assembly.GetTypes().Where(t => typeof(Form).IsAssignableFrom(t) && !t.IsAbstract).OrderBy(t => t.Name))
            {
                Form form = null;
                try
                {
                    form = (Form)Value(type, ""); if (form == null) throw new Exception("No factory");
                    if (form is fmsl.Table_Form settings) settings.PrepareForDisplay();
                    form.CreateControl(); form.PerformAutoScale(); form.PerformLayout();
                    var widgets = Tree(form).ToArray(); int cycles = 0;
                    if (form is fmsl.Main_Form)
                    {
                        foreach (var pair in new[] { ("image_Menu", "BuildImageMenu"), ("video_Menu", "BuildVideoMenu") })
                        {
                            var menu = new ContextMenuStrip(); type.GetField(pair.Item1, Flags).SetValue(form, menu);
                            type.GetMethod(pair.Item2, Flags).Invoke(form, null);
                            if (menu.Items.Count == 0) issues.Add(type.Name + " empty " + pair.Item1);
                        }
                    }
                    foreach (var mode in new[] { "Dark", "Light", "Dark", "Light", "Dark" })
                    {
                        var palette = palettes.GetField(mode).GetValue(null); field.SetValue(null, palette); service.GetMethod("ApplyRenderer", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null); attach.Invoke(null, new[] { (object)form }); form.PerformLayout();
                        foreach (var num in Tree(form).OfType<NumericUpDown>())
                        {
                            var buttons = num.Controls.Cast<Control>().Single(c => c is not TextBox); if (buttons.Width < Math.Round(28.0 * num.DeviceDpi / 96) - 1) issues.Add(type.Name + " numeric small spinner " + buttons.Width);
                            checks++;
                        }
                        if (new[] { "Resource_Edit_Form", "Resource_Credential_Form", "Secure_Password_Form", "Share_Settings_Form", "Share_Root_Params_Form", "Ask_Dialog", "Video_Replace_Confirm_Form" }.Contains(type.Name))
                        {
                            form.StartPosition = FormStartPosition.Manual; form.ShowInTaskbar = false; form.Opacity = 0; form.Location = new Point(-5000, -5000);
                            if (!form.Visible) { form.Show(); form.Location = new Point(-5000, -5000); }
                            Application.DoEvents();
                            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(Output, type.Name + "-" + mode + ".png")); }
                        }
                        cycles++;
                    }
                    records.Add(new { window = type.FullName, status = "constructed; 5 theme applications", dpi = form.DeviceDpi, controls = widgets.Length, checkboxes = widgets.OfType<CheckBox>().Count(), numeric = widgets.OfType<NumericUpDown>().Count(), buttons = widgets.OfType<Button>().Count(), menus = widgets.OfType<ToolStrip>().Count(), cycles });
                    forms++;
                }
                catch (Exception ex) { issues.Add(type.FullName + ": " + (ex.InnerException ?? ex).ToString()); records.Add(new { window = type.FullName, status = "not verified", reason = (ex.InnerException ?? ex).Message }); }
                finally { form?.Dispose(); field.SetValue(null, orig); }
            }
        }
        File.WriteAllText(Path.Combine(Output, "surface-inventory.json"), JsonSerializer.Serialize(new { scope = "All reflected Form types in viewer and Companion; construction, layout and five palette applications. No actions or worker launch. Seven safe dialog types additionally shown off-screen for render evidence at the current DPI. Main viewer/server hosts are constructed only. Physical multi-DPI, high contrast and spoken accessibility remain unverified.", forms, numericChecks = checks, issues, windows = records }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"WindowsUiAudit: {(issues.Count == 0 ? "PASS" : "FAIL")} (construction/theme scope: {forms} windows, {checks} numeric checks; {issues.Count} issues)"); Environment.ExitCode = issues.Count == 0 ? 0 : 1;
        foreach (var issue in issues) Console.WriteLine(issue);
    }
}
