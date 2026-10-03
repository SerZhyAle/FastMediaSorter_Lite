Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

<Collection("AppFileLogger")>
Public Class LifecycleAuditFollowupTests

    ' --------------------------------------------------------------- 057.S15-3 ----
    ' History_Form bounds concurrent probes, checks form disposal, observes faults

    <Fact>
    Public Sub S15_3_HistoryForm_BoundsProbesAndChecksDisposal()
        Dim source As String = ReadSource("ExpandedSettingsDialogs.vb")
        ' Wiring only: the behaviour (permits, queue cap, coalescing, cancellation) is proven by
        ' ProbeSchedulerTests against the very scheduler this dialog submits to.
        Assert.Contains("HistoryProbes.Submit(Me, key", source)
        Assert.Contains("HistoryProbes.CancelOwner(Me)", source)
        Assert.DoesNotContain("SemaphoreSlim", source)
    End Sub

    ' --------------------------------------------------------------- 057.S15-4 ----
    ' LogPackage BoundedReadStream caps reading to snapshot length

#If Not NETFRAMEWORK Then
    <Fact>
    Public Sub S15_4_BoundedReadStream_RespectsByteCapEvenIfUnderlyingGrows()
        Dim initialData As Byte() = Encoding.UTF8.GetBytes("Initial Log Content Header" & vbCrLf)
        Using ms As New MemoryStream()
            ms.Write(initialData, 0, initialData.Length)
            ms.Position = 0

            ' Create BoundedReadStream capped at initial length
            Using bounded As New BoundedReadStream(ms, initialData.Length)
                ' Now simulate concurrent append to underlying stream
                Dim extraData As Byte() = Encoding.UTF8.GetBytes("Appended extra data while reading" & vbCrLf)
                Dim curPos = ms.Position
                ms.Seek(0, SeekOrigin.End)
                ms.Write(extraData, 0, extraData.Length)
                ms.Position = curPos

                ' Read from bounded stream to EOF
                Using reader As New StreamReader(bounded, Encoding.UTF8)
                    Dim readContent As String = reader.ReadToEnd()
                    Assert.Equal("Initial Log Content Header" & vbCrLf, readContent)
                End Using
            End Using
        End Using
    End Sub
