Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Linq

' Selection conditions of scheduled operations (ticket 222, sections 4.1 and 4.2): the glob masks,
' the matcher that applies every active condition to one scanned file, and the checks that refuse a
' contradictory definition at SAVE, so it is never discovered at run time.
'
' Pure: no file system, no registry. The matcher is handed the clock, the "last success" reference
' and the classifier, which is what makes "the file written 59 minutes ago" a unit test.

''' <summary>
''' One or more "*"/"?" globs separated by ";", matched case-insensitively against the file NAME only
''' (never the path). Blank parts are dropped; an empty mask is no condition at all. No regular
''' expression is involved: a glob with many stars cannot be made to backtrack for ever.
''' </summary>
Friend NotInheritable Class GlobMask

    Private ReadOnly patterns As String()

    Private Sub New(patterns As String())
        Me.patterns = patterns
    End Sub

    Friend Shared Function Parse(text As String) As GlobMask
        If String.IsNullOrWhiteSpace(text) Then Return New GlobMask(Array.Empty(Of String)())
        Dim parts As String() = text.Split(";"c).
            Select(Function(p) p.Trim()).
            Where(Function(p) p.Length > 0).
            ToArray()
        Return New GlobMask(parts)
    End Function

    ''' <summary>True when nothing is set - the condition is not active.</summary>
    Friend ReadOnly Property IsEmpty As Boolean
        Get
            Return patterns.Length = 0
        End Get
    End Property

    Friend Function IsMatch(fileName As String) As Boolean
        If patterns.Length = 0 Then Return False
        Dim name As String = If(fileName, "")
        For Each pattern As String In patterns
            If WildcardMatch(pattern, name) Then Return True
        Next
        Return False
    End Function

    ''' <summary>Iterative wildcard match with one backtrack point, O(n*m) in the worst case.</summary>
    Private Shared Function WildcardMatch(pattern As String, text As String) As Boolean
        Dim p As Integer = 0
        Dim t As Integer = 0
        Dim star As Integer = -1
        Dim mark As Integer = 0
        While t < text.Length
            If p < pattern.Length AndAlso (pattern(p) = "?"c OrElse SameChar(pattern(p), text(t))) Then
                p += 1
                t += 1
            ElseIf p < pattern.Length AndAlso pattern(p) = "*"c Then
                star = p
                mark = t
                p += 1
            ElseIf star >= 0 Then
                p = star + 1
                mark += 1
                t = mark
            Else
                Return False
            End If
        End While
        While p < pattern.Length AndAlso pattern(p) = "*"c
            p += 1
        End While
        Return p = pattern.Length
    End Function

    Private Shared Function SameChar(a As Char, b As Char) As Boolean
        Return a = b OrElse Char.ToUpperInvariant(a) = Char.ToUpperInvariant(b)
    End Function

End Class

''' <summary>Applies every active condition of an operation to one scanned file.</summary>
Friend NotInheritable Class ConditionMatcher

    Private ReadOnly conditions As ScheduledConditions
    Private ReadOnly now As DateTime
    Private ReadOnly sinceReference As DateTime?
    Private ReadOnly include As GlobMask
    Private ReadOnly exclude As GlobMask
    Private ReadOnly kindOf As Func(Of String, MediaKind)

    ''' <param name="sinceReference">The operation's <c>LastSuccessAt</c>: "new since the last run" is
    ''' everything written after it, and with none yet everything.</param>
    ''' <param name="classifier">The viewer's one classifier (<c>MediaKindClassifier.KindOf</c>) in the
    ''' product; a test passes its own.</param>
    Friend Sub New(conditions As ScheduledConditions, now As DateTime, sinceReference As DateTime?,
                   classifier As Func(Of String, MediaKind))
        Me.conditions = If(conditions, New ScheduledConditions())
        Me.now = now
        Me.sinceReference = sinceReference
        include = GlobMask.Parse(Me.conditions.NameMask)
        exclude = GlobMask.Parse(Me.conditions.ExcludeMask)
        kindOf = classifier
    End Sub

    Friend Function Matches(entry As SideEntry) As Boolean
        ' The scan layer: hidden and system files are out unless asked for.
        If entry.IsHiddenOrSystem AndAlso Not conditions.IncludeHidden Then Return False

        If Not TypeMatches(entry.Name) Then Return False
        If Not TimeMatches(entry.LastWriteLocal) Then Return False

        If Not include.IsEmpty AndAlso Not include.IsMatch(entry.Name) Then Return False
        If Not exclude.IsEmpty AndAlso exclude.IsMatch(entry.Name) Then Return False

        ' LastWriteTime, named: never DateCreated (4.1). Strict on both sides, like the size window.
        Dim ageHours As Double = (now - entry.LastWriteLocal).TotalHours
        If conditions.MinAgeHours.HasValue AndAlso Not (ageHours > conditions.MinAgeHours.Value) Then Return False
        If conditions.MaxAgeHours.HasValue AndAlso Not (ageHours < conditions.MaxAgeHours.Value) Then Return False

        ' "Larger than 0" is a real condition - it skips empty files; "smaller than 0" matches nothing.
        If conditions.MinSizeBytes.HasValue AndAlso Not (entry.Length > conditions.MinSizeBytes.Value) Then Return False
        If conditions.MaxSizeBytes.HasValue AndAlso Not (entry.Length < conditions.MaxSizeBytes.Value) Then Return False
        Return True
    End Function

    Private Function TypeMatches(fileName As String) As Boolean
        Dim mask As ScheduledTypeMask = conditions.TypeMask
        If (mask And ScheduledTypeMask.AllFiles) <> 0 Then Return True
        If mask = ScheduledTypeMask.None Then Return False
        Dim kind As MediaKind = kindOf(fileName)
        Select Case kind
            Case MediaKind.Image : Return (mask And ScheduledTypeMask.Images) <> 0
            Case MediaKind.Audio : Return (mask And ScheduledTypeMask.Audio) <> 0
            Case MediaKind.Video : Return (mask And ScheduledTypeMask.Video) <> 0
            Case MediaKind.Document, MediaKind.Book : Return (mask And ScheduledTypeMask.Documents) <> 0
            Case Else : Return False
        End Select
    End Function

    Private Function TimeMatches(lastWrite As DateTime) As Boolean
        Select Case conditions.TimeFilter
            Case ScheduledTimeFilter.LastHour : Return lastWrite >= now.AddHours(-1)
            Case ScheduledTimeFilter.LastDay : Return lastWrite >= now.AddDays(-1)
            Case ScheduledTimeFilter.SinceLastRun
                Return Not sinceReference.HasValue OrElse lastWrite > sinceReference.Value
            Case Else : Return True
        End Select
    End Function

