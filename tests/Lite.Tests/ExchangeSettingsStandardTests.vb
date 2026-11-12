#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Ticket L0296, DEVICE-EXCHANGE 1.12 items EN-ER, the client cases 10 to 14 of the amendment's Conformance table: the declared
''' table, the exclusions on both ends (vector 07 spellings and this program's own local-only preferences), groups left out of a
''' save and of a read, a read that changes nothing until the whole payload is understood, and the frames' shape.
''' </summary>
Public Class ExchangeSettingsStandardTests

    ''' <summary>The spellings of vectors/settings-standard-v1/07-local-exclusion-manifest.json, typed from the catalog.</summary>
    Private Shared ReadOnly ManifestExclusions As String() = {
        "device_key", "private_key", "auth_tokens", "passwords", "hardware_paths", "drive_letters", "port_bindings",
        "loopback_ip", "window_bounds", "window_state", "screen_dpi"}

    ''' <summary>This program's own preferences that fall in a category of item EQ: paths, window/display state, history, caches,
    ''' secure-container behaviour, the "asked once" marker and the key its own check box writes.</summary>
    Private Shared ReadOnly OwnExclusions As String() = {
        "DocumentConverterLocation", "ChromeRow1Place", "ChromeRow2Place", "ChromeStatusPlace", "ChromePinned",
        "RecipientsOverlayCustomX", "RecipientsOverlayCustomY", "RecipientsOverlayHasCustomPosition", "RecipientsOverlayPosition",
        "RecipientsOverlayWidth", "InterfaceScalePercent", "InterfaceScaleRelativePercent", "FolderZoomHistory",
        "VideoPositionHistory", "DocumentPageHistory", "DecodeCacheMaxMb", "RemoteCacheMaxMb", "ArchiveCacheMaxMb",
        "OcrDiskCacheMaxMb", "RecentFilesLimit", "RecentFoldersLimit", "SecureRememberMinutes", "SecureShowTrueName",
        "AnimatedViaVideoFfmpegAsked", "AllowNewWindows", "AutoAcceptLiveCast", "FileManagerPlaceSorts"}

    Private Shared Function KeysOf(payload As String) As List(Of String)
        Dim keys As New List(Of String)
        Using doc As JsonDocument = JsonDocument.Parse(payload)
            For Each g As JsonProperty In doc.RootElement.GetProperty("groups").EnumerateObject()
                For Each k As JsonProperty In g.Value.EnumerateObject()
                    keys.Add(k.Name)
                Next
            Next
        End Using
        Return keys
    End Function

    <Fact>
    Public Sub Every_declared_preference_is_a_scalar_property_of_the_model_and_belongs_to_one_group()
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        For Each groupId As String In ExchangeSettingsStandard.Groups
            Assert.Matches("^[a-z][a-z0-9-]{0,31}$", groupId)
            Assert.NotEmpty(ExchangeSettingsStandard.DeclaredIn(groupId))
            For Each name As String In ExchangeSettingsStandard.DeclaredIn(groupId)
                Assert.True(seen.Add(name), "declared twice: " & name)
                Dim prop As Reflection.PropertyInfo = ModernViewerPreferences.WritableProperties().FirstOrDefault(Function(p) p.Name = name)
                Assert.True(prop IsNot Nothing, "not a preference: " & name)
                Assert.Contains(prop.PropertyType, {GetType(Boolean), GetType(Integer), GetType(Long), GetType(Double), GetType(String)})
            Next
        Next
    End Sub

    <Fact>
    Public Sub Case_10_no_exclusion_is_declared_and_none_reaches_a_payload()
        For Each name As String In OwnExclusions
            Assert.True(ModernViewerPreferences.WritableProperties().Any(Function(p) p.Name = name), "the exclusion list names a missing preference: " & name)
            Assert.False(ExchangeSettingsStandard.IsExchangeable(name), name)
        Next
        Dim keys As List(Of String) = KeysOf(ExchangeSettingsStandard.Serialize(New ModernViewerPreferences(), ExchangeSettingsStandard.Groups))
        Assert.NotEmpty(keys)
        For Each spelling As String In ManifestExclusions
            Assert.DoesNotContain(keys, Function(k) k.IndexOf(spelling, StringComparison.OrdinalIgnoreCase) >= 0)
        Next
        For Each name As String In OwnExclusions
            Assert.DoesNotContain(ExchangeSettingsStandard.KeyOf(name), keys)
        Next
        ' an undeclared preference is local-only: every key written is a declared one
        For Each k As String In keys
            Assert.Contains(ExchangeSettingsStandard.Groups.SelectMany(Function(g) ExchangeSettingsStandard.DeclaredIn(g)), Function(n) ExchangeSettingsStandard.KeyOf(n) = k)
        Next
    End Sub

    <Fact>
    Public Sub Case_10_an_inbound_payload_cannot_reach_a_local_only_value()
        Dim current As New ModernViewerPreferences()
        current.DocumentConverterLocation = "C:\Tools\converter"
        current.ChromeRow1Place = "kept"
        current.SecureRememberMinutes = 42
        current.AllowNewWindows = True
        current.FolderZoomHistory = "[]"
        Dim hostile As New StringBuilder("{""version"":1,""groups"":{")
        Dim first As Boolean = True
        For Each groupId As String In ExchangeSettingsStandard.Groups
            If Not first Then hostile.Append(",")
            first = False
            hostile.Append("""" & groupId & """:{")
            hostile.Append(String.Join(",", ManifestExclusions.Select(Function(s) """" & s & """:""x""")))
            hostile.Append(",""documentConverterLocation"":""D:\\evil"",""chromeRow1Place"":""moved"",""secureRememberMinutes"":1,")
            hostile.Append("""allowNewWindows"":false,""folderZoomHistory"":""[1]""}")
        Next
        hostile.Append("}}")
        Dim r As ExchangeSettingsReadResult = ExchangeSettingsStandard.Read(current, 1, hostile.ToString(), ExchangeSettingsStandard.Groups)
        Assert.Equal(ExchangeSettingsReadOutcome.Applied, r.Outcome)
        Assert.Equal(0, r.AppliedCount)
        ' the read normalizes its copy; every local-only value is what normalization alone makes of the local one
        Dim expected As ModernViewerPreferences = SettingsImportPlan.Clone(current)
        expected.Normalize()
        For Each name As String In OwnExclusions
            Dim prop As Reflection.PropertyInfo = GetType(ModernViewerPreferences).GetProperty(name)
            Assert.Equal(prop.GetValue(expected), prop.GetValue(r.Preferences))
        Next
        Assert.True(r.Preferences.AllowNewWindows)
        Assert.Equal(42, r.Preferences.SecureRememberMinutes)
    End Sub

    <Fact>
    Public Sub Case_11_a_group_left_out_of_a_save_is_absent_and_one_left_out_of_a_read_is_not_applied()
        Dim source As New ModernViewerPreferences()
        source.VideoAutoplay = Not source.VideoAutoplay
        source.ReduceMotion = Not source.ReduceMotion
        source.CustomHotkeysJson = "{""next"":""Right""}"
        Dim payload As String = ExchangeSettingsStandard.Serialize(source, {ExchangeSettingsStandard.GroupPlayback, ExchangeSettingsStandard.GroupAppearance})
        Using doc As JsonDocument = JsonDocument.Parse(payload)
            Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32())
            Dim groups As String() = doc.RootElement.GetProperty("groups").EnumerateObject().Select(Function(p) p.Name).ToArray()
            Assert.Equal({ExchangeSettingsStandard.GroupAppearance, ExchangeSettingsStandard.GroupPlayback}, groups)
        End Using

        Dim target As New ModernViewerPreferences()
        Dim r As ExchangeSettingsReadResult = ExchangeSettingsStandard.Read(target, 1, payload, {ExchangeSettingsStandard.GroupPlayback})
        Assert.Equal(ExchangeSettingsReadOutcome.Applied, r.Outcome)
        Assert.Equal(source.VideoAutoplay, r.Preferences.VideoAutoplay)
        Assert.Equal(target.ReduceMotion, r.Preferences.ReduceMotion)          ' appearance was in the payload but not chosen
        Assert.Equal(target.CustomHotkeysJson, r.Preferences.CustomHotkeysJson) ' shortcuts was not saved at all
        ' the caller's object is never touched; the new values arrive in a copy
        Assert.NotSame(target, r.Preferences)
        Assert.Equal(New ModernViewerPreferences().VideoAutoplay, target.VideoAutoplay)
    End Sub

    <Fact>
    Public Sub A_full_save_then_read_carries_every_declared_value()
        Dim source As New ModernViewerPreferences()
        source.ConfirmDelete = "never"
        source.SeekLongSec = 90
        source.PreferredAudioLanguage = "uk"
        source.ChromeOpacityPercent = 70
        source.CustomHotkeysJson = "{""a"":""B""}"
        Dim payload As String = ExchangeSettingsStandard.Serialize(source, ExchangeSettingsStandard.Groups)
        Assert.True(Encoding.UTF8.GetByteCount(payload) <= ExchangeSettingsStandard.MaxPayloadBytes)
        Dim r As ExchangeSettingsReadResult = ExchangeSettingsStandard.Read(New ModernViewerPreferences(), 1, payload, ExchangeSettingsStandard.Groups)
        Assert.Equal(ExchangeSettingsReadOutcome.Applied, r.Outcome)
        For Each groupId As String In ExchangeSettingsStandard.Groups
            For Each name As String In ExchangeSettingsStandard.DeclaredIn(groupId)
                Dim prop As Reflection.PropertyInfo = GetType(ModernViewerPreferences).GetProperty(name)
                Dim expected As ModernViewerPreferences = SettingsImportPlan.Clone(source)
                expected.Normalize()
                Assert.Equal(prop.GetValue(expected), prop.GetValue(r.Preferences))
            Next
        Next
    End Sub

    <Fact>
    Public Sub A_read_applies_nothing_until_the_whole_payload_is_understood()
        Dim current As New ModernViewerPreferences()
        Dim good As String = "{""version"":1,""groups"":{""playback"":{""videoAutoplay"":false}}}"
        Assert.Equal(ExchangeSettingsReadOutcome.FormatUnsupported, ExchangeSettingsStandard.Read(current, 2, good, ExchangeSettingsStandard.Groups).Outcome)
        For Each bad As String In {"", "[]", "not json", "{""version"":2,""groups"":{}}", "{""version"":1}", "{""version"":1,""groups"":[]}",
                                   "{""version"":""1"",""groups"":{}}", "{""version"":1,""groups"":{""playback"":{""videoAutoplay"":false},""general"":5}}"}
            Dim r As ExchangeSettingsReadResult = ExchangeSettingsStandard.Read(current, 1, bad, ExchangeSettingsStandard.Groups)
            Assert.Equal(ExchangeSettingsReadOutcome.Invalid, r.Outcome)
            Assert.Null(r.Preferences)
        Next
        Assert.Equal(ExchangeSettingsReadOutcome.Invalid, ExchangeSettingsStandard.Read(current, 1, "{""version"":1,""groups"":{""a"":""" & New String("x"c, 65536) & """}}", ExchangeSettingsStandard.Groups).Outcome)
        Assert.True(current.VideoAutoplay)
    End Sub

    <Fact>
    Public Sub Unknown_groups_unknown_keys_and_values_of_the_wrong_kind_are_ignored()
        ' the payload of vector 01: `appearance.theme`, `appearance.density` are not keys of this program, and `general.confirmDelete`
        ' is a string preference here - a boolean leaves it as it is
        Dim vector01 As String = "{""version"":1,""groups"":{""appearance"":{""theme"":""dark"",""density"":""compact""},""general"":{""confirmDelete"":true}}}"
        Dim current As New ModernViewerPreferences()
        Dim r As ExchangeSettingsReadResult = ExchangeSettingsStandard.Read(current, 1, vector01, ExchangeSettingsStandard.Groups)
        Assert.Equal(ExchangeSettingsReadOutcome.Applied, r.Outcome)
        Assert.Equal(0, r.AppliedCount)
        Assert.Equal(current.ConfirmDelete, r.Preferences.ConfirmDelete)
        Dim extra As String = "{""version"":1,""groups"":{""interface"":{""x"":1},""playback"":{""seekShortSec"":5,""seekLongSec"":""many"",""videoAutoplay"":0}}}"
        r = ExchangeSettingsStandard.Read(current, 1, extra, ExchangeSettingsStandard.Groups)
        Assert.Equal(1, r.AppliedCount)
        Assert.Equal(5, r.Preferences.SeekShortSec)
        Assert.Equal(current.SeekLongSec, r.Preferences.SeekLongSec)
        Assert.Equal(current.VideoAutoplay, r.Preferences.VideoAutoplay)
    End Sub

    <Fact>
    Public Sub The_frames_are_built_in_the_shape_of_the_generated_vectors()
        Dim payload As String = "{""version"":1,""groups"":{""appearance"":{""theme"":""dark"",""density"":""compact""},""general"":{""confirmDelete"":true}}}"
        Dim save As String = Encoding.UTF8.GetString(ExchangeEnvelopeCodec.Encode(ExchangeMessages.SettingsSaveStandard("fms_win", 1, payload, "2026-11-10T02:00:00Z")))
        Assert.Equal("{""schemaVersion"":2,""type"":""settings_save_standard"",""appId"":""fms_win"",""formatVersion"":1,""settingsPayload"":" &
                     JsonSerializer.Serialize(payload) & ",""timestamp"":""2026-11-10T02:00:00Z""}", save)
        Dim noStamp As String = Encoding.UTF8.GetString(ExchangeEnvelopeCodec.Encode(ExchangeMessages.SettingsSaveStandard("fms_win", 1, payload, "")))
        Assert.DoesNotContain("timestamp", noStamp)
        Assert.Equal("{""schemaVersion"":2,""type"":""settings_get_standard"",""appId"":""fms_win""}",
                     Encoding.UTF8.GetString(ExchangeEnvelopeCodec.Encode(ExchangeMessages.SettingsGetStandard(ExchangeSettingsStandard.AppId))))
        Assert.Equal("2026-11-10T02:00:00Z", ExchangeSettingsStandard.TimestampOf(New DateTime(2026, 11, 10, 2, 0, 0, DateTimeKind.Utc)))
    End Sub

    <Fact>
    Public Sub The_four_reasons_have_their_own_rows()
        For Each pair As (ExchangeRefusal, String) In {(ExchangeRefusal.NotFound, "not-found"), (ExchangeRefusal.InvalidPayload, "invalid-payload"),
                                                      (ExchangeRefusal.AppIdMismatch, "app-id-mismatch"), (ExchangeRefusal.VersionIncompatible, "version-incompatible")}
            Assert.Equal(pair.Item1, ExchangeRefusals.Parse(pair.Item2))
            Assert.Equal(pair.Item2, ConnectionDiagnosisMessages.OfWire(pair.Item2).Code)
        Next
    End Sub

    <Fact>
    Public Async Function Case_14_a_session_whose_server_does_not_list_settings_sends_nothing() As Task
        Dim session As New ExchangeControlSession(New ExchangeControlOptions(), Nothing)
        Assert.False(session.OffersSettings())
        Dim save As ExchangeSettingsRequestResult = Await session.SaveSettingsStandardAsync("fms_win", 1, "{""version"":1,""groups"":{}}", "", CancellationToken.None)
        Assert.Equal(ExchangeSettingsRequestOutcome.NotOffered, save.Outcome)
        Dim read As ExchangeSettingsRequestResult = Await session.GetSettingsStandardAsync("fms_win", CancellationToken.None)
        Assert.Equal(ExchangeSettingsRequestOutcome.NotOffered, read.Outcome)
    End Function

End Class
#End If