#End If

    ' --------------------------------------------------------------- 057.S15-5 ----
    ' MailSender non-MAPI exceptions return Failed and do not crash or suppress fallback

    <Fact>
    Public Sub S15_5_MailSender_NonMapiExceptionReturnsFailed()
        Dim tempFile = Path.GetTempFileName()
        Try
            Dim unicodeCalled As Boolean = False
            Dim ansiCalled As Boolean = False

            Dim unicodeDelegate As UnicodeMapiSendDelegate =
                Function(path, subject, body, owner, ByRef rc)
                    unicodeCalled = True
                    rc = 0
                    Return UnicodeMapiResult.ExecutionFailed
                End Function

            Dim ansiDelegate As AnsiMapiSendDelegate =
                Function(path, subject, body, owner, ByRef rc)
                    ansiCalled = True
                    rc = 0
                    Return True
                End Function

            Dim outcome = MailSender.SendCore(tempFile, "Subject", "Body", IntPtr.Zero,
                                              unicodeDelegate, ansiDelegate)

            Assert.True(unicodeCalled)
            Assert.False(ansiCalled) ' ExecutionFailed must NOT fall back to ANSI
            Assert.Equal(MailSendOutcome.Failed, outcome)
        Finally
            Try : File.Delete(tempFile) : Catch : End Try
        End Try
    End Sub

    <Fact>
    Public Sub S15_5_MailSender_MapiNotSupportedFallsBackToAnsi()
        Dim tempFile = Path.GetTempFileName()
        Try
            Dim unicodeCalled As Boolean = False
            Dim ansiCalled As Boolean = False

            Dim unicodeDelegate As UnicodeMapiSendDelegate =
                Function(path, subject, body, owner, ByRef rc)
                    unicodeCalled = True
                    rc = 26 ' MAPI_E_NOT_SUPPORTED
                    Return UnicodeMapiResult.NotSupported
                End Function

            Dim ansiDelegate As AnsiMapiSendDelegate =
                Function(path, subject, body, owner, ByRef rc)
                    ansiCalled = True
                    rc = 0 ' SUCCESS_SUCCESS
                    Return True
                End Function

            Dim outcome = MailSender.SendCore(tempFile, "Subject", "Body", IntPtr.Zero,
                                              unicodeDelegate, ansiDelegate)

            Assert.True(unicodeCalled)
            Assert.True(ansiCalled)
            Assert.Equal(MailSendOutcome.Sent, outcome)
        Finally
            Try : File.Delete(tempFile) : Catch : End Try
        End Try
    End Sub

    <Fact>
    Public Sub S15_5_MailSender_UserAbortReturnsCancelled()
        Dim tempFile = Path.GetTempFileName()
        Try
            Dim unicodeDelegate As UnicodeMapiSendDelegate =
                Function(path, subject, body, owner, ByRef rc)
                    rc = 1 ' MAPI_E_USER_ABORT
                    Return UnicodeMapiResult.Answered
                End Function

            Dim outcome = MailSender.SendCore(tempFile, "Subject", "Body", IntPtr.Zero,
                                              unicodeDelegate, Nothing)

            Assert.Equal(MailSendOutcome.Cancelled, outcome)
        Finally
            Try : File.Delete(tempFile) : Catch : End Try
        End Try
    End Sub

    ' --------------------------------------------------------------- 057.S15-6 ----
    ' SingleInstanceProtocol payload bounds and validation

    <Fact>
    Public Sub S15_6_SingleInstanceProtocol_RejectsOversizedPayload()
        Dim oversizedByteCount As Integer = SingleInstanceProtocol.MaxPayloadBytes + 100
        Dim fakePtr As New IntPtr(12345)
        Assert.False(SingleInstanceProtocol.IsValidPayload(oversizedByteCount, fakePtr))

        Dim decoded As String = Nothing
        Assert.False(SingleInstanceProtocol.TryDecodePayload(oversizedByteCount, fakePtr, decoded))
        Assert.Equal(String.Empty, decoded)
    End Sub

    <Fact>
    Public Sub S15_6_SingleInstanceProtocol_RejectsNegativeOrZeroOrNullPayload()
        Dim fakePtr As New IntPtr(12345)
        Assert.False(SingleInstanceProtocol.IsValidPayload(0, fakePtr))
        Assert.False(SingleInstanceProtocol.IsValidPayload(-5, fakePtr))
        Assert.False(SingleInstanceProtocol.IsValidPayload(100, IntPtr.Zero))
    End Sub

    <Fact>
    Public Sub S15_6_SingleInstanceProtocol_DecodesValidPayload()
        Dim path As String = "C:\Test\Image file 123.jpg"
        Dim rawBytes As Byte() = Encoding.UTF8.GetBytes(path)
        Dim hGlobal As IntPtr = Marshal.AllocHGlobal(rawBytes.Length)
        Try
            Marshal.Copy(rawBytes, 0, hGlobal, rawBytes.Length)

            Assert.True(SingleInstanceProtocol.IsValidPayload(rawBytes.Length, hGlobal))

            Dim decoded As String = Nothing
            Assert.True(SingleInstanceProtocol.TryDecodePayload(rawBytes.Length, hGlobal, decoded))
            Assert.Equal(path, decoded)
        Finally
            Marshal.FreeHGlobal(hGlobal)
        End Try
    End Sub

    ' --------------------------------------------------------------- 057.S15-7 ----
    ' File_Types_Form empty selection handling

    <Fact>
    Public Sub S15_7_FileTypesForm_PreventsEmptyAcceptance()
        Dim source As String = ReadSource("ExpandedSettingsDialogs.vb")
        ' Wiring only: zero / all / subset behaviour is FileTypeSelectionTests, on the very function this calls.
        Assert.Contains("FileTypeSelection.Evaluate(", source)
        Assert.Contains("btnOk.Enabled = CurrentVerdict().CanAccept", source)
    End Sub

    ' --------------------------------------------------------------- 057.S13-7 ----
    ' Application_Events process disposal and target disappearance resilience

    <Fact>
    Public Sub S13_7_ApplicationEvents_DisposesProcessesSafely()
        Dim source As String = ReadSource("Application_Events.vb")
        ' Wiring only: that every Process is disposed and a vanishing target costs only itself is
        ' proven by CopyDataTransportTests against ViewerProcessDiscovery, the code this calls.
        Assert.Contains("ViewerProcessDiscovery.Discover(", source)
        Assert.Contains("GetRunningViewerTargets", source)
    End Sub

    ' --------------------------------------------------------------- 057.S13-8 & S13-9 ----
    ' Share launcher acknowledgement and elevated instance detection

    <Fact>
    Public Sub S13_8_S13_9_ShareLauncher_RequiresAckAndHandlesElevation()
        Dim source As String = ReadSource("Main_Form.ShareLauncher.vb")
        ' Wiring only: ack-1 delivery (CopyDataTransportTests), the version-pair plan and the
        ' access-denied-means-present hint (ShareActivationPolicyTests) are behavioural.
        Assert.Contains("sendResult = New IntPtr(1)", source)
        Assert.Contains("ShareActivationPolicy.ExistingInstanceHint(", source)
    End Sub

    ' --------------------------------------------------------------- 057.S13-10 & S13-12 ----
    ' AppFileLogger rotation staging recovery and transactional initialization

    <Fact>
    Public Sub S13_10_LoggerRotation_HandlesStaging()
        Dim tempDir = Path.Combine(Path.GetTempPath(), "FmsLoggerTest_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir)
        Try
            Dim logFile = Path.Combine(tempDir, "current.log")
            ' Create oversized file
            Dim content(CInt(AppFileLogger.Max_Log_Bytes + 1024)) As Byte
            File.WriteAllBytes(logFile, content)

            Dim rotated = AppFileLogger.RotateIfOversized(logFile)
            Assert.True(rotated)
            Assert.True(File.Exists(Path.Combine(tempDir, "previous.log")))
            Assert.False(File.Exists(logFile))
        Finally
            Try : Directory.Delete(tempDir, True) : Catch : End Try
        End Try
    End Sub

    <Fact>
    Public Sub S13_12_Logger_TransactionalRollbackOnFailure()
        AppFileLogger.ResetForTesting()
        Try
            Dim faultThrown As Boolean = False
            AppFileLogger.InitializeFaultInjector =
                Sub(stepNum)
                    If stepNum = 3 Then
                        faultThrown = True
                        Throw New IOException("Simulated disk error during logger init")
                    End If
                End Sub

            AppFileLogger.Initialize()
            Assert.True(faultThrown)
            Assert.False(AppFileLogger.IsReady)
        Finally
            AppFileLogger.InitializeFaultInjector = Nothing
            AppFileLogger.ResetForTesting()
        End Try
    End Sub

    ' --------------------------------------------------------------- 057.S13-11 & S13-13 ----
    ' PersistSettings dirty state retention and bounded argument probes

    <Fact>
    Public Sub S13_11_S13_13_Lifecycle_BoundedProbesAndDirtyRetention()
        Dim source As String = ReadSource("Main_Form.Lifecycle.vb")
        Assert.Contains("ArgumentProbeScheduler As New ProbeScheduler(Of ArgumentProbe)", source)
        ' Wiring only: generations, the 1/4/10 s retry schedule, re-entry and exhaustion are
        ' SettingsPersistenceTests against SettingsFlushScheduler, the object this form delegates to.
        Assert.Contains("New SettingsFlushScheduler(", source)
        Assert.Contains("SettingsWriter.WriteAll(", source)
    End Sub

    ' --------------------------------------------------------------- 057.S14-1 ----
    ' ExpandedSettings refresh on display and language change

    <Fact>
    Public Sub S14_1_ExpandedSettings_RefreshesOnDisplay()
        Dim source As String = ReadSource("Table_Form.ExpandedSettings.vb")
        Assert.Contains("expandedRefreshActions", source)
        Assert.Contains("RefreshExpandedSettingsRows", source)
        Assert.Contains("RefreshOcrCacheSize()", source)
        Assert.Contains("RefreshDecodeCacheSize()", source)
    End Sub

    ' --------------------------------------------------------------- 057.S14-2 ----
    ' AssociationButtonCaption chooser

    <Fact>
    Public Sub S14_2_AssociationButtonCaption_SourceInvariant()
        Dim source As String = ReadSource("Table_Form.ModernLayout.vb")
        Assert.Contains("Friend Shared Function AssociationButtonCaption(isPackaged As Boolean) As String", source)
        ' Wiring only: the chooser itself (mode, caption, every language) is SettingsImportTests.
        Assert.Contains("AssociationUx.Caption(AssociationUx.ModeFor(isPackaged))", source)
    End Sub

    ' --------------------------------------------------------------- 057.S14-3 ----
    ' CreateImportBackup helper

    <Fact>
    Public Sub S14_3_CreateImportBackup_SourceInvariant()
        Dim source As String = ReadSource("Table_Form.ExpandedSettings.vb")
        ' Wiring only: exclusivity, read-back, the 3-name retry, refusal without a TEMP fallback and
        ' the restore are SettingsImportTests against SettingsImportBackupStore.
        Assert.Contains("SettingsImportBackupStore.Create(", source)
        Assert.DoesNotContain("GetTempPath", source)
    End Sub

    ' ---------------------------------------------------------------- helpers ----

    Private Shared Function ReadSource(fileName As String) As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", fileName)
            If File.Exists(candidate) Then Return File.ReadAllText(candidate).Replace(vbCr, "")
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src/" & fileName & " not found above " & AppContext.BaseDirectory)
    End Function

End Class
