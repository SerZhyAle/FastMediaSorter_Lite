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

    Public Property NameCollisionPolicy As String = "ask"
    Public Property AfterFileOperation As String = "next"
    ''' <summary>Show the next file once a copy has been queued. On by default - that is
    ''' the fast sorting run the number keys are for; off keeps the same file on screen,
    ''' which is what filing one picture into several folders wants
    ''' (SPECIFICATION_COPY_ACTIONS_REWORK.md §3.2).</summary>
    Public Property AdvanceAfterCopy As Boolean = True
    ''' <summary>Whether DEL aims for the Recycle Bin at all
    ''' (SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md §3.9). Off means every deletion is
    ''' permanent and SAYS so - the same wording Shift+DEL produces, because it is the same
    ''' thing: the user asked for it.</summary>
    Public Property DeleteToRecycleBin As Boolean = True

    ''' <summary>always | permanentOnly | never. The middle value is the one §0.4 of that
    ''' specification says is unreachable today and is what a triage session wants: a
    ''' recycled deletion goes straight through, a PERMANENT one still stops and asks.</summary>
    Public Property ConfirmDelete As String = "always"

    Public Property IncludeSubfolders As Boolean
    Public Property IncludedExtensions As String = ""
    Public Property InterfaceScalePercent As Integer
    Public Property NewImageScaleMode As String = "fit"
    Public Property ReduceMotion As Boolean

    Public Property RecipientsOverlayPosition As String = "topLeft"
    Public Property RecipientsOverlayWidth As Integer = 280
    Public Property RecipientsOverlayFontSize As Integer = 11
    Public Property RecipientsOverlayOpacity As Integer = 88
    Public Property RecipientsOverlayVisibleRows As Integer = 10

    Public Property SlideshowRandomOrder As String = "natural"
    Public Property StopSlideshowOnManualNavigation As Boolean = True
    Public Property SlideshowUiMode As String = "none"

    Public Property VideoAutoplay As Boolean = True
    Public Property RememberVideoPosition As Boolean = True
    Public Property VideoControlsHideDelaySec As Integer = 3
    Public Property ShowVideoControlsWhenPaused As Boolean = True
    Public Property VideoSingleClickAction As String = "pauseResume"
    Public Property VideoEndAction As String = "stay"
    Public Property PreferredAudioLanguage As String = ""
    Public Property PreferredSubtitleLanguage As String = ""

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
    Public Property CustomHotkeysJson As String = "{}"
    ''' <summary>Bounded JSON stores. They are intentionally profile keys rather than
    ''' binary settings so an old profile remains readable and portable.</summary>
    Public Property FolderZoomHistory As String = "[]"
    Public Property VideoPositionHistory As String = "[]"

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
        p.IncludeSubfolders = ReadBool("IncludeSubfolders", p.IncludeSubfolders)
        p.IncludedExtensions = NormalizeExtensionJson(ReadString("IncludedExtensions", p.IncludedExtensions))
        p.InterfaceScalePercent = ReadInt("InterfaceScalePercent", p.InterfaceScalePercent, 0, 150)
        If p.InterfaceScalePercent <> 0 AndAlso p.InterfaceScalePercent < 90 Then p.InterfaceScalePercent = 90
        p.NewImageScaleMode = ReadChoice("NewImageScaleMode", p.NewImageScaleMode, "fit", "actual", "perFolder")
        p.ReduceMotion = ReadBool("ReduceMotion", p.ReduceMotion)

        p.RecipientsOverlayPosition = ReadChoice("RecipientsOverlayPosition", p.RecipientsOverlayPosition, "topLeft", "topRight", "bottomLeft", "bottomRight")
        p.RecipientsOverlayWidth = ReadInt("RecipientsOverlayWidth", p.RecipientsOverlayWidth, 180, 520)
        p.RecipientsOverlayFontSize = ReadInt("RecipientsOverlayFontSize", p.RecipientsOverlayFontSize, 9, 18)
        p.RecipientsOverlayOpacity = ReadInt("RecipientsOverlayOpacity", p.RecipientsOverlayOpacity, 40, 100)
        p.RecipientsOverlayVisibleRows = ReadInt("RecipientsOverlayVisibleRows", p.RecipientsOverlayVisibleRows, 3, 11)

        p.SlideshowRandomOrder = ReadChoice("SlideshowRandomOrder", p.SlideshowRandomOrder, "natural", "random", "shuffleCycle")
        p.StopSlideshowOnManualNavigation = ReadBool("StopSlideshowOnManualNavigation", p.StopSlideshowOnManualNavigation)
        p.SlideshowUiMode = ReadChoice("SlideshowUiMode", p.SlideshowUiMode, "none", "toolbar", "toolbarAndStatus")

        p.VideoAutoplay = ReadBool("VideoAutoplay", p.VideoAutoplay)
        p.RememberVideoPosition = ReadBool("RememberVideoPosition", p.RememberVideoPosition)
        p.VideoControlsHideDelaySec = ReadInt("VideoControlsHideDelaySec", p.VideoControlsHideDelaySec, 1, 15)
        p.ShowVideoControlsWhenPaused = ReadBool("ShowVideoControlsWhenPaused", p.ShowVideoControlsWhenPaused)
        p.VideoSingleClickAction = ReadChoice("VideoSingleClickAction", p.VideoSingleClickAction, "pauseResume", "nextFile")
        p.VideoEndAction = ReadChoice("VideoEndAction", p.VideoEndAction, "stay", "nextFile", "repeat")
        p.PreferredAudioLanguage = ReadString("PreferredAudioLanguage", p.PreferredAudioLanguage)
        p.PreferredSubtitleLanguage = ReadString("PreferredSubtitleLanguage", p.PreferredSubtitleLanguage)

        p.StartupOpenMode = ReadChoice("StartupOpenMode", p.StartupOpenMode, "home", "lastFolder", "lastFile")
        p.ResumeLastPlayback = ReadBool("ResumeLastPlayback", p.ResumeLastPlayback)
        p.AllowNewWindows = ReadBool("AllowNewWindows", p.AllowNewWindows)
        p.RecentFilesLimit = ReadInt("RecentFilesLimit", p.RecentFilesLimit, 0, 200)
        p.RecentFoldersLimit = ReadInt("RecentFoldersLimit", p.RecentFoldersLimit, 0, 200)
        p.OcrDiskCacheMaxMb = ReadInt("OcrDiskCacheMaxMb", p.OcrDiskCacheMaxMb, 0, 1024)
        p.CustomHotkeysJson = NormalizeJson(ReadString("CustomHotkeys", p.CustomHotkeysJson))
        p.FolderZoomHistory = NormalizeArrayJson(ReadString("FolderZoomHistory", p.FolderZoomHistory))
        p.VideoPositionHistory = NormalizeArrayJson(ReadString("VideoPositionHistory", p.VideoPositionHistory))
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
        WriteBool("IncludeSubfolders", IncludeSubfolders)
        WriteString("IncludedExtensions", NormalizeExtensionJson(IncludedExtensions))
        WriteString("InterfaceScalePercent", InterfaceScalePercent.ToString(CultureInfo.InvariantCulture))
        WriteString("NewImageScaleMode", NewImageScaleMode)
        WriteBool("ReduceMotion", ReduceMotion)
        WriteString("RecipientsOverlayPosition", RecipientsOverlayPosition)
        WriteString("RecipientsOverlayWidth", RecipientsOverlayWidth.ToString(CultureInfo.InvariantCulture))
        WriteString("RecipientsOverlayFontSize", RecipientsOverlayFontSize.ToString(CultureInfo.InvariantCulture))
        WriteString("RecipientsOverlayOpacity", RecipientsOverlayOpacity.ToString(CultureInfo.InvariantCulture))
        WriteString("RecipientsOverlayVisibleRows", RecipientsOverlayVisibleRows.ToString(CultureInfo.InvariantCulture))
        WriteString("SlideshowRandomOrder", SlideshowRandomOrder)
        WriteBool("StopSlideshowOnManualNavigation", StopSlideshowOnManualNavigation)
        WriteString("SlideshowUiMode", SlideshowUiMode)
        WriteBool("VideoAutoplay", VideoAutoplay)
        WriteBool("RememberVideoPosition", RememberVideoPosition)
        WriteString("VideoControlsHideDelaySec", VideoControlsHideDelaySec.ToString(CultureInfo.InvariantCulture))
        WriteBool("ShowVideoControlsWhenPaused", ShowVideoControlsWhenPaused)
        WriteString("VideoSingleClickAction", VideoSingleClickAction)
        WriteString("VideoEndAction", VideoEndAction)
        WriteString("PreferredAudioLanguage", PreferredAudioLanguage)
        WriteString("PreferredSubtitleLanguage", PreferredSubtitleLanguage)
        WriteString("StartupOpenMode", StartupOpenMode)
        WriteBool("ResumeLastPlayback", ResumeLastPlayback)
        ' Keep a primary process with an older in-memory value from resurrecting a
        ' choice that a secondary process just wrote for subsequent launches.
        AllowNewWindows = ReadBool("AllowNewWindows", AllowNewWindows)
        WriteBool("AllowNewWindows", AllowNewWindows)
        WriteString("RecentFilesLimit", RecentFilesLimit.ToString(CultureInfo.InvariantCulture))
        WriteString("RecentFoldersLimit", RecentFoldersLimit.ToString(CultureInfo.InvariantCulture))
        WriteString("OcrDiskCacheMaxMb", OcrDiskCacheMaxMb.ToString(CultureInfo.InvariantCulture))
        WriteString("CustomHotkeys", NormalizeJson(CustomHotkeysJson))
        WriteString("FolderZoomHistory", NormalizeArrayJson(FolderZoomHistory))
        WriteString("VideoPositionHistory", NormalizeArrayJson(VideoPositionHistory))
    End Sub

    Public Function ExportJson(Optional includePersonalData As Boolean = False) As String
        Normalize()
        Dim exported As ModernViewerPreferences = JsonSerializer.Deserialize(Of ModernViewerPreferences)(JsonSerializer.Serialize(Me))
        If Not includePersonalData Then
            ' Paths and resume positions are personal data. The normal export is safe to
            ' share; an explicit future UI opt-in can use includePersonalData:=True.
            exported.FolderZoomHistory = "[]"
            exported.VideoPositionHistory = "[]"
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
        If entry.Position <= 0.0F OrElse entry.Position >= 1.0F Then Return -1.0F
        entries.Remove(entry)
        entries.Add(entry) ' read is use: maintain the LRU order
        VideoPositionHistory = JsonSerializer.Serialize(entries)
        Return entry.Position
    End Function

    Public Sub StoreVideoPosition(path As String, lastWriteUtcTicks As Long, length As Long, position As Single)
        If Not RememberVideoPosition OrElse String.IsNullOrEmpty(path) OrElse position <= 0.0F OrElse position >= 1.0F Then Return
        Dim entries As List(Of VideoPositionEntry) = ReadVideoPositions()
        entries.RemoveAll(Function(item) String.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
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

    Public Sub Normalize()
        NameCollisionPolicy = Choice(NameCollisionPolicy, "ask", "ask", "skip", "rename", "replace")
        AfterFileOperation = Choice(AfterFileOperation, "next", "next", "stay", "closeIfEmpty")
        InterfaceScalePercent = Clamp(InterfaceScalePercent, 0, 150)
        If InterfaceScalePercent <> 0 AndAlso InterfaceScalePercent < 90 Then InterfaceScalePercent = 90
        NewImageScaleMode = Choice(NewImageScaleMode, "fit", "fit", "actual", "perFolder")
        RecipientsOverlayPosition = Choice(RecipientsOverlayPosition, "topLeft", "topLeft", "topRight", "bottomLeft", "bottomRight")
        RecipientsOverlayWidth = Clamp(RecipientsOverlayWidth, 180, 520)
        RecipientsOverlayFontSize = Clamp(RecipientsOverlayFontSize, 9, 18)
        RecipientsOverlayOpacity = Clamp(RecipientsOverlayOpacity, 40, 100)
        RecipientsOverlayVisibleRows = Clamp(RecipientsOverlayVisibleRows, 3, 11)
        SlideshowRandomOrder = Choice(SlideshowRandomOrder, "natural", "natural", "random", "shuffleCycle")
        SlideshowUiMode = Choice(SlideshowUiMode, "none", "none", "toolbar", "toolbarAndStatus")
        VideoControlsHideDelaySec = Clamp(VideoControlsHideDelaySec, 1, 15)
        VideoSingleClickAction = Choice(VideoSingleClickAction, "pauseResume", "pauseResume", "nextFile")
        VideoEndAction = Choice(VideoEndAction, "stay", "stay", "nextFile", "repeat")
        StartupOpenMode = Choice(StartupOpenMode, "home", "home", "lastFolder", "lastFile")
        RecentFilesLimit = Clamp(RecentFilesLimit, 0, 200)
        RecentFoldersLimit = Clamp(RecentFoldersLimit, 0, 200)
        OcrDiskCacheMaxMb = Clamp(OcrDiskCacheMaxMb, 0, 1024)
        IncludedExtensions = NormalizeExtensionJson(IncludedExtensions)
        PreferredAudioLanguage = If(PreferredAudioLanguage, String.Empty)
        PreferredSubtitleLanguage = If(PreferredSubtitleLanguage, String.Empty)
        CustomHotkeysJson = NormalizeJson(CustomHotkeysJson)
        FolderZoomHistory = NormalizeArrayJson(FolderZoomHistory)
        VideoPositionHistory = NormalizeArrayJson(VideoPositionHistory)
    End Sub

    Private Shared Function ReadString(key As String, defaultValue As String) As String
        Return Microsoft.VisualBasic.Interaction.GetSetting(App_name, Second_App_Name, key, defaultValue)
    End Function

    Private Shared Function ReadBool(key As String, defaultValue As Boolean) As Boolean
        Return ReadString(key, If(defaultValue, "1", "0")) = "1"
    End Function

    Private Shared Function ReadInt(key As String, defaultValue As Integer, minimum As Integer, maximum As Integer) As Integer
        Dim value As Integer = defaultValue
        Integer.TryParse(ReadString(key, defaultValue.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer, CultureInfo.InvariantCulture, value)
        Return Clamp(value, minimum, maximum)
    End Function

    Private Shared Function ReadChoice(key As String, defaultValue As String, ParamArray allowed As String()) As String
        Return Choice(ReadString(key, defaultValue), defaultValue, allowed)
    End Function

    ''' <summary>
    ''' The delete-confirmation setting, migrated ONCE from the profile that predates it
    ''' (SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md §3.7).
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
        If stored <> "" Then Return Choice(stored, "always", "always", "permanentOnly", "never")

        Dim migrated As String = If(ReadString("NoRequestBeforeFileOperation", "0") = "1", "never", "always")
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
            values.AddRange(value.Split(";"c))
        End Try
        Dim normalized As New List(Of String)()
        For Each item As String In values
            Dim extension As String = If(item, String.Empty).Trim().ToLowerInvariant()
            If extension.Length = 0 Then Continue For
            If Not extension.StartsWith(".", StringComparison.Ordinal) Then extension = "." & extension
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
