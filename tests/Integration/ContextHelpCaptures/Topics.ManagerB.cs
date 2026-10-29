using System.Reflection;
using Manager = FastMediaSorterCompanion;

// Specification 267: real-window recipes for the ManagerB help topics (.fdd disks, broadcasts, scheduled operations,
// outside access, phone code intake).
// Safety: nothing here starts the worker, a service or any process, opens a port, reaches a network host, reads or writes
// the clipboard, opens a camera, signs in or elevates. The worker is replaced by in-memory answers (disks) or kept absent
// (broadcasts); the scheduled engine is a scratch one that is never started; files exist only under Path.GetTempPath()
// and are only created. Every name is a sample, every host a documentation host.
static partial class Topics
{
    static IEnumerable<Topic> ManagerB() => new Topic[]
    {
        new Topic("share-disks", false, ManagerBHelp.DisksForm,
            "three sample .fdd disks (open, closed read-only, open under the Windows service); the first row selected; no worker contacted",
            Settle: async form => await Task.Delay(400)),

        new Topic("share-broadcasts", false, ManagerBHelp.BroadcastsForm,
            "gateway on (port 8765) with three sample broadcasts: a folder (selected), a file with no expiry, an expired file; worker kept absent",
            Settle: ManagerBHelp.SettleBroadcasts),

        new Topic("share-broadcast-create", false, ManagerBHelp.BroadcastCreateForm,
            "folder broadcast dialog for a sample folder; nothing sent until Broadcast is pressed",
            Prepare: ManagerBHelp.MaskBroadcastPath),

        new Topic("scheduled-operation", false, ManagerBHelp.OperationForm,
            "editor of a sample copy operation (Holiday 2026 backup) with the whole body visible down to the schedule; the file-selection conditions are open when the display is tall enough, otherwise folded; nothing saved or previewed",
            Settle: ManagerBHelp.SettleOperation),

        new Topic("scheduled-history", false, ManagerBHelp.HistoryForm,
            "scratch journal with sample runs and notes: ok, failed, manual, missed, late, catch-up, wall, delete to the Recycle Bin; window widened so the Result sentences are not cut",
            Settle: ManagerBHelp.SettleHistory),

        new Topic("scheduled-resource-picker", false, ManagerBHelp.ResourcePickerForm,
            "two sample resources (SFTP and network folder) seeded into the isolated profile; first one selected"),

        new Topic("folder-outside-access", false,
            () => new Manager.Folder_Outside_Access_Form("Holiday 2026", @"D:\Demo\Holiday 2026", null),
            "sample shared folder with no publishing service: both groups show their reason, nothing can be created"),

        new Topic("phone-code-intake", false, () => new Manager.Phone_Code_Intake_Form(),
            "intake window with a masked sample code typed into the field; no clipboard, file or camera used",
            Prepare: form => ManagerBHelp.Set(form, "_codeBox", c => ((TextBox)c).Text = "FMSDEMO-0000-0000")),
    };
}

/// <summary>Shared helpers of the ManagerB recipes. The prefix keeps them apart from the other groups' partials.</summary>
static class ManagerBHelp
{
    internal static string Scratch(string name)
    {
        var folder = Path.Combine(Path.GetTempPath(), "fms267-help", name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    internal static object Get(object target, string name) =>
        target.GetType().GetField(name, Program.Instance).GetValue(target);

    internal static void Set(object target, string name, Action<object> action) => action(Get(target, name));

    internal static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, Program.Instance).SetValue(target, value);

    internal static object Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Program.Instance).Invoke(target, args);

