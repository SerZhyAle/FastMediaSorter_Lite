#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO

' A selection plus an intent, turned into an ordered, de-duplicated, pre-resolved list of
' operations - with everything that will not be done named BEFORE anything runs
' (004_SPECIFICATION_BATCH_OPERATIONS_ENGINE_DOTNET10.md PB-2).
'
' PURE. The plan is handed a context (where the files go, the collision policy, the
' delete verdict, whether the resource is writable, and three probe delegates) instead of
' reaching for the file system, which is what makes "the 22nd file has the same name as the
' 4th" a unit test rather than something to reproduce with a card full of IMG_0001.jpg.
' Modern build only: the x86 panel keeps its own loop (maintenance policy).

''' <summary>What the user asked of the selection.</summary>
Friend Enum BatchIntent
    MoveToSlot
    CopyToSlot
    Delete
End Enum

''' <summary>
''' Why a file is NOT part of the run. Closed: every member is a case the single-file path
''' already refuses today, plus NameTaken - the collision policy "skip" (or the answer Skip
''' to the one question) - which the single-file path spells as a status line.
''' </summary>
Friend Enum SkipReason
    ''' <summary>The slot is the folder the file is already in ("! Файл уже в этой папке").</summary>
    AlreadyInDestination
    ''' <summary>The file stopped existing between selection and run.</summary>
    VanishedBeforeStart
    ''' <summary>An archive's entries are extracted copies; nothing is done to the archive.</summary>
    InsideArchive
    ''' <summary>Two selected files resolve to the same destination name. The second is
    ''' REPORTED - not silently renamed and not silently overwritten.</summary>
    DuplicateName
    ''' <summary>A read-only resource (an SFTP root exported with readOnly).</summary>
    NotWritableResource
    ''' <summary>Source and destination are one and the same file.</summary>
    SameFile
    ''' <summary>The destination already holds that name and the policy says skip.</summary>
    NameTaken
End Enum

''' <summary>The answer to the ONE collision question a batch asks (PB-6).</summary>
Friend Enum CollisionAnswer
    Replace
    KeepBoth
    Skip
End Enum

''' <summary>What a restore from the Recycle Bin said - its own enum so this file stays
''' free of the shell code (RecycleBinIo is not linked into the test project).</summary>
Friend Enum BatchRestoreOutcome
    NotAttempted
    Restored
    NotInBin
    SourceFolderGone
End Enum

Friend NotInheritable Class BatchSkip
    Public Property Path As String = ""
    Public Property Reason As SkipReason
End Class

''' <summary>
''' One operation of a run, whole. The same facts a single-file FileOp carries, but pure:
''' FileOp is a private nested class of the form, and a plan that depended on it could not
''' be tested without one. Main_Form maps the two at the queue boundary.
''' </summary>
Friend NotInheritable Class BatchOp
    Public Property Kind As FileOpKind
    Public Property Source As String = ""
    Public Property Destination As String = ""
    Public Property SlotKey As String = ""
    Public Property SizeBytes As Long
    Public Property ReplaceExisting As Boolean
    Public Property CreateDestinationFolder As Boolean
    ''' <summary>The destination name is taken and nobody has decided what to do yet.</summary>
    Public Property NameTaken As Boolean
    ''' <summary>Tail for the report when the name had to change.</summary>
    Public Property StatusNote As String = ""

    ' --- facts an inverse needs ---------------------------------------------------
    ''' <summary>DeleteUndo: the file the copy duplicates. The copy is deleted only while
    ''' this still exists (046 C-04).</summary>
    Public Property OriginalSource As String = ""
    ''' <summary>RecycleDelete / RestoreFromBin: when the deletion happened, stamped by the
    ''' runner just before the shell call.</summary>
    Public Property DeletedAtUtc As DateTime
    Public Property PermanentDeleteReason As PermanentReason
    ''' <summary>Where the file sat in the main window's list when it left it; -1 when it
    ''' was not in the list on screen. Undo puts it back there.</summary>
    Public Property ListIndex As Integer = -1

    ' --- what the worker found, written once the operation has finished -------------
    Public Property CreatedDestinationFolder As Boolean
    Public Property RecycleFellThrough As Boolean
    Public Property CopyUndoRefused As Boolean
    Public Property RestoreOutcome As BatchRestoreOutcome = BatchRestoreOutcome.NotAttempted
    ''' <summary>The operation finished WITHOUT an exception and still did not do what it
    ''' was asked - the bin was emptied, a copy was kept because it is now the only instance.
    ''' An answer, not an error: it travels with the operation, is reported per file in the
    ''' user's words and never counts as a transport failure.</summary>
    Public Property Refusal As String = ""

    ''' <summary>The name the user knows it by.</summary>
    Public ReadOnly Property DisplayName As String
        Get
            Try
                Return Path.GetFileName(Source)
            Catch
                Return Source
            End Try
        End Get
    End Property
End Class

''' <summary>Everything a plan may need to know, handed in so the plan itself touches
''' nothing. The three delegates default to "everything exists, size 0, never the same
''' file" - a test overrides what it is about.</summary>
Friend NotInheritable Class BatchPlanContext
    ''' <summary>Move/Copy: the slot's folder.</summary>
    Public Property DestinationFolder As String = ""
    ''' <summary>The key the user pressed ("0".."9").</summary>
    Public Property SlotKey As String = ""
    ''' <summary>The slot verdict was WillBeCreated: the worker makes the final segment, once.</summary>
    Public Property CreateDestinationFolder As Boolean
    ''' <summary>ask | rename | skip | replace (ModernViewerPreferences.NameCollisionPolicy).</summary>
    Public Property CollisionPolicy As String = "ask"
    Public Property InsideArchive As Boolean
    ''' <summary>The files live on a resource that cannot be written to (Move and Delete need it).</summary>
    Public Property SourceReadOnly As Boolean
    ''' <summary>The slot is on a resource that cannot be written to.</summary>
    Public Property DestinationReadOnly As Boolean
    ''' <summary>Delete: where the files will go. Nothing means "not known", which counts as permanent.</summary>
    Public Property DeleteDecision As DeleteDecision
    ''' <summary>always | permanentOnly | never (ModernViewerPreferences.ConfirmDelete).</summary>
    Public Property ConfirmDeletePolicy As String = "always"

    Public Property FileExists As Func(Of String, Boolean) = Function(p) True
    Public Property FileSize As Func(Of String, Long) = Function(p) 0L
    Public Property IsSameFile As Func(Of String, String, Boolean) = Function(a, b) False
End Class

''' <summary>The plan. Built by <see cref="Build"/>; never mutated afterwards - the one
''' question that changes it (a collision answer) returns a NEW plan.</summary>
Friend NotInheritable Class BatchPlan

    Private ReadOnly ops As List(Of BatchOp)
    Private ReadOnly skips As List(Of BatchSkip)
    Private ReadOnly collisions As List(Of String)

    Private Sub New(intent As BatchIntent, operations As List(Of BatchOp), skipped As List(Of BatchSkip),
                    collisionDestinations As List(Of String), permanentReason As PermanentReason,
                    needsDeleteConfirmation As Boolean, destinationFolder As String, slotKey As String)
        Me.Intent = intent
        ops = operations
        skips = skipped
        collisions = collisionDestinations
        Me.PermanentDeletionReason = permanentReason
        Me.NeedsDeleteConfirmation = needsDeleteConfirmation
        Me.DestinationFolder = destinationFolder
        Me.SlotKey = slotKey
        Dim total As Long = 0
        For Each op As BatchOp In operations
            total += Math.Max(0L, op.SizeBytes)
        Next
        TotalBytes = total
    End Sub

    Friend ReadOnly Property Intent As BatchIntent
    Friend ReadOnly Property DestinationFolder As String
    Friend ReadOnly Property SlotKey As String

    ''' <summary>The ordered operations, ready for the queue.</summary>
    Friend ReadOnly Property Operations As IReadOnlyList(Of BatchOp)
        Get
            Return ops
        End Get
    End Property

    ''' <summary>Decided before anything runs.</summary>
    Friend ReadOnly Property Skipped As IReadOnlyList(Of BatchSkip)
        Get
            Return skips
        End Get
    End Property

    Friend ReadOnly Property Count As Integer
        Get
            Return ops.Count
        End Get
    End Property

    ''' <summary>For the progress line, and for the "4 GB over SFTP" warning later.</summary>
    Friend ReadOnly Property TotalBytes As Long

    ''' <summary>The destinations that already exist and that the policy "ask" has not
    ''' decided - the set the ONE question is about. Empty under every other policy.</summary>
    Friend ReadOnly Property NeedsCollisionAnswer As IReadOnlyList(Of String)
        Get
            Return collisions
        End Get
    End Property

    ''' <summary>Delete only: the single reason no Recycle Bin is available, or
    ''' PermanentReason.NotPermanent ("Nothing") when the bin will take the files.</summary>
    Friend ReadOnly Property PermanentDeletionReason As PermanentReason

    ''' <summary>Delete only: the user has to be asked once, by the setting and by the
    ''' verdict - the same rule the single-file delete uses.</summary>
    Friend ReadOnly Property NeedsDeleteConfirmation As Boolean

    ' ------------------------------------------------------------------- build ----

    Friend Shared Function Build(paths As IEnumerable(Of String), intent As BatchIntent,
                                 context As BatchPlanContext) As BatchPlan
        Dim ctx As BatchPlanContext = If(context, New BatchPlanContext())
        Dim operations As New List(Of BatchOp)()
        Dim skipped As New List(Of BatchSkip)()
        Dim collision_Destinations As New List(Of String)()

        If paths Is Nothing Then
            Return New BatchPlan(intent, operations, skipped, collision_Destinations, PermanentReason.NotPermanent,
                                 False, ctx.DestinationFolder, ctx.SlotKey)
        End If

        If intent = BatchIntent.Delete Then
            Return BuildDelete(paths, ctx)
        End If

        Dim is_Copy As Boolean = (intent = BatchIntent.CopyToSlot)
        Dim candidates As New List(Of BatchOp)()
        Dim names As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        ' Pass one: the refusals that depend on one file alone, then the name each
        ' survivor would get. A name already claimed by an earlier file is a DuplicateName -
        ' the earlier one keeps it.
        For Each source As String In paths
            If String.IsNullOrEmpty(source) Then Continue For

            If ctx.InsideArchive Then
                skipped.Add(Skip(source, SkipReason.InsideArchive))
                Continue For
            End If
            If ctx.DestinationReadOnly OrElse (Not is_Copy AndAlso ctx.SourceReadOnly) Then
                skipped.Add(Skip(source, SkipReason.NotWritableResource))
                Continue For
            End If
            If Not ctx.FileExists(source) Then
                skipped.Add(Skip(source, SkipReason.VanishedBeforeStart))
                Continue For
            End If

            Dim destination As String = DestinationFor(ctx.DestinationFolder, source)
            If SameFolder(destination, source) Then
                skipped.Add(Skip(source, SkipReason.AlreadyInDestination))
                Continue For
            End If
            If ctx.FileExists(destination) AndAlso ctx.IsSameFile(source, destination) Then
                skipped.Add(Skip(source, SkipReason.SameFile))
                Continue For
            End If
            If Not names.Add(destination) Then
                skipped.Add(Skip(source, SkipReason.DuplicateName))
                Continue For
            End If

            candidates.Add(New BatchOp With {
                .Kind = If(is_Copy, FileOpKind.Copy, FileOpKind.Move),
                .Source = source,
                .Destination = destination,
                .SlotKey = ctx.SlotKey,
                .SizeBytes = SafeSize(ctx, source),
                .CreateDestinationFolder = ctx.CreateDestinationFolder})
        Next

        ' Pass two: names that are already taken in the destination, by the user's policy.
        ' Every natural name of the selection is reserved first, so a rename can never land
        ' on a name a later file in the same run is about to claim.
        For Each op As BatchOp In candidates
            If Not ctx.FileExists(op.Destination) Then
                operations.Add(op)
                Continue For
            End If

            Select Case If(ctx.CollisionPolicy, "ask")
                Case "skip"
                    skipped.Add(Skip(op.Source, SkipReason.NameTaken))
                Case "rename"
                    ApplyKeepBoth(op, ctx, names)
                    operations.Add(op)
                Case "replace"
                    op.ReplaceExisting = True
                    operations.Add(op)
                Case Else
                    op.NameTaken = True
                    collision_Destinations.Add(op.Destination)
                    operations.Add(op)
            End Select
        Next

        Return New BatchPlan(intent, operations, skipped, collision_Destinations, PermanentReason.NotPermanent,
                             False, ctx.DestinationFolder, ctx.SlotKey)
    End Function

    Private Shared Function BuildDelete(paths As IEnumerable(Of String), ctx As BatchPlanContext) As BatchPlan
        Dim operations As New List(Of BatchOp)()
        Dim skipped As New List(Of BatchSkip)()

        ' Nothing known is not "fine": it counts as permanent and says so (DeletePolicy).
        Dim decision As DeleteDecision = ctx.DeleteDecision
        Dim permanent As Boolean = decision Is Nothing OrElse decision.IsPermanent
        Dim reason As PermanentReason = If(Not permanent, PermanentReason.NotPermanent,
                                           If(decision Is Nothing, PermanentReason.VolumeUnknown, decision.Reason))

        For Each source As String In paths
            If String.IsNullOrEmpty(source) Then Continue For
            If ctx.InsideArchive Then
                skipped.Add(Skip(source, SkipReason.InsideArchive))
                Continue For
            End If
            If ctx.SourceReadOnly Then
                skipped.Add(Skip(source, SkipReason.NotWritableResource))
                Continue For
            End If
            If Not ctx.FileExists(source) Then
                skipped.Add(Skip(source, SkipReason.VanishedBeforeStart))
                Continue For
            End If
            operations.Add(New BatchOp With {
                .Kind = If(permanent, FileOpKind.Delete, FileOpKind.RecycleDelete),
                .Source = source,
                .SizeBytes = SafeSize(ctx, source),
                .PermanentDeleteReason = reason})
        Next

        Dim confirm As Boolean = operations.Count > 0 AndAlso DeletePolicy.ShouldConfirm(ctx.ConfirmDeletePolicy, decision)
        Return New BatchPlan(BatchIntent.Delete, operations, skipped, New List(Of String)(), reason,
                             confirm, ctx.DestinationFolder, ctx.SlotKey)
    End Function

    ''' <summary>
    ''' The plan once the one question has been answered: Replace marks the colliding
    ''' operations, KeepBoth gives them free names, Skip moves them to the skipped list.
    ''' Operations that did not collide are untouched, and the original plan is not changed.
    ''' </summary>
    Friend Function WithCollisionAnswer(answer As CollisionAnswer, context As BatchPlanContext) As BatchPlan
        Dim ctx As BatchPlanContext = If(context, New BatchPlanContext())
        Dim reserved As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each op As BatchOp In ops
            reserved.Add(op.Destination)
        Next

        Dim operations As New List(Of BatchOp)(ops.Count)
        Dim skipped As New List(Of BatchSkip)(skips)
        For Each op As BatchOp In ops
            If Not op.NameTaken Then
                operations.Add(op)
                Continue For
            End If
            Dim copy As BatchOp = CloneOp(op)
            copy.NameTaken = False
            Select Case answer
                Case CollisionAnswer.Replace
                    copy.ReplaceExisting = True
                    operations.Add(copy)
                Case CollisionAnswer.KeepBoth
                    ApplyKeepBoth(copy, ctx, reserved)
                    operations.Add(copy)
                Case Else
                    skipped.Add(Skip(op.Source, SkipReason.NameTaken))
            End Select
        Next

        Return New BatchPlan(Intent, operations, skipped, New List(Of String)(), PermanentDeletionReason,
                             NeedsDeleteConfirmation, DestinationFolder, SlotKey)
    End Function

    ' ----------------------------------------------------------------- helpers ----

    Private Shared Function Skip(path As String, reason As SkipReason) As BatchSkip
        Return New BatchSkip With {.Path = path, .Reason = reason}
    End Function

    Private Shared Function SafeSize(ctx As BatchPlanContext, path As String) As Long
        Try
            Return Math.Max(0L, ctx.FileSize(path))
        Catch
            Return 0L
        End Try
    End Function

    ''' <summary>The destination of one file: the slot's folder and the file's own name.</summary>
    Friend Shared Function DestinationFor(folder As String, source As String) As String
        Return Path.Combine(If(folder, ""), Path.GetFileName(source))
    End Function

    ''' <summary>Does the destination sit in the very folder the source is in?</summary>
    Private Shared Function SameFolder(destination As String, source As String) As Boolean
        Try
            Return String.Equals(Path.GetDirectoryName(destination), Path.GetDirectoryName(source),
                                 StringComparison.OrdinalIgnoreCase)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' "IMG_0001.jpg" taken -> "IMG_0001 (2).jpg", the first free one, Explorer-style. Free
    ''' means free in the destination AND not claimed by another file of the same run.
    ''' </summary>
    Friend Shared Function FreeName(destination As String, exists As Func(Of String, Boolean),
                                    reserved As ISet(Of String)) As String
        Dim dir As String = Path.GetDirectoryName(destination)
        Dim base As String = Path.GetFileNameWithoutExtension(destination)
        Dim ext As String = Path.GetExtension(destination)
        For n As Integer = 2 To 9999
            Dim candidate As String = Path.Combine(dir, base & " (" & n.ToString() & ")" & ext)
            If reserved IsNot Nothing AndAlso reserved.Contains(candidate) Then Continue For
            If Not exists(candidate) Then Return candidate
        Next
        ' 9998 namesakes in one folder is not a collision any more - the operation fails
        ' honestly instead of this looping.
        Return destination
    End Function

    Private Shared Sub ApplyKeepBoth(op As BatchOp, ctx As BatchPlanContext, reserved As ISet(Of String))
        Dim free As String = FreeName(op.Destination, ctx.FileExists, reserved)
        If Not String.Equals(free, op.Destination, StringComparison.OrdinalIgnoreCase) Then
            reserved?.Add(free)
            op.Destination = free
            op.StatusNote = Path.GetFileName(free)
        End If
    End Sub

    Private Shared Function CloneOp(op As BatchOp) As BatchOp
        Return New BatchOp With {
            .Kind = op.Kind, .Source = op.Source, .Destination = op.Destination, .SlotKey = op.SlotKey,
            .SizeBytes = op.SizeBytes, .ReplaceExisting = op.ReplaceExisting,
            .CreateDestinationFolder = op.CreateDestinationFolder, .NameTaken = op.NameTaken,
            .StatusNote = op.StatusNote, .OriginalSource = op.OriginalSource,
            .PermanentDeleteReason = op.PermanentDeleteReason}
    End Function

End Class
#End If
