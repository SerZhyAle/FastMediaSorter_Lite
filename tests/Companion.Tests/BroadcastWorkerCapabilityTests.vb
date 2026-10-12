Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Text
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Spec 159 section 3.4 (test T-4): the host half of the broadcasts feature cannot ship alone.
'''
''' The Share Manager's broadcasts page is built (196 phase 2) and the worker that serves it is not (196 phase 1,
''' another repository). This test reads the vendored worker as bytes and holds one rule in both directions:
''' the five verb names and the <c>broadcastSupported</c> status field are ALL present in
''' <c>payload/companion/fms-share-worker.exe</c> if and only if <see cref="BroadcastPage.Offered"/> is True.
'''
''' - Offered but the worker lacks something: the page would ship with a window that never works. Fails, naming the
'''   missing verb.
''' - Not offered but the worker has everything: a worker with the verbs was vendored and the constant was
'''   not flipped in the same change. Fails, saying so.
'''
''' A byte scan proves presence, not behaviour: the behavioural proof is 016 criterion 7 against the running worker.
''' </summary>
Public Class BroadcastWorkerCapabilityTests

    ''' <summary>The five verbs of 196 section 3.4 plus the status field spec 159 section 3.1 adds, spelled as on the wire.</summary>
    Private Shared ReadOnly RequiredNames As String() = {
        "SetBroadcastPolicy", "CreateBroadcast", "ListBroadcasts", "RefreshBroadcast", "RevokeBroadcast",
        "broadcastSupported"}

    Private Shared Function WorkerPath() As String
        Return Path.Combine(ExchangeTestSupport.RepoRoot(), "payload", "companion", "fms-share-worker.exe")
    End Function

    ''' <summary>The names from <paramref name="names"/> that do not occur in <paramref name="image"/>, as ASCII.</summary>
    Friend Shared Function Missing(image As Byte(), names As IEnumerable(Of String)) As List(Of String)
        ' Latin-1 maps every byte to one char, so an ASCII name is found exactly where its bytes are.
        Dim text As String = Encoding.Latin1.GetString(image)
        Return names.Where(Function(n) text.IndexOf(n, StringComparison.Ordinal) < 0).ToList()
    End Function

    Private Shared Function ReadWorker() As Byte()
        Dim workerFile As String = WorkerPath()
        Assert.True(File.Exists(workerFile), "The vendored worker is missing: " & workerFile)
        Return File.ReadAllBytes(workerFile)
    End Function

    <Fact>
    Public Sub The_page_is_offered_exactly_when_the_vendored_worker_has_the_verbs()
        Dim missingNames As List(Of String) = Missing(ReadWorker(), RequiredNames)

        If BroadcastPage.Offered Then
            Assert.True(missingNames.Count = 0,
                        "BroadcastPage.Offered is True, but the vendored worker (payload/companion/fms-share-worker.exe) lacks: " &
                        String.Join(", ", missingNames) & ". The page would ship with a window that never works - re-vendor a worker " &
                        "that has the broadcast verbs, or set Offered back to False.")
        Else
            Assert.True(missingNames.Count > 0,
                        "The vendored worker now has every broadcast verb and broadcastSupported, but BroadcastPage.Offered is still " &
                        "False. Flip it in the SAME change that re-vendors the worker and updates fms-share-worker.exe.sha256.")
        End If
    End Sub

    ''' <summary>Proves the scan reads the right file and finds what is there: a verb the worker has always had is
    ''' found, a made-up name is not. Without it the guard above could pass because the scan finds nothing at all.</summary>
    <Fact>
    Public Sub The_scan_finds_a_verb_the_worker_does_have_and_misses_one_it_does_not()
        Dim image As Byte() = ReadWorker()
        Assert.True(image.Length > 1000000, "The vendored worker is implausibly small: " & image.Length.ToString() & " bytes")
        Assert.Empty(Missing(image, {"SetSharedFolders"}))
        Assert.Equal({"NoSuchVerbAnywhere"}, Missing(image, {"NoSuchVerbAnywhere"}).ToArray())
    End Sub

    <Fact>
    Public Sub The_scan_reports_every_missing_name_in_a_synthetic_image()
        Dim image As Byte() = Encoding.ASCII.GetBytes("xx SetBroadcastPolicy yy ListBroadcasts zz")
        Assert.Equal({"CreateBroadcast", "RefreshBroadcast", "RevokeBroadcast", "broadcastSupported"},
                     Missing(image, RequiredNames).ToArray())
    End Sub

End Class
