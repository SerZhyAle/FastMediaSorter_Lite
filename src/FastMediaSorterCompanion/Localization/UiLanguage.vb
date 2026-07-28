Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The UI-language picker for the Share Manager, and what has to happen when the
''' choice changes.
'''
''' Companion's windows are built entirely in code, in one pass, with every caption
''' resolved at construction time - there is no LngCh()-style re-caption pass as in the
''' viewer. So a language switch does not re-label the live window: it rebuilds it.
''' That is why <see cref="Changed"/> exists - TrayContext owns the window and the tray
''' menu, so it is the one that can recreate both.
'''
''' See docs/specifications/SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block A'.
''' </summary>
Friend Module UiLanguage

    ''' <summary>Raised after the new language has been applied and persisted.</summary>
    Friend Event Changed As EventHandler

    ''' <summary>
    ''' Switch the interface language. Writes the same registry values the viewer reads,
    ''' so the two programs stay in step (invariant 9) - the viewer picks it up on its
    ''' next start, exactly as Companion picks up a switch made there.
    ''' </summary>
    Friend Sub Switch(code As String)
        If String.IsNullOrEmpty(code) Then Return
        If String.Equals(Localization.CurrentCode, code, StringComparison.OrdinalIgnoreCase) Then Return
        Localization.CurrentCode = code
        Try
            Localization.SaveToSettings()
        Catch
            ' A locked/denied HKCU still leaves the switch working for this session.
        End Try
        RaiseEvent Changed(Nothing, EventArgs.Empty)
    End Sub

    ''' <summary>
    ''' Fills a menu with one entry per shipped language, each named in its own script.
    ''' No flag icons here - unlike the viewer, Companion embeds no flag bitmaps, and an
    ''' endonym identifies the language to the person who reads it.
    ''' </summary>
    Friend Sub AddLanguageItems(items As ToolStripItemCollection)
        If items Is Nothing Then Return
        For i = 0 To Localization.Codes.Length - 1
            Dim code = Localization.Codes(i)
            Dim item As New ToolStripMenuItem(Localization.Names(i)) With {
                .Checked = (code = Localization.CurrentCode),
                .Tag = code}
            AddHandler item.Click, Sub(s, e) Switch(CStr(DirectCast(s, ToolStripMenuItem).Tag))
            items.Add(item)
        Next
    End Sub

    ''' <summary>Drops the language list under <paramref name="anchor"/>.</summary>
    Friend Sub ShowLanguageMenu(anchor As Control)
        If anchor Is Nothing Then Return
        Dim menu As New ContextMenuStrip()
        AddLanguageItems(menu.Items)
        menu.Show(anchor, New Point(0, anchor.Height))
    End Sub

    ''' <summary>
    ''' Applies the script font and text direction to a window being built.
    '''
    ''' Setting Me.Font is safe here in a way it is not in the viewer: these layouts scale
    ''' by <c>AutoScaleMode.Dpi</c> (see DpiLayout), so the font is not the scaling
    ''' baseline and swapping the family does not move anything. The SIZE is kept - only
    ''' the family changes, and only for scripts the app-wide Segoe UI cannot draw.
    '''
    ''' <c>RightToLeft</c> only - deliberately NOT <c>RightToLeftLayout</c>. That second
    ''' flag sets WS_EX_LAYOUTRTL, which mirrors the whole window DEVICE CONTEXT: captured
    ''' at 175% DPI it came out as a mirror image, Arabic glyphs and the Latin title bar
    ''' alike, rather than a right-to-left layout. RightToLeft alone gives what is actually
    ''' wanted - right-aligned text and reversed column/flow order in the layout panels
    ''' every Companion window is built from. Same conclusion the viewer reached
    ''' (SPECIFICATION_THIRTEEN_UI_LANGUAGES.md §2.6 г), for a different reason.
    '''
    ''' Call as the FIRST statement of the form's builder, before any control is added:
    ''' children inherit both, and the direction has to be set before the layout is
    ''' measured.
    ''' </summary>
    Friend Sub ApplyTo(f As Form)
        If f Is Nothing Then Return

        Dim family = Localization.FontFamily()
        If family IsNot Nothing Then
            Try
                Dim candidate As New Font(family, f.Font.Size)
                ' A missing family silently substitutes something that may not cover the
                ' script either - keep Segoe UI rather than swap to an unknown.
                If String.Equals(candidate.FontFamily.Name, family, StringComparison.OrdinalIgnoreCase) Then
                    f.Font = candidate
                Else
                    candidate.Dispose()
                End If
            Catch
            End Try
        End If

        If Localization.IsRightToLeft() Then f.RightToLeft = RightToLeft.Yes
    End Sub

End Module
