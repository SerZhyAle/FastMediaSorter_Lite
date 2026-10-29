using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Manager = FastMediaSorterCompanion;

// Specification 267: real-window recipes for the Exchange help topics (all in the Share Manager).
// Offline by construction: no recipe passes a real agent, a real ports object or the real server process; every window is
// built with sample data (example.com, fake codes and passwords) and only shown. Nothing here clicks a command.
static partial class Topics
{
    static IEnumerable<Topic> Exchange() => new Topic[]
    {
        // Not first-run mode: OnShown starts nothing. Every port throws, so a call by mistake fails the capture instead of running.
        new Topic("exchange-server-wizard", false,
            () => new Manager.Exchange_Server_Wizard_Form(new ExchangeOfflineWizardPorts(), "demo"),
            "first step: login suggestion and the statement of what the operator can see; Start not pressed"),

        // No agent: the window only calls it on Check certificate / Sign in, which are never pressed.
        new Topic("exchange-account", false,
            () => new Manager.Exchange_Account_Form(null, ExchangeHelpSamples.Server, "demo"),
            "sign-in with sample server and login, password mode, no certificate checked yet, nothing connected",
            Prepare: form => ExchangeHelpSamples.SetText(form, "_txtName", "Demo PC")),

        new Topic("exchange-account-admin", false,
            () => new Manager.Exchange_Account_Admin_Form(Manager.Exchange_Account_Admin_Form.DialogMode.Add, "", false),
            "Add an account mode with a sample login; generated password chosen",
            Prepare: form => ExchangeHelpSamples.SetText(form, "_txtLogin", "demo")),

        new Topic("exchange-account-limits", false,
            () => new Manager.Exchange_Account_Limits_Form("demo", new Manager.ExchangeAccountLimits
            {
                DevicesPerAccount = 5, ResourcesPerAccount = 20, BroadcastsPerAccount = 2, BroadcastsPerDevice = 1,
                ClipboardMaxBytes = 100L * 1024 * 1024
            }),
            "limits of the sample account demo with five overrides filled in"),

        // The fake delegate replaces the server binary; the browser sign-in link builder is fake too, so its button shows.
        new Topic("exchange-connection-string", false,
            () => new Manager.Exchange_Connection_String_Form(
                (name, login) => new Manager.ExchangeProcess.ConnectionStringResult
                {
                    Outcome = Manager.ExchangeProcess.ConnectionStringOutcome.Made,
                    Text = ExchangeHelpSamples.ConnectionString(name, login)
                },
                text => "https://exchange.example.com:22022/#sample"),
            "string made by a fake delegate for sample name and login; copy, save, QR and sign-in link buttons enabled",
            Prepare: form =>
            {
                ExchangeHelpSamples.SetText(form, "_txtName", "Family server");
                ExchangeHelpSamples.SetText(form, "_txtLogin", "demo");
            },
            Settle: async form => await Task.Delay(500)),

        new Topic("exchange-invitation-create", false,
            () => new Manager.Exchange_Invitation_Create_Form(),
            "type choice: own device or family selected, Guest shown as not available with the reason"),

        new Topic("exchange-invitation-created", false,
            () => new Manager.Exchange_Invitation_Created_Form("DEMO-SAMPLE-0000",
                ExchangeHelpSamples.ConnectionString("", "", ExchangeHelpSamples.InvitationId()), "member"),
            "fake one-time code and a sample invitation connection string, own device or family type"),

        new Topic("exchange-shared-invitation", false,
            () => new Manager.Exchange_Shared_Invitation_Form("member"),
            "shared registration password: own device or family type, generated password chosen, Guest not available"),

        new Topic("exchange-link", false,
            () => new Manager.Exchange_Link_Form("Holiday 2026", "FMSSFTP2:DEMO-ONLY-SAMPLE", "{}",
                Manager.ShareConfigBuilder.QrPngForText("FMSSFTP2:DEMO-ONLY-SAMPLE")),
            "link of the sample folder Holiday 2026 with the QR, copy-text and save-file commands (fake code, nothing copied)"),

        new Topic("exchange-password", false,
            () => new Manager.Exchange_Password_Form(Manager.Exchange_Password_Form.DialogMode.Generated, "Sample-Only-1234", "demo", false),
            "generated password shown once, masked, with copy and set-my-own commands (fake password)"),

        new Topic("share-exchange-password-change", false,
            () => new Manager.Exchange_Password_Change_Form("demo @ exchange.example.com:22022"),
            "old password and the new one twice, masked sample entries, policy line visible",
            Prepare: form =>
            {
                ExchangeHelpSamples.SetText(form, "_txtOld", "Sample-Old-1");
                ExchangeHelpSamples.SetText(form, "_txtNew", "Sample-New-2");
                ExchangeHelpSamples.SetText(form, "_txtRepeat", "Sample-New-2");
            }),
    };
}

