using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using Manager = FastMediaSorterCompanion;

// Specification 267: real-window recipes for the ViewerA help topics (modern viewer windows).
// Every recipe works on fictional data created under Path.GetTempPath() and inside the harness's isolated registry
// profile; none of them starts playback, a download, a worker, a network connection or the clipboard.
static partial class Topics
{
    static IEnumerable<Topic> ViewerA() => new Topic[]
    {
        new Topic("hotkeys", true,
            () => Program.ViewerForm("Hotkeys_Form", new Dictionary<string, string> { ["randomFile"] = "Ctrl+R", ["undo"] = "Ctrl+Z" }),
            "shortcut list with two sample overrides (Random file, Undo); the Random file row is selected",
            Prepare: form =>
            {
                var grid = (ListView)ViewerAReflect.Field(form, "grid");
                foreach (ListViewItem item in grid.Items)
                    if ((string)item.Tag == "randomFile") { item.Selected = true; item.EnsureVisible(); break; }
            }),

        new Topic("file-details", true,
            () => Program.ViewerForm("File_Details_Form", ViewerASamples.Photo(), null),
            "details of a sample photo with camera data (no GPS); no media engine, nothing copied",
            Settle: async form =>
            {
                var box = (RichTextBox)ViewerAReflect.Field(form, "box");
                await ViewerAReflect.WaitFor(() => box.TextLength > 20);
                // The temp folder carries the user name: show a neutral folder instead (display only, the file is untouched).
                var folder = Path.GetDirectoryName(ViewerASamples.Photo());
                var at = box.Find(folder);
                if (at >= 0) { box.Select(at, folder.Length); box.SelectedText = @"C:\Pictures\Holiday 2026"; }
                box.SelectionStart = 0; box.SelectionLength = 0; box.ScrollToCaret();
            }),

        new Topic("image-editor", true,
            () => Program.ViewerForm("Image_Editor_Form", ViewerASamples.Photo(), false),
            "sample landscape with one red ellipse outline drawn; the original file is never saved or replaced",
            Prepare: form =>
            {
                if (!(bool)ViewerAReflect.Call(form, "TryLoadOriginal")) throw new InvalidOperationException("The sample picture did not open in the editor.");
            },
            Settle: async form =>
            {
                // Replays one drag with the ellipse tool through the editor's own mouse handlers, so Undo is enabled in the picture.
                var canvas = (Control)ViewerAReflect.Field(form, "canvas");
                var toolType = form.GetType().GetNestedType("EditorTool", Program.Static | BindingFlags.NonPublic);
                ViewerAReflect.Call(form, "SelectTool", Enum.Parse(toolType, "EllipseOutline"));
                // The press is a canvas point, the drag end is stored in IMAGE pixels (gesture_Current): a ring around the sun.
                var fit = (Rectangle)ViewerAReflect.Call(canvas, "ImageRect");
                var picture = (Image)ViewerAReflect.Field(form, "image_Bitmap");
                var from = new Point(fit.Left + fit.Width * 75 / 100, fit.Top + fit.Height * 17 / 100);
                var to = new Point(picture.Width * 96 / 100, picture.Height * 48 / 100);
                ViewerAReflect.Call(form, "Canvas_MouseDown", canvas, new MouseEventArgs(MouseButtons.Left, 1, from.X, from.Y, 0));
                ViewerAReflect.SetField(form, "gesture_Current", to);
                ViewerAReflect.Call(form, "Canvas_MouseUp", canvas, new MouseEventArgs(MouseButtons.Left, 1, from.X, from.Y, 0));
                await Task.Delay(150);
                // The harness closes the window after the picture; with an unsaved edit the editor would ask "Save / Don't save / Cancel"
                // and block the run. The discard answer is pre-set (Image_Editor_Form.vb:771), so the close passes without any dialog and
                // nothing is saved; Save is never invoked.
                ViewerAReflect.SetField(form, "discard_Confirmed", true);
            }),

        new Topic("video-convert", true,
            () => Program.ViewerForm("Video_Convert_Form", true),
            "progress window at 42 percent; no encoder is started",
            Settle: async form =>
            {
                ViewerAReflect.Call(form, "ReportPercent", 42);
                ViewerAReflect.SetField(form, "finished", true); // lets the harness close the window without a cancel round trip
                await Task.Delay(100);
            }),

        new Topic("welcome", true,
            () =>
            {
                // The wizard needs a viewer window as its host; this one is never shown or loaded. The servers block of the finish
                // step is fed fixed facts (nothing installed), so no component file or process is looked at.
                var host = new ViewerAHost();
                ViewerAReflect.SetField(host, "WelcomeServerFactsOverride", new bool?[] { false, null, false, null });
                var form = Program.ViewerForm("Welcome_Form", host, false);
                form.Disposed += (_, _) => { try { host.Dispose(); } catch { } };
                return form;
            },
            "step 3 of 6 (Sorting) with a sample folder chosen for key 1; Next is never pressed, so nothing is applied",
            Prepare: form =>
            {
                ViewerAReflect.SetField(form, "draftKeyOne", @"D:\Sorted\Holiday 2026");
                ViewerAReflect.Call(form, "ShowStep", 2);
            }),

        new Topic("settings", true,
            () =>
            {
                // Same order as the viewer: the (never shown) default viewer window and its preferences exist before the settings window.
                var viewer = ViewerADefault.MainForm();
                ViewerAReflect.Call(viewer, "GetModernPreferences");
                return Program.ViewerForm("Table_Form");
            },
            "first page (General) with the program's defaults of a fresh profile; no other page is built",
            Prepare: form => ViewerAReflect.Call(form, "PrepareForDisplay"),
            Settle: async form => await Task.Delay(400)),

        new Topic("thumbnail-panel", true,
            () =>
            {
                // The panel reads the main window's file list: fill the (never shown) default viewer window with a fictional folder.
                var viewer = ViewerADefault.MainForm();
                var files = ViewerASamples.Gallery();
                ViewerAReflect.SetField(viewer, "files_List", files);
                ViewerAReflect.SetField(viewer, "is_Files_Array_Active", false);
                ViewerAReflect.SetField(viewer, "Current_Folder_Path", ViewerASamples.Folder());
                Program.ViewerType("Common_Module").GetField("Current_File_Name", Program.Static).SetValue(null, files[2]);
                // No disk cache: nothing is written to %LOCALAPPDATA%; the cards are filled from memory below.
                var prefs = ViewerAReflect.Call(viewer, "GetModernPreferences");
                prefs.GetType().GetProperty("PanelThumbCacheMb").SetValue(prefs, 0);
                prefs.GetType().GetProperty("PanelViewMode").SetValue(prefs, "grid");
                prefs.GetType().GetProperty("PanelThumbnailSize").SetValue(prefs, 128);
                return Program.ViewerForm("Image_Panel_Form");
            },
            "triage mode, tile view, 12 sample pictures with three selected; thumbnails come from memory, no loader work",
            Prepare: form =>
            {
                ViewerAReflect.Call(form, "PrepareForDisplay");
                var px = (int)ViewerAReflect.Field(form, "thumb_Px");
                var memory = ViewerAReflect.Field(form, "memory");
                var files = ViewerASamples.Gallery();
                for (var i = 0; i < files.Count; i++)
                    ViewerAReflect.Call(memory, "Put", files[i], ViewerASamples.Picture(px, px * 2 / 3, i % 4));
                var model = ViewerAReflect.Field(form, "model");
                ViewerAReflect.Call(model, "CtrlClick", 5);
                ViewerAReflect.Call(model, "CtrlClick", 6);
                ViewerAReflect.Call(form, "AfterSelectionChange");
            },
            Settle: async form => await Task.Delay(300)),
    };
}

