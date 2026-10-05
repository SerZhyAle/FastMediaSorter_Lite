#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Globalization
Imports System.Text.Json
Imports System.Collections.Generic
Imports System.Linq

''' <summary>
''' Persisted preferences introduced by SPECIFICATION_SETTINGS_EXPANSION.  The
''' legacy profile remains the source of truth for the old keys; this class keeps
''' only additive .NET 10 preferences and deliberately uses simple invariant
''' strings so a profile is still inspectable with the legacy VB registry tools.
''' </summary>
Public NotInheritable Class ModernViewerPreferences

    ' One range per limit, shared by Load() (registry) and Normalize() (import).
    Public Const ArchiveCacheMaxMbMin As Integer = 100
    Public Const ArchiveCacheMaxMbMax As Integer = 16384
    Public Const ArchiveMaxEntryMbMin As Integer = 16
    Public Const ArchiveMaxEntryMbMax As Integer = 4096
    Public Const ArchiveMaxEntriesMin As Integer = 100
    Public Const ArchiveMaxEntriesMax As Integer = 100000
    Public Const RemoteCacheMaxMbMin As Integer = 100
    Public Const RemoteCacheMaxMbMax As Integer = 16384
    Public Const SecureRememberMinutesMin As Integer = 0
    Public Const SecureRememberMinutesMax As Integer = 480
    Public Const TextPreviewMaxKbMin As Integer = 64
    Public Const TextPreviewMaxKbMax As Integer = 65536

    Public Property NameCollisionPolicy As String = "ask"
    Public Property AfterFileOperation As String = "next"
    ''' <summary>Show the next file once a copy has been queued. On by default - that is
    ''' the fast sorting run the number keys are for; off keeps the same file on screen,
    ''' which is what filing one picture into several folders wants
    ''' (SPECIFICATION_COPY_ACTIONS_REWORK.md §3.2).</summary>
    Public Property AdvanceAfterCopy As Boolean = True
    ''' <summary>Whether DEL aims for the Recycle Bin at all
    ''' (017_SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md §3.9). Off means every deletion is
    ''' permanent and SAYS so - the same wording Shift+DEL produces, because it is the same
    ''' thing: the user asked for it.</summary>
    Public Property DeleteToRecycleBin As Boolean = True

    ''' <summary>always | permanentOnly. The second value is the one §0.4 of that
    ''' specification says is unreachable today and is what a triage session wants: a
    ''' recycled deletion goes straight through, a PERMANENT one still stops and asks.
    ''' A third value, "never", silences the permanent question too: retired by ticket 028
    ''' Ф-5, restored by owner decision 2026-09-27 as an explicit choice.</summary>
    Public Property ConfirmDelete As String = "always"

    ''' <summary>Make a recipient slot's final folder when it is not there yet
    ''' (011_SPECIFICATION_SLOT_HEALTH_AND_HONEST_FAILURES_DOTNET10.md §3.6). On, because
    ''' "sort this card into 2026-08/best" otherwise means leaving the loop, making the
    ''' folder in Explorer and finding the place in the list again - and the mechanism
    ''' costs one line. Off makes a missing destination refuse with "not found", which is
    ''' today's behaviour with a better sentence. Only ever the FINAL segment, and only
    ''' when its parent answered (D6) - that is the difference between "the folder for this
    ''' session did not exist yet" and building a mistyped share name.</summary>
    Public Property CreateMissingDestination As Boolean = True

    Public Property IncludeSubfolders As Boolean
    Public Property IncludedExtensions As String = ""
    Public Property SortOtherFiles As Boolean = False
    Public Property DocumentExtensions As String = ".txt;.md;.log;.csv;.json;.xml;.html;.htm;.pdf;.epub;.fb2;.mobi"
    Public Property OtherExtensions As String = ""

    Public Function DocumentExtensionSet() As HashSet(Of String)
        Return ExtensionSet(DocumentExtensions)
    End Function

    Public Function OtherExtensionSet() As HashSet(Of String)
        Return ExtensionSet(OtherExtensions)
    End Function

    Private Shared Function ExtensionSet(value As String) As HashSet(Of String)
        Dim result As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Using doc As JsonDocument = JsonDocument.Parse(NormalizeExtensionJson(value))
            For Each item As JsonElement In doc.RootElement.EnumerateArray()
                result.Add(item.GetString())
            Next
        End Using
        Return result
    End Function
    Public Property InterfaceScalePercent As Integer
    Public Property InterfaceScaleRelativePercent As Integer

    Public Function InterfaceFactor(dpi As Integer) As Single
        If InterfaceScaleRelativePercent > 0 Then Return UiScale.Factor(0, dpi) * InterfaceScaleRelativePercent / 100.0F
        Return UiScale.Factor(InterfaceScalePercent, dpi)
    End Function

    Friend Function FolderEnumerationKey() As String
        Return JsonSerializer.Serialize(New Object() {IncludeSubfolders, IncludedExtensions, IncludeVideoInNavigation,
            IncludeAudioInNavigation, IncludeDocumentInNavigation, DocumentExtensions, SortOtherFiles,
            OtherExtensions, SecureIncludeInNavigation, ArchiveMaxEntryMb, ArchiveMaxEntries})
    End Function

    Friend Function ChromeLayoutKey() As String
        Return JsonSerializer.Serialize(New Object() {InterfaceScalePercent, InterfaceScaleRelativePercent, ChromeSize,
            ChromePinned, ChromeOpacityPercent, ChromeCornerRadius, ChromeRow1Pinned, ChromeRow2Pinned, ChromeStatusPinned})
    End Function

    Friend Function OverlayLayoutKey() As String
        Return JsonSerializer.Serialize(New Object() {RecipientsOverlayPosition, RecipientsOverlayWidth,
            RecipientsOverlayFontSize, RecipientsOverlayOpacity, RecipientsOverlayVisibleRows, RecipientsOverlayPinned})
    End Function

    Friend Function Snapshot() As ModernViewerPreferences
        Return JsonSerializer.Deserialize(Of ModernViewerPreferences)(JsonSerializer.Serialize(Me))
    End Function
    Public Property NewImageScaleMode As String = "fit"
    Public Property ReduceMotion As Boolean

    ''' <summary>Retired coexistence value, retained only for old settings profile round trips.
    ''' The modern viewer always uses floating chrome; this value no longer selects a layout.</summary>
    Public Property ModernChrome As Boolean = True

    ''' <summary>Keeps the floating panels up permanently instead of auto-hiding them. Not the old docked
    ''' geometry - only a disabled auto-hide.</summary>
    Public Property ChromeOpacityPercent As Integer = 86
    Public Property ChromeCornerRadius As Integer = 6
    Public Property ChromePinned As Boolean

    ''' <summary>Which size the floating panels take: "small", "medium" (the historical size), "large",
    ''' "adaptive" (follows the width of the window as well as the monitor scale) or "touch" (large targets: every button at least 44 px, 125 Q125-4). It multiplies
    ''' whatever <see cref="InterfaceScalePercent"/> settles on, so it never fights that setting.</summary>
    Public Property ChromeSize As String = "medium"

    ''' <summary>Per-panel pins, set by the small button in each panel's corner: a pinned panel never
    ''' auto-hides. <see cref="ChromePinned"/> is the same thing for every panel at once.</summary>
    Public Property ChromeRow1Pinned As Boolean
    Public Property ChromeRow2Pinned As Boolean
    Public Property ChromeStatusPinned As Boolean

    ''' <summary>The recipients overlay's own pin (2026-10-04 revision of 117 Ф-2), set by the small
    ''' button in its corner: a pinned table never auto-hides. <see cref="ChromePinned"/> holds it up
    ''' as well; the per-panel pins do not - the table pins like a panel, not with the panels.</summary>
    Public Property RecipientsOverlayPinned As Boolean

    Public Property RecipientsOverlayPosition As String = "topLeft"
    Public Property RecipientsOverlayWidth As Integer = 280
    Public Property RecipientsOverlayFontSize As Integer = 9
    Public Property RecipientsOverlayOpacity As Integer = 88
    Public Property RecipientsOverlayVisibleRows As Integer = 11   ' ten slots + Delete fit without scrolling (audit B-13)

    Public Property SlideshowRandomOrder As String = "natural"
    Public Property StopSlideshowOnManualNavigation As Boolean = True
    Public Property SlideshowUiMode As String = "none"

    Public Property VideoAutoplay As Boolean = True
    ''' <summary>Whether video files participate in folder navigation. On by default so
    ''' existing profiles keep their historical mixed image/video browsing behaviour.</summary>
    Public Property IncludeVideoInNavigation As Boolean = True
    Public Property RememberVideoPosition As Boolean = True
    Public Property VideoControlsHideDelaySec As Integer = 3
    Public Property ShowVideoControlsWhenPaused As Boolean = True
    Public Property VideoSingleClickAction As String = "pauseResume"
    Public Property VideoEndAction As String = "stay"
    Public Property PreferredAudioLanguage As String = ""
    Public Property PreferredSubtitleLanguage As String = ""

    ''' <summary>The three keyboard seek steps of 119 Ф-2, in seconds. Shift+arrows use the
    ''' short one, Alt+arrows the medium one, Ctrl+arrows the long one (VLC's scheme).</summary>
    Public Property SeekShortSec As Integer = 3
    Public Property SeekMediumSec As Integer = 10
    Public Property SeekLongSec As Integer = 60

    ''' <summary>Seconds of pointer stillness before the cursor hides itself in a slideshow
    ''' or full-screen viewing (119 Ф-6, modern only). Zero means never.</summary>
    Public Property HideCursorAfterSec As Integer = 3

    ''' <summary>What to do when an audio track finishes (SPECIFICATION_AUDIO_FIRST_CLASS_DOTNET10.md PA-4).
    ''' Default "next" - advancing at the end of a track makes sense in a music folder.</summary>
    Public Property AudioEndAction As String = "next"
    ''' <summary>Whether audio files participate in folder navigation. On by default so
    ''' an upgrade never silently hides music from an existing sorting pass.</summary>
    Public Property IncludeAudioInNavigation As Boolean = True
    ''' <summary>Opt-in document and book sorting (003 PK), even without a preview.</summary>
    Public Property IncludeDocumentInNavigation As Boolean = False
    ''' <summary>Show audio transport bar always, never hide on idle
    ''' (SPECIFICATION_AUDIO_FIRST_CLASS_DOTNET10.md PA-3). On by default - nothing else is
    ''' on the audio surface to interact with, so hiding covers would be pointless.</summary>
    Public Property AudioControlsAlwaysVisible As Boolean = True
    ''' <summary>Show the branded fallback when the audio file has no embedded cover art.
    ''' On by default: an otherwise empty audio surface is not useful.</summary>
    Public Property AudioVisualiser As Boolean = True
    ''' <summary>Stop playback after N minutes (PA-6). Zero means off.</summary>
    Public Property SleepTimerMinutes As Integer = 0

    Public Property StartupOpenMode As String = "home"
    ''' <summary>Reopen the exact file that was on screen, and let the existing
    ''' VideoPositionHistory carry the playback position across the restart
    ''' (SPECIFICATION_RESUME_LAST_PLAYBACK_DOTNET10 §1). Off by default: restoring is a
    ''' guess the application makes on its own, so it has to be asked for. Deliberately
    ''' separate from StartupOpenMode - that one says WHAT to open, this one says "and
    ''' carry on from the same frame".</summary>
    Public Property ResumeLastPlayback As Boolean = False
    Public Property AllowNewWindows As Boolean = False
    Public Property RecentFilesLimit As Integer = 50
    Public Property RecentFoldersLimit As Integer = 100
    Public Property OcrDiskCacheMaxMb As Integer = 250
    ''' <summary>Budget for the decode cache, in megabytes
    ''' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §6.2). Zero means the
    ''' cache is OFF - nothing read, nothing written. Note the adjacent OcrDiskCacheMaxMb reads
    ''' zero the OTHER way - there it means "no limit" (DiskCacheTrim, and the settings hint
    ''' says so); this comment used to claim the two agreed (audit 051 E-07). Aligning them is
    ''' the owner's call. The ceiling is higher than the OCR cache's because one entry here
    ''' is a whole transcoded animation, not a page of JSON.</summary>
    Public Property DecodeCacheMaxMb As Integer = 512

    ''' <summary>How the F3 panel shows the folder: "grid" (thumbnail cards) or "list" (name /
    ''' kind / size / date). Remembered between sessions and toggled with Ctrl+L
    ''' (005_SPECIFICATION_THUMBNAIL_GRID_TRIAGE_DOTNET10.md PG-5, PG-7).</summary>
    Public Property PanelViewMode As String = "grid"

    ''' <summary>Side of a thumbnail card in the F3 panel, in pixels: 96, 128, 192 or 256
    ''' (PG-3). Zero means "not chosen here yet" - the panel then follows the legacy card size
    ''' of the old settings combo, rounded to the nearest of these, so an upgrade does not
    ''' change what a user already picked.</summary>
    Public Property PanelThumbnailSize As Integer = 0

    ''' <summary>Budget of the on-disk thumbnail cache, in megabytes (PG-3). Zero switches the
    ''' cache OFF - nothing read, nothing written - and the panel then asks the shell and
    ''' decodes as it did before the cache existed.</summary>
    Public Property PanelThumbCacheMb As Integer = 256

    ''' <summary>Ceiling on an archive session's temporary directory, in megabytes
    ''' (010_SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md §5.3, §9). Enforced by LRU
    ''' eviction (§5.4): the entry just shown and its immediate neighbours are never
    ''' evicted, so browsing never stalls even when the budget is tight.</summary>
    Public Property ArchiveCacheMaxMb As Integer = 2048
    ''' <summary>Ceiling on a single archive entry the viewer will extract, in megabytes
    ''' (§5.3, §9). An entry over this size is refused with a plain message instead of
    ''' being written to disk.</summary>
    Public Property ArchiveMaxEntryMb As Integer = 512
    ''' <summary>Ceiling on how many entries of one archive become rows in the file list
    ''' (§5.3, §9). An archive with more entries than this is shown truncated, and the
    ''' status line says so.</summary>
    Public Property ArchiveMaxEntries As Integer = 20000

    ''' <summary>Ceiling on an open remote resource's cache session, in megabytes
    ''' (006_SPECIFICATION_FILESYSTEM_PROVIDER_LAYER_DOTNET10.md PV-3). Enforced by LRU
    ''' eviction that never touches the file on screen or its neighbours. No settings row
    ''' yet: nothing in this build opens a resource that materialises (ticket 007).</summary>
    Public Property RemoteCacheMaxMb As Integer = RemoteCacheStore.Default_Max_Mb

    ''' <summary>Whether .fd-sec containers are listed in the folder at all
    ''' (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §9). On: a container is an
    ''' ordinary file of the user's, and hiding it by default would mean a folder that
    ''' silently shows fewer files than it holds. The navigation filters of ticket 009
    ''' deliberately do NOT apply to it - its real kind is unknown until it is opened, and
    ''' deriving a password-protected key to decide whether a file belongs in a list is not
    ''' a thing a folder listing may do.</summary>
    Public Property SecureIncludeInNavigation As Boolean = True

    ''' <summary>Idle timeout of the session credential, in minutes; 0 means "until the
    ''' program closes" (§5.5, §9). The credential is held in memory only and is never
    ''' written anywhere, so this is about a screen left unattended, not about storage.</summary>
    Public Property SecureRememberMinutes As Integer = 15

    ''' <summary>Show the sealed true name on screen once a container is open - the status
    ''' line, the current-file label and the info overlay (§9, O-1). On, because the user
    ''' needs to know what is open; off turns it off everywhere at once, which is what
    ''' somebody sharing a screen wants.</summary>
    Public Property SecureShowTrueName As Boolean = True

    Public Property CustomHotkeysJson As String = "{}"
    ''' <summary>Bounded JSON stores. They are intentionally profile keys rather than
    ''' binary settings so an old profile remains readable and portable.</summary>
    Public Property FolderZoomHistory As String = "[]"
    Public Property VideoPositionHistory As String = "[]"
    Public Property DocumentPageHistory As String = "[]"
    Public Property TextPreviewMaxKb As Integer = 2048

    Public NotInheritable Class DocumentPageEntry
        Public Property Path As String = ""
        Public Property LastWriteUtcTicks As Long
        Public Property Length As Long
        Public Property PageNumber As Integer
    End Class

    Public NotInheritable Class VideoPositionEntry
        Public Property Path As String = ""
        Public Property LastWriteUtcTicks As Long
        Public Property Length As Long
        Public Property Position As Single
    End Class

    ''' <summary>One folder's last zoom, for NewImageScaleMode = "perFolder" (§4.2).
    ''' A scale, not a rectangle: the spec restores how big the next picture opens, and
    ''' says nothing about where a previous picture had been panned to.</summary>
    Public NotInheritable Class FolderZoomEntry
        Public Property Path As String = ""
        Public Property Factor As Double
    End Class

    Public Shared Function Load() As ModernViewerPreferences
        Dim p As New ModernViewerPreferences()
        p.NameCollisionPolicy = ReadChoice("NameCollisionPolicy", p.NameCollisionPolicy, "ask", "skip", "rename", "replace")
        p.AfterFileOperation = ReadChoice("AfterFileOperation", p.AfterFileOperation, "next", "stay", "closeIfEmpty")
        p.AdvanceAfterCopy = ReadBool("AdvanceAfterCopy", p.AdvanceAfterCopy)
        p.DeleteToRecycleBin = ReadBool("DeleteToRecycleBin", p.DeleteToRecycleBin)
        p.ConfirmDelete = ReadConfirmDelete()
        p.CreateMissingDestination = ReadBool("CreateMissingDestination", p.CreateMissingDestination)
        p.IncludeSubfolders = ReadBool("IncludeSubfolders", p.IncludeSubfolders)
        p.IncludedExtensions = NormalizeExtensionJson(ReadString("IncludedExtensions", p.IncludedExtensions))
        p.SortOtherFiles = ReadBool("SortOtherFiles", p.SortOtherFiles)
        p.DocumentExtensions = NormalizeExtensionJson(ReadString("DocumentExtensions", p.DocumentExtensions))
        p.OtherExtensions = NormalizeExtensionJson(ReadString("OtherExtensions", p.OtherExtensions))
        p.InterfaceScaleRelativePercent = UiScale.NormalizePercent(ReadInt("InterfaceScaleRelativePercent", 0, 0, 150))
        p.InterfaceScalePercent = ReadInt("InterfaceScalePercent", p.InterfaceScalePercent, 0, 150)
        If p.InterfaceScalePercent <> 0 AndAlso p.InterfaceScalePercent < 90 Then p.InterfaceScalePercent = 90
        p.NewImageScaleMode = ReadChoice("NewImageScaleMode", p.NewImageScaleMode, "fit", "actual", "perFolder")
        p.ReduceMotion = ReadBool("ReduceMotion", p.ReduceMotion)
        p.ModernChrome = ReadBool("ModernChrome", p.ModernChrome)
        p.ChromeOpacityPercent = ReadInt("ChromeOpacityPercent", p.ChromeOpacityPercent, 0, 100)
        p.ChromeCornerRadius = ReadInt("ChromeCornerRadius", p.ChromeCornerRadius, 0, 16)
        p.ChromePinned = ReadBool("ChromePinned", p.ChromePinned)
        p.ChromeSize = ReadChoice("ChromeSize", p.ChromeSize, "medium", "small", "large", "adaptive", "touch")
        p.ChromeRow1Pinned = ReadBool("ChromeRow1Pinned", p.ChromeRow1Pinned)
        p.ChromeRow2Pinned = ReadBool("ChromeRow2Pinned", p.ChromeRow2Pinned)
        p.ChromeStatusPinned = ReadBool("ChromeStatusPinned", p.ChromeStatusPinned)
        p.RecipientsOverlayPinned = ReadBool("RecipientsOverlayPinned", p.RecipientsOverlayPinned)

        p.RecipientsOverlayPosition = ReadChoice("RecipientsOverlayPosition", p.RecipientsOverlayPosition, "topLeft", "topRight", "bottomLeft", "bottomRight")
        p.RecipientsOverlayWidth = ReadInt("RecipientsOverlayWidth", p.RecipientsOverlayWidth, 180, 520)
        p.RecipientsOverlayFontSize = ReadInt("RecipientsOverlayFontSize", p.RecipientsOverlayFontSize, 9, 18)
        p.RecipientsOverlayOpacity = ReadInt("RecipientsOverlayOpacity", p.RecipientsOverlayOpacity, 40, 100)
        p.RecipientsOverlayVisibleRows = ReadInt("RecipientsOverlayVisibleRows", p.RecipientsOverlayVisibleRows, 3, 11)

        p.SlideshowRandomOrder = ReadChoice("SlideshowRandomOrder", p.SlideshowRandomOrder, "natural", "random", "shuffleCycle")
        p.StopSlideshowOnManualNavigation = ReadBool("StopSlideshowOnManualNavigation", p.StopSlideshowOnManualNavigation)
        p.SlideshowUiMode = ReadChoice("SlideshowUiMode", p.SlideshowUiMode, "none", "toolbar", "toolbarAndStatus")

        p.VideoAutoplay = ReadBool("VideoAutoplay", p.VideoAutoplay)
        p.IncludeVideoInNavigation = ReadBool("IncludeVideoInNavigation", p.IncludeVideoInNavigation)
        p.RememberVideoPosition = ReadBool("RememberVideoPosition", p.RememberVideoPosition)
        p.VideoControlsHideDelaySec = ReadInt("VideoControlsHideDelaySec", p.VideoControlsHideDelaySec, 1, 15)
        p.ShowVideoControlsWhenPaused = ReadBool("ShowVideoControlsWhenPaused", p.ShowVideoControlsWhenPaused)
        p.VideoSingleClickAction = ReadChoice("VideoSingleClickAction", p.VideoSingleClickAction, "pauseResume", "nextFile")
        p.VideoEndAction = ReadChoice("VideoEndAction", p.VideoEndAction, "stay", "nextFile", "repeat")
        p.SeekShortSec = ReadInt("SeekShortSec", p.SeekShortSec, 1, 600)
        p.SeekMediumSec = ReadInt("SeekMediumSec", p.SeekMediumSec, 1, 600)
        p.SeekLongSec = ReadInt("SeekLongSec", p.SeekLongSec, 1, 600)
        p.HideCursorAfterSec = ReadInt("HideCursorAfterSec", p.HideCursorAfterSec, 0, 30)
        p.PreferredAudioLanguage = ReadString("PreferredAudioLanguage", p.PreferredAudioLanguage)
        p.PreferredSubtitleLanguage = ReadString("PreferredSubtitleLanguage", p.PreferredSubtitleLanguage)

        p.AudioEndAction = ReadChoice("AudioEndAction", p.AudioEndAction, "next", "stay", "repeat")
        p.IncludeAudioInNavigation = ReadBool("IncludeAudioInNavigation", p.IncludeAudioInNavigation)
        p.IncludeDocumentInNavigation = ReadBool("IncludeDocumentInNavigation", p.IncludeDocumentInNavigation)
        p.AudioControlsAlwaysVisible = ReadBool("AudioControlsAlwaysVisible", p.AudioControlsAlwaysVisible)
        p.AudioVisualiser = ReadBool("AudioVisualiser", p.AudioVisualiser)
        p.SleepTimerMinutes = ReadInt("SleepTimerMinutes", p.SleepTimerMinutes, 0, 180)

        p.StartupOpenMode = ReadChoice("StartupOpenMode", p.StartupOpenMode, "home", "lastFolder", "lastFile")
        p.ResumeLastPlayback = ReadBool("ResumeLastPlayback", p.ResumeLastPlayback)
        p.AllowNewWindows = ReadBool("AllowNewWindows", p.AllowNewWindows)
        p.RecentFilesLimit = ReadInt("RecentFilesLimit", p.RecentFilesLimit, 0, 200)
        p.RecentFoldersLimit = ReadInt("RecentFoldersLimit", p.RecentFoldersLimit, 0, 200)
        p.OcrDiskCacheMaxMb = ReadInt("OcrDiskCacheMaxMb", p.OcrDiskCacheMaxMb, 0, 1024)
        p.DecodeCacheMaxMb = ReadInt("DecodeCacheMaxMb", p.DecodeCacheMaxMb, 0, 8192)
        p.PanelViewMode = ReadChoice("PanelViewMode", p.PanelViewMode, "grid", "list")
        p.PanelThumbnailSize = SnapPanelThumbnailSize(ReadInt("PanelThumbnailSize", p.PanelThumbnailSize, 0, 256))
        p.PanelThumbCacheMb = ReadInt("PanelThumbCacheMb", p.PanelThumbCacheMb, 0, 4096)
        p.ArchiveCacheMaxMb = ReadInt("ArchiveCacheMaxMb", p.ArchiveCacheMaxMb, ArchiveCacheMaxMbMin, ArchiveCacheMaxMbMax)
        p.ArchiveMaxEntryMb = ReadInt("ArchiveMaxEntryMb", p.ArchiveMaxEntryMb, ArchiveMaxEntryMbMin, ArchiveMaxEntryMbMax)
        p.ArchiveMaxEntries = ReadInt("ArchiveMaxEntries", p.ArchiveMaxEntries, ArchiveMaxEntriesMin, ArchiveMaxEntriesMax)
        p.RemoteCacheMaxMb = ReadInt("RemoteCacheMaxMb", p.RemoteCacheMaxMb, RemoteCacheMaxMbMin, RemoteCacheMaxMbMax)
        p.SecureIncludeInNavigation = ReadBool("SecureIncludeInNavigation", p.SecureIncludeInNavigation)
        p.SecureRememberMinutes = ReadInt("SecureRememberMinutes", p.SecureRememberMinutes, SecureRememberMinutesMin, SecureRememberMinutesMax)
        p.SecureShowTrueName = ReadBool("SecureShowTrueName", p.SecureShowTrueName)
        p.TextPreviewMaxKb = ReadInt("TextPreviewMaxKb", p.TextPreviewMaxKb, TextPreviewMaxKbMin, TextPreviewMaxKbMax)
        p.CustomHotkeysJson = NormalizeJson(ReadString("CustomHotkeys", p.CustomHotkeysJson))
        p.FolderZoomHistory = NormalizeArrayJson(ReadString("FolderZoomHistory", p.FolderZoomHistory))
        p.VideoPositionHistory = NormalizeArrayJson(ReadString("VideoPositionHistory", p.VideoPositionHistory))
        p.DocumentPageHistory = NormalizeArrayJson(ReadString("DocumentPageHistory", p.DocumentPageHistory))
        Return p
    End Function

    Public Sub Save()
        ' Secondary processes never overwrite the primary process's profile. The
        ' AllowNewWindows checkbox writes its one key immediately by design.
        If MultiWindowPolicy.IsSecondaryInstance() Then Return

        WriteString("NameCollisionPolicy", NameCollisionPolicy)
        WriteString("AfterFileOperation", AfterFileOperation)
        WriteBool("AdvanceAfterCopy", AdvanceAfterCopy)
        WriteBool("DeleteToRecycleBin", DeleteToRecycleBin)
        WriteString("ConfirmDelete", ConfirmDelete)
        WriteBool("CreateMissingDestination", CreateMissingDestination)
        WriteBool("IncludeSubfolders", IncludeSubfolders)
        WriteString("IncludedExtensions", NormalizeExtensionJson(IncludedExtensions))
        WriteBool("SortOtherFiles", SortOtherFiles)
        WriteString("DocumentExtensions", NormalizeExtensionJson(DocumentExtensions))
        WriteString("OtherExtensions", NormalizeExtensionJson(OtherExtensions))
        WriteString("InterfaceScaleRelativePercent", InterfaceScaleRelativePercent.ToString(CultureInfo.InvariantCulture))
        WriteString("InterfaceScalePercent", InterfaceScalePercent.ToString(CultureInfo.InvariantCulture))
        WriteString("NewImageScaleMode", NewImageScaleMode)
        WriteBool("ReduceMotion", ReduceMotion)
        WriteBool("ModernChrome", ModernChrome)
        WriteString("ChromeOpacityPercent", ChromeOpacityPercent.ToString(CultureInfo.InvariantCulture))
        WriteString("ChromeCornerRadius", ChromeCornerRadius.ToString(CultureInfo.InvariantCulture))
        WriteBool("ChromePinned", ChromePinned)
        WriteString("ChromeSize", ChromeSize)
        WriteBool("ChromeRow1Pinned", ChromeRow1Pinned)
        WriteBool("ChromeRow2Pinned", ChromeRow2Pinned)
        WriteBool("ChromeStatusPinned", ChromeStatusPinned)
        WriteBool("RecipientsOverlayPinned", RecipientsOverlayPinned)
        WriteString("RecipientsOverlayPosition", RecipientsOverlayPosition)
        WriteString("RecipientsOverlayWidth", RecipientsOverlayWidth.ToString(CultureInfo.InvariantCulture))
        WriteString("RecipientsOverlayFontSize", RecipientsOverlayFontSize.ToString(CultureInfo.InvariantCulture))
        WriteString("RecipientsOverlayOpacity", RecipientsOverlayOpacity.ToString(CultureInfo.InvariantCulture))
        WriteString("RecipientsOverlayVisibleRows", RecipientsOverlayVisibleRows.ToString(CultureInfo.InvariantCulture))
        WriteString("SlideshowRandomOrder", SlideshowRandomOrder)
        WriteBool("StopSlideshowOnManualNavigation", StopSlideshowOnManualNavigation)
        WriteString("SlideshowUiMode", SlideshowUiMode)
        WriteBool("VideoAutoplay", VideoAutoplay)
        WriteBool("IncludeVideoInNavigation", IncludeVideoInNavigation)
        WriteBool("RememberVideoPosition", RememberVideoPosition)
        WriteString("VideoControlsHideDelaySec", VideoControlsHideDelaySec.ToString(CultureInfo.InvariantCulture))
        WriteBool("ShowVideoControlsWhenPaused", ShowVideoControlsWhenPaused)
        WriteString("VideoSingleClickAction", VideoSingleClickAction)
        WriteString("VideoEndAction", VideoEndAction)
        WriteString("SeekShortSec", SeekShortSec.ToString(CultureInfo.InvariantCulture))
        WriteString("SeekMediumSec", SeekMediumSec.ToString(CultureInfo.InvariantCulture))
        WriteString("SeekLongSec", SeekLongSec.ToString(CultureInfo.InvariantCulture))
        WriteString("HideCursorAfterSec", HideCursorAfterSec.ToString(CultureInfo.InvariantCulture))
        WriteString("PreferredAudioLanguage", PreferredAudioLanguage)
        WriteString("PreferredSubtitleLanguage", PreferredSubtitleLanguage)

        WriteString("AudioEndAction", AudioEndAction)
        WriteBool("IncludeAudioInNavigation", IncludeAudioInNavigation)
        WriteBool("IncludeDocumentInNavigation", IncludeDocumentInNavigation)
        WriteBool("AudioControlsAlwaysVisible", AudioControlsAlwaysVisible)
        WriteBool("AudioVisualiser", AudioVisualiser)
        WriteString("SleepTimerMinutes", SleepTimerMinutes.ToString(CultureInfo.InvariantCulture))

        WriteString("StartupOpenMode", StartupOpenMode)
        WriteBool("ResumeLastPlayback", ResumeLastPlayback)
        ' Keep a primary process with an older in-memory value from resurrecting a
        ' choice that a secondary process just wrote for subsequent launches.
        AllowNewWindows = ReadBool("AllowNewWindows", AllowNewWindows)
        WriteBool("AllowNewWindows", AllowNewWindows)
        WriteString("RecentFilesLimit", RecentFilesLimit.ToString(CultureInfo.InvariantCulture))
        WriteString("RecentFoldersLimit", RecentFoldersLimit.ToString(CultureInfo.InvariantCulture))
        WriteString("OcrDiskCacheMaxMb", OcrDiskCacheMaxMb.ToString(CultureInfo.InvariantCulture))
        WriteString("DecodeCacheMaxMb", DecodeCacheMaxMb.ToString(CultureInfo.InvariantCulture))
        WriteString("PanelViewMode", PanelViewMode)
        WriteString("PanelThumbnailSize", PanelThumbnailSize.ToString(CultureInfo.InvariantCulture))
        WriteString("PanelThumbCacheMb", PanelThumbCacheMb.ToString(CultureInfo.InvariantCulture))
        WriteString("ArchiveCacheMaxMb", ArchiveCacheMaxMb.ToString(CultureInfo.InvariantCulture))
        WriteString("ArchiveMaxEntryMb", ArchiveMaxEntryMb.ToString(CultureInfo.InvariantCulture))
        WriteString("ArchiveMaxEntries", ArchiveMaxEntries.ToString(CultureInfo.InvariantCulture))
        WriteString("RemoteCacheMaxMb", RemoteCacheMaxMb.ToString(CultureInfo.InvariantCulture))
        ' No value here stores a credential, a derived key or a sealed true name, and none
        ' ever may (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §9).
        WriteBool("SecureIncludeInNavigation", SecureIncludeInNavigation)
        WriteString("SecureRememberMinutes", SecureRememberMinutes.ToString(CultureInfo.InvariantCulture))
        WriteBool("SecureShowTrueName", SecureShowTrueName)
        WriteString("TextPreviewMaxKb", TextPreviewMaxKb.ToString(CultureInfo.InvariantCulture))
        WriteString("CustomHotkeys", NormalizeJson(CustomHotkeysJson))
        WriteString("FolderZoomHistory", NormalizeArrayJson(FolderZoomHistory))
        WriteString("VideoPositionHistory", NormalizeArrayJson(VideoPositionHistory))
        WriteString("DocumentPageHistory", NormalizeArrayJson(DocumentPageHistory))
    End Sub

    Public Function ExportJson(Optional includePersonalData As Boolean = False) As String
        Normalize()
        Dim exported As ModernViewerPreferences = JsonSerializer.Deserialize(Of ModernViewerPreferences)(JsonSerializer.Serialize(Me))
        If Not includePersonalData Then
            ' Paths and resume positions are personal data. The normal export is safe to
            ' share; an explicit future UI opt-in can use includePersonalData:=True.
            exported.FolderZoomHistory = "[]"
            exported.VideoPositionHistory = "[]"
            exported.DocumentPageHistory = "[]"
        End If
        Return JsonSerializer.Serialize(New SettingsExport With {.SchemaVersion = 1, .Preferences = exported},
                                        New JsonSerializerOptions With {.WriteIndented = True})
    End Function

    Public Function IncludedExtensionsForEditing() As String
        Try
            Using doc As JsonDocument = JsonDocument.Parse(NormalizeExtensionJson(IncludedExtensions))
                Return String.Join("; ", doc.RootElement.EnumerateArray().Select(Function(item) item.GetString()))
            End Using
        Catch
            Return String.Empty
        End Try
    End Function

    Public Sub SetIncludedExtensionsFromEditing(value As String)
        IncludedExtensions = NormalizeExtensionJson(value)
    End Sub

    Public Function RememberedVideoPosition(path As String, lastWriteUtcTicks As Long, length As Long) As Single
        If Not RememberVideoPosition OrElse String.IsNullOrEmpty(path) Then Return -1.0F
        Dim entries As List(Of VideoPositionEntry) = ReadVideoPositions()
        Dim entry As VideoPositionEntry = entries.FirstOrDefault(Function(item) String.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
        If entry Is Nothing OrElse entry.LastWriteUtcTicks <> lastWriteUtcTicks OrElse entry.Length <> length Then Return -1.0F
        If entry.Position <= 0.0F OrElse entry.Position >= VideoWatchedToEndPosition Then Return -1.0F
        entries.Remove(entry)
        entries.Add(entry) ' read is use: maintain the LRU order
        VideoPositionHistory = JsonSerializer.Serialize(entries)
        Return entry.Position
    End Function

    ''' <summary>From this fraction on a video counts as watched to the end: it keeps no
    ''' resume point and opens from the start (RESUME spec §3.3, audit 050 D-11). Below 1.0
    ''' on purpose - libvlc reports 0.99x for a file that played out, and a resume "in the
    ''' last second" with VideoEndAction = nextFile skipped the file the moment it opened.</summary>
    Public Const VideoWatchedToEndPosition As Single = 0.97F

    Public Sub StoreVideoPosition(path As String, lastWriteUtcTicks As Long, length As Long, position As Single)
        ' Zero or less says nothing: a player that has not started yet (the user flipped on
        ' before the pending seek landed) or one already stopped. The entry is left alone.
        If Not RememberVideoPosition OrElse String.IsNullOrEmpty(path) OrElse position <= 0.0F Then Return
        Dim entries As List(Of VideoPositionEntry) = ReadVideoPositions()
        ' Remove FIRST (050 D-11): a position at the end must clear the older mid-video
        ' entry, not leave it behind to resume at next time.
        Dim removed As Integer = entries.RemoveAll(Function(item) String.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
        If position >= VideoWatchedToEndPosition Then
            If removed > 0 Then VideoPositionHistory = JsonSerializer.Serialize(entries)
            Return
        End If
        entries.Add(New VideoPositionEntry With {.Path = path, .LastWriteUtcTicks = lastWriteUtcTicks, .Length = length, .Position = position})
        If entries.Count > 200 Then entries.RemoveRange(0, entries.Count - 200)
        VideoPositionHistory = JsonSerializer.Serialize(entries)
    End Sub

    ''' <summary>The zoom the given folder was last left at, or -1 when it has none.
    ''' Reading is a use, so the entry moves to the young end of the 50-entry LRU §4.2
    ''' bounds this list to.</summary>
    Public Function RememberedFolderZoom(folder As String) As Double
        If String.IsNullOrEmpty(folder) Then Return -1.0R
        Dim entries As List(Of FolderZoomEntry) = ReadFolderZooms()
        Dim entry As FolderZoomEntry = entries.FirstOrDefault(Function(item) String.Equals(item.Path, folder, StringComparison.OrdinalIgnoreCase))
        If entry Is Nothing OrElse entry.Factor <= 0.0R Then Return -1.0R
        entries.Remove(entry)
        entries.Add(entry)
        FolderZoomHistory = JsonSerializer.Serialize(entries)
        Return entry.Factor
    End Function

    ''' <summary>Drops a folder's record, which is how "Fit" is stored: Fit is not a
    ''' number - it depends on the window and the picture - so the honest way to remember
    ''' it is to have nothing to restore, which §4.2 already defines as Fit.</summary>
    Public Sub ForgetFolderZoom(folder As String)
        If String.IsNullOrEmpty(folder) Then Return
        Dim entries As List(Of FolderZoomEntry) = ReadFolderZooms()
        If entries.RemoveAll(Function(item) String.Equals(item.Path, folder, StringComparison.OrdinalIgnoreCase)) = 0 Then Return
        FolderZoomHistory = JsonSerializer.Serialize(entries)
    End Sub

    Public Sub StoreFolderZoom(folder As String, factor As Double)
        If String.IsNullOrEmpty(folder) OrElse factor <= 0.0R Then Return
        Dim entries As List(Of FolderZoomEntry) = ReadFolderZooms()
        entries.RemoveAll(Function(item) String.Equals(item.Path, folder, StringComparison.OrdinalIgnoreCase))
        entries.Add(New FolderZoomEntry With {.Path = folder, .Factor = factor})
        If entries.Count > 50 Then entries.RemoveRange(0, entries.Count - 50)
        FolderZoomHistory = JsonSerializer.Serialize(entries)
    End Sub

    Public Function RememberedDocumentPage(path As String, lastWriteUtcTicks As Long, length As Long) As Integer
        If String.IsNullOrEmpty(path) Then Return 1
        Dim entries As List(Of DocumentPageEntry) = ReadDocumentPages()
        Dim entry As DocumentPageEntry = entries.FirstOrDefault(Function(item) String.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
        If entry Is Nothing OrElse entry.LastWriteUtcTicks <> lastWriteUtcTicks OrElse entry.Length <> length Then Return 1
        If entry.PageNumber < 1 Then Return 1
        entries.Remove(entry)
        entries.Add(entry) ' read is use: maintain LRU order
        DocumentPageHistory = JsonSerializer.Serialize(entries)
        Return entry.PageNumber
    End Function

    Public Sub RecordDocumentPage(path As String, lastWriteUtcTicks As Long, length As Long, pageNumber As Integer)
        If String.IsNullOrEmpty(path) OrElse pageNumber < 1 Then Return
        Dim entries As List(Of DocumentPageEntry) = ReadDocumentPages()
        entries.RemoveAll(Function(item) String.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
        entries.Add(New DocumentPageEntry With {.Path = path, .LastWriteUtcTicks = lastWriteUtcTicks, .Length = length, .PageNumber = pageNumber})
        If entries.Count > 200 Then entries.RemoveRange(0, entries.Count - 200)
        DocumentPageHistory = JsonSerializer.Serialize(entries)
    End Sub

    Private Function ReadDocumentPages() As List(Of DocumentPageEntry)
        Try
            Dim list As List(Of DocumentPageEntry) = JsonSerializer.Deserialize(Of List(Of DocumentPageEntry))(NormalizeArrayJson(DocumentPageHistory))
            If list IsNot Nothing Then
                list.RemoveAll(Function(item) item Is Nothing OrElse String.IsNullOrWhiteSpace(item.Path) OrElse item.PageNumber < 1 OrElse item.Length < 0)
                If list.Count > 200 Then list.RemoveRange(0, list.Count - 200)
                Return list
            End If
        Catch
        End Try
        Return New List(Of DocumentPageEntry)()
    End Function

    Public Shared Function FromJson(json As String) As ModernViewerPreferences
        Dim document As SettingsExport = JsonSerializer.Deserialize(Of SettingsExport)(json)
        If document Is Nothing OrElse document.SchemaVersion <> 1 OrElse document.Preferences Is Nothing Then
            Throw New FormatException("Unsupported settings document.")
        End If
        Dim p As ModernViewerPreferences = document.Preferences
        If p Is Nothing Then Throw New FormatException("Settings document is empty.")
        p.Normalize()
        Return p
    End Function

    ''' <summary>The scalar preferences, which is every property the profile stores. The
    ''' nested entry classes are not properties of this type, so nothing else gets in.</summary>
    Public Shared Function WritableProperties() As IEnumerable(Of Reflection.PropertyInfo)
        Return GetType(ModernViewerPreferences).
            GetProperties(Reflection.BindingFlags.Public Or Reflection.BindingFlags.Instance).
            Where(Function(prop) prop.CanRead AndAlso prop.CanWrite)
    End Function

    ''' <summary>
    ''' Takes every stored value of <paramref name="source"/> into THIS instance.
    '''
    ''' An import goes through here instead of swapping the object: every row of the
    ''' settings window holds a closure over the instance it was built from, and after a
    ''' swap each of them wrote into an orphan - neither applied nor saved for the rest of
    ''' the session (053 F-02). Keeping the identity keeps every closure live.
    ''' </summary>
    Public Sub CopyFrom(source As ModernViewerPreferences)
        If source Is Nothing Then Throw New ArgumentNullException(NameOf(source))
        If ReferenceEquals(source, Me) Then Return
        For Each prop As Reflection.PropertyInfo In WritableProperties()
            prop.SetValue(Me, prop.GetValue(source))
        Next
    End Sub

    ''' <summary>
    ''' Writes AllowNewWindows straight away, the way its checkbox does. Save() re-reads the
    ''' key before writing it (so an older in-memory value cannot undo a secondary process's
    ''' choice), which means an import that goes only through Save() could never change it
    ''' although the import summary listed it (053 F-18).
    ''' </summary>
    Public Sub PersistAllowNewWindows()
        WriteBool("AllowNewWindows", AllowNewWindows)
    End Sub

    Public Sub Normalize()
        NameCollisionPolicy = Choice(NameCollisionPolicy, "ask", "ask", "skip", "rename", "replace")
        AfterFileOperation = Choice(AfterFileOperation, "next", "next", "stay", "closeIfEmpty")
        InterfaceScaleRelativePercent = UiScale.NormalizePercent(InterfaceScaleRelativePercent)
        InterfaceScalePercent = Clamp(InterfaceScalePercent, 0, 150)
        If InterfaceScalePercent <> 0 AndAlso InterfaceScalePercent < 90 Then InterfaceScalePercent = 90
        NewImageScaleMode = Choice(NewImageScaleMode, "fit", "fit", "actual", "perFolder")
        RecipientsOverlayPosition = Choice(RecipientsOverlayPosition, "topLeft", "topLeft", "topRight", "bottomLeft", "bottomRight")
        ChromeSize = Choice(ChromeSize, "medium", "medium", "small", "large", "adaptive", "touch")
        ChromeOpacityPercent = Clamp(ChromeOpacityPercent, 0, 100)
        ChromeCornerRadius = Clamp(ChromeCornerRadius, 0, 16)
        RecipientsOverlayWidth = Clamp(RecipientsOverlayWidth, 180, 520)
        RecipientsOverlayFontSize = Clamp(RecipientsOverlayFontSize, 9, 18)
        RecipientsOverlayOpacity = Clamp(RecipientsOverlayOpacity, 40, 100)
        RecipientsOverlayVisibleRows = Clamp(RecipientsOverlayVisibleRows, 3, 11)
        SlideshowRandomOrder = Choice(SlideshowRandomOrder, "natural", "natural", "random", "shuffleCycle")
        SlideshowUiMode = Choice(SlideshowUiMode, "none", "none", "toolbar", "toolbarAndStatus")
        VideoControlsHideDelaySec = Clamp(VideoControlsHideDelaySec, 1, 15)
        VideoSingleClickAction = Choice(VideoSingleClickAction, "pauseResume", "pauseResume", "nextFile")
        VideoEndAction = Choice(VideoEndAction, "stay", "stay", "nextFile", "repeat")
        SeekShortSec = Clamp(SeekShortSec, 1, 600)
        SeekMediumSec = Clamp(SeekMediumSec, 1, 600)
        SeekLongSec = Clamp(SeekLongSec, 1, 600)
        HideCursorAfterSec = Clamp(HideCursorAfterSec, 0, 30)

        AudioEndAction = Choice(AudioEndAction, "next", "next", "stay", "repeat")
        SleepTimerMinutes = Clamp(SleepTimerMinutes, 0, 180)

        StartupOpenMode = Choice(StartupOpenMode, "home", "home", "lastFolder", "lastFile")
        RecentFilesLimit = Clamp(RecentFilesLimit, 0, 200)
        RecentFoldersLimit = Clamp(RecentFoldersLimit, 0, 200)
        OcrDiskCacheMaxMb = Clamp(OcrDiskCacheMaxMb, 0, 1024)
        DecodeCacheMaxMb = Clamp(DecodeCacheMaxMb, 0, 8192)
        PanelViewMode = Choice(PanelViewMode, "grid", "grid", "list")
        PanelThumbnailSize = SnapPanelThumbnailSize(PanelThumbnailSize)
        PanelThumbCacheMb = Clamp(PanelThumbCacheMb, 0, 4096)
        ' The same ranges Load() applies to the registry: an imported profile used to keep
        ' these as written - a negative credential timeout never expired, a zero archive
        ' budget disabled eviction, a huge extraction ceiling could fill the disk (072 S15-1).
        ArchiveCacheMaxMb = Clamp(ArchiveCacheMaxMb, ArchiveCacheMaxMbMin, ArchiveCacheMaxMbMax)
        ArchiveMaxEntryMb = Clamp(ArchiveMaxEntryMb, ArchiveMaxEntryMbMin, ArchiveMaxEntryMbMax)
        ArchiveMaxEntries = Clamp(ArchiveMaxEntries, ArchiveMaxEntriesMin, ArchiveMaxEntriesMax)
        RemoteCacheMaxMb = Clamp(RemoteCacheMaxMb, RemoteCacheMaxMbMin, RemoteCacheMaxMbMax)
        SecureRememberMinutes = Clamp(SecureRememberMinutes, SecureRememberMinutesMin, SecureRememberMinutesMax)
        ' "never" is the owner's explicit choice and survives; anything else unknown is the safe default.
        ConfirmDelete = Choice(ConfirmDelete, "always", "always", "permanentOnly", "never")
        TextPreviewMaxKb = Clamp(TextPreviewMaxKb, TextPreviewMaxKbMin, TextPreviewMaxKbMax)
        IncludedExtensions = NormalizeExtensionJson(IncludedExtensions)
        DocumentExtensions = NormalizeExtensionJson(DocumentExtensions)
        OtherExtensions = NormalizeExtensionJson(OtherExtensions)
        PreferredAudioLanguage = If(PreferredAudioLanguage, String.Empty)
        PreferredSubtitleLanguage = If(PreferredSubtitleLanguage, String.Empty)
        CustomHotkeysJson = NormalizeJson(CustomHotkeysJson)
        FolderZoomHistory = NormalizeArrayJson(FolderZoomHistory)
        VideoPositionHistory = NormalizeArrayJson(VideoPositionHistory)
        DocumentPageHistory = NormalizeArrayJson(DocumentPageHistory)
    End Sub

    Private Shared Function ReadString(key As String, defaultValue As String) As String
        Return Microsoft.VisualBasic.Interaction.GetSetting(App_name, Second_App_Name, key, defaultValue)
    End Function

    Private Shared Function ReadBool(key As String, defaultValue As Boolean) As Boolean
        Return ReadString(key, If(defaultValue, "1", "0")) = "1"
    End Function

    Private Shared Function ReadInt(key As String, defaultValue As Integer, minimum As Integer, maximum As Integer) As Integer
        ' Not TryParse into the default: it zeroes its out argument on failure, and a
        ' corrupt value then came out as 0 or the minimum - no decode cache, a credential
        ' kept until exit - instead of the default (053 F-17).
        Return Utils.ParseIntSetting(ReadString(key, defaultValue.ToString(CultureInfo.InvariantCulture)), defaultValue, minimum, maximum)
    End Function

    Private Shared Function ReadChoice(key As String, defaultValue As String, ParamArray allowed As String()) As String
        Return Choice(ReadString(key, defaultValue), defaultValue, allowed)
    End Function

    ''' <summary>
    ''' The delete-confirmation setting, migrated ONCE from the profile that predates it
    ''' (017_SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md §3.7).
    '''
    ''' A profile with no key of its own is not a fresh install - it is somebody who
    ''' already decided, through the old "no confirmations" checkbox, whether deleting asks.
    ''' Reading the default over that would start asking again for a person who switched
    ''' the question off, which is exactly the sort of silent reversal a migration exists to
    ''' prevent. The new middle value is never arrived at by migration: nobody has asked for
    ''' it yet, and choosing it for them would change what their setting means.
    '''
    ''' It is written back immediately so the answer stops depending on the legacy key: from
    ''' then on the two are independent, which is what lets the old checkbox keep governing
    ''' everything else it always did.
    ''' </summary>
    Private Shared Function ReadConfirmDelete() As String
        Dim stored As String = ReadString("ConfirmDelete", "")
        If stored <> "" Then
            ' "never" was retired on 2026-09-24 (ticket 028 Ф-5) and restored by owner
            ' decision 2026-09-27: it is offered again as an explicit choice. A value the
            ' retirement already rewrote to permanentOnly stays so - it is not guessed back.
            Return Choice(stored, "always", "always", "permanentOnly", "never")
        End If

        Dim migrated As String = If(ReadString("NoRequestBeforeFileOperation", "0") = "1", "permanentOnly", "always")
        WriteString("ConfirmDelete", migrated)
        Return migrated
    End Function

    Private Shared Sub WriteString(key As String, value As String)
        Microsoft.VisualBasic.Interaction.SaveSetting(App_name, Second_App_Name, key, If(value, String.Empty))
    End Sub

    Private Shared Sub WriteBool(key As String, value As Boolean)
        WriteString(key, If(value, "1", "0"))
    End Sub

    Private Shared Function Choice(value As String, defaultValue As String, ParamArray allowed As String()) As String
        For Each optionValue As String In allowed
            If String.Equals(value, optionValue, StringComparison.OrdinalIgnoreCase) Then Return optionValue
        Next
        Return defaultValue
    End Function

    Private Shared Function Clamp(value As Integer, minimum As Integer, maximum As Integer) As Integer
        Return Math.Max(minimum, Math.Min(maximum, value))
    End Function

    ''' <summary>The four sizes a thumbnail card may have (005 PG-3).</summary>
    Public Shared ReadOnly PanelThumbnailTiers As Integer() = {96, 128, 192, 256}

    ''' <summary>Zero stays zero ("follow the legacy card size"); anything else becomes the
    ''' nearest of <see cref="PanelThumbnailTiers"/>, so a hand-edited or imported value can
    ''' never produce a card size the panel's layout was not built for.</summary>
    Public Shared Function SnapPanelThumbnailSize(value As Integer) As Integer
        If value <= 0 Then Return 0
        Dim best As Integer = PanelThumbnailTiers(0)
        For Each tier As Integer In PanelThumbnailTiers
            If Math.Abs(tier - value) < Math.Abs(best - value) Then best = tier
        Next
        Return best
    End Function

    Private Shared Function NormalizeJson(value As String) As String
        Try
            Using doc As JsonDocument = JsonDocument.Parse(If(value, "{}"))
                If doc.RootElement.ValueKind = JsonValueKind.Object Then Return doc.RootElement.GetRawText()
            End Using
        Catch
        End Try
        Return "{}"
    End Function

    Private Shared Function NormalizeArrayJson(value As String) As String
        Try
            Using doc As JsonDocument = JsonDocument.Parse(If(value, "[]"))
                If doc.RootElement.ValueKind = JsonValueKind.Array Then Return doc.RootElement.GetRawText()
            End Using
        Catch
        End Try
        Return "[]"
    End Function

    Private Function ReadFolderZooms() As List(Of FolderZoomEntry)
        Try
            Dim entries As List(Of FolderZoomEntry) = JsonSerializer.Deserialize(Of List(Of FolderZoomEntry))(NormalizeArrayJson(FolderZoomHistory))
            If entries IsNot Nothing Then Return entries.Where(Function(item) item IsNot Nothing AndAlso Not String.IsNullOrEmpty(item.Path)).ToList()
        Catch
        End Try
        Return New List(Of FolderZoomEntry)()
    End Function

    Private Function ReadVideoPositions() As List(Of VideoPositionEntry)
        Try
            Dim entries As List(Of VideoPositionEntry) = JsonSerializer.Deserialize(Of List(Of VideoPositionEntry))(NormalizeArrayJson(VideoPositionHistory))
            If entries IsNot Nothing Then Return entries.Where(Function(item) item IsNot Nothing AndAlso Not String.IsNullOrEmpty(item.Path)).ToList()
        Catch
        End Try
        Return New List(Of VideoPositionEntry)()
    End Function

    ''' <summary>Empty is the deliberate all-supported-formats sentinel.  Older
    ''' semicolon-separated values are accepted once and rewritten as the documented
    ''' lowercase JSON array.</summary>
    Private Shared Function NormalizeExtensionJson(value As String) As String
        If String.IsNullOrWhiteSpace(value) Then Return "[]"
        Dim values As New List(Of String)()
        Try
            Using doc As JsonDocument = JsonDocument.Parse(value)
                If doc.RootElement.ValueKind = JsonValueKind.Array Then
                    For Each item As JsonElement In doc.RootElement.EnumerateArray()
                        If item.ValueKind = JsonValueKind.String Then values.Add(item.GetString())
                    Next
                End If
            End Using
        Catch
            values.AddRange(value.Split(New Char() {";"c, " "c, ","c, ChrW(9), ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries))
        End Try
        Dim normalized As New List(Of String)()
        For Each item As String In values
            Dim extension As String = If(item, String.Empty).Trim().ToLowerInvariant()
            If extension.Length = 0 Then Continue For
            If Not extension.StartsWith(".", StringComparison.Ordinal) Then extension = "." & extension
            If extension.Length < 2 OrElse extension.Substring(1).Any(Function(c) Not Char.IsLetterOrDigit(c) AndAlso c <> "-"c AndAlso c <> "_"c) Then Continue For
            If Not normalized.Contains(extension) Then normalized.Add(extension)
        Next
        Return JsonSerializer.Serialize(normalized)
    End Function

    Private NotInheritable Class SettingsExport
        Public Property SchemaVersion As Integer
        Public Property Preferences As ModernViewerPreferences
    End Class
End Class
#End If
