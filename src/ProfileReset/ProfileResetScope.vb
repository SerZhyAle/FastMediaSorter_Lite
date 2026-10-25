#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Collections.ObjectModel

''' <summary>
''' Specification 169 §4: Scope of the profile reset.
'''
''' Rule of ownership: a value is reset if and only if the viewer alone owns it and it is
''' configuration or remembered window state. A value shared with another program, a secret,
''' a list the user built, and anything that records history or a consent is kept.
''' The reset deletes ONLY names listed in ResetNames.
''' </summary>
Public NotInheritable Class ProfileResetScope

    Private Sub New()
    End Sub

    ''' <summary>4.1: Names reset to defaults (deleted from HKCU\Software\VB and VBA Program Settings\SZA\FastMediaSorter).</summary>
    Private Shared ReadOnly _resetNames As New ReadOnlyCollection(Of String)(New List(Of String) From {
        "chkTopMost",
        "ShowRecipientsOverlay",
        "WheelZooms",
        "MoveOn0",
        "MoveOn1",
        "MoveOn2",
        "MoveOn3",
        "MoveOn4",
        "MoveOn5",
        "MoveOn6",
        "MoveOn7",
        "MoveOn8",
        "MoveOn9",
        "CopyMode",
        "isPerspective",
        "DynamicPerspective",
        "AnimatedPerspective",
        "AnimatedPerspectiveSpeed",
        "TableOpened",
        "SortDir",
        "color_scheme",
        "ShowPictureSizes",
        "ShowFileSizes",
        "ShowFileDates",
        "SlideshowIntervalSec",
        "SlideshowIntervalMs",
        "IsVideoLoop",
        "ExifAutoRotate",
        "HighQualityScaling",
        "ShowInfoOverlay",
        "NoRequestBeforeFileOperation",
        "Picture_Box_Width_At_Panel",
        "Picture_Box_Height_At_Panel",
        "VideoVolume",
        "VideoMuted",
        "UseIndependentThreadForOperationsWithFiles",
        "FirstRun",
        "WelcomeDone",
        "WelcomePending",
        "AppTop",
        "AppLeft",
        "AppWidth",
        "AppHeight",
        "AppWindowState",
        "AppSuperFullScreen",
        "PicturePanelTop",
        "PicturePanelLeft",
        "PicturePanelWidth",
        "PicturePanelHeight",
        "PicturePanelDpi",
        "EditorTop",
        "EditorLeft",
        "EditorWidth",
        "EditorHeight",
        "EditorDpi",
        "SettingsWindowX",
        "SettingsWindowY",
        "SettingsWindowWidth",
        "SettingsWindowHeight",
        "ModernSettingsContext",
        "NameCollisionPolicy",
        "AfterFileOperation",
        "AdvanceAfterCopy",
        "RevealStatusOnFileOperation",
        "AutoAcceptLiveCast",
        "DeleteToRecycleBin",
        "ConfirmDelete",
        "CreateMissingDestination",
        "IncludeSubfolders",
        "IncludedExtensions",
        "SortOtherFiles",
        "DocumentExtensions",
        "OtherExtensions",
        "InterfaceScaleRelativePercent",
        "InterfaceScalePercent",
        "NewImageScaleMode",
        "ReduceMotion",
        "ModernChrome",
        "ChromeOpacityPercent",
        "ChromeCornerRadius",
        "ChromePinned",
        "ChromeSize",
        "ChromeRow1Pinned",
        "ChromeRow2Pinned",
        "ChromeStatusPinned",
        "RecipientsOverlayPinned",
        "ChromeWindowControlsPinned",
        "RecipientsOverlayPosition",
        "RecipientsOverlayHasCustomPosition",
        "RecipientsOverlayCustomX",
        "RecipientsOverlayCustomY",
        "RecipientsOverlayWidth",
        "RecipientsOverlayFontSize",
        "RecipientsOverlayOpacity",
        "RecipientsOverlayVisibleRows",
        "SlideshowRandomOrder",
        "StopSlideshowOnManualNavigation",
        "SlideshowUiMode",
        "VideoAutoplay",
        "IncludeVideoInNavigation",
        "RememberVideoPosition",
        "VideoControlsHideDelaySec",
        "ShowVideoControlsWhenPaused",
        "VideoSingleClickAction",
        "VideoEndAction",
        "SeekShortSec",
        "SeekMediumSec",
        "SeekLongSec",
        "HideCursorAfterSec",
        "PreferredAudioLanguage",
        "PreferredSubtitleLanguage",
        "AudioEndAction",
        "IncludeAudioInNavigation",
        "IncludeDocumentInNavigation",
        "AudioControlsAlwaysVisible",
        "AudioVisualiser",
        "SleepTimerMinutes",
        "StartupOpenMode",
        "ResumeLastPlayback",
        "AllowNewWindows",
        "RecentFilesLimit",
        "RecentFoldersLimit",
        "OcrDiskCacheMaxMb",
        "DecodeCacheMaxMb",
        "AnimatedViaVideo",
        "PanelViewMode",
        "PanelThumbnailSize",
        "PanelThumbCacheMb",
        "ArchiveCacheMaxMb",
        "ArchiveMaxEntryMb",
        "ArchiveMaxEntries",
        "RemoteCacheMaxMb",
        "SecureIncludeInNavigation",
        "SecureRememberMinutes",
        "SecureShowTrueName",
        "TextPreviewMaxKb",
        "CustomHotkeys",
        "OcrEnabled",
        "OcrAutoMode",
        "OcrEngine",
        "OcrSourceLang",
        "TranslateProvider",
        "TranslateTargetLang",
        "TranslateEndpoint",
        "OverlayVisible",
        "OverlayOpacity",
        "OcrDiskCache",
        "OllamaModel",
        "OcrModelQuality",
        "OcrPageMode",
        "FileManagerViewMode",
        "FileManagerContentMode",
        "FileManagerShowHiddenFiles",
        "FileManagerNonMediaOpenAction",
        "FileManagerShowLocalPlaces",
        "FileManagerShowResourcePlaces",
        "FileManagerShowDevicePlaces",
        "FileManagerRememberSort",
        "FileManagerPlaceSorts"
    })

    ''' <summary>4.2: Explicitly kept names (never reset, never backed up).</summary>
    Private Shared ReadOnly _keptNames As New ReadOnlyCollection(Of String)(New List(Of String) From {
        "UiLanguage",
        "Is_" & "Russian_Language",
        "UiTheme",
        "TranslateApiKey",
        "ResourceSecrets",
        "Resources",
        "Resources.bak",
        "ImageFolder",
        "LastCounter",
        "LastPlayedFile",
        "RecentFolders",
        "RecentMediaFiles",
        "FolderZoomHistory",
        "VideoPositionHistory",
        "DocumentPageHistory",
        "RunsCount",
        "mediaViewedCount",
        "UserAlreadyAskedForAssociations",
        "AnimatedViaVideoFfmpegAsked"
    })

    ''' <summary>4.3: Transient names (operation marker).</summary>
    Private Shared ReadOnly _transientNames As New ReadOnlyCollection(Of String)(New List(Of String) From {
        "ProfileResetRequested"
    })

    Private Shared ReadOnly _resetSet As New HashSet(Of String)(_resetNames, StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly _keptSet As New HashSet(Of String)(_keptNames, StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly _transientSet As New HashSet(Of String)(_transientNames, StringComparer.OrdinalIgnoreCase)

    Public Shared ReadOnly Property ResetNames As IReadOnlyCollection(Of String)
        Get
            Return _resetNames
        End Get
    End Property

    Public Shared ReadOnly Property KeptNames As IReadOnlyCollection(Of String)
        Get
            Return _keptNames
        End Get
    End Property

    Public Shared ReadOnly Property TransientNames As IReadOnlyCollection(Of String)
        Get
            Return _transientNames
        End Get
    End Property

    Public Shared Function IsResetName(name As String) As Boolean
        If String.IsNullOrEmpty(name) Then Return False
        Return _resetSet.Contains(name)
    End Function

    Public Shared Function IsKeptName(name As String) As Boolean
        If String.IsNullOrEmpty(name) Then Return False
        If _keptSet.Contains(name) Then Return True
        ' Every Share_* value belongs to the Share Manager
        If name.StartsWith("Share_", StringComparison.OrdinalIgnoreCase) Then Return True
        Return False
    End Function

    Public Shared Function IsTransientName(name As String) As Boolean
        If String.IsNullOrEmpty(name) Then Return False
        Return _transientSet.Contains(name)
    End Function

    Public Shared Function Classify(name As String) As String
        If IsResetName(name) Then Return "Reset"
        If IsKeptName(name) Then Return "Kept"
        If IsTransientName(name) Then Return "Transient"
        Return "Unclassified"
    End Function

End Class
#End If
