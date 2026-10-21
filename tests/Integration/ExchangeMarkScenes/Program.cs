using System.Drawing.Imaging;
using System.Reflection;
using Microsoft.VisualBasic;
using Manager = FastMediaSorterCompanion;

// Offline render walk: do not Show a window, run OnShown probes, click a command or start a server.
static class Program
{
    const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly string[] Dialogs = {
        "Exchange_Server_Wizard_Form", "Exchange_Account_Form", "Exchange_Account_Admin_Form",
        "Exchange_Account_Limits_Form", "Exchange_Password_Form", "Exchange_Password_Change_Form",
        "Exchange_Connection_String_Form", "Exchange_Invitation_Create_Form", "Exchange_Invitation_Created_Form",
        "Exchange_Shared_Invitation_Form", "Exchange_Link_Form", "Exchange_Local_Account_Picker_Form",
        "Exchange_Admin_Browser_Form", "Exchange_Admin_Audit_Form", "Connection_Diagnosis_Dialog"
    };
    static IEnumerable<Control> Children(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Children(child)) yield return nested;
        }
    }
    static object Argument(ParameterInfo parameter)
    {
        if (parameter.HasDefaultValue) return parameter.DefaultValue;
        var type = parameter.ParameterType;
        if (type == typeof(string)) return parameter.Name == "url" ? "https://example.invalid/" : "example";
        if (type == typeof(byte[])) return null;
        if (type == typeof(IReadOnlyList<string>)) return new[] { "alice", "bob" };
        if (type.IsEnum || type.IsValueType) return Activator.CreateInstance(type);
        if (type.Name == "ExchangeDeviceAgent" || type.Name == "ConnectionDiagnosisResult") return null;
        return type.GetConstructor(Type.EmptyTypes) == null ? null : Activator.CreateInstance(type);
    }
    static Form Make(Assembly assembly, string name)
    {
        var type = assembly.GetTypes().Single(t => t.Name == name);
        var ctor = type.GetConstructors(Instance).OrderByDescending(c => c.GetParameters().Length).First();
        return (Form)ctor.Invoke(ctor.GetParameters().Select(Argument).ToArray());
    }
    static void Capture(Form form, string path, bool requiresMark)
    {
        // DrawToBitmap skips invisible descendants. Set the managed visibility bit without
        // SetVisibleCore/Show, so OnShown cannot start a live probe or onboarding action.
        var setState = typeof(Control).GetMethod("SetState", Instance);
        var visibleState = Enum.Parse(setState.GetParameters()[0].ParameterType, "Visible");
        setState.Invoke(form, new[] { visibleState, (object)true });
        form.CreateControl();
        // The real load-time reflow/clamp, without invoking OnShown (which owns live reads).
        form.GetType().GetMethod("OnLoad", Instance).Invoke(form, new object[] { EventArgs.Empty });
        form.PerformLayout();
        var marks = Children(form).Where(c => c.Name == "exchangeMark" && c.Visible).ToArray();
        if (requiresMark && marks.Length == 0) throw new Exception("Missing mark: " + path);
        foreach (var mark in marks)
        {
            if (string.IsNullOrWhiteSpace(mark.AccessibleName)) throw new Exception("Missing accessible name: " + path);
            if (!mark.Parent.ClientRectangle.Contains(mark.Bounds)) throw new Exception("Clipped mark: " + path);
        }
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
        image.Save(path, ImageFormat.Png);
    }
    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetDefaultFont(new Font("Segoe UI", 9));
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "temp/exchange-mark-248");
        Directory.CreateDirectory(output);
        var scratch = "ExchangeMarkScenes_" + Guid.NewGuid().ToString("N");
        // The override is a Friend test seam; reflection avoids widening the production API for this scene.
        var settings = typeof(Manager.CompanionGlobals).GetProperty("SettingsAppNameOverride", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var prior = settings.GetValue(null);
        var exchangeProcess = typeof(Manager.MainWindow).Assembly.GetType("FastMediaSorterCompanion.ExchangeProcess");
        var componentOverride = exchangeProcess.GetField("ExeDirectoryOverride", BindingFlags.Static | BindingFlags.NonPublic);
        var priorComponent = componentOverride.GetValue(null);
        var component = Path.Combine(output, "component");
        Directory.CreateDirectory(component);
        File.WriteAllBytes(Path.Combine(component, "fms-exchange-server.exe"), Array.Empty<byte>());
        var language = Manager.Localization.CurrentCode;
        int captures = 0;
        try
        {
            settings.SetValue(null, scratch);
            componentOverride.SetValue(null, component);
            Manager.ButtonGlyphs.AssetOpener = Manager.CompanionAssets.OpenBundledAsset;
            fmsl.ButtonGlyphs.AssetOpener = name => typeof(fmsl.Main_Form).Assembly.GetManifestResourceStream("FmsPayload" + name);
            foreach (var mode in new[] { Manager.ThemeMode.Light, Manager.ThemeMode.Dark })
            foreach (var code in new[] { "en", "ar" })
            {
                Manager.Localization.CurrentCode = code;
                Manager.ThemeService.SetMode(mode);
                foreach (var name in Dialogs)
                {
                    using var form = Make(typeof(Manager.MainWindow).Assembly, name);
                    Manager.ThemeService.Attach(form);
                    Capture(form, Path.Combine(output, $"manager-{name}-{code}-{mode}.png"), true);
                    captures++;
                }
                // Ticket 250: the "exchange" page belongs to the local exchange-server window, "exchange-account" to the manager's.
                foreach (var (page, destination) in new[] { ("exchange", Manager.SettingsDestination.ExchangeServer), ("exchange-account", Manager.SettingsDestination.Manager) })
                {
                    using var form = new Manager.Share_Settings_Form(null, destination);
                    Manager.ThemeService.Attach(form);
                    // Selection before OnShown changes presentation only; the deferred loader is dormant.
                    form.GetType().GetMethod("SelectSettingsPage", Instance).Invoke(form, new object[] { page });
                    Capture(form, Path.Combine(output, $"manager-settings-{page}-{code}-{mode}.png"), true);
                    captures++;
                }
                fmsl.Localization.CurrentCode = code;
                fmsl.ThemeService.SetMode(mode == Manager.ThemeMode.Light ? fmsl.ThemeMode.Light : fmsl.ThemeMode.Dark);
                foreach (var name in new[] { "Exchange_Connect_Form", "Exchange_Password_Change_Form", "Connection_Diagnosis_Dialog" })
                {
                    using var form = Make(typeof(fmsl.Main_Form).Assembly, name);
                    form.RightToLeft = code == "ar" ? RightToLeft.Yes : RightToLeft.No;
                    fmsl.ThemeService.Attach(form);
                    Capture(form, Path.Combine(output, $"viewer-{name}-{code}-{mode}.png"), true);
                    captures++;
                }
            }
            Console.WriteLine($"ExchangeMarkScenes: PASS ({captures} offline captures, EN/AR, light/dark; no live probes)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Console.WriteLine("ExchangeMarkScenes: FAIL"); return 1; }
        finally
        {
            settings.SetValue(null, prior);
            componentOverride.SetValue(null, priorComponent);
            Manager.Localization.CurrentCode = language;
            try { Interaction.DeleteSetting(scratch); } catch { }
        }
    }
}
