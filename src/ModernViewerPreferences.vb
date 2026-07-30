#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Globalization
Imports System.Text.Json

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
    Public Property RecentFilesLimit As Integer = 50
    Public Property RecentFoldersLimit As Integer = 100
    Public Property OcrDiskCacheMaxMb As Integer = 250
    Public Property CustomHotkeysJson As String = "{}"

    Public Shared Function Load() As ModernViewerPreferences
        Dim p As New ModernViewerPreferences()
        p.NameCollisionPolicy = ReadChoice("NameCollisionPolicy", p.NameCollisionPolicy, "ask", "skip", "rename", "replace")
        p.AfterFileOperation = ReadChoice("AfterFileOperation", p.AfterFileOperation, "next", "stay", "closeIfEmpty")
        p.AdvanceAfterCopy = ReadBool("AdvanceAfterCopy", p.AdvanceAfterCopy)
        p.IncludeSubfolders = ReadBool("IncludeSubfolders", p.IncludeSubfolders)
        p.IncludedExtensions = ReadString("IncludedExtensions", p.IncludedExtensions)
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
        p.RecentFilesLimit = ReadInt("RecentFilesLimit", p.RecentFilesLimit, 0, 200)
        p.RecentFoldersLimit = ReadInt("RecentFoldersLimit", p.RecentFoldersLimit, 0, 200)
        p.OcrDiskCacheMaxMb = ReadInt("OcrDiskCacheMaxMb", p.OcrDiskCacheMaxMb, 0, 1024)
        p.CustomHotkeysJson = NormalizeJson(ReadString("CustomHotkeys", p.CustomHotkeysJson))
        Return p
    End Function

    Public Sub Save()
        WriteString("NameCollisionPolicy", NameCollisionPolicy)
        WriteString("AfterFileOperation", AfterFileOperation)
        WriteBool("AdvanceAfterCopy", AdvanceAfterCopy)
        WriteBool("IncludeSubfolders", IncludeSubfolders)
        WriteString("IncludedExtensions", IncludedExtensions)
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
        WriteString("RecentFilesLimit", RecentFilesLimit.ToString(CultureInfo.InvariantCulture))
        WriteString("RecentFoldersLimit", RecentFoldersLimit.ToString(CultureInfo.InvariantCulture))
        WriteString("OcrDiskCacheMaxMb", OcrDiskCacheMaxMb.ToString(CultureInfo.InvariantCulture))
        WriteString("CustomHotkeys", NormalizeJson(CustomHotkeysJson))
    End Sub

    Public Function ExportJson() As String
        Return JsonSerializer.Serialize(Me, New JsonSerializerOptions With {.WriteIndented = True})
    End Function

    Public Shared Function FromJson(json As String) As ModernViewerPreferences
        Dim p As ModernViewerPreferences = JsonSerializer.Deserialize(Of ModernViewerPreferences)(json)
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
        IncludedExtensions = If(IncludedExtensions, String.Empty)
        PreferredAudioLanguage = If(PreferredAudioLanguage, String.Empty)
        PreferredSubtitleLanguage = If(PreferredSubtitleLanguage, String.Empty)
        CustomHotkeysJson = NormalizeJson(CustomHotkeysJson)
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
End Class
#End If
