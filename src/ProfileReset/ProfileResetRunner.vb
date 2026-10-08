#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq

Public Enum ProfileResetStatus
    None
    Applied
    Skipped
    PartialSuccess
    Expired
End Enum

''' <summary>
''' Represents the outcome of applying a pending profile reset.
''' </summary>
Public NotInheritable Class ProfileResetOutcome

    Public ReadOnly Property Status As ProfileResetStatus
    Public ReadOnly Property BackupPath As String
    Public ReadOnly Property FailedCount As Integer
    Public ReadOnly Property TotalCount As Integer
    Public ReadOnly Property FailureCause As String

    Public Sub New(status As ProfileResetStatus,
                   Optional backupPath As String = Nothing,
                   Optional failedCount As Integer = 0,
                   Optional totalCount As Integer = 0,
                   Optional failureCause As String = Nothing)
        Me.Status = status
        Me.BackupPath = backupPath
        Me.FailedCount = failedCount
        Me.TotalCount = totalCount
        Me.FailureCause = failureCause
    End Sub

End Class

''' <summary>
''' Specification 169 §5, §6: Coordinates profile reset request and delayed execution at startup.
''' </summary>
Public NotInheritable Class ProfileResetRunner

    Private Sub New()
    End Sub

    Private Shared _lastOutcome As ProfileResetOutcome

    ''' <summary>Gets the outcome of the most recent ApplyPending run, if any.</summary>
    Public Shared ReadOnly Property LastOutcome As ProfileResetOutcome
        Get
            Return _lastOutcome
        End Get
    End Property

    ''' <summary>Returns the recorded outcome once and clears it.</summary>
    Public Shared Function ConsumeOutcome() As ProfileResetOutcome
        Dim outcome = _lastOutcome
        _lastOutcome = Nothing
        Return outcome
    End Function

    ''' <summary>Checks whether any of the reset scope settings are currently saved in the hive.</summary>
    Public Shared Function HasAnyResetSettings(store As IProfileStore) As Boolean
        If store Is Nothing Then Return False
        Dim snapshot = store.Snapshot()
        For Each key In snapshot.Keys
            If ProfileResetScope.IsResetName(key) Then Return True
        Next
        Return False
    End Function

    ''' <summary>Records the reset marker in the store.</summary>
    Public Shared Sub RecordResetRequest(store As IProfileStore, Optional clock As Func(Of DateTime) = Nothing)
        If store Is Nothing Then Throw New ArgumentNullException(NameOf(store))
        Dim getNow As Func(Of DateTime) = If(clock, Function() DateTime.UtcNow)
        Dim nowUtc As DateTime = getNow()
        Dim markerValue As String = "1|" & nowUtc.ToString("o", CultureInfo.InvariantCulture)
        store.WriteMarker(markerValue)
    End Sub

    ''' <summary>Mainline startup entry point using real stores and system services.</summary>
    Public Shared Function ApplyPending() As ProfileResetOutcome
        Dim store As New VbSettingsProfileStore()
        Dim outcome As ProfileResetOutcome = ApplyPending(
            store,
            Function(values) RegBackup.CreateBackup(values),
            Function() DateTime.UtcNow,
            AddressOf AppFileLogger.WriteLine,
            AddressOf AppFileLogger.LogException)
        _lastOutcome = outcome
        Return outcome
    End Function

    ''' <summary>Testable core of pending reset application.</summary>
    Public Shared Function ApplyPending(store As IProfileStore,
                                        backupWriter As Func(Of IReadOnlyDictionary(Of String, String), String),
                                        Optional clock As Func(Of DateTime) = Nothing,
                                        Optional logLine As Action(Of String) = Nothing,
                                        Optional logException As Action(Of String, Exception) = Nothing) As ProfileResetOutcome
        If store Is Nothing Then Throw New ArgumentNullException(NameOf(store))
        If backupWriter Is Nothing Then Throw New ArgumentNullException(NameOf(backupWriter))

        Dim getNow As Func(Of DateTime) = If(clock, Function() DateTime.UtcNow)
        Dim log As Action(Of String) = If(logLine, Sub(msg) AppFileLogger.WriteLine(msg))
        Dim logEx As Action(Of String, Exception) = If(logException, Sub(msg, ex) AppFileLogger.LogException(msg, ex))

        ' 5.4.a: no marker: return
        Dim marker As String = store.ReadMarker()
        If String.IsNullOrWhiteSpace(marker) Then
            Return New ProfileResetOutcome(ProfileResetStatus.None)
        End If

        ' 5.4.b: marker unreadable or expired
        Dim parts As String() = marker.Split("|"c)
        Dim reqTime As DateTime
        If parts.Length <> 2 OrElse parts(0) <> "1" OrElse Not DateTime.TryParse(parts(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, reqTime) Then
            log("Profile reset: marker is unreadable, clearing marker")
            store.ClearMarker()
            Return New ProfileResetOutcome(ProfileResetStatus.None)
        End If

        Dim nowUtc As DateTime = getNow()
        If (nowUtc - reqTime).TotalDays > 7.0R Then
            log("Profile reset: marker is older than 7 days, clearing marker")
            store.ClearMarker()
            Return New ProfileResetOutcome(ProfileResetStatus.Expired)
        End If

        ' 5.4.c: take snapshot of owned values (GetAllSettings filtered to 4.1 names)
        Dim fullSnapshot As IReadOnlyDictionary(Of String, String) = store.Snapshot()
        Dim ownedValues As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        For Each kvp In fullSnapshot
            If ProfileResetScope.IsResetName(kvp.Key) Then
                ownedValues(kvp.Key) = kvp.Value
            End If
        Next

        ' Write backup; if it cannot be written/read back identical: delete nothing, outcome Skipped
        Dim backupPath As String = Nothing
        Try
            backupPath = backupWriter(ownedValues)
        Catch ex As Exception
            logEx("Profile reset: backup failed, reset skipped", ex)
            store.ClearMarker()
            Return New ProfileResetOutcome(ProfileResetStatus.Skipped, failureCause:=FailureText.Describe(ex))
        End Try

        ' 5.4.d: delete each owned name, continuing past a failure
        Dim failedNames As New List(Of String)()
        Dim resetNames = ProfileResetScope.ResetNames
        For Each name In resetNames
            Try
                store.Delete(name)
            Catch ex As Exception
                failedNames.Add(name)
                logEx("Profile reset: delete failed for " & name, ex)
            End Try
        Next

        ' 5.4.e: remove marker, log counts and names (never values)
        store.ClearMarker()
        Dim successCount As Integer = resetNames.Count - failedNames.Count
        log("Profile reset: applied. Successfully reset " & successCount & " of " & resetNames.Count & " keys.")
        If failedNames.Count > 0 Then
            log("Profile reset: failed keys: " & String.Join(", ", failedNames))
            Return New ProfileResetOutcome(ProfileResetStatus.PartialSuccess, backupPath:=backupPath, failedCount:=failedNames.Count, totalCount:=resetNames.Count)
        Else
            Return New ProfileResetOutcome(ProfileResetStatus.Applied, backupPath:=backupPath)
        End If
    End Function

End Class
#End If
