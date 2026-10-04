#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

' The viewer's «Ресурс» toolbar button (ticket 118 Ф-2). Created in code, not the Designer,
' so the x86 build never sees it (R5) - the same way btn_ToVideo is made. It exists only
' while at least one resource is registered: hidden through SetToolbarItemHidden, the toolbar's
' own mechanism, never a disabled stub. The decision itself is the tested rule
' ResourceUiRules.ResourceButtonShouldShow; this partial only owns the control and its triggers.
Partial Public Class Main_Form

    Private WithEvents btn_Resource As Button

    ''' <summary>Mirrors btn_ToVideo's creation (Main_Form.ToVideo.vb): a plain Button with the
    ''' chrome carrier's name, hidden until a resource exists. MUST run before BuildChromePanels -
    ''' a Nothing carrier is skipped by BuildChromeRow, which would drop the button for the
    ''' whole session.</summary>
    Friend Sub EnsureResourceButton()
        If btn_Resource IsNot Nothing Then Return
        btn_Resource = New Button With {
            .Name = "btn_Resource",
            .Text = Localization.T("Ресурс"),
            .TabStop = False,
            .Visible = False,
            .AccessibleName = Localization.T("Ресурс"),
            .AccessibleDescription = Localization.T("Открыть сохранённый ресурс..")}
        toolTip?.SetToolTip(btn_Resource, Localization.T("Открыть сохранённый ресурс.."))
        flow_Toolbar.Controls.Add(btn_Resource)
        ' Collapsed until the first resource is registered: Visible = False alone would not
        ' survive the next layout, exactly like btn_ToVideo.
        SetToolbarItemHidden(btn_Resource, True)
    End Sub

    ''' <summary>Re-reads the language-dependent words (a language switch re-reads them, like
    ''' LocalizeToVideo).</summary>
    Friend Sub LocalizeResourceButton()
        If btn_Resource Is Nothing Then Return
        btn_Resource.Text = Localization.T("Ресурс")
        btn_Resource.AccessibleName = Localization.T("Ресурс")
        btn_Resource.AccessibleDescription = Localization.T("Открыть сохранённый ресурс..")
        toolTip?.SetToolTip(btn_Resource, Localization.T("Открыть сохранённый ресурс.."))
    End Sub

    ''' <summary>The visibility trigger: the registry count read once, the tested rule applied,
    ''' then the toolbar and the chrome item re-flowed with the same call pattern the tracks
    ''' button uses (Main_Form.VideoTracks.vb). Cheap enough to call at every bounded trigger.</summary>
    Friend Sub RefreshResourceButton()
        If btn_Resource Is Nothing Then Return
        Dim show As Boolean = ResourceUiRules.ResourceButtonShouldShow(ResourceRegistry.Load().Count,
                                                                      IsArchiveMode() OrElse IsArchiveOpenPending())
        SetToolbarItemHidden(btn_Resource, Not show)
        LayoutToolbar()
        SyncChromeResourceItem()
    End Sub

    ''' <summary>Pushes the carrier's state into its chrome item at the next opportunity. The
    ''' chrome sync runs on the timer anyway; this only makes the change appear at once instead
    ''' of after the next tick.</summary>
    Private Sub SyncChromeResourceItem()
        If chrome_Panels.Count = 0 Then Return
        For Each p As ChromePanel In chrome_Panels
            For Each pair In p.Legacy
                If pair.Value Is btn_Resource Then
                    SyncChromeItem(pair.Key, pair.Value)
                    p.Host.Surface.LayoutItems()
                    Return
                End If
            Next
        Next
    End Sub

    Private Sub btn_Resource_Click(sender As Object, e As EventArgs) Handles btn_Resource.Click
        ' The chrome item carries its own action (ChromeShowResourceMenu); this handler only
        ' answers the legacy docked-toolbar path, which the modern viewer keeps hidden.
        ChromeShowResourceMenuFromCarrier()
    End Sub

    ''' <summary>The docked fallback: the same popup, anchored to the carrier itself.</summary>
    Private Sub ChromeShowResourceMenuFromCarrier()
        If chrome_Panels.Count > 0 Then
            For Each p As ChromePanel In chrome_Panels
                For Each pair In p.Legacy
                    If pair.Value Is btn_Resource Then
                        ChromeShowResourceMenu(pair.Key)
                        Return
                    End If
                Next
            Next
        End If
    End Sub

End Class
#End If
