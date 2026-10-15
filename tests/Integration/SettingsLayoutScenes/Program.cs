using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic;
using fmsl;

// WINDOWS-UI sections 5 and 9 (0.2), measured on the real Table_Form of the modern viewer, in process:
//   * how many layout passes a page switch and a group toggle cost (Layout events over the whole tree), and
//   * what the page looks like afterwards (the bounds of every control of the shown page, the scroll range of
//     its flow, and a hash of its rendered pixels), so a faster page that lays out differently can be caught
//     by comparing two runs: "compare <before.json> <after.json>".
// The window is shown off screen and never closed (closing would write the window rectangle), and the program's
// settings hive is a scratch one: the owner's registry values are neither read nor written.
static class Program
{
    // The viewer assembly is stamped with the build minute (AutoVersion), so a probe built today cannot name
    // the viewer of an hour ago. Taking whichever FastMediaSorter_LITE.dll sits beside the probe lets one
    // probe measure an older build of the viewer - which is how a before/after comparison is made.
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void ResolveViewerByName()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
        {
            if (!new AssemblyName(e.Name).Name.Equals("FastMediaSorter_LITE", StringComparison.OrdinalIgnoreCase)) return null;
            return Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "FastMediaSorter_LITE.dll"));
        };
    }

    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly App = typeof(Main_Form).Assembly;
    static readonly Type Common = App.GetType("fmsl.Common_Module", true);

    sealed class Counter
    {
        public int Layouts;
        public int Resizes;
        public readonly Dictionary<string, int> ByProperty = new Dictionary<string, int>();
    }

    static readonly Dictionary<Control, Counter> Counters = new Dictionary<Control, Counter>();
    static readonly List<object> Steps = new List<object>();
    // FMS_LAYOUT_ORIGINS=1: which method of the viewer asked for each layout pass (the first frame of the
    // fmsl namespace on the stack). A diagnostic aid for reading the counts, off by default (it is slow).
    static readonly bool Origins = Environment.GetEnvironmentVariable("FMS_LAYOUT_ORIGINS") == "1";
    static readonly bool Trace = Environment.GetEnvironmentVariable("FMS_LAYOUT_TRACE") == "1";
    static readonly SortedDictionary<string, int> OriginCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
    static Form Window;
    static TabControl Tabs;
    static string Scratch;
    static double ColdConstructMs, ColdPrepareMs, ColdOpenMs;

    static object Member(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Instance | BindingFlags.DeclaredOnly);
            if (field != null) return field.GetValue(target);
            var property = type.GetProperty(name, Instance | BindingFlags.DeclaredOnly);
            if (property != null) return property.GetValue(target);
        }
        throw new Exception("Missing member " + name);
    }

    static IEnumerable<Control> Tree(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
            foreach (var nested in Tree(child)) yield return nested;
    }

    static void Hook(Control root)
    {
        foreach (var control in Tree(root))
        {
            if (Counters.ContainsKey(control)) continue;
            var counter = new Counter();
            Counters[control] = counter;
            control.Layout += (s, e) =>
            {
                counter.Layouts++;
                string key = e.AffectedProperty ?? "(none)";
                counter.ByProperty[key] = counter.ByProperty.TryGetValue(key, out int n) ? n + 1 : 1;
                if (Origins)
                {
                    string origin = "(framework)";
                    foreach (var frame in new StackTrace(1, false).GetFrames())
                    {
                        var method = frame.GetMethod();
                        string type = method?.DeclaringType?.FullName ?? "";
                        if (type.StartsWith("fmsl.") && !type.StartsWith("fmsl.My"))
                        {
                            origin = type.Replace("fmsl.", "") + "." + method.Name;
                            break;
                        }
                    }
                    OriginCounts[origin + " [" + key + "]"] = OriginCounts.TryGetValue(origin + " [" + key + "]", out int o) ? o + 1 : 1;
                }
            };
            control.Resize += (s, e) => counter.Resizes++;
            // FMS_LAYOUT_TRACE=1: the page flows' own layout passes, in order, with the scroll range and the
            // heights of its groups at that moment - the way to read a scroll range that came out stale.
            if (Trace && control is FlowLayoutPanel flow && flow.Parent is TabPage)
            {
                flow.Layout += (s, e) =>
                {
                    var heights = flow.Controls.Cast<Control>().Select(c => c.Height).ToArray();
                    Console.WriteLine($"      [layout {flow.Parent.Name} {e.AffectedProperty} by {(e.AffectedControl == null ? "-" : e.AffectedControl.Name)}] vMax={flow.VerticalScroll.Maximum} vVis={flow.VerticalScroll.Visible} clientW={flow.ClientSize.Width} heights={string.Join(",", heights)}");
                };
            }
        }
    }

    static void ResetCounters()
    {
        foreach (var counter in Counters.Values) { counter.Layouts = 0; counter.Resizes = 0; counter.ByProperty.Clear(); }
        OriginCounts.Clear();
    }

    static Task IdleAsync()
    {
        var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler handler = null;
        handler = (s, e) => { Application.Idle -= handler; source.TrySetResult(true); };
        Application.Idle += handler;
        return source.Task;
    }

    static string PathOf(Control control, Control root)
    {
        var parts = new Stack<string>();
        for (var c = control; c != null && c != root; c = c.Parent)
        {
            int index = c.Parent == null ? 0 : c.Parent.Controls.IndexOf(c);
            parts.Push((string.IsNullOrEmpty(c.Name) ? c.GetType().Name : c.Name) + "[" + index + "]");
        }
        return string.Join("/", parts);
    }

    static string PixelHash(Control control)
    {
        if (control.Width <= 0 || control.Height <= 0) return "empty";
        using (var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb))
        {
            control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[Math.Abs(data.Stride) * data.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                return Convert.ToHexString(SHA256.HashData(bytes)).Substring(0, 16);
            }
            finally { bitmap.UnlockBits(data); }
        }
    }

    static Dictionary<string, object> Snapshot(TabPage page)
    {
        var bounds = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var control in Tree(page))
        {
            if (control == page) continue;
            var r = control.Bounds;
            bounds[PathOf(control, page)] = new[] { r.X, r.Y, r.Width, r.Height, control.Visible ? 1 : 0 };
        }
        var flow = page.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
        var metrics = new Dictionary<string, int>();
        if (flow != null)
        {
            metrics["clientW"] = flow.ClientSize.Width;
            metrics["clientH"] = flow.ClientSize.Height;
            metrics["minW"] = flow.AutoScrollMinSize.Width;
            metrics["minH"] = flow.AutoScrollMinSize.Height;
            metrics["vMax"] = flow.VerticalScroll.Maximum;
            metrics["vLarge"] = flow.VerticalScroll.LargeChange;
            metrics["vVisible"] = flow.VerticalScroll.Visible ? 1 : 0;
            metrics["hVisible"] = flow.HorizontalScroll.Visible ? 1 : 0;
            metrics["hMax"] = flow.HorizontalScroll.Maximum;
            metrics["scrollY"] = -flow.AutoScrollPosition.Y;
        }
        metrics["pageW"] = page.Width;
        metrics["pageH"] = page.Height;
        var title = (Label)Member(Window, "modernSettingsTitle");
        var subtitle = (Label)Member(Window, "modernSettingsSubtitle");
        return new Dictionary<string, object>
        {
            ["bounds"] = bounds,
            ["metrics"] = metrics,
            ["title"] = title.Text,
            ["subtitle"] = subtitle.Text,
            ["titleBounds"] = new[] { title.Left, title.Top, title.Width, title.Height },
            ["subtitleBounds"] = new[] { subtitle.Left, subtitle.Top, subtitle.Width, subtitle.Height },
            ["pixels"] = PixelHash(page),
            ["windowPixels"] = PixelHash(Window),
        };
    }

    static object CountsOf()
    {
        int layouts = 0, resizes = 0, containers = 0, maxOne = 0;
        var byProperty = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var counter in Counters.Values)
        {
            layouts += counter.Layouts;
            resizes += counter.Resizes;
            if (counter.Layouts > 0) containers++;
            maxOne = Math.Max(maxOne, counter.Layouts);
            foreach (var pair in counter.ByProperty)
                byProperty[pair.Key] = (byProperty.TryGetValue(pair.Key, out int n) ? n : 0) + pair.Value;
        }
        var hottest = Counters.OrderByDescending(p => p.Value.Layouts).Take(3)
            .Select(p => (string.IsNullOrEmpty(p.Key.Name) ? p.Key.GetType().Name : p.Key.Name) + "=" + p.Value.Layouts).ToArray();
        return new Dictionary<string, object>
        {
            ["layouts"] = layouts, ["resizes"] = resizes, ["containersLaidOut"] = containers,
            ["maxOnOneControl"] = maxOne, ["hottest"] = hottest, ["byProperty"] = byProperty,
            ["origins"] = OriginCounts.OrderByDescending(p => p.Value).Take(8).Select(p => p.Key + "=" + p.Value).ToArray(),
        };
    }

    static async Task Record(string step, Action action, bool snapshotPage = true)
    {
        ResetCounters();
        if (Trace) Console.WriteLine("  step " + step);
        var sw = Stopwatch.StartNew();
        action();
        await IdleAsync();
        double ms = sw.Elapsed.TotalMilliseconds;
        var page = Tabs.SelectedTab;
        Steps.Add(new Dictionary<string, object>
        {
            ["step"] = step,
            ["page"] = page.Name,
            ["ms"] = Math.Round(ms, 1),
            ["counts"] = CountsOf(),
            ["snapshot"] = snapshotPage ? Snapshot(page) : null,
        });
    }

    static IEnumerable<(string id, object group)> GroupsOf(TabPage page)
    {
        var groups = (System.Collections.IDictionary)Member(Window, "settingsGroups");
        var flow = page.Controls.OfType<FlowLayoutPanel>().First();
        foreach (System.Collections.DictionaryEntry entry in groups)
        {
            var control = (Control)entry.Value;
            if (control.Parent == flow) yield return ((string)entry.Key, entry.Value);
        }
    }

    static async Task Run(string output)
    {
        try
        {
            int pages = Tabs.TabCount;
            // Two passes over every page: the first visit and the repeat visit of WINDOWS-UI section 5.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < pages; i++)
                {
                    int index = i;
                    await Record("page:" + (pass == 0 ? "first:" : "repeat:") + i, () => Tabs.SelectedIndex = index);
                }
            // Group toggles on every page: collapse, then expand again (the remembered context ends where it began).
            for (int i = 0; i < pages; i++)
            {
                int index = i;
                await Record("select:" + i, () => Tabs.SelectedIndex = index, snapshotPage: false);
                foreach (var (id, group) in GroupsOf(Tabs.TabPages[i]).ToArray())
                {
                    var header = (Button)Member(group, "Header");
                    await Record("toggle:" + id + ":1", () => header.PerformClick());
                    await Record("toggle:" + id + ":2", () => header.PerformClick());
                }
            }
            // Expand all / collapse all on one page with groups.
            for (int i = 0; i < pages; i++)
            {
                int index = i;
                var groups = GroupsOf(Tabs.TabPages[i]).ToArray();
                if (groups.Length < 2) continue;
                await Record("select:" + i, () => Tabs.SelectedIndex = index, snapshotPage: false);
                var collapse = (Button)Member(Window, "collapseSettingsButton");
                var expand = (Button)Member(Window, "expandSettingsButton");
                await Record("collapseAll:" + i, () => collapse.PerformClick());
                await Record("expandAll:" + i, () => expand.PerformClick());
                break;
            }
            // The window is shown again (F2 after a close): the viewer calls PrepareForDisplay on every opening
            // of the same instance, which is what a warm "dialog appears" is made of.
            for (int again = 1; again <= 2; again++)
                await Record("reopen:" + again, () => ((Table_Form)Window).PrepareForDisplay());
            // The window is resized: every page must be what it would have been - the shown one at once, the
            // others when they are shown (the pages that are not on screen are not re-measured by a page
            // switch, so this is where that claim is held to the page's own layout).
            var original = Window.ClientSize;
            await Record("resize:grow", () => Window.ClientSize = new Size(original.Width + 140, original.Height + 90));
            for (int i = 0; i < pages; i++)
            {
                int index = i;
                await Record("afterGrow:" + i, () => Tabs.SelectedIndex = index);
            }
            await Record("resize:shrink", () => Window.ClientSize = original);
            for (int i = pages - 1; i >= 0; i--)
            {
                int index = i;
                await Record("afterShrink:" + i, () => Tabs.SelectedIndex = index);
            }
            File.WriteAllText(output, JsonSerializer.Serialize(new { scratch = Scratch, dpi = Window.DeviceDpi, pages, coldConstructMs = Math.Round(ColdConstructMs, 1), coldPrepareMs = Math.Round(ColdPrepareMs, 1), coldOpenMs = Math.Round(ColdOpenMs, 1), steps = Steps },
                new JsonSerializerOptions { WriteIndented = false }));
        }
        catch (Exception ex)
        {
            Console.WriteLine("SettingsLayoutScenes: scene failed: " + ex);
            Environment.ExitCode = 2;
        }
        finally
        {
            Application.ExitThread();
        }
    }

    static int Compare(string beforePath, string afterPath)
    {
        using var before = JsonDocument.Parse(File.ReadAllText(beforePath));
        using var after = JsonDocument.Parse(File.ReadAllText(afterPath));
        var a = before.RootElement.GetProperty("steps").EnumerateArray().ToArray();
        var b = after.RootElement.GetProperty("steps").EnumerateArray().ToArray();
        int differences = 0, compared = 0;
        if (a.Length != b.Length) { Console.WriteLine($"step count differs: {a.Length} vs {b.Length}"); differences++; }
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            string name = a[i].GetProperty("step").GetString();
            if (name != b[i].GetProperty("step").GetString()) { Console.WriteLine($"step {i} differs: {name}"); differences++; continue; }
            var sa = a[i].GetProperty("snapshot"); var sb = b[i].GetProperty("snapshot");
            if (sa.ValueKind == JsonValueKind.Null) continue;
            compared++;
            int before_ = differences;
            foreach (var key in new[] { "title", "subtitle", "pixels", "windowPixels" })
                if (sa.GetProperty(key).GetString() != sb.GetProperty(key).GetString())
                { Console.WriteLine($"{name}: {key} differs ({sa.GetProperty(key).GetString()} -> {sb.GetProperty(key).GetString()})"); differences++; }
            foreach (var key in new[] { "titleBounds", "subtitleBounds" })
                if (sa.GetProperty(key).GetRawText() != sb.GetProperty(key).GetRawText())
                { Console.WriteLine($"{name}: {key} differs"); differences++; }
            var ma = sa.GetProperty("metrics"); var mb = sb.GetProperty("metrics");
            // The range of a scrollbar that is not shown cannot be seen or scrolled: it keeps whatever the page
            // last needed, so it is compared only when the bar is on screen in both runs.
            bool barShown = ma.GetProperty("vVisible").GetInt32() == 1 || mb.GetProperty("vVisible").GetInt32() == 1;
            foreach (var p in ma.EnumerateObject())
                if (!barShown && (p.Name == "vMax" || p.Name == "vLarge" || p.Name == "hMax")) continue;
                else if (!mb.TryGetProperty(p.Name, out var q) || q.GetInt32() != p.Value.GetInt32())
                { Console.WriteLine($"{name}: metric {p.Name} {p.Value} -> {(mb.TryGetProperty(p.Name, out var r) ? r.ToString() : "missing")}"); differences++; }
            var ba = sa.GetProperty("bounds"); var bb = sb.GetProperty("bounds");
            int boundsDiffs = 0;
            foreach (var p in ba.EnumerateObject())
            {
                if (!bb.TryGetProperty(p.Name, out var q) || q.GetRawText() != p.Value.GetRawText())
                {
                    if (boundsDiffs++ < 5) Console.WriteLine($"{name}: bounds {p.Name} {p.Value} -> {(bb.TryGetProperty(p.Name, out var r) ? r.ToString() : "missing")}");
                    differences++;
                }
            }
            foreach (var p in bb.EnumerateObject())
                if (!ba.TryGetProperty(p.Name, out _)) { Console.WriteLine($"{name}: new control {p.Name}"); differences++; }
            if (boundsDiffs > 5) Console.WriteLine($"{name}: .. and {boundsDiffs - 5} more bounds");
            if (differences == before_) { }
        }
        Console.WriteLine($"SettingsLayoutScenes compare: {(differences == 0 ? "IDENTICAL" : "DIFFERENT")} ({compared} snapshots compared, {differences} differences)");
        return differences == 0 ? 0 : 1;
    }

    static void Summarise(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Console.WriteLine($"dpi {doc.RootElement.GetProperty("dpi")}, {doc.RootElement.GetProperty("pages")} pages; cold open {doc.RootElement.GetProperty("coldOpenMs")} ms (construct {doc.RootElement.GetProperty("coldConstructMs")} + PrepareForDisplay {doc.RootElement.GetProperty("coldPrepareMs")} + first idle)");
        Console.WriteLine("step                              ms   layouts  resizes  containers  max/one  hottest");
        foreach (var step in doc.RootElement.GetProperty("steps").EnumerateArray())
        {
            var counts = step.GetProperty("counts");
            Console.WriteLine($"{step.GetProperty("step").GetString(),-30} {step.GetProperty("ms").GetDouble(),7:0.0} {counts.GetProperty("layouts").GetInt32(),8} {counts.GetProperty("resizes").GetInt32(),8} {counts.GetProperty("containersLaidOut").GetInt32(),10} {counts.GetProperty("maxOnOneControl").GetInt32(),8}  {string.Join(", ", counts.GetProperty("hottest").EnumerateArray().Select(h => h.GetString()))}");
            if (Origins)
                foreach (var origin in counts.GetProperty("origins").EnumerateArray())
                    Console.WriteLine("      origin " + origin.GetString());
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "compare") return Compare(args[1], args[2]);
        if (args.Length == 2 && args[0] == "summary") { Summarise(args[1]); return 0; }
        // --unaware: the process is DPI-unaware, so the window is built at 96 DPI whatever the monitors are
        // ("100 %" of WINDOWS-UI section 9). Without it the window is per-monitor aware and sits on the PRIMARY
        // monitor, so a comparison between two runs is made at one scale (the output names it).
        bool unaware = args.Contains("--unaware");
        string output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "settings-layout-scenes.json");
        Application.SetHighDpiMode(unaware ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.SetDefaultFont(new Font("Microsoft Sans Serif", 8.25f));
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        // A scratch settings hive: reads give the code defaults, writes (a group toggle marks the context dirty)
        // land in a section nobody owns, removed at the end.
        Scratch = "FmsSettingsLayoutScenes_" + Guid.NewGuid().ToString("N");
        Common.GetField("App_name", Static).SetValue(null, Scratch);
        try
        {
            Console.WriteLine("SettingsLayoutScenes: scratch hive " + Scratch + ", window off screen, output " + output);
            var sw = Stopwatch.StartNew();
            var openClock = Stopwatch.StartNew();
            Window = new Table_Form();
            double constructMs = sw.Elapsed.TotalMilliseconds;
            Window.StartPosition = FormStartPosition.Manual;
            Window.ShowInTaskbar = false;
            Window.Opacity = 0;
            var primary = Screen.PrimaryScreen.WorkingArea;
            Window.Location = unaware ? new Point(-5000, -5000) : new Point(primary.Left + 20, primary.Top + 20);
            sw.Restart();
            ((Table_Form)Window).PrepareForDisplay();
            double prepareMs = sw.Elapsed.TotalMilliseconds;
            ColdConstructMs = constructMs;
            ColdPrepareMs = prepareMs;
            Tabs = (TabControl)Member(Window, "Tab_Control");
            Hook(Window);
            Window.Shown += async (s, e) =>
            {
                Console.WriteLine($"  construct {constructMs:0} ms, PrepareForDisplay {prepareMs:0} ms");
                await IdleAsync();
                ColdOpenMs = openClock.Elapsed.TotalMilliseconds;
                Hook(Window);
                await Run(output);
            };
            Application.Run(Window);
            if (Environment.ExitCode == 0) Summarise(output);
            Window.Dispose();
        }
        finally
        {
            try { Interaction.DeleteSetting(Scratch); } catch { }
        }
        return Environment.ExitCode;
    }
}
