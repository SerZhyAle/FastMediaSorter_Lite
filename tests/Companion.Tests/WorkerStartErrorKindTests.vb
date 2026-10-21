Option Strict On

Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' WORKER-IPC 0.17, consolidation amendment item 1 - <c>status.lastStartErrorKind</c> from the host's
''' side, and the compatibility law (VERSIONING section 4) for the field next to it.
'''
''' What is pinned: the four tokens bind; an unknown token and a value that is not a JSON string read as
''' <c>other</c> with the token kept; a worker that does not send the field is read from the literal
''' ` [excluded-range]` token and the free text exactly as before (the vendored worker predates the
''' field); a kind that is present is never second-guessed from the text; a type drift of
''' <c>lastStartError</c> is read as absent instead of turning the whole answer into "unreadable"; and a
''' response with a higher <c>schemaVersion</c> is refused cleanly, by name, through
''' <c>WorkerIpc.ParseResponse</c>. Wire bodies are written with single quotes and turned into JSON by
''' <c>Q</c> - none of them contains an apostrophe.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class WorkerStartErrorKindTests

    Private Shared Function Q(text As String) As String
        Return text.Replace("'"c, """"c)
    End Function

    ''' <summary>A GetStatus answer with the server down; <paramref name="members"/> are extra members of
    ''' <c>status</c> written as JSON (leading comma included).</summary>
    Private Shared Function StatusWire(members As String, Optional schema As Integer = 1) As String
        Return Q("{'schemaVersion':" & schema.ToString() & ",'ok':true,'status':{'running':false,'listenPort':0,'roots':[]," &
                 "'desiredPort':2222,'portSupported':true" & members & "}}")
    End Function

    Private Shared Function StatusOf(members As String) As WorkerStatus
        Return WorkerIpc.ParseResponse(StatusWire(members)).Status
    End Function

    ' --- the field itself --------------------------------------------------------------------------

    <Fact>
    Public Sub KindAbsent_IsNotSent_AndReadsAsEmpty()
        Dim s As WorkerStatus = StatusOf("")
        Assert.False(s.HasLastStartErrorKind)
        Assert.Equal(StartErrorKinds.None, s.LastStartErrorKind)
        Assert.Equal("", s.LastStartErrorKindRaw)
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("in-use")>
    <InlineData("excluded-range")>
    <InlineData("other")>
    Public Sub EachKnownKind_Binds_AsItself(token As String)
        Dim s As WorkerStatus = StatusOf(",'lastStartErrorKind':'" & token & "'")
        Assert.True(s.HasLastStartErrorKind)
        Assert.Equal(token, s.LastStartErrorKind)
        Assert.Equal(token, s.LastStartErrorKindRaw)
    End Sub

    <Fact>
    Public Sub TheFourTokens_AreTheContractsAsciiStrings()
        Assert.Equal("", StartErrorKinds.None)
        Assert.Equal("in-use", StartErrorKinds.InUse)
        Assert.Equal("excluded-range", StartErrorKinds.ExcludedRange)
        Assert.Equal("other", StartErrorKinds.Other)
    End Sub

    ''' <summary>VERSIONING section 4 rule 4: an unknown enum value degrades to the documented default,
    ''' and the contract documents it as <c>other</c>. Matching is literal and case-sensitive, like the
    ''' marker token. The token as received is kept.</summary>
    <Theory>
    <InlineData("weird")>
    <InlineData("IN-USE")>
    <InlineData("In-Use")>
    <InlineData(" in-use")>
    <InlineData("in-use ")>
    <InlineData("excluded_range")>
    <InlineData("access-denied")>
    Public Sub AnUnknownKindString_ReadsAsOther_TokenKept(token As String)
        Dim s As WorkerStatus = StatusOf(",'lastStartErrorKind':'" & token & "'")
        Assert.True(s.HasLastStartErrorKind)
        Assert.Equal(StartErrorKinds.Other, s.LastStartErrorKind)
        Assert.Equal(token, s.LastStartErrorKindRaw)
    End Sub

    ''' <summary>A value that is not a JSON string - a number, a boolean, an object, an array - reads as
    ''' <c>other</c> with its JSON text kept, and the REST of the answer still binds: it is not "an answer
    ''' we could not read".</summary>
    <Theory>
    <InlineData("7")>
    <InlineData("-1.5")>
    <InlineData("true")>
    <InlineData("false")>
    <InlineData("{'a':1}")>
    <InlineData("['in-use']")>
    Public Sub ANonStringKind_ReadsAsOther_TokenKept_AndTheAnswerStillBinds(literal As String)
        Dim json As String = Q(literal)
        Dim response As WorkerResponse = WorkerIpc.ParseResponse(StatusWire(",'lastStartErrorKind':" & literal))
        Assert.True(response.Ok)
        Dim s As WorkerStatus = response.Status
        Assert.True(s.HasLastStartErrorKind)
        Assert.Equal(StartErrorKinds.Other, s.LastStartErrorKind)
        Assert.Equal(json, s.LastStartErrorKindRaw)
        Assert.Equal(2222, s.DesiredPort)
        Assert.True(s.PortSupported)
        Assert.NotNull(s.Roots)
    End Sub

    <Fact>
    Public Sub ALongNonStringKind_KeepsOnlyItsFirst64Characters()
        Dim s As WorkerStatus = StatusOf(",'lastStartErrorKind':{'a':'" & New String("x"c, 500) & "'}")
        Assert.Equal(StartErrorKinds.Other, s.LastStartErrorKind)
        Assert.Equal(64, s.LastStartErrorKindRaw.Length)
    End Sub

    ''' <summary>JSON null is the same as absent: the host then reads the text, as it did before the field.</summary>
    <Fact>
    Public Sub ANullKind_IsTheSameAsAbsent()
        Dim s As WorkerStatus = StatusOf(",'lastStartErrorKind':null")
        Assert.False(s.HasLastStartErrorKind)
        Assert.Equal(StartErrorKinds.None, s.LastStartErrorKind)
    End Sub

    <Fact>
    Public Sub SettingTheKindInCode_MarksItAsSent_AndNormalizes()
        Dim s As New WorkerStatus With {.LastStartErrorKind = "whatever"}
        Assert.True(s.HasLastStartErrorKind)
        Assert.Equal(StartErrorKinds.Other, s.LastStartErrorKind)
        s.LastStartErrorKind = Nothing
        Assert.False(s.HasLastStartErrorKind)
        Assert.Equal(StartErrorKinds.None, s.LastStartErrorKind)
    End Sub

    ' --- what the host concludes -------------------------------------------------------------------

    Private Shared Function Warning(members As String) As String
        Return ShareController.PortWarning(StatusOf(members), 2222)
    End Function

    ''' <summary>A worker that does not send the field (the vendored one) is read exactly as before: the
    ''' literal token, then "bind:", then the free text.</summary>
    <Fact>
    Public Sub WithoutTheField_TheTextFallbackIsUnchanged()
        Dim marked As String = Warning(",'lastStartError':'port 2222 unavailable [excluded-range]: bind: denied'")
        Assert.Contains(ShareText.PortExcludedRangeHint(), marked)
        Assert.Contains(ShareText.PortBusyText(2222), marked)

        Dim busy As String = Warning(",'lastStartError':'port 2222 unavailable: bind: address already in use'")
        Assert.Contains(ShareText.PortBusyText(2222), busy)
        Assert.DoesNotContain(ShareText.PortExcludedRangeHint(), busy)

        Dim other As String = Warning(",'lastStartError':'port 2222 unavailable: duplicate root name Photos'")
        Assert.Contains("duplicate root name", other)
        Assert.DoesNotContain(ShareText.PortBusyText(2222), other)

        Assert.Equal("", Warning(""))
    End Sub

    <Fact>
    Public Sub TheKindIsRead_InPreferenceToTheText()
        ' in-use: the marker in the text is not consulted
        Dim inUse As String = Warning(",'lastStartErrorKind':'in-use','lastStartError':'port 2222 unavailable [excluded-range]: bind: x'")
        Assert.Contains(ShareText.PortBusyText(2222), inUse)
        Assert.DoesNotContain(ShareText.PortExcludedRangeHint(), inUse)

        ' excluded-range: no marker needed in the text, and no "bind:" either
        Dim excluded As String = Warning(",'lastStartErrorKind':'excluded-range','lastStartError':'start failed'")
        Assert.Contains(ShareText.PortBusyText(2222), excluded)
        Assert.Contains(ShareText.PortExcludedRangeHint(), excluded)

        ' other: a text that says "bind:" is not a port problem when the worker said it is not
        Dim other As String = Warning(",'lastStartErrorKind':'other','lastStartError':'port 2222 unavailable: bind: x'")
        Assert.DoesNotContain(ShareText.PortBusyText(2222), other)
        Assert.Contains("bind: x", other)

        ' an unknown token is other, so the same
        Dim unknown As String = Warning(",'lastStartErrorKind':'weird','lastStartError':'port 2222 unavailable: bind: y'")
        Assert.DoesNotContain(ShareText.PortBusyText(2222), unknown)
        Assert.Contains("bind: y", unknown)

        ' empty = no start error, whatever the text still says
        Assert.Equal("", Warning(",'lastStartErrorKind':'','lastStartError':'port 2222 unavailable: bind: z'"))
    End Sub

    <Fact>
    Public Sub TheKind_NeedsNoTextAtAll()
        Dim busy As String = Warning(",'lastStartErrorKind':'in-use'")
        Assert.Contains(ShareText.PortBusyText(2222), busy)
        Dim other As String = Warning(",'lastStartErrorKind':'other'")
        Assert.NotEqual("", other)
        Assert.DoesNotContain(ShareText.PortBusyText(2222), other)
    End Sub

    <Fact>
    Public Sub AServerThatRuns_HasNothingToSay_WhateverTheKind()
        Dim wire As String = Q("{'schemaVersion':1,'ok':true,'status':{'running':true,'listenPort':2222,'roots':[]," &
                               "'lastStartErrorKind':'in-use','lastStartError':'old'}}")
        Assert.Equal("", ShareController.PortWarning(WorkerIpc.ParseResponse(wire).Status, 2222))
    End Sub

    <Fact>
    Public Sub StartErrorKindOf_AnswersFromTheFieldOnlyWhenSent()
        Assert.Equal(StartErrorKinds.None, ShareController.StartErrorKindOf(Nothing))
        Assert.Equal(StartErrorKinds.ExcludedRange, ShareController.StartErrorKindFromText("x" & ShareController.ExcludedRangeMarker))
        Assert.Equal(StartErrorKinds.InUse, ShareController.StartErrorKindFromText("listen tcp :1: bind: busy"))
        Assert.Equal(StartErrorKinds.Other, ShareController.StartErrorKindFromText("duplicate root name"))
        Assert.Equal(StartErrorKinds.None, ShareController.StartErrorKindFromText(""))
        Assert.Equal(StartErrorKinds.None, ShareController.StartErrorKindFromText(Nothing))
    End Sub

    ' --- a type drift of lastStartError ------------------------------------------------------------

    ''' <summary>VERSIONING section 4: a type drift of a KNOWN field is read as its default. A worker that
    ''' sent a non-string <c>lastStartError</c> used to make the whole answer "unreadable" (Malformed) -
    ''' status, roots and ports lost with it.</summary>
    <Theory>
    <InlineData("5")>
    <InlineData("-1")>
    <InlineData("true")>
    <InlineData("false")>
    <InlineData("{'code':5}")>
    <InlineData("['a','b']")>
    Public Sub ANonStringLastStartError_IsReadAsAbsent_NotMalformed(literal As String)
        Dim response As WorkerResponse = WorkerIpc.ParseResponse(StatusWire(",'lastStartError':" & literal))
        Assert.True(response.Ok)
        Assert.Null(response.Status.LastStartError)
        Assert.Equal(2222, response.Status.DesiredPort)
        Assert.NotNull(response.Status.Roots)
    End Sub

    <Fact>
    Public Sub AStringLastStartError_StillBindsUnchanged()
        Dim s As WorkerStatus = StatusOf(",'lastStartError':'port 2222 unavailable: bind: busy'")
        Assert.Equal("port 2222 unavailable: bind: busy", s.LastStartError)
    End Sub

    ''' <summary>The drifted text does not cost the structured kind: the kind still says what happened.</summary>
    <Fact>
    Public Sub ANonStringLastStartError_NextToAKind_StillGivesTheKindsVerdict()
        Dim text As String = Warning(",'lastStartError':5,'lastStartErrorKind':'in-use'")
        Assert.Contains(ShareText.PortBusyText(2222), text)
    End Sub

    ' --- a higher schemaVersion is refused cleanly, by name ----------------------------------------

    <Theory>
    <InlineData(2)>
    <InlineData(3)>
    <InlineData(4)>
    <InlineData(99)>
    Public Sub AHigherSchemaVersion_IsRefusedCleanly_ThroughParseResponse(version As Integer)
        Dim ex As WorkerVersionRefusedException = Assert.Throws(Of WorkerVersionRefusedException)(
            Sub() WorkerIpc.ParseResponse(StatusWire(",'lastStartErrorKind':'in-use'", version)))
        Assert.Equal(version, ex.WorkerVersion)
        Assert.Equal(WorkerIpc.SchemaVersion, ex.ClientVersion)
    End Sub

    ''' <summary>A newer worker whose answer ALSO does not bind (a field whose type changed in that very
    ''' version) is still refused by name, never reported as an unreadable answer.</summary>
    <Fact>
    Public Sub AHigherSchemaVersion_WithABodyThatDoesNotBind_IsStillRefusedByName()
        Dim wire As String = Q("{'schemaVersion':4,'ok':true,'status':{'running':'yes','roots':'none'}}")
        Dim ex As WorkerVersionRefusedException = Assert.Throws(Of WorkerVersionRefusedException)(Sub() WorkerIpc.ParseResponse(wire))
        Assert.Equal(4, ex.WorkerVersion)
    End Sub

    <Fact>
    Public Sub AHigherSchemaVersion_IsRefusedAgainstTheDiskAndPinSchemasToo()
        Dim diskWire As String = Q("{'schemaVersion':4,'ok':true}")
        Assert.Throws(Of WorkerVersionRefusedException)(Sub() WorkerIpc.ParseResponse(diskWire, 2))
        Assert.Throws(Of WorkerVersionRefusedException)(Sub() WorkerIpc.ParseResponse(diskWire, 3))
    End Sub

    <Fact>
    Public Sub ABodyThatDoesNotBind_AtOurOwnVersion_IsMalformed_NotARefusal()
        Dim wire As String = Q("{'schemaVersion':1,'ok':true,'status':{'running':'yes'}}")
        Dim ex As WorkerExchangeException = Assert.Throws(Of WorkerExchangeException)(Sub() WorkerIpc.ParseResponse(wire))
        Assert.Equal(WorkerExchangeProblem.Malformed, ex.Problem)
    End Sub

End Class
