using System.Reflection;
using Manager = FastMediaSorterCompanion;

// Specification 267: real-window recipes for the ManagerA help topics (Share Manager main window and its dialogs).
// Safety: nothing here starts the worker, a service or any process, opens a port, reaches a network host, reads or writes
// the clipboard, signs in or elevates. The worker is replaced by an in-memory WorkerStatus; where a form would query it
// on Shown, the worker payload is kept absent so the query returns at once. Files exist only under Path.GetTempPath()
// and are only created, never deleted or moved. Every address is a documentation address, every name a sample.
static partial class Topics
{
    const string SampleLan = "192.0.2.15";
    const string SampleExternal = "198.51.100.24";
    const string SampleRouter = "192.0.2.1";

    // --- shared helpers (the MA prefix keeps them apart from the other groups' partials) -------------------------------

    static object MAGet(object target, string name) => target.GetType().GetField(name, Program.Instance).GetValue(target);
    static void MASet(object target, string name, object value) => target.GetType().GetField(name, Program.Instance).SetValue(target, value);
    static object MACall(object target, string name, params object[] args) => target.GetType().GetMethod(name, Program.Instance).Invoke(target, args);

    /// <summary>The zero-byte stand-in the worker-presence check looks for. It is never started.</summary>
    static string MAStandInDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FmsHelpCapture", "standin");
        Directory.CreateDirectory(directory);
        var exe = Path.Combine(directory, "fms-share-worker.exe");
        if (!File.Exists(exe)) File.WriteAllBytes(exe, Array.Empty<byte>());
        return directory;
    }

    /// <summary>
    /// Presets the process-wide server gate (instead of reading this machine's service, marker file or registry) and,
    /// when asked, makes the worker payload look present. Both are reverted when the form is disposed.
    /// </summary>
    static T MAMake<T>(bool gateOn, bool workerPresent, Func<T> create) where T : Form
    {
        var features = typeof(Manager.ServerFeatures);
        features.GetField("_cached", Program.Static).SetValue(null, gateOn);
        features.GetField("_cachedHostMode", Program.Static).SetValue(null,
            gateOn ? Manager.ServerFeatures.ServerHostMode.UserSession : Manager.ServerFeatures.ServerHostMode.None);
        Manager.WorkerProcess.ExeDirectoryOverride = workerPresent ? MAStandInDirectory() : null;
        var form = create();
        form.Disposed += (_, _) =>
        {
            Manager.WorkerProcess.ExeDirectoryOverride = null;
            Manager.ServerFeatures.Refresh();
        };
        return form;
    }

    static string[] MAFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "FmsHelpCapture");
        var holiday = Path.Combine(root, "Holiday 2026");
        var music = Path.Combine(root, "Music");
        Directory.CreateDirectory(holiday);
        Directory.CreateDirectory(music);
        // Per-folder options live in the isolated registry profile the harness created.
        Manager.ShareRootParamsStore.SetFor(holiday, new Manager.ShareRootParams
        {
            Label = "Holiday 2026", Profile = "photo_storage", MediaTypes = new List<string> { "image", "video" },
            SoftReadOnly = false, SlideshowInterval = 8,
            Comment = "Photos and clips from the trip", AccessPin = "4821",   // fake PIN, sample comment
        });
        Manager.ShareRootParamsStore.SetFor(music, new Manager.ShareRootParams
        {
            Label = "Music", Profile = "audio_library", IsReadOnly = true,
        });
        return new[] { holiday, music };
    }

    /// <summary>A running share with two sample folders. forward = the router still needs a manual port forward.</summary>
    static Manager.WorkerStatus MAStatus(bool forward = false)
    {
        var folders = MAFolders();
        return new Manager.WorkerStatus
        {
            Running = true, ListenPort = 2222, DesiredPort = 2222, PortSupported = true,
            Username = "demo", Password = "Demo-Pass-0000",
            Fingerprint = "SHA256:ZGVtby1maW5nZXJwcmludC1zYW1wbGUtMDAwMDAwMDA",
            Capabilities = new Dictionary<string, bool>(),
            Roots = new List<Manager.ShareFolder>
            {
                new Manager.ShareFolder { name = "Holiday 2026", hostPath = folders[0] },
                new Manager.ShareFolder { name = "Music", hostPath = folders[1], readOnly = true },
            },
            Reachability = new Manager.WorkerReachability
            {
                LanAddress = SampleLan, LanMdnsActive = true,
                ExternalHost = SampleExternal, ExternalPort = forward ? 0 : 2222,
                PortMapMethod = forward ? "" : "UPnP", ExternalVerified = !forward,
                ExternalPortChecked = !forward, ExternalPortOpen = !forward,
            },
            Stats = new Manager.WorkerStats
            {
                TotalConnections = 12, ConnectionsSinceStart = 3, FilesServedTotal = 348, FilesServedSinceStart = 41,
                FirstConnectionAt = "2026-09-02T18:30:00Z", LastConnectionAt = "2026-10-25T09:12:00Z",
                LastConnectionAddress = "192.0.2.77",
            },
        };
    }

    // --- scripted ports for the two wizards that take them -------------------------------------------------------------

    sealed class MAResourcePorts : Manager.IResourceAddActions
    {
        public bool CanPickDisks => false;
        public string Pick(Form owner, bool disk) => @"D:\Demo\Holiday 2026";
        public bool Duplicate(string path) => false;
        public void WarnNetwork(Form owner, string path) { }
        public Manager.ShareRootParams Parameters(string path) => new()
        {
            Profile = "photo_storage", MediaTypes = new List<string> { "image", "video" },
        };
        public string FolderName(string path) => "Holiday 2026";
        public Task<Manager.ResourceAddResult> CommitAsync(Form owner, string path, Manager.ShareRootParams value, bool exchange, bool publish)
            => throw new InvalidOperationException("The capture never commits.");
        public void Pair(Form owner) => throw new InvalidOperationException("The capture never pairs.");
        public void Settings(Form owner) { }
    }

    sealed class MAExchangePorts : Manager.IExchangeFirstRunActions
    {
        public Manager.ExchangeSnapshot Snapshot;
        public Task<Manager.ExchangeSnapshot> ReadAsync() => Task.FromResult(Snapshot);
        public Task<Manager.ExchangeProcess.ListenOutcome> SetListenAsync(string value) => throw new InvalidOperationException("The capture never writes.");
        public Task<Manager.ExchangeProcess.ConfigOutcome> SetRangeAsync(string value) => throw new InvalidOperationException("The capture never writes.");
        public Task<Manager.ExchangeProcess.ConfigOutcome> SetEndpointAsync(string value) => throw new InvalidOperationException("The capture never writes.");
        public Task<Manager.ExchangeElevation.ElevatedOutcome> FirewallAsync() => throw new InvalidOperationException("The capture never elevates.");
        public Task<Manager.ServiceControl.ManageResult> ServiceAsync() => throw new InvalidOperationException("The capture never elevates.");
        public Manager.ExchangeWizardResult Account(Form owner, string login, bool typed, bool enroll) => throw new InvalidOperationException("The capture never signs in.");
        public string Suggest(int port) => SampleLan + ":" + port;
        public void ConnectionString(Form owner) => throw new InvalidOperationException("The capture never opens dialogs.");
    }

    // --- the recipes ---------------------------------------------------------------------------------------------------

    static IEnumerable<Topic> ManagerA() => new Topic[]
    {
        new Topic("share-main", false, () =>
            {
                // The Clipboard button is drawn only when the exchange client holds an account, which the stores answer from
                // this PC. Preset the process-wide coordinator so the picture does not depend on the machine; nothing is sent.
                var current = typeof(Manager.ClipboardCoordinator).GetField("_current", Program.Static);
                current.SetValue(null, new Manager.ClipboardCoordinator(new Manager.ClipboardExchange(), () => true));
                var window = MAMake(true, true, () => new Manager.MainWindow());
                window.Disposed += (_, _) => current.SetValue(null, null);
                return window;
            },
            "sharing is on: two sample folders (one read-only), phone access rows filled, port opened automatically (UPnP); no worker contacted",
            Prepare: form =>
            {
                MASet(form, "_entered", true);          // skip the Shown handler that would start and query the worker
                // While _loading is set the list's check events are ignored. Left set on purpose: a ticked row would
                // otherwise ask the window to push the folder list to the worker.
                MASet(form, "_loading", true);
            },
            Settle: async form =>
            {
                var status = MAStatus();
                MACall(form, "LoadLocalState");
                MACall(form, "ApplyGate");
                MASet(form, "_status", status);
                MACall(form, "PopulateFolders", status.Roots);
                MASet(form, "_listPopulated", true);
                MACall(form, "ApplyStatusToUi");
                MACall(form, "SetAllSectionsExpanded", true);   // the "expand all" arrow: phone access and internet access open
                await Task.Delay(700);                   // the folder access walk answers on a worker thread
                // The list shows the real temp path; replace the displayed text with sample paths (the row tags keep the real ones).
                var list = (ListView)MAGet(form, "lvFolders");
                var shown = new[] { @"D:\Demo\Holiday 2026", @"D:\Demo\Music" };
                for (var i = 0; i < list.Items.Count && i < shown.Length; i++) list.Items[i].SubItems[2].Text = shown[i];
                // A selected row is drawn without its check box (MainWindow.Folders.vb OnFolderDrawSubItem), so nothing is
                // selected in the picture and every row shows whether it is served.
                list.SelectedItems.Clear();
                // With both sections open the content is a little taller than the window: give it room so no scroll bar is drawn.
                var area = Screen.FromControl(form).WorkingArea;
                form.Height = Math.Min(form.Height + 70, area.Height);
                form.Top = area.Top + (area.Height - form.Height) / 2;
                await Task.Delay(400);
            }),

        new Topic("manager-welcome", false, () =>
            {
                Manager.MainWindow host = null;
                var welcome = MAMake(false, false, () => new Manager.Manager_Welcome_Form(host = new Manager.MainWindow()));
                welcome.Disposed += (_, _) => host.Dispose();   // the host window is never shown
                return welcome;
            },
            "first-folder wizard, step 1 of 6 (introduction); server off; host window never shown"),

        new Topic("server-welcome", false, () =>
            {
                var ports = new MAExchangePorts
                {
                    Snapshot = new Manager.ExchangeSnapshot
                    {
                        CanChangeFirewall = true,
                        Report = new Manager.ExchangeProcess.InspectReport { Answered = true, Listen = ":22022", PortRange = "40000-40100" },
                    },
                };
                return MAMake(false, false, () => new Manager.Server_Welcome_Form(Manager.WelcomeHalf.Exchange, ports, ports.Snapshot));
            },
            "Media Exchange Server first-run wizard, step 1 (statement); scripted ports, nothing written"),

        new Topic("package-wizard", false, () => MAMake(false, false, () => new Manager.PackageWizardForm(null)),
            "recipient table with two sample folders and the QR code of a sample UPnP share; the worker is not queried",
            Settle: async form =>
            {
                // The form asks the worker on Shown; with no worker payload that returned at once. The sample state goes in here.
                MASet(form, "_status", MAStatus());
                MACall(form, "PopulateGrid");
                MACall(form, "FitToGrid");
                MACall(form, "Rebuild");
                // The access check of the two sample folders (a bounded read-only walk); the QR button turns on when it is done.
                var qr = (Button)MAGet(form, "btnShowQr");
                for (var i = 0; i < 40 && !qr.Enabled; i++) await Task.Delay(150);
                // A finished check leaves the "Checking folder access.." line behind; clear it, as the product shows no news.
                MACall(form, "SetHint", "");
                var grid = (DataGridView)MAGet(form, "dgv");
                grid.CurrentCell = null;                 // no cell in edit mode: the first check box is drawn with its value
                grid.ClearSelection();
                await Task.Delay(300);
            }),

        new Topic("share-resource-add", false,
            () => MAMake(false, false, () => new Manager.Resource_Add_Form(new MAResourcePorts(), Manager.ResourceAddEntry.AddButton)),
            "Add Shared Resource wizard on the questions step for the sample folder Holiday 2026; scripted ports, nothing added",
            Settle: async form =>
            {
                MACall(form, "AcceptPick", @"D:\Demo\Holiday 2026");
                MACall(form, "ShowStep", Manager.ResourceAddStep.Questions);
                await Task.Delay(300);
            }),

        new Topic("internet-access", false, () => MAMake(false, false, () => new Manager.InternetAccessForm(MAStatus(forward: true))),
            "external address known, router not forwarding: the numbered instruction; Test and the guide link not pressed",
            Settle: async form =>
            {
                await Task.Delay(100);
                // The instruction names this PC's real router; show the sample router address instead.
                var box = (TextBox)MAGet(form, "txtForward");
                var gateway = Manager.NetworkInfo.DefaultGatewayIp();
                if (!string.IsNullOrEmpty(gateway)) box.Text = box.Text.Replace(gateway, SampleRouter);
            }),

        new Topic("share-enable", false, () => MAMake(false, true, () => new Manager.Share_Enable_Form()),
            "the opt-in dialog before the one administrator prompt; Enable not pressed"),

        new Topic("share-status", false, () => MAMake(false, false, () => new Manager.Share_Status_Form(MAStatus())),
            "sharing on, port 2222, sample counters; Reset and Refresh not pressed"),

        new Topic("share-access-test", false, () => MAMake(false, false, () => new Manager.Share_Access_Test_Form(SampleExternal, 2222)),
            "result view of the test from this PC (reachable); the probe is skipped, no connection is made",
            Prepare: form => MASet(form, "_probeStarted", true),
            Settle: async form =>
            {
                var endpoint = SampleExternal + ":2222";
                const Manager.SftpProbe.ProbeResult outcome = Manager.SftpProbe.ProbeResult.SshOk;
                var headline = (Label)MAGet(form, "_headline");
                headline.Text = Manager.ShareText.AccessTestResultLine(outcome, endpoint);
                headline.ForeColor = Manager.Share_Access_Test_Form.HeadlineColor(outcome);
                ((Label)MAGet(form, "_detail")).Text = Manager.ShareText.AccessTestDetail(outcome);
                await Task.Delay(500);                   // the auto-sized dialog grows to the result text
            }),

        new Topic("share-root-params", false, () => MAMake(false, false, () => new Manager.Share_Root_Params_Form("Holiday 2026",
                new Manager.ShareRootParams
                {
                    Label = "Holiday photos", Profile = "photo_storage", MediaTypes = new List<string> { "image", "video" },
                    ScanSubdirectories = true, Comment = "Photos and clips from the trip", AccessPin = "4821", SlideshowInterval = 8,
                })),
            "options of the sample folder Holiday 2026: photo storage, images and video, fake PIN, 8 second slideshow",
            Settle: async form =>
            {
                ((TextBox)MAGet(form, "txtLabel")).SelectionLength = 0;   // the first field opens with its text selected
                await Task.Delay(150);
            }),
    };
}
