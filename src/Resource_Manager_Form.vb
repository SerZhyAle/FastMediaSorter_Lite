#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The resources the user has set up (007 §3.2): servers and network shares that are not a drive letter,
''' each opened like an ordinary folder. A small dialog, not a program of its own. Since ticket 118 the
''' list and its actions live in <see cref="Resource_List_Panel"/>, hosted here AND on the Settings
''' window's own resources page - one implementation, two hosts. What remains here is the dialog's own
''' chrome: Open hands the resource back to the viewer (which owns the connection, the trust prompt and
''' the folder it becomes), Close closes, and a typed sftp:// address opens the "new resource" form
''' filled in.
''' </summary>
Friend NotInheritable Class Resource_Manager_Form
    Inherits Form

    Private ReadOnly panel As Resource_List_Panel
    Private ReadOnly openButton As Button
    Private ReadOnly closeButton As Button
    Private script_Font As Font

    ''' <summary>The resource the user chose to open, or Nothing.</summary>
    <System.ComponentModel.Browsable(False), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property ToOpen As ResourceDescriptor

    ''' <summary>Ids removed while the dialog was open, so the viewer can close one that is on screen.</summary>
    Friend ReadOnly Property RemovedIds As IReadOnlyList(Of String)
        Get
            Return panel.RemovedIds
        End Get
    End Property

    ''' <param name="draft">An address the user typed that matches no saved resource: the dialog opens the
    ''' "new resource" form with it already filled in.</param>
    ''' <param name="openShareManager">The handover a managed share's Edit/Remove becomes. The viewer
    ''' names its own Share entry point; Nothing keeps the panel's quiet form-free default, which is
    ''' what the tests construct with.</param>
    ''' <param name="shareInstalledProbe">Spec 227 rule 2: the host's "Share Manager installed" probe
    ''' for the camera item's gate. Nothing keeps the panel's own file probe.</param>
    ''' <param name="scanPhoneCodeCamera">Spec 227 rule 2: the host's camera hand-over to the Share
    ''' Manager's add-resource flow. Nothing keeps the item absent.</param>
    Friend Sub New(Optional draft As ResourceDescriptor = Nothing,
                   Optional openShareManager As Action = Nothing,
                   Optional shareInstalledProbe As Func(Of Boolean) = Nothing,
                   Optional scanPhoneCodeCamera As Action = Nothing)
        Me.Text = Localization.T("Ресурсы")
        Me.FormBorderStyle = FormBorderStyle.Sizable
        Me.MinimumSize = New Size(640, 340)
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ClientSize = New Size(700, 372)

        Dim family As String = Localization.FontFamily()
        If family IsNot Nothing Then
            script_Font = New Font(family, Me.Font.Size)
            Me.Font = script_Font
        End If
        If Localization.IsRightToLeft() Then Me.RightToLeft = RightToLeft.Yes

        ' The shared list+actions panel (the Settings window's resources page embeds the same one).
        ' A managed share's Edit/Remove hands the user over to the Share Manager - the viewer
        ' passes its own wake (Main_Form.ShowResourceManager), the tests construct without one.
        panel = New Resource_List_Panel(openShareManager:=openShareManager,
                                        shareInstalledProbe:=shareInstalledProbe,
                                        scanPhoneCodeCamera:=scanPhoneCodeCamera) With {.Dock = DockStyle.Fill}
        AddHandler panel.ItemActivated, AddressOf OnOpenFromPanel
        AddHandler panel.SelectionChanged, Sub(has As Boolean) openButton.Enabled = has

        ' The dialog's own chrome: Open at the left of the bottom strip, Close at its right.
        openButton = New Button With {.Name = "res_Open", .Text = Localization.T("Открыть"), .Size = New Size(92, 28),
                                      .Location = New Point(12, 6), .Enabled = False,
                                      .Anchor = AnchorStyles.Bottom Or AnchorStyles.Left}
        AddHandler openButton.Click, AddressOf OnOpen
        ButtonGlyphs.Apply(openButton, "action.confirm", ButtonGlyphRole.Affirmative)
        closeButton = New Button With {.Name = "res_Close", .Text = Localization.T("Закрыть"), .Size = New Size(92, 28),
                                       .Location = New Point(ClientSize.Width - 104, 6),
                                       .DialogResult = DialogResult.Cancel,
                                       .Anchor = AnchorStyles.Bottom Or AnchorStyles.Right}
        ButtonGlyphs.Apply(closeButton, "nav.close", ButtonGlyphRole.Dismissing)

        Dim bottom As New Panel With {.Dock = DockStyle.Bottom, .Height = 40}
        bottom.Controls.Add(openButton)
        bottom.Controls.Add(closeButton)

        ' Docking is laid out from the end of the collection: the bottom strip takes its edge
        ' first, the shared panel fills the rest.
        Controls.Add(panel)
        Controls.Add(bottom)
        Me.CancelButton = closeButton

        RefillForFirstShow()
        Me.pendingDraft = draft

        ' LAST line of the builder: scaling touches only the children that exist by now (124 UX-202).
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DialogLayout.ClampToWorkingArea(Me)
        FitStripCaptions()
    End Sub

    ''' <summary>The bottom strip is docked, so DialogLayout.FitContent leaves it alone: a caption with its glyph
    ''' must not be clipped by the fixed 92 px, so each button grows to what it needs - Open towards the end, Close
    ''' keeping its end edge. The strip keeps its height and the buttons their one height.</summary>
    Private Sub FitStripCaptions()
        PerformAutoScale()
        Dim padding As Integer = DialogLayout.Device(Me, 24)
        Dim openWidth As Integer = Math.Max(openButton.Width,
            TextRenderer.MeasureText(openButton.Text, openButton.Font).Width + ButtonGlyphs.ExtraWidth(openButton) + padding)
        openButton.SetBounds(openButton.Left, openButton.Top, openWidth, openButton.Height)
        Dim closeWidth As Integer = Math.Max(closeButton.Width,
            TextRenderer.MeasureText(closeButton.Text, closeButton.Font).Width + ButtonGlyphs.ExtraWidth(closeButton) + padding)
        closeButton.SetBounds(closeButton.Right - closeWidth, closeButton.Top, closeWidth, closeButton.Height)
    End Sub

    Private pendingDraft As ResourceDescriptor

    ''' <summary>The panel refills itself in its constructor; only the Open button starts off.</summary>
    Private Sub RefillForFirstShow()
        openButton.Enabled = False
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If pendingDraft IsNot Nothing Then
            Dim draft As ResourceDescriptor = pendingDraft
            pendingDraft = Nothing
            panel.BeginAdd(draft)
        End If
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        If disposing Then
            script_Font?.Dispose()
        End If
    End Sub

    ' ---------------------------------------------------------------------------- open ----

    Private Sub OnOpen(Optional sender As Object = Nothing, Optional e As EventArgs = Nothing)
        OpenSelected()
    End Sub

    ''' <summary>A double-click means the same thing as the Open button - the dialog's one
    ''' answer to <see cref="Resource_List_Panel.ItemActivated"/>.</summary>
    Private Sub OnOpenFromPanel(resource As ResourceDescriptor)
        OpenSelected()
    End Sub

    Private Sub OpenSelected()
        Dim selected As ResourceDescriptor = panel.SelectedResource
        If selected Is Nothing Then Return
        ToOpen = selected
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

End Class
#End If
