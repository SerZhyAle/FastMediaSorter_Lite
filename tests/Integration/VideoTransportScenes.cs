using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using LibVLCSharp.Shared;
using fmsl;

class TransportSceneWindow : Main_Form
{
    protected override void OnLoad(EventArgs e) { }
    protected override void OnShown(EventArgs e) { }
    protected override void OnFormClosing(FormClosingEventArgs e) { }
}

static class VideoTransportScenes
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    static FieldInfo Field(string name) => typeof(Main_Form).GetField(name, Flags) ?? typeof(Main_Form).GetField("_" + name, Flags);
    static object Get(Main_Form form, string name) => Field(name).GetValue(form);
    static void Set(Main_Form form, string name, object value) => Field(name).SetValue(form, value);
    static object Call(Main_Form form, string name, params object[] args)
    {
        if (SynchronizationContext.Current == null) SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        return typeof(Main_Form).GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(form, args);
    }
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Pump(int milliseconds)
    {
        long until = Environment.TickCount64 + milliseconds;
        do { Application.DoEvents(); Thread.Sleep(10); } while (Environment.TickCount64 < until);
    }
    static void Await(Func<bool> condition, string message)
    {
        long until = Environment.TickCount64 + 8000;
        while (!condition() && Environment.TickCount64 < until) Pump(20);
        Assert(condition(), message);
    }
    static void Click(Button button)
    {
        Cursor.Position = button.PointToScreen(new Point(button.Width / 2, button.Height / 2));
        Assert(WindowFromPoint(Cursor.Position) == button.Handle, "The scene button is obscured by another window");
        var point = (IntPtr)((button.Height / 2 << 16) | button.Width / 2);
        SendMessage(button.Handle, 0x201, (IntPtr)1, point);
        SendMessage(button.Handle, 0x202, IntPtr.Zero, point);
        Pump(100);
    }

    static void DoubleClick(Control surface)
    {
        Cursor.Position = surface.PointToScreen(new Point(surface.Width / 2, surface.Height / 2));
        Pump(50);
        // Exercise the gesture handlers without depending on a native VLC child's hit-testing.
        var click = new MouseEventArgs(MouseButtons.Left, 1, surface.Width / 2, surface.Height / 2, 0);
        typeof(Control).GetMethod("OnMouseClick", Flags).Invoke(surface, new object[] { click });
        typeof(Control).GetMethod("OnMouseDoubleClick", Flags).Invoke(surface,
            new object[] { new MouseEventArgs(MouseButtons.Left, 2, click.X, click.Y, 0) });
        Pump(150);
    }

    static ToolStripMenuItem FullScreenRow(Main_Form form)
    {
        Call(form, "BuildVideoMenu");
        return ((ContextMenuStrip)Get(form, "video_Menu")).Items.OfType<ToolStripMenuItem>()
            .Single(item => item.Tag != null && (string)item.Tag.GetType().GetField("ActionId").GetValue(item.Tag) == "fullScreen");
    }

    static int SnapshotRed(Main_Form form, string clip, int sequence)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(clip), "frame-identity-" + sequence + ".png");
        var frameSurface = (PictureBox)Get(form, "frame_Surface");
        if (frameSurface != null && frameSurface.Visible && frameSurface.Image != null)
        {
            int red = ((Bitmap)frameSurface.Image).GetPixel(frameSurface.Image.Width / 2, frameSurface.Image.Height / 2).R;
            using var sample = new Bitmap(1, 1);
            sample.SetPixel(0, 0, Color.FromArgb(red, 0, 0));
            sample.Save(path);
            return red;
        }
        var surface = (Control)Get(form, "vlc_Video_View");
        var center = surface.PointToScreen(new Point(surface.Width / 2, surface.Height / 2));
        using var bitmap = new Bitmap(1, 1);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(center, Point.Empty, bitmap.Size);
        bitmap.Save(path);
        return bitmap.GetPixel(0, 0).R;
    }

    static void CheckFramePlayback(Main_Form form, MediaPlayer player, LibVLC vlc, string clip, long[] frameTimes = null)
    {
        frameTimes ??= Enumerable.Range(0, 10).Select(index => index * 200L).ToArray();
        ((ModernViewerPreferences)Get(form, "modern_Preferences")).VideoControlsHideDelaySec = 15;
        Set(form, "video_Volume_Level", 0.5);
        player.Volume = 50;
        player.Stop();
        Call(form, "ResetPlaybackExtrasForNewMedia");
        Set(form, "current_Loaded_File_Name", clip);
        using var media = new Media(vlc, clip, FromType.FromPath);
        player.Play(media);
        Await(() => player.IsPlaying && player.Length > 0, "The frame-identity clip did not start");
        player.SetPause(true);
        Await(() => player.State == VLCState.Paused, "Could not pause the frame-identity clip");
        player.Time = 0;
        Pump(300);
        form.TopMost = false;
        form.TopMost = true;
        form.BringToFront();
        form.Activate();
        double redScale = 1.0;
        player.Time = 0;
        Pump(300);
        Set(form, "is_Video_Muted", false);
        var rows = ((System.Collections.IEnumerable)Call(form, "BuildSpeedLadderItems")).Cast<ToolStripMenuItem>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        rows.Single(item => !string.IsNullOrEmpty(item.ToolTipText)).PerformClick();
        Await(() => (bool)Get(form, "frame_Playback_Mode") && !(bool)Get(form, "frame_Busy"), "Frame decoding did not become ready");
        Assert((bool)Get(form, "frame_Playback_Mode") && player.Mute,
            $"Frame mode did not force silent playback (mode={Get(form, "frame_Playback_Mode")}, mute={player.Mute}, canPause={player.CanPause}, seekable={player.IsSeekable}, fps={player.Fps})");
        Assert(!(bool)Get(form, "is_Video_Muted"), "Frame mode changed the user's saved mute choice");
        int sequence = 0;
        Pump(200);
        int firstRed = SnapshotRed(form, clip, sequence++);
        using (var screenshot = new Bitmap(form.Width, form.Height))
        {
            using (var graphics = Graphics.FromImage(screenshot)) graphics.CopyFromScreen(form.Location, Point.Empty, form.Size);
            screenshot.Save(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(clip), "frame-mode.png"));
        }
        Assert(firstRed <= 3, $"Frame mode did not begin on the first frame: red={firstRed}");
        for (int index = 1; index < 10; index++)
        {
            int expected = index;
            Await(() => (long)Call(form, "PlayerTimeMs") >= frameTimes[expected], $"The frame timer did not reach frame {expected}");
            Pump(500);
            int red = SnapshotRed(form, clip, sequence++);
            Console.WriteLine($"Frame {expected}: red={red}, time={Call(form, "PlayerTimeMs")}");
            if (Math.Abs(red - expected * 20 * redScale) > 4)
            {
                using var screenshot = new Bitmap(form.Width, form.Height);
                using (var graphics = Graphics.FromImage(screenshot)) graphics.CopyFromScreen(form.Location, Point.Empty, form.Size);
                screenshot.Save(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(clip), "frame-failure.png"));
                Console.WriteLine($"Failure state: nativeTime={player.Time}, nativePosition={player.Position}, nativeState={player.State}, enabled={((System.Windows.Forms.Timer)Get(form, "frame_Playback_Timer")).Enabled}, scale={redScale}");
            }
            Assert(Math.Abs(red - expected * 20 * redScale) <= 4, $"Frame {expected} was skipped or repeated: red={red}");
            Assert(watch.ElapsedMilliseconds >= expected * 1000 - 200, "Frame playback advanced faster than one frame per second");
            Assert(player.Mute, "Frame playback lost its forced mute");
        }
        Await(() => !(bool)Get(form, "frame_Auto_Running") && !(bool)Get(form, "frame_Busy"), "Frame playback did not stop at EOF");
        Assert(!((bool)Call(form, "IsVlcActuallyPlaying")), "The transport still reports playback after the final frame");
        Call(form, "RunPreviousFrame");
        Await(() => !(bool)Get(form, "frame_Busy"), "Previous-frame decoding did not finish");
        Pump(250);
        Assert(Math.Abs(SnapshotRed(form, clip, sequence++) - 160 * redScale) <= 4, "Previous-frame action did not show frame 8");
        Call(form, "RunPlaybackAction", "frameBack");
        Await(() => !(bool)Get(form, "frame_Busy"), "Repeated previous-frame decoding did not finish");
        Pump(250);
        Assert(Math.Abs(SnapshotRed(form, clip, sequence++) - 140 * redScale) <= 4, "Repeated backward step did not show frame 7");
        Call(form, "RunFrameStep");
        Await(() => !(bool)Get(form, "frame_Busy"), "Forward-frame decoding did not finish");
        Pump(250);
        Assert(Math.Abs(SnapshotRed(form, clip, sequence++) - 160 * redScale) <= 4, "Forward after backward did not return to frame 8");
        long pausedTime = (long)Call(form, "PlayerTimeMs");
        Pump(1150);
        Assert((long)Call(form, "PlayerTimeMs") == pausedTime, "Manual frame stepping unexpectedly resumed the frame timer");
        Call(form, "TogglePlayPause");
        Call(form, "ApplyPlaybackRate", 0.5f, false);
        Await(() => player.IsPlaying, "Selecting a numeric speed did not resume ordinary playback");
        Await(() => !player.Mute && !(bool)Get(form, "frame_Playback_Mode"), "Leaving frame mode did not restore the user's sound choice");
        Call(form, "ResetPlaybackExtrasForNewMedia");
        Assert(!((System.Windows.Forms.Timer)Get(form, "frame_Playback_Timer")).Enabled, "A frame timer survived the media reset");
        Console.WriteLine("All ten decoded frames appeared once at one-second intervals; backward/forward identities, EOF, silence and sound restoration passed.");
    }

    static void CheckFrameCache(string executable, string clip)
    {
        var type = typeof(Main_Form).Assembly.GetType("fmsl.VideoFrameSession");
        using var session = (IDisposable)Activator.CreateInstance(type, Flags, null,
            new object[] { executable, clip, 120000L }, null);
        void Run(string name, params object[] arguments)
        {
            var task = (System.Threading.Tasks.Task)type.GetMethod(name, Flags).Invoke(session, arguments);
            Await(() => task.IsCompleted, "Frame cache operation did not complete: " + name);
            task.GetAwaiter().GetResult();
        }
        int Red()
        {
            var frame = type.GetProperty("Current", Flags).GetValue(session);
            var image = (Bitmap)frame.GetType().GetField("Image", Flags).GetValue(frame);
            return image.GetPixel(image.Width / 2, image.Height / 2).R;
        }
        Run("OpenAsync", 0L, false);
        for (int frame = 1; frame < 10; frame++) Run("MoveAsync", true);
        Assert(Red() == 180, "Cache test did not reach the final real frame");
        Run("MoveAsync", false);
        Assert(Red() == 160, "Cached backward step did not show frame 8");
        Run("MoveAsync", false);
        Assert(Red() == 140, "Backward step outside the cache did not decode frame 7 again");
        type.GetMethod("ReleaseRetired", Flags).Invoke(session, null);
        Run("MoveAsync", true);
        Assert(Red() == 160, "Forward after cache refill did not show frame 8");
        using var beyondEnd = (IDisposable)Activator.CreateInstance(type, Flags, null,
            new object[] { executable, clip, 120000L }, null);
        var openEnd = (System.Threading.Tasks.Task)type.GetMethod("OpenAsync", Flags).Invoke(beyondEnd, new object[] { 10000L, true });
        Await(() => openEnd.IsCompleted, "Seeking past the video end did not recover its preceding frame");
        openEnd.GetAwaiter().GetResult();
        var previous = type.GetProperty("Current", Flags).GetValue(beyondEnd);
        Assert((long)previous.GetType().GetField("TimeMs", Flags).GetValue(previous) == 1600,
            "A playback clock past the video end did not select the preceding real frame");
        Console.WriteLine("Backward stepping beyond the bounded cache decoded the exact older frame again.");
    }

    [STAThread] static int Main(string[] args)
    {
        string profileName = "FmsVideoScene_" + Guid.NewGuid().ToString("N");
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            typeof(Main_Form).Assembly.GetType("fmsl.Common_Module").GetField("App_name").SetValue(null, profileName);
            typeof(Main_Form).Assembly.GetType("fmsl.AppFileLogger").GetMethod("Initialize").Invoke(null, null);
            Core.Initialize(args[0]);
            using var vlc = new LibVLC("--no-video-title-show", "--no-mouse-events", "--quiet");
            using var player = new MediaPlayer(vlc) { EnableMouseInput = false, EnableKeyInput = false };
            using var form = new TransportSceneWindow { TopMost = true, StartPosition = FormStartPosition.Manual,
                Location = new Point(20, 20), ClientSize = new Size(600, 350) };
            Call(form, "BuildModernLayout");
            var schedulerType = typeof(Main_Form).Assembly.GetType("fmsl.SettingsFlushScheduler");
            Set(form, "settings_Scheduler", Activator.CreateInstance(schedulerType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new object[] { new Func<bool>(() => true), new Action<int>(delay => { }), new Action(() => { }), new Action<string>(message => { }), 1500 }, null));
            var prefs = new ModernViewerPreferences { VideoControlsHideDelaySec = 4 };
            Set(form, "modern_Preferences", prefs);
            Set(form, "video_Menu", new ContextMenuStrip());
            using var textFont = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
            using var symbolFont = new Font("Segoe UI Symbol", 16, FontStyle.Regular, GraphicsUnit.Pixel);
            Set(form, "chrome_Text_Font", textFont);
            Set(form, "chrome_Symbol_Font", symbolFont);
            Set(form, "chrome_Theme", typeof(Main_Form).Assembly.GetType("fmsl.ChromeTheme")
                .GetMethod("FromScheme").Invoke(null, new object[] { Color.White, Color.Black, false }));
            form.Show(); form.Activate();
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            Set(form, "libVlc", vlc);
            Set(form, "vlc_Media_Player", player);
            Set(form, "frame_Ffmpeg_Path", args[4]);
            player.Volume = 0;
            Set(form, "video_Volume_Level", 0.0);
            Set(form, "is_Vlc_Playing", true);
            Set(form, "current_Loaded_File_Name", args[1]);
            using var surface = new LibVLCSharp.WinForms.VideoView { MediaPlayer = player, Dock = DockStyle.Fill };
            Set(form, "vlc_Video_View", surface);
            ((Control)Get(form, "panel_Media")).Controls.Add(surface);
            surface.BringToFront();
            surface.MouseClick += (sender, e) => Call(form, "Vlc_Video_View_MouseClick", sender, e);
            surface.MouseDoubleClick += (sender, e) => Call(form, "Vlc_Video_View_MouseDoubleClick", sender, e);
            surface.MouseMove += (sender, e) => Call(form, "Vlc_Video_View_MouseMove", sender, e);
            Call(form, "EnsureVideoControls"); Call(form, "ShowVideoControls");
            ((Form)Get(form, "video_Chrome_Host")).TopMost = true;
            if (args.Length > 3 && args[3] == "frames")
            {
                CheckFramePlayback(form, player, vlc, args[2]);
                CheckFramePlayback(form, player, vlc, args[5], new long[] { 0, 350, 400, 750, 800, 1150, 1200, 1550, 1600, 1950 });
                CheckFrameCache(args[4], args[2]);
                player.Stop();
                Set(form, "is_Vlc_Playing", false);
                Call(form, "HideVideoControls");
                return 0;
            }
            var button = (Button)Get(form, "btn_Video_Play");
            int clicks = 0;
            button.Click += (sender, e) => clicks++;
            using var media = new Media(vlc, args[1], FromType.FromPath);
            player.Play(media);
            Await(() => player.IsPlaying && player.Length > 0, "The clip did not start");
            Click(button);
            Await(() => player.State == VLCState.Paused, "Play button did not pause playback");
            Click(button);
            Await(() => player.IsPlaying, "Play button did not resume playback");
            player.Time = player.Length - 350;
            Await(() => player.State == VLCState.Ended, "The clip did not reach its end");
            Set(form, "playback_Rate", 1.25f);
            Click(button);
            Await(() => player.IsPlaying, "Play button did not restart an ended clip");
            Assert(player.Time < 2000, "Replay did not start at the beginning");
            Assert((float)Get(form, "playback_Rate") == 1.25f, "Replay discarded the selected rate");
            Assert(clicks == 3, "The scene did not deliver all three pointer clicks");
            Click(button);
            Await(() => player.State == VLCState.Paused, "Replayed clip could not be paused");
            var originalBounds = form.Bounds;
            var enterCaption = FullScreenRow(form).Text;
            DoubleClick(surface);
            Assert((bool)Get(form, "is_Full_Screen_Mode"), "Double click did not enter full screen");
            Assert(FullScreenRow(form).Text != enterCaption, "Full-screen menu did not offer an explicit exit");
            DoubleClick(surface);
            Assert(!(bool)Get(form, "is_Full_Screen_Mode"), "Double click did not leave full screen");
            Assert(form.Bounds == originalBounds, "Leaving full screen did not restore the window bounds");
            Console.WriteLine("Double click entered and left full screen, restoring the window bounds.");

            Call(form, "ToggleSuperFullScreenMode");
            FullScreenRow(form).PerformClick();
            Assert(!(bool)Get(form, "is_Full_Screen_Mode") && !(bool)Get(form, "is_Super_Full_Screen_Mode"),
                "The menu did not leave full screen without panels");
            Assert(form.Bounds == originalBounds, "Menu exit did not restore the window bounds");

            form.WindowState = FormWindowState.Maximized;
            DoubleClick(surface);
            DoubleClick(surface);
            Assert(!(bool)Get(form, "is_Full_Screen_Mode") && form.WindowState == FormWindowState.Maximized,
                "Full-screen exit did not preserve the previous maximized state");
            form.WindowState = FormWindowState.Normal;

            player.Stop();
            using var hideMedia = new Media(vlc, args[1], FromType.FromPath);
            player.Play(hideMedia);
            Await(() => player.IsPlaying, "Could not resume for the auto-hide scene");
            var speedItems = (System.Collections.IEnumerable)Call(form, "BuildSpeedLadderItems");
            speedItems.Cast<ToolStripMenuItem>().Single(item => item.Text == "0.25×").PerformClick();
            Assert(Math.Abs(player.Rate - 0.25f) < 0.001f, "The quarter-speed menu item did not set VLC to 0.25x");
            Assert(((Button)Get(form, "btn_Video_Speed")).Text == "0.25×", "Speed button did not show 0.25x");
            Console.WriteLine("The speed menu selected 0.25x in VLC and in the transport.");
            Call(form, "ShowVideoControls");
            var barHost = (Form)Get(form, "video_Chrome_Host");
            // Windows can report mouse moves after layout/painting with the pointer stationary.
            for (int i = 0; i < 45; i++)
            {
                Call(form, "Vlc_Video_View_MouseMove", surface, new MouseEventArgs(MouseButtons.None, 0, surface.Width / 2, surface.Height / 2, 0));
                Pump(100);
            }
            Assert(!barHost.Visible, "Transport did not hide after four seconds under a stationary pointer");
            Console.WriteLine("Transport hid after four seconds despite repeated stationary mouse messages.");
            Cursor.Position = surface.PointToScreen(new Point(surface.Width / 2 + 20, surface.Height / 2));
            Call(form, "Vlc_Video_View_MouseMove", surface, new MouseEventArgs(MouseButtons.None, 0, surface.Width / 2 + 20, surface.Height / 2, 0));
            Assert(barHost.Visible, "A real pointer move did not reveal the transport");
            Cursor.Position = button.PointToScreen(new Point(button.Width / 2, button.Height / 2));
            Call(form, "Video_Controls_Hide_Timer_Tick", null, EventArgs.Empty);
            Assert(barHost.Visible, "Transport hid under the pointer");
            Cursor.Position = surface.PointToScreen(new Point(surface.Width / 2, surface.Height / 2));
            player.Pause();
            Await(() => player.State == VLCState.Paused, "Could not pause for the auto-hide scene");
            Call(form, "Video_Controls_Hide_Timer_Tick", null, EventArgs.Empty);
            Assert(barHost.Visible, "Transport hid while paused with the keep-visible preference");

            Call(form, "ToggleFullScreenMode");
            Call(form, "ShowVideoContextMenu", form.PointToScreen(new Point(100, 100)), false, int.MinValue);
            Pump(100);
            using (var screenshot = new Bitmap(form.Width, form.Height))
            {
                using (var graphics = Graphics.FromImage(screenshot))
                    graphics.CopyFromScreen(form.Location, Point.Empty, form.Size);
                screenshot.Save(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(args[1]), "video-fullscreen-menu.png"));
            }
            typeof(Main_Form).Assembly.GetType("fmsl.ChromeMenu").GetMethod("DismissPopup").Invoke(null, null);
            FullScreenRow(form).PerformClick();
            CheckFramePlayback(form, player, vlc, args[2]);
            player.Stop();
            Set(form, "is_Vlc_Playing", false);
            Call(form, "HideVideoControls");
            Console.WriteLine("Pointer pause, resume, replay from start and pause after replay passed.");
            return 0;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree("Software\\VB and VBA Program Settings\\" + profileName, false);
        }
    }
}
