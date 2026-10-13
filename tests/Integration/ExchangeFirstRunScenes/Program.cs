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

// Scripted ports only. The developer gate and VB registry hive are process-local test scope.
static class Program
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Field(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    static void Step(Form form, int step) => form.GetType().GetMethod("ShowStep", Private).Invoke(form, new object[] { step });
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static IEnumerable<Control> Children(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Children(child)) yield return nested;
        }
    }
    sealed class Ports : IExchangeFirstRunActions
    {
        public ExchangeSnapshot Snapshot;
        public int Writes;
        public Task<ExchangeSnapshot> ReadAsync() => Task.FromResult(Snapshot);
        public Task<ExchangeProcess.ListenOutcome> SetListenAsync(string value) { Writes++; throw new Exception("Unexpected write"); }
        public Task<ExchangeProcess.ConfigOutcome> SetRangeAsync(string value) { Writes++; throw new Exception("Unexpected write"); }
        public Task<ExchangeProcess.ConfigOutcome> SetEndpointAsync(string value) { Writes++; throw new Exception("Unexpected write"); }
        public Task<ExchangeElevation.ElevatedOutcome> FirewallAsync() { Writes++; throw new Exception("Unexpected elevation"); }
        public Task<ServiceControl.ManageResult> ServiceAsync() { Writes++; throw new Exception("Unexpected elevation"); }
        public ExchangeWizardResult Account(Form owner, string login, bool typed, bool enroll) { Writes++; throw new Exception("Unexpected account"); }
        public string Suggest(int port) => "192.168.1.10:" + port;
        public void ConnectionString(Form owner) { Writes++; throw new Exception("Unexpected dialog"); }
    }
    static void Check(Form form)
    {
        Assert(Screen.FromControl(form).WorkingArea.Contains(form.Bounds), $"Outside working area: {form.Bounds} in {Screen.FromControl(form).WorkingArea}");
        foreach (var target in Children(form).Where(c => c.Visible && (c is ButtonBase || c is NumericUpDown)))
        {
            Assert(target.Height >= form.LogicalToDeviceUnits(44), $"Small target: {target.Text} ({target.Height})");
            Assert(target.Parent.ClientRectangle.Contains(target.Bounds), $"Clipped target: {target.Text}");
            if (target is Button button) Assert(button.Image != null, $"Missing glyph: {button.Text}");
        }
        foreach (var text in Children(form).OfType<Label>().Where(c => c.Visible))
            Assert(text.Height >= text.GetPreferredSize(new Size(text.Width, 0)).Height, $"Clipped text: {text.Text}");
        foreach (var text in (IEnumerable<Label>)Field(form, "_accentLabels"))
            if (text.Visible) Assert(AccentColors.Contrast(text.ForeColor, text.BackColor) >= 4.5, $"Unreadable heading: {text.Text}");
        Assert(!Children(form).Any(c => c.Text.Contains("TransientPassword9")), "Secret in frame");
    }
    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetDefaultFont(new Font("Segoe UI", 9f));
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        string priorGate = Environment.GetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName);
        string scratch = "FmsExchangeFirstRunScenes_" + Guid.NewGuid().ToString("N");
        string priorApp = CompanionGlobals.SettingsAppNameOverride;
        string evidence = Path.GetFullPath(args.Length == 0 ? "temp/welcome-213-scenes" : args[0]);
        Directory.CreateDirectory(evidence);
        Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, "1");
        CompanionGlobals.SettingsAppNameOverride = scratch;
        ButtonGlyphs.AssetOpener = CompanionAssets.OpenBundledAsset;
        int captures = 0;
        try
        {
            foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
            foreach (string code in Localization.Codes)
            foreach (bool server in new[] { false, true })
            {
                ThemeService.SetMode(mode);
                Localization.CurrentCode = code;
                var ports = new Ports { Snapshot = new ExchangeSnapshot { CanRegisterService = server, CanChangeFirewall = true,
                    Report = new ExchangeProcess.InspectReport { Answered = true, Listen = ":22022", PortRange = "40000-40100" } } };
                using var form = new Server_Welcome_Form(WelcomeHalf.Exchange, ports, ports.Snapshot);
                ThemeService.Attach(form);
                form.Show();
                Application.DoEvents();
                Assert(form.RightToLeft == (code is "ar" or "ur" ? RightToLeft.Yes : RightToLeft.No), "RTL mismatch");
                Assert(!((CheckBox)Field(form, "_firewall")).Checked, "Firewall preselected");
                if (server) Assert(!((CheckBox)Field(form, "_service")).Checked, "Service preselected");
                Assert(((TextBox)Field(form, "_statement")).Text == string.Join(Environment.NewLine + Environment.NewLine, ExchangeStatement.Paragraphs()), "Incomplete statement");
                int count = server ? 7 : 6;
                for (int step = 0; step < count; step++)
                {
                    Step(form, step);
                    Application.DoEvents();
                    Check(form);
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(evidence, $"{mode}-{code}-{(server ? "Server" : "Desktop")}-{step}.png"), ImageFormat.Png);
                    captures++;
                }
                Assert(ports.Writes == 0, "Rendering performed acts");
                Console.WriteLine($"{mode} {code} {(server ? "Server" : "Desktop")}: {count} steps, DPI={form.DeviceDpi}");
                form.Close();
                Assert(Interaction.GetSetting(scratch, CompanionGlobals.Second_App_Name, ServerWelcomeModel.DismissExchangeValue, "") != "", "Close did not dismiss");
            }
            Console.WriteLine($"ExchangeFirstRunScenes: PASS ({captures} captures; scripted ports; no server, account or elevation)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Console.Error.WriteLine("ExchangeFirstRunScenes: FAIL");
            return 1;
        }
        finally
        {
            CompanionGlobals.SettingsAppNameOverride = priorApp;
            Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, priorGate);
            try { Interaction.DeleteSetting(scratch); } catch { }
        }
    }
}
