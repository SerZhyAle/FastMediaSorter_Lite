using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

// Specification L0361 section 6.4 and rule S9 (ticket L0365): the scene matrix of the form scale model on the real
// Table_Form, in process. "--font-matrix" visits every page at the monitor the window opened on, then drags the
// window across the monitors in the order 100 -> 175 -> 225 -> 100 % (each stop a real WM_DPICHANGED), visits every
// page again at every stop and runs the window's own font audit (Table_Form.AuditSettingsFonts: each control's
// effective font against its recipe at the current dpi). The interface scale U is set per process with
// "--interface-scale <percent>"; the Windows Text size T is read, never changed - a run names the T it measured.
// A stop whose dpi no monitor offers is named as omitted, never counted as measured.
static partial class Program
{
    static bool FontMatrixMode;
    static int InterfaceScalePercent;

    static readonly int[] DragSequence = { 96, 168, 216, 96 };

    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point pt, uint flags);

    static int DpiOf(Screen screen)
    {
        var centre = new Point(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2);
        var monitor = MonitorFromPoint(centre, 2);
        return GetDpiForMonitor(monitor, 0, out uint x, out _) == 0 ? (int)x : 0;
    }

    static double TextFactor()
    {
        var sizing = App.GetType("fmsl.UiSizing", false);
        var method = sizing?.GetMethod("CurrentTextFactor", Static, null, Type.EmptyTypes, null);
        return method == null ? double.NaN : Convert.ToDouble(method.Invoke(null, null));
    }

    // Applied before the window is built: the window reads the interface scale once, at construction.
    static void ApplyInterfaceScale(object viewer)
    {
        if (InterfaceScalePercent <= 0) return;
        var preferences = viewer.GetType().GetMethod("GetModernPreferences", Instance).Invoke(viewer, null);
        preferences.GetType().GetProperty("InterfaceScalePercent", Instance).SetValue(preferences, 0);
        preferences.GetType().GetProperty("InterfaceScaleRelativePercent", Instance).SetValue(preferences, InterfaceScalePercent);
    }

    static string[] Audit() => (string[])Window.GetType().GetMethod("AuditSettingsFonts", Instance).Invoke(Window, null);
    static int RecipeCount() => (int)Window.GetType().GetMethod("SettingsFontRecipeCount", Instance).Invoke(Window, null);

    static async Task<(object record, int mismatches)> Stop(string name)
    {
        for (int i = 0; i < Tabs.TabCount; i++)
        {
            Tabs.SelectedIndex = i;
            await IdleAsync();
        }
        // The window's own font follows the platform AFTER the DpiChanged event (L0361 fact E3): one more idle.
        await IdleAsync();
        var mismatches = Audit();
        Console.WriteLine($"  {name}: dpi {Window.DeviceDpi}, {RecipeCount()} recorded fonts, {mismatches.Length} mismatch(es)");
        foreach (var line in mismatches.Take(20)) Console.WriteLine("    " + line);
        return (new { stop = name, dpi = Window.DeviceDpi, recipes = RecipeCount(), mismatches }, mismatches.Length);
    }

    static async Task RunFontMatrix(string output)
    {
        var stops = new List<object>();
        var omitted = new List<string>();
        int measured = 0, failed = 0;
        try
        {
            var screens = Screen.AllScreens.Select(s => (screen: s, dpi: DpiOf(s))).ToArray();
            Console.WriteLine("  monitors: " + string.Join(", ", screens.Select(s => $"{s.dpi} dpi at {s.screen.Bounds.Left},{s.screen.Bounds.Top}")));
            async Task Measure(string name)
            {
                var (record, mismatches) = await Stop(name);
                stops.Add(record);
                measured++;
                if (mismatches > 0) failed++;
            }
            await Measure("open");
            int index = 0;
            foreach (int dpi in DragSequence)
            {
                index++;
                var target = screens.FirstOrDefault(s => s.dpi == dpi);
                if (target.screen == null)
                {
                    omitted.Add($"drag {index}: no monitor at {dpi} dpi ({Math.Round(dpi / 0.96)} %)");
                    continue;
                }
                var area = target.screen.WorkingArea;
                Window.Location = new Point(area.Left + 20, area.Top + 20);
                await IdleAsync();
                if (Window.DeviceDpi != dpi)
                {
                    omitted.Add($"drag {index}: the window stayed at {Window.DeviceDpi} dpi on the {dpi} dpi monitor");
                    continue;
                }
                await Measure($"drag{index}:{dpi}");
            }
            if (RecipeCount() == 0) omitted.Add("the window recorded no font recipe - the audit inspected nothing");
        }
        catch (Exception ex)
        {
            Console.WriteLine("SettingsLayoutScenes: font matrix failed: " + ex);
            omitted.Add("exception: " + ex.GetType().Name);
            measured = 0;
        }
        finally
        {
            int recipes = 0;
            try { recipes = RecipeCount(); } catch { }
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                scratch = Scratch,
                interfaceScalePercent = InterfaceScalePercent <= 0 ? 100 : InterfaceScalePercent,
                textFactor = TextFactor(),
                systemDpi = new Label().DeviceDpi,
                stops,
                omitted,
            }));
            // CHECK-VERDICT: 0 every measured stop clean, 1 a mismatch, 2 nothing measured or no recipe.
            Environment.ExitCode = failed > 0 ? 1 : (measured == 0 || recipes == 0 ? 2 : 0);
            Console.WriteLine($"SettingsLayoutScenes font matrix: {measured} stop(s) measured, {failed} with mismatches, {omitted.Count} omitted");
            Application.ExitThread();
        }
    }
}
