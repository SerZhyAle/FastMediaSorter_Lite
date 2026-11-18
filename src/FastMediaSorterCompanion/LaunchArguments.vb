Option Strict On

Imports System.Collections.Generic
Imports AppCommandsLib = FastMediaSorterCompanion.AppCommands

''' <summary>
''' What a launch of the Share Manager asks for, sorted out of its command line. Pure: no window, no registry, no
''' mutex - so every shipped spelling is held by a fact (ticket L0316, APP-COMMANDS rule 2: the frozen spellings stay
''' ordinary launches). <see cref="LaunchArguments.Classify"/> is the sorting that used to be written inline in
''' <c>Program.Main</c>, moved here unchanged; <see cref="LaunchArguments.FromCommand"/> maps a parsed verb of the
''' command table onto the same plan, so a cold start of either kind reaches the same startup code.
''' </summary>
Friend NotInheritable Class LaunchPlan
    ''' <summary>The logon autostart (--tray): no window, no hint.</summary>
    Friend Property SilentTray As Boolean
    ''' <summary>The launch itself asked for the window.</summary>
    Friend Property WindowRequested As Boolean
    Friend Property AddResourceRequested As Boolean
    Friend Property AddResourceCameraRequested As Boolean
    ''' <summary>Spec 230: the two clipboard shortcuts; neither asks for the window.</summary>
    Friend Property ClipboardSendRequested As Boolean
    Friend Property ClipboardGetRequested As Boolean
    ''' <summary>"::fms-schedule::" + a folder, or "" - never a bare folder.</summary>
    Friend Property SchedulePayload As String = ""
    ''' <summary>"::fms-broadcast::" + a path, or "".</summary>
    Friend Property BroadcastPayload As String = ""
    ''' <summary>A folder to share (consent first), or Nothing.</summary>
    Friend Property Folder As String

    ''' <summary>The payload a second instance forwards to the first: the clipboard marker, the folder, the add-resource
    ''' intents, the schedule or broadcast payload, the show-window request or the plain-start marker - in that order.
    ''' A silent logon launch that finds a manager already up forwards NOTHING ("").</summary>
    Friend Function ForwardPayload() As String
        Dim clipboardPayload As String = Program.ClipboardPayloadOf(ClipboardSendRequested, ClipboardGetRequested)
        If clipboardPayload.Length > 0 Then Return clipboardPayload
        If Not String.IsNullOrEmpty(Folder) Then Return Folder
        If AddResourceCameraRequested Then Return Program.AddResourceCameraCommand
        If AddResourceRequested Then Return Program.AddResourceCommand
        If SchedulePayload.Length > 0 Then Return SchedulePayload
        If BroadcastPayload.Length > 0 Then Return BroadcastPayload
        If WindowRequested Then Return Program.ShowWindowCommand
        If SilentTray Then Return ""
        Return Program.PlainStartCommand
    End Function

    Friend ReadOnly Property ClipboardRequested As Boolean
        Get
            Return ClipboardSendRequested OrElse ClipboardGetRequested
        End Get
    End Property
End Class

