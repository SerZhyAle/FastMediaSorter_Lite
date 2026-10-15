Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic

' Scheduled file operations (ticket 222) - the model.
'
' Part of the Share Manager and of nothing else (section 0 of the specification): the engine, the
' store and the editor all live in this program; the viewer carries only a launcher. Every type here
' is plain data - no registry, no file system, no WinForms - so the rules that sit on top of it
' (next slot, conditions, validation) are provable in a unit test.
'
' TIMES ARE WALL-CLOCK. A schedule says "03:00" and means 03:00 on the clock on the wall, so every
' DateTime in this model is a local, unspecified-kind value and never goes through UTC; only a file's
' write time, which the file system keeps in UTC, is converted once on its way in.

''' <summary>What an operation does with the files it selects.</summary>
Friend Enum ScheduledAction
    Copy = 0
    Move = 1
    Delete = 2
    Archive = 3
End Enum

''' <summary>What happens when the destination name is taken (D5). Skip is the default.</summary>
Friend Enum ScheduledConflict
    Skip = 0
    Overwrite = 1
    ''' <summary>"name (2).ext" - the viewer's own free-name rule.</summary>
    KeepBoth = 2
End Enum

''' <summary>The time filter of FMS_A, repeated: the incrementality mechanism of the feature.</summary>
Friend Enum ScheduledTimeFilter
    All = 0
    ''' <summary>Files written after the operation last succeeded.</summary>
    SinceLastRun = 1
    LastHour = 2
    LastDay = 3
End Enum

''' <summary>File type selection, a bit mask with FMS_A's interlock: all-files excludes the rest.</summary>
<Flags>
Friend Enum ScheduledTypeMask
    None = 0
    Images = 1
    Audio = 2
    Video = 4
    Documents = 8
    AllFiles = 16
End Enum

Friend Enum ScheduledLocationKind
    ''' <summary>A folder on a local drive or a UNC path, used under the signed-in user's own credentials.</summary>
    Path = 0
    ''' <summary>A registered resource of the viewer's registry plus a folder inside it (4.3).</summary>
    Resource = 1
End Enum

''' <summary>Why a scheduled run started. Manual triggers never move the schedule.</summary>
Friend Enum ScheduledTrigger
    Schedule = 0
    RunNow = 1
    RunAll = 2
    ''' <summary>A slot that came due while the Share Manager was not running, run once at start (5).</summary>
    CatchUp = 3
End Enum

''' <summary>What to do with a slot that came due while the program was not running (4.4).</summary>
Friend Enum MissedRunPolicy
    RunOnceAtStart = 0
    [Skip] = 1
End Enum

''' <summary>How a run ended. A language-independent code - the words are composed at display time.</summary>
Friend Enum ScheduledRunOutcome
    None = 0
    Ok = 1
    ''' <summary>The run reached its end but at least one file failed.</summary>
    Failed = 2
    ''' <summary>Refused before anything was touched (validation or target pre-flight).</summary>
    Refused = 3
    ''' <summary>The operation-level wall: nothing a retry can fix (6).</summary>
    Wall = 4
    Cancelled = 5
End Enum

''' <summary>Why the scheduler leaves an operation alone although the user left it enabled.</summary>
Friend Enum ScheduledHalt
    None = 0
    ''' <summary>The operation-level wall was hit; cleared by a successful run or by editing it.</summary>
    Wall = 1
    ''' <summary>The registered resource it names is gone from the registry (4.3).</summary>
    ResourceRemoved = 2
    ''' <summary>The stored definition cannot be run (an action this build does not know).</summary>
    InvalidDefinition = 3
End Enum

''' <summary>One side of an operation: a path or a registered resource plus a folder.</summary>
Friend NotInheritable Class ScheduledLocation

    Friend Property Kind As ScheduledLocationKind = ScheduledLocationKind.Path
    ''' <summary>The folder: a Windows path for <see cref="ScheduledLocationKind.Path"/>, a "/"-separated
    ''' folder inside the resource for <see cref="ScheduledLocationKind.Resource"/>.</summary>
    Friend Property Path As String = ""
    ''' <summary>The registry id of the resource. The reference: resolution never matches by text.</summary>
    Friend Property ResourceId As String = ""
    ''' <summary>The resource's name as it was when the operation was saved - for showing only.</summary>
    Friend Property ResourceName As String = ""
    ''' <summary>A property of the SOURCE (4.1), never of the operation. Ignored on a destination.</summary>
    Friend Property Recursive As Boolean = True

    Friend ReadOnly Property IsEmpty As Boolean
        Get
            If Kind = ScheduledLocationKind.Resource Then Return String.IsNullOrWhiteSpace(ResourceId)
            Return String.IsNullOrWhiteSpace(Path)
        End Get
    End Property

    Friend Function Clone() As ScheduledLocation
        Return New ScheduledLocation With {.Kind = Kind, .Path = Path, .ResourceId = ResourceId,
            .ResourceName = ResourceName, .Recursive = Recursive}
    End Function

    ''' <summary>A short human spelling for notes and rows: the path, or "name: folder".</summary>
    Friend Function Describe() As String
        If Kind = ScheduledLocationKind.Resource Then
            Dim name As String = If(String.IsNullOrEmpty(ResourceName), ResourceId, ResourceName)
            Return If(String.IsNullOrEmpty(Path), name, name & ": " & Path)
        End If
        Return Path
    End Function