End Class

''' <summary>What a definition is refused for. A language-independent code; the words are composed at display time.</summary>
Friend Enum ScheduledIssueCode
    SourceMissing = 0
    DestinationMissing = 1
    SourceNotAbsolute = 2
    DestinationNotAbsolute = 3
    ''' <summary>The destination is the source or lies inside it: a move into itself is not harmless (6).</summary>
    DestinationInsideSource = 4
    SourceProtected = 5
    DestinationProtected = 6
    TypeMaskEmpty = 7
    AgeBoundsContradict = 8
    SizeBoundsContradict = 9
    ''' <summary>Archive takes paths only in the first version (12.2 default 7).</summary>
    ArchiveNeedsPaths = 10
    NegativeBound = 11
    ''' <summary>A read-only resource cannot be moved from or deleted in (4.3).</summary>
    ResourceReadOnly = 12
    ''' <summary>The resource's root is PIN-locked (4.3).</summary>
    ResourcePinLocked = 13
    ResourceMissing = 14
    ''' <summary>The source and the destination are the same resource folder.</summary>
    SameResourceFolder = 15
    ''' <summary>The folder is not inside the root the resource was added with.</summary>
    ResourceFolderOutsideRoot = 16
    ''' <summary>The stored definition is one this build cannot read (an action it does not know): it is not
    ''' rewritten from what this build understands of it.</summary>
    DefinitionUnreadable = 18
    ''' <summary>An exchange link or another tunnelled resource: not usable unattended in this version.</summary>
    ResourceUnsupported = 17
End Enum

