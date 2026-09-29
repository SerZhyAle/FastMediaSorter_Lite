#If Not NETFRAMEWORK Then
Option Strict On

' The main window and the application theme (060_SPECIFICATION_APP_THEME_SYSTEM_LIGHT_DARK.md
' rule 10).
'
' DECLARED OUTSIDE THE THEME - a content-coloured window (APP-STYLE rule 5, amendment 0.10). Its
' background, its toolbar and its status strip take their colour from the picture through the
' background scheme (Form_Color_Scheme: dynamic, black, white, sampled) - that IS the product, and
' a themed charcoal behind a photo would be the defect. Everything drawn over the picture carries
' its own contrast already (the OCR plates, the info HUD, the recipients overlay).
'
' What still follows the theme, as the amendment requires: every menu this window opens (they
' paint through ToolStripManager.Renderer) and every dialog it opens (the service themes each
' on its first paint). So the service must not walk THIS window's controls, and has nothing to add.
Partial Public Class Main_Form
    Implements IThemeAware

    Private ReadOnly Property ThemesItself As Boolean Implements IThemeAware.ThemesItself
        Get
            Return True
        End Get
    End Property

    Private Sub ApplyTheme(palette As ThemePalette) Implements IThemeAware.ApplyTheme
        ' Content-coloured: nothing here follows the palette (see the file remark).
    End Sub

End Class
#End If
