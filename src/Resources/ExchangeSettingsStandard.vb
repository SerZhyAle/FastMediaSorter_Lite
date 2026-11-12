#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.Json

' Ticket L0296, DEVICE-EXCHANGE 1.12 items EN-ER: the viewer's half of the application settings standard. This program's
' `appId` is `fms_win` (its devices enroll as `fms-windows`). The settings it exchanges are the ones declared below and nothing
' else: a preference that is not in the table is local-only (item EQ), so a new preference stays on this PC until somebody
' declares it here on purpose.
'
' What is never declared, by the categories of item EQ: secrets (the Secure* group, device keys and tokens live outside this
' model anyway), machine bindings (DocumentConverterLocation and every other path or folder), window and display state (the
' chrome rows' places and pins, the recipients overlay's position and width, the interface scale), and what is not a setting
' (the *History members, cache sizes, recent-list limits, the "asked once" markers). AllowNewWindows is written straight to the
' registry by its own check box (053 F-18) and stays local as well.
'
' Both ends hold the exclusions: the serializer writes only declared keys, and the reader applies only declared keys - a
' foreign, old or hostile payload cannot reach a local-only value. A read is a point in time (item ER): nothing here keeps a
' link to the server, and nothing is sent when a local value changes afterwards.
'
' Mainline only, like the rest of the exchange client; the net48 viewer project lists no exchange file (DualModeGateGuardTests).

''' <summary>The result of reading a standard into the local preferences.</summary>
Friend Enum ExchangeSettingsReadOutcome
    ''' <summary>The payload was understood and the chosen groups were applied to <see cref="ExchangeSettingsReadResult.Preferences"/>.</summary>
    Applied = 0
    ''' <summary>The standard's `formatVersion` is newer than this program implements: nothing applied (`format-unsupported`).</summary>
    FormatUnsupported = 1
    ''' <summary>The payload is not one object of the shape of item EO: nothing applied.</summary>
    Invalid = 2
End Enum

Friend NotInheritable Class ExchangeSettingsReadResult

    Public ReadOnly Outcome As ExchangeSettingsReadOutcome
    ''' <summary>A NEW preferences object (the caller's is never touched) holding the applied values, normalized; Nothing unless Applied.</summary>
    Public ReadOnly Preferences As ModernViewerPreferences
    ''' <summary>How many declared values the payload carried for the chosen groups and were taken.</summary>
    Public ReadOnly AppliedCount As Integer

    Public Sub New(outcome As ExchangeSettingsReadOutcome, preferences As ModernViewerPreferences, appliedCount As Integer)
        Me.Outcome = outcome
        Me.Preferences = preferences
        Me.AppliedCount = appliedCount
    End Sub

End Class