// A viewer window that is only a host: the viewer's lifecycle (settings load, media, listeners) never runs.
sealed class ViewerAHost : fmsl.Main_Form
{
    protected override void OnLoad(EventArgs e) { }
    protected override void OnShown(EventArgs e) { }
    protected override void OnFormClosing(FormClosingEventArgs e) { }
}

static class ViewerADefault
{
    // VB's default instance of the viewer window (what the panel and the settings window call "Main_Form"); constructed, never shown.
    internal static Form MainForm()
    {
        var project = typeof(fmsl.Main_Form).Assembly.GetType("fmsl.My.MyProject", true);
        var forms = project.GetProperty("Forms", Program.Static).GetValue(null);
        return (Form)forms.GetType().GetProperty("Main_Form", Program.Instance).GetValue(forms);
    }
}

static class ViewerAReflect
{
    internal static object Field(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Program.Instance | BindingFlags.DeclaredOnly);
            if (field != null) return field.GetValue(target);
            var property = type.GetProperty(name, Program.Instance | BindingFlags.DeclaredOnly);
            if (property != null) return property.GetValue(target);
        }
        throw new MissingMemberException(target.GetType().Name, name);
    }

    internal static void SetField(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Program.Instance | BindingFlags.DeclaredOnly);
            if (field != null) { field.SetValue(target, value); return; }
        }
        throw new MissingMemberException(target.GetType().Name, name);
    }

    internal static object Call(object target, string name, params object[] args)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var method = type.GetMethod(name, Program.Instance | BindingFlags.DeclaredOnly);
            if (method != null) return method.Invoke(target, args);
        }
        throw new MissingMethodException(target.GetType().Name, name);
    }

    internal static async Task WaitFor(Func<bool> condition, int milliseconds = 5000)
    {
        for (var waited = 0; waited < milliseconds && !condition(); waited += 50) await Task.Delay(50);
    }
}