/// <summary>Sample data and field access for the Exchange recipes. Fictional values only.</summary>
static class ExchangeHelpSamples
{
    internal const string Server = "exchange.example.com:22022";

    /// <summary>A canonical SHA256 fingerprint of a made-up certificate.</summary>
    internal static string Fingerprint() =>
        "SHA256:" + Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes("demo-only-certificate"))).TrimEnd('=');

    /// <summary>A valid invitation id (22 characters of base64url over 16 bytes) of no real invitation.</summary>
    internal static string InvitationId() =>
        Convert.ToBase64String(Enumerable.Range(1, 16).Select(i => (byte)(i * 7)).ToArray())
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The connection string of the sample server, built by the real codec (the server binary is never asked).</summary>
    internal static string ConnectionString(string name, string login, string invitation = "")
    {
        var built = Manager.ExchangeConnectionString.Build(Server, Fingerprint(), name, login, invitation);
        if (!built.Succeeded) throw new InvalidOperationException("The sample connection string was refused: " + built.Reason);
        Manager.ExchangeConnectionStringResult refusal = null;
        return Manager.ExchangeConnectionString.Format(built.Value, ref refusal)
            ?? throw new InvalidOperationException("The sample connection string could not be written.");
    }

    internal static void SetText(Form form, string field, string text)
    {
        var info = form.GetType().GetField(field, Program.Instance)
            ?? throw new MissingFieldException(form.GetType().Name, field);
        ((Control)info.GetValue(form)).Text = text;
    }
}

/// <summary>Stand-in for the wizard's ports. The first step runs none of them; if a call ever happens it fails loudly.</summary>
sealed class ExchangeOfflineWizardPorts : Manager.IExchangeWizardPorts
{
    static T Refuse<T>() => throw new InvalidOperationException("The help capture never runs the wizard.");
    public Manager.ExchangeProcess.InspectReport Inspect() => Refuse<Manager.ExchangeProcess.InspectReport>();
    public bool ServiceExists() => Refuse<bool>();
    public bool IsConsoleRunning() => Refuse<bool>();
    public string GeneratePassword() => Refuse<string>();
    public Task<Manager.ExchangeAdminReply> AddAccountAsync(string login, string password, CancellationToken ct) => Refuse<Task<Manager.ExchangeAdminReply>>();
    public Manager.ExchangeProcess.StartOutcome Start() => Refuse<Manager.ExchangeProcess.StartOutcome>();
    public bool AgentOffered() => Refuse<bool>();
    public Task<Manager.ExchangeCertificateInfo> ProbeAsync(string server, CancellationToken ct) => Refuse<Task<Manager.ExchangeCertificateInfo>>();
    public Task<Manager.ExchangeEnrollResult> EnrollAsync(Manager.ExchangeEnrollRequest request, CancellationToken ct) => Refuse<Task<Manager.ExchangeEnrollResult>>();
    public string MachineName() => Refuse<string>();
    public Task DelayAsync(int milliseconds, CancellationToken ct) => Refuse<Task>();
}