Friend NotInheritable Class ExchangeSettingsStandard

    Private Sub New()
    End Sub

    ''' <summary>Item EN: the `appId` of this program, the one its `product` (`fms-windows`) maps to.</summary>
    Public Const AppId As String = "fms_win"
    ''' <summary>Item EO: the one `formatVersion` this program writes and reads.</summary>
    Public Const FormatVersion As Integer = 1
    ''' <summary>Item EO: the bound of `settingsPayload`, bytes of UTF-8.</summary>
    Public Const MaxPayloadBytes As Integer = 65536

    ' The group ids are wire values (item EQ: lowercase, never translated); the person sees GroupTitle.
    Public Const GroupGeneral As String = "general"
    Public Const GroupAppearance As String = "appearance"
    Public Const GroupPlayback As String = "playback"
    Public Const GroupShortcuts As String = "shortcuts"

    ''' <summary>The groups of this program, in the order they are shown and written.</summary>
    Public Shared ReadOnly Groups As String() = {GroupGeneral, GroupAppearance, GroupPlayback, GroupShortcuts}

    ' group -> the declared exchangeable preferences of ModernViewerPreferences, in the order they are written
    Private Shared ReadOnly Declared As New Dictionary(Of String, String())(StringComparer.Ordinal) From {
        {GroupGeneral, {"NameCollisionPolicy", "AfterFileOperation", "AdvanceAfterCopy", "RevealStatusOnFileOperation",
                        "DeleteToRecycleBin", "ConfirmDelete", "CreateMissingDestination", "IncludeSubfolders", "IncludedExtensions",
                        "SortOtherFiles", "DocumentExtensions", "OtherExtensions", "IncludeVideoInNavigation",
                        "IncludeAudioInNavigation", "IncludeDocumentInNavigation", "StartupOpenMode"}},
        {GroupAppearance, {"NewImageScaleMode", "ReduceMotion", "ModernChrome", "ChromeOpacityPercent", "ChromeCornerRadius",
                           "ChromeSize", "RecipientsOverlayFontSize", "RecipientsOverlayOpacity", "RecipientsOverlayVisibleRows",
                           "PanelViewMode", "FileManagerViewMode", "FileManagerShowHiddenFiles"}},
        {GroupPlayback, {"SlideshowRandomOrder", "StopSlideshowOnManualNavigation", "SlideshowUiMode", "VideoAutoplay",
                         "RememberVideoPosition", "VideoControlsHideDelaySec", "ShowVideoControlsWhenPaused", "VideoSingleClickAction",
                         "VideoEndAction", "PreferredAudioLanguage", "PreferredSubtitleLanguage", "SeekShortSec", "SeekMediumSec",
                         "SeekLongSec", "HideCursorAfterSec", "AudioEndAction", "AudioControlsAlwaysVisible", "AudioVisualiser",
                         "AnimatedViaVideo", "ResumeLastPlayback"}},
        {GroupShortcuts, {"CustomHotkeysJson"}}}

    ''' <summary>The declared preferences of one group (empty for an unknown id).</summary>
    Public Shared Function DeclaredIn(groupId As String) As IReadOnlyList(Of String)
        Dim names As String() = Nothing
        If groupId IsNot Nothing AndAlso Declared.TryGetValue(groupId, names) Then Return names
        Return Array.Empty(Of String)()
    End Function

    ''' <summary>True for a preference this program exchanges; everything else is local-only (item EQ).</summary>
    Public Shared Function IsExchangeable(propertyName As String) As Boolean
        If String.IsNullOrEmpty(propertyName) Then Return False
        For Each names As String() In Declared.Values
            If Array.IndexOf(names, propertyName) >= 0 Then Return True
        Next
        Return False
    End Function

    ''' <summary>The payload key of a preference: its name with a lowercase first letter (`ConfirmDelete` -> `confirmDelete`).</summary>
    Public Shared Function KeyOf(propertyName As String) As String
        If String.IsNullOrEmpty(propertyName) Then Return ""
        Return Char.ToLowerInvariant(propertyName(0)) & propertyName.Substring(1)
    End Function

    Private Shared Function PropertyOf(propertyName As String) As Reflection.PropertyInfo
        Return ModernViewerPreferences.WritableProperties().FirstOrDefault(Function(p) String.Equals(p.Name, propertyName, StringComparison.Ordinal))
    End Function

    ''' <summary>Only the known group ids of <paramref name="chosen"/>, in this program's order, each once.</summary>
    Public Shared Function ChosenGroups(chosen As IEnumerable(Of String)) As IReadOnlyList(Of String)
        If chosen Is Nothing Then Return Array.Empty(Of String)()
        Dim wanted As New HashSet(Of String)(chosen.Where(Function(g) g IsNot Nothing), StringComparer.Ordinal)
        Return Groups.Where(Function(g) wanted.Contains(g)).ToArray()
    End Function

    ''' <summary>Item EO / EQ: `{"version":1,"groups":{..}}` holding the declared values of the chosen groups and nothing else. A
    ''' group left out is absent from the payload (a save replaces the whole standard, so it leaves the server's copy too).</summary>
    Public Shared Function Serialize(preferences As ModernViewerPreferences, chosen As IEnumerable(Of String)) As String
        If preferences Is Nothing Then Throw New ArgumentNullException(NameOf(preferences))
        Using buffer As New MemoryStream()
            Using w As New Utf8JsonWriter(buffer)
                w.WriteStartObject()
                w.WriteNumber("version", FormatVersion)
                w.WriteStartObject("groups")
                For Each groupId As String In ChosenGroups(chosen)
                    w.WriteStartObject(groupId)
                    For Each name As String In Declared(groupId)
                        Dim prop As Reflection.PropertyInfo = PropertyOf(name)
                        If prop Is Nothing Then Continue For
                        WriteValue(w, KeyOf(name), prop.GetValue(preferences))
                    Next
                    w.WriteEndObject()
                Next
                w.WriteEndObject()
                w.WriteEndObject()
            End Using
            Return Encoding.UTF8.GetString(buffer.ToArray())
        End Using
    End Function

    Private Shared Sub WriteValue(w As Utf8JsonWriter, key As String, value As Object)
        If TypeOf value Is Boolean Then
            w.WriteBoolean(key, CBool(value))
        ElseIf TypeOf value Is Integer Then
            w.WriteNumber(key, CInt(value))
        ElseIf TypeOf value Is Long Then
            w.WriteNumber(key, CLng(value))
        ElseIf TypeOf value Is Double Then
            w.WriteNumber(key, CDbl(value))
        ElseIf TypeOf value Is String Then
            w.WriteString(key, CStr(value))
        End If
        ' any other type is not a value of the standard: left out
    End Sub

    ''' <summary>Item ER: understands the WHOLE answer before anything is applied, then applies only the chosen groups and only the
    ''' declared keys to a copy of <paramref name="current"/>. A group or a key this program does not know is ignored; a key of the
    ''' wrong JSON kind is ignored; an absent key leaves the local value. <paramref name="current"/> itself is never changed.</summary>
    Public Shared Function Read(current As ModernViewerPreferences, formatVersion As Long, payload As String,
                                chosen As IEnumerable(Of String)) As ExchangeSettingsReadResult
        If current Is Nothing Then Throw New ArgumentNullException(NameOf(current))
        If formatVersion > ExchangeSettingsStandard.FormatVersion Then
            Return New ExchangeSettingsReadResult(ExchangeSettingsReadOutcome.FormatUnsupported, Nothing, 0)
        End If
        If formatVersion < 1 OrElse String.IsNullOrEmpty(payload) OrElse Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes Then
            Return New ExchangeSettingsReadResult(ExchangeSettingsReadOutcome.Invalid, Nothing, 0)
        End If
        Dim doc As JsonDocument
        Try
            doc = JsonDocument.Parse(payload)
        Catch ex As JsonException
            Return New ExchangeSettingsReadResult(ExchangeSettingsReadOutcome.Invalid, Nothing, 0)
        End Try
        Using doc
            Dim root As JsonElement = doc.RootElement
            Dim version As JsonElement
            Dim groups As JsonElement
            Dim v As Long
            If root.ValueKind <> JsonValueKind.Object OrElse
               Not root.TryGetProperty("version", version) OrElse version.ValueKind <> JsonValueKind.Number OrElse
               Not version.TryGetInt64(v) OrElse v <> formatVersion OrElse
               Not root.TryGetProperty("groups", groups) OrElse groups.ValueKind <> JsonValueKind.Object Then
                Return New ExchangeSettingsReadResult(ExchangeSettingsReadOutcome.Invalid, Nothing, 0)
            End If
            For Each g As JsonProperty In groups.EnumerateObject()
                If g.Value.ValueKind <> JsonValueKind.Object Then Return New ExchangeSettingsReadResult(ExchangeSettingsReadOutcome.Invalid, Nothing, 0)
            Next

            Dim result As ModernViewerPreferences = SettingsImportPlan.Clone(current)
            Dim applied As Integer = 0
            For Each groupId As String In ChosenGroups(chosen)
                Dim group As JsonElement
                If Not groups.TryGetProperty(groupId, group) Then Continue For
                For Each name As String In Declared(groupId)
                    Dim value As JsonElement
                    If Not group.TryGetProperty(KeyOf(name), value) Then Continue For
                    Dim prop As Reflection.PropertyInfo = PropertyOf(name)
                    If prop Is Nothing Then Continue For
                    Dim taken As Object = Nothing
                    If Not TryConvert(value, prop.PropertyType, taken) Then Continue For
                    prop.SetValue(result, taken)
                    applied += 1
                Next
            Next
            result.Normalize()
            Return New ExchangeSettingsReadResult(ExchangeSettingsReadOutcome.Applied, result, applied)
        End Using
    End Function

    Private Shared Function TryConvert(value As JsonElement, target As Type, ByRef taken As Object) As Boolean
        If target Is GetType(Boolean) Then
            If value.ValueKind = JsonValueKind.True Then taken = True : Return True
            If value.ValueKind = JsonValueKind.False Then taken = False : Return True
            Return False
        End If
        If target Is GetType(String) Then
            If value.ValueKind <> JsonValueKind.String Then Return False
            taken = value.GetString()
            Return True
        End If
        If value.ValueKind <> JsonValueKind.Number Then Return False
        If target Is GetType(Integer) Then
            Dim i As Integer
            If Not value.TryGetInt32(i) Then Return False
            taken = i
            Return True
        End If
        If target Is GetType(Long) Then
            Dim l As Long
            If Not value.TryGetInt64(l) Then Return False
            taken = l
            Return True
        End If
        If target Is GetType(Double) Then
            Dim d As Double
            If Not value.TryGetDouble(d) OrElse Double.IsNaN(d) OrElse Double.IsInfinity(d) Then Return False
            taken = d
            Return True
        End If
        Return False
    End Function

    ''' <summary>RFC 3339 in UTC, seconds, for the informational `timestamp` of a save.</summary>
    Public Shared Function TimestampOf(utc As DateTime) As String
        Return utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Globalization.CultureInfo.InvariantCulture)
    End Function

End Class

''' <summary>The viewer's live preferences, as the exchange page reaches them: the page is linked into Lite.Tests, which has no
''' viewer window, so the window plugs its two functions in at start-up. Unset, the page shows no settings action.</summary>
Friend NotInheritable Class ExchangeSettingsPort

    Private Sub New()
    End Sub

    ''' <summary>The live preferences object the viewer holds (never a copy).</summary>
    Public Shared Current As Func(Of ModernViewerPreferences)
    ''' <summary>Puts a read standard into the live object and persists it; True when the write reached the registry.</summary>
    Public Shared Apply As Func(Of ModernViewerPreferences, Boolean)
    ''' <summary>After a read: the window hosting the page rebuilds what it shows (may be unset).</summary>
    Public Shared Refresh As Action(Of System.Windows.Forms.Form)

    Public Shared ReadOnly Property IsConnected As Boolean
        Get
            Return Current IsNot Nothing AndAlso Apply IsNot Nothing
        End Get
    End Property

End Class
#End If
