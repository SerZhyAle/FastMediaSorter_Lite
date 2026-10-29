using System.Globalization;
using System.Reflection;
using Manager = FastMediaSorterCompanion;

// Specification 267: real-window recipes for the ViewerB help topics (resources, exchange account dialogs, diagnosis,
// batch report, secure-container password). Every recipe works on fictional data (example.com, 192.0.2.x, 198.51.100.x,
// Demo, Holiday 2026) inside the harness's isolated registry profile. None of them presses a button, starts a worker,
// opens a port, connects to a host, signs in, or touches the clipboard or a real secret store.
// resource-phone-code has no recipe: its constructor starts a TcpListener and probes the LAN route (see the report).
static partial class Topics
{
    static IEnumerable<Topic> ViewerB() => new Topic[]
    {
        new Topic("resource-edit", true,
            () => Program.ViewerForm("Resource_Edit_Form", ViewerBHelp.Sftp("Demo NAS", "nas.example.com", 22, "demo", "/Holiday 2026", false,
                "SHA256:DEMO+fingerprint+for+help+pictures+only00000"), null),
            "editing a saved SFTP resource (Demo NAS, nas.example.com:22, folder /Holiday 2026) with a remembered server key; password field empty = keep the stored one"),

        new Topic("resource-manager", true,
            () => ViewerBHelp.ResourceManager(),
            "resource list with four sample rows: SFTP Demo NAS (selected, read and write), SMB Office share, SFTP Backup server (read only), and one folder managed by the Share Manager; the My devices section is not shown",
            Settle: async form => await Task.Delay(200)),

        new Topic("resource-export", true,
            () => ViewerBHelp.ExportForm(),
            "export of the SFTP resource Demo NAS (server key remembered), with a stored sample password, so both choices are enabled and Without password is selected; nothing saved or copied"),

        new Topic("resource-credential", true,
            () => Program.ViewerForm("Resource_Credential_Form", "Demo NAS", "demo", false, true, false),
            "first password request for Demo NAS (user demo), a sample password typed (masked), Remember ticked, Show password off",
            Prepare: form => ViewerBHelp.SetText(form, "field", "demo-password")),

        new Topic("secure-password", true,
            () => Program.ViewerForm("Secure_Password_Form", "Holiday 2026.fd-sec", false),
            "first password request for the container Holiday 2026.fd-sec, a sample password typed (masked), remember-for-this-session ticked",
            Prepare: form => ViewerBHelp.SetText(form, "field", "demo-password")),

        new Topic("batch-report", true,
            () => Program.ViewerForm("Batch_Report_Form", ViewerBHelp.SampleReport()),
            "details of a cancelled Move run of 8 files: 2 failed (file in use, disk full), 2 not started, 1 skipped (already in the folder), 3 done; worst rows first; nothing selected"),

        new Topic("exchange-connect", true,
            () => Program.ViewerForm("Exchange_Connect_Form", "demo.example.com:8443", null, null),
            "adding an account from an exchange server: address demo.example.com:8443 typed, sample login and password filled (masked); Connect not pressed, nothing sent",
            Prepare: form =>
            {
                ViewerBHelp.SetText(form, "loginBox", "demo");
                ViewerBHelp.SetText(form, "passwordBox", "Demo-password-2026");
            }),

        new Topic("exchange-password-change", true,
            () => Program.ViewerForm("Exchange_Password_Change_Form", "Demo server (demo)"),
            "changing the password of the account Demo server (demo): old and new passwords filled with fake values (masked); the button is not pressed, nothing sent",
            Prepare: form =>
            {
                ViewerBHelp.SetText(form, "oldBox", "Old-demo-pass-1");
                ViewerBHelp.SetText(form, "newBox", "New-demo-pass-2026");
                ViewerBHelp.SetText(form, "repeatBox", "New-demo-pass-2026");
            }),

        new Topic("connection-diagnosis", true,
            () => ViewerBHelp.DiagnosisDialog(),
            "'Why doesn't it connect?' for a server on the home network, built offline (no probe runs): the walk stopped at the Windows public-network step, with its Open Windows settings button, Next step, and the Details box open showing the redacted code",
            Settle: async form =>
            {
                ((Button)ViewerBHelp.GetField(form, "_btnDetails")).PerformClick(); // only toggles a read-only code box
                await Task.Delay(150);
            }),
    };
}

