Option Strict On

Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Drawing
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The window-walking half of the application theme (060_SPECIFICATION_APP_THEME_SYSTEM_LIGHT_DARK.md
''' rules 5 and 7), on real WinForms controls: dark mode repaints every system colour by its role and
''' leaves the code's own colours alone, and light mode puts back exactly what was there - which is
''' the promise that lets the theme reach forms nobody rewrote for it.
''' </summary>
Public Class ThemeApplierTests

    Private NotInheritable Class Sample
        Public ReadOnly Form As New Form()
        Public ReadOnly Standard As New Button With {.Text = "OK"}
        Public ReadOnly Accent As New Button With {.Text = "QR", .FlatStyle = FlatStyle.Flat, .UseVisualStyleBackColor = False,
                                                    .BackColor = Color.FromArgb(30, 120, 220), .ForeColor = Color.White}
        Public ReadOnly Edit As New TextBox()
        Public ReadOnly Combo As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
        Public ReadOnly Note As New Label With {.ForeColor = SystemColors.GrayText}
        Public ReadOnly Status As New Label With {.ForeColor = Color.ForestGreen}
        Public ReadOnly Plain As New Label()
        Public ReadOnly Canvas As New Panel With {.BackColor = Color.FromArgb(32, 32, 32)}
        Public ReadOnly Host As New Panel()
        Public ReadOnly List As New ListView()
        Public ReadOnly Grid As New DataGridView()
        Public ReadOnly Link As New LinkLabel()

        Public Sub New()
            Host.Controls.Add(Plain)
            Form.Controls.AddRange(New Control() {Standard, Accent, Edit, Combo, Note, Status, Canvas, Host, List, Grid, Link})
        End Sub
    End Class

    <Fact>
    Public Sub Dark_repaints_system_colours_by_role_and_keeps_the_codes_own()
        Dim s As New Sample()
        Dim d As ThemePalette = ThemePalette.Dark
        ThemeApplier.ApplyTree(s.Form, d)

        Assert.Equal(d.SurfaceWindow, s.Form.BackColor)
        Assert.Equal(d.TextPrimary, s.Form.ForeColor)
        Assert.Equal(d.SurfaceRaised, s.Edit.BackColor)
        Assert.Equal(d.SurfaceRaised, s.Combo.BackColor)
        Assert.Equal(FlatStyle.Flat, s.Combo.FlatStyle)
        Assert.Equal(d.SurfaceRaised, s.List.BackColor)
        Assert.Equal(d.TextMuted, s.Note.ForeColor)
        Assert.Equal(d.Success, s.Status.ForeColor)
        Assert.Equal(d.Link, s.Link.LinkColor)

        ' A standard button is flattened onto the control role.
        Assert.Equal(FlatStyle.Flat, s.Standard.FlatStyle)
        Assert.False(s.Standard.UseVisualStyleBackColor)
        Assert.Equal(d.ControlFace, s.Standard.BackColor)

        ' The code's own colours: untouched.
        Assert.Equal(Color.FromArgb(30, 120, 220), s.Accent.BackColor)
        Assert.Equal(Color.White, s.Accent.ForeColor)
        Assert.Equal(Color.FromArgb(32, 32, 32), s.Canvas.BackColor)

        ' An inherited colour stays inherited - it follows its parent, not a role.
        Assert.Equal(d.TextPrimary, s.Plain.ForeColor)
        Assert.False(TypeDescriptor.GetProperties(s.Plain)("ForeColor").ShouldSerializeValue(s.Plain))

        Assert.False(s.Grid.EnableHeadersVisualStyles)
        Assert.Equal(d.SurfaceRaised, s.Grid.DefaultCellStyle.BackColor)
    End Sub

    <Fact>
    Public Sub Light_after_dark_puts_back_exactly_what_was_there()
        Dim s As New Sample()
        Dim before As Color() = {s.Form.BackColor, s.Edit.BackColor, s.Combo.BackColor, s.List.BackColor, s.Note.ForeColor}
        Dim originalLink As Color = s.Link.LinkColor
        Dim originalHeaders As Boolean = s.Grid.EnableHeadersVisualStyles

        ThemeApplier.ApplyTree(s.Form, ThemePalette.Dark)
        ThemeApplier.ApplyTree(s.Form, ThemePalette.Light)

        Dim after As Color() = {s.Form.BackColor, s.Edit.BackColor, s.Combo.BackColor, s.List.BackColor, s.Note.ForeColor}
        For i As Integer = 0 To before.Length - 1
            Assert.Equal(before(i).ToArgb(), after(i).ToArgb())
        Next
        Assert.Equal(Color.ForestGreen.ToArgb(), s.Status.ForeColor.ToArgb())
        Assert.Equal(FlatStyle.Standard, s.Standard.FlatStyle)
        Assert.True(s.Standard.UseVisualStyleBackColor)
        Assert.Equal(FlatStyle.Standard, s.Combo.FlatStyle)
        Assert.Equal(originalLink, s.Link.LinkColor)
        Assert.Equal(originalHeaders, s.Grid.EnableHeadersVisualStyles)
        Assert.Equal(Color.FromArgb(30, 120, 220), s.Accent.BackColor)
    End Sub

    <Fact>
    Public Sub A_control_added_after_the_theme_is_themed_too()
        Dim s As New Sample()
        ThemeApplier.ApplyTree(s.Form, ThemeService.Palette)
        Dim late As New TextBox()
        s.Host.Controls.Add(late)
        Assert.Equal(ThemeService.Palette.SurfaceRaised, late.BackColor)
    End Sub

    ''' <summary>The Share Manager's settings dialog, built for real, walks without a failure and
    ''' leaves no role-bearing system colour behind in dark mode.</summary>
    <Fact>
    Public Sub The_real_settings_dialog_is_fully_themed()
        Using f As New Share_Settings_Form()
            ThemeApplier.ApplyTree(f, ThemePalette.Dark)
            Dim leftovers As New List(Of String)()
            CollectSystemColours(f, leftovers)
            Assert.True(leftovers.Count = 0, "system colours left in dark mode: " & String.Join(", ", leftovers))
            ThemeApplier.ApplyTree(f, ThemePalette.Light)
        End Using
    End Sub

    Private Shared Sub CollectSystemColours(c As Control, into As List(Of String))
        If ThemePalette.RoleForSystemColor(c.BackColor, True) <> ThemeRole.None AndAlso Not (TypeOf c Is Button) Then
            into.Add(c.GetType().Name & "." & c.Name & " back " & c.BackColor.Name)
        End If
        If ThemePalette.RoleForSystemColor(c.ForeColor, False) <> ThemeRole.None Then
            into.Add(c.GetType().Name & "." & c.Name & " fore " & c.ForeColor.Name)
        End If
        For Each child As Control In c.Controls
            CollectSystemColours(child, into)
        Next
    End Sub

End Class
