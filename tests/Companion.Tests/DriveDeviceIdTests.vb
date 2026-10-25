Option Strict On

Imports System.IO
Imports System.Text
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Specification 203 section 5.9 / 5.10 (T-UI-1), the device id of a PC that signs in to Google Drive: one id, created once on
''' the first explicit sign-in, kept beside the token, preferring the id of the remote-server account when there is one.
''' Asking for the id (<see cref="DriveDeviceId.Peek"/>) reads and creates nothing.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class DriveDeviceIdTests
    Implements IDisposable

    Private Const IdServer As String = "AAAAAAAAAAAAAAAAAAAAAA"
    Private Const IdOther As String = "BBBBBBBBBBBBBBBBBBBBBA"

    Private ReadOnly _temp As New List(Of String)

    Private Function NewFolder() As String
        Dim path As String = Directory.CreateTempSubdirectory("fmsdid").FullName
        _temp.Add(path)
        Return path
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each path As String In _temp
            Try
                Directory.Delete(path, True)
            Catch ex As IOException
            End Try
        Next
    End Sub

    ''' <summary>Hands out ids from a list and counts how many were asked for.</summary>
    Private NotInheritable Class IdSource
        Private ReadOnly _ids As Queue(Of String)
        Public Asked As Integer

        Public Sub New(ParamArray ids As String())
            _ids = New Queue(Of String)(ids)
        End Sub

        Public Function [Next]() As String
            Asked += 1
            Return If(_ids.Count > 0, _ids.Dequeue(), "")
        End Function
    End Class

    Private NotInheritable Class ServerAccount
        Public Id As String = ""
        Public Function Read() As String
            Return Id
        End Function
    End Class

    <Fact>
    Public Sub Asking_for_the_id_reads_and_creates_nothing_on_a_PC_that_never_signed_in()
        Dim folder As String = Path.Combine(NewFolder(), "exchange")
        Dim source As New IdSource(IdServer)
        Dim ids As New DriveDeviceId(folder, Function() "", AddressOf source.Next)

        Assert.Equal("", ids.Peek())
        Assert.False(Directory.Exists(folder))
        Assert.False(File.Exists(ids.FilePath))
        Assert.Equal(0, source.Asked)
    End Sub

    <Fact>
    Public Sub The_first_sign_in_creates_one_id_and_every_later_one_reuses_it()
        Dim folder As String = Path.Combine(NewFolder(), "exchange")
        Dim source As New IdSource(IdOther, IdServer)
        Dim ids As New DriveDeviceId(folder, Function() "", AddressOf source.Next)

        Dim first As String = ids.Resolve()
        Assert.Equal(IdOther, first)
        Assert.Equal(first, ids.Peek())
        Assert.Equal(first, File.ReadAllText(ids.FilePath, Encoding.ASCII).Trim())
        Assert.Equal(Path.Combine(folder, DriveDeviceId.FileName), ids.FilePath)

        Assert.Equal(first, ids.Resolve())
        Assert.Equal(first, New DriveDeviceId(folder, Function() "", AddressOf source.Next).Resolve())
        Assert.Equal(1, source.Asked)
        Assert.Equal(New String() {DriveDeviceId.FileName}, Directory.GetFiles(folder).Select(Function(f) Path.GetFileName(f)).ToArray())
    End Sub

    <Fact>
    Public Sub The_id_of_the_remote_server_account_is_the_id_in_the_channel_and_is_written_beside_the_token()
        Dim folder As String = Path.Combine(NewFolder(), "exchange")
        Dim account As New ServerAccount With {.Id = IdServer}
        Dim source As New IdSource(IdOther)
        Dim ids As New DriveDeviceId(folder, AddressOf account.Read, AddressOf source.Next)

        Assert.Equal(IdServer, ids.Resolve())
        Assert.Equal(IdServer, ids.Peek())
        Assert.Equal(0, source.Asked)
    End Sub

    <Fact>
    Public Sub An_id_made_before_the_account_existed_gives_way_to_the_accounts_id()
        Dim folder As String = Path.Combine(NewFolder(), "exchange")
        Dim account As New ServerAccount()
        Dim source As New IdSource(IdOther)
        Dim ids As New DriveDeviceId(folder, AddressOf account.Read, AddressOf source.Next)

        Assert.Equal(IdOther, ids.Resolve())
        account.Id = IdServer
        Assert.Equal(IdServer, ids.Resolve())
        Assert.Equal(IdServer, ids.Peek())
    End Sub

    <Fact>
    Public Sub The_id_stays_stable_after_the_account_is_left()
        Dim folder As String = Path.Combine(NewFolder(), "exchange")
        Dim account As New ServerAccount With {.Id = IdServer}
        Dim source As New IdSource(IdOther)
        Dim ids As New DriveDeviceId(folder, AddressOf account.Read, AddressOf source.Next)

        Assert.Equal(IdServer, ids.Resolve())
        account.Id = ""
        Assert.Equal(IdServer, ids.Resolve())
        Assert.Equal(0, source.Asked)
    End Sub

    <Fact>
    Public Sub A_server_id_that_is_not_an_id_is_ignored()
        Dim folder As String = Path.Combine(NewFolder(), "exchange")
        Dim account As New ServerAccount With {.Id = "not an id"}
        Dim source As New IdSource(IdOther)
        Dim ids As New DriveDeviceId(folder, AddressOf account.Read, AddressOf source.Next)

        Assert.Equal(IdOther, ids.Resolve())
    End Sub

    <Fact>
    Public Sub A_registry_that_cannot_be_read_does_not_stop_the_sign_in()
        Dim folder As String = Path.Combine(NewFolder(), "exchange")
        Dim source As New IdSource(IdOther)
        Dim ids As New DriveDeviceId(folder, Function() As String
                                                 Throw New UnauthorizedAccessException("registry")
                                             End Function, AddressOf source.Next)

        Assert.Equal(IdOther, ids.Resolve())
    End Sub

    <Fact>
    Public Sub A_damaged_file_is_replaced_by_a_new_id_not_returned()
        Dim folder As String = Path.Combine(NewFolder(), "exchange")
        Directory.CreateDirectory(folder)
        File.WriteAllText(Path.Combine(folder, DriveDeviceId.FileName), "garbage that is not an id", Encoding.ASCII)
        Dim source As New IdSource(IdOther)
        Dim ids As New DriveDeviceId(folder, Function() "", AddressOf source.Next)

        Assert.Equal("", ids.Peek())
        Assert.Equal(IdOther, ids.Resolve())
        Assert.Equal(IdOther, ids.Peek())
    End Sub

    <Fact>
    Public Sub An_id_that_could_not_be_kept_is_not_handed_out()
        ' the "folder" is a file, so nothing can be written there: an id that would change at the next start is worse than none
        Dim blocker As String = Path.Combine(NewFolder(), "exchange")
        File.WriteAllText(blocker, "x")
        Dim source As New IdSource(IdOther)
        Dim ids As New DriveDeviceId(blocker, Function() "", AddressOf source.Next)

        Assert.Equal("", ids.Resolve())
        Assert.Equal("", ids.Peek())
    End Sub

    <Fact>
    Public Sub A_new_id_that_is_not_an_id_is_refused_and_nothing_is_written()
        Dim folder As String = Path.Combine(NewFolder(), "exchange")
        Dim source As New IdSource("short")
        Dim ids As New DriveDeviceId(folder, Function() "", AddressOf source.Next)

        Assert.Equal("", ids.Resolve())
        Assert.False(File.Exists(ids.FilePath))
    End Sub

    <Fact>
    Public Sub The_default_source_makes_an_id_of_the_shape_every_record_is_named_by()
        Dim folder As String = Path.Combine(NewFolder(), "exchange")
        Dim ids As New DriveDeviceId(folder, Function() "")

        Dim id As String = ids.Resolve()
        Assert.True(ExchangeIds.IsValidId(id), "the id is not in the id form: " & id)
        Assert.Equal(id, ids.Resolve())
    End Sub

    <Fact>
    Public Sub A_folder_is_required()
        Assert.Throws(Of ArgumentException)(Function() New DriveDeviceId("  "))
    End Sub

End Class