End Class

''' <summary>The selection conditions of 4.1 and 4.2. Every null is "no condition".</summary>
Friend NotInheritable Class ScheduledConditions

    Friend Property TypeMask As ScheduledTypeMask = ScheduledTypeMask.AllFiles
    Friend Property TimeFilter As ScheduledTimeFilter = ScheduledTimeFilter.All
    ''' <summary>"*.jpg;IMG_??.png" - globs separated by ";", matched against the file NAME only.</summary>
    Friend Property NameMask As String = ""
    ''' <summary>The exclude mask of 4.2: same syntax, same rules, "everything except".</summary>
    Friend Property ExcludeMask As String = ""
    ''' <summary>Older than this many hours, measured from LastWriteTime.</summary>
    Friend Property MinAgeHours As Long?
    ''' <summary>Younger than this many hours, measured from LastWriteTime.</summary>
    Friend Property MaxAgeHours As Long?
    ''' <summary>Larger than this many bytes (strictly: "larger than 0" skips empty files).</summary>
    Friend Property MinSizeBytes As Long?
    ''' <summary>Smaller than this many bytes (strictly: "smaller than 0" matches nothing).</summary>
    Friend Property MaxSizeBytes As Long?
    ''' <summary>Hidden and system files are skipped unless this is set (the scan layer of 4.1).</summary>
    Friend Property IncludeHidden As Boolean

    Friend Function Clone() As ScheduledConditions
        Return New ScheduledConditions With {.TypeMask = TypeMask, .TimeFilter = TimeFilter, .NameMask = NameMask,
            .ExcludeMask = ExcludeMask, .MinAgeHours = MinAgeHours, .MaxAgeHours = MaxAgeHours,
            .MinSizeBytes = MinSizeBytes, .MaxSizeBytes = MaxSizeBytes, .IncludeHidden = IncludeHidden}
    End Function

End Class

