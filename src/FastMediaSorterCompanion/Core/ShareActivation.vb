Option Strict On

Imports System.Collections.Generic
Imports System.IO

''' <summary>Who owns the Share Manager's single-instance mutex after a launch tried to take it.</summary>
Friend Enum InstanceRole
    ''' <summary>This process acquired the mutex: it may build the tray icon and own the worker.</summary>
    Primary = 0
    ''' <summary>Another instance owns it (or may - see <see cref="InstanceOwnership.Decide"/>).
    ''' Forward the request and exit; never start a second manager.</summary>
    Existing = 1
    ''' <summary>The mutex could not be examined for an unexpected reason. Neither a primary
    ''' (a second tray icon over one worker pipe) nor a silent exit: log it and say so.</summary>
    Unavailable = 2
End Enum

Friend Module InstanceOwnership

    ''' <summary>
    ''' The decision, as a function of what the mutex call did. Access denied is evidence that
    ''' an instance owned by a higher-integrity token exists - its default DACL excludes the same
    ''' user's ordinary token - so it means <see cref="InstanceRole.Existing"/>, never "first".
    ''' Any other failure is NOT "first" either (the old catch-all claimed ownership and produced
    ''' a second tray icon and two managers racing over one pipe, silently).
    ''' </summary>
    Friend Function Decide(createdNew As Boolean, failure As Exception) As InstanceRole
        If failure Is Nothing Then Return If(createdNew, InstanceRole.Primary, InstanceRole.Existing)
        If TypeOf failure Is UnauthorizedAccessException Then Return InstanceRole.Existing
        Return InstanceRole.Unavailable
    End Function

    ''' <summary>True when the failure should be written to the log (everything except the
    ''' ordinary "another instance exists" answers).</summary>
    Friend Function IsUnexpected(failure As Exception) As Boolean
        Return failure IsNot Nothing AndAlso Not TypeOf failure Is UnauthorizedAccessException
    End Function

End Module

Friend Enum CompanionCommandKind
    Invalid = 0
    ShowWindow = 1
    PlainStart = 2
    ShareFolder = 3
End Enum

''' <summary>What a WM_COPYDATA payload asks of the Share Manager, parsed strictly.</summary>
Friend NotInheritable Class CompanionCommand
    Friend ReadOnly Property Kind As CompanionCommandKind
    ''' <summary>The canonical folder (fully qualified, no trailing separator) for ShareFolder.</summary>
    Friend ReadOnly Property Folder As String

    Private Sub New(kind As CompanionCommandKind, folder As String)
        Me.Kind = kind
        Me.Folder = folder
    End Sub

    Friend Shared ReadOnly Invalid As New CompanionCommand(CompanionCommandKind.Invalid, "")

    ''' <summary>
    ''' The two sentinels are exact; anything else must be a folder: a fully qualified drive or
    ''' UNC path, no device namespace, no wildcard or reserved characters, no ".." segment. The
    ''' folder need not exist here - that check can block on a dead share, so the consent step
    ''' makes it off the UI thread.
    ''' </summary>
    Friend Shared Function Parse(payload As String) As CompanionCommand
        If String.IsNullOrWhiteSpace(payload) Then Return Invalid
        If String.Equals(payload, Program.ShowWindowCommand, StringComparison.Ordinal) Then
            Return New CompanionCommand(CompanionCommandKind.ShowWindow, "")
        End If
        If String.Equals(payload, Program.PlainStartCommand, StringComparison.Ordinal) Then
            Return New CompanionCommand(CompanionCommandKind.PlainStart, "")
        End If
        Dim canonical As String = CanonicalFolder(payload)
        If canonical.Length = 0 Then Return Invalid
        Return New CompanionCommand(CompanionCommandKind.ShareFolder, canonical)
    End Function

    ''' <summary>The canonical spelling of a folder path, or "" when it is not an acceptable one.</summary>
    Friend Shared Function CanonicalFolder(raw As String) As String
        If String.IsNullOrWhiteSpace(raw) Then Return ""
        Dim path As String = raw.Trim()
        If path.StartsWith("\\?\", StringComparison.Ordinal) OrElse path.StartsWith("\\.\", StringComparison.Ordinal) Then Return ""
        If path.IndexOfAny(New Char() {"*"c, "?"c, "<"c, ">"c, "|"c, """"c}) >= 0 Then Return ""
        Dim isDrive As Boolean = path.Length >= 3 AndAlso Char.IsLetter(path(0)) AndAlso path(1) = ":"c AndAlso (path(2) = "\"c OrElse path(2) = "/"c)
        Dim isUnc As Boolean = path.StartsWith("\\", StringComparison.Ordinal) AndAlso path.Length > 2 AndAlso path(2) <> "\"c
        If Not isDrive AndAlso Not isUnc Then Return ""
        For Each segment As String In path.Split("\"c, "/"c)
            If segment = ".." Then Return ""
        Next
        Try
            Dim full As String = System.IO.Path.GetFullPath(path)
            ' A bare root ("D:\") keeps its separator: it is the folder, not an empty name.
            Dim root As String = System.IO.Path.GetPathRoot(full)
            If full.Length > root.Length Then full = full.TrimEnd("\"c)
            Return full
        Catch ex As Exception
            Return ""
        End Try
    End Function

End Class

''' <summary>The generation of the sender, judged from the tag in dwData - NOT its identity.</summary>
Friend Enum SenderGeneration
    ''' <summary>Carries the Share Manager's tag: a build with the tagged protocol.</summary>
    Tagged = 0
    ''' <summary>dwData = 0: a build from before the tag. Its payload may only be PREVIEWED.</summary>
    LegacyUntagged = 1
    ''' <summary>Any other dwData: not ours.</summary>
    Unknown = 2
End Enum

Friend Module ActivationSender

    Friend Function Classify(dwData As IntPtr) As SenderGeneration
        If dwData = New IntPtr(CopyDataTransport.CompanionTag) Then Return SenderGeneration.Tagged
        If dwData = IntPtr.Zero Then Return SenderGeneration.LegacyUntagged
        Return SenderGeneration.Unknown
    End Function

End Module

Friend Enum ProposalVerdict
    ''' <summary>A new proposal is now pending (ack 1).</summary>
    Queued = 0
    ''' <summary>The same folder is already pending, or was refused/cancelled inside the retry
    ''' window: acknowledged, not scheduled again (ack 1).</summary>
    Duplicate = 1
    ''' <summary>The folder is already in the share list: acknowledged, shown, nothing broadened (ack 1).</summary>
    AlreadyShared = 2
    ''' <summary>32 proposals are pending: refused (ack 0).</summary>
    Full = 3
End Enum

''' <summary>
''' Folders another process asked this one to share. A request is a PROPOSAL until the user has
''' answered it (108 H01): it is queued, de-duplicated by canonical folder for a retry window on a
''' monotonic clock - a cancelled one included, so a sender whose first send timed out cannot
''' reopen the question - and bounded. Nothing here shares anything.
''' </summary>
Friend NotInheritable Class FolderProposalQueue

    Friend Const MaxPending As Integer = 32
    Friend Const DedupWindowMs As Long = 10000

    Private ReadOnly clock As Func(Of Long)
    Private ReadOnly gate As New Object()
    Private ReadOnly pending As New Queue(Of String)()
    Private ReadOnly seen As New Dictionary(Of String, Long)(StringComparer.OrdinalIgnoreCase)

    Friend Sub New(clock As Func(Of Long))
        Me.clock = clock
    End Sub

    Friend ReadOnly Property PendingCount As Integer
        Get
            SyncLock gate
                Return pending.Count
            End SyncLock
        End Get
    End Property

    Friend Function Offer(canonicalFolder As String, isAlreadyShared As Func(Of String, Boolean)) As ProposalVerdict
        SyncLock gate
            Dim now As Long = clock()
            Dim expired As New List(Of String)()
            For Each entry As KeyValuePair(Of String, Long) In seen
                If now - entry.Value > DedupWindowMs Then expired.Add(entry.Key)
            Next
            For Each key As String In expired
                seen.Remove(key)
            Next

            If seen.ContainsKey(canonicalFolder) Then Return ProposalVerdict.Duplicate
            If isAlreadyShared IsNot Nothing AndAlso isAlreadyShared(canonicalFolder) Then
                seen(canonicalFolder) = now
                Return ProposalVerdict.AlreadyShared
            End If
            If pending.Count >= MaxPending Then Return ProposalVerdict.Full
            pending.Enqueue(canonicalFolder)
            seen(canonicalFolder) = now
            Return ProposalVerdict.Queued
        End SyncLock
    End Function

    ''' <summary>The next pending proposal for the UI to put to the user, or "" when none.</summary>
    Friend Function TakeNext() As String
        SyncLock gate
            If pending.Count = 0 Then Return ""
            Return pending.Dequeue()
        End SyncLock
    End Function

End Class

''' <summary>
''' The receiver's whole decision for one WM_COPYDATA, with no window in it: sender generation,
''' bounds, strict decode, command grammar, and the staging of a folder request. Returns the
''' acknowledgement (1 = accepted for handling, 0 = refused, nothing changed). A folder from
''' ANY accepted generation - tagged or the legacy untagged one - is only ever STAGED as a
''' proposal; no path through here shares anything.
''' </summary>
Friend Module ActivationReceiver

    Friend Function Evaluate(dwData As IntPtr, cbData As Integer, lpData As IntPtr,
                             stageFolder As Func(Of CompanionCommand, Boolean),
                             ByRef command As CompanionCommand) As Integer
        command = Nothing
        If ActivationSender.Classify(dwData) = SenderGeneration.Unknown Then
            AppFileLogger.WriteLine("Companion WM_COPYDATA: unknown tag ignored")
            Return 0
        End If

        Dim text As String = Nothing
        Dim refusal As PayloadRefusal
        If Not CopyDataTransport.TryDecode(cbData, lpData, text, refusal) Then
            AppFileLogger.WriteLine("Companion WM_COPYDATA: payload refused (" & refusal.ToString() & ")")
            Return 0
        End If

        Dim parsed As CompanionCommand = CompanionCommand.Parse(text)
        If parsed.Kind = CompanionCommandKind.Invalid Then
            AppFileLogger.WriteLine("Companion WM_COPYDATA: not a command or a folder - refused")
            Return 0
        End If

        If parsed.Kind = CompanionCommandKind.ShareFolder Then
            If stageFolder Is Nothing OrElse Not stageFolder(parsed) Then Return 0
        End If
        command = parsed
        Return 1
    End Function

End Module
