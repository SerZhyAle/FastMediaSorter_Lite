Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports Xunit

#If Not NETFRAMEWORK Then
''' <summary>
''' Ticket 103 (audit 057.S15-1): an imported profile used to keep the archive, remote
''' cache and credential limits exactly as written, because Normalize() did not cover them
''' while the registry loader bounded the very same values. Pure tests - no registry.
''' </summary>
Public Class ImportedSettingsLimitsTests

    Private Class Limit
        Public ReadOnly Name As String
        Public ReadOnly Minimum As Integer
        Public ReadOnly Maximum As Integer
        Public ReadOnly Getter As Func(Of ModernViewerPreferences, Integer)

        Public Sub New(name As String, minimum As Integer, maximum As Integer, getter As Func(Of ModernViewerPreferences, Integer))
            Me.Name = name
            Me.Minimum = minimum
            Me.Maximum = maximum
            Me.Getter = getter
        End Sub
    End Class

    Private Shared Function Limits() As List(Of Limit)
        Return New List(Of Limit) From {
            New Limit("ArchiveCacheMaxMb", ModernViewerPreferences.ArchiveCacheMaxMbMin, ModernViewerPreferences.ArchiveCacheMaxMbMax, Function(p) p.ArchiveCacheMaxMb),
            New Limit("ArchiveMaxEntryMb", ModernViewerPreferences.ArchiveMaxEntryMbMin, ModernViewerPreferences.ArchiveMaxEntryMbMax, Function(p) p.ArchiveMaxEntryMb),
            New Limit("ArchiveMaxEntries", ModernViewerPreferences.ArchiveMaxEntriesMin, ModernViewerPreferences.ArchiveMaxEntriesMax, Function(p) p.ArchiveMaxEntries),
            New Limit("RemoteCacheMaxMb", ModernViewerPreferences.RemoteCacheMaxMbMin, ModernViewerPreferences.RemoteCacheMaxMbMax, Function(p) p.RemoteCacheMaxMb),
            New Limit("SecureRememberMinutes", ModernViewerPreferences.SecureRememberMinutesMin, ModernViewerPreferences.SecureRememberMinutesMax, Function(p) p.SecureRememberMinutes),
            New Limit("TextPreviewMaxKb", ModernViewerPreferences.TextPreviewMaxKbMin, ModernViewerPreferences.TextPreviewMaxKbMax, Function(p) p.TextPreviewMaxKb)
        }
    End Function

    Private Shared Function Probes(limit As Limit) As Integer()
        Return New Integer() {Integer.MinValue, -1, 0, limit.Minimum - 1, limit.Minimum, limit.Maximum, limit.Maximum + 1, Integer.MaxValue}
    End Function

    Friend Shared Function JsonWith(name As String, value As Integer) As String
        Return "{""SchemaVersion"":1,""Preferences"":{""" & name & """:" & value.ToString(CultureInfo.InvariantCulture) & "}}"
    End Function

    Friend Shared Function JsonWithString(name As String, value As String) As String
        Dim token As String = If(value Is Nothing, "null", """" & value & """")
        Return "{""SchemaVersion"":1,""Preferences"":{""" & name & """:" & token & "}}"
    End Function

    Private Shared Function Expected(limit As Limit, value As Integer) As Integer
        ' What the registry loader produces for the same stored text.
        Return Utils.ParseIntSetting(value.ToString(CultureInfo.InvariantCulture), limit.Minimum, limit.Minimum, limit.Maximum)
    End Function

    <Fact>
    Public Sub Normalize_bounds_every_limit_the_same_way_the_registry_loader_does()
        For Each limit As Limit In Limits()
            For Each value As Integer In Probes(limit)
                Dim p As New ModernViewerPreferences()
                p.GetType().GetProperty(limit.Name).SetValue(p, value)
                p.Normalize()
                Assert.Equal(Expected(limit, value), limit.Getter(p))
            Next
        Next
    End Sub

    <Fact>
    Public Sub FromJson_bounds_every_limit()
        For Each limit As Limit In Limits()
            For Each value As Integer In Probes(limit)
                Dim p As ModernViewerPreferences = ModernViewerPreferences.FromJson(JsonWith(limit.Name, value))
                Assert.Equal(Expected(limit, value), limit.Getter(p))
            Next
        Next
    End Sub

    <Fact>
    Public Sub CopyFrom_followed_by_Normalize_bounds_every_limit()
        For Each limit As Limit In Limits()
            For Each value As Integer In Probes(limit)
                Dim imported As New ModernViewerPreferences()
                imported.GetType().GetProperty(limit.Name).SetValue(imported, value)
                Dim live As New ModernViewerPreferences()
                live.CopyFrom(imported)
                live.Normalize()
                Assert.Equal(Expected(limit, value), limit.Getter(live))
            Next
        Next
    End Sub

    <Fact>
    Public Sub Explicit_zero_is_kept_only_where_zero_is_supported()
        Dim p As New ModernViewerPreferences() With {.SecureRememberMinutes = 0, .ArchiveCacheMaxMb = 0}
        p.Normalize()
        Assert.Equal(0, p.SecureRememberMinutes)                                   ' 0 = until the program closes
        Assert.Equal(ModernViewerPreferences.ArchiveCacheMaxMbMin, p.ArchiveCacheMaxMb) ' 0 would disable eviction
    End Sub

    <Fact>
    Public Sub Negative_credential_timeout_no_longer_means_no_expiry()
        Dim p As ModernViewerPreferences = ModernViewerPreferences.FromJson(JsonWith("SecureRememberMinutes", -5))
        Assert.Equal(0, p.SecureRememberMinutes)
        Assert.True(p.SecureRememberMinutes >= 0)
    End Sub

    <Theory>
    <InlineData("always", "always")>
    <InlineData("permanentOnly", "permanentOnly")>
    <InlineData("never", "never")>
    <InlineData("NEVER", "never")>
    <InlineData("sometimes", "always")>
    <InlineData("", "always")>
    <InlineData(Nothing, "always")>
    Public Sub ConfirmDelete_keeps_every_choice_and_falls_back_to_always(stored As String, expectedValue As String)
        Dim p As New ModernViewerPreferences() With {.ConfirmDelete = stored}
        p.Normalize()
        Assert.Equal(expectedValue, p.ConfirmDelete)
        ' The safety fallback of DeletePolicy agrees with what the window will now show.
        Assert.Equal(DeletePolicy.ShouldConfirm(expectedValue, Nothing), DeletePolicy.ShouldConfirm(p.ConfirmDelete, Nothing))
    End Sub

    <Fact>
    Public Sub FromJson_preserves_the_owners_explicit_never_choice()
        Dim p As ModernViewerPreferences = ModernViewerPreferences.FromJson(
            "{""SchemaVersion"":1,""Preferences"":{""ConfirmDelete"":""never""}}")
        Assert.Equal("never", p.ConfirmDelete)
    End Sub

    <Fact>
    Public Sub Normalize_is_idempotent_for_extreme_values()
        Dim p As New ModernViewerPreferences() With {
            .ArchiveCacheMaxMb = Integer.MaxValue, .ArchiveMaxEntryMb = Integer.MinValue,
            .ArchiveMaxEntries = -1, .RemoteCacheMaxMb = 0, .SecureRememberMinutes = Integer.MaxValue,
            .ConfirmDelete = "bogus"}
        p.Normalize()
        Dim first As String = p.ExportJson(True)
        p.Normalize()
        Assert.Equal(first, p.ExportJson(True))
    End Sub

    <Fact>
    Public Sub Export_then_import_agrees_with_the_normalized_values()
        Dim p As New ModernViewerPreferences() With {.ArchiveMaxEntries = Integer.MaxValue, .SecureRememberMinutes = -9}
        Dim reloaded As ModernViewerPreferences = ModernViewerPreferences.FromJson(p.ExportJson())
        Assert.Equal(ModernViewerPreferences.ArchiveMaxEntriesMax, reloaded.ArchiveMaxEntries)
        Assert.Equal(0, reloaded.SecureRememberMinutes)
        Assert.Equal(p.ArchiveMaxEntries, reloaded.ArchiveMaxEntries)
    End Sub

    <Fact>
    Public Sub Every_integer_and_choice_property_is_covered_by_Normalize()
        ' Guards the next property someone adds to Load() and forgets in Normalize().
        Dim covered As New HashSet(Of String)(Limits().ConvertAll(Function(l) l.Name))
        Dim p As New ModernViewerPreferences()
        For Each prop As Reflection.PropertyInfo In ModernViewerPreferences.WritableProperties()
            If prop.PropertyType <> GetType(Integer) Then Continue For
            Dim original As Object = prop.GetValue(p)
            prop.SetValue(p, Integer.MaxValue)
            p.Normalize()
            Dim after As Integer = CInt(prop.GetValue(p))
            Assert.True(after <> Integer.MaxValue, prop.Name & " keeps Integer.MaxValue through Normalize()")
            prop.SetValue(p, original)
        Next
        Assert.NotEmpty(covered)
    End Sub

    ' Friend so SettingsRegistryParityTests can reuse the same fixtures: the registry
    ' parity test and these pure tests must not keep two inventories that can drift.

    Friend Structure ChoiceSpec
        Public ReadOnly Name As String
        Public ReadOnly DefaultValue As String
        Public ReadOnly Allowed As String()
        Public Sub New(name As String, defaultValue As String, ParamArray allowed As String())
            Me.Name = name
            Me.DefaultValue = defaultValue
            Me.Allowed = allowed
        End Sub
    End Structure

    Friend Shared Function ChoiceSpecs() As List(Of ChoiceSpec)
        Return New List(Of ChoiceSpec) From {
            New ChoiceSpec("NameCollisionPolicy", "ask", "ask", "skip", "rename", "replace"),
            New ChoiceSpec("AfterFileOperation", "next", "next", "stay", "closeIfEmpty"),
            New ChoiceSpec("ConfirmDelete", "always", "always", "permanentOnly", "never"),
            New ChoiceSpec("NewImageScaleMode", "fit", "fit", "actual", "perFolder"),
            New ChoiceSpec("RecipientsOverlayPosition", "topLeft", "topLeft", "topRight", "bottomLeft", "bottomRight"),
            New ChoiceSpec("SlideshowRandomOrder", "natural", "natural", "random", "shuffleCycle"),
            New ChoiceSpec("SlideshowUiMode", "none", "none", "toolbar", "toolbarAndStatus"),
            New ChoiceSpec("VideoSingleClickAction", "pauseResume", "pauseResume", "nextFile"),
            New ChoiceSpec("VideoEndAction", "stay", "stay", "nextFile", "repeat"),
            New ChoiceSpec("AudioEndAction", "next", "next", "stay", "repeat"),
            New ChoiceSpec("StartupOpenMode", "home", "home", "lastFolder", "lastFile"),
            New ChoiceSpec("PanelViewMode", "grid", "grid", "list"),
            New ChoiceSpec("FileManagerViewMode", "grid", "grid", "list"),
            New ChoiceSpec("FileManagerContentMode", "media", "media", "documents", "all"),
            New ChoiceSpec("FileManagerNonMediaOpenAction", "default", "default", "openWith"),
            New ChoiceSpec("ChromeSize", "medium", "medium", "small", "large", "adaptive", "touch")
        }
    End Function

    <Fact>
    Public Sub All_string_choice_properties_normalize_invalid_and_cased_tokens_correctly()
        For Each spec As ChoiceSpec In ChoiceSpecs()
            Dim prop As Reflection.PropertyInfo = GetType(ModernViewerPreferences).GetProperty(spec.Name)
            Assert.NotNull(prop)

            ' Invalid values fallback to default
            Dim p As New ModernViewerPreferences()
            prop.SetValue(p, "INVALID_TOKEN_XYZ")
            p.Normalize()
            Assert.Equal(spec.DefaultValue, CStr(prop.GetValue(p)))

            ' Empty value fallback
            prop.SetValue(p, "")
            p.Normalize()
            Assert.Equal(spec.DefaultValue, CStr(prop.GetValue(p)))

            ' Each valid choice is preserved (case-insensitively mapped to canonical casing)
            For Each choice As String In spec.Allowed
                prop.SetValue(p, choice.ToUpperInvariant())
                p.Normalize()
                Assert.Equal(choice, CStr(prop.GetValue(p)))
            Next
        Next
    End Sub

    Friend Structure NumericSpec
        Public ReadOnly Name As String
        Public ReadOnly Min As Integer
        Public ReadOnly Max As Integer
        Public Sub New(name As String, min As Integer, max As Integer)
            Me.Name = name
            Me.Min = min
            Me.Max = max
        End Sub
    End Structure

    ''' <summary>Plain-clamp integers, written with literals of their own - NOT with the
    ''' production constants (103: the same constants must not be their own oracle). The
    ''' two properties with a bespoke policy (InterfaceScalePercent, PanelThumbnailSize)
    ''' are in Special_numeric_properties_follow_their_exact_snapping_rules.</summary>
    Friend Shared Function NumericSpecs() As List(Of NumericSpec)
        Return New List(Of NumericSpec) From {
            New NumericSpec("RecipientsOverlayWidth", 180, 520),
            New NumericSpec("RecipientsOverlayFontSize", 9, 18),
            New NumericSpec("ChromeOpacityPercent", 0, 100),
            New NumericSpec("ChromeCornerRadius", 0, 16),
            New NumericSpec("RecipientsOverlayOpacity", 40, 100),
            New NumericSpec("RecipientsOverlayVisibleRows", 3, 11),
            New NumericSpec("RecipientsOverlayCustomX", -32768, 32767),
            New NumericSpec("RecipientsOverlayCustomY", -32768, 32767),
            New NumericSpec("VideoControlsHideDelaySec", 1, 15),
            New NumericSpec("SeekShortSec", 1, 600),
            New NumericSpec("SeekMediumSec", 1, 600),
            New NumericSpec("SeekLongSec", 1, 600),
            New NumericSpec("HideCursorAfterSec", 0, 30),
            New NumericSpec("SleepTimerMinutes", 0, 180),
            New NumericSpec("RecentFilesLimit", 0, 200),
            New NumericSpec("RecentFoldersLimit", 0, 200),
            New NumericSpec("OcrDiskCacheMaxMb", 0, 1024),
            New NumericSpec("DecodeCacheMaxMb", 0, 8192),
            New NumericSpec("PanelThumbCacheMb", 0, 4096),
            New NumericSpec("ArchiveCacheMaxMb", 100, 16384),
            New NumericSpec("ArchiveMaxEntryMb", 16, 4096),
            New NumericSpec("ArchiveMaxEntries", 100, 100000),
            New NumericSpec("RemoteCacheMaxMb", 100, 16384),
            New NumericSpec("SecureRememberMinutes", 0, 480),
            New NumericSpec("TextPreviewMaxKb", 64, 65536)
        }
    End Function

    <Fact>
    Public Sub All_persisted_numeric_properties_clamp_to_their_valid_ranges()
        For Each spec As NumericSpec In NumericSpecs()
            Dim prop As Reflection.PropertyInfo = GetType(ModernViewerPreferences).GetProperty(spec.Name)
            Assert.NotNull(prop)

            ' Test below min
            Dim p As New ModernViewerPreferences()
            prop.SetValue(p, spec.Min - 10)
            p.Normalize()
            Assert.Equal(spec.Min, CInt(prop.GetValue(p)))

            ' Test above max
            prop.SetValue(p, spec.Max + 10)
            p.Normalize()
            Assert.Equal(spec.Max, CInt(prop.GetValue(p)))

            ' Test MinValue
            prop.SetValue(p, Integer.MinValue)
            p.Normalize()
            Assert.Equal(spec.Min, CInt(prop.GetValue(p)))

            ' Test MaxValue
            prop.SetValue(p, Integer.MaxValue)
            p.Normalize()
            Assert.Equal(spec.Max, CInt(prop.GetValue(p)))
        Next
    End Sub

    <Fact>
    Public Sub Special_numeric_properties_follow_their_exact_snapping_rules()
        ' InterfaceScalePercent: 0 is allowed, otherwise clamped between 90 and 150
        Dim p As New ModernViewerPreferences()
        p.InterfaceScalePercent = 0
        p.Normalize()
        Assert.Equal(0, p.InterfaceScalePercent)

        p.InterfaceScalePercent = 50
        p.Normalize()
        Assert.Equal(90, p.InterfaceScalePercent)

        p.InterfaceScalePercent = 200
        p.Normalize()
        Assert.Equal(150, p.InterfaceScalePercent)

        ' PanelThumbnailSize: 0 is allowed, otherwise snapped to nearest tier {96, 128, 192, 256}
        p.PanelThumbnailSize = 0
        p.Normalize()
        Assert.Equal(0, p.PanelThumbnailSize)

        p.PanelThumbnailSize = 100
        p.Normalize()
        Assert.Equal(96, p.PanelThumbnailSize)

        p.PanelThumbnailSize = 150
        p.Normalize()
        Assert.Equal(128, p.PanelThumbnailSize)

        p.PanelThumbnailSize = 220
        p.Normalize()
        Assert.Equal(192, p.PanelThumbnailSize)

        p.PanelThumbnailSize = 300
        p.Normalize()
        Assert.Equal(256, p.PanelThumbnailSize)
    End Sub

    <Fact>
    Public Sub CopyFrom_faithfully_copies_every_single_property_without_changing_identity()
        Dim source As New ModernViewerPreferences() With {
            .NameCollisionPolicy = "rename",
            .AfterFileOperation = "stay",
            .AdvanceAfterCopy = False,
            .AutoAcceptLiveCast = True,
            .DeleteToRecycleBin = False,
            .ConfirmDelete = "never",
            .CreateMissingDestination = False,
            .IncludeSubfolders = True,
            .IncludedExtensions = "[""jpg"", ""png""]",
            .InterfaceScalePercent = 120,
            .InterfaceScaleRelativePercent = 110,
            .NewImageScaleMode = "actual",
            .ReduceMotion = True,
            .RecipientsOverlayPosition = "bottomRight",
            .RecipientsOverlayWidth = 300,
            .RecipientsOverlayFontSize = 14,
            .RecipientsOverlayOpacity = 90,
            .RecipientsOverlayVisibleRows = 8,
            .SlideshowRandomOrder = "random",
            .StopSlideshowOnManualNavigation = False,
            .SlideshowUiMode = "toolbar",
            .VideoAutoplay = False,
            .IncludeVideoInNavigation = False,
            .RememberVideoPosition = False,
            .VideoControlsHideDelaySec = 5,
            .SeekShortSec = 4,
            .SeekMediumSec = 12,
            .SeekLongSec = 90,
            .HideCursorAfterSec = 7,
            .ShowVideoControlsWhenPaused = False,
            .VideoSingleClickAction = "nextFile",
            .VideoEndAction = "repeat",
            .PreferredAudioLanguage = "eng",
            .PreferredSubtitleLanguage = "fra",
            .AudioEndAction = "repeat",
            .IncludeAudioInNavigation = False,
            .IncludeDocumentInNavigation = True,
            .AudioControlsAlwaysVisible = False,
            .AudioVisualiser = False,
            .SleepTimerMinutes = 45,
            .StartupOpenMode = "lastFile",
            .ResumeLastPlayback = True,
            .AllowNewWindows = True,
            .RecentFilesLimit = 75,
            .RecentFoldersLimit = 150,
            .OcrDiskCacheMaxMb = 500,
            .DecodeCacheMaxMb = 1024,
            .AnimatedViaVideo = False,
            .AnimatedViaVideoFfmpegAsked = True,
            .PanelViewMode = "list",
            .PanelThumbnailSize = 192,
            .PanelThumbCacheMb = 512,
            .ArchiveCacheMaxMb = 4096,
            .ArchiveMaxEntryMb = 1024,
            .ArchiveMaxEntries = 50000,
            .RemoteCacheMaxMb = 2048,
            .SecureIncludeInNavigation = False,
            .SecureRememberMinutes = 30,
            .SecureShowTrueName = False,
            .CustomHotkeysJson = "{""F1"":""Help""}",
            .FolderZoomHistory = "[{""Path"":""C:\\"", ""Factor"":1.5}]",
            .VideoPositionHistory = "[{""Path"":""C:\\a.mp4"", ""Position"":0.5}]"
        }

        Dim dest As New ModernViewerPreferences()
        Dim destRef As ModernViewerPreferences = dest
        dest.CopyFrom(source)

        Assert.Same(destRef, dest)
        Assert.Equal(source.ExportJson(True), dest.ExportJson(True))
    End Sub

    ' ----------------------------- 103 re-audit: independent oracle and full coverage ----

    <Fact>
    Public Sub The_shared_limit_constants_match_the_documented_policy()
        Assert.Equal(100, ModernViewerPreferences.ArchiveCacheMaxMbMin)
        Assert.Equal(16384, ModernViewerPreferences.ArchiveCacheMaxMbMax)
        Assert.Equal(16, ModernViewerPreferences.ArchiveMaxEntryMbMin)
        Assert.Equal(4096, ModernViewerPreferences.ArchiveMaxEntryMbMax)
        Assert.Equal(100, ModernViewerPreferences.ArchiveMaxEntriesMin)
        Assert.Equal(100000, ModernViewerPreferences.ArchiveMaxEntriesMax)
        Assert.Equal(100, ModernViewerPreferences.RemoteCacheMaxMbMin)
        Assert.Equal(16384, ModernViewerPreferences.RemoteCacheMaxMbMax)
        Assert.Equal(0, ModernViewerPreferences.SecureRememberMinutesMin)
        Assert.Equal(480, ModernViewerPreferences.SecureRememberMinutesMax)
        Assert.Equal(64, ModernViewerPreferences.TextPreviewMaxKbMin)
        Assert.Equal(65536, ModernViewerPreferences.TextPreviewMaxKbMax)
    End Sub

    <Fact>
    Public Sub FromJson_clamps_every_plain_clamp_property_at_min1_min_max_max1_and_both_extremes()
        For Each spec As NumericSpec In NumericSpecs()
            Dim prop As Reflection.PropertyInfo = GetType(ModernViewerPreferences).GetProperty(spec.Name)
            For Each raw As Integer In {Integer.MinValue, spec.Min - 1, spec.Min, spec.Max, spec.Max + 1, Integer.MaxValue}
                Dim reloaded As ModernViewerPreferences = ModernViewerPreferences.FromJson(JsonWith(spec.Name, raw))
                Dim expected As Integer = Math.Max(spec.Min, Math.Min(spec.Max, raw))
                Assert.Equal(expected, CInt(prop.GetValue(reloaded)))
            Next
        Next
    End Sub

    <Fact>
    Public Sub The_numeric_inventory_covers_every_integer_property_including_the_two_specials()
        Dim listed As New HashSet(Of String)(NumericSpecs().Select(Function(spec) spec.Name)) From {"InterfaceScalePercent", "InterfaceScaleRelativePercent", "PanelThumbnailSize"}
        Dim actual As New HashSet(Of String)(ModernViewerPreferences.WritableProperties().
                                             Where(Function(prop) prop.PropertyType = GetType(Integer)).
                                             Select(Function(prop) prop.Name))
        Assert.Empty(actual.Except(listed))   ' a new Integer property must join an inventory test
        Assert.Empty(listed.Except(actual))   ' a removed one must leave it
    End Sub

    <Fact>
    Public Sub The_choice_inventory_covers_every_string_property_that_is_not_free_form()
        Dim choiceNames As New HashSet(Of String)(ChoiceSpecs().Select(Function(spec) spec.Name))
        Dim freeNames As New HashSet(Of String) From {
            "IncludedExtensions", "DocumentExtensions", "OtherExtensions", "PreferredAudioLanguage", "PreferredSubtitleLanguage",
            "CustomHotkeysJson", "FolderZoomHistory", "VideoPositionHistory", "DocumentPageHistory", "FileManagerPlaceSorts"}
        Dim actual As New HashSet(Of String)(ModernViewerPreferences.WritableProperties().
                                             Where(Function(prop) prop.PropertyType = GetType(String)).
                                             Select(Function(prop) prop.Name))
        Assert.Empty(actual.Except(choiceNames).Except(freeNames))
        Assert.Empty(choiceNames.Except(actual))
        Assert.Empty(freeNames.Except(actual))
    End Sub

    <Fact>
    Public Sub Free_string_properties_survive_null_and_garbage()
        Dim p As New ModernViewerPreferences() With {
            .IncludedExtensions = Nothing, .PreferredAudioLanguage = Nothing,
            .PreferredSubtitleLanguage = Nothing, .CustomHotkeysJson = "not json",
            .FolderZoomHistory = "not json", .VideoPositionHistory = Nothing,
            .DocumentPageHistory = Nothing}
        p.Normalize()
        Assert.Equal("[]", p.IncludedExtensions)
        Assert.Equal(String.Empty, p.PreferredAudioLanguage)
        Assert.Equal(String.Empty, p.PreferredSubtitleLanguage)
        Assert.Equal("{}", p.CustomHotkeysJson)
        Assert.Equal("[]", p.FolderZoomHistory)
        Assert.Equal("[]", p.VideoPositionHistory)
        Assert.Equal("[]", p.DocumentPageHistory)
    End Sub

    <Fact>
    Public Sub The_boolean_inventory_covers_every_persisted_boolean_property()
        ' A Boolean can only be true or false, so there is nothing to fold - the list
        ' exists so a NEW property cannot slip past the inventory unnoticed.
        Dim listed As New HashSet(Of String) From {
            "AdvanceAfterCopy", "AllowNewWindows", "AnimatedViaVideo", "AnimatedViaVideoFfmpegAsked", "AudioControlsAlwaysVisible", "AudioVisualiser", "AutoAcceptLiveCast",
            "RevealStatusOnFileOperation",
            "FileManagerShowHiddenFiles", "FileManagerShowLocalPlaces", "FileManagerShowResourcePlaces", "FileManagerShowDevicePlaces", "FileManagerRememberSort",
            "CreateMissingDestination", "DeleteToRecycleBin", "IncludeAudioInNavigation",
            "IncludeDocumentInNavigation", "IncludeSubfolders", "IncludeVideoInNavigation", "ModernChrome", "ChromePinned", "ChromeRow1Pinned", "ChromeRow2Pinned", "ChromeStatusPinned", "RecipientsOverlayPinned", "ChromeWindowControlsPinned",
            "ReduceMotion", "RememberVideoPosition", "ResumeLastPlayback", "SortOtherFiles", "SecureIncludeInNavigation",
            "SecureShowTrueName", "ShowVideoControlsWhenPaused", "StopSlideshowOnManualNavigation",
            "VideoAutoplay", "RecipientsOverlayHasCustomPosition"}
        Dim actual As New HashSet(Of String)(ModernViewerPreferences.WritableProperties().
                                             Where(Function(prop) prop.PropertyType = GetType(Boolean)).
                                             Select(Function(prop) prop.Name))
        Assert.Empty(actual.Except(listed))
        Assert.Empty(listed.Except(actual))
    End Sub

    <Fact>
    Public Sub Export_then_import_round_trips_every_property()
        Dim p As New ModernViewerPreferences() With {
            .NameCollisionPolicy = "rename", .AfterFileOperation = "closeIfEmpty",
            .AdvanceAfterCopy = False, .RevealStatusOnFileOperation = True, .DeleteToRecycleBin = False, .ConfirmDelete = "never",
            .AutoAcceptLiveCast = True,
            .CreateMissingDestination = False, .IncludeSubfolders = True,
            .IncludedExtensions = "[""jpg"", ""png""]", .SortOtherFiles = True,
            .DocumentExtensions = "[""txt"", ""pdf""]", .OtherExtensions = "[""xyz""]",
            .TextPreviewMaxKb = 1024,
            .InterfaceScalePercent = 120,
            .InterfaceScaleRelativePercent = 110,
            .NewImageScaleMode = "perFolder", .ReduceMotion = True, .ModernChrome = False, .ChromePinned = True, .ChromeRow1Pinned = True, .ChromeRow2Pinned = True, .ChromeStatusPinned = True, .ChromeWindowControlsPinned = False, .RecipientsOverlayPinned = True, .ChromeSize = "adaptive", .ChromeOpacityPercent = 75, .ChromeCornerRadius = 10,
            .RecipientsOverlayPosition = "bottomRight", .RecipientsOverlayWidth = 400,
            .RecipientsOverlayFontSize = 14, .RecipientsOverlayOpacity = 77,
            .RecipientsOverlayVisibleRows = 8, .SlideshowRandomOrder = "shuffleCycle",
            .RecipientsOverlayHasCustomPosition = True, .RecipientsOverlayCustomX = -137, .RecipientsOverlayCustomY = 245,
            .StopSlideshowOnManualNavigation = True, .SlideshowUiMode = "toolbarAndStatus",
            .VideoAutoplay = False, .IncludeVideoInNavigation = False, .RememberVideoPosition = False,
            .VideoControlsHideDelaySec = 9, .ShowVideoControlsWhenPaused = False,
            .SeekShortSec = 5, .SeekMediumSec = 15, .SeekLongSec = 120, .HideCursorAfterSec = 9,
            .VideoSingleClickAction = "nextFile", .VideoEndAction = "repeat",
            .PreferredAudioLanguage = "ru", .PreferredSubtitleLanguage = "en",
            .AudioEndAction = "repeat", .IncludeAudioInNavigation = False,
            .IncludeDocumentInNavigation = True, .AudioControlsAlwaysVisible = False,
            .AudioVisualiser = False, .SleepTimerMinutes = 45, .StartupOpenMode = "lastFile",
            .ResumeLastPlayback = True, .AllowNewWindows = True, .RecentFilesLimit = 120,
            .RecentFoldersLimit = 64, .OcrDiskCacheMaxMb = 333, .DecodeCacheMaxMb = 1000,
            .AnimatedViaVideo = False, .AnimatedViaVideoFfmpegAsked = True,
            .PanelViewMode = "list", .PanelThumbnailSize = 128, .PanelThumbCacheMb = 512,
            .FileManagerViewMode = "list", .FileManagerContentMode = "all", .FileManagerNonMediaOpenAction = "openWith",
            .FileManagerShowHiddenFiles = True, .FileManagerShowLocalPlaces = False, .FileManagerShowResourcePlaces = False,
            .FileManagerShowDevicePlaces = False, .FileManagerRememberSort = False,
            .FileManagerPlaceSorts = "{""local:C:\\"":{""Column"":""Size"",""Descending"":true}}",
            .ArchiveCacheMaxMb = 4096, .ArchiveMaxEntryMb = 256, .ArchiveMaxEntries = 5000,
            .RemoteCacheMaxMb = 3000, .SecureIncludeInNavigation = False,
            .SecureRememberMinutes = 60, .SecureShowTrueName = False,
            .CustomHotkeysJson = "{""a"":""b""}",
            .FolderZoomHistory = "[{""Path"":""C:\\z"",""Factor"":1.5}]",
            .VideoPositionHistory = "[{""Path"":""C:\\a.mp4"",""LastWriteUtcTicks"":5,""Length"":6,""Position"":0.5}]",
            .DocumentPageHistory = "[{""Path"":""C:\\doc.pdf"",""LastWriteUtcTicks"":5,""Length"":6,""PageNumber"":2}]"}
        p.Normalize()
        Dim defaults As New ModernViewerPreferences()
        For Each prop As Reflection.PropertyInfo In ModernViewerPreferences.WritableProperties()
            Assert.NotEqual(prop.GetValue(defaults), prop.GetValue(p)) ' the fixture must not silently fall back to a default
        Next
        Dim reloaded As ModernViewerPreferences = ModernViewerPreferences.FromJson(p.ExportJson(includePersonalData:=True))
        For Each prop As Reflection.PropertyInfo In ModernViewerPreferences.WritableProperties()
            Assert.True(Equals(prop.GetValue(p), prop.GetValue(reloaded)), prop.Name)
        Next
        ' And it is stable: re-exporting the reload changes nothing.
        Assert.Equal(p.ExportJson(includePersonalData:=True), reloaded.ExportJson(includePersonalData:=True))
    End Sub

End Class
#End If