''' <summary>What the last runs left on the operation (4): two timestamps, kept apart on purpose.</summary>
Friend NotInheritable Class ScheduledRunRecord

    ''' <summary>When the latest run STARTED, whatever came of it.</summary>
    Friend Property LastRunAt As DateTime?
    ''' <summary>When the latest run that finished without a failed file STARTED. "New since the last run"
    ''' reads this one, so a file that failed is picked up again and none is ever lost between runs.</summary>
    Friend Property LastSuccessAt As DateTime?
    Friend Property LastOutcome As ScheduledRunOutcome = ScheduledRunOutcome.None
    Friend Property LastTrigger As ScheduledTrigger = ScheduledTrigger.Schedule
    ''' <summary>Files acted on by the latest run.</summary>
    Friend Property LastFileCount As Integer
    Friend Property LastFailedCount As Integer
    ''' <summary>The first error of the latest run, as the machine wrote it - never translated.</summary>
    Friend Property LastError As String = ""
    ''' <summary>The slot the scheduler is armed for, or Nothing while it is not armed.</summary>
    Friend Property NextRunAt As DateTime?

    Friend Function Clone() As ScheduledRunRecord
        Return New ScheduledRunRecord With {.LastRunAt = LastRunAt, .LastSuccessAt = LastSuccessAt,
            .LastOutcome = LastOutcome, .LastTrigger = LastTrigger, .LastFileCount = LastFileCount,
            .LastFailedCount = LastFailedCount, .LastError = LastError, .NextRunAt = NextRunAt}
    End Function

End Class

''' <summary>An operation: what to act on, what to do with it, and when.</summary>
Friend NotInheritable Class ScheduledOperation

    ''' <summary>A GUID - a file-based store has no allocator. The reference; a name is never one.</summary>
    Friend Property Id As String = ""
    Friend Property Name As String = ""
    ''' <summary>The user's switch. Off = manual runs only.</summary>
    Friend Property Enabled As Boolean = True
    Friend Property Action As ScheduledAction = ScheduledAction.Copy
    Friend Property Source As New ScheduledLocation()
    ''' <summary>Nothing for delete.</summary>
    Friend Property Destination As New ScheduledLocation()
    Friend Property Conditions As New ScheduledConditions()
    ''' <summary>The daily anchor, minutes after midnight (0..1439).</summary>
    Friend Property AnchorMinutes As Integer
    ''' <summary>The date the grid is counted from. Set when the schedule is saved; it only fixes the
    ''' phase of intervals that do not divide a day and the day of multi-day ones.</summary>
    Friend Property StartDate As DateTime = DateTime.Today
    ''' <summary>Repeat interval in minutes, at least <see cref="ScheduleMath.MinIntervalMinutes"/>.</summary>
    Friend Property IntervalMinutes As Integer = ScheduleMath.DefaultIntervalMinutes
    Friend Property Conflict As ScheduledConflict = ScheduledConflict.Skip
    ''' <summary>Archive only (D4): delete the originals once the archive has been read back and matched.</summary>
    Friend Property DeleteOriginalsAfterVerify As Boolean
    Friend Property Halt As ScheduledHalt = ScheduledHalt.None
    ''' <summary>What the halt says: the wall's reason, the removed resource's name. Machine text, not translated.</summary>
    Friend Property HaltDetail As String = ""
    Friend Property CreatedAt As DateTime = DateTime.Now
    Friend Property Record As New ScheduledRunRecord()

    ''' <summary>True when the scheduler arms this operation. A walled one stays armed: at each slot
    ''' the scheduler only re-checks what stopped it, silently, and runs the operation again by itself
    ''' once that is gone; every other halt waits for the user.</summary>
    Friend ReadOnly Property IsArmable As Boolean
        Get
            Return Enabled AndAlso (Halt = ScheduledHalt.None OrElse Halt = ScheduledHalt.Wall)
        End Get
    End Property

    ''' <summary>The definition without the run record - what an edit works on.</summary>
    Friend Function Clone() As ScheduledOperation
        Return New ScheduledOperation With {.Id = Id, .Name = Name, .Enabled = Enabled, .Action = Action,
            .Source = Source.Clone(), .Destination = Destination.Clone(), .Conditions = Conditions.Clone(),
            .AnchorMinutes = AnchorMinutes, .StartDate = StartDate, .IntervalMinutes = IntervalMinutes,
            .Conflict = Conflict, .DeleteOriginalsAfterVerify = DeleteOriginalsAfterVerify, .Halt = Halt,
            .HaltDetail = HaltDetail, .CreatedAt = CreatedAt, .Record = Record.Clone()}
    End Function

    ''' <summary>The name the list shows: the user's text, or the folder the operation works on.</summary>
    Friend Function DisplayName() As String
        If Not String.IsNullOrWhiteSpace(Name) Then Return Name.Trim()
        Return Source.Describe()
    End Function

    ''' <summary>A destructive action takes something away from the source.</summary>
    Friend ReadOnly Property IsDestructive As Boolean
        Get
            Return Action = ScheduledAction.Move OrElse Action = ScheduledAction.Delete OrElse
                   (Action = ScheduledAction.Archive AndAlso DeleteOriginalsAfterVerify)
        End Get
    End Property

    ''' <summary>Copy, move and archive have a destination; delete has none.</summary>
    Friend ReadOnly Property NeedsDestination As Boolean
        Get
            Return Action <> ScheduledAction.Delete
        End Get
    End Property

End Class

''' <summary>A file the scan found, on either kind of side.</summary>
Friend NotInheritable Class SideEntry
    ''' <summary>Relative to the side's root; folders separated by "\" on a path, "/" on a resource.</summary>
    Friend Property RelativePath As String = ""
    Friend Property Name As String = ""
    Friend Property Length As Long
    ''' <summary>LastWriteTime, as the file system keeps it. The exact value a changed-since-listing check
    ''' compares against.</summary>
    Friend Property LastWriteUtc As DateTime
    Friend Property IsHiddenOrSystem As Boolean

    ''' <summary>LastWriteTime as wall-clock local time - what the age and time conditions are measured in.</summary>
    Friend ReadOnly Property LastWriteLocal As DateTime
        Get
            Return DateTime.SpecifyKind(LastWriteUtc.ToLocalTime(), DateTimeKind.Unspecified)
        End Get
    End Property

    ''' <summary>An entry written at the given wall-clock time (tests, and anything that has no UTC).</summary>
    Friend Shared Function WrittenAtLocal(name As String, length As Long, localTime As DateTime) As SideEntry
        Dim utc As DateTime = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), TimeZoneInfo.Local)
        Return New SideEntry With {.Name = name, .RelativePath = name, .Length = length, .LastWriteUtc = utc}
    End Function
End Class