    internal static IEnumerable<Control> Walk(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var deeper in Walk(child)) yield return deeper;
        }
    }

    // --- share-disks -------------------------------------------------------------------------------------------------

    internal static Form DisksForm()
    {
        var status = new Manager.WorkerStatus
        {
            Running = true, Mode = "user",
            Capabilities = new Dictionary<string, bool> { ["disk-sharing"] = true, ["disk-autostart"] = true },
        };
        var disks = new List<Manager.SharedDisk>
        {
            new() { ContainerId = "demo-1", RootName = "Holiday 2026", ContainerPath = @"D:\Demo\Holiday 2026.fdd", State = "open",
                    Holder = "session", Autostart = true, OpenHandles = 2 },
            new() { ContainerId = "demo-2", RootName = "Archive 2025", ContainerPath = @"D:\Demo\Archive 2025.fdd", State = "closed",
                    Holder = "none", Autostart = false, ReadOnly = true },
            new() { ContainerId = "demo-3", RootName = "Work notes", ContainerPath = @"E:\Demo\Work notes.fdd", State = "open",
                    Holder = "service", Autostart = true },
        };
        // The Friend constructor: the real (parameterless) one would query the worker and reconcile this PC's drive aliases.
        return new Manager.Share_Disks_Form(
            () => Task.FromResult(status),
            () => Task.FromResult(new Manager.WorkerResponse { Ok = true, SchemaVersion = 2, Disks = disks }),
            _ => { });
    }

    // --- share-broadcasts --------------------------------------------------------------------------------------------

    /// <summary>
    /// The page asks the worker on Shown and every 5 s, and has no seam for it. A scratch folder with no worker exe makes
    /// WorkerProcess.IsAvailable() False, so the first refresh returns at once without contacting or starting anything. The
    /// sample state is then set by hand in SettleBroadcasts, and the override is put back there and again when the form is
    /// disposed (whichever comes first), so it cannot reach the next topic.
    /// </summary>
    internal static Form BroadcastsForm()
    {
        var prior = Manager.WorkerProcess.ExeDirectoryOverride;
        var none = Path.Combine(Path.GetTempPath(), "fms267-help", "no-worker");
        Directory.CreateDirectory(none); // an empty folder: it never holds fms-share-worker.exe
        Manager.WorkerProcess.ExeDirectoryOverride = none;
        try
        {
            var form = new Manager.Share_Broadcasts_Form();
            form.Disposed += (_, _) => Manager.WorkerProcess.ExeDirectoryOverride = prior;
            _broadcastsPrior = prior;
            return form;
        }
        catch
        {
            Manager.WorkerProcess.ExeDirectoryOverride = prior;
            throw;
        }
    }

    static string _broadcastsPrior;

    internal static async Task SettleBroadcasts(Form form)
    {
        try
        {
            // 1. The first (offline) refresh has ended.
            for (var i = 0; i < 60 && (bool)Get(form, "_refreshInFlight"); i++) await Task.Delay(50);
            // 2. No later refresh may run: stop the timer before anything else.
            ((System.Windows.Forms.Timer)Get(form, "_refreshTimer")).Stop();
            // 3. Tick the box while the page is still degraded: its handler returns at once then (it would call the worker otherwise).
            ((CheckBox)Get(form, "chkEnableGateway")).Checked = true;
            var status = new Manager.WorkerStatus
            {
                Running = true, Mode = "user", BroadcastSupported = true, BroadcastEnabled = true, BroadcastPort = 8765,
                Capabilities = new Dictionary<string, bool>(),
            };
            var broadcasts = new List<Manager.WorkerBroadcast>
            {
                new() { Id = "b1", Token = "DEMO0123456789", Kind = "folder", Title = "Holiday 2026", ItemCount = 48,
                        CreatedAt = "2026-10-25T18:30:00Z", ExpiresAt = "2026-10-26T18:30:00Z", Hits = 12 },
                new() { Id = "b2", Token = "DEMO9876543210", Kind = "file", Title = "Demo clip", ItemCount = 1,
                        CreatedAt = "2026-10-24T09:00:00Z", ExpiresAt = "", Hits = 3 },
                new() { Id = "b3", Token = "DEMO5555555555", Kind = "file", Title = "Old trailer", ItemCount = 1,
                        CreatedAt = "2026-10-20T20:15:00Z", ExpiresAt = "2026-10-21T20:15:00Z", Hits = 40, Expired = true },
            };
            SetField(form, "_status", status);
            SetField(form, "_broadcasts", broadcasts);
            Call(form, "SetDegraded", false);
            Call(form, "ShowNotice", "");
            var label = (Label)Get(form, "lblGatewayStatus");
            label.Text = Manager.Localization.TF("Шлюз трансляций включён (порт {0})", 8765);
            label.ForeColor = Color.DarkGreen;
            Call(form, "PopulateListView");
            ((ListView)Get(form, "lvBroadcasts")).Items[0].Selected = true;
            await Task.Delay(100);
        }
        finally { Manager.WorkerProcess.ExeDirectoryOverride = _broadcastsPrior; }
    }

    // --- scheduled-operation -----------------------------------------------------------------------------------------

    internal static Form OperationForm()
    {
        var engine = NewEngine(Scratch("scheduled-operation"));
        var op = new Manager.ScheduledOperation
        {
            Name = "Holiday 2026 backup", Enabled = true, Action = Manager.ScheduledAction.Copy,
            Source = new Manager.ScheduledLocation { Path = @"D:\Demo\Holiday 2026", Recursive = true },
            Destination = new Manager.ScheduledLocation { Path = @"E:\Demo\Backup" },
            Conflict = Manager.ScheduledConflict.KeepBoth,
            Conditions = new Manager.ScheduledConditions
            {
                TypeMask = Manager.ScheduledTypeMask.Images | Manager.ScheduledTypeMask.Video,
                TimeFilter = Manager.ScheduledTimeFilter.SinceLastRun, ExcludeMask = "*.tmp", MinSizeBytes = 1024L * 1024L,
            },
            AnchorMinutes = 180, IntervalMinutes = 1440, StartDate = new DateTime(2026, 10, 26),
            RetryMissedRunsAtStart = true, ShowBalloonOnError = true,
        };
        return new Manager.Scheduled_Operation_Form(engine, op);
    }

    /// <summary>The editor is 640 x 640 with a scrolling body; the window is made as tall as the body (and the screen allows).</summary>
    internal static async Task SettleOperation(Form form)
    {
        // A condition that is set opens the group while the window is built; fold it and open it again the way a click does.
        Call(form, "SetConditionsOpen", false);
        await Task.Delay(100);
        Call(form, "SetConditionsOpen", true);
        await Task.Delay(150);
        var scroller = Walk(form).OfType<Panel>().First(p => p.AutoScroll);
        var page = scroller.Controls[0];
        var chrome = form.Height - form.ClientSize.Height;
        var area = Screen.FromControl(form).WorkingArea;
        var cap = area.Height - chrome - 16;
        var rowHeight = form.Controls.Cast<Control>().Where(c => c.Dock == DockStyle.Bottom).Select(c => c.Height).DefaultIfEmpty(0).Max();
        // The body re-flows when the window grows, so measure again after each step.
        async Task<int> Fit()
        {
            var wanted = 0;
            for (var pass = 0; pass < 3; pass++)
            {
                wanted = Math.Max(page.Height, page.PreferredSize.Height) + rowHeight + 8;
                form.ClientSize = new Size(form.ClientSize.Width, Math.Min(wanted, cap));
                form.PerformLayout();
                await Task.Delay(200);
            }
            return wanted;
        }
        // The whole body must be visible (the schedule block is below the conditions): when the open conditions panel does not
        // fit this display, it stays folded - the way the editor opens for an untouched definition.
        if (await Fit() > cap)
        {
            Call(form, "SetConditionsOpen", false);
            await Task.Delay(150);
            await Fit();
        }
        form.Location = new Point(form.Left, Math.Max(area.Top, area.Top + (area.Height - form.Height) / 2));
        await Task.Delay(150);
    }

    // --- share-broadcast-create --------------------------------------------------------------------------------------

    static string _broadcastFolder = "";

    internal static Form BroadcastCreateForm()
    {
        _broadcastFolder = Scratch("Holiday 2026");
        return new Manager.Broadcast_Create_Form(_broadcastFolder, new Manager.ShareSettings());
    }

    /// <summary>The path label shows the scratch folder; a sample path is shown instead of this machine's temp folder.</summary>
    internal static void MaskBroadcastPath(Form form)
    {
        foreach (var label in Walk(form).OfType<Label>())
            if (label.Text == _broadcastFolder) label.Text = @"D:\Demo\Holiday 2026";
    }

    // --- scheduled-history -------------------------------------------------------------------------------------------

    internal static Form HistoryForm()
    {
        // One scratch folder; its history file is emptied first (overwritten, never deleted), because the journal appends and
        // the rows would otherwise pile up across the captures.
        var folder = Scratch("scheduled-history");
        File.WriteAllText(Path.Combine(folder, "history.log"), "");
        var engine = NewEngine(folder);
        var journal = new Manager.ScheduledJournal(folder);
        var day = new DateTime(2026, 10, 26);
        void Run(int hour, int minute, string code, string trigger, string name, int count, int failed = 0, int skipped = 0, string verdict = "", string detail = "") =>
            journal.Append(new Manager.JournalEntry
            {
                Timestamp = day.AddHours(hour).AddMinutes(minute), Kind = "run", Code = code, OperationId = "op-" + name.GetHashCode(),
                OperationName = name, Trigger = trigger, Count = count, Failed = failed, Skipped = skipped,
                Bytes = count * 4_000_000L, DurationMs = 8000, Verdict = verdict, Detail = detail,
            });
        void Note(int hour, int minute, string code, string name, string detail) =>
            journal.Append(new Manager.JournalEntry
            {
                Timestamp = day.AddHours(hour).AddMinutes(minute), Kind = "note", Code = code, OperationId = "op-" + name.GetHashCode(),
                OperationName = name, Detail = detail,
            });
        // Oldest first; the form lists the newest first.
        Run(3, 0, "ok", "schedule", "Holiday 2026 backup", 48, skipped: 2);
        Run(3, 5, "ok", "schedule", "Clean Demo downloads", 17, verdict: "recycle");
        Run(9, 20, "failed", "schedule", "Holiday 2026 backup", 46, failed: 2, detail: "locked|IMG_0412.jpg");
        Run(9, 40, "ok", "runNow", "Holiday 2026 backup", 2);
        Note(10, 5, "missed", "Music sync", "2026-10-25T03:00:00");
        Note(10, 30, "late", "Music sync", "2026-10-26T10:00:00");
        Run(10, 31, "ok", "catchUp", "Music sync", 12);
        Run(11, 0, "wall", "schedule", "Photo archive", 0, detail: "destination-unreachable|E:\\Demo\\Backup");
        return new Manager.Scheduled_History_Form(engine);
    }

    /// <summary>The columns keep their authored widths, so a long sentence is cut: the window is widened (it is sizable) and the
    /// Result column takes the extra width, as a person would by dragging.</summary>
    internal static async Task SettleHistory(Form form)
    {
        var list = (ListView)Get(form, "list");
        var area = Screen.FromControl(form).WorkingArea;
        form.Width = Math.Min((int)(form.Width * 1.5), area.Width - 40);
        form.Location = new Point(area.Left + (area.Width - form.Width) / 2, form.Top);
        await Task.Delay(150);
        var others = list.Columns[0].Width + list.Columns[1].Width + list.Columns[2].Width;
        list.Columns[3].Width = Math.Max(list.Columns[3].Width, list.ClientSize.Width - others - 4);
        await Task.Delay(150);
    }

    /// <summary>A scratch engine over a scratch folder. Never started: no timer is armed, no catch-up run happens.</summary>
    internal static Manager.ScheduledEngine NewEngine(string folder) =>
        new(new Manager.ScheduledStore(folder), new Manager.ScheduledJournal(folder),
            new Manager.ScheduledServices { Clock = () => new DateTime(2026, 10, 26, 12, 0, 0) }, null);

    // --- scheduled-resource-picker -----------------------------------------------------------------------------------

    internal static Form ResourcePickerForm()
    {
        // The resource list lives in the isolated registry profile of the harness; nothing is connected to.
        Manager.ResourceRegistry.Save(new List<Manager.ResourceDescriptor>
        {
            new() { Id = "demo-sftp-1", DisplayName = "Demo NAS", Kind = Manager.ResourceKind.Sftp, Host = "nas.example.com",
                    Port = 22, UserName = "demo", RootPath = "/Photos" },
            new() { Id = "demo-smb-1", DisplayName = "Office share", Kind = Manager.ResourceKind.Smb,
                    RootPath = @"\\files.example.com\Demo" },
        });
        return new Manager.Scheduled_Resource_Picker_Form(new Manager.ScheduledLocation());
    }
}
