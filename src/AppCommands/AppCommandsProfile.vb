Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Text

' The APP-COMMANDS request-response profile (contract APP-COMMANDS 0.1, rules 1 to 20), as one pure
' library: no window, no registry, no localization, no runtime-specific API. The viewer links it today;
' the Share Manager links the same file (ticket L0316) and supplies its own command table. Everything a
' person reads is produced by the caller from the reason tokens here, in the product's language.
Namespace AppCommands

    ''' <summary>A JSON object that keeps the order its members were added in, so a generated
    ''' document is byte-stable.</summary>
    Friend NotInheritable Class JsonObject
        Inherits List(Of KeyValuePair(Of String, Object))

        Friend Overloads Sub Add(name As String, value As Object)
            MyBase.Add(New KeyValuePair(Of String, Object)(name, value))
        End Sub

        Friend Function TryGet(name As String, ByRef value As Object) As Boolean
            For Each pair As KeyValuePair(Of String, Object) In Me
                If String.Equals(pair.Key, name, StringComparison.Ordinal) Then
                    value = pair.Value
                    Return True
                End If
            Next
            value = Nothing
            Return False
        End Function

        Friend Function GetString(name As String) As String
            Dim value As Object = Nothing
            If TryGet(name, value) Then Return TryCast(value, String)
            Return Nothing
        End Function

        Friend Function GetInt(name As String, fallback As Integer) As Integer
            Dim value As Object = Nothing
            If TryGet(name, value) AndAlso TypeOf value Is Long Then
                Dim n As Long = DirectCast(value, Long)
                If n >= Integer.MinValue AndAlso n <= Integer.MaxValue Then Return CInt(n)
            End If
            Return fallback
        End Function

        Friend Function GetBool(name As String, fallback As Boolean) As Boolean
            Dim value As Object = Nothing
            If TryGet(name, value) AndAlso TypeOf value Is Boolean Then Return DirectCast(value, Boolean)
            Return fallback
        End Function
    End Class

    ''' <summary>The JSON this profile needs, and no more: a writer whose output is deterministic
    ''' (members in insertion order, two-space indent, LF) and a bounded reader. Values are
    ''' <see cref="JsonObject"/>, <c>List(Of Object)</c>, String, Long, Double, Boolean or Nothing.</summary>
    Friend Module CommandJson

        Private Const MaxDepth As Integer = 32

        Friend Function Quote(text As String) As String
            Dim sb As New StringBuilder(If(text, String.Empty).Length + 2)
            sb.Append(""""c)
            For Each c As Char In If(text, String.Empty)
                Select Case c
                    Case """"c : sb.Append("\""")
                    Case "\"c : sb.Append("\\")
                    Case ControlChars.Lf : sb.Append("\n")
                    Case ControlChars.Cr : sb.Append("\r")
                    Case ControlChars.Tab : sb.Append("\t")
                    Case Else
                        If AscW(c) < &H20 OrElse c = ChrW(&H2028) OrElse c = ChrW(&H2029) Then
                            sb.Append("\u").Append(AscW(c).ToString("x4", CultureInfo.InvariantCulture))
                        Else
                            sb.Append(c)
                        End If
                End Select
            Next
            sb.Append(""""c)
            Return sb.ToString()
        End Function

        ''' <summary>One line, no whitespace between tokens - the request and response lines of rule 7.</summary>
        Friend Function ToLine(value As Object) As String
            Dim sb As New StringBuilder()
            Write(sb, value, -1, 0)
            Return sb.ToString()
        End Function

        ''' <summary>Indented with two spaces, LF line ends, a final LF - the shape of the files under docs/.</summary>
        Friend Function ToIndented(value As Object) As String
            Dim sb As New StringBuilder()
            Write(sb, value, 0, 0)
            sb.Append(ControlChars.Lf)
            Return sb.ToString()
        End Function

        Private Sub NewLine(sb As StringBuilder, indent As Integer, level As Integer)
            If indent < 0 Then Return
            sb.Append(ControlChars.Lf)
            sb.Append(" "c, 2 * level)
        End Sub

        Private Sub Write(sb As StringBuilder, value As Object, indent As Integer, level As Integer)
            If value Is Nothing Then
                sb.Append("null")
            ElseIf TypeOf value Is String Then
                sb.Append(Quote(DirectCast(value, String)))
            ElseIf TypeOf value Is Boolean Then
                sb.Append(If(DirectCast(value, Boolean), "true", "false"))
            ElseIf TypeOf value Is Integer Then
                sb.Append(DirectCast(value, Integer).ToString(CultureInfo.InvariantCulture))
            ElseIf TypeOf value Is Long Then
                sb.Append(DirectCast(value, Long).ToString(CultureInfo.InvariantCulture))
            ElseIf TypeOf value Is Double Then
                sb.Append(DirectCast(value, Double).ToString("R", CultureInfo.InvariantCulture))
            ElseIf TypeOf value Is JsonObject Then
                Dim obj As JsonObject = DirectCast(value, JsonObject)
                If obj.Count = 0 Then
                    sb.Append("{}")
                    Return
                End If
                sb.Append("{"c)
                For i As Integer = 0 To obj.Count - 1
                    If i > 0 Then sb.Append(","c)
                    NewLine(sb, indent, level + 1)
                    sb.Append(Quote(obj(i).Key)).Append(If(indent < 0, ":", ": "))
                    Write(sb, obj(i).Value, indent, level + 1)
                Next
                NewLine(sb, indent, level)
                sb.Append("}"c)
            ElseIf TypeOf value Is IEnumerable(Of Object) Then
                Dim items As New List(Of Object)(DirectCast(value, IEnumerable(Of Object)))
                If items.Count = 0 Then
                    sb.Append("[]")
                    Return
                End If
                sb.Append("["c)
                For i As Integer = 0 To items.Count - 1
                    If i > 0 Then sb.Append(","c)
                    NewLine(sb, indent, level + 1)
                    Write(sb, items(i), indent, level + 1)
                Next
                NewLine(sb, indent, level)
                sb.Append("]"c)
            Else
                Throw New ArgumentException("not a JSON value: " & value.GetType().Name)
            End If
        End Sub

        ''' <summary>Parses one JSON value; Nothing and False on anything malformed, never an exception.</summary>
        Friend Function TryParse(text As String, ByRef value As Object) As Boolean
            value = Nothing
            If text Is Nothing Then Return False
            Dim pos As Integer = 0
            Try
                Dim parsed As Object = ParseValue(text, pos, 0)
                SkipSpace(text, pos)
                If pos <> text.Length Then Return False
                value = parsed
                Return True
            Catch ex As FormatException
                Return False
            End Try
        End Function

        Private Sub SkipSpace(text As String, ByRef pos As Integer)
            While pos < text.Length AndAlso (text(pos) = " "c OrElse text(pos) = ControlChars.Tab OrElse text(pos) = ControlChars.Cr OrElse text(pos) = ControlChars.Lf)
                pos += 1
            End While
        End Sub

        Private Function ParseValue(text As String, ByRef pos As Integer, depth As Integer) As Object
            If depth > MaxDepth Then Throw New FormatException("too deep")
            SkipSpace(text, pos)
            If pos >= text.Length Then Throw New FormatException("end of input")
            Dim c As Char = text(pos)
            Select Case c
                Case "{"c
                    pos += 1
                    Dim obj As New JsonObject()
                    SkipSpace(text, pos)
                    If pos < text.Length AndAlso text(pos) = "}"c Then
                        pos += 1
                        Return obj
                    End If
                    Do
                        SkipSpace(text, pos)
                        If pos >= text.Length OrElse text(pos) <> """"c Then Throw New FormatException("member name")
                        Dim name As String = ParseString(text, pos)
                        SkipSpace(text, pos)
                        If pos >= text.Length OrElse text(pos) <> ":"c Then Throw New FormatException("colon")
                        pos += 1
                        obj.Add(name, ParseValue(text, pos, depth + 1))
                        SkipSpace(text, pos)
                        If pos >= text.Length Then Throw New FormatException("end of object")
                        If text(pos) = ","c Then
                            pos += 1
                        ElseIf text(pos) = "}"c Then
                            pos += 1
                            Return obj
                        Else
                            Throw New FormatException("object separator")
                        End If
                    Loop
                Case "["c
                    pos += 1
                    Dim items As New List(Of Object)()
                    SkipSpace(text, pos)
                    If pos < text.Length AndAlso text(pos) = "]"c Then
                        pos += 1
                        Return items
                    End If
                    Do
                        items.Add(ParseValue(text, pos, depth + 1))
                        SkipSpace(text, pos)
                        If pos >= text.Length Then Throw New FormatException("end of array")
                        If text(pos) = ","c Then
                            pos += 1
                        ElseIf text(pos) = "]"c Then
                            pos += 1
                            Return items
                        Else
                            Throw New FormatException("array separator")
                        End If
                    Loop
                Case """"c
                    Return ParseString(text, pos)
                Case "t"c
                    Expect(text, pos, "true")
                    Return True
                Case "f"c
                    Expect(text, pos, "false")
                    Return False
                Case "n"c
                    Expect(text, pos, "null")
                    Return Nothing
                Case Else
                    Return ParseNumber(text, pos)
            End Select
        End Function

        Private Sub Expect(text As String, ByRef pos As Integer, word As String)
            If String.CompareOrdinal(text, pos, word, 0, word.Length) <> 0 Then Throw New FormatException(word)
            pos += word.Length
        End Sub

        Private Function ParseString(text As String, ByRef pos As Integer) As String
            pos += 1
            Dim sb As New StringBuilder()
            While pos < text.Length
                Dim c As Char = text(pos)
                If c = """"c Then
                    pos += 1
                    Return sb.ToString()
                End If
                If AscW(c) < &H20 Then Throw New FormatException("control character in a string")
                If c = "\"c Then
                    pos += 1
                    If pos >= text.Length Then Throw New FormatException("escape")
                    Select Case text(pos)
                        Case """"c : sb.Append(""""c)
                        Case "\"c : sb.Append("\"c)
                        Case "/"c : sb.Append("/"c)
                        Case "b"c : sb.Append(ChrW(8))
                        Case "f"c : sb.Append(ChrW(12))
                        Case "n"c : sb.Append(ControlChars.Lf)
                        Case "r"c : sb.Append(ControlChars.Cr)
                        Case "t"c : sb.Append(ControlChars.Tab)
                        Case "u"c
                            If pos + 4 >= text.Length Then Throw New FormatException("unicode escape")
                            Dim code As Integer
                            If Not Integer.TryParse(text.Substring(pos + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, code) Then
                                Throw New FormatException("unicode escape")
                            End If
                            sb.Append(ChrW(code))
                            pos += 4
                        Case Else
                            Throw New FormatException("escape")
                    End Select
                    pos += 1
                Else
                    sb.Append(c)
                    pos += 1
                End If
            End While
            Throw New FormatException("unterminated string")
        End Function

        Private Function ParseNumber(text As String, ByRef pos As Integer) As Object
            Dim start As Integer = pos
            While pos < text.Length AndAlso "+-0123456789.eE".IndexOf(text(pos)) >= 0
                pos += 1
            End While
            If pos = start Then Throw New FormatException("value")
            Dim token As String = text.Substring(start, pos - start)
            Dim whole As Long
            If Long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, whole) Then Return whole
            Dim real As Double
            If Double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, real) Then Return real
            Throw New FormatException("number")
        End Function
    End Module

    ''' <summary>Exit codes of rule 11 (the portfolio table, CLI-EVENT-STREAM rule 11).</summary>
    Friend Module CommandExit
        Friend Const Done As Integer = 0
        Friend Const Failed As Integer = 1
        Friend Const NotProven As Integer = 2
        Friend Const Usage As Integer = 10
        Friend Const NotReady As Integer = 11
        Friend Const Permission As Integer = 12
        Friend Const Resources As Integer = 13
        Friend Const Hardware As Integer = 14
        Friend Const Network As Integer = 15
        Friend Const Stopped As Integer = 16

        Friend Function IsKnown(code As Integer) As Boolean
            Return code = 0 OrElse code = 1 OrElse code = 2 OrElse (code >= 10 AndAlso code <= 16)
        End Function
    End Module

    ''' <summary>The closed reason tokens of rule 12 and the status and exit each one carries.</summary>
    Friend Module CommandReasons
        Friend Const UnknownVerb As String = "unknown-verb"
        Friend Const BadArgument As String = "bad-argument"
        Friend Const TooLarge As String = "too-large"
        Friend Const Schema As String = "schema"
        Friend Const SwitchOff As String = "switch-off"
        Friend Const Elevation As String = "elevation"
        Friend Const Busy As String = "busy"
        Friend Const NotRunning As String = "not-running"
        Friend Const TargetMissing As String = "target-missing"
        Friend Const Timeout As String = "timeout"
        Friend Const Cancelled As String = "cancelled"
        ''' <summary>Item M: the command ran and the operation failed with no more exact token (<c>failed</c>, exit 1).
        ''' The wire word is <c>error</c>; the name avoids the VB keyword.</summary>
        Friend Const OperationFailed As String = "error"
        ''' <summary>Item M: the target volume or a resource budget is exhausted (<c>failed</c>, exit 13).</summary>
        Friend Const NoSpace As String = "no-space"
        ''' <summary>Item M: a file or device the verb needed could not be read or written (<c>failed</c>, exit 14).</summary>
        Friend Const IoError As String = "io-error"
        ''' <summary>Item M: the network or a remote share the verb needed did not answer (<c>failed</c>, exit 15).</summary>
        Friend Const Network As String = "network"
        ''' <summary>Item L: a verb's <c>data</c> is over the 64 KiB response bound and the verb is not paged
        ''' (<c>failed</c>, exit 1).</summary>
        Friend Const ResponseTooLarge As String = "response-too-large"

        Friend ReadOnly All As String() = {UnknownVerb, BadArgument, TooLarge, Schema, SwitchOff, Elevation, Busy,
                                           NotRunning, TargetMissing, Timeout, Cancelled, OperationFailed, NoSpace, IoError, Network, ResponseTooLarge}

        Friend Function StatusOf(reason As String) As String
            Select Case reason
                Case Busy : Return CommandStatus.Busy
                Case TargetMissing, Timeout, Cancelled, OperationFailed, NoSpace, IoError, Network, ResponseTooLarge : Return CommandStatus.Failed
                Case UnknownVerb, BadArgument, TooLarge, Schema, SwitchOff, Elevation, NotRunning : Return CommandStatus.Refused
                Case Else : Return CommandStatus.Failed
            End Select
        End Function

        Friend Function ExitOf(reason As String) As Integer
            Select Case reason
                Case UnknownVerb, BadArgument, TooLarge : Return CommandExit.Usage
                Case Schema, Timeout : Return CommandExit.NotProven
                Case SwitchOff, Elevation : Return CommandExit.Permission
                Case Busy, NotRunning, TargetMissing : Return CommandExit.NotReady
                Case NoSpace : Return CommandExit.Resources
                Case IoError : Return CommandExit.Hardware
                Case Network : Return CommandExit.Network
                Case Cancelled : Return CommandExit.Stopped
                Case Else : Return CommandExit.Failed
            End Select
        End Function
    End Module

    Friend Module CommandStatus
        Friend Const Done As String = "done"
        Friend Const Accepted As String = "accepted"
        Friend Const Refused As String = "refused"
        Friend Const Failed As String = "failed"
        Friend Const Busy As String = "busy"
    End Module

    ''' <summary>Risk classes of rule 13.</summary>
    Friend Module CommandRisk
        Friend Const Safe As String = "safe"
        Friend Const ReadsUserData As String = "reads-user-data"
        Friend Const WritesUserData As String = "writes-user-data"
        Friend Const LeavesMachine As String = "leaves-machine"
    End Module

    ''' <summary>Where a request comes from (rule 7 <c>origin</c>). Informational only: a window message cannot prove
    ''' its sender (APP-COMMANDS 0.2 item T.1), so whatever the line says, a receiver reads every request as <c>cli</c>
    ''' and the setting of rule 13 never looks at it. There is no <c>sibling</c> origin: a message of the product's own
    ''' other program is a shipped spelling and is covered by item F (<c>legacy</c>).</summary>
    Friend Module CommandOrigin
        Friend Const Cli As String = "cli"
    End Module

    Friend Module CommandBounds
        ''' <summary>Rule 3: a whole request as UTF-8.</summary>
        Friend Const MaxRequestBytes As Integer = 64 * 1024
        ''' <summary>Item L: a whole response line as UTF-8, the same bound as a request.</summary>
        Friend Const MaxResponseBytes As Integer = 64 * 1024
        ''' <summary>Rule 3: one argument as UTF-8.</summary>
        Friend Const MaxArgumentBytes As Integer = 32 * 1024
        ''' <summary>Rule 8: the delivery bound.</summary>
        Friend Const DeliveryMs As Integer = 1000
        ''' <summary>Rule 8: completion when a verb states none, and the most any verb may state.</summary>
        Friend Const DefaultCompletionMs As Integer = 10000
        Friend Const MaxCompletionMs As Integer = 600000
        ''' <summary>The largest secret a <c>--secret-file</c> may hold.</summary>
        Friend Const MaxSecretBytes As Integer = 32 * 1024
    End Module

    ' ---------------------------------------------------------------------------------------------
    ' The manifest (rule 18)
    ' ---------------------------------------------------------------------------------------------

    Friend NotInheritable Class CommandArg
        Friend Property Name As String
        ''' <summary>string, path, int, bool, enum or guid.</summary>
        Friend Property Kind As String
        Friend Property Required As Boolean
        ''' <summary>The default as the manifest shows it; Nothing omits the member.</summary>
        Friend Property DefaultValue As Object
        Friend Property Values As String()
        Friend Property Description As String
        ''' <summary>A value after the verb rather than a <c>--name</c> option. Not a manifest member: the
        ''' synopsis shows it; the parser needs it.</summary>
        Friend Property Positional As Boolean
        ''' <summary>Item H.2: the value may carry a credential - never written to a log line, an event, a
        ''' <c>data</c> member or the <c>message</c>; written to the manifest only when true.</summary>
        Friend Property Sensitive As Boolean

        Friend Function ToJson() As JsonObject
            Dim o As New JsonObject()
            o.Add("name", Name)
            o.Add("kind", Kind)
            o.Add("required", Required)
            If DefaultValue IsNot Nothing Then o.Add("default", DefaultValue)
            If Values IsNot Nothing AndAlso Values.Length > 0 Then o.Add("values", ToObjects(Values))
            If Sensitive Then o.Add("sensitive", True)
            o.Add("description", Description)
            Return o
        End Function

        Friend Shared Function ToObjects(items As IEnumerable(Of String)) As List(Of Object)
            Dim list As New List(Of Object)()
            For Each item As String In items
                list.Add(item)
            Next
            Return list
        End Function
    End Class

    Friend NotInheritable Class CommandReturn
        Friend Property Name As String
        Friend Property Kind As String
        Friend Property Description As String

        Friend Function ToJson() As JsonObject
            Dim o As New JsonObject()
            o.Add("name", Name)
            o.Add("kind", Kind)
            o.Add("description", Description)
            Return o
        End Function
    End Class

    Friend NotInheritable Class CommandExitCode
        Friend Property Code As Integer
        Friend Property Meaning As String

        Friend Function ToJson() As JsonObject
            Dim o As New JsonObject()
            o.Add("code", Code)
            o.Add("meaning", Meaning)
            Return o
        End Function
    End Class

    Friend NotInheritable Class CommandExample
        ''' <summary>cmd or powershell.</summary>
        Friend Property Shell As String
        Friend Property Line As String
        Friend Property Expect As String

        Friend Function ToJson() As JsonObject
            Dim o As New JsonObject()
            o.Add("shell", Shell)
            o.Add("line", Line)
            o.Add("expect", Expect)
            Return o
        End Function
    End Class

    ''' <summary>What an alias keeps as it shipped (item E): the options it implies, whether it is a legacy
    ''' spelling (item F), whether it returns an answer. An alias with no entry is a plain string: the same words, an
    ''' answer, not legacy.</summary>
    Friend NotInheritable Class CommandAliasInfo
        ''' <summary>Options, as typed, the spelling implies; each is an option the canonical verb has.</summary>
        Friend Property Implies As String()
        ''' <summary>The spelling shipped before the product wrote its adoption row.</summary>
        Friend Property Legacy As Boolean
        ''' <summary><c>False</c> when the spelling keeps the start it always made and returns no answer of rule 11.</summary>
        Friend Property Answers As Boolean = True

        Friend Function ToJson(spelling As String) As JsonObject
            Dim o As New JsonObject()
            o.Add("spelling", spelling)
            If Implies IsNot Nothing AndAlso Implies.Length > 0 Then o.Add("implies", CommandArg.ToObjects(Implies))
            If Legacy Then o.Add("legacy", True)
            If Not Answers Then o.Add("answers", False)
            Return o
        End Function
    End Class

    Friend NotInheritable Class CommandSpec
        Friend Property Name As String
        ''' <summary>The spellings, as typed. What each keeps is in <see cref="AliasInfo"/>.</summary>
        Friend Property Aliases As New List(Of String)()
        ''' <summary>Item E: the alias object, by spelling; a spelling with no entry is a plain string.</summary>
        Friend ReadOnly AliasInfo As New Dictionary(Of String, CommandAliasInfo)(StringComparer.Ordinal)
        ''' <summary>Item C: <c>always</c> (when absent) or <c>never</c>: a <c>never</c> verb is done by the process that
        ''' received it, touching neither the first instance's pipe, window message nor mutex.</summary>
        Friend Property Forward As String
        ''' <summary>Item A.3: the executable whose bare command line this verb serves; written only when set.</summary>
        Friend Property DefaultFor As String
        Friend Property Summary As String
        Friend Property Synopsis As String
        Friend Property Args As New List(Of CommandArg)()
        Friend Property Returns As New List(Of CommandReturn)()
        Friend Property Risk As String = CommandRisk.Safe
        ''' <summary>start, oneshot or refuse (rule 10); there is no default.</summary>
        Friend Property ColdStart As String
        Friend Property Wait As String = "done"
        Friend Property TimeoutMs As Integer = CommandBounds.DefaultCompletionMs
        Friend Property Interactive As Boolean
        Friend Property Idempotent As Boolean
        Friend Property ExitCodes As New List(Of CommandExitCode)()
        Friend Property Examples As New List(Of CommandExample)()
        Friend Property Since As String
        ''' <summary>Item R.4: groups of argument names of which exactly one is given; written only when not empty.</summary>
        Friend Property OneOf As New List(Of String())()

        ''' <summary>Add a spelling with what it keeps (item E).</summary>
        Friend Sub AddAlias(spelling As String, info As CommandAliasInfo)
            Aliases.Add(spelling)
            If info IsNot Nothing Then AliasInfo(spelling) = info
        End Sub

        ''' <summary>Item F.2: a first instance honours <c>legacy</c> only for a verb that lists at least one legacy alias.</summary>
        Friend ReadOnly Property HasLegacyAlias As Boolean
            Get
                For Each info As CommandAliasInfo In AliasInfo.Values
                    If info.Legacy Then Return True
                Next
                Return False
            End Get
        End Property

        Friend Function AliasesToObjects() As List(Of Object)
            Dim list As New List(Of Object)()
            For Each spelling As String In Aliases
                Dim info As CommandAliasInfo = Nothing
                If AliasInfo.TryGetValue(spelling, info) Then
                    list.Add(info.ToJson(spelling))
                Else
                    list.Add(spelling)
                End If
            Next
            Return list
        End Function

        Friend Function FindOption(optionName As String) As CommandArg
            For Each a As CommandArg In Args
                If Not a.Positional AndAlso String.Equals(a.Name, optionName, StringComparison.OrdinalIgnoreCase) Then Return a
            Next
            Return Nothing
        End Function

        Friend Function PositionalArgs() As List(Of CommandArg)
            Dim list As New List(Of CommandArg)()
            For Each a As CommandArg In Args
                If a.Positional Then list.Add(a)
            Next
            Return list
        End Function

        ''' <summary>The completion bound of rule 8, clamped to what the contract allows.</summary>
        Friend Function CompletionMs() As Integer
            If TimeoutMs <= 0 Then Return CommandBounds.DefaultCompletionMs
            Return Math.Min(TimeoutMs, CommandBounds.MaxCompletionMs)
        End Function

        Friend Function ToJson() As JsonObject
            Dim o As New JsonObject()
            o.Add("name", Name)
            o.Add("aliases", AliasesToObjects())
            If Not String.IsNullOrEmpty(Forward) Then o.Add("forward", Forward)
            If Not String.IsNullOrEmpty(DefaultFor) Then o.Add("defaultFor", DefaultFor)
            o.Add("summary", Summary)
            o.Add("synopsis", Synopsis)
            Dim argList As New List(Of Object)()
            For Each a As CommandArg In Args
                argList.Add(a.ToJson())
            Next
            o.Add("args", argList)
            If OneOf.Count > 0 Then
                Dim groups As New List(Of Object)()
                For Each g As String() In OneOf
                    groups.Add(CommandArg.ToObjects(g))
                Next
                o.Add("oneOf", groups)
            End If
            Dim returnList As New List(Of Object)()
            For Each r As CommandReturn In Returns
                returnList.Add(r.ToJson())
            Next
            o.Add("returns", returnList)
            o.Add("risk", Risk)
            o.Add("coldStart", ColdStart)
            o.Add("wait", Wait)
            o.Add("timeoutMs", TimeoutMs)
            o.Add("interactive", Interactive)
            o.Add("idempotent", Idempotent)
            Dim exitList As New List(Of Object)()
            For Each e As CommandExitCode In ExitCodes
                exitList.Add(e.ToJson())
            Next
            o.Add("exitCodes", exitList)
            Dim exampleList As New List(Of Object)()
            For Each x As CommandExample In Examples
                exampleList.Add(x.ToJson())
            Next
            o.Add("examples", exampleList)
            o.Add("since", Since)
            Return o
        End Function
    End Class

    Friend NotInheritable Class CommandManifest
        Friend Property Product As String
        Friend Property Exe As String
        Friend Property CliVersion As String
        Friend Property Commands As New List(Of CommandSpec)()

        Friend Function Find(name As String) As CommandSpec
            For Each c As CommandSpec In Commands
                If String.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) Then Return c
            Next
            Return Nothing
        End Function

        Friend Function ToJson() As JsonObject
            Dim o As New JsonObject()
            o.Add("schemaVersion", 1)
            o.Add("product", Product)
            o.Add("exe", Exe)
            o.Add("cliVersion", CliVersion)
            Dim list As New List(Of Object)()
            For Each c As CommandSpec In Commands
                list.Add(c.ToJson())
            Next
            o.Add("commands", list)
            Return o
        End Function

        ''' <summary>The text of docs/cli-commands.json and of <c>commands --json</c>: one generator for both.</summary>
        Friend Function ToJsonText() As String
            Return CommandJson.ToIndented(ToJson())
        End Function

        ''' <summary>The readable list <c>commands</c> prints without <c>--json</c>.</summary>
        Friend Function ToReadableList() As String
            Dim sb As New StringBuilder()
            sb.Append(Product).Append(" - ").Append(Exe).Append(", command set ").Append(CliVersion).Append(ControlChars.Lf)
            For Each c As CommandSpec In Commands
                sb.Append("  ").Append(c.Synopsis).Append(ControlChars.Lf)
                sb.Append("      ").Append(c.Summary).Append(ControlChars.Lf)
            Next
            Return sb.ToString()
        End Function
    End Class

    ' ---------------------------------------------------------------------------------------------
    ' Parsing (rules 1 and 3)
    ' ---------------------------------------------------------------------------------------------

    Friend NotInheritable Class ParsedCommand
        Friend Property Spec As CommandSpec
        ''' <summary>Option values by option name, in the order typed; a bool option holds "true" or "false".</summary>
        Friend Property Options As New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase)
        Friend Property Positionals As New List(Of String)()
        ''' <summary>The arguments after the verb words exactly as typed (rule 7 <c>args</c>).</summary>
        Friend Property RawArgs As New List(Of String)()

        Friend Function Has(optionName As String) As Boolean
            Return Options.ContainsKey(optionName)
        End Function

        Friend Function Flag(optionName As String) As Boolean
            Dim values As List(Of String) = Nothing
            If Not Options.TryGetValue(optionName, values) OrElse values.Count = 0 Then Return False
            Return values(values.Count - 1) = "true"
        End Function

        Friend Function Value(optionName As String) As String
            Dim values As List(Of String) = Nothing
            If Not Options.TryGetValue(optionName, values) OrElse values.Count = 0 Then Return Nothing
            Return values(values.Count - 1)
        End Function
    End Class

    ''' <summary>What the parser refused, as a reason token and the token the person typed wrong.
    ''' The caller turns it into a sentence in the product's language.</summary>
    Friend NotInheritable Class ParseRefusal
        Friend Property Reason As String
        ''' <summary>unknown-verb, unknown-option, missing-value, bad-value, missing-argument, extra-value,
        ''' request-too-large, argument-too-large: which sentence to say.</summary>
        Friend Property Detail As String
        ''' <summary>The offending word or option name (never a value: a value may be a secret or a path).</summary>
        Friend Property Token As String
    End Class

    Friend Module CommandLineParser

        Friend Const DetailUnknownVerb As String = "unknown-verb"
        Friend Const DetailUnknownOption As String = "unknown-option"
        Friend Const DetailMissingValue As String = "missing-value"
        Friend Const DetailBadValue As String = "bad-value"
        Friend Const DetailMissingArgument As String = "missing-argument"
        Friend Const DetailExtraValue As String = "extra-value"
        Friend Const DetailRequestTooLarge As String = "request-too-large"
        Friend Const DetailArgumentTooLarge As String = "argument-too-large"

        ''' <summary>Rule 1: one lower-case ASCII word <c>[a-z][a-z0-9-]*</c>, compared without regard to case.</summary>
        Friend Function IsVerbWord(token As String) As Boolean
            If String.IsNullOrEmpty(token) OrElse token.Length > 64 Then Return False
            Dim lower As String = token.ToLowerInvariant()
            If lower(0) < "a"c OrElse lower(0) > "z"c Then Return False
            For Each c As Char In lower
                If Not ((c >= "a"c AndAlso c <= "z"c) OrElse (c >= "0"c AndAlso c <= "9"c) OrElse c = "-"c) Then Return False
            Next
            Return True
        End Function

        ''' <summary>Item A.1: a first word is reserved when it begins a verb of the manifest or is the first word of an
        ''' alias spelling, compared without regard to case. A reserved word wins over a path of the same name; the
        ''' disk is never probed, so the grammar does not depend on the working directory. Any other first word is
        ''' not an unknown verb here: the product keeps reading it as the path it always was.</summary>
        Friend Function IsReservedFirstWord(first As String, manifest As CommandManifest) As Boolean
            If manifest Is Nothing OrElse Not IsVerbWord(first) Then Return False
            Dim lower As String = first.ToLowerInvariant()
            For Each spec As CommandSpec In manifest.Commands
                If String.Equals(FirstWord(spec.Name), lower, StringComparison.Ordinal) Then Return True
                For Each spelling As String In spec.Aliases
                    If String.Equals(FirstWord(spelling), lower, StringComparison.Ordinal) Then Return True
                Next
            Next
            Return False
        End Function

        Private Function FirstWord(text As String) As String
            If String.IsNullOrEmpty(text) Then Return String.Empty
            Dim space As Integer = text.IndexOf(" "c)
            Dim word As String = If(space < 0, text, text.Substring(0, space))
            Return word.ToLowerInvariant()
        End Function

        ''' <summary>Rule 3: the whole request and each argument within their bounds as UTF-8.</summary>
        Friend Function CheckSize(args As IList(Of String)) As ParseRefusal
            Dim total As Long = 0
            For Each a As String In args
                Dim bytes As Integer = Encoding.UTF8.GetByteCount(If(a, String.Empty))
                If bytes > CommandBounds.MaxArgumentBytes Then
                    Return New ParseRefusal With {.Reason = CommandReasons.TooLarge, .Detail = DetailArgumentTooLarge}
                End If
                total += bytes + 1
            Next
            If total > CommandBounds.MaxRequestBytes Then
                Return New ParseRefusal With {.Reason = CommandReasons.TooLarge, .Detail = DetailRequestTooLarge}
            End If
            Return Nothing
        End Function

        ''' <summary>Finds the verb (two words before one) and the number of tokens it takes.</summary>
        Friend Function ResolveVerb(args As IList(Of String), manifest As CommandManifest, ByRef consumed As Integer) As CommandSpec
            consumed = 0
            If args Is Nothing OrElse args.Count = 0 Then Return Nothing
            If args.Count >= 2 AndAlso IsVerbWord(args(0)) AndAlso IsVerbWord(args(1)) Then
                Dim two As CommandSpec = manifest.Find(args(0).ToLowerInvariant() & " " & args(1).ToLowerInvariant())
                If two IsNot Nothing Then
                    consumed = 2
                    Return two
                End If
            End If
            If IsVerbWord(args(0)) Then
                Dim one As CommandSpec = manifest.Find(args(0).ToLowerInvariant())
                If one IsNot Nothing Then
                    consumed = 1
                    Return one
                End If
            End If
            Return Nothing
        End Function

        ''' <summary>The one strict parser of rule 3, used alike by a cold start and by the first instance
        ''' (rule 7). Never throws; refuses anything it does not know.</summary>
        Friend Function Parse(args As IList(Of String), manifest As CommandManifest, ByRef parsed As ParsedCommand) As ParseRefusal
            parsed = Nothing
            If args Is Nothing Then args = New String() {}
            Dim tooLarge As ParseRefusal = CheckSize(args)
            If tooLarge IsNot Nothing Then Return tooLarge

            Dim consumed As Integer
            Dim spec As CommandSpec = ResolveVerb(args, manifest, consumed)
            If spec Is Nothing Then
                Return New ParseRefusal With {.Reason = CommandReasons.UnknownVerb, .Detail = DetailUnknownVerb,
                                              .Token = If(args.Count > 0 AndAlso IsVerbWord(args(0)), args(0).ToLowerInvariant(), Nothing)}
            End If

            Dim result As New ParsedCommand With {.Spec = spec}
            Dim positionalSpecs As List(Of CommandArg) = spec.PositionalArgs()
            Dim endOfOptions As Boolean = False
            Dim i As Integer = consumed
            While i < args.Count
                Dim token As String = If(args(i), String.Empty)
                result.RawArgs.Add(token)
                If Not endOfOptions AndAlso token = "--" Then
                    endOfOptions = True
                ElseIf Not endOfOptions AndAlso token.StartsWith("--", StringComparison.Ordinal) Then
                    Dim body As String = token.Substring(2)
                    Dim inlineValue As String = Nothing
                    Dim eq As Integer = body.IndexOf("="c)
                    If eq >= 0 Then
                        inlineValue = body.Substring(eq + 1)
                        body = body.Substring(0, eq)
                    End If
                    Dim arg As CommandArg = spec.FindOption(body)
                    If arg Is Nothing OrElse Not IsVerbWord(body) Then
                        Return New ParseRefusal With {.Reason = CommandReasons.BadArgument, .Detail = DetailUnknownOption,
                                                      .Token = If(IsVerbWord(body), "--" & body.ToLowerInvariant(), Nothing)}
                    End If
                    Dim value As String
                    If arg.Kind = "bool" Then
                        If inlineValue Is Nothing Then
                            value = "true"
                        ElseIf inlineValue = "true" OrElse inlineValue = "false" Then
                            value = inlineValue
                        Else
                            Return BadValue(arg)
                        End If
                    Else
                        If inlineValue IsNot Nothing Then
                            value = inlineValue
                        ElseIf i + 1 < args.Count AndAlso Not If(args(i + 1), String.Empty).StartsWith("--", StringComparison.Ordinal) Then
                            i += 1
                            value = If(args(i), String.Empty)
                            result.RawArgs.Add(value)
                        Else
                            Return New ParseRefusal With {.Reason = CommandReasons.BadArgument, .Detail = DetailMissingValue, .Token = "--" & arg.Name}
                        End If
                        If Not IsValidValue(arg, value) Then Return BadValue(arg)
                    End If
                    Dim list As List(Of String) = Nothing
                    If Not result.Options.TryGetValue(arg.Name, list) Then
                        list = New List(Of String)()
                        result.Options(arg.Name) = list
                    End If
                    list.Add(value)
                ElseIf Not endOfOptions AndAlso token.Length > 1 AndAlso (token(0) = "-"c OrElse token(0) = "/"c) AndAlso Not LooksLikePath(token) Then
                    ' A single-dash or slash switch is not a spelling of this profile (rule 1); the frozen
                    ' spellings of rule 2 never reach this parser.
                    Return New ParseRefusal With {.Reason = CommandReasons.BadArgument, .Detail = DetailUnknownOption, .Token = Nothing}
                Else
                    If result.Positionals.Count >= positionalSpecs.Count Then
                        Return New ParseRefusal With {.Reason = CommandReasons.BadArgument, .Detail = DetailExtraValue, .Token = Nothing}
                    End If
                    Dim posSpec As CommandArg = positionalSpecs(result.Positionals.Count)
                    If Not IsValidValue(posSpec, token) Then Return BadValue(posSpec)
                    result.Positionals.Add(token)
                End If
                i += 1
            End While

            For Each a As CommandArg In spec.Args
                If Not a.Required Then Continue For
                Dim present As Boolean
                If a.Positional Then
                    present = positionalSpecs.IndexOf(a) < result.Positionals.Count
                Else
                    present = result.Options.ContainsKey(a.Name)
                End If
                If Not present Then
                    Return New ParseRefusal With {.Reason = CommandReasons.BadArgument, .Detail = DetailMissingArgument,
                                                  .Token = If(a.Positional, a.Name, "--" & a.Name)}
                End If
            Next

            ' Item R.4: exactly one argument of each oneOf group.
            For Each group As String() In spec.OneOf
                Dim given As Integer = 0
                For Each name As String In group
                    If IsGiven(spec, result, positionalSpecs, name) Then given += 1
                Next
                If given = 0 Then
                    Return New ParseRefusal With {.Reason = CommandReasons.BadArgument, .Detail = DetailMissingArgument,
                                                  .Token = ArgToken(spec, group(0))}
                End If
                If given > 1 Then
                    Return New ParseRefusal With {.Reason = CommandReasons.BadArgument, .Detail = DetailExtraValue, .Token = Nothing}
                End If
            Next

            parsed = result
            Return Nothing
        End Function

        Private Function IsGiven(spec As CommandSpec, result As ParsedCommand, positionalSpecs As List(Of CommandArg), name As String) As Boolean
            For Each a As CommandArg In spec.Args
                If Not String.Equals(a.Name, name, StringComparison.Ordinal) Then Continue For
                If a.Positional Then Return positionalSpecs.IndexOf(a) < result.Positionals.Count
                Return result.Options.ContainsKey(a.Name)
            Next
            Return False
        End Function

        Private Function ArgToken(spec As CommandSpec, name As String) As String
            For Each a As CommandArg In spec.Args
                If String.Equals(a.Name, name, StringComparison.Ordinal) Then Return If(a.Positional, a.Name, "--" & a.Name)
            Next
            Return Nothing
        End Function

        ''' <summary>
        ''' Item H.2: the value of <c>--&lt;name&gt;-file</c>, read by the sender, takes the place of the option as the
        ''' positional it stands for - in <see cref="ParsedCommand.Positionals"/> and, after a <c>--</c>, at the end of
        ''' <see cref="ParsedCommand.RawArgs"/>, so the first instance parses the same request it would have had from the
        ''' command line. The value never reaches a log line: <see cref="CommandDispatch.DescribeForLog"/> counts values only.
        ''' </summary>
        Friend Sub InlineFileValue(parsed As ParsedCommand, optionName As String, value As String)
            parsed.Options.Remove(optionName)
            Dim raw As List(Of String) = parsed.RawArgs
            Dim i As Integer = 0
            While i < raw.Count
                Dim token As String = If(raw(i), String.Empty)
                If token = "--" Then Exit While
                If String.Equals(token, "--" & optionName, StringComparison.OrdinalIgnoreCase) Then
                    raw.RemoveAt(i)
                    If i < raw.Count Then raw.RemoveAt(i)
                    Continue While
                End If
                If token.StartsWith("--" & optionName & "=", StringComparison.OrdinalIgnoreCase) Then
                    raw.RemoveAt(i)
                    Continue While
                End If
                i += 1
            End While
            If raw.IndexOf("--") < 0 Then raw.Add("--")
            raw.Add(value)
            parsed.Positionals.Add(value)
        End Sub

        Private Function BadValue(arg As CommandArg) As ParseRefusal
            Return New ParseRefusal With {.Reason = CommandReasons.BadArgument, .Detail = DetailBadValue,
                                          .Token = If(arg.Positional, arg.Name, "--" & arg.Name)}
        End Function

        Private Function LooksLikePath(token As String) As Boolean
            ' "/c/folder" style is not ours either; a Windows path starts with a drive or a backslash.
            Return token.Length >= 2 AndAlso token(1) = ":"c
        End Function

        ''' <summary>Rule 1: values are compared exactly (never without regard to case).</summary>
        Friend Function IsValidValue(arg As CommandArg, value As String) As Boolean
            Select Case arg.Kind
                Case "int"
                    Dim n As Integer
                    Return Integer.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, n)
                Case "guid"
                    Dim g As Guid
                    Return Guid.TryParse(value, g)
                Case "enum"
                    If arg.Values Is Nothing Then Return False
                    Return Array.IndexOf(arg.Values, value) >= 0
                Case "bool"
                    Return value = "true" OrElse value = "false"
                Case "path"
                    Return Not String.IsNullOrEmpty(value) AndAlso value.IndexOf(ChrW(0)) < 0
                Case Else
                    Return value IsNot Nothing
            End Select
        End Function
    End Module

    ' ---------------------------------------------------------------------------------------------
    ' Request and response lines (rule 7)
    ' ---------------------------------------------------------------------------------------------

    Friend NotInheritable Class CommandRequest
        Friend Const CurrentSchema As Integer = 1

        Friend Property SchemaVersion As Integer = CurrentSchema
        Friend Property Command As String
        Friend Property Args As New List(Of String)()
        Friend Property WorkingDir As String
        Friend Property Origin As String = CommandOrigin.Cli
        ''' <summary>Item F.2: the caller typed a legacy alias of the verb. Honoured only for a verb whose manifest lists
        ''' one, and it lifts the setting of rule 13 only. Written to the line only when true (default false).</summary>
        Friend Property Legacy As Boolean
        Friend Property RequestId As String
        Friend Property Wait As String = "done"
        Friend Property ResultPath As String
        Friend Property EventsPath As String
        ''' <summary>Where the first instance writes the response line - the reply file the second process
        ''' names (proposal item 1). Not a contract member of 0.1; a reader ignores it.</summary>
        Friend Property ReplyPath As String
        ''' <summary>The caller's process id, for the log of rule 16.</summary>
        Friend Property CallerPid As Integer
        ''' <summary>Whether the caller runs elevated, for the <c>elevation</c> answer of rule 12.</summary>
        Friend Property CallerElevated As Boolean

        Friend Function ToJson() As JsonObject
            Dim o As New JsonObject()
            o.Add("schemaVersion", SchemaVersion)
            o.Add("command", Command)
            o.Add("args", CommandArg.ToObjects(Args))
            o.Add("workingDir", WorkingDir)
            o.Add("origin", Origin)
            If Legacy Then o.Add("legacy", True)
            o.Add("requestId", RequestId)
            o.Add("wait", Wait)
            If ResultPath IsNot Nothing Then o.Add("resultPath", ResultPath)
            If EventsPath IsNot Nothing Then o.Add("eventsPath", EventsPath)
            If ReplyPath IsNot Nothing Then o.Add("replyPath", ReplyPath)
            o.Add("callerPid", CallerPid)
            o.Add("callerElevated", CallerElevated)
            Return o
        End Function

        ''' <summary>One line. Sent over WM_COPYDATA without its final LF: that transport refuses a control
        ''' character anywhere in the payload, and the message is already one request.</summary>
        Friend Function ToLine() As String
            Return CommandJson.ToLine(ToJson())
        End Function

        Friend Shared Function TryParse(text As String, ByRef request As CommandRequest) As Boolean
            request = Nothing
            Dim value As Object = Nothing
            If Not CommandJson.TryParse(If(text, String.Empty).TrimEnd(ControlChars.Lf, ControlChars.Cr), value) Then Return False
            Dim o As JsonObject = TryCast(value, JsonObject)
            If o Is Nothing Then Return False
            Dim r As New CommandRequest()
            r.SchemaVersion = o.GetInt("schemaVersion", 0)
            r.Command = o.GetString("command")
            r.WorkingDir = o.GetString("workingDir")
            ' The origin on the line is read and dropped: a window message cannot prove its sender (item T.1).
            r.Origin = CommandOrigin.Cli
            r.Legacy = o.GetBool("legacy", False)
            r.RequestId = o.GetString("requestId")
            r.Wait = If(o.GetString("wait"), "done")
            r.ResultPath = o.GetString("resultPath")
            r.EventsPath = o.GetString("eventsPath")
            r.ReplyPath = o.GetString("replyPath")
            r.CallerPid = o.GetInt("callerPid", 0)
            r.CallerElevated = o.GetBool("callerElevated", False)
            Dim argsValue As Object = Nothing
            If o.TryGet("args", argsValue) Then
                Dim items As List(Of Object) = TryCast(argsValue, List(Of Object))
                If items Is Nothing Then Return False
                For Each item As Object In items
                    Dim s As String = TryCast(item, String)
                    If s Is Nothing Then Return False
                    r.Args.Add(s)
                Next
            End If
            If r.SchemaVersion < 1 OrElse String.IsNullOrEmpty(r.Command) OrElse String.IsNullOrEmpty(r.RequestId) Then Return False
            request = r
            Return True
        End Function
    End Class

    Friend NotInheritable Class CommandResponse
        Friend Property SchemaVersion As Integer = 1
        Friend Property RequestId As String
        Friend Property Status As String
        Friend Property ExitCode As Integer
        Friend Property Reason As String
        Friend Property Message As String
        Friend Property Data As JsonObject

        Friend Shared Function Done(requestId As String, message As String, Optional data As JsonObject = Nothing) As CommandResponse
            Return New CommandResponse With {.RequestId = requestId, .Status = CommandStatus.Done, .ExitCode = CommandExit.Done,
                                             .Message = message, .Data = data}
        End Function

        ''' <summary>A refusal, busy or failure: status and exit follow the reason (rule 12).</summary>
        Friend Shared Function ForReason(requestId As String, reason As String, message As String) As CommandResponse
            Return New CommandResponse With {.RequestId = requestId, .Status = CommandReasons.StatusOf(reason),
                                             .ExitCode = CommandReasons.ExitOf(reason), .Reason = reason, .Message = message}
        End Function

        Friend Function ToJson() As JsonObject
            Dim o As New JsonObject()
            o.Add("schemaVersion", SchemaVersion)
            o.Add("requestId", RequestId)
            o.Add("status", Status)
            o.Add("exitCode", ExitCode)
            If Reason IsNot Nothing AndAlso Status <> CommandStatus.Done AndAlso Status <> CommandStatus.Accepted Then o.Add("reason", Reason)
            If Message IsNot Nothing Then o.Add("message", Message)
            If Data IsNot Nothing Then o.Add("data", Data)
            Return o
        End Function

        Friend Function ToLine() As String
            Return CommandJson.ToLine(ToJson())
        End Function

        ''' <summary>Item L, the one place a reply is finished: the response line, at most 64 KiB of UTF-8. A verb that
        ''' is not paged and answers past the bound fails instead - status <c>failed</c>, reason
        ''' <c>response-too-large</c>, exit 1 - with the same <c>requestId</c> and no <c>data</c>. The complete data
        ''' goes through <c>--result</c>, which has no line bound (rule 9).</summary>
        Friend Function ToBoundedLine(tooLargeMessage As String) As String
            Dim line As String = ToLine()
            If Encoding.UTF8.GetByteCount(line) <= CommandBounds.MaxResponseBytes Then Return line
            Return ForReason(RequestId, CommandReasons.ResponseTooLarge, tooLargeMessage).ToLine()
        End Function

        ''' <summary>Item I.3: the answer belongs to the request when its <c>requestId</c> is the request's own. An empty
        ''' one (the answer to a line that could not be read) or a foreign one is unreadable to the sender, which exits 2.</summary>
        Friend Function IsAnswerTo(expectedRequestId As String) As Boolean
            Return Not String.IsNullOrEmpty(Me.RequestId) AndAlso String.Equals(Me.RequestId, expectedRequestId, StringComparison.OrdinalIgnoreCase)
        End Function

        Friend Shared Function TryParse(text As String, ByRef response As CommandResponse) As Boolean
            response = Nothing
            Dim value As Object = Nothing
            If Not CommandJson.TryParse(If(text, String.Empty).TrimEnd(ControlChars.Lf, ControlChars.Cr), value) Then Return False
            Dim o As JsonObject = TryCast(value, JsonObject)
            If o Is Nothing Then Return False
            Dim r As New CommandResponse()
            r.SchemaVersion = o.GetInt("schemaVersion", 0)
            r.RequestId = o.GetString("requestId")
            r.Status = o.GetString("status")
            r.ExitCode = o.GetInt("exitCode", CommandExit.Failed)
            r.Reason = o.GetString("reason")
            r.Message = o.GetString("message")
            Dim dataValue As Object = Nothing
            If o.TryGet("data", dataValue) Then r.Data = TryCast(dataValue, JsonObject)
            If r.SchemaVersion < 1 OrElse String.IsNullOrEmpty(r.Status) Then Return False
            ' A reason a reader does not know is an ordinary failure (rule 12); an exit outside the
            ' vocabulary is one too.
            If Not CommandExit.IsKnown(r.ExitCode) Then r.ExitCode = CommandExit.Failed
            response = r
            Return True
        End Function
    End Class

    ' ---------------------------------------------------------------------------------------------
    ' The first instance's decision before any work (rules 3, 7, 10, 13)
    ' ---------------------------------------------------------------------------------------------

    Friend NotInheritable Class DispatchDecision
        ''' <summary>Set when the command may run.</summary>
        Friend Property Parsed As ParsedCommand
        ''' <summary>Set when it may not: the answer to send at once. Its message is filled by the caller.</summary>
        Friend Property RefusalReason As String
        Friend Property Refusal As ParseRefusal
    End Class

    Friend Module CommandDispatch

        ''' <summary>
        ''' Everything the first instance decides before it runs a command, in order: schema, verb and
        ''' arguments with the cold-start parser (rule 7), elevation (rule 12), the risk-class setting (rule 13),
        ''' busy (rule 10). A <c>safe</c> verb is always admitted by the setting; nothing else lifts it except a
        ''' request that carries <c>legacy</c> for a verb with a legacy alias (item F.2).
        ''' </summary>
        Friend Function Decide(request As CommandRequest, manifest As CommandManifest, restrictedAllowed As Boolean,
                               busy As Boolean, receiverElevated As Boolean) As DispatchDecision
            If request.SchemaVersion > CommandRequest.CurrentSchema Then
                Return New DispatchDecision With {.RefusalReason = CommandReasons.Schema}
            End If
            Dim spec As CommandSpec = manifest.Find(request.Command)
            If spec Is Nothing Then
                Return New DispatchDecision With {.RefusalReason = CommandReasons.UnknownVerb,
                    .Refusal = New ParseRefusal With {.Reason = CommandReasons.UnknownVerb, .Detail = CommandLineParser.DetailUnknownVerb}}
            End If
            Dim words As New List(Of String)(spec.Name.Split(" "c))
            words.AddRange(request.Args)
            Dim parsed As ParsedCommand = Nothing
            Dim refusal As ParseRefusal = CommandLineParser.Parse(words, manifest, parsed)
            If refusal IsNot Nothing Then Return New DispatchDecision With {.RefusalReason = refusal.Reason, .Refusal = refusal}
            If receiverElevated AndAlso Not request.CallerElevated Then
                Return New DispatchDecision With {.RefusalReason = CommandReasons.Elevation}
            End If
            If Not IsAdmittedByRisk(spec, request.Legacy, restrictedAllowed) Then
                Return New DispatchDecision With {.RefusalReason = CommandReasons.SwitchOff}
            End If
            If busy Then Return New DispatchDecision With {.RefusalReason = CommandReasons.Busy}
            Return New DispatchDecision With {.Parsed = parsed}
        End Function

        ''' <summary>
        ''' Item I.3: a request line the receiver could decode but not read as a request (not an object, no
        ''' <c>requestId</c>, a member of the wrong kind, or over the bound of rule 3). It is answered <c>refused</c>
        ''' with <c>requestId</c> <c>""</c>, and only into a reply file that passes <see cref="CommandFiles.IsValidReplyFile"/>:
        ''' a line that names no such file gets no answer, because the receiver never writes where a stranger says.
        ''' Returns True with the file and the reason (<c>too-large</c> or <c>bad-argument</c>) when an answer is owed.
        ''' </summary>
        Friend Function TryAnswerUnreadable(text As String, productFolder As String,
                                            ByRef replyPath As String, ByRef reason As String) As Boolean
            replyPath = Nothing
            reason = Nothing
            If String.IsNullOrEmpty(text) Then Return False
            Dim value As Object = Nothing
            If Not CommandJson.TryParse(text.TrimEnd(ControlChars.Lf, ControlChars.Cr), value) Then Return False
            Dim o As JsonObject = TryCast(value, JsonObject)
            If o Is Nothing Then Return False
            Dim candidate As String = o.GetString("replyPath")
            If Not CommandFiles.IsValidReplyFile(productFolder, candidate) Then Return False
            replyPath = candidate
            reason = If(Encoding.UTF8.GetByteCount(text) > CommandBounds.MaxRequestBytes, CommandReasons.TooLarge, CommandReasons.BadArgument)
            Return True
        End Function

        ''' <summary>Rule 13 and item F. The only thing that lifts the setting for a non-<c>safe</c> verb is the person
        ''' turning it on, or - for a verb whose manifest lists a legacy alias - a request typed with that alias
        ''' (<paramref name="legacy"/>); it lifts rule 13 only, never the parser, the elevation check or busy. The wire
        ''' <c>origin</c> is never consulted (item T.1).</summary>
        Friend Function IsAdmittedByRisk(spec As CommandSpec, legacy As Boolean, restrictedAllowed As Boolean) As Boolean
            If String.Equals(spec.Risk, CommandRisk.Safe, StringComparison.Ordinal) Then Return True
            If legacy AndAlso spec.HasLegacyAlias Then Return True
            Return restrictedAllowed
        End Function

        ''' <summary>Rule 16: the log line of an accepted command - verb, origin, caller, outcome; the arguments
        ''' redacted to their option names and a count of values (CLI-EVENT-STREAM rule 13): no value, no path,
        ''' no secret.</summary>
        Friend Function DescribeForLog(request As CommandRequest, outcome As CommandResponse) As String
            Dim names As New List(Of String)()
            Dim values As Integer = 0
            For Each a As String In request.Args
                If a IsNot Nothing AndAlso a.StartsWith("--", StringComparison.Ordinal) AndAlso a.Length > 2 Then
                    Dim body As String = a.Substring(2)
                    Dim eq As Integer = body.IndexOf("="c)
                    If eq >= 0 Then
                        body = body.Substring(0, eq)
                        values += 1
                    End If
                    names.Add("--" & If(CommandLineParser.IsVerbWord(body), body.ToLowerInvariant(), "?"))
                Else
                    values += 1
                End If
            Next
            Dim verb As String = If(CommandLineParser.IsVerbWord(If(request.Command, String.Empty).Replace(" "c, "-"c)), request.Command.ToLowerInvariant(), "?")
            Return "App command: " & verb &
                   " | origin=" & CommandOrigin.Cli & If(request.Legacy, " legacy", String.Empty) &
                   " | caller=" & request.CallerPid.ToString(CultureInfo.InvariantCulture) &
                   " | outcome=" & If(outcome Is Nothing, "?", outcome.Status & If(outcome.Reason IsNot Nothing, "/" & outcome.Reason, String.Empty)) &
                   " | options=" & If(names.Count = 0, "-", String.Join(",", names.ToArray())) &
                   " | values=" & values.ToString(CultureInfo.InvariantCulture) & " redacted"
        End Function
    End Module

    ' ---------------------------------------------------------------------------------------------
    ' Files (rules 5, 9, 14)
    ' ---------------------------------------------------------------------------------------------

    Friend Module CommandFiles

        ''' <summary>The one folder a first instance writes reply files into: the user's own temporary folder.
        ''' A reply path anywhere else is refused, so a request can never make the first instance write a
        ''' file of the caller's choosing.</summary>
        Friend Function ReplyDirectory(productFolder As String) As String
            Return Path.Combine(Path.GetTempPath(), productFolder)
        End Function

        Friend Function ReplyPathFor(productFolder As String, requestId As String) As String
            Return Path.Combine(ReplyDirectory(productFolder), requestId & ".reply.json")
        End Function

        Friend Function IsValidReplyPath(productFolder As String, request As CommandRequest) As Boolean
            If request Is Nothing OrElse String.IsNullOrEmpty(request.ReplyPath) Then Return False
            Dim g As Guid
            If Not Guid.TryParse(request.RequestId, g) Then Return False
            Try
                Dim expected As String = Path.GetFullPath(ReplyPathFor(productFolder, g.ToString("D")))
                Return String.Equals(Path.GetFullPath(request.ReplyPath), expected, StringComparison.OrdinalIgnoreCase)
            Catch ex As ArgumentException
                Return False
            Catch ex As NotSupportedException
                Return False
            Catch ex As PathTooLongException
                Return False
            End Try
        End Function

        ''' <summary>The same check for a line whose <c>requestId</c> cannot be trusted (item I.3): the reply file must be
        ''' <c>&lt;guid&gt;.reply.json</c> directly inside the product's own reply folder, nowhere else.</summary>
        Friend Function IsValidReplyFile(productFolder As String, replyPath As String) As Boolean
            If String.IsNullOrEmpty(replyPath) Then Return False
            Try
                Dim full As String = Path.GetFullPath(replyPath)
                Const suffix As String = ".reply.json"
                Dim name As String = Path.GetFileName(full)
                If name Is Nothing OrElse Not name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) Then Return False
                Dim g As Guid
                If Not Guid.TryParse(name.Substring(0, name.Length - suffix.Length), g) Then Return False
                Return String.Equals(full, Path.GetFullPath(ReplyPathFor(productFolder, g.ToString("D"))), StringComparison.OrdinalIgnoreCase)
            Catch ex As ArgumentException
                Return False
            Catch ex As NotSupportedException
                Return False
            Catch ex As PathTooLongException
                Return False
            End Try
        End Function

        ''' <summary>Rule 9: a file appears whole or not at all - written beside the target, then moved over it. A folder
        ''' the product owns (the reply folder) is created; a folder a caller named is not
        ''' (<paramref name="createFolder"/> False, CAPTURE-OUTPUT 0.8 section 5): it must exist, or this throws
        ''' <see cref="DirectoryNotFoundException"/> before anything is written.</summary>
        Friend Sub WriteWhole(target As String, bytes As Byte(), Optional createFolder As Boolean = True)
            Dim full As String = Path.GetFullPath(target)
            Dim folder As String = Path.GetDirectoryName(full)
            If Not String.IsNullOrEmpty(folder) Then
                If createFolder Then
                    Directory.CreateDirectory(folder)
                ElseIf Not Directory.Exists(folder) Then
                    Throw New DirectoryNotFoundException()
                End If
            End If
            Dim temp As String = full & "." & Guid.NewGuid().ToString("N") & ".tmp"
            Try
                File.WriteAllBytes(temp, bytes)
                If File.Exists(full) Then
                    File.Replace(temp, full, Nothing, True)
                Else
                    File.Move(temp, full)
                End If
            Finally
                Try
                    If File.Exists(temp) Then File.Delete(temp)
                Catch ex As IOException
                Catch ex As UnauthorizedAccessException
                End Try
            End Try
        End Sub

        Friend Sub WriteWholeText(target As String, text As String)
            WriteWhole(target, New UTF8Encoding(False).GetBytes(text))
        End Sub

        ''' <summary>Rule 9: <c>--result</c> holds the response <c>data</c> as one JSON file (an empty object when
        ''' the verb returns nothing).</summary>
        Friend Sub WriteResult(target As String, data As JsonObject)
            WriteWholeText(target, CommandJson.ToIndented(If(data, New JsonObject())))
        End Sub

        ''' <summary>Rule 14: an <c>--out</c> target is written only where named, and an existing file is kept
        ''' unless <c>--overwrite</c> was given. Returns False (nothing written) when the file exists. The folder a
        ''' caller named is never created (CAPTURE-OUTPUT 0.8 section 5): a missing one throws
        ''' <see cref="DirectoryNotFoundException"/> and nothing is written.</summary>
        Friend Function TryWriteOut(target As String, bytes As Byte(), overwrite As Boolean) As Boolean
            If Not overwrite AndAlso File.Exists(target) Then Return False
            WriteWhole(target, bytes, False)
            Return True
        End Function

        ''' <summary>Rule 5: a secret comes from a file the caller names, UTF-8, one trailing line end dropped,
        ''' at most <see cref="CommandBounds.MaxSecretBytes"/>. False when it cannot be read whole.</summary>
        Friend Function TryReadSecret(path As String, ByRef secret As String) As Boolean
            secret = Nothing
            Try
                Dim info As New FileInfo(path)
                If Not info.Exists OrElse info.Length > CommandBounds.MaxSecretBytes Then Return False
                Dim text As String = New UTF8Encoding(False, True).GetString(File.ReadAllBytes(path))
                If text.Length > 0 AndAlso text(0) = ChrW(&HFEFF) Then text = text.Substring(1)
                If text.EndsWith(ControlChars.CrLf, StringComparison.Ordinal) Then
                    text = text.Substring(0, text.Length - 2)
                ElseIf text.EndsWith(ControlChars.Lf, StringComparison.Ordinal) Then
                    text = text.Substring(0, text.Length - 1)
                End If
                secret = text
                Return True
            Catch ex As IOException
                Return False
            Catch ex As UnauthorizedAccessException
                Return False
            Catch ex As ArgumentException
                ' DecoderFallbackException (not UTF-8) is an ArgumentException too.
                Return False
            Catch ex As NotSupportedException
                Return False
            End Try
        End Function
    End Module

    ' ---------------------------------------------------------------------------------------------
    ' docs/CLI.md (rule 20)
    ' ---------------------------------------------------------------------------------------------

    Friend Module CommandDocs

        ''' <summary>docs/CLI.md, rendered from the manifest and nothing else. <paramref name="settingLabel"/> is
        ''' the English text of the setting of rule 13, as the Settings window shows it.</summary>
        Friend Function ToMarkdown(manifest As CommandManifest, settingLabel As String, settingPlace As String) As String
            Return Render(manifest, settingLabel, settingPlace, 0, String.Empty)
        End Function

        ''' <summary>The same document as a section of a larger one (docs/CLI.md holds one per program): every heading
        ''' one level lower and every command anchored under <paramref name="anchorPrefix"/>, so two programs' verbs
        ''' of one name never share an anchor. The text of the first program's document is unchanged by this.</summary>
        Friend Function ToMarkdownSection(manifest As CommandManifest, settingLabel As String, settingPlace As String,
                                          anchorPrefix As String) As String
            Return Render(manifest, settingLabel, settingPlace, 1, anchorPrefix)
        End Function

        Private Function Render(manifest As CommandManifest, settingLabel As String, settingPlace As String,
                                extraLevel As Integer, anchorPrefix As String) As String
            Dim lf As String = ControlChars.Lf
            Dim sb As New StringBuilder()
            Dim h1 As String = New String("#"c, 1 + extraLevel)
            Dim h2 As String = New String("#"c, 2 + extraLevel)
            sb.Append(h1).Append(" Command line of ").Append(manifest.Exe).Append(lf).Append(lf)
            sb.Append("<!-- Generated from the command table in code (tools/Build-CliDocs.ps1); never edit by hand. -->").Append(lf).Append(lf)
            sb.Append(manifest.Product).Append(", command set ").Append(manifest.CliVersion)
            sb.Append(", contract `APP-COMMANDS` 0.1. The same list in machine form is `docs/cli-commands.json` and the output of `")
            sb.Append(manifest.Exe).Append(" commands --json`.").Append(lf).Append(lf)
            sb.Append("A command is handed to the copy that is already running; that copy does the work and answers, and the program you started exits with the answer's code. ")
            sb.Append("The program is a window program: from `cmd` use `start /wait`, from PowerShell `Start-Process -Wait -PassThru`, and read the exit code. ")
            sb.Append("Text is printed to the console the program was started from; `--json` prints the response line itself, and `--result <path>` writes the returned data to a file.").Append(lf).Append(lf)

            sb.Append(h2).Append(" Commands").Append(lf).Append(lf)
            sb.Append("| Command | What it does | Risk | With nothing running | Bound |").Append(lf)
            sb.Append("| --- | --- | --- | --- | --- |").Append(lf)
            For Each c As CommandSpec In manifest.Commands
                sb.Append("| [`").Append(c.Name).Append("`](#").Append(anchorPrefix).Append(c.Name.Replace(" "c, "-"c)).Append(") | ").Append(Cell(c.Summary)).Append(" | `")
                sb.Append(c.Risk).Append("` | ").Append(ColdStartText(c.ColdStart)).Append(" | ")
                sb.Append(c.TimeoutMs.ToString(CultureInfo.InvariantCulture)).Append(" ms |").Append(lf)
            Next
            sb.Append(lf)

            sb.Append(h2).Append(" Exit codes").Append(lf).Append(lf)
            sb.Append("| Code | Meaning |").Append(lf).Append("| --- | --- |").Append(lf)
            sb.Append("| 0 | done |").Append(lf)
            sb.Append("| 1 | the command ran and the operation failed |").Append(lf)
            sb.Append("| 2 | not proven: no answer inside the bound, or the running copy is older and must be restarted |").Append(lf)
            sb.Append("| 10 | usage: unknown command, unknown option, bad value, request too large |").Append(lf)
            sb.Append("| 11 | not ready: nothing running, or busy with an open window |").Append(lf)
            sb.Append("| 12 | permission: the setting below is off, or the running copy is elevated and the caller is not |").Append(lf)
            If UsesExit(manifest, CommandExit.Resources) Then
                sb.Append("| 13 | resources: too large, or no room |").Append(lf)
            End If
            If UsesExit(manifest, CommandExit.Network) Then
                sb.Append("| 15 | network: the server did not answer |").Append(lf)
            End If
            sb.Append("| 16 | stopped by the person |").Append(lf).Append(lf)

            sb.Append(h2).Append(" Risk classes and the setting").Append(lf).Append(lf)
            sb.Append("A `safe` command is always accepted. A command of any other class (`reads-user-data`, `writes-user-data`, `leaves-machine`) ")
            sb.Append("is refused with exit 12 while the setting **").Append(settingLabel).Append("** (").Append(settingPlace)
            sb.Append(") is off. It is off until the person turns it on, and no command changes it.").Append(lf).Append(lf)

            For Each c As CommandSpec In manifest.Commands
                If anchorPrefix.Length > 0 Then
                    sb.Append("<a id=""").Append(anchorPrefix).Append(c.Name.Replace(" "c, "-"c)).Append("""></a>").Append(lf).Append(lf)
                End If
                sb.Append(h2).Append(" ").Append(c.Name).Append(lf).Append(lf)
                sb.Append(c.Summary).Append(lf).Append(lf)
                sb.Append("```").Append(lf).Append(c.Synopsis).Append(lf).Append("```").Append(lf).Append(lf)
                sb.Append("- Risk: `").Append(c.Risk).Append("`; with nothing running: ").Append(ColdStartText(c.ColdStart))
                sb.Append("; answer: `").Append(c.Wait).Append("` within ").Append(c.TimeoutMs.ToString(CultureInfo.InvariantCulture)).Append(" ms")
                sb.Append("; idempotent: ").Append(If(c.Idempotent, "yes", "no")).Append("; since ").Append(c.Since).Append(".").Append(lf)
                If c.Aliases.Count > 0 Then
                    Dim shown As New List(Of String)()
                    For Each a As String In c.Aliases
                        shown.Add(If(a.Length = 0, "(no arguments)", "`" & a & "`"))
                    Next
                    sb.Append("- Also reached by: ").Append(String.Join(", ", shown.ToArray())).Append(".").Append(lf)
                End If
                If c.Args.Count > 0 Then
                    sb.Append(lf).Append("| Option | Kind | Required | Meaning |").Append(lf).Append("| --- | --- | --- | --- |").Append(lf)
                    For Each a As CommandArg In c.Args
                        sb.Append("| `").Append(If(a.Positional, "<" & a.Name & ">", "--" & a.Name)).Append("` | ").Append(a.Kind)
                        If a.Sensitive Then sb.Append(", sensitive")
                        sb.Append(" | ").Append(If(a.Required, "yes", "no")).Append(" | ").Append(Cell(a.Description)).Append(" |").Append(lf)
                    Next
                    For Each g As String() In c.OneOf
                        Dim names As New List(Of String)()
                        For Each n As String In g
                            names.Add("`" & ArgLabel(c, n) & "`")
                        Next
                        sb.Append(lf).Append("Exactly one of ").Append(String.Join(", ", names)).Append(" is given.").Append(lf)
                    Next
                End If
                If c.Returns.Count > 0 Then
                    sb.Append(lf).Append("| Returned | Kind | Meaning |").Append(lf).Append("| --- | --- | --- |").Append(lf)
                    For Each r As CommandReturn In c.Returns
                        sb.Append("| `").Append(r.Name).Append("` | ").Append(r.Kind).Append(" | ").Append(Cell(r.Description)).Append(" |").Append(lf)
                    Next
                End If
                sb.Append(lf).Append("| Exit | Meaning |").Append(lf).Append("| --- | --- |").Append(lf)
                For Each e As CommandExitCode In c.ExitCodes
                    sb.Append("| ").Append(e.Code.ToString(CultureInfo.InvariantCulture)).Append(" | ").Append(Cell(e.Meaning)).Append(" |").Append(lf)
                Next
                For Each x As CommandExample In c.Examples
                    sb.Append(lf).Append("```").Append(If(x.Shell = "powershell", "powershell", "bat")).Append(lf).Append(x.Line).Append(lf).Append("```").Append(lf)
                    sb.Append(lf).Append("Expect: ").Append(x.Expect).Append(".").Append(lf)
                Next
                sb.Append(lf)
            Next
            Return sb.ToString().TrimEnd(ControlChars.Lf) & lf
        End Function

        Private Function UsesExit(manifest As CommandManifest, code As Integer) As Boolean
            For Each c As CommandSpec In manifest.Commands
                For Each e As CommandExitCode In c.ExitCodes
                    If e.Code = code Then Return True
                Next
            Next
            Return False
        End Function

        Private Function Cell(text As String) As String
            Return If(text, String.Empty).Replace("|", "\|")
        End Function

        Private Function ArgLabel(c As CommandSpec, name As String) As String
            For Each a As CommandArg In c.Args
                If String.Equals(a.Name, name, StringComparison.Ordinal) Then Return If(a.Positional, "<" & a.Name & ">", "--" & a.Name)
            Next
            Return name
        End Function

        Private Function ColdStartText(coldStart As String) As String
            Select Case coldStart
                Case "start" : Return "starts the program"
                Case "oneshot" : Return "answers by itself, starts no window"
                Case "refuse" : Return "exit 11, not running"
                Case Else : Return coldStart
            End Select
        End Function
    End Module

End Namespace