Friend NotInheritable Class ScheduledIssue
    Friend Property Code As ScheduledIssueCode
    ''' <summary>The path or name the issue is about, for the sentence.</summary>
    Friend Property Detail As String = ""
    Friend Sub New(code As ScheduledIssueCode, Optional detail As String = "")
        Me.Code = code
        Me.Detail = If(detail, "")
    End Sub
End Class

''' <summary>The folders a scheduled operation may never read from or write into (6): system and
''' program folders, spelled once and injectable so the rule can be tested on any machine.</summary>
Friend NotInheritable Class ProtectedFolders

    ''' <summary>A folder equal to one of these, or inside one, is refused.</summary>
    Friend ReadOnly Trees As String()
    ''' <summary>A folder equal to one of these is refused; anything below them is the user's own
    ''' (a drive root, or the Users folder itself, is not a place to sweep).</summary>
    Friend ReadOnly Exact As String()

    Friend Sub New(trees As IEnumerable(Of String), exact As IEnumerable(Of String))
        Me.Trees = trees.Select(Function(p) FolderPaths.Clean(p)).Where(Function(p) p.Length > 0).ToArray()
        Me.Exact = exact.Select(Function(p) FolderPaths.Clean(p)).Where(Function(p) p.Length > 0).ToArray()
    End Sub

    Friend Shared Function ForThisMachine() As ProtectedFolders
        ' Windows and the program folders. ProgramData is program DATA and stays the user's to use.
        Dim trees As New List(Of String) From {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles)}
        Dim exact As New List(Of String)()
        Try
            ' The system drive's root and the Users folder, and the profile root itself: places an
            ' unattended sweep is never aimed at on purpose. Anything BELOW them is the user's own,
            ' and a removable drive's root (an SD card to back up) is an ordinary source.
            Dim systemRoot As String = System.IO.Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))
            If Not String.IsNullOrEmpty(systemRoot) Then
                exact.Add(systemRoot)
                exact.Add(System.IO.Path.Combine(systemRoot, "Users"))
            End If
            exact.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
        Catch
        End Try
        Return New ProtectedFolders(trees, exact)
    End Function

    Friend Function Covers(folder As String) As Boolean
        Dim canonical As String = FolderPaths.Clean(folder)
        If canonical.Length = 0 Then Return False
        For Each e As String In Exact
            If String.Equals(canonical, e, StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        For Each tree As String In Trees
            If FolderPaths.IsSameOrInside(canonical, tree) Then Return True
        Next
        Return False
    End Function

End Class

''' <summary>Folder-path comparisons used by the refusals: trailing separators and case do not matter.</summary>
Friend Module FolderPaths

    ''' <summary>The folder without a trailing separator ("D:\" keeps its own: it is the folder).</summary>
    Friend Function Clean(folder As String) As String
        If String.IsNullOrWhiteSpace(folder) Then Return ""
        Dim text As String = folder.Trim()
        Dim root As String = ""
        Try
            root = System.IO.Path.GetPathRoot(text)
        Catch
        End Try
        If text.Length > root.Length Then text = text.TrimEnd("\"c, "/"c)
        Return text
    End Function

    ''' <summary>Is <paramref name="child"/> the same folder as <paramref name="parent"/>, or below it?</summary>
    Friend Function IsSameOrInside(child As String, parent As String) As Boolean
        Dim c As String = Clean(child)
        Dim p As String = Clean(parent)
        If c.Length = 0 OrElse p.Length = 0 Then Return False
        If String.Equals(c, p, StringComparison.OrdinalIgnoreCase) Then Return True
        Dim prefix As String = If(p.EndsWith("\", StringComparison.Ordinal), p, p & "\")
        Return c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
    End Function

End Module

Friend Module ScheduledValidation

    ''' <summary>One resource side: it must exist and be usable unattended, the folder must lie inside its root, and a
    ''' read-only root is only ever copied OUT of.</summary>
    Private Sub CheckResource(op As ScheduledOperation, location As ScheduledLocation, isDestination As Boolean,
                              resources As Func(Of String, ScheduledResourceFacts), issues As List(Of ScheduledIssue))
        Dim facts As ScheduledResourceFacts = resources(location.ResourceId)
        If facts Is Nothing OrElse Not facts.Exists Then
            issues.Add(New ScheduledIssue(ScheduledIssueCode.ResourceMissing, location.ResourceName))
            Return
        End If
        If facts.IsUnsupported Then
            issues.Add(New ScheduledIssue(ScheduledIssueCode.ResourceUnsupported, facts.DisplayName))
            Return
        End If
        If facts.IsPinLocked Then
            issues.Add(New ScheduledIssue(ScheduledIssueCode.ResourcePinLocked, facts.DisplayName))
            Return
        End If
        If Not String.IsNullOrWhiteSpace(location.Path) AndAlso facts.RootPath.Length > 0 AndAlso
           Not InsideRoot(location.Path, facts.RootPath) Then
            issues.Add(New ScheduledIssue(ScheduledIssueCode.ResourceFolderOutsideRoot, facts.RootPath))
        End If
        ' A read-only root is copied OUT of; writing into it, moving from it or deleting in it is refused.
        If facts.IsReadOnly AndAlso (isDestination OrElse op.IsDestructive) Then
            issues.Add(New ScheduledIssue(ScheduledIssueCode.ResourceReadOnly, facts.DisplayName))
        End If
    End Sub

    ''' <summary>Is <paramref name="folder"/> the root or below it? Separators and case are the provider's: an SFTP
    ''' root is a case-sensitive "/" path, a share root a case-insensitive "\" one.</summary>
    Friend Function InsideRoot(folder As String, root As String) As Boolean
        Dim f As String = folder.Trim()
        Dim r As String = root.Trim()
        If f.Contains("..") Then Return False
        Dim windows As Boolean = r.StartsWith("\\", StringComparison.Ordinal) OrElse r.Contains("\"c)
        Dim comparison As StringComparison = If(windows, StringComparison.OrdinalIgnoreCase, StringComparison.Ordinal)
        Dim separator As Char = If(windows, "\"c, "/"c)
        f = f.Replace(If(windows, "/"c, "\"c), separator).TrimEnd(separator)
        r = r.Replace(If(windows, "/"c, "\"c), separator).TrimEnd(separator)
        If r.Length = 0 Then Return True
        If String.Equals(f, r, comparison) Then Return True
        Return f.StartsWith(r & separator, comparison)
    End Function

    ''' <summary>
    ''' Corrects what is corrected rather than refused (section 4): the interval floor, the anchor
    ''' wrapped into a day, trimmed text and the type mask's interlock - all-files switches the rest
    ''' off, and nothing at all is read as all-files. Called by the editor's save and again before a run.
    ''' </summary>
    Friend Sub Normalize(op As ScheduledOperation)
        op.Name = If(op.Name, "").Trim()
        op.IntervalMinutes = ScheduleMath.NormalizeInterval(op.IntervalMinutes)
        op.AnchorMinutes = ScheduleMath.NormalizeAnchor(op.AnchorMinutes)
        op.Conditions.NameMask = If(op.Conditions.NameMask, "").Trim()
        op.Conditions.ExcludeMask = If(op.Conditions.ExcludeMask, "").Trim()
        Dim mask As ScheduledTypeMask = op.Conditions.TypeMask
        If (mask And ScheduledTypeMask.AllFiles) <> 0 Then op.Conditions.TypeMask = ScheduledTypeMask.AllFiles
        ' "Delete originals after verification" belongs to archive alone; a delete has no destination.
        If op.Action <> ScheduledAction.Archive Then op.DeleteOriginalsAfterVerify = False
        If op.Action = ScheduledAction.Delete Then op.Destination = New ScheduledLocation()
    End Sub

    ''' <summary>
    ''' Everything that makes a definition unrunnable, in one list. The path rules need no file system:
    ''' "does it exist" is asked by the run's pre-flight, never here, because the save must not block on
    ''' a sleeping share. A path must be absolute (drive or UNC) and free of device-namespace spellings.
    ''' </summary>
    Friend Function Validate(op As ScheduledOperation, protectedFolders As ProtectedFolders,
                             Optional resources As Func(Of String, ScheduledResourceFacts) = Nothing) As List(Of ScheduledIssue)
        Dim issues As New List(Of ScheduledIssue)()

        Dim sourceFolder As String = ""
        If op.Source.IsEmpty Then
            issues.Add(New ScheduledIssue(ScheduledIssueCode.SourceMissing))
        ElseIf op.Source.Kind = ScheduledLocationKind.Path Then
            sourceFolder = CompanionCommand.CanonicalFolder(op.Source.Path)
            If sourceFolder.Length = 0 Then
                issues.Add(New ScheduledIssue(ScheduledIssueCode.SourceNotAbsolute, op.Source.Path))
            ElseIf protectedFolders IsNot Nothing AndAlso protectedFolders.Covers(sourceFolder) Then
                issues.Add(New ScheduledIssue(ScheduledIssueCode.SourceProtected, sourceFolder))
            End If
        End If

        Dim destinationFolder As String = ""
        If op.NeedsDestination Then
            If op.Destination.IsEmpty Then
                issues.Add(New ScheduledIssue(ScheduledIssueCode.DestinationMissing))
            ElseIf op.Destination.Kind = ScheduledLocationKind.Path Then
                destinationFolder = CompanionCommand.CanonicalFolder(op.Destination.Path)
                If destinationFolder.Length = 0 Then
                    issues.Add(New ScheduledIssue(ScheduledIssueCode.DestinationNotAbsolute, op.Destination.Path))
                ElseIf protectedFolders IsNot Nothing AndAlso protectedFolders.Covers(destinationFolder) Then
                    issues.Add(New ScheduledIssue(ScheduledIssueCode.DestinationProtected, destinationFolder))
                End If
            End If
        End If

        ' Destination inside the source (or the very folder): on raw NTFS paths a move into itself is not harmless.
        If sourceFolder.Length > 0 AndAlso destinationFolder.Length > 0 AndAlso
           FolderPaths.IsSameOrInside(destinationFolder, sourceFolder) Then
            issues.Add(New ScheduledIssue(ScheduledIssueCode.DestinationInsideSource, destinationFolder))
        End If

        If op.Action = ScheduledAction.Archive AndAlso
           (op.Source.Kind = ScheduledLocationKind.Resource OrElse op.Destination.Kind = ScheduledLocationKind.Resource) Then
            issues.Add(New ScheduledIssue(ScheduledIssueCode.ArchiveNeedsPaths))
        End If

        ' Registered resources (4.3): the registry belongs to another program, so its answers come in as facts.
        If resources IsNot Nothing Then
            If op.Source.Kind = ScheduledLocationKind.Resource AndAlso Not op.Source.IsEmpty Then
                CheckResource(op, op.Source, isDestination:=False, resources, issues)
            End If
            If op.NeedsDestination AndAlso op.Destination.Kind = ScheduledLocationKind.Resource AndAlso Not op.Destination.IsEmpty Then
                CheckResource(op, op.Destination, isDestination:=True, resources, issues)
            End If
            If op.NeedsDestination AndAlso op.Source.Kind = ScheduledLocationKind.Resource AndAlso
               op.Destination.Kind = ScheduledLocationKind.Resource AndAlso
               String.Equals(op.Source.ResourceId, op.Destination.ResourceId, StringComparison.OrdinalIgnoreCase) AndAlso
               String.Equals(op.Source.Path.TrimEnd("/"c, "\"c), op.Destination.Path.TrimEnd("/"c, "\"c), StringComparison.OrdinalIgnoreCase) Then
                issues.Add(New ScheduledIssue(ScheduledIssueCode.SameResourceFolder))
            End If
        End If

        Dim c As ScheduledConditions = op.Conditions
        If c.TypeMask = ScheduledTypeMask.None Then issues.Add(New ScheduledIssue(ScheduledIssueCode.TypeMaskEmpty))

        If (c.MinAgeHours.HasValue AndAlso c.MinAgeHours.Value < 0) OrElse
           (c.MaxAgeHours.HasValue AndAlso c.MaxAgeHours.Value < 0) OrElse
           (c.MinSizeBytes.HasValue AndAlso c.MinSizeBytes.Value < 0) OrElse
           (c.MaxSizeBytes.HasValue AndAlso c.MaxSizeBytes.Value < 0) Then
            issues.Add(New ScheduledIssue(ScheduledIssueCode.NegativeBound))
        End If
        ' Contradictory bounds are refused at save, not discovered at run time: the upper bound must
        ' lie above the lower one.
        If c.MinAgeHours.HasValue AndAlso c.MaxAgeHours.HasValue AndAlso c.MaxAgeHours.Value <= c.MinAgeHours.Value Then
            issues.Add(New ScheduledIssue(ScheduledIssueCode.AgeBoundsContradict))
        End If
        If c.MinSizeBytes.HasValue AndAlso c.MaxSizeBytes.HasValue AndAlso c.MaxSizeBytes.Value <= c.MinSizeBytes.Value Then
            issues.Add(New ScheduledIssue(ScheduledIssueCode.SizeBoundsContradict))
        End If
        Return issues
    End Function

End Module
