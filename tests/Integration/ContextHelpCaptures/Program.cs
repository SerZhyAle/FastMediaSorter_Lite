using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using Manager = FastMediaSorterCompanion;

// Opt-in real-window captures. No Start, Connect, Sign in, clipboard, camera, listener or service command is invoked.
static partial class Program
{
    internal const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    internal const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string Method = "Shown WinForms window; PrintWindow by process-owned HWND";
    static readonly string[] Locales = { "en", "ru", "uk", "de", "it", "es", "fr", "pt", "ar", "hi", "bn", "ur", "zh" };
    [DllImport("user32.dll")] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] static extern bool SwitchDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window, IntPtr context, uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    static void Capture(Form form, string path, Func<Form, Task> settle)
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
                if (settle != null) await settle(form);
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
            // Dispose, not Close: Close raises FormClosing, where an editor asks about unsaved edits or a
            // password and blocks the run on the owner's screen. Destroying the handle ends Application.Run.
            finally { form.Dispose(); }
        };
        Application.Run(form);
        if (failure != null) throw failure;
    }

    // Viewer forms and their sample data are mostly Friend; the viewer exe grants no internals, so they are built by reflection.
    internal static Type ViewerType(string name) =>
        typeof(fmsl.Main_Form).Assembly.GetType(name.Contains('.') ? name : "fmsl." + name, true);

    internal static object ViewerNew(string name, params object[] args) =>
        Activator.CreateInstance(ViewerType(name), Instance, null, args, null);

    internal static Form ViewerForm(string name, params object[] args) => (Form)ViewerNew(name, args);

    [STAThread]
    static int Main(string[] args)
    {
        var options = new Dictionary<string, string[]>();
        for (var i = 1; i + 1 < args.Length; i += 2)
            options[args[i]] = args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (args.Length % 2 != 1 || options.Keys.Any(k => k is not ("--topics" or "--locales")))
        { Console.Error.WriteLine("Pass an output directory, optionally followed by --topics a,b,c and --locales en,ru."); return 2; }
        // A locale subset is an iteration aid; it writes no evidence, so it can never stand in for the full set.
        var locales = options.TryGetValue("--locales", out var someLocales) ? Locales.Intersect(someLocales).ToArray() : Locales;
        var desktop = OpenInputDesktop(0, false, 0x0100);
        try
        {
            if (desktop == IntPtr.Zero || !SwitchDesktop(desktop))
            { Console.Error.WriteLine("An unlocked interactive desktop is required."); return 2; }
        }
        finally { if (desktop != IntPtr.Zero) CloseDesktop(desktop); }
        var topics = Topics.All();
        if (options.TryGetValue("--topics", out var wanted))
        {
            var unknown = wanted.Where(w => topics.All(t => t.Name != w)).ToArray();
            if (unknown.Length > 0) { Console.Error.WriteLine("Unknown topics: " + string.Join(", ", unknown)); return 2; }
            topics = topics.Where(t => wanted.Contains(t.Name)).ToList();
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetDefaultFont(new Font("Segoe UI", 9));
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        var profile = "ContextHelpCaptures_" + Guid.NewGuid().ToString("N");
        var settings = typeof(Manager.CompanionGlobals).GetProperty("SettingsAppNameOverride", Static);
        var priorSettings = settings.GetValue(null);
        var viewerApp = ViewerType("Common_Module").GetField("App_name", Static);
        var priorViewerApp = viewerApp.GetValue(null);
        var priorManagerLanguage = Manager.Localization.CurrentCode;
        var priorViewerLanguage = fmsl.Localization.CurrentCode;
        var evidence = new List<object>();
        var failures = new List<string>();
        try
        {
            settings.SetValue(null, profile);
            viewerApp.SetValue(null, profile);
            Manager.ButtonGlyphs.AssetOpener = Manager.CompanionAssets.OpenBundledAsset;
            fmsl.ButtonGlyphs.AssetOpener = name => typeof(fmsl.Main_Form).Assembly.GetManifestResourceStream("FmsPayload" + name);
            foreach (var locale in locales)
            foreach (var dark in new[] { false, true })
            {
                Manager.Localization.CurrentCode = locale;
                fmsl.Localization.CurrentCode = locale;
                Manager.ThemeService.SetMode(dark ? Manager.ThemeMode.Dark : Manager.ThemeMode.Light);
                fmsl.ThemeService.SetMode(dark ? fmsl.ThemeMode.Dark : fmsl.ThemeMode.Light);
                var folder = Path.Combine(output, locale);
                Directory.CreateDirectory(folder);
                foreach (var topic in topics)
                {
                    var path = Path.Combine(folder, $"{topic.Name}-{(dark ? "dark" : "light")}.png");
                    try
                    {
                        using var form = topic.Create();
                        if (topic.Viewer) fmsl.ThemeService.Attach(form);
                        else Manager.ThemeService.Attach(form);
                        topic.Prepare?.Invoke(form);
                        Capture(form, path, topic.Settle);
                        evidence.Add(new { locale, theme = dark ? "dark" : "light", topic = topic.Name,
                            path = Path.GetRelativePath(output, path).Replace('\\', '/'),
                            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
                            width = form.Width, height = form.Height, dpi = form.DeviceDpi,
                            os = Environment.OSVersion.VersionString, architecture = RuntimeInformation.OSArchitecture.ToString(),
                            capturedUtc = DateTime.UtcNow, process = Environment.ProcessId,
                            method = Method, isolatedProfile = profile, state = topic.State });
                    }
                    catch (Exception ex)
                    {
                        var inner = ex is TargetInvocationException { InnerException: { } cause } ? cause : ex;
                        failures.Add($"{locale}/{topic.Name}-{(dark ? "dark" : "light")}: {inner.GetType().Name}: {inner.Message}");
                    }
                }
            }
            if (locales.Length == Locales.Length) WriteEvidence(output, evidence, topics.Select(t => t.Name).ToHashSet());
            else Console.WriteLine("Locale subset: no evidence written.");
            Console.WriteLine($"Captured {evidence.Count} real windows ({topics.Count} topics) in {locales.Length} locales, light/dark.");
            foreach (var failure in failures.Distinct().Take(60)) Console.Error.WriteLine("FAILED " + failure);
            return failures.Count == 0 ? 0 : 1;
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

    // A partial run replaces only the records of the topics it captured; the others keep their own proof.
    static void WriteEvidence(string output, List<object> captured, HashSet<string> recaptured)
    {
        var file = Path.Combine(output, "context-help-capture-evidence.json");
        var kept = new JsonArray();
        JsonNode declared = new JsonArray();
        if (File.Exists(file))
        {
            var prior = JsonNode.Parse(File.ReadAllText(file))!;
            foreach (var record in prior["screenshots"]!.AsArray())
                if (!recaptured.Contains((string)record!["topic"]!)) kept.Add(record.DeepClone());
            // The hand-kept list of topics this harness does not capture (gaps, other capture tools) is never rewritten here.
            if (prior["declared"] is { } list) declared = list.DeepClone();
        }
        foreach (var record in captured) kept.Add(JsonSerializer.SerializeToNode(record));
        var ordered = new JsonArray(kept.OrderBy(r => (string)r!["path"]!, StringComparer.Ordinal).Select(r => r!.DeepClone()).ToArray());
        var document = new JsonObject { ["schema"] = 1, ["declared"] = declared, ["screenshots"] = ordered };
        File.WriteAllText(file, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>One help topic captured from its real window. State says which sample state the picture shows.</summary>
sealed record Topic(string Name, bool Viewer, Func<Form> Create, string State,
    Action<Form> Prepare = null, Func<Form, Task> Settle = null);

static partial class Topics
{
    internal static List<Topic> All() =>
        Core().Concat(ViewerA()).Concat(ViewerB()).Concat(ManagerA()).Concat(ManagerB()).Concat(Exchange()).ToList();

    static IEnumerable<Topic> Core() => new[]
    {
        new Topic("live-broadcast", true, () => Program.ViewerForm("LiveBroadcast_Form", false),
            "receiver with nothing pasted; Connect not pressed"),
        new Topic("share-live-broadcast", false, () => new Manager.Live_Broadcast_Form(),
            "producer before Start; no capture source opened"),
        new Topic("share-settings-drive", false, () => new Manager.Share_Settings_Form(null, Manager.SettingsDestination.Manager),
            "Manager settings, Google Drive page, not signed in",
            form => ((Manager.Share_Settings_Form)form).SelectSettingsPage("drive")),
    };
}
