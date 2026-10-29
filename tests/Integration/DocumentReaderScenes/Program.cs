using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic;
using fmsl;

// Real controls/native renderer, a private profile and an owned fixture copy.
// The shell's startup/shutdown handlers are excluded so the probe never opens
// the owner's folders, performs a file command or changes the running viewer.
sealed class SceneViewer : Main_Form
{
    public event EventHandler SceneShown;
    protected override void OnLoad(EventArgs e) { }
    protected override void OnShown(EventArgs e) { SceneShown?.Invoke(this, e); }
    protected override void OnFormClosing(FormClosingEventArgs e) { }
    protected override void OnFormClosed(FormClosedEventArgs e) { }
}

static class Program
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type Viewer = typeof(Main_Form);
    static readonly Type Common = Viewer.Assembly.GetType("fmsl.Common_Module", true);
    static object Get(object target, string name)
    {
        var field = Viewer.GetField(name, Instance);
        return field != null ? field.GetValue(target) : Viewer.GetProperty(name, Instance).GetValue(target);
    }
    static object Call(object target, string name, params object[] args) => Viewer.GetMethod(name, Instance).Invoke(target, args);
    static void Set(object target, string name, object value) => Viewer.GetField(name, Instance).SetValue(target, value);
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static async Task Until(Func<bool> condition, string message)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.ElapsedMilliseconds > 30000) throw new TimeoutException(message);
            await Task.Delay(25);
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--epub") return EpubReaderScenes.Run(args[1]);
        if (args.Length < 2) { Console.WriteLine("Usage: DocumentReaderScenes <fixture.pdf> <output-directory>"); return 2; }
        string input = Path.GetFullPath(args[0]);
        string output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        string scratch = "FmsDocumentReaderScenes_" + Guid.NewGuid().ToString("N");
        Common.GetField("App_name", Static).SetValue(null, scratch);
        var logger = Viewer.Assembly.GetType("fmsl.AppFileLogger", true);
        logger.GetField("LogDirectoryOverride", Static).SetValue(null, Path.Combine(output, "logs"));
        logger.GetMethod("Initialize", Static).Invoke(null, null);
        string fixture = Path.Combine(Path.GetTempPath(), scratch + ".pdf");
        File.Copy(input, fixture);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetDefaultFont(new Font("Microsoft Sans Serif", 8.25f));
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        int verdict = 0;
        var observations = new System.Collections.Generic.List<object>();
        using var window = new SceneViewer { Text = "Document reader verification", StartPosition = FormStartPosition.Manual, Location = new Point(30, 30), Size = new Size(1200, 800) };
        try
        {
            Console.WriteLine("DocumentReaderScenes: constructing isolated viewer");
            Call(window, "BuildModernLayout");
            var preferences = (ModernViewerPreferences)Call(window, "GetModernPreferences");
            preferences.FullPdfReader = true;
            Common.GetField("Current_File_Name", Static).SetValue(null, fixture);
            Set(window, "is_PictureBox1_Visible", true);
            Set(window, "is_PictureBox2_Visible", false);
            var picture = (PictureBox)Get(window, "Picture_Box_1");
            picture.Visible = true;
            Action run = async () =>
            {
                try
                {
                    Console.WriteLine("DocumentReaderScenes: opening owned fixture");
                    Call(window, "LoadPdfDocument", fixture);
                    await Until(() => (bool)Call(window, "FullPdfReaderActive") && Get(window, "pdf_Reader_TextPage") != null && picture.Image != null, "First usable PDF content");
                    foreach (var scenario in new[] { ("light", "en", "single"), ("dark", "en", "continuous"), ("dark", "ar", "spread") })
                    {
                        Localization.CurrentCode = scenario.Item2;
                        Call(window, "LocalizePdfReader");
                        ThemeService.SetMode(scenario.Item1 == "dark" ? ThemeMode.Dark : ThemeMode.Light);
                        var previous = ModernViewerPreferences.FromJson(preferences.ExportJson(true));
                        preferences.PdfLayout = scenario.Item3;
                        Call(window, "ApplyDocumentReaderPreferences", previous);
                        Call(window, "ApplyPdfReaderTheme", ThemeService.Palette);
                        await Task.Delay(600);
                        var toolbar = (ToolStrip)Get(window, "pdf_Reader_Toolbar");
                        var media = (Panel)Get(window, "panel_Media");
                        Console.WriteLine($"DocumentReaderScenes: {scenario.Item3}, toolbar={toolbar.Bounds}, parent={media.Name}, index={media.Controls.GetChildIndex(toolbar)}, top={media.GetChildAtPoint(new Point(10, 10))?.Name}");
                        Check(media.GetChildAtPoint(new Point(10, 10)) == toolbar, "Reader toolbar is occluded");
                        Check(toolbar.Right <= media.ClientSize.Width, "Reader toolbar overflow escaped the viewport");
                        using var bitmap = new Bitmap(window.Width, window.Height);
                        using (var graphics = Graphics.FromImage(bitmap))
                        {
                            var dc = graphics.GetHdc();
                            try { Check(PrintWindow(window.Handle, dc, 2), "Native window capture failed"); }
                            finally { graphics.ReleaseHdc(dc); }
                        }
                        string path = Path.Combine(output, $"pdf-{scenario.Item1}-{scenario.Item2}-{scenario.Item3}.png");
                        bitmap.Save(path, ImageFormat.Png);
                        observations.Add(new { scenario = scenario.Item3, theme = scenario.Item1, locale = scenario.Item2, dpi = window.DeviceDpi, screenshot = Path.GetFileName(path) });
                    }
                    Call(window, "TogglePdfReaderPane", 1);
                    var query = (TextBox)Get(window, "pdf_Reader_Query");
                    query.Focus();
                    query.Text = "B12N";
                    Check(query.Focused, "Search field did not take focus");
                    foreach (Keys key in new[] { Keys.B, Keys.N, Keys.D1, Keys.Delete })
                    {
                        Check((bool)Call(window, "PdfReaderOwnsInput", new KeyEventArgs(key)), "Document input escaped: " + key);
                        Check(File.Exists(fixture), "Fixture was changed by a document key");
                    }
                    Call(window, "PdfReaderOwnsInput", new KeyEventArgs(Keys.Escape));
                    Call(window, "PdfReaderOwnsInput", new KeyEventArgs(Keys.Escape));
                    Check(!(bool)Get(window, "pdf_Reader_Interaction"), "Escape did not return to canvas");
                    using (var answerPrompt = new System.Windows.Forms.Timer { Interval = 50 })
                    {
                        answerPrompt.Tick += (_, _) =>
                        {
                            var prompt = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.GetType().Name == "Ask_Dialog");
                            if (prompt == null) return;
                            var buttons = (System.Collections.IEnumerable)prompt.GetType().GetField("answerButtons", Instance).GetValue(prompt);
                            buttons.Cast<Button>().First().PerformClick();
                        };
                        answerPrompt.Start();
                        Call(window, "AddPdfReaderBookmark");
                    }
                    Check(preferences.ReadDocumentBookmarks().Count == 1, "Bookmark was not saved");
                    var previousReader = ModernViewerPreferences.FromJson(preferences.ExportJson(true));
                    preferences.FullPdfReader = false;
                    Call(window, "ApplyDocumentReaderPreferences", previousReader);
                    await Until(() => (bool)Call(window, "IsPdfDocumentShown"), "Disabled baseline preview");
                    Check(!(bool)Call(window, "FullPdfReaderActive"), "Reader remained active after disabling");
                    Check(Get(window, "pdf_Reader_Toolbar") == null, "Disabled reader retained its toolbar");
                    Check(preferences.ReadDocumentBookmarks().Count == 1, "Disabling erased bookmarks");
                    preferences.DocumentTtsEnabled = true;
                    preferences.Save();
                    var loaded = ModernViewerPreferences.Load();
                    Check(!loaded.FullPdfReader && !loaded.FullEpubReader && loaded.DocumentTtsEnabled && loaded.RememberDocumentPosition && loaded.EpubUseBookStyles, "Document booleans did not survive registry save/load");
                    Console.WriteLine("DocumentReaderScenes: PASS (three captures; document input ownership; Escape; bookmark; independent PDF disable)");
                }
                catch (Exception error)
                {
                    verdict = 1;
                    Console.WriteLine("DocumentReaderScenes: FAIL " + error);
                    Console.WriteLine($"Reader state: source={Get(window, "pdf_Current_Source") != null}, toolbar={Get(window, "pdf_Reader_Toolbar") != null}, text={Get(window, "pdf_Reader_TextPage") != null}, image={picture.Image != null}, error={Get(window, "pdf_Page_Error")}, status={((Label)Get(window, "lbl_Status")).Text}");
                    try
                    {
                        using var logStream = new FileStream((string)logger.GetProperty("LogFilePath", Static).GetValue(null), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var logReader = new StreamReader(logStream);
                        Console.WriteLine(string.Join(Environment.NewLine, logReader.ReadToEnd().Split('\n').TakeLast(18)));
                    }
                    catch { }
                }
                finally
                {
                    Call(window, "ReleasePdfDocument");
                    window.Close();
                }
            };
            window.Show();
            window.BeginInvoke(run);
            Application.Run(window);
            File.WriteAllText(Path.Combine(output, "scenes.json"), JsonSerializer.Serialize(new { verdict, os = Environment.OSVersion.ToString(), build = Viewer.Assembly.GetName().Version.ToString(), observations }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) { Console.WriteLine("DocumentReaderScenes: FAIL " + error); verdict = 1; }
        finally
        {
            File.Delete(fixture);
            try { Interaction.DeleteSetting(scratch); } catch { }
        }
        return verdict;
    }
}
