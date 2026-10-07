Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.Json
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' FMSCFG 2.3 - the language of <c>accessNote</c> (CONFIG_FORMAT.md "Amendment 2026-10-07 (2.3)").
'''
''' Amendment A: the note is written in the exporter's own UI language. Amendment B (a SHOULD):
''' an exporter whose UI language is not English appends the same note in English to the same
''' string, after its own text; how the two are separated is the exporter's choice and an importer
''' parses nothing out of the note. This product separates them with one line feed
''' (<see cref="ShareText.AccessNoteLanguageSeparator"/>) and, for English, writes the note once -
''' which is also what keeps the two byte-pinned canonical vectors of
''' <c>ShareConfigCanonicalVectorTests</c> unchanged.
'''
''' The class shares the UI-language collection: it switches the process-wide language, as every
''' test of localized text here does.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ShareConfigAccessNoteTests

    Private Const Port As Integer = 2222

    ''' <summary>Every branch of the note ladder, by name. The LAN-only export is a branch of its
    ''' own (it describes the local path only).</summary>
    Public Shared Function Branches() As IEnumerable(Of Object())
        Dim open As New WorkerReachability With {.LanAddress = "192.168.1.50", .ExternalHost = "203.0.113.5", .ExternalPort = 2222, .ExternalPortChecked = True, .ExternalPortOpen = True}
        Dim closed As New WorkerReachability With {.LanAddress = "192.168.1.50", .ExternalHost = "203.0.113.5", .ExternalPort = 2222, .ExternalPortChecked = True, .ExternalPortOpen = False}
        Dim cgnat As New WorkerReachability With {.LanAddress = "192.168.1.50", .IsCgnat = True}
        Dim v6 As New WorkerReachability With {.LanAddress = "192.168.1.50", .Ipv6Address = "2001:db8::1"}
        Dim unconfirmed As New WorkerReachability With {.LanAddress = "192.168.1.50", .ExternalHost = "203.0.113.5", .ExternalPort = 2222}
        Dim lanOnly As New WorkerReachability With {.LanAddress = "192.168.1.50"}
        Return New List(Of Object()) From {
            New Object() {"lan-only-export", lanOnly, False},
            New Object() {"confirmed-open", open, True},
            New Object() {"confirmed-closed", closed, True},
            New Object() {"cgnat", cgnat, True},
            New Object() {"ipv6-only", v6, True},
            New Object() {"unconfirmed-forward", unconfirmed, True},
            New Object() {"lan-with-advice", lanOnly, True}
        }
    End Function

    Public Shared Function EveryBranchInEveryLanguage() As IEnumerable(Of Object())
        Dim all As New List(Of Object())()
        For Each code As String In Localization.Codes
            For Each b As Object() In Branches()
                all.Add(New Object() {code, b(0), b(1), b(2)})
            Next
        Next
        Return all
    End Function

    Private Shared Function NoteIn(code As String, reach As WorkerReachability, includeExternal As Boolean) As String
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            Return ShareText.AccessNote(reach, Port, includeExternal)
        Finally
            Localization.CurrentCode = saved
        End Try
    End Function

    Private Shared Function ExportNoteIn(code As String, reach As WorkerReachability, includeExternal As Boolean) As String
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            Return ShareText.AccessNoteForExport(reach, Port, includeExternal)
        Finally
            Localization.CurrentCode = saved
        End Try
    End Function

    ''' <summary>Amendments A and B on every rung of the ladder, in all thirteen languages: the first
    ''' line is the note in the UI language exactly as the window shows it, the second is the same
    ''' rung in English, and English itself is written once.</summary>
    <Theory>
    <MemberData(NameOf(EveryBranchInEveryLanguage))>
    Public Sub TheNote_IsOwnLanguageThenEnglish(code As String, branch As String, reach As WorkerReachability, includeExternal As Boolean)
        Dim own As String = NoteIn(code, reach, includeExternal)
        Dim english As String = NoteIn("en", reach, includeExternal)
        Dim exported As String = ExportNoteIn(code, reach, includeExternal)
        Assert.False(String.IsNullOrWhiteSpace(own), branch & ": the ladder has nothing to say in " & code)

        If code = "en" Then
            Assert.Equal(english, exported)
            Assert.DoesNotContain(ShareText.AccessNoteLanguageSeparator, exported)
            Return
        End If

        Assert.Equal(own & ShareText.AccessNoteLanguageSeparator & english, exported)
        Dim lines As String() = exported.Split(ShareText.AccessNoteLanguageSeparator(0))
        Assert.Equal(2, lines.Length)
        Assert.Equal(own, lines(0))
        Assert.Equal(english, lines(1))
        ' The duplicate is English, not the Russian key leaking through a missing translation.
        Assert.DoesNotMatch("[А-яЁё]", lines(1))
    End Sub

    ''' <summary>The same note, built twice, must not depend on the language being flipped in between:
    ''' the English duplicate is read from the table, so the process-wide language the UI thread paints
    ''' with is never touched while an export is built.</summary>
    <Fact>
    Public Sub BuildingTheDuplicate_DoesNotSwitchTheProcessLanguage()
        Dim reach As New WorkerReachability With {.LanAddress = "192.168.1.50"}
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = "de"
            Dim note As String = ShareText.AccessNoteForExport(reach, Port, True)
            Assert.Equal("de", Localization.CurrentCode)
            Assert.Contains(ShareText.AccessNoteLanguageSeparator, note)
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ''' <summary>"" stays "" - nothing to say is not a note with an empty English half, and no
    ''' separator is left dangling.</summary>
    <Theory>
    <InlineData("ru")>
    <InlineData("de")>
    <InlineData("ar")>
    <InlineData("en")>
    Public Sub NothingToSay_IsAnEmptyNote_InAnyLanguage(code As String)
        Assert.Equal("", ExportNoteIn(code, Nothing, True))
        ' LAN-only export of a worker that reported no LAN address: nothing local to describe.
        Assert.Equal("", ExportNoteIn(code, New WorkerReachability(), False))
        Assert.Equal("", ExportNoteIn(code, New WorkerReachability(), True))
    End Sub

    ''' <summary>The note that goes into the file is the note that comes back on the result, and it
    ''' survives the JSON round trip with its separator (escaped as <c>\n</c> in the payload, which
    ''' stays one compact line).</summary>
    <Theory>
    <InlineData("ru")>
    <InlineData("uk")>
    <InlineData("de")>
    <InlineData("ar")>
    <InlineData("zh")>
    Public Sub TheExportedPayload_CarriesTheDuplicate_AndStaysValidCompactJson(code As String)
        Dim reach As New WorkerReachability With {.LanAddress = "192.168.1.50", .ExternalHost = "203.0.113.5", .ExternalPort = 2222}
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            Dim res As ShareConfigResult = ShareConfigBuilder.Build(Running(reach), includeExternal:=True)
            Assert.NotNull(res)
            Assert.False(res.QrOverflow, "the English duplicate must not push an ordinary export past a QR code")
            Assert.NotNull(res.QrPng)

            Assert.DoesNotContain(vbLf, res.ConfigJson)   ' compact: the separator is escaped, not a raw line feed
            Using doc As JsonDocument = JsonDocument.Parse(res.ConfigJson)
                Dim note As String = doc.RootElement.GetProperty("accessNote").GetString()
                Assert.Equal(res.AccessNote, note)
                Dim parts As String() = note.Split(ShareText.AccessNoteLanguageSeparator(0))
                Assert.Equal(2, parts.Length)
                Assert.Equal(NoteIn(code, reach, True), parts(0))
                Assert.Equal(NoteIn("en", reach, True), parts(1))
                ' Rule 5 field order is untouched: accessNote is still the last member.
                Dim names As List(Of String) = doc.RootElement.EnumerateObject().Select(Function(p) p.Name).ToList()
                Assert.Equal("accessNote", names(names.Count - 1))
                Assert.Equal("createdAt", names(names.Count - 2))
            End Using
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ''' <summary>The cheapest honest check that a long language does not blow the reader's string
    ''' limit (the viewer's reader refuses a string over 4096 UTF-16 units): every rung, every
    ''' language, own text plus English.</summary>
    <Theory>
    <MemberData(NameOf(EveryBranchInEveryLanguage))>
    Public Sub TheDuplicate_StaysFarBelowAnyReadersStringLimit(code As String, branch As String, reach As WorkerReachability, includeExternal As Boolean)
        Assert.True(ExportNoteIn(code, reach, includeExternal).Length < 1024, branch & "/" & code & " grew past 1 KiB")
    End Sub

    ''' <summary>The Share Manager's own window and the broadcast descriptor keep the single-language
    ''' note: the duplicate is for the phone, so only the .fmscfg export asks for it.</summary>
    <Fact>
    Public Sub TheWindowsNote_IsStillOneLanguage()
        Dim reach As New WorkerReachability With {.LanAddress = "192.168.1.50"}
        Assert.DoesNotContain(ShareText.AccessNoteLanguageSeparator, NoteIn("de", reach, True))
        Assert.DoesNotContain(ShareText.AccessNoteLanguageSeparator, NoteIn("ru", reach, False))
    End Sub

    Private Shared Function Running(reach As WorkerReachability) As WorkerStatus
        Return New WorkerStatus With {
            .Running = True,
            .ListenPort = Port,
            .Username = "fms",
            .Password = "secret",
            .Fingerprint = "SHA256:abc123",
            .Roots = New List(Of ShareFolder) From {
                New ShareFolder With {.name = "Photos", .hostPath = "C:\__fms_test__" & Guid.NewGuid().ToString("N"), .readOnly = False}
            },
            .Reachability = reach
        }
    End Function

End Class
