Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Ticket L0278 T-4, specification sections 1 (goal 1, "it cannot hit the wrong server"), 5.1 and 7.6 ("Separation"): the
''' remote administration layer and the local server module are held apart by a deny-list over the sources, in both
''' directions, from the first file of the remote layer on.
'''
''' The remote layer is <c>Core\RemoteAdmin\*.vb</c>, the shell partials <c>Share_Settings_Form.RemoteExchange*.vb</c>, the
''' dialogs <c>Remote_Admin_*_Form.vb</c> and the library files <c>src\Exchange\ExchangeRemoteAdmin*.vb</c>,
''' <c>ExchangeAdminWire.vb</c> and <c>ExchangeAdminTrust.vb</c>. None of them may name a type of the local server module or
''' call one of its request functions. <c>Share_Settings_Form.ExchangeRemoteAdmin.vb</c> is NOT part of it: that is the local
''' card for administration in the browser (specification 3.4), a local file like any other.
'''
''' The other direction: a local exchange file - one that drives the local server (it names <c>ExchangeProcess</c> or calls the
''' <c>ExchangeAdmin</c> module) - names none of the remote layer's types. The gate class
''' <c>ExchangeRemoteAdminFeatures</c> is the one remote name a local file may read: the entry button asks it (5.2).
'''
''' Whole-line comments are not code and are skipped, so a header that explains the separation does not trip it.
''' </summary>
Public Class RemoteAdminSeparationTests

    ' Types and stores of the local server module and of this PC's own exchange identity (spec 7.6).
    Private Shared ReadOnly DeniedTypes As String() = {
        "ExchangeProcess", "ExchangeAccountPasswordStore", "EnrollThisPcAsFirstDeviceAsync", "ExchangeAccountsView",
        "ExchangeEntryStore", "ExchangeCredentialStore", "ExchangeDeviceAgent"}

    ' The request functions of the local module ExchangeAdmin (Core\ExchangeAdmin.vb, "the operations"). A VB Module's members
    ' are reachable without the module name, so a bare call is denied as well as ExchangeAdmin.<name>; a declaration of a
    ' method of the same name in the remote layer, or a call through another object (session.StatusAsync), is not a call into
    ' the module. The pure parsers (ExchangeAdminParser) hold no local state (T-0 item 9) and are not listed.
    Private Shared ReadOnly LocalRequestFunctions As String() = {
        "ListAccountsAsync", "ListDevicesAsync", "AddAccountAsync", "ResetPasswordAsync", "PairingCodeAsync",
        "CreateInvitationAsync", "ListInvitationsAsync", "WithdrawInvitationAsync", "GetSharedInvitationStatusAsync",
        "SetSharedInvitationAsync", "SetAccountLimitsAsync", "RemoveAccountAsync", "RevokeDeviceAsync", "StatusAsync",
        "ListBroadcastsAsync", "EndBroadcastAsync", "ListResourcesAsync", "UnpublishResourceAsync", "GetClipboardInfoAsync",
        "EraseClipboardAsync", "ListAdminAuditAsync"}

    ' The remote layer's types a local file must not name (the gate class is allowed, see the header).
    Private Shared ReadOnly RemoteTypes As New Regex("\b(ExchangeRemoteAdmin(?!Features\b)\w*|ExchangeAdminTargets|RemoteAdminState|RemoteAdminOps|RemoteAdminConfirm|RemoteAdminController|RemoteAdminCards?|RemoteAdminActions)\b",
                                                     RegexOptions.CultureInvariant)

    Private Shared ReadOnly LocalDriver As New Regex("\bExchangeProcess\b|\bExchangeAdmin\.", RegexOptions.CultureInvariant)

    Private Shared Function Rel(full As String) As String
        Return full.Substring(ExchangeTestSupport.RepoRoot().Length + 1)
    End Function

    Private Shared Function NotBuildOutput(f As String) As Boolean
        Dim sep As Char = Path.DirectorySeparatorChar
        Return f.IndexOf(sep & "obj" & sep, StringComparison.OrdinalIgnoreCase) < 0 AndAlso
               f.IndexOf(sep & "bin" & sep, StringComparison.OrdinalIgnoreCase) < 0
    End Function

    ''' <summary>Every source of the remote layer that exists today (the list grows with T-3..T-8).</summary>
    Friend Shared Function RemoteLayerFiles() As List(Of String)
        Dim found As New List(Of String)()
        Dim companion As String = ExchangeTestSupport.CompanionDir()
        Dim remoteDir As String = Path.Combine(companion, "Core", "RemoteAdmin")
        If Directory.Exists(remoteDir) Then found.AddRange(Directory.GetFiles(remoteDir, "*.vb", SearchOption.AllDirectories))
        For Each f As String In ExchangeTestSupport.CompanionSources()
            Dim name As String = Path.GetFileName(f)
            If name.StartsWith("Share_Settings_Form.RemoteExchange", StringComparison.Ordinal) OrElse
               Regex.IsMatch(name, "^Remote_Admin_\w+_Form(\.\w+)?\.vb$") Then found.Add(f)
        Next
        Dim library As String = Path.Combine(ExchangeTestSupport.RepoRoot(), "src", "Exchange")
        For Each f As String In Directory.GetFiles(library, "*.vb", SearchOption.TopDirectoryOnly)
            Dim name As String = Path.GetFileName(f)
            If name.StartsWith("ExchangeRemoteAdmin", StringComparison.Ordinal) OrElse name = "ExchangeAdminWire.vb" OrElse
               name = "ExchangeAdminTrust.vb" Then found.Add(f)
        Next
        Return found.Where(AddressOf NotBuildOutput).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    ''' <summary>The defects of one remote-layer source: every denied local name it uses in code.</summary>
    Friend Shared Function RemoteFileDefects(code As String) As List(Of String)
        Dim found As New List(Of String)()
        For Each name As String In DeniedTypes
            If Regex.IsMatch(code, "\b" & name & "\b") Then found.Add(name)
        Next
        For Each name As String In LocalRequestFunctions
            If Regex.IsMatch(code, "\bExchangeAdmin\s*\.\s*" & name & "\b") Then
                found.Add("ExchangeAdmin." & name)
            ElseIf Regex.IsMatch(code, "(?<![.\w])" & name & "\s*\(") AndAlso
                   Regex.Matches(code, "(?<![.\w])" & name & "\s*\(").Cast(Of Match)().
                       Any(Function(m) Not Regex.IsMatch(code.Substring(0, m.Index), "\b(Function|Sub)\s+$")) Then
                found.Add(name & " (a bare call into the local module)")
            End If
        Next
        Return found
    End Function

    ' 7.6 "One state object": a field or a property of the target type; a local of a method and a function's result hold nothing.
    Private Shared ReadOnly TargetMember As New Regex(
        "(?m)^(?:[ \t]{0,4}Dim[ \t]+|[ \t]*(?:(?:Private|Friend|Public|Protected|Shared|ReadOnly|WithEvents)[ \t]+)+(?:Property[ \t]+)?)\w+[ \t]+As[ \t]+(?:New[ \t]+)?ExchangeAdminTarget\b",
        RegexOptions.CultureInvariant)

    <Fact>
    Public Sub Only_the_state_object_keeps_the_chosen_target()
        Dim holders As List(Of String) = RemoteLayerFiles().
            Where(Function(f) TargetMember.IsMatch(ExchangeTestSupport.CodeOnly(File.ReadAllText(f)))).
            Select(Function(f) Path.GetFileName(f)).ToList()
        Assert.Equal("RemoteAdminState.vb", String.Join("|", holders))
    End Sub

    <Fact>
    Public Sub The_target_member_pattern_catches_a_field_and_a_property_and_lets_a_local_go()
        Assert.Matches(TargetMember, "    Private _held As ExchangeAdminTarget")
        Assert.Matches(TargetMember, "    Friend ReadOnly Property Held As ExchangeAdminTarget")
        Assert.Matches(TargetMember, "    Dim _held As ExchangeAdminTarget")
        Assert.DoesNotMatch(TargetMember, "        Dim held As ExchangeAdminTarget = _state.Target")
        Assert.DoesNotMatch(TargetMember, "    Private Sub Show(target As ExchangeAdminTarget)")
        Assert.DoesNotMatch(TargetMember, "    Public Function Find(id As String) As ExchangeAdminTarget")
    End Sub

    <Fact>
    Public Sub The_remote_layer_names_nothing_of_the_local_server_module()
        Dim files As List(Of String) = RemoteLayerFiles()
        ' not vacuous: the gate class is the first file of the layer (T-4); the library seams of T-1/T-2 are the next
        Assert.Contains(files, Function(f) Path.GetFileName(f) = "ExchangeRemoteAdminFeatures.vb")
        Assert.Contains(files, Function(f) Path.GetFileName(f) = "ExchangeAdminWire.vb")
        Dim problems As New List(Of String)()
        For Each f As String In files
            For Each hit As String In RemoteFileDefects(ExchangeTestSupport.CodeOnly(File.ReadAllText(f)))
                problems.Add(Rel(f) & ": " & hit)
            Next
        Next
        Assert.True(problems.Count = 0, "the remote administration layer reaches the local server module: " & String.Join("; ", problems))
    End Sub

    <Fact>
    Public Sub The_local_browser_card_is_not_mistaken_for_the_remote_layer()
        Dim names As List(Of String) = RemoteLayerFiles().Select(Function(f) Path.GetFileName(f)).ToList()
        Assert.DoesNotContain("Share_Settings_Form.ExchangeRemoteAdmin.vb", names)
    End Sub

    <Fact>
    Public Sub No_local_exchange_file_names_a_type_of_the_remote_layer()
        Dim remote As New HashSet(Of String)(RemoteLayerFiles(), StringComparer.OrdinalIgnoreCase)
        Dim drivers As Integer = 0
        Dim problems As New List(Of String)()
        For Each f As String In ExchangeTestSupport.CompanionSources()
            If remote.Contains(f) Then Continue For
            Dim code As String = ExchangeTestSupport.CodeOnly(File.ReadAllText(f))
            If Not LocalDriver.IsMatch(code) Then Continue For
            drivers += 1
            For Each m As Match In RemoteTypes.Matches(code)
                problems.Add(Rel(f) & ": " & m.Value)
            Next
        Next
        Assert.True(drivers >= 5, "the scan found " & drivers.ToString() & " files that drive the local server - the pattern no longer matches the sources")
        Assert.True(problems.Count = 0, "a local exchange file names the remote administration layer: " & String.Join("; ", problems.Distinct()))
    End Sub

    ' ---------------------------------------------------------------- the rule itself, on synthetic sources

    <Fact>
    Public Sub The_deny_list_catches_a_local_type_a_qualified_and_a_bare_module_call()
        Assert.Contains("ExchangeProcess", RemoteFileDefects("Dim r = ExchangeProcess.Inspect()"))
        Assert.Contains("ExchangeCredentialStore", RemoteFileDefects("Private _c As ExchangeCredentialStore"))
        Assert.Contains("ExchangeAdmin.StatusAsync", RemoteFileDefects("Dim r = Await ExchangeAdmin.StatusAsync(ct)"))
        Assert.Single(RemoteFileDefects("Dim r = Await ListAccountsAsync(ct)"))
    End Sub

    <Fact>
    Public Sub The_deny_list_lets_the_remote_layer_declare_and_call_its_own_methods_of_the_same_name()
        Assert.Empty(RemoteFileDefects("Friend Function StatusAsync(ct As CancellationToken) As Task(Of X)"))
        Assert.Empty(RemoteFileDefects("Public Async Function ListAccountsAsync() As Task" & vbLf & "End Function"))
        Assert.Empty(RemoteFileDefects("Dim r = Await _session.StatusAsync(ct)"))
        Assert.Empty(RemoteFileDefects("Dim p = ExchangeAdminParser.Status(json)"))
        Assert.Empty(RemoteFileDefects("Dim ops = New RemoteAdminOps(session) ' StatusAsync is ours"))
    End Sub

    <Fact>
    Public Sub The_reverse_pattern_allows_the_gate_class_and_nothing_else_of_the_remote_layer()
        Assert.False(RemoteTypes.IsMatch("If ExchangeRemoteAdminFeatures.IsEnabled() Then"))
        Assert.True(RemoteTypes.IsMatch("Dim s As ExchangeRemoteAdminSession"))
        Assert.True(RemoteTypes.IsMatch("Dim t = ExchangeAdminTargets.Load()"))
        Assert.True(RemoteTypes.IsMatch("RemoteAdminState.Phase(x)"))
        Assert.True(RemoteTypes.IsMatch("ExchangeRemoteAdminFeaturesX"))
    End Sub

End Class
