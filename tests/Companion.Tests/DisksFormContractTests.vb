Option Strict On

Imports System.Drawing
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The disk-management window against the desktop UX contracts (APP-BEHAVIOUR rule 1, APP-STYLE
''' section 4 `text.disabled`): a modal window that asks something has one way out that Escape, the
''' close box and a button share, and a control the user cannot use is still legible.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class DisksFormContractTests

    <Fact>
    Public Sub The_disks_window_leaves_by_one_path_that_escape_shares()
        Using form As New Share_Disks_Form()
            Assert.NotNull(form.CancelButton)
            Dim leave As Button = DirectCast(form.CancelButton, Button)
            Assert.Equal(DialogResult.Cancel, leave.DialogResult)
            Assert.False(form.MinimizeBox)
        End Using
    End Sub

    <Fact>
    Public Sub The_disks_window_hides_what_cannot_work_instead_of_greying_it()
        Using form As New Share_Disks_Form()
            ' No worker status yet = disk sharing is not available: no dead toolbar, no empty list.
            ' Show() runs Load synchronously; the test thread pumps nothing, so the window's own
            ' status refresh cannot complete (or find a real worker) before the assertions.
            form.Show()
            Assert.False(OwnVisible(FindByText(form, Localization.T("Подключить .fdd.."))))
            ' The per-disk actions live in one row; the row is what is hidden.
            Assert.False(OwnVisible(FindByText(form, Localization.T("Открыть..")).Parent))
            Assert.True(OwnVisible(FindByText(form, Localization.T("Обновить"))))
        End Using
    End Sub

    <Fact>
    Public Sub The_disk_manager_button_stays_offered_while_disk_sharing_is_unavailable()
        Using form As New Share_Disks_Form()
            form.Show()
            ' A disk held by FileDO has to be unmounted there before anything else here can work.
            Assert.True(OwnVisible(FindByText(form, Localization.T("Открыть менеджер дисков FileDO"))))
        End Using
    End Sub

    ''' <summary>The control's own Visible flag - Control.Visible also answers False for every child of
    ''' a window that was never shown, which would make this check pass for the wrong reason.</summary>
    Private Shared Function OwnVisible(c As Control) As Boolean
        Dim getState = GetType(Control).GetMethod("GetState", Reflection.BindingFlags.NonPublic Or Reflection.BindingFlags.Instance)
        Return CBool(getState.Invoke(c, New Object() {2}))
    End Function

    <Fact>
    Public Sub A_disabled_dark_button_keeps_its_caption_legible()
        Using form As New Form(), button As New Button With {.Text = "Open..", .Width = 160, .Height = 60, .Enabled = False}
            form.Controls.Add(button)
            form.BackColor = Color.FromArgb(32, 32, 32)
            Dim palette As ThemePalette = ThemePalette.Dark
            ThemeApplier.ApplyTree(form, palette)
            form.Show()
            Using bitmap As New Bitmap(160, 60)
                button.DrawToBitmap(bitmap, New Rectangle(0, 0, 160, 60))
                Dim face As Color = bitmap.GetPixel(4, 4)
                Dim best As Double = 0
                For x As Integer = 10 To 150
                    For y As Integer = 10 To 50
                        best = Math.Max(best, Contrast(bitmap.GetPixel(x, y), face))
                    Next
                Next
                Assert.True(best >= 3.0, "caption contrast on the face: " & best.ToString("0.00"))
            End Using
        End Using
    End Sub

    Private Shared Function FindByText(root As Control, text As String) As Control
        For Each child As Control In root.Controls
            If TypeOf child Is Button AndAlso child.Text = text Then Return child
            Dim found As Control = FindByText(child, text)
            If found IsNot Nothing Then Return found
        Next
        Return Nothing
    End Function

    Private Shared Function Contrast(a As Color, b As Color) As Double
        Dim la As Double = Luminance(a), lb As Double = Luminance(b)
        Return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05)
    End Function

    Private Shared Function Luminance(c As Color) As Double
        Dim Lin = Function(v As Integer) As Double
                      Dim s As Double = v / 255.0
                      Return If(s <= 0.03928, s / 12.92, Math.Pow((s + 0.055) / 1.055, 2.4))
                  End Function
        Return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B)
    End Function
End Class