// Sample pictures drawn from scratch, so no real photo and no personal data is involved.
static class ViewerASamples
{
    internal static string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "fms-help-viewer-a", "Holiday 2026");
        Directory.CreateDirectory(folder);
        return folder;
    }

    // Twelve small JPEGs in the sample folder (fictional names), for the thumbnail panel.
    internal static List<string> Gallery()
    {
        var files = new List<string>();
        var codec = ImageCodecInfo.GetImageEncoders().First(e => e.MimeType == "image/jpeg");
        for (var i = 0; i < 12; i++)
        {
            var path = Path.Combine(Folder(), $"IMG_{101 + i:0000}.jpg");
            if (!File.Exists(path))
            {
                using var bitmap = Picture(480, 320, i % 4);
                bitmap.Save(path, codec, null);
                File.SetLastWriteTime(path, new DateTime(2026, 7, 14, 9 + i / 3, 10 + 7 * i % 50, 0));
            }
            files.Add(path);
        }
        return files;
    }

    // A small landscape: sky gradient, sun, two hills. Variant changes the colours.
    internal static Bitmap Picture(int width, int height, int variant = 0)
    {
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color[][] skies =
        {
            new[] { Color.FromArgb(58, 120, 196), Color.FromArgb(252, 214, 150) },
            new[] { Color.FromArgb(40, 52, 110), Color.FromArgb(236, 130, 96) },
            new[] { Color.FromArgb(110, 170, 220), Color.FromArgb(240, 244, 250) },
            new[] { Color.FromArgb(92, 70, 140), Color.FromArgb(250, 190, 120) },
        };
        var sky = skies[variant % skies.Length];
        using (var brush = new LinearGradientBrush(new Rectangle(0, 0, width, height), sky[0], sky[1], 90f)) g.FillRectangle(brush, 0, 0, width, height);
        using (var sun = new SolidBrush(Color.FromArgb(255, 246, 200))) g.FillEllipse(sun, width * (0.62f + 0.06f * (variant % 3)), height * 0.16f, height * 0.2f, height * 0.2f);
        using (var far = new SolidBrush(Color.FromArgb(70, 110, 96)))
            g.FillPolygon(far, new[] { new PointF(0, height * 0.72f), new PointF(width * 0.28f, height * (0.5f + 0.03f * (variant % 3))), new PointF(width * 0.58f, height * 0.7f), new PointF(width * 0.84f, height * 0.52f), new PointF(width, height * 0.68f), new PointF(width, height), new PointF(0, height) });
        using (var near = new SolidBrush(Color.FromArgb(36, 78, 64)))
            g.FillPolygon(near, new[] { new PointF(0, height * 0.86f), new PointF(width * 0.36f, height * 0.7f), new PointF(width * 0.7f, height * 0.88f), new PointF(width, height * 0.78f), new PointF(width, height), new PointF(0, height) });
        return bitmap;
    }

    // "Holiday 2026 - 042.jpg": a JPEG with a small Exif block (camera, exposure, date) and no location.
    internal static string Photo()
    {
        var path = Path.Combine(Folder(), "IMG_0042.jpg");
        if (File.Exists(path)) return path;
        using var bitmap = Picture(1600, 1067);
        using var jpeg = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(e => e.MimeType == "image/jpeg");
        using (var parameters = new EncoderParameters(1))
        {
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, 88L);
            bitmap.Save(jpeg, codec, parameters);
        }
        var bytes = jpeg.ToArray();
        var exif = ExifSegment();
        // After the SOI and the JFIF segment that GDI+ writes.
        var insertAt = 2;
        if (bytes[2] == 0xFF && bytes[3] == 0xE0) insertAt = 4 + ((bytes[4] << 8) | bytes[5]);
        using var output = new MemoryStream();
        output.Write(bytes, 0, insertAt);
        output.Write(exif, 0, exif.Length);
        output.Write(bytes, insertAt, bytes.Length - insertAt);
        File.WriteAllBytes(path, output.ToArray());
        File.SetCreationTime(path, new DateTime(2026, 7, 14, 18, 32, 10));
        File.SetLastWriteTime(path, new DateTime(2026, 7, 14, 18, 32, 10));
        return path;
    }

    sealed record Entry(ushort Tag, ushort Type, uint Count, byte[] Value);

    static byte[] U16(int value) => new[] { (byte)(value >> 8), (byte)value };
    static byte[] U32(long value) => new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
    static Entry Ascii(ushort tag, string text) { var bytes = System.Text.Encoding.ASCII.GetBytes(text + "\0"); return new Entry(tag, 2, (uint)bytes.Length, bytes); }
    static Entry Short(ushort tag, int value) => new Entry(tag, 3, 1, U16(value));
    static Entry Long(ushort tag, long value) => new Entry(tag, 4, 1, U32(value));
    static Entry Rational(ushort tag, long numerator, long denominator) => new Entry(tag, 5, 1, U32(numerator).Concat(U32(denominator)).ToArray());

    // One big-endian IFD whose offsets are measured from the start of the TIFF header; 'start' is where this IFD begins.
    static byte[] Ifd(List<Entry> entries, int start)
    {
        entries = entries.OrderBy(e => e.Tag).ToList();
        var head = 2 + 12 * entries.Count + 4;
        var table = new MemoryStream();
        var data = new MemoryStream();
        table.Write(U16(entries.Count));
        foreach (var e in entries)
        {
            table.Write(U16(e.Tag)); table.Write(U16(e.Type)); table.Write(U32(e.Count));
            if (e.Value.Length <= 4) { table.Write(e.Value); for (var i = e.Value.Length; i < 4; i++) table.WriteByte(0); }
            else { table.Write(U32(start + head + data.Length)); data.Write(e.Value); if (data.Length % 2 == 1) data.WriteByte(0); }
        }
        table.Write(U32(0));
        table.Write(data.ToArray());
        return table.ToArray();
    }

    static byte[] ExifSegment()
    {
        // The Exif IFD sits right after IFD0 and its data; the pointer entry has a fixed size, so IFD0's length is known first.
        List<Entry> Zero(long exifOffset) => new()
        {
            Ascii(0x010F, "Demo Camera Co."), Ascii(0x0110, "DC-100"), Ascii(0x0131, "Demo Firmware 1.2"),
            // 96 dpi on purpose: the editor copies the picture with DrawImageUnscaled, which honours the file's own dpi (a 72-dpi
            // photo is enlarged 1.33 times and its right and bottom edges are lost); see the report.
            Rational(0x011A, 96, 1), Rational(0x011B, 96, 1), Short(0x0128, 2),
            Ascii(0x0132, "2026:07:14 18:32:10"), Long(0x8769, exifOffset),
        };
        var ifd0Length = Ifd(Zero(0), 8).Length;
        var exifOffset = 8 + ifd0Length;
        var ifd0 = Ifd(Zero(exifOffset), 8);
        var exif = Ifd(new List<Entry>
        {
            Rational(0x829A, 1, 250), Rational(0x829D, 28, 10), Short(0x8827, 200),
            Ascii(0x9003, "2026:07:14 18:32:10"), Rational(0x920A, 35, 1),
        }, exifOffset);
        var tiff = new MemoryStream();
        tiff.Write(new byte[] { 0x4D, 0x4D, 0x00, 0x2A, 0, 0, 0, 8 });
        tiff.Write(ifd0);
        tiff.Write(exif);
        var payload = new MemoryStream();
        payload.Write(System.Text.Encoding.ASCII.GetBytes("Exif\0\0"));
        payload.Write(tiff.ToArray());
        var segment = new MemoryStream();
        segment.Write(new byte[] { 0xFF, 0xE1 });
        segment.Write(U16((int)payload.Length + 2));
        segment.Write(payload.ToArray());
        return segment.ToArray();
    }
}