Friend Module LaunchArguments

    ''' <summary>The sorting of <c>Program.Main</c>, unchanged: the silent-tray switch, the show-window request, the
    ''' add-resource intents, the clipboard switches, the schedule and broadcast payloads and the first bare word as a
    ''' folder. A marker is checked BEFORE the folder branch, because it has no leading "-" and would be taken for a path.</summary>
    Friend Function Classify(args As IList(Of String)) As LaunchPlan
        Dim plan As New LaunchPlan()
        If args Is Nothing Then Return plan
        For Each a As String In args
            If a Is Nothing Then Continue For
            If String.Equals(a, Program.TrayFlag, StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(a, "/tray", StringComparison.OrdinalIgnoreCase) Then
                plan.SilentTray = True
            ElseIf Program.ClipboardSwitchOf(a) = ClipboardSwitch.Send Then
                plan.ClipboardSendRequested = True
            ElseIf Program.ClipboardSwitchOf(a) = ClipboardSwitch.Receive Then
                plan.ClipboardGetRequested = True
            ElseIf String.Equals(a, Program.AddResourceCameraFlag, StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(a, "/add-resource-camera", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(a, Program.AddResourceCameraCommand, StringComparison.Ordinal) Then
                plan.WindowRequested = True
                plan.AddResourceCameraRequested = True
            ElseIf String.Equals(a, Program.AddResourceFlag, StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(a, "/add-resource", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(a, Program.AddResourceCommand, StringComparison.Ordinal) Then
                plan.WindowRequested = True
                plan.AddResourceRequested = True
            ElseIf a.StartsWith(CompanionCommand.ScheduleCommandPrefix, StringComparison.Ordinal) Then
                ' Validated by the same parser a running instance applies; an invalid one is just a show-window.
                plan.WindowRequested = True
                If CompanionCommand.Parse(a).Kind = CompanionCommandKind.Schedule Then plan.SchedulePayload = a
            ElseIf a.StartsWith(CompanionCommand.BroadcastCommandPrefix, StringComparison.Ordinal) Then
                ' L0316: a cold start of the viewer's broadcast launcher used to be taken for a folder to share (the marker
                ' has no leading "-"), which never opened the window. Now it opens it; an invalid path is a show-window.
                plan.WindowRequested = True
                If CompanionCommand.Parse(a).Kind = CompanionCommandKind.Broadcast Then plan.BroadcastPayload = a
            ElseIf String.Equals(a, Program.ShowWindowFlag, StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(a, "/show", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(a, Program.ShowWindowCommand, StringComparison.Ordinal) Then
                plan.WindowRequested = True
            ElseIf plan.Folder Is Nothing AndAlso Not String.IsNullOrWhiteSpace(a) AndAlso Not a.StartsWith("-", StringComparison.Ordinal) Then
                plan.Folder = a
            End If
        Next
        Return plan
    End Function

    ''' <summary>The canonical folder a command line names, or "" when it is not an acceptable one. A relative path is read
    ''' against the working directory of the process that typed it (the request carries it, APP-COMMANDS rule 7), so a
    ''' manager that was started elsewhere does not read it against its own. The same rules as every other way a folder
    ''' reaches the manager (<see cref="CompanionCommand.CanonicalFolder"/>): a drive or UNC path, no wildcard, no device path.</summary>
    Friend Function ResolveFolder(raw As String, workingDir As String) As String
        If String.IsNullOrWhiteSpace(raw) Then Return ""
        Dim path As String = raw.Trim()
        Try
            If Not System.IO.Path.IsPathFullyQualified(path) AndAlso Not String.IsNullOrWhiteSpace(workingDir) AndAlso
               System.IO.Path.IsPathFullyQualified(workingDir) Then
                path = System.IO.Path.GetFullPath(path, workingDir)
            End If
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException OrElse
                                   TypeOf ex Is System.IO.PathTooLongException
            Return ""
        End Try
        Return CompanionCommand.CanonicalFolder(path)
    End Function

    ''' <summary>The folder or path a verb carries, resolved: <c>share add &lt;folder&gt;</c>, <c>schedule open --folder</c>,
    ''' <c>broadcast open --path</c>. "" when the verb has none or the optional one was not given; <paramref name="badToken"/>
    ''' is set (to the name the person typed wrong) when one was given and is not acceptable, so both the client and the
    ''' manager refuse the same words.</summary>
    Friend Function TargetOf(parsed As AppCommandsLib.ParsedCommand, workingDir As String, ByRef badToken As String) As String
        badToken = Nothing
        Dim raw As String = Nothing
        Dim token As String = Nothing
        Select Case parsed.Spec.Name
            Case "share add"
                If parsed.Positionals.Count > 0 Then raw = parsed.Positionals(0)
                token = "folder"
            Case "schedule open"
                raw = parsed.Value("folder")
                token = "--folder"
            Case "broadcast open"
                raw = parsed.Value("path")
                token = "--path"
            Case Else
                Return ""
        End Select
        If raw Is Nothing Then
            If parsed.Spec.Name = "share add" Then badToken = token
            Return ""
        End If
        Dim resolved As String = ResolveFolder(raw, workingDir)
        If resolved.Length = 0 Then badToken = token
        Return resolved
    End Function

    ''' <summary>The plan of a verb of the command table that starts the manager (coldStart "start"): the same fields the
    ''' frozen spellings set, so the tray context starts exactly as it does for them. <paramref name="folder"/> is the
    ''' canonical folder the receiver-side validation accepted.</summary>
    Friend Function FromCommand(parsed As AppCommandsLib.ParsedCommand, folder As String) As LaunchPlan
        Dim plan As New LaunchPlan()
        Select Case parsed.Spec.Name
            Case "show"
                plan.WindowRequested = True
            Case "start"
                plan.SilentTray = parsed.Flag("silent")
            Case "share add"
                plan.Folder = folder
            Case "resource add"
                plan.WindowRequested = True
                If parsed.Flag("camera") Then plan.AddResourceCameraRequested = True Else plan.AddResourceRequested = True
            Case "schedule open"
                plan.WindowRequested = True
                plan.SchedulePayload = CompanionCommand.ScheduleCommandPrefix & If(folder, "")
            Case "broadcast open"
                plan.WindowRequested = True
                If Not String.IsNullOrEmpty(folder) Then plan.BroadcastPayload = CompanionCommand.BroadcastCommandPrefix & folder
            Case "clipboard send"
                plan.ClipboardSendRequested = True
            Case "clipboard get"
                plan.ClipboardGetRequested = True
        End Select
        Return plan
    End Function

    ''' <summary>The payload of the spelling the program shipped for the same verb - what an OLDER manager, which does not know
    ''' the command tag, still understands (APP-COMMANDS rule 2). "" for a verb that never had one (<c>status</c>,
    ''' <c>quit</c>, the two list verbs).</summary>
    Friend Function LegacyForwardOf(parsed As AppCommandsLib.ParsedCommand, folder As String) As String
        Select Case parsed.Spec.Name
            Case "show", "start", "share add", "resource add", "schedule open", "broadcast open", "clipboard send", "clipboard get"
                Return FromCommand(parsed, folder).ForwardPayload()
            Case Else
                Return ""
        End Select
    End Function

End Module
