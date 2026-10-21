Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' FMSCFG 2.5, the producer choices that depend on the UI language (the path choice (c) and the
''' always-explicit <c>readOnly</c> (a) live in <c>ShareConfigBuilderTests</c>; they need no language).
'''
''' Choice (d): an exporter whose UI language is not English writes <c>accessNote</c> as its own text, one
''' line feed, then the same note in English; English is written once. Nothing else in the document may
''' depend on the language, so the two byte-pinned vectors of <c>ShareConfigCanonicalVectorTests</c> stay
''' what they are.
'''
''' The class shares the UI-language collection: it switches the process-wide language, like every test of
''' localized text here.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ShareConfigProducerChoicesTests

    Private Const Port As Integer = 2222
    Private Const HostPath As String = "C:\fmscfg-producer\Photos"

    ''' <summary>Every shape of connect-back result the note ladder distinguishes for an export that carries an
    ''' outside address: a forward that answered, one confirmed closed (dropped from the file) and one never
    ''' checked.</summary>
    Public Shared Function Reaches() As IEnumerable(Of Object())
        Return New List(Of Object()) From {
            New Object() {"confirmed-open", New WorkerReachability With {.LanAddress = "192.168.1.50", .ExternalHost = "203.0.113.5", .ExternalPort = Port, .ExternalPortChecked = True, .ExternalPortOpen = True}},
            New Object() {"confirmed-closed", New WorkerReachability With {.LanAddress = "192.168.1.50", .ExternalHost = "203.0.113.5", .ExternalPort = Port, .ExternalPortChecked = True, .ExternalPortOpen = False}},
            New Object() {"never-checked", New WorkerReachability With {.LanAddress = "192.168.1.50", .ExternalHost = "203.0.113.5", .ExternalPort = Port}}
        }
    End Function

    Public Shared Function NonEnglishCodesWithReaches() As IEnumerable(Of Object())
        Dim all As New List(Of Object())()
        For Each code As String In Localization.Codes
            If String.Equals(code, "en", StringComparison.OrdinalIgnoreCase) Then Continue For
            For Each r As Object() In Reaches()
                all.Add(New Object() {code, r(0), r(1)})
            Next
        Next
        Return all
    End Function

    Private Shared Function Status(reach As WorkerReachability) As WorkerStatus
        Return New WorkerStatus With {
            .Running = True, .ListenPort = Port, .Username = "fms", .Password = "secret",
            .Fingerprint = "SHA256:abc123",
            .Roots = New List(Of ShareFolder) From {New ShareFolder With {.name = "Photos", .hostPath = HostPath, .readOnly = False}},
            .Reachability = reach
        }
    End Function

    ''' <summary>The export built while the process language is <paramref name="code"/>, with everything
    ''' else pinned (the stamp, and an explicit per-root entry so the user's own registry never decides a
    ''' byte).</summary>
    Private Shared Function BuildIn(code As String, reach As WorkerReachability, includeExternal As Boolean) As ShareConfigResult
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            Dim res As ShareConfigResult = ShareConfigBuilder.Build(
                Status(reach), includeExternal:=includeExternal, includePassword:=True,
                perRootParams:=New Dictionary(Of String, ShareRootParams)(StringComparer.OrdinalIgnoreCase) From {{HostPath, New ShareRootParams()}},
                stamp:=New ShareConfigStamp With {.ResourceName = "Home PC", .CreatedAtUtc = New Date(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc)})
            Assert.NotNull(res)
            Return res
        Finally
            Localization.CurrentCode = saved
        End Try
    End Function

    Private Shared Function NoteIn(code As String, reach As WorkerReachability) As String
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            Return ShareText.AccessNote(reach, Port, True)
        Finally
            Localization.CurrentCode = saved
        End Try
    End Function

    ''' <summary>The accessNote member of a document, read back as a string.</summary>
    Private Shared Function NoteOf(json As String) As String
        Using doc = JsonDocument.Parse(json)
            Return doc.RootElement.GetProperty("accessNote").GetString()
        End Using
    End Function

    ''' <summary>The document with its <c>accessNote</c> member cut off - what is left must not depend on the
    ''' language the PC happened to run in.</summary>
    Private Shared Function Head(json As String) As String
        Dim tail As String = ShareConfigCanonicalVectorTests.WithoutAccessNoteSuffix(json)
        Return json.Substring(0, json.Length - tail.Length)
    End Function

    ' --- (d) own & LF & English --------------------------------------------------------------

    <Theory>
    <MemberData(NameOf(NonEnglishCodesWithReaches))>
    Public Sub ANonEnglishAccessNote_IsOwnThenOneLineFeedThenEnglish(code As String, branch As String, reach As WorkerReachability)
        Dim own As String = NoteIn(code, reach)
        Dim english As String = NoteIn("en", reach)
        Assert.False(String.IsNullOrWhiteSpace(own), branch & "/" & code & ": nothing to say")
        Assert.False(String.IsNullOrWhiteSpace(english), branch & ": no English note")

        Dim res As ShareConfigResult = BuildIn(code, reach, includeExternal:=True)
        Assert.Equal(own & vbLf & english, NoteOf(res.ConfigJson))
        Assert.Equal(res.AccessNote, NoteOf(res.ConfigJson))
        Assert.Equal(1, NoteOf(res.ConfigJson).Count(Function(c) c = ChrW(10)))   ' one separator, nothing else breaks the line
        Assert.DoesNotContain(vbLf, res.ConfigJson)                                   ' compact: the separator rides escaped
    End Sub

    ''' <summary>The proof that the language touches the accessNote and nothing else: cut the accessNote
    ''' suffix off the document built in a non-English language and the English one, and the rest is the
    ''' same bytes - so the pinned English vectors describe every language's file but the note.</summary>
    <Theory>
    <MemberData(NameOf(NonEnglishCodesWithReaches))>
    Public Sub OnlyTheAccessNote_DiffersFromTheEnglishDocument(code As String, branch As String, reach As WorkerReachability)
        Dim inEnglish As ShareConfigResult = BuildIn("en", reach, includeExternal:=True)
        Dim inOwn As ShareConfigResult = BuildIn(code, reach, includeExternal:=True)
        Assert.Equal(Head(inEnglish.ConfigJson), Head(inOwn.ConfigJson))
        Assert.NotEqual(inEnglish.ConfigJson, inOwn.ConfigJson)
        ' The suffix is the note, the whole note and the closing brace: the English document ends with the
        ' English note alone, the other one with own & LF & English.
        Dim english As String = NoteIn("en", reach)
        Assert.Equal(english, NoteOf(inEnglish.ConfigJson))
        Assert.EndsWith("}", ShareConfigCanonicalVectorTests.WithoutAccessNoteSuffix(inOwn.ConfigJson))
    End Sub

    ''' <summary>English is written once: no line feed, no second copy, and it is the same single-language
    ''' note the Share Manager's window shows.</summary>
    <Theory>
    <MemberData(NameOf(Reaches))>
    Public Sub EnglishAccessNote_IsWrittenOnce(branch As String, reach As WorkerReachability)
        Dim res As ShareConfigResult = BuildIn("en", reach, includeExternal:=True)
        Dim note As String = NoteOf(res.ConfigJson)
        Assert.Equal(NoteIn("en", reach), note)
        Assert.DoesNotContain(vbLf, note)
        Assert.Equal(note, res.AccessNote)
        Dim first As Integer = res.ConfigJson.IndexOf(note.Substring(0, 20), StringComparison.Ordinal)
        Assert.True(first >= 0)
        Assert.Equal(-1, res.ConfigJson.IndexOf(note.Substring(0, 20), first + 1, StringComparison.Ordinal))
    End Sub

    ''' <summary>The LAN-only export describes the local path only, and still carries the English duplicate
    ''' in a non-English language.</summary>
    <Fact>
    Public Sub TheLanOnlyExport_CarriesOwnThenEnglish_AndNoOutsideAddress()
        Dim reach As New WorkerReachability With {.LanAddress = "192.168.1.50", .ExternalHost = "203.0.113.5", .ExternalPort = Port, .Ipv6Address = "2001:db8::1"}
        Dim saved As String = Localization.CurrentCode
        Try
            Dim res As ShareConfigResult = BuildIn("de", reach, includeExternal:=False)
            Localization.CurrentCode = "de"
            Dim own As String = ShareText.AccessNote(reach, Port, False)
            Localization.CurrentCode = "en"
            Dim english As String = ShareText.AccessNote(reach, Port, False)
            Assert.Equal(own & vbLf & english, NoteOf(res.ConfigJson))
            Assert.DoesNotContain("203.0.113.5", res.ConfigJson)
            Assert.DoesNotContain("2001:db8::1", res.ConfigJson)
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ' --- (c) the note still says why there is no outside address -----------------------------

    ''' <summary>With the dead forward gone from accessPaths the phone's user sees only the LAN address, so
    ''' the note is what says why: the outside port did not answer the external check, and what to do about
    ''' it - in the PC's language and in English.</summary>
    <Fact>
    Public Sub ConfirmedClosed_TheNoteStillExplainsTheMissingOutsideAddress()
        Dim reach As WorkerReachability = DirectCast(Reaches().Single(Function(r) CStr(r(0)) = "confirmed-closed")(1), WorkerReachability)
        Dim res As ShareConfigResult = BuildIn("en", reach, includeExternal:=True)
        Assert.False(res.HasExternal)
        Assert.DoesNotContain("portforward", res.ConfigJson)
        Dim note As String = NoteOf(res.ConfigJson)
        Assert.Contains("only", note)
        Assert.Contains("did not answer", note)
        Assert.Contains(Port.ToString(), note)
        Assert.Contains("UPnP", note)

        Dim ru As ShareConfigResult = BuildIn("ru", reach, includeExternal:=True)
        Dim parts As String() = NoteOf(ru.ConfigJson).Split(ChrW(10))
        Assert.Equal(2, parts.Length)
        Assert.Matches("[А-яЁё]", parts(0))
        Assert.Equal(note, parts(1))
    End Sub

    ' --- every accessNote text has an English entry ------------------------------------------

    ''' <summary>The English duplicate is read from the registered table by the Russian source string
    ''' (<c>ShareText.EnglishOf</c>), and a missing entry hands back the Russian source - a "duplicate" that
    ''' is not English. Every text the ladder can say (each <c>Say</c> / <c>SayF</c> literal in
    ''' <c>AccessNoteCore</c>, found in the source so a new rung is covered the day it is added) must have a
    ''' non-empty English column that holds no Cyrillic.</summary>
    <Fact>
    Public Sub EveryAccessNoteText_HasAnEnglishEntry_InTheEnglishIndex()
        Dim source As String = File.ReadAllText(Path.Combine(ExchangeTestSupport.CompanionDir(), "Core", "ShareText.vb"))
        Dim core As Integer = source.IndexOf("Function AccessNoteCore(", StringComparison.Ordinal)
        Dim endCore As Integer = source.IndexOf("End Function", core, StringComparison.Ordinal)
        Assert.True(core > 0 AndAlso endCore > core, "AccessNoteCore was not found in ShareText.vb - this scan needs updating")
        Dim body As String = source.Substring(core, endCore - core)

        Dim literals As New List(Of String)()
        For Each m As Match In Regex.Matches(body, "\bSayF?\(\s*english\s*,\s*""((?:[^""]|"""")*)""")
            literals.Add(m.Groups(1).Value.Replace("""""", """"))
        Next
        Assert.True(literals.Count >= 7, "Only " & literals.Count & " ladder texts found in AccessNoteCore - the scan is not reading the ladder")

        Dim table As Dictionary(Of String, String()) = Localization.All.ToDictionary(Function(kv) kv.Key, Function(kv) kv.Value, StringComparer.Ordinal)
        For Each ru As String In literals
            Dim row As String() = Nothing
            Assert.True(table.TryGetValue(ru, row), "not registered in the localization tables: " & ru)
            Assert.True(row.Length > 0 AndAlso Not String.IsNullOrWhiteSpace(row(0)), "no English column for: " & ru)
            Assert.DoesNotMatch("[А-яЁё]", row(0))
        Next
    End Sub

End Class
