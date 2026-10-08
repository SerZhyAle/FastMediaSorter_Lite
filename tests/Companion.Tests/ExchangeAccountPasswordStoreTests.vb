Option Strict On

Imports System.IO
Imports System.Reflection
Imports System.Security.Cryptography
Imports System.Text
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The keeping of account passwords (ExchangeAccountPasswordStore, ExchangeSettings.KeepAccountPasswords): off by default, a DPAPI
''' file that is never readable text, one password per login, a damaged or foreign file reads as nothing, a refused protection saves
''' nothing and says so, and the dialogs tell the truth about whether a password can be shown again. The dialogs are built and
''' inspected, never shown: a shown MessageBox would stop a test run.
''' </summary>
Public Class ExchangeAccountPasswordStoreTests

    Private Const Alice As String = "alice"
    Private Const Bob As String = "bob"
    Private Const PwAlice As String = "Abcdefghi1"
    Private Const PwBob As String = "Zyxwvuts9Q"

    Private Shared Function NewFolder() As String
        Return Path.Combine(Path.GetTempPath(), "fms-account-passwords-" & Guid.NewGuid().ToString("N"))
    End Function

    Private Shared Sub Cleanup(folder As String)
        Try
            If Directory.Exists(folder) Then Directory.Delete(folder, True)
        Catch ex As IOException
        End Try
    End Sub

    Private Shared Function Field(Of T)(obj As Object, name As String) As T
        Return DirectCast(obj.GetType().GetField(name, BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(obj), T)
    End Function

    ''' <summary>A protection that is always refused, as a damaged profile or a locked key store would refuse it.</summary>
    Friend NotInheritable Class RefusingCipher
        Implements IExchangeSecretCipher

        Public Function Protect(plain As Byte()) As Byte() Implements IExchangeSecretCipher.Protect
            Throw New CryptographicException("refused by the test")
        End Function

        Public Function Unprotect(blob As Byte()) As Byte() Implements IExchangeSecretCipher.Unprotect
            Throw New CryptographicException("refused by the test")
        End Function
    End Class

    ' --- the switch ---

    <Fact>
    Public Sub Keeping_is_off_by_default()
        Assert.False((New ExchangeSettings()).KeepAccountPasswords)
    End Sub

    ' --- the file ---

    <Fact>
    Public Sub A_read_or_a_removal_of_nothing_creates_nothing()
        Dim folder As String = NewFolder()
        Try
            Dim store As New ExchangeAccountPasswordStore(folder, New ExchangeDeviceAgentTests.XorCipher())
            Dim got As String = "unset"
            Assert.False(store.TryLoad(Alice, got))
            Assert.Equal("", got)
            Assert.True(store.Remove(Alice))
            store.Clear()
            Assert.False(Directory.Exists(folder))
        Finally
            Cleanup(folder)
        End Try
    End Sub

    <Fact>
    Public Sub Each_login_keeps_its_own_password_and_a_new_save_replaces_it()
        Dim folder As String = NewFolder()
        Try
            Dim store As New ExchangeAccountPasswordStore(folder, New ExchangeDeviceAgentTests.XorCipher())
            Assert.True(store.Save(Alice, PwAlice))
            Assert.True(store.Save(Bob, PwBob))
            Dim got As String = ""
            Assert.True(store.TryLoad(Alice, got))
            Assert.Equal(PwAlice, got)
            Assert.True(store.TryLoad(Bob, got))
            Assert.Equal(PwBob, got)
            Assert.False(store.TryLoad("carol", got))
            Assert.Equal("", got)
            Assert.True(store.Save(Alice, PwBob))
            Assert.True(store.TryLoad(Alice, got))
            Assert.Equal(PwBob, got)
        Finally
            Cleanup(folder)
        End Try
    End Sub

    <Fact>
    Public Sub The_file_is_protected_text_holding_neither_the_password_nor_the_login()
        Dim folder As String = NewFolder()
        Try
            Dim store As New ExchangeAccountPasswordStore(folder, Nothing)   ' the real DPAPI cipher
            Assert.True(store.Save(Alice, PwAlice))
            Dim raw As String = Encoding.Latin1.GetString(File.ReadAllBytes(store.FilePath))
            Assert.DoesNotContain(PwAlice, raw, StringComparison.Ordinal)
            Assert.DoesNotContain(Alice, raw, StringComparison.Ordinal)
            Dim got As String = ""
            Assert.True(store.TryLoad(Alice, got))
            Assert.Equal(PwAlice, got)
        Finally
            Cleanup(folder)
        End Try
    End Sub

    <Fact>
    Public Sub Remove_forgets_one_login_and_the_last_removal_deletes_the_file()
        Dim folder As String = NewFolder()
        Try
            Dim store As New ExchangeAccountPasswordStore(folder, New ExchangeDeviceAgentTests.XorCipher())
            Assert.True(store.Save(Alice, PwAlice))
            Assert.True(store.Save(Bob, PwBob))
            Assert.True(store.Remove(Alice))
            Dim got As String = ""
            Assert.False(store.TryLoad(Alice, got))
            Assert.True(store.TryLoad(Bob, got))
            Assert.Equal(PwBob, got)
            Assert.True(File.Exists(store.FilePath))
            Assert.True(store.Remove(Bob))
            Assert.False(File.Exists(store.FilePath))
        Finally
            Cleanup(folder)
        End Try
    End Sub

    <Fact>
    Public Sub Clear_removes_every_kept_password_at_once()
        Dim folder As String = NewFolder()
        Try
            Dim store As New ExchangeAccountPasswordStore(folder, New ExchangeDeviceAgentTests.XorCipher())
            Assert.True(store.Save(Alice, PwAlice))
            Assert.True(store.Save(Bob, PwBob))
            store.Clear()
            Assert.False(File.Exists(store.FilePath))
            Dim got As String = ""
            Assert.False(store.TryLoad(Alice, got))
            Assert.False(store.TryLoad(Bob, got))
        Finally
            Cleanup(folder)
        End Try
    End Sub

    <Fact>
    Public Sub A_damaged_file_reads_as_nothing_and_a_save_replaces_it()
        Dim folder As String = NewFolder()
        Try
            Directory.CreateDirectory(folder)
            Dim store As New ExchangeAccountPasswordStore(folder, Nothing)
            File.WriteAllBytes(store.FilePath, {1, 2, 3, 4, 5})
            Dim got As String = ""
            Assert.False(store.TryLoad(Alice, got))
            Assert.Equal("", got)
            Assert.True(store.Save(Alice, PwAlice))
            Assert.True(store.TryLoad(Alice, got))
            Assert.Equal(PwAlice, got)
        Finally
            Cleanup(folder)
        End Try
    End Sub

    <Fact>
    Public Sub A_refused_protection_saves_nothing_and_says_so()
        Dim folder As String = NewFolder()
        Try
            Dim store As New ExchangeAccountPasswordStore(folder, New RefusingCipher())
            Assert.False(store.Save(Alice, PwAlice))
            Assert.False(File.Exists(store.FilePath))
            ' the protection comes before anything is created, so not even the folder is
            Assert.False(Directory.Exists(folder))
        Finally
            Cleanup(folder)
        End Try
    End Sub

    <Fact>
    Public Sub An_empty_login_or_password_is_refused()
        Dim folder As String = NewFolder()
        Try
            Dim store As New ExchangeAccountPasswordStore(folder, New ExchangeDeviceAgentTests.XorCipher())
            Assert.False(store.Save("", PwAlice))
            Assert.False(store.Save(Alice, ""))
            Assert.False(File.Exists(store.FilePath))
        Finally
            Cleanup(folder)
        End Try
    End Sub

    ' --- the rule, in the source ---

    <Fact>
    Public Sub The_store_is_a_file_under_dpapi_and_never_the_registry_or_a_log()
        Dim code As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Core\ExchangeAccountPasswordStore.vb"))
        For Each forbidden As String In {"SaveSetting(", "GetSetting(", "Registry", "AppFileLogger", "Debug.WriteLine", "Console."}
            Assert.DoesNotContain(forbidden, code)
        Next
        Assert.Contains("ProtectedData.Protect(", code)
        Assert.Contains("ProtectedData.Unprotect(", code)
    End Sub

    <Fact>
    Public Sub Switching_keeping_off_asks_first_with_the_default_on_no_and_deletes_what_was_kept()
        Dim admin As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\Share_Settings_Form.ExchangeAdmin.vb"))
        Dim body As String = ExchangeTestSupport.MethodBody(admin, "Private Sub OnXKeepPasswordsChanged(")
        Assert.Contains("MessageBoxDefaultButton.Button2", body)
        Assert.Contains("ExchangeAccountPasswordStore.CreateDefault().Clear()", body)
        Assert.Contains("_chkXKeepPasswords.Checked = Not wanted", body)
    End Sub

    <Fact>
    Public Sub A_removed_account_takes_its_kept_password_with_it()
        Dim admin As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Forms\Share_Settings_Form.ExchangeAdmin.vb"))
        Dim body As String = ExchangeTestSupport.MethodBody(admin, "Private Async Sub OnXRemoveAccount(")
        Assert.Contains("ExchangeAccountPasswordStore.CreateDefault().Remove(login)", body)
    End Sub

    <Fact>
    Public Sub A_kept_password_is_not_asked_about_on_close_and_an_unkept_one_still_is()
        Dim body As String = ExchangeTestSupport.MethodBody(ExchangeTestSupport.ReadSource("Forms\Exchange_Password_Form.vb"), "Protected Overrides Sub OnFormClosing(")
        Assert.Contains("Not _retrievable", body)
        Assert.Contains("MessageBoxDefaultButton.Button2", body)
    End Sub

    ' --- the dialogs, built and inspected, never shown ---

    <Fact>
    Public Sub The_kept_dialog_shows_the_password_masked_under_the_login()
        ExchangeTestSupport.OnUi(Sub()
                                     Using dlg As New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Kept, PwAlice, Alice, True)
                                         Dim txt As TextBox = Field(Of TextBox)(dlg, "_txt")
                                         Assert.True(txt.ReadOnly)
                                         Assert.True(txt.UseSystemPasswordChar)
                                         Assert.Equal(PwAlice, txt.Text)
                                         Assert.Contains(Alice, dlg.Text, StringComparison.Ordinal)
                                         ' a kept password is shown again from the account row, so it is not offered as a new one
                                         Assert.Null(Field(Of Button)(dlg, "_btnOwn"))
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub The_generated_dialog_offers_the_persons_own_password_and_reports_the_choice()
        ExchangeTestSupport.OnUi(Sub()
                                     Using dlg As New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Generated, "Hq7Tm2Zk9Xv4Bn8Rc3Wd", Alice)
                                         Assert.NotNull(Field(Of Button)(dlg, "_btnOwn"))
                                         Assert.False(dlg.ChoseOwnPassword)
                                         GetType(Exchange_Password_Form).GetMethod("OnOwnClicked", BindingFlags.Instance Or BindingFlags.NonPublic).
                                             Invoke(dlg, New Object() {Nothing, EventArgs.Empty})
                                         Assert.True(dlg.ChoseOwnPassword)
                                         Assert.Equal(DialogResult.Retry, dlg.DialogResult)
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub A_reset_opened_from_the_own_password_way_starts_on_typed_and_an_ordinary_one_on_generated()
        ExchangeTestSupport.OnUi(Sub()
                                     Using typed As New Exchange_Account_Admin_Form(Exchange_Account_Admin_Form.DialogMode.Reset, Alice, False, True)
                                         Assert.True(Field(Of RadioButton)(typed, "_rbType").Checked)
                                         Assert.False(Field(Of RadioButton)(typed, "_rbGenerate").Checked)
                                     End Using
                                     Using ordinary As New Exchange_Account_Admin_Form(Exchange_Account_Admin_Form.DialogMode.Reset, Alice)
                                         Assert.True(Field(Of RadioButton)(ordinary, "_rbGenerate").Checked)
                                         Assert.False(Field(Of RadioButton)(ordinary, "_rbType").Checked)
                                     End Using
                                 End Sub)
    End Sub

End Class
