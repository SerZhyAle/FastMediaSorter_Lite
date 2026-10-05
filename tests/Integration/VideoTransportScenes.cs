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

    static FieldInfo Field(string name) => typeof(Main_Form).GetField(name, Flags) ?? typeof(Main_Form).GetField("_" + name, Flags);
    static object Get(Main_Form form, string name) => Field(name).GetValue(form);
    static void Set(Main_Form form, string name, object value) => Field(name).SetValue(form, value);
    static object Call(Main_Form form, string name, params object[] args) =>
        typeof(Main_Form).GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(form, args);
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Pump(int milliseconds)
    {
        long until = Environment.TickCount64 + milliseconds;
        do { Application.DoEvents(); Thread.Sleep(10); } while (Environment.TickCount64 < until);
    }
    static void Await(Func<bool> condition, string message)
    {
        long until = Environment.TickCount64 + 3000;
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

    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Core.Initialize(args[0]);
            using var vlc = new LibVLC("--no-audio", "--vout=dummy", "--no-video-title-show");
            using var player = new MediaPlayer(vlc);
            using var form = new TransportSceneWindow { TopMost = true, StartPosition = FormStartPosition.Manual,
                Location = new Point(20, 20), ClientSize = new Size(600, 350) };
            Call(form, "BuildModernLayout");
            form.Show(); form.Activate();
            Set(form, "libVlc", vlc);
            Set(form, "vlc_Media_Player", player);
            Set(form, "is_Vlc_Playing", true);
            Set(form, "current_Loaded_File_Name", args[1]);
            Call(form, "EnsureVideoControls"); Call(form, "ShowVideoControls");
            ((Form)Get(form, "video_Chrome_Host")).TopMost = true;
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
            player.Stop();
            Set(form, "is_Vlc_Playing", false);
            Call(form, "HideVideoControls");
            Console.WriteLine("Pointer pause, resume, replay from start and pause after replay passed.");
            return 0;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
    }
}
