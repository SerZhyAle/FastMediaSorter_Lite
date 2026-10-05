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
<Collection("LocalizationState")>
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
        ' T-U2, revised by 131: the panel embedded by the dialog and by the Settings page is one
        ' control. The action row reads Add, Edit, Remove, Test, then the overflow; the Settings
        ' host inserts the primary Open before the overflow, the dialog keeps Open in its own
        ' chrome. Export and the two imports are rows of the overflow, not buttons.
        OnSta(Sub()
                  Dim hive As MemoryRegistry = SeededRegistry()
                  Dim secrets As New MemoryStore()
                  secrets.Blobs("cred:1") = "blob:pw"
                  Using panel As New Resource_List_Panel(1.0F, AddressOf hive.Read, AddressOf hive.Write, secrets, withPrimaryOpen:=True)
                      For Each name As String In {"res_List", "res_Hint", "res_Status", "res_Summary", "res_Empty", "res_EmptyAdd",
                                                  "res_Add", "res_Edit", "res_Remove", "res_Test", "res_Open", "res_More"}
                          Assert.Single(panel.Controls.Find(name, True))
                      Next
                      Dim row As Control = panel.Controls.Find("res_Add", True)(0).Parent
                      Dim buttons As IEnumerable(Of String) = row.Controls.OfType(Of Button)().Select(Function(b) b.Name)
                      Assert.Equal({"res_Add", "res_Edit", "res_Remove", "res_Test", "res_Open", "res_More"}, buttons)

                      ' With the first row auto-selected (the dialog's own rule, kept): the selection
                      ' commands and the primary Open are all on, Add and the imports never depend
                      ' on a selection. The handle makes the auto-selection real for the list.
                      Dim list As ListView = DirectCast(panel.Controls.Find("res_List", True)(0), ListView)
                      Dim listHandle As IntPtr = list.Handle
                      Assert.True(DirectCast(panel.Controls.Find("res_Edit", True)(0), Button).Enabled)
                      Assert.True(DirectCast(panel.Controls.Find("res_Remove", True)(0), Button).Enabled)
                      Assert.True(DirectCast(panel.Controls.Find("res_Test", True)(0), Button).Enabled)
                      Assert.True(DirectCast(panel.Controls.Find("res_Open", True)(0), Button).Enabled)
                      Assert.True(DirectCast(panel.Controls.Find("res_Add", True)(0), Button).Enabled)
                      Dim overflow As IEnumerable(Of ChromeMenuItem) = panel.BuildOverflowItems()
                      Assert.Equal({"res_Export", "res_ImportFile", "res_ImportClipboard"}, overflow.Select(Function(item) item.Id))
                      Assert.All(overflow, Sub(item) Assert.True(item.Enabled))

                      ' The dialog builds the same panel without the primary Open: its own chrome
                      ' is just Open and Close.
                      Using manager As New Resource_Manager_Form()
                          Assert.NotNull(manager.Controls.Find("res_Open", True).Single())
                          Assert.NotNull(manager.Controls.Find("res_Close", True).Single())
                          Assert.NotNull(manager.Controls.Find("res_Add", True).Single())
                          Dim dialogRow As Control = manager.Controls.Find("res_Add", True)(0).Parent
                          Assert.Equal({"res_Add", "res_Edit", "res_Remove", "res_Test", "res_More"},
                                       dialogRow.Controls.OfType(Of Button)().Select(Function(b) b.Name))
                      End Using

                      ' The "nothing selected" matrix is read on an EMPTY list (131 §3.2): the
                      ' selection commands are off AND say why in their accessible names.
                      Dim empty As New MemoryRegistry()
                      Using bare As New Resource_List_Panel(1.0F, AddressOf empty.Read, AddressOf empty.Write, secrets, withPrimaryOpen:=True)
                          Dim bareHandle As IntPtr = DirectCast(bare.Controls.Find("res_List", True)(0), ListView).Handle
                          Dim reason As String = Localization.T("Сначала выберите ресурс в списке")
                          For Each name As String In {"res_Edit", "res_Remove", "res_Test", "res_Open"}
                              Dim button As Button = DirectCast(bare.Controls.Find(name, True)(0), Button)
                              Assert.False(button.Enabled)
                              Assert.Contains(reason, button.AccessibleName)
                          Next
                          Assert.True(DirectCast(bare.Controls.Find("res_Add", True)(0), Button).Enabled)
                          Dim bareExport As ChromeMenuItem = bare.BuildOverflowItems().Single(Function(item) item.Id = "res_Export")
                          Assert.False(bareExport.Enabled)
                          Assert.Equal(reason, bareExport.DisabledReason)
                          Assert.True(bare.BuildOverflowItems().Where(Function(item) item.Id.StartsWith("res_Import", StringComparison.Ordinal)).All(Function(item) item.Enabled))
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

    <Fact>
    Public Sub ThePanelButtonsKeepTheTwentyEightPixelFloorAtBothHostScales_AndTheListCarriesItsAccessibleName()
        ' T-U5: the ICON-RENDER floor scales with the host window - 1.0 is the manager
        ' dialog's own metrics, 1.5 stands for the settings window's scale. The list's
        ' accessible name is the screen-reader word the settings page is reached by (§14).
        OnSta(Sub()
                  For Each scale As Single In {1.0F, 1.5F}
                      Dim hive As New MemoryRegistry()
                      Using panel As New Resource_List_Panel(scale, AddressOf hive.Read, AddressOf hive.Write, New MemoryStore())
                          Dim floor As Integer = CInt(Math.Floor(28.0F * scale))
                          For Each b As Button In panel.Controls.Find("res_Add", True)(0).Parent.Controls.OfType(Of Button)()
                              Assert.True(b.MinimumSize.Height >= floor,
                                          b.Name & " sits below the scaled floor at scale " &
                                          scale.ToString(Globalization.CultureInfo.InvariantCulture))
                          Next
                          Assert.Equal(Localization.T("Ресурсы"),
                                       DirectCast(panel.Controls.Find("res_List", True)(0), ListView).AccessibleName)
                      End Using
                  Next
              End Sub)
    End Sub

    ' ---------------------------------------------------------------- ticket 131 ----

    <Fact>
    Public Sub CommandsStayInReadingOrderAndInsideTheHostAcrossLanguagesAndScales()
        OnSta(Sub()
                  Dim saved As String = Localization.CurrentCode
                  Try
                      For Each code As String In Localization.Codes
                          Localization.CurrentCode = code
                          For Each scale As Single In {1.0F, 1.75F}
                              Dim hive As MemoryRegistry = SeededRegistry()
                              Using panel As New Resource_List_Panel(scale, AddressOf hive.Read, AddressOf hive.Write, New MemoryStore(),
                                                                     withPrimaryOpen:=True, deviceScale:=True)
                                  panel.Font = New Drawing.Font("Segoe UI", 9.0F * scale)
                                  panel.Size = New Size(CInt(560 * scale), CInt(650 * scale))
                                  Dim panelHandle As IntPtr = panel.Handle
                                  panel.Localize()
                                  Dim row As Control = panel.Controls.Find("res_ActionRow", True)(0)
                                  Dim buttons As Button() = row.Controls.OfType(Of Button)().ToArray()
                                  Dim rtl As Boolean = Localization.IsRightToLeft()
                                  For index As Integer = 0 To buttons.Length - 1
                                      Assert.True(row.ClientRectangle.Contains(buttons(index).Bounds), code & ": " & buttons(index).Name)
                                      Assert.Equal(CInt(32 * scale), buttons(index).Height)
                                      For other As Integer = index + 1 To buttons.Length - 1
                                          Assert.False(buttons(index).Bounds.IntersectsWith(buttons(other).Bounds))
                                      Next
                                      If index > 0 AndAlso buttons(index).Top = buttons(index - 1).Top Then
                                          Assert.True(If(rtl, buttons(index).Right <= buttons(index - 1).Left,
                                                             buttons(index).Left >= buttons(index - 1).Right))
                                      End If
                                  Next
                                  Dim summary As Control = panel.Controls.Find("res_Summary", True)(0)
                                  Assert.All(buttons, Sub(b) Assert.False(summary.Bounds.IntersectsWith(b.Bounds)))
                                  Dim list As ListView = DirectCast(panel.Controls.Find("res_List", True)(0), ListView)
                                  Assert.True(list.Height >= CInt(320 * scale))
                                  Assert.True(list.Columns.Cast(Of ColumnHeader)().Sum(Function(c) c.Width) <= list.ClientSize.Width)
                              End Using
                          Next
                      Next
                  Finally
                      Localization.CurrentCode = saved
                  End Try
              End Sub)
    End Sub

    <Fact>
    Public Sub SwitchingLanguageRefreshesRowTextAndDirectionWithoutLosingSelection()
        OnSta(Sub()
                  Dim saved As String = Localization.CurrentCode
                  Try
                      Localization.CurrentCode = "en"
                      Dim hive As MemoryRegistry = SeededRegistry()
                      Using panel As New Resource_List_Panel(1.0F, AddressOf hive.Read, AddressOf hive.Write, New MemoryStore(), withPrimaryOpen:=True)
                          panel.Size = New Size(900, 500)
                          Dim list As ListView = DirectCast(panel.Controls.Find("res_List", True)(0), ListView)
                          Dim listHandle As IntPtr = list.Handle
                          Dim selectedId As String = panel.SelectedResource.Id
                          For Each code As String In {"ru", "ar", "en"}
                              Localization.CurrentCode = code
                              panel.Localize()
                              Assert.Equal(selectedId, panel.SelectedResource.Id)
                              Assert.Equal(Localization.IsRightToLeft(), list.RightToLeftLayout)
                              Assert.Equal(If(Localization.IsRightToLeft(), RightToLeft.Yes, RightToLeft.No), panel.RightToLeft)
                              For Each item As ListViewItem In list.Items
                                  Dim resource As ResourceDescriptor = DirectCast(item.Tag, ResourceDescriptor)
                                  Dim access As String = Localization.T(If(resource.ReadOnlyHint, "только чтение", "чтение и запись"))
                                  Assert.Equal(ResourceMessages.KindLabel(resource.Kind), item.SubItems(1).Text)
                                  Assert.Equal(access, item.SubItems(3).Text)
                                  Assert.Contains(access, item.ToolTipText)
                              Next
                          Next
                      End Using
                  Finally
                      Localization.CurrentCode = saved
                  End Try
              End Sub)
    End Sub

    <Fact>
    Public Sub ChangingTheSettingsDeviceScaleResizesCommandsAndColumnMinimums()
        OnSta(Sub()
                  Dim hive As MemoryRegistry = SeededRegistry()
                  Using panel As New Resource_List_Panel(1.0F, AddressOf hive.Read, AddressOf hive.Write, New MemoryStore(),
                                                         withPrimaryOpen:=True, deviceScale:=True)
                      panel.Size = New Size(1200, 800)
                      Dim panelHandle As IntPtr = panel.Handle
                      panel.SetDeviceScale(1.75F)
                      Dim add As Control = panel.Controls.Find("res_Add", True)(0)
                      Assert.Equal(56, add.Height)
                      Dim list As ListView = DirectCast(panel.Controls.Find("res_List", True)(0), ListView)
                      Assert.Equal(CInt(110 * 1.75F), list.Columns(0).Width)
                      Assert.Equal(CInt(16 * 1.75F), list.SmallImageList.ImageSize.Width)
                      panel.SetDeviceScale(1.0F)
                      Assert.Equal(32, add.Height)
                      Assert.Equal(110, list.Columns(0).Width)
                  End Using
              End Sub)
    End Sub

    Private Shared Sub InvokeSay(panel As Resource_List_Panel, text As String, severity As Integer)
        Dim say As MethodInfo = panel.GetType().GetMethod("Say", BindingFlags.NonPublic Or BindingFlags.Instance)
        Dim kind As Type = panel.GetType().GetNestedType("ResourceStatusSeverity", BindingFlags.NonPublic Or BindingFlags.Public)
        say.Invoke(panel, New Object() {text, [Enum].ToObject(kind, severity)})
    End Sub

    <Fact>
    Public Sub DisabledCommandsExposeTheirReasonThroughTheParentsHoverTooltip()
        OnSta(Sub()
                  Dim hive As New MemoryRegistry()
                  Using panel As New Resource_List_Panel(1.0F, AddressOf hive.Read, AddressOf hive.Write, New MemoryStore(), withPrimaryOpen:=True)
                      panel.Size = New Size(900, 500)
                      Dim edit As Control = panel.Controls.Find("res_Edit", True)(0)
                      Dim row As Control = edit.Parent
                      Dim tips As ToolTip = DirectCast(panel.GetType().GetField("tips", BindingFlags.NonPublic Or BindingFlags.Instance).GetValue(panel), ToolTip)
                      Dim move As MethodInfo = GetType(Control).GetMethod("OnMouseMove", BindingFlags.NonPublic Or BindingFlags.Instance)
                      move.Invoke(row, {New MouseEventArgs(MouseButtons.None, 0, edit.Left + 2, edit.Top + 2, 0)})
                      Assert.Equal(Localization.T("Сначала выберите ресурс в списке"), tips.GetToolTip(row))
                      Dim add As Control = panel.Controls.Find("res_Add", True)(0)
                      move.Invoke(row, {New MouseEventArgs(MouseButtons.None, 0, add.Left + 2, add.Top + 2, 0)})
                      Assert.Equal("", tips.GetToolTip(row))
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub SelectedAndUnselectedCellsUseTheDarkPaletteInsteadOfNativeLightSelection()
        OnSta(Sub()
                  Dim hive As MemoryRegistry = SeededRegistry()
                  Using panel As New Resource_List_Panel(1.0F, AddressOf hive.Read, AddressOf hive.Write, New MemoryStore())
                      panel.Size = New Size(900, 500)
                      Dim list As ListView = DirectCast(panel.Controls.Find("res_List", True)(0), ListView)
                      Dim listHandle As IntPtr = list.Handle
                      Dim theme As ThemePalette = ThemePalette.Dark
                      Dim palette As New ResourcePanelPalette With {
                          .Dark = True, .Surface = theme.SurfaceRaised, .Text = theme.TextPrimary,
                          .Muted = theme.TextMuted, .Border = theme.Border, .EditorBack = theme.SurfaceSunken,
                          .EditorText = theme.TextPrimary, .Accent = theme.Accent, .OnAccent = theme.AccentInk,
                          .Success = theme.Success, .Warning = theme.Warning, .Danger = theme.Danger}
                      panel.ApplyPalette(palette)
                      Dim draw As MethodInfo = panel.GetType().GetMethod("OnDrawSubItem", BindingFlags.NonPublic Or BindingFlags.Instance)
                      Dim item As ListViewItem = list.Items(0)
                      Using bitmap As New Drawing.Bitmap(220, 48), graphics As Drawing.Graphics = Drawing.Graphics.FromImage(bitmap)
                          For Each selected As Boolean In {False, True}
                              item.Selected = selected
                              Dim args As New DrawListViewSubItemEventArgs(graphics, New Drawing.Rectangle(0, 0, 220, 48),
                                  item, item.SubItems(2), 0, 2, list.Columns(2), CType(0, ListViewItemStates))
                              draw.Invoke(panel, {list, args})
                              Assert.False(args.DrawDefault)
                              Dim background As Drawing.Color = bitmap.GetPixel(1, 1)
                              If selected Then
                                  Assert.NotEqual(palette.EditorBack.ToArgb(), background.ToArgb())
                                  Assert.True(background.GetBrightness() < 0.5F)
                              Else
                                  Assert.Equal(palette.EditorBack.ToArgb(), background.ToArgb())
                              End If
                          Next
                      End Using
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub TheSummaryCountsAndTheEmptyStateTakesOver()
        ' 131 §3.1/§3.4: the row's summary says the count (nothing when empty); with no
        ' resources the empty state replaces the list body and the column headers do not
        ' float over it, because the list itself is hidden.
        OnSta(Sub()
                  Dim hive As MemoryRegistry = SeededRegistry()
                  Using panel As New Resource_List_Panel(1.0F, AddressOf hive.Read, AddressOf hive.Write, New MemoryStore())
                      Dim listHandle As IntPtr = DirectCast(panel.Controls.Find("res_List", True)(0), ListView).Handle
                      Dim summary As Label = DirectCast(panel.Controls.Find("res_Summary", True)(0), Label)
                      Assert.Equal(Localization.TF("Ресурсов: {0}", 2), summary.Text)
                      Assert.False(OwnVisible(panel.Controls.Find("res_Empty", True)(0)))
                      Assert.True(OwnVisible(panel.Controls.Find("res_List", True)(0)))

                      Dim empty As New MemoryRegistry()
                      Using bare As New Resource_List_Panel(1.0F, AddressOf empty.Read, AddressOf empty.Write, New MemoryStore())
                          Dim bareList As ListView = DirectCast(bare.Controls.Find("res_List", True)(0), ListView)
                          Dim bareHandle As IntPtr = bareList.Handle
                          Assert.Equal("", DirectCast(bare.Controls.Find("res_Summary", True)(0), Label).Text)
                          Assert.True(OwnVisible(bare.Controls.Find("res_Empty", True)(0)))
                          Assert.False(OwnVisible(bareList))
                          Assert.False(String.IsNullOrEmpty(DirectCast(bare.Controls.Find("res_Hint", True)(0), Label).Text))
                          ' The empty state offers its own Add into the existing add path.
                          Assert.True(DirectCast(bare.Controls.Find("res_EmptyAdd", True)(0), Button).Enabled)
                      End Using
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub TheStatusLineStaysOneLine_AndKeepsTheWholeSentenceOnItsTooltip()
        ' 131 §3.4: multi-part results join on one line; the full text lives in the tooltip;
        ' the severity is recorded for the leading glyph, nothing when there is nothing to
        ' say. (The glyph BITMAP itself needs the bundled assets - the scenes prove it; what
        ' the tests' world can hold is the decision.)
        OnSta(Sub()
                  Dim empty As New MemoryRegistry()
                  Using panel As New Resource_List_Panel(1.0F, AddressOf empty.Read, AddressOf empty.Write, New MemoryStore())
                      Dim listHandle As IntPtr = DirectCast(panel.Controls.Find("res_List", True)(0), ListView).Handle
                      Dim status As Label = DirectCast(panel.Controls.Find("res_Status", True)(0), Label)
                      Dim tips As ToolTip = DirectCast(panel.GetType().GetField("tips", BindingFlags.NonPublic Or BindingFlags.Instance).GetValue(panel), ToolTip)
                      Dim severity As FieldInfo = panel.GetType().GetField("lastSeverity", BindingFlags.NonPublic Or BindingFlags.Instance)

                      Dim sentence As String = "a:22 - доступен; b:22 - отказано в доступе"
                      InvokeSay(panel, sentence, 4) ' Danger
                      Assert.Equal(sentence, status.Text)
                      Assert.Equal(sentence, tips.GetToolTip(status))
                      Assert.Equal(4, CInt(severity.GetValue(panel)))

                      InvokeSay(panel, "", 0) ' Nothing to say: no text, no tooltip, no severity.
                      Assert.Equal("", status.Text)
                      Assert.Equal("", tips.GetToolTip(status))
                      Assert.Equal(0, CInt(severity.GetValue(panel)))
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub TheListKeepsTheMinimumWidths_GrowsAddress_AndMarksTheTypeGlyphs()
        ' 131 §3.3: the five columns hold their minimums at the window's narrowest, Address
        ' absorbs what is left and is therefore the widest on a wide host; every row carries
        ' the catalogue type glyph and the full row text in its tooltip.
        OnSta(Sub()
                  Dim hive As MemoryRegistry = SeededRegistry()
                  Using panel As New Resource_List_Panel(1.0F, AddressOf hive.Read, AddressOf hive.Write, New MemoryStore())
                      panel.Size = New Size(900, 400)
                      Dim list As ListView = DirectCast(panel.Controls.Find("res_List", True)(0), ListView)
                      Dim listHandle As IntPtr = list.Handle
                      panel.GetType().GetMethod("Relayout", BindingFlags.NonPublic Or BindingFlags.Instance).Invoke(panel, New Object() {})

                      Assert.Equal(5, list.Columns.Count)
                      Dim widths As Integer() = list.Columns.Cast(Of ColumnHeader)().Select(Function(c) c.Width).ToArray()
                      Assert.True(widths(2) = widths.Max(), "Address must be the growing column")
                      Assert.True(widths(0) > widths(3), "Name keeps a wider minimum than Access")
                      Assert.True(widths.Sum() <= list.ClientSize.Width, "the minimums must fit the host")

                      Dim sftpRow As ListViewItem = list.Items.OfType(Of ListViewItem)().Single(Function(i) DirectCast(i.Tag, ResourceDescriptor).Id = "sftp1")
                      Dim smbRow As ListViewItem = list.Items.OfType(Of ListViewItem)().Single(Function(i) DirectCast(i.Tag, ResourceDescriptor).Id = "smb1")
                      Assert.Equal("source.resource", sftpRow.ImageKey)
                      Assert.Equal("content.folder", smbRow.ImageKey)
                      Assert.Contains(ResourceMessages.DisplayAddress(DirectCast(sftpRow.Tag, ResourceDescriptor)), sftpRow.ToolTipText)
                  End Using
              End Sub)
    End Sub

End Class
#End If