// Reflection helpers of this group only: viewer (fmsl) types are Friend and not visible to the harness.
static class ViewerBHelp
{
    static object Instance(string typeName) => Program.ViewerNew(typeName);

    internal static void SetProp(object target, string name, object value)
    {
        var property = target.GetType().GetProperty(name, Program.Instance) ?? throw new MissingMemberException(target.GetType().Name, name);
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        property.SetValue(target, type.IsEnum && value is string text ? Enum.Parse(type, text) : value);
    }

    internal static object GetField(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Program.Instance | BindingFlags.DeclaredOnly);
            if (field != null) return field.GetValue(target);
        }
        throw new MissingMemberException(target.GetType().Name, name);
    }

    internal static void SetText(Form form, string field, string text) => ((Control)GetField(form, field)).Text = text;

    // A ResourceDescriptor of kind SFTP (Friend class, built by reflection).
    internal static object Sftp(string name, string host, int port, string user, string root, bool readOnly, string pin = "", DateTime? lastUsed = null)
    {
        var d = Instance("ResourceDescriptor");
        SetProp(d, "Id", Guid.NewGuid().ToString("N"));
        SetProp(d, "DisplayName", name);
        SetProp(d, "Kind", "Sftp");
        SetProp(d, "Host", host);
        SetProp(d, "Port", port);
        SetProp(d, "UserName", user);
        SetProp(d, "RootPath", root);
        SetProp(d, "ReadOnlyHint", readOnly);
        SetProp(d, "PinnedFingerprint", pin);
        SetProp(d, "LastUsedUtc", lastUsed);
        return d;
    }

    // A Smb resource: the UNC path is the root, no host or port (as Resource_Edit_Form saves it).
    static object Smb(string name, string unc, bool readOnly, DateTime? lastUsed = null)
    {
        var d = Instance("ResourceDescriptor");
        SetProp(d, "Id", Guid.NewGuid().ToString("N"));
        SetProp(d, "DisplayName", name);
        SetProp(d, "Kind", "Smb");
        SetProp(d, "UserName", "demo");
        SetProp(d, "RootPath", unc);
        SetProp(d, "ReadOnlyHint", readOnly);
        SetProp(d, "LastUsedUtc", lastUsed);
        return d;
    }

    const string DemoPin = "SHA256:DEMOdemoDEMOdemoDEMOdemoDEMOdemoDEMOdemo000";

    // The Resource Manager builds its list inside its constructor, from the isolated registry hive and from the worker's data
    // folder. So: (1) the hive is seeded with fictional rows, (2) FMS_COMPANION_DATA_DIR points at a temp folder holding one
    // sample shares.json, so the real worker folder is never read, (3) the exchange feature flags are switched off for this
    // flow, so the My devices panel (real registry entries, live sync) is never built. All three are undone on close.
    internal static Form ResourceManager()
    {
        var descriptor = Program.ViewerType("ResourceDescriptor");
        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(descriptor));
        list.Add(Sftp("Demo NAS", "nas.example.com", 22, "demo", "/Holiday 2026", false, DemoPin, DateTime.UtcNow.AddHours(-1)));
        list.Add(Smb("Office share", @"\\files.example.com\Demo", true, DateTime.UtcNow.AddDays(-3)));
        list.Add(Sftp("Backup server", "198.51.100.7", 2222, "backup", "/", true, DemoPin));
        var save = Program.ViewerType("ResourceRegistry").GetMethod("Save", Program.Static, new[] { typeof(IEnumerable<>).MakeGenericType(descriptor) });
        save.Invoke(null, new object[] { list });

        var dataDir = Path.Combine(Path.GetTempPath(), "fms-help-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "shares.json"),
            "[{\"name\":\"Family photos\",\"hostPath\":\"C:\\\\Users\\\\Public\\\\Pictures\",\"readOnly\":false}]");
        const string variable = "FMS_COMPANION_DATA_DIR";
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, dataDir);
        var gate = (IDisposable)Program.ViewerType("ExchangeFeatures").GetMethod("ForFlow", Program.Static)
            .Invoke(null, new object[] { false, false, false });
        Form form;
        try { form = Program.ViewerForm("Resource_Manager_Form", null, null, null, null); }
        catch { Restore(); throw; }
        form.FormClosed += (_, _) => Restore();
        return form;

        void Restore()
        {
            gate.Dispose();
            Environment.SetEnvironmentVariable(variable, previous);
            try { Directory.Delete(dataDir, true); } catch (IOException) { }
        }
    }

    // The export dialog reads the stored password back (in memory) to enable "With password"; the blob goes into the isolated hive.
    internal static Form ExportForm()
    {
        var resource = Sftp("Demo NAS", "nas.example.com", 22, "demo", "/Holiday 2026", false, DemoPin);
        const string handle = "demo-handle";
        SetProp(resource, "SecretHandle", handle);
        var store = Program.ViewerNew("RegistryResourceSecretStore");
        var blob = fmsl.DpapiSecretChecks.ProtectChecked("demo-password").Value;
        store.GetType().GetMethod("PutBlob").Invoke(store, new object[] { handle, blob });
        return Program.ViewerForm("Resource_Export_Form", resource, store);
    }

    // The dialog is Public; its result is built by hand, so none of the engine's probes (TCP connect, adapter and registry reads) runs.
    internal static Form DiagnosisDialog()
    {
        var message = fmsl.ConnectionDiagnosisMessages.UnreachableServer;
        var result = new fmsl.ConnectionDiagnosisResult(message, fmsl.ConnectionDiagnosisTarget.Server, fmsl.ConnectionDiagnosisApplies.Network)
        {
            WalkedSteps = fmsl.ConnectionDiagnosisEngine.ApplicableSteps(message, fmsl.ConnectionDiagnosisTarget.Server, fmsl.ConnectionDiagnosisApplies.Network)
        };
        result.StepOutcomes[fmsl.ConnectionDiagnosisStep.PcAsleep] = fmsl.ProbeOutcome.Passed;
        result.StepOutcomes[fmsl.ConnectionDiagnosisStep.AddressLan] = fmsl.ProbeOutcome.Passed;
        result.StepOutcomes[fmsl.ConnectionDiagnosisStep.GuestNetwork] = fmsl.ProbeOutcome.CouldNotCheck;
        result.StepOutcomes[fmsl.ConnectionDiagnosisStep.Vpn] = fmsl.ProbeOutcome.Passed;
        result.StepOutcomes[fmsl.ConnectionDiagnosisStep.WindowsPublicNetwork] = fmsl.ProbeOutcome.Failed;
        result.FirstFailedStep = fmsl.ConnectionDiagnosisStep.WindowsPublicNetwork;
        // The dialog takes its words from the Windows display language (CultureInfo.CurrentUICulture), not from the language
        // chosen in the program, so the harness's locale is lent to it for the lifetime of the window (a product note in the report).
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(fmsl.Localization.CurrentCode);
        Form form;
        try { form = new fmsl.Connection_Diagnosis_Dialog(result); }
        catch { CultureInfo.CurrentUICulture = previous; throw; }
        form.FormClosed += (_, _) => CultureInfo.CurrentUICulture = previous;
        return form;
    }

    internal static object SampleReport()
    {
        var report = Instance("BatchReport");
        SetProp(report, "Kind", "Move");
        var results = report.GetType().GetProperty("Results", Program.Instance).GetValue(report);
        var add = results.GetType().GetMethod("Add");
        void Row(string path, string state, string detail = "", string skip = null)
        {
            var item = Instance("BatchItemResult");
            SetProp(item, "Path", path);
            SetProp(item, "State", state);
            SetProp(item, "Detail", detail);
            if (skip != null) SetProp(item, "SkipReason", skip);
            add.Invoke(results, new[] { item });
        }
        const string folder = @"C:\Demo\Holiday 2026\";
        Row(folder + "IMG_0412.jpg", "Failed", fmsl.FailureText.Sentence(fmsl.FailureText.FailureCause.Denied));
        Row(folder + "VID_0418.mp4", "Failed", fmsl.FailureText.Sentence(fmsl.FailureText.FailureCause.DiskFull));
        Row(folder + "IMG_0431.jpg", "NotStarted");
        Row(folder + "IMG_0432.jpg", "NotStarted");
        Row(folder + "IMG_0405.jpg", "Skipped", skip: "AlreadyInDestination");
        Row(folder + "IMG_0401.jpg", "Done");
        Row(folder + "IMG_0402.jpg", "Done");
        Row(folder + "IMG_0403.jpg", "Done");
        SetProp(report, "Cancelled", true);
        return report;
    }
}
