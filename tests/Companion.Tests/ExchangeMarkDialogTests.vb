Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>Spec 248 acceptance 4 for the Share Manager: every window of the table that shows the mark (M4, M6, M8) wears
''' it as a named picture, and no source bypasses the shared helper, so the name cannot be forgotten at one place.
''' Real windows are built and never shown: the helper adds its picture and sets the accessible name while the window
''' is built, so no server is started, no probe is run and no setting is written.</summary>
<Collection(UiLanguageCollection.Name)>
Public Class ExchangeMarkDialogTests

    ''' <summary>One Share Manager window of the table, made by the same call its other tests use.</summary>
    Private NotInheritable Class Surface
        Friend ReadOnly Name As String
        Friend ReadOnly Make As Func(Of Func(Of ExchangeDeviceAgent), Form)

        Friend Sub New(name As String, make As Func(Of Func(Of ExchangeDeviceAgent), Form))
            Me.Name = name
            Me.Make = make
        End Sub
    End Class

    Private Shared Function Made(name As String, login As String) As ExchangeProcess.ConnectionStringResult
        Return New ExchangeProcess.ConnectionStringResult With {.Outcome = ExchangeProcess.ConnectionStringOutcome.Made, .Text = "FMSEXCH1:test"}
    End Function

    Private Shared Function Surfaces() As List(Of Surface)
        Dim qr As Byte() = ShareConfigBuilder.QrPngForText("FMSSFTP2:test")
        Dim fp As String = "AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12"
        Dim accounts As IReadOnlyList(Of String) = New List(Of String) From {"alice", "bob"}
        Dim ports As New ExchangeFirstRunUiTests.Actions()
        Return New List(Of Surface) From {
            New Surface("Exchange_Server_Wizard_Form", Function(agent) New Exchange_Server_Wizard_Form(New ExchangeServerWizardTests.StubPorts(), "operator", firstRun:=True)),
            New Surface("Exchange_Account_Form", Function(agent) New Exchange_Account_Form(agent())),
            New Surface("Exchange_Account_Admin_Form", Function(agent) New Exchange_Account_Admin_Form(Exchange_Account_Admin_Form.DialogMode.Add)),
            New Surface("Exchange_Account_Limits_Form", Function(agent) New Exchange_Account_Limits_Form("user@exchange.example", Nothing)),
            New Surface("Exchange_Password_Form", Function(agent) New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Enter)),
            New Surface("Exchange_Password_Change_Form", Function(agent) New Exchange_Password_Change_Form("user@exchange.example")),
            New Surface("Exchange_Connection_String_Form", Function(agent) New Exchange_Connection_String_Form(AddressOf Made)),
            New Surface("Exchange_Invitation_Create_Form", Function(agent) New Exchange_Invitation_Create_Form()),
            New Surface("Exchange_Invitation_Created_Form", Function(agent) New Exchange_Invitation_Created_Form("INV-CODE-0001", "FMSEXCH1:test", "member")),
            New Surface("Exchange_Shared_Invitation_Form", Function(agent) New Exchange_Shared_Invitation_Form("member")),
            New Surface("Exchange_Link_Form", Function(agent) New Exchange_Link_Form("Holiday photos", "FMSSFTP2:test", "{}", qr)),
            New Surface("Exchange_Local_Account_Picker_Form", Function(agent) New Exchange_Local_Account_Picker_Form(accounts)),
            New Surface("Exchange_Admin_Browser_Form", Function(agent) New Exchange_Admin_Browser_Form("https://exch.example.com:22022/#admin", fp)),
            New Surface("Exchange_Admin_Audit_Form", Function(agent) New Exchange_Admin_Audit_Form(New ExchangeAdminAuditView())),
            New Surface("Connection_Diagnosis_Dialog", Function(agent) New Connection_Diagnosis_Dialog(Nothing)),
            New Surface("Server_Welcome_Form (exchange)", Function(agent) New Server_Welcome_Form(WelcomeHalf.Exchange, ports, ports.Snapshot))}
    End Function

    Private Shared Function Descendants(parent As Control) As List(Of Control)
        Dim found As New List(Of Control)()
        For Each child As Control In parent.Controls
            found.Add(child)
            found.AddRange(Descendants(child))
        Next
        Return found
    End Function

    <Fact>
    Public Sub Every_manager_window_that_shows_the_exchange_mark_names_it_by_the_CW_term()
        ExchangeTestSupport.OnUi(
            Sub()
                ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                Using rig As New ExchangeDeviceAgentTests.Rig()
                    Dim agents As New List(Of ExchangeDeviceAgent)()
                    Dim newAgent As Func(Of ExchangeDeviceAgent) =
                        Function()
                            Dim agent As ExchangeDeviceAgent = rig.NewAgent()
                            agents.Add(agent)
                            Return agent
                        End Function
                    Try
                        For Each entry In Surfaces()
                            Using form As Form = entry.Make(newAgent)
                                Dim marks As List(Of Control) = Descendants(form).Where(Function(c) c.Name = "exchangeMark").ToList()
                                Assert.True(marks.Count > 0, entry.Name & " must show the mark")
                                For Each mark In marks
                                    Assert.Equal(ExchangeMark.AccessibleName(), mark.AccessibleName)
                                    Assert.Equal(AccessibleRole.Graphic, mark.AccessibleRole)
                                    Assert.False(String.IsNullOrWhiteSpace(mark.AccessibleName), entry.Name & ": the picture has no name")
                                Next
                            End Using
                        Next
                    Finally
                        For Each agent In agents
                            agent.Dispose()
                        Next
                    End Try
                End Using
            End Sub)
    End Sub

    ''' <summary>The title bar belongs to the form icon, which stays the product's own (Q248-2, rule 12): the mark is in the
    ''' body, so no source sets a form icon from the exchange artwork.</summary>
    <Fact>
    Public Sub No_source_sets_a_form_icon_from_the_exchange_artwork()
        Dim pattern As New Regex("\.Icon\s*=[^\r\n]*(exchange-mark|ExchangeMark|exchange-server\.ico)", RegexOptions.IgnoreCase)
        Dim offenders As List(Of String) = SourcesUnderSrc().Where(Function(p) pattern.IsMatch(File.ReadAllText(p))).ToList()
        Assert.True(offenders.Count = 0, "A form icon must not be the exchange artwork: " & String.Join(", ", offenders.Select(Function(p) Path.GetFileName(p))))
    End Sub

    ''' <summary>The artwork has one way in: the shared helper (pictures and accessible name) and the glyph renderer (the
    ''' one-tone glyph of a control whose own text is its name). A source that opens the files itself would draw a picture
    ''' nothing names.</summary>
    <Fact>
    Public Sub Only_the_shared_helper_and_the_glyph_path_open_the_exchange_artwork()
        Dim allowed As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {"ExchangeMark.vb", "GlyphRenderer.vb", "IconMap.vb", "TrayContext.vb"}
        Dim offenders As List(Of String) = SourcesUnderSrc().
            Where(Function(p) Not allowed.Contains(Path.GetFileName(p))).
            Where(Function(p) File.ReadAllText(p).Contains("exchange-mark/", StringComparison.Ordinal)).
            Select(Function(p) Path.GetFileName(p)).ToList()
        Assert.True(offenders.Count = 0, "These sources open the exchange artwork without the helper: " & String.Join(", ", offenders))
    End Sub

    Private Shared Function SourcesUnderSrc() As IEnumerable(Of String)
        Dim sep As String = Path.DirectorySeparatorChar
        Return Directory.EnumerateFiles(Path.Combine(ExchangeTestSupport.RepoRoot(), "src"), "*.vb", SearchOption.AllDirectories).
            Where(Function(p) Not p.Contains(sep & "obj" & sep, StringComparison.OrdinalIgnoreCase) AndAlso
                              Not p.Contains(sep & "bin" & sep, StringComparison.OrdinalIgnoreCase))
    End Function
End Class
