using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using fmsl;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

static class EpubReaderScenes
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static object Get(object target, string name) => target.GetType().GetField(name, Instance)?.GetValue(target)
        ?? typeof(Main_Form).GetField(name, Instance)?.GetValue(target);
    static object Call(object target, string name, params object[] args) => typeof(Main_Form).GetMethod(name, Instance).Invoke(target, args);
    static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    static async Task Until(Func<bool> predicate, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate())
        {
            if (watch.Elapsed.TotalSeconds > 30) throw new TimeoutException(message);
            await Task.Delay(25);
        }
    }

    internal static int Run(string output)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        var scratch = "FmsEpubReaderScenes_" + Guid.NewGuid().ToString("N");
        string fixture = Path.Combine(Path.GetTempPath(), scratch + ".epub");
        using (var file = File.Create(fixture))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            void Add(string key, string text) { using var writer = new StreamWriter(zip.CreateEntry(key).Open(), new UTF8Encoding(false)); writer.Write(text); }
            Add("mimetype", "application/epub+zip");
            Add("META-INF/container.xml", "<container><rootfiles><rootfile full-path='book/package.opf' media-type='application/oebps-package+xml'/></rootfiles></container>");
            Add("book/package.opf", "<package><metadata><title>Reader verification</title></metadata><manifest><item id='one' href='one.xhtml' media-type='application/xhtml+xml'/><item id='two' href='chapter %23%25&amp; книга.xhtml' media-type='application/xhtml+xml'/><item id='nav' href='nav.xhtml' media-type='application/xhtml+xml' properties='nav'/><item id='css' href='book.css' media-type='text/css'/></manifest><spine><itemref idref='one'/><itemref idref='two'/></spine></package>");
            Add("book/one.xhtml", "<html xmlns='http://www.w3.org/1999/xhtml'><head><title>One</title><link rel='stylesheet' href='book.css'/></head><body><p id='text'>Needle alpha. Reader sample.</p><script>document.body.dataset.executed='yes';</script><img src='https://example.invalid/blocked.png'/><div style='height:4000px'>Tail</div><a href='chapter %23%25&amp; книга.xhtml#note'>Next</a></body></html>");
            Add("book/chapter #%& книга.xhtml", "<html xmlns='http://www.w3.org/1999/xhtml' lang='ar' dir='rtl'><head><title>Two</title></head><body><p id='note'>Needle beta. العربية 中文.</p></body></html>");
            Add("book/nav.xhtml", "<html xmlns='http://www.w3.org/1999/xhtml' xmlns:epub='http://www.idpf.org/2007/ops'><body><nav epub:type='toc'><ol><li><a href='one.xhtml'>One</a><ol><li><a href='chapter %23%25&amp; книга.xhtml#note'>Note</a></li></ol></li></ol></nav></body></html>");
            Add("book/book.css", "p {font-style:italic} @import url(https://example.invalid/blocked.css);");
        }
        var common = typeof(Main_Form).Assembly.GetType("fmsl.Common_Module", true);
        common.GetField("App_name", Static).SetValue(null, scratch);
        common.GetField("Current_File_Name", Static).SetValue(null, fixture);
        var logger = typeof(Main_Form).Assembly.GetType("fmsl.AppFileLogger", true);
        logger.GetField("LogDirectoryOverride", Static).SetValue(null, Path.Combine(output, "logs"));
        logger.GetMethod("Initialize", Static).Invoke(null, null);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetDefaultFont(new Font("Microsoft Sans Serif", 8.25f));
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        using var window = new SceneViewer { Text = "EPUB reader verification", Location = new Point(40, 40), Size = new Size(1200, 800) };
        int verdict = 0;
        var observations = new System.Collections.Generic.List<object>();
        Action run = async () =>
        {
            try
            {
                Call(window, "BuildModernLayout");
                var preferences = (ModernViewerPreferences)Call(window, "GetModernPreferences");
                preferences.FullEpubReader = true;
                var watch = Stopwatch.StartNew();
                Call(window, "LoadEpubReader", fixture);
                await Until(() => Get(window, "epub_Reader_Host") is not null && Get(window, "epub_Reader_Toolbar") is not null, "EPUB host initialization");
                var host = Get(window, "epub_Reader_Host");
                var view = (WebView2)Get(host, "view");
                await Until(() => view.CoreWebView2?.DocumentTitle == "One", "First chapter");
                observations.Add(new { name = "first-chapter", milliseconds = watch.ElapsedMilliseconds, dpi = window.DeviceDpi, runtime = view.CoreWebView2.Environment.BrowserVersionString });
                string dom = await view.CoreWebView2.ExecuteScriptAsync("JSON.stringify({script:document.body.dataset.executed??null,text:document.body.innerText,scripts:document.scripts.length})");
                Check(dom.Contains("Needle alpha") && dom.Contains("null") && dom.Contains("scripts\\\":0"), "Authored script removed and text preserved");
                observations.Add(new { name = "sanitized-script-free-content", pass = true });
                using (var exclusive = new FileStream(fixture, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                observations.Add(new { name = "original-handle-released", pass = true });
                var query = (TextBox)Get(window, "epub_Reader_Query");
                Call(window, "ToggleEpubReaderPane", 1);
                query.Focus();
                query.Text = "Needle";
                Call(window, "SearchEpubReader");
                var results = (ListBox)Get(window, "epub_Reader_Results");
                await Until(() => results.Items.Count == 2, "Whole-book search");
                results.SelectedIndex = 1;
                await Until(() => view.CoreWebView2.DocumentTitle == "Two", "Reserved and Unicode chapter navigation");
                await Until(() => view.CoreWebView2.Find.MatchCount > 0, "Search highlight");
                observations.Add(new { name = "whole-book-search-and-highlight", pass = true, matches = results.Items.Count });
                await view.CoreWebView2.ExecuteScriptAsync("(()=>{let r=document.createRange();r.selectNodeContents(document.getElementById('note'));let s=window.getSelection();s.removeAllRanges();s.addRange(r)})()");
                Call(window, "CopyEpubReaderSelection");
                await Until(() => Clipboard.ContainsText() && Clipboard.GetText().Contains("Needle beta"), "Selection copy");
                observations.Add(new { name = "selection-copy", pass = true });
                using (var png = File.Create(Path.Combine(output, "epub-reader.png"))) await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, png);
                var key = new KeyEventArgs(Keys.D5);
                observations.Add(new { name = "input-state-before-key", interaction = Get(window, "epub_Reader_Interaction"), shown = Call(window, "IsEpubReaderShown"), windowActive = Form.ActiveForm == window });
                bool owns = (bool)Call(window, "EpubReaderOwnsInput", key);
                Check(owns, "Document interaction owns sorting keys");
                preferences.FullEpubReader = false;
                Call(window, "ApplyEpubReaderPreferences");
                Check(Get(window, "epub_Reader_Host") is null, "Independent disable disposes host");
                observations.Add(new { name = "input-and-independent-disable", pass = true });
                File.Move(fixture, fixture + ".moved");
                File.Delete(fixture + ".moved");
            }
            catch (Exception ex) { verdict = 1; observations.Add(new { error = ex.ToString() }); }
            finally
            {
                Call(window, "ReleaseEpubReader");
                File.WriteAllText(Path.Combine(output, "epub-scenes.json"), JsonSerializer.Serialize(new { verdict, observations }, new JsonSerializerOptions { WriteIndented = true }));
                window.Close();
            }
        };
        try { window.Show(); window.BeginInvoke(run); Application.Run(window); }
        finally
        {
            try { Microsoft.VisualBasic.Interaction.DeleteSetting(scratch); } catch (ArgumentException) { }
            File.Delete(fixture);
        }
        Console.WriteLine("EpubReaderScenes: " + (verdict == 0 ? "PASS" : "FAIL"));
        return verdict;
    }
}
