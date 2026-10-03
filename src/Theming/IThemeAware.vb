#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' A window that is not walked by the theme, or that adds its own painting to it
''' (060_SPECIFICATION_APP_THEME_SYSTEM_LIGHT_DARK.md rule 10, APP-STYLE rule 5).
''' </summary>
Public Interface IThemeAware
    ''' <summary>True: the service never walks this window's controls - it paints itself, or it is
    ''' content-coloured and says why where it is defined. False: the service walks it first and
    ''' then calls <see cref="ApplyTheme"/> for the owner-drawn rest.</summary>
    ReadOnly Property ThemesItself As Boolean
    Sub ApplyTheme(palette As ThemePalette)
End Interface

#End If
