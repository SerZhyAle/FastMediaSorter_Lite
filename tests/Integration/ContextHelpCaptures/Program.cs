using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using Manager = FastMediaSorterCompanion;

// Opt-in real-window captures. No Start, Connect, Sign in, clipboard or service command is invoked.
static class Program
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly string[] Locales = { "en", "ru", "uk", "de", "it", "es", "fr", "pt", "ar", "hi", "bn", "ur", "zh" };
    [DllImport("user32.dll")] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] static extern bool SwitchDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window, IntPtr context, uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    static void Capture(Form form, string path)
    {
        Exception failure = null;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.ShowInTaskbar = false;
        form.Shown += async (_, _) =>
        {
            try
            {
                form.Activate();
                await Task.Delay(250); // Allow native layout/paint, not a synchronization substitute in a test.
                if (!Screen.FromControl(form).Bounds.Contains(form.Bounds))
                    throw new InvalidOperationException("The window does not fit on the capture display: " + path);
                using var image = new Bitmap(form.Width, form.Height);
                using (var graphics = Graphics.FromImage(image))
                {
                    GetWindowThreadProcessId(form.Handle, out var owner);
                    if (owner != Environment.ProcessId) throw new InvalidOperationException("Capture window belongs to another process.");
                    var context = graphics.GetHdc();
                    try
                    {
                        if (!PrintWindow(form.Handle, context, 2)) throw new InvalidOperationException("PrintWindow failed: " + path);
                    }
                    finally { graphics.ReleaseHdc(context); }
                }
                image.Save(path, ImageFormat.Png);
            }
            catch (Exception ex) { failure = ex; }
            finally { form.Close(); }
        };
        Application.Run(form);
        if (failure != null) throw failure;
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Pass an output directory."); return 2; }
        var desktop = OpenInputDesktop(0, false, 0x0100);
        try
        {
            if (desktop == IntPtr.Zero || !SwitchDesktop(desktop))
            { Console.Error.WriteLine("An unlocked interactive desktop is required."); return 2; }
        }
        finally { if (desktop != IntPtr.Zero) CloseDesktop(desktop); }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetDefaultFont(new Font("Segoe UI", 9));
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        var profile = "ContextHelpCaptures_" + Guid.NewGuid().ToString("N");
        var settings = typeof(Manager.CompanionGlobals).GetProperty("SettingsAppNameOverride", Static);
        var priorSettings = settings.GetValue(null);
        var viewerApp = typeof(fmsl.Main_Form).Assembly.GetType("fmsl.Common_Module").GetField("App_name", Static);
        var priorViewerApp = viewerApp.GetValue(null);
        var priorManagerLanguage = Manager.Localization.CurrentCode;
        var priorViewerLanguage = fmsl.Localization.CurrentCode;
        var evidence = new List<object>();
        try
        {
            settings.SetValue(null, profile);
            viewerApp.SetValue(null, profile);
            Manager.ButtonGlyphs.AssetOpener = Manager.CompanionAssets.OpenBundledAsset;
            fmsl.ButtonGlyphs.AssetOpener = name => typeof(fmsl.Main_Form).Assembly.GetManifestResourceStream("FmsPayload" + name);
            foreach (var locale in Locales)
            foreach (var dark in new[] { false, true })
            {
                Manager.Localization.CurrentCode = locale;
                fmsl.Localization.CurrentCode = locale;
                Manager.ThemeService.SetMode(dark ? Manager.ThemeMode.Dark : Manager.ThemeMode.Light);
                fmsl.ThemeService.SetMode(dark ? fmsl.ThemeMode.Dark : fmsl.ThemeMode.Light);
                var folder = Path.Combine(output, locale);
                Directory.CreateDirectory(folder);
                foreach (var topic in new[] { "live-broadcast", "share-live-broadcast", "share-settings-drive" })
                {
                    using Form form = topic switch
                    {
                        "share-live-broadcast" => new Manager.Live_Broadcast_Form(),
                        "share-settings-drive" => new Manager.Share_Settings_Form(null, Manager.SettingsDestination.Manager),
                        _ => (Form)Activator.CreateInstance(typeof(fmsl.Main_Form).Assembly.GetType("fmsl.LiveBroadcast_Form"), new object[] { false })
                    };
                    if (topic == "live-broadcast") fmsl.ThemeService.Attach(form);
                    else Manager.ThemeService.Attach(form);
                    if (topic == "share-settings-drive")
                        form.GetType().GetMethod("SelectSettingsPage", Instance).Invoke(form, new object[] { "drive" });
                    var path = Path.Combine(folder, $"{topic}-{(dark ? "dark" : "light")}.png");
                    Capture(form, path);
                    evidence.Add(new { locale, theme = dark ? "dark" : "light", topic,
                        path = Path.GetRelativePath(output, path).Replace('\\', '/'),
                        sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
                        width = form.Width, height = form.Height, dpi = form.DeviceDpi,
                        os = Environment.OSVersion.VersionString, architecture = RuntimeInformation.OSArchitecture.ToString(),
                        capturedUtc = DateTime.UtcNow, process = Environment.ProcessId,
                        method = "Shown WinForms window; PrintWindow by process-owned HWND", isolatedProfile = profile });
                }
            }
            File.WriteAllText(Path.Combine(output, "context-help-capture-evidence.json"),
                JsonSerializer.Serialize(new { schema = 1, screenshots = evidence }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Captured {evidence.Count} real windows in 13 locales, light/dark.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            settings.SetValue(null, priorSettings);
            viewerApp.SetValue(null, priorViewerApp);
            Manager.Localization.CurrentCode = priorManagerLanguage;
            fmsl.Localization.CurrentCode = priorViewerLanguage;
            using var registry = Registry.CurrentUser.OpenSubKey(@"Software\VB and VBA Program Settings", true);
            registry?.DeleteSubKeyTree(profile, false);
        }
    }
}
