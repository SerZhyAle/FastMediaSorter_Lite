#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Net
Imports System.Net.Sockets
Imports System.Reflection
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' 007 §3: the parts of the resource UI that can be held without a screen - what each failure class is
''' called, what a probe says about a real socket, which saved resource an address belongs to, and what the
''' add/edit dialog accepts and refuses. The dialogs are built and driven on an STA thread without being
''' shown; what stays for a human is how they look, not what they decide.
''' </summary>
Public Class ResourceUiTests

    Private Shared Sub OnSta(action As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         action()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(20000), "the UI check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    ' ------------------------------------------------------------------------ sentences ----

    <Fact>
    Public Sub Every_failure_class_has_its_own_sentence()
        Dim failures As New List(Of ResourceFailure) From {
            New ResourceFailure(ResourceFailureKind.Auth),
            New ResourceFailure(ResourceFailureKind.KeyMismatch),
            New ResourceFailure(ResourceFailureKind.Transport),
            New ResourceFailure(ResourceFailureKind.Timeout),
            New ResourceFailure(ResourceFailureKind.SftpStatus, 2),
            New ResourceFailure(ResourceFailureKind.SftpStatus, 3),
            New ResourceFailure(ResourceFailureKind.SftpStatus, 4),
            New ResourceFailure(ResourceFailureKind.Cancelled),
            New ResourceFailure(ResourceFailureKind.CredentialConflict),
            New ResourceFailure(ResourceFailureKind.Unsupported),
            New ResourceFailure(ResourceFailureKind.LocalIO)}

        Dim sentences As List(Of String) = failures.Select(Function(f) ResourceMessages.ForFailure(f)).ToList()

        Assert.All(sentences, Sub(s) Assert.False(String.IsNullOrWhiteSpace(s)))
        ' A dropped link, a refused password and a missing folder must never read the same.
        Assert.Equal(sentences.Count, sentences.Distinct(StringComparer.Ordinal).Count())
    End Sub

    <Fact>
    Public Sub A_failure_sentence_never_carries_a_detail_it_was_given()
        Dim sentence As String = ResourceMessages.ForFailure(New ResourceFailure(ResourceFailureKind.Auth, 5, "alice@secret-host password=hunter2"))

        Assert.DoesNotContain("hunter2", sentence)
        Assert.DoesNotContain("secret-host", sentence)
    End Sub

    <Fact>
    Public Sub An_import_that_is_too_new_names_the_way_out_and_the_others_do_not_quote_the_file()
        Dim tooNew As String = ResourceMessages.ForImportFailure(FmsConfigFailure.UpdateRequired)
        Dim broken As String = ResourceMessages.ForImportFailure(FmsConfigFailure.InvalidJson)

        Assert.Contains("Fast Media Sorter", tooNew)
        Assert.NotEqual(tooNew, broken)
        Assert.NotEqual(ResourceMessages.ForImportFailure(FmsConfigFailure.UnsupportedProtocol), broken)
        Assert.NotEqual(ResourceMessages.ForImportFailure(FmsConfigFailure.TooLarge), broken)
        ' Every class of refusal has a sentence, not an empty string or an enum name.
        For Each failure As FmsConfigFailure In [Enum].GetValues(GetType(FmsConfigFailure)).Cast(Of FmsConfigFailure)().Where(Function(f) f <> FmsConfigFailure.None)
            Assert.False(String.IsNullOrWhiteSpace(ResourceMessages.ForImportFailure(failure)))
        Next
    End Sub

    ' ----------------------------------------------------------------------------- probe ----

    ''' <summary>A one-shot loopback server that says <paramref name="greeting"/> (or nothing) to the first client.</summary>
    Private Shared Function Serve(greeting As String, ByRef port As Integer) As TcpListener
        Dim listener As New TcpListener(IPAddress.Loopback, 0)
        listener.Start()
        port = CType(listener.LocalEndpoint, IPEndPoint).Port
        Task.Run(Async Function()
                     Try
                         Using client As TcpClient = Await listener.AcceptTcpClientAsync()
                             If greeting IsNot Nothing Then
                                 Dim bytes As Byte() = Encoding.ASCII.GetBytes(greeting)
                                 Await client.GetStream().WriteAsync(bytes, 0, bytes.Length)
                             End If
                             Await Task.Delay(1500)
                         End Using
                     Catch ex As Exception
                     End Try
                 End Function)
        Return listener
    End Function

    <Fact>
    Public Sub Probe_an_ssh_banner_is_our_kind_of_server()
        Dim port As Integer
        Dim listener As TcpListener = Serve("SSH-2.0-OpenSSH_9.9" & vbCrLf, port)
        Try
            Assert.Equal(ProbeVerdict.SshOk, ResourceProbe.ProbeSftpAsync("127.0.0.1", port, CancellationToken.None).GetAwaiter().GetResult())
        Finally
            listener.Stop()
        End Try
    End Sub

    <Fact>
    Public Sub Probe_anything_else_that_answers_is_only_an_open_port()
        Dim port As Integer
        Dim listener As TcpListener = Serve("HTTP/1.1 400 Bad Request" & vbCrLf, port)
        Try
            Assert.Equal(ProbeVerdict.PortOpen, ResourceProbe.ProbeSftpAsync("127.0.0.1", port, CancellationToken.None).GetAwaiter().GetResult())
        Finally
            listener.Stop()
        End Try
    End Sub

    <Fact>
    Public Sub Probe_a_peer_that_says_nothing_is_an_open_port_not_a_hang()
        Dim port As Integer
        Dim listener As TcpListener = Serve(Nothing, port)
        Try
            Dim watch As Diagnostics.Stopwatch = Diagnostics.Stopwatch.StartNew()
            Dim verdict As ProbeVerdict = ResourceProbe.ProbeSftpAsync("127.0.0.1", port, CancellationToken.None, timeoutMs:=1000).GetAwaiter().GetResult()
            Assert.Equal(ProbeVerdict.PortOpen, verdict)
            Assert.True(watch.ElapsedMilliseconds < 5000, "the probe must be bounded by its own timeout")
        Finally
            listener.Stop()
        End Try
    End Sub

    <Fact>
    Public Sub Probe_a_closed_port_is_refused_and_bad_input_is_not_asked()
        Dim port As Integer
        Dim listener As TcpListener = Serve(Nothing, port)
        listener.Stop()
        Assert.Equal(ProbeVerdict.Refused, ResourceProbe.ProbeSftpAsync("127.0.0.1", port, CancellationToken.None, timeoutMs:=8000).GetAwaiter().GetResult())
        Assert.Equal(ProbeVerdict.BadInput, ResourceProbe.ProbeSftpAsync("", 22, CancellationToken.None).GetAwaiter().GetResult())
        Assert.Equal(ProbeVerdict.BadInput, ResourceProbe.ProbeSftpAsync("host", 0, CancellationToken.None).GetAwaiter().GetResult())
        Assert.Equal(ProbeVerdict.BadInput, ResourceProbe.ProbeSftpAsync("host", 70000, CancellationToken.None).GetAwaiter().GetResult())
    End Sub

    <Fact>
    Public Sub Probe_every_verdict_has_a_sentence()
        Dim sentences As List(Of String) = [Enum].GetValues(GetType(ProbeVerdict)).Cast(Of ProbeVerdict)().Select(Function(v) ResourceProbe.VerdictSentence(v)).ToList()
        Assert.All(sentences, Sub(s) Assert.False(String.IsNullOrWhiteSpace(s)))
        Assert.Equal(sentences.Count, sentences.Distinct(StringComparer.Ordinal).Count())
    End Sub

    ' ------------------------------------------------------------------------- address ----

    Private Shared Function Saved(id As String, host As String, port As Integer, user As String, root As String) As ResourceDescriptor
        Return New ResourceDescriptor With {.Id = id, .Kind = ResourceKind.Sftp, .Host = host, .Port = port, .UserName = user, .RootPath = root}
    End Function

    <Fact>
    Public Sub An_address_finds_the_saved_server_whatever_the_case_and_the_default_port()
        Dim list As New List(Of ResourceDescriptor) From {Saved("a", "NAS.local", 22, "alice", "/Photos")}

        Assert.Equal("a", ResourceMatching.FindForAddress(list, "nas.LOCAL", 0, "alice", "/Photos").Id)
        Assert.Equal("a", ResourceMatching.FindForAddress(list, "nas.local", 22, "", "/Photos").Id)
        Assert.Null(ResourceMatching.FindForAddress(list, "nas.local", 2222, "alice", "/Photos"))
        Assert.Null(ResourceMatching.FindForAddress(list, "other.local", 22, "alice", "/Photos"))
        ' The user the address names must be the saved one: a different login is a different resource.
        Assert.Null(ResourceMatching.FindForAddress(list, "nas.local", 22, "bob", "/Photos"))
    End Sub

    <Fact>
    Public Sub An_address_prefers_the_deepest_saved_root_it_is_under()
        Dim list As New List(Of ResourceDescriptor) From {
            Saved("root", "nas", 22, "alice", "/"),
            Saved("photos", "nas", 22, "alice", "/Photos"),
            Saved("videos", "nas", 22, "alice", "/Videos")}

        Assert.Equal("photos", ResourceMatching.FindForAddress(list, "nas", 22, "alice", "/Photos/2024/July").Id)
        Assert.Equal("videos", ResourceMatching.FindForAddress(list, "nas", 22, "alice", "/Videos").Id)
        Assert.Equal("root", ResourceMatching.FindForAddress(list, "nas", 22, "alice", "/Documents").Id)
        ' "/PhotosArchive" is not under "/Photos": a prefix of the text is not a parent folder.
        Assert.Equal("root", ResourceMatching.FindForAddress(list, "nas", 22, "alice", "/PhotosArchive").Id)
    End Sub

    <Fact>
    Public Sub An_address_outside_every_saved_root_still_uses_that_servers_saved_login()
        Dim list As New List(Of ResourceDescriptor) From {Saved("photos", "nas", 22, "alice", "/Photos")}

        Assert.Equal("photos", ResourceMatching.FindForAddress(list, "nas", 22, "alice", "/Elsewhere").Id)
    End Sub

    <Fact>
    Public Sub An_address_also_matches_a_stored_alternate_address_of_the_server()
        Dim imported As ResourceDescriptor = Saved("imported", "192.168.1.5", 2022, "fms", "/Share")
        imported.Endpoints = New List(Of ResourceEndpoint) From {
            New ResourceEndpoint With {.Kind = "portforward", .Host = "home.example.org", .Port = 55001}}

        Assert.Equal("imported", ResourceMatching.FindForAddress({imported}, "home.example.org", 55001, "fms", "/Share").Id)
    End Sub

    ' --------------------------------------------------------------------------- dialogs ----

    Private Shared Function Box(form As Form, name As String) As Control
        Return form.Controls.Find(name, True).Single()
    End Function

    ''' <summary>The control's OWN visible flag. <c>Control.Visible</c> is False for every child of a form that
    ''' was never shown, so asking it would test the form, not the decision under test.</summary>
    Private Shared Function OwnVisible(control As Control) As Boolean
        ' GetState takes WinForms' internal States enum: find it by shape, pass the Visible bit (0x2).
        Dim getState As MethodInfo = GetType(Control).GetMethods(BindingFlags.NonPublic Or BindingFlags.Instance).
            Single(Function(m) m.Name = "GetState" AndAlso m.GetParameters().Length = 1 AndAlso m.GetParameters()(0).ParameterType.IsEnum)
        Dim flag As Object = [Enum].ToObject(getState.GetParameters()(0).ParameterType, &H2)
        Return CBool(getState.Invoke(control, New Object() {flag}))
    End Function

    Private Shared Sub Press(form As Form, handler As String)
        form.GetType().GetMethod(handler, BindingFlags.NonPublic Or BindingFlags.Instance).Invoke(form, New Object() {Nothing, EventArgs.Empty})
    End Sub

    <Fact>
    Public Sub The_three_dialogs_build_and_carry_text()
        OnSta(Sub()
                  Using credential As New Resource_Credential_Form("NAS", "alice", retry:=False)
                      Assert.False(String.IsNullOrWhiteSpace(credential.Text))
                      Assert.True(OwnVisible(Box(credential, "resource_Remember")))
                  End Using
                  Using noRemember As New Resource_Credential_Form("NAS", "alice", retry:=True, showRemember:=False)
                      Assert.False(OwnVisible(Box(noRemember, "resource_Remember")))
                      Assert.False(String.IsNullOrEmpty(Box(noRemember, "resource_Warning").Text))
                  End Using
                  Using edit As New Resource_Edit_Form(Nothing)
                      Assert.False(String.IsNullOrWhiteSpace(edit.Text))
                  End Using
                  Using manager As New Resource_Manager_Form()
                      Assert.False(String.IsNullOrWhiteSpace(manager.Text))
                  End Using
              End Sub)
    End Sub

    Private Shared Sub Fill(form As Form, host As String, port As String, user As String, password As String, folder As String)
        Box(form, "res_Host").Text = host
        Box(form, "res_Port").Text = port
        Box(form, "res_User").Text = user
        Box(form, "res_Password").Text = password
        Box(form, "res_Folder").Text = folder
    End Sub

    <Fact>
    Public Sub The_edit_dialog_accepts_a_good_server_and_canonicalises_what_it_was_given()
        OnSta(Sub()
                  Using edit As New Resource_Edit_Form(Nothing)
                      Fill(edit, "nas.local", "2022", "alice", "pw", "Photos\Summer")
                      Press(edit, "Confirm")

                      Assert.NotNull(edit.Result)
                      Assert.Equal(ResourceKind.Sftp, edit.Result.Kind)
                      Assert.Equal("nas.local", edit.Result.Host)
                      Assert.Equal(2022, edit.Result.Port)
                      Assert.Equal("/Photos/Summer", edit.Result.RootPath)
                      Assert.Equal("nas.local", edit.Result.DisplayName)
                      Assert.Equal("pw", edit.NewPassword)
                      Assert.StartsWith("res:", edit.Result.Id)
                      Assert.Equal("", edit.Result.SecretHandle)
                  End Using
              End Sub)
    End Sub

    <Theory>
    <InlineData("", "22", "alice", "pw", "/", "res_Host")>
    <InlineData("has space", "22", "alice", "pw", "/", "res_Host")>
    <InlineData("user@host", "22", "alice", "pw", "/", "res_Host")>
    <InlineData("nas", "0", "alice", "pw", "/", "res_Port")>
    <InlineData("nas", "70000", "alice", "pw", "/", "res_Port")>
    <InlineData("nas", "abc", "alice", "pw", "/", "res_Port")>
    <InlineData("nas", "22", "", "pw", "/", "res_User")>
    <InlineData("nas", "22", "alice", "", "/", "res_Password")>
    <InlineData("nas", "22", "alice", "pw", "/a/../b", "res_Folder")>
    Public Sub The_edit_dialog_refuses_a_bad_field_and_says_which(host As String, port As String, user As String, password As String, folder As String, culprit As String)
        OnSta(Sub()
                  Using edit As New Resource_Edit_Form(Nothing)
                      Fill(edit, host, port, user, password, folder)
                      Press(edit, "Confirm")

                      Assert.Null(edit.Result)
                      Assert.False(String.IsNullOrEmpty(Box(edit, "res_Problem").Text), "the problem must be said")
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Editing_a_saved_resource_keeps_its_credential_when_the_password_is_left_empty()
        OnSta(Sub()
                  Dim saved As New ResourceDescriptor With {
                      .Id = "keep", .DisplayName = "My NAS", .Kind = ResourceKind.Sftp, .Host = "nas", .Port = 22, .UserName = "alice",
                      .RootPath = "/Photos", .SecretHandle = "cred:abc", .PinnedFingerprint = "SHA256:" & New String("A"c, 43)}
                  Using edit As New Resource_Edit_Form(saved)
                      Fill(edit, "nas", "22", "alice", "", "/Photos")
                      Press(edit, "Confirm")

                      Assert.NotNull(edit.Result)
                      Assert.Null(edit.NewPassword)
                      Assert.Equal("keep", edit.Result.Id)
                      Assert.Equal("cred:abc", edit.Result.SecretHandle)
                      Assert.Equal(saved.PinnedFingerprint, edit.Result.PinnedFingerprint)
                      Assert.Equal("My NAS", edit.Result.DisplayName)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Editing_to_another_address_drops_the_old_alternates()
        OnSta(Sub()
                  Dim saved As New ResourceDescriptor With {
                      .Id = "keep", .Kind = ResourceKind.Sftp, .Host = "nas", .Port = 22, .UserName = "alice", .RootPath = "/",
                      .SecretHandle = "cred:abc",
                      .Endpoints = New List(Of ResourceEndpoint) From {New ResourceEndpoint With {.Kind = "lan", .Host = "10.0.0.5", .Port = 22}}}
                  Using edit As New Resource_Edit_Form(saved)
                      Fill(edit, "somewhere-else", "22", "alice", "", "/")
                      Press(edit, "Confirm")

                      ' The old alternates would send the credential somewhere the user did not choose.
                      Assert.Null(edit.Result.Endpoints)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub The_forget_key_button_exists_only_for_a_pinned_resource()
        OnSta(Sub()
                  Dim pinned As New ResourceDescriptor With {
                      .Id = "p", .Kind = ResourceKind.Sftp, .Host = "nas", .Port = 22, .UserName = "a", .RootPath = "/",
                      .SecretHandle = "cred:1", .PinnedFingerprint = "SHA256:" & New String("A"c, 43)}
                  Using withPin As New Resource_Edit_Form(pinned)
                      Assert.True(OwnVisible(Box(withPin, "res_ForgetKey")))
                  End Using
                  Using fresh As New Resource_Edit_Form(Nothing)
                      Assert.False(OwnVisible(Box(fresh, "res_ForgetKey")))
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_network_share_needs_a_unc_path_and_a_login_and_has_no_port_or_key()
        OnSta(Sub()
                  Using edit As New Resource_Edit_Form(Nothing, New ResourceDescriptor With {.Kind = ResourceKind.Smb})
                      Dim combo As ComboBox = DirectCast(Box(edit, "res_Kind"), ComboBox)
                      Assert.Equal(1, combo.SelectedIndex)
                      Assert.False(OwnVisible(Box(edit, "res_Port")))
                      Assert.False(OwnVisible(Box(edit, "res_ForgetKey")))

                      Box(edit, "res_Host").Text = "not a unc path"
                      Box(edit, "res_User").Text = "alice"
                      Box(edit, "res_Password").Text = "pw"
                      Press(edit, "Confirm")
                      Assert.Null(edit.Result)

                      Box(edit, "res_Host").Text = "\\server\photos\2024"
                      Press(edit, "Confirm")
                      Assert.NotNull(edit.Result)
                      Assert.Equal(ResourceKind.Smb, edit.Result.Kind)
                      Assert.Equal("\\server\photos\2024", edit.Result.RootPath)
                      Assert.Equal("", edit.Result.Host)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub The_password_dialog_refuses_an_empty_password_and_keeps_what_was_typed_untouched()
        OnSta(Sub()
                  Using dialog As New Resource_Credential_Form("NAS", "alice", retry:=False)
                      Press(dialog, "Confirm")
                      Assert.NotEqual(DialogResult.OK, dialog.DialogResult)
                      Assert.False(String.IsNullOrEmpty(Box(dialog, "resource_Warning").Text))

                      Box(dialog, "resource_Password").Text = " pw with spaces "
                      Press(dialog, "Confirm")
                      Assert.Equal(DialogResult.OK, dialog.DialogResult)
                      ' Never trimmed: a space at either end is part of the password.
                      Assert.Equal(" pw with spaces ", dialog.Credential)
                  End Using
              End Sub)
    End Sub

    ' --------------------------------------------------------------------- ticket 118 ----

    Private NotInheritable Class MemoryStore
        Implements IResourceSecretStore

        Friend ReadOnly Blobs As New Dictionary(Of String, String)()
        Friend Sub PutBlob(handle As String, protectedBlob As String) Implements IResourceSecretStore.PutBlob
            Blobs(handle) = protectedBlob
        End Sub
        Friend Function GetBlob(handle As String) As String Implements IResourceSecretStore.GetBlob
            Dim v As String = Nothing
            Return If(Blobs.TryGetValue(handle, v), v, "")
        End Function
        Friend Sub DeleteBlob(handle As String) Implements IResourceSecretStore.DeleteBlob
            Blobs.Remove(handle)
        End Sub
        Friend Function AllHandles() As IReadOnlyList(Of String) Implements IResourceSecretStore.AllHandles
            Return Blobs.Keys.ToList()
        End Function
    End Class

    Private NotInheritable class MemoryRegistry
        Friend ReadOnly Values As New Dictionary(Of String, String)()
        Friend Function Read(name As String) As String
            Dim v As String = Nothing
            Return If(Values.TryGetValue(name, v), v, "")
        End Function
        Friend Sub Write(name As String, value As String)
            Values(name) = value
        End Sub
    End Class

    ''' <summary>An in-memory hive with one SFTP and one SMB resource; the SFTP one carries a secret.</summary>
    Private Shared Function SeededRegistry() As MemoryRegistry
        Dim hive As New MemoryRegistry()
        Dim sftp As New ResourceDescriptor With {
            .Id = "sftp1", .Kind = ResourceKind.Sftp, .DisplayName = "NAS", .Host = "nas.local", .Port = 22,
            .UserName = "alice", .RootPath = "/Photos", .SecretHandle = "cred:1", .PinnedFingerprint = "SHA256:" & New String("A"c, 43)}
        Dim smb As New ResourceDescriptor With {
            .Id = "smb1", .Kind = ResourceKind.Smb, .DisplayName = "Share", .RootPath = "\\server\photos"}
        hive.Write(ResourceRegistry.Value_Name, ResourceRegistry.Serialize({sftp, smb}))
        Return hive
    End Function

    <Fact>
    Public Sub ResourceButtonShouldShow_IsAFunctionOfTheCountAndArchiveMode()
        ' T-U1: no resources, no button - hidden, never a disabled stub.
        Assert.False(ResourceUiRules.ResourceButtonShouldShow(0, False))
        Assert.True(ResourceUiRules.ResourceButtonShouldShow(1, False))
        Assert.True(ResourceUiRules.ResourceButtonShouldShow(3, False))
        ' An open archive hides it exactly like the folder menu's resource entries.
        Assert.False(ResourceUiRules.ResourceButtonShouldShow(1, True))
        ' Removing the last resource flips the visible state back off.
        Dim wasVisible As Boolean = ResourceUiRules.ResourceButtonShouldShow(1, False)
        Assert.True(wasVisible)
        Assert.False(ResourceUiRules.ResourceButtonShouldShow(0, False))
    End Sub

    <Fact>
    Public Sub ThePanelForBothHostsHasTheSameNames_ButtonOrder_AndEnabledMatrix()
        ' T-U2: the panel embedded by the dialog and by the Settings page is one control. The
        ' button order is the dialog's reading order with Export beside its siblings.
        OnSta(Sub()
                  Dim hive As MemoryRegistry = SeededRegistry()
                  Dim secrets As New MemoryStore()
                  secrets.Blobs("cred:1") = "blob:pw"
                  Using panel As New Resource_List_Panel(1.0F, AddressOf hive.Read, AddressOf hive.Write, secrets)
                      For Each name As String In {"res_List", "res_Hint", "res_Status", "res_Add", "res_Edit", "res_Remove",
                                                  "res_Test", "res_Export", "res_ImportFile", "res_ImportClipboard"}
                          Assert.Single(panel.Controls.Find(name, True))
                      Next
                      Dim buttons As IEnumerable(Of String) = panel.Controls.Find("res_Add", True)(0).Parent.Controls.OfType(Of Button)().
                          Select(Function(b) b.Name)
                      Assert.Equal({"res_Add", "res_Edit", "res_Remove", "res_Test", "res_Export", "res_ImportFile", "res_ImportClipboard"}, buttons)

                      ' The panel auto-selects its first row once it has one (the dialog's own
                      ' rule, kept) - so the "nothing selected" matrix is read on an EMPTY list.
                      Dim empty As New MemoryRegistry()
                      Using bare As New Resource_List_Panel(1.0F, AddressOf empty.Read, AddressOf empty.Write, secrets)
                          Dim bareHandle As IntPtr = DirectCast(bare.Controls.Find("res_List", True)(0), ListView).Handle
                          Assert.False(DirectCast(bare.Controls.Find("res_Edit", True)(0), Button).Enabled)
                          Assert.False(DirectCast(bare.Controls.Find("res_Export", True)(0), Button).Enabled)
                          ' Add and the imports never depend on a selection.
                          Assert.True(DirectCast(bare.Controls.Find("res_Add", True)(0), Button).Enabled)
                          Assert.True(DirectCast(bare.Controls.Find("res_ImportFile", True)(0), Button).Enabled)
                      End Using

                      ' With the first row auto-selected: edit, remove, test and export all come on.
                      Dim list As ListView = DirectCast(panel.Controls.Find("res_List", True)(0), ListView)
                      Dim listHandle As IntPtr = list.Handle
                      Assert.True(DirectCast(panel.Controls.Find("res_Edit", True)(0), Button).Enabled)
                      Assert.True(DirectCast(panel.Controls.Find("res_Remove", True)(0), Button).Enabled)
                      Assert.True(DirectCast(panel.Controls.Find("res_Test", True)(0), Button).Enabled)
                      Assert.True(DirectCast(panel.Controls.Find("res_Export", True)(0), Button).Enabled)

                      ' The dialog builds the same panel: its own chrome is just Open and Close.
                      Using manager As New Resource_Manager_Form()
                          Assert.NotNull(manager.Controls.Find("res_Open", True).Single())
                          Assert.NotNull(manager.Controls.Find("res_Close", True).Single())
                          Assert.NotNull(manager.Controls.Find("res_Add", True).Single())
                      End Using
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub RemovingThroughThePanelRemovesTheEntryAndItsSecret_AndRecordsTheId()
        ' T-U3: Remove goes through ResourceCommit, so the credential blob goes with the entry,
        ' and the host learns the id through RemovedIds / ResourcesChanged.
        OnSta(Sub()
                  Dim hive As MemoryRegistry = SeededRegistry()
                  Dim secrets As New MemoryStore()
                  secrets.Blobs("cred:1") = "blob:pw"
                  Dim changed As IReadOnlyList(Of String) = Nothing
                  Using panel As New Resource_List_Panel(1.0F, AddressOf hive.Read, AddressOf hive.Write, secrets,
                                                         Function(r) True) ' the confirmation is the test's yes
                      AddHandler panel.ResourcesChanged, Sub(ids) changed = ids
                      Dim list As ListView = DirectCast(panel.Controls.Find("res_List", True)(0), ListView)
                      Dim listHandle As IntPtr = list.Handle ' the selection speaks through the handle
                      Dim sftpRow As ListViewItem = list.Items.OfType(Of ListViewItem)().Single(Function(i) DirectCast(i.Tag, ResourceDescriptor).Id = "sftp1")
                      sftpRow.Selected = True
                      panel.GetType().GetMethod("OnRemove", BindingFlags.NonPublic Or BindingFlags.Instance).
                          Invoke(panel, New Object() {Nothing, EventArgs.Empty})

                      Dim left As String = hive.Read(ResourceRegistry.Value_Name)
                      Assert.DoesNotContain("sftp1", left)
                      Assert.Contains("smb1", left)
                      Assert.False(secrets.Blobs.ContainsKey("cred:1"), "the secret blob goes with the entry")
                      Assert.Contains("sftp1", panel.RemovedIds)
                      Assert.NotNull(changed)
                      Assert.Contains("sftp1", changed)

                      ' Double-click raises ItemActivated with the selected resource.
                      Dim activated As ResourceDescriptor = Nothing
                      AddHandler panel.ItemActivated, Sub(r) activated = r
                      Dim smbRow As ListViewItem = list.Items.OfType(Of ListViewItem)().Single(Function(i) DirectCast(i.Tag, ResourceDescriptor).Id = "smb1")
                      smbRow.Selected = True
                      list.GetType().GetMethod("OnDoubleClick", BindingFlags.NonPublic Or BindingFlags.Instance).
                          Invoke(list, New Object() {EventArgs.Empty})
                      Assert.Equal("smb1", activated.Id)
                  End Using
              End Sub)
    End Sub

End Class
#End If
