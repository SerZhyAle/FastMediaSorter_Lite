Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

' Two-row toolbar with a per-row overflow menu.
'
' The chrome used to live in a wrapping FlowLayoutPanel with a single flow break:
' two rows at wide widths, but as the window narrowed each row wrapped onto more
' rows, growing the toolbar downward and covering the top of the image. This
' partial keeps the familiar two rows (flow_Toolbar is a plain Panel now) but
' instead of wrapping, whatever no longer fits on a row collapses into a "»"
' dropdown button pinned to that row's right edge:
'   * Row 1 - always-on-top / sort / "Folder:" / folder box, then file & folder
'     actions. The folder box shrinks proportionally to make room.
'   * Row 2 - navigation and file actions (recent, prev/next, slideshow, rename,
'     delete, translate).
Partial Public Class Main_Form

    Private WithEvents btn_Overflow As Button      ' row 1
    Private WithEvents btn_Overflow_2 As Button    ' row 2
    Private overflow_Menu As ContextMenuStrip
    Private overflow_Menu_2 As ContextMenuStrip

    ' Row height shared with ApplyModernStyling (set there); gap between the rows.
    Private toolbar_Row_Height As Integer = 24
    Private Const Toolbar_Row_Gap As Integer = 2

    ' Row 1 tail (after the priority core + folder box) and the whole of row 2, in
    ' reading order. Built by BuildToolbarOverflow.
    Private toolbar_Row1_Tail As Control()
    Private toolbar_Row2_Items As Control()

    ' Toolbar items intentionally collapsed out of the layout entirely (kept hidden,
    ' excluded from both the row and its overflow menu). PlaceControl force-shows any
    ' laid-out control, so a plain c.Visible = False would be undone on the next
    ' LayoutToolbar - callers hide via SetToolbarItemHidden instead. Used by the
    ' translate buttons, which are shown only for still images.
    Private ReadOnly toolbar_Hidden_Items As New HashSet(Of Control)()

    ' Folder box width bounds (px). Preferred matches ApplyModernStyling's 320.
    Private Const Folder_Box_Pref_Width As Integer = 320
    Private Const Folder_Box_Min_Width As Integer = 90

    ' Guards the self-triggered SizeChanged that setting the panel height raises.
    Private laying_Out_Toolbar As Boolean = False

    ''' <summary>
    ''' Creates the two right-edge overflow buttons + menus and records the row
    ''' item order. Called from BuildModernLayout after the OCR/Share buttons are
    ''' added and before ApplyModernStyling (so the buttons are styled uniformly).
    ''' </summary>
    Friend Sub BuildToolbarOverflow(host As Panel)
        If host Is Nothing OrElse btn_Overflow IsNot Nothing Then Return

        btn_Overflow = New Button With {.Name = "btn_Overflow", .Text = "»", .AutoSize = True, .TabStop = False, .Visible = False}
        btn_Overflow_2 = New Button With {.Name = "btn_Overflow_2", .Text = "»", .AutoSize = True, .TabStop = False, .Visible = False}
        host.Controls.Add(btn_Overflow)
        host.Controls.Add(btn_Overflow_2)

        overflow_Menu = New ContextMenuStrip()
        overflow_Menu_2 = New ContextMenuStrip()

        ' Row 1 tail (the priority core chkbox/sort/folder-label/folder-box is
        ' handled explicitly in LayoutRow1).
        toolbar_Row1_Tail = New Control() {
            btn_choose_file, btn_Select_Folder, btn_Review, btn_Panel,
            btn_Full_Screen, lbl_Slideshow_Time, btn_Language, lbl_Info}

        ' Row 2 (navigation + file actions). The two translate buttons sit at the
        ' end - our OCR overlay, then the "in browser" companion (doc-html-translate).
        ' A List, not an array literal, because the modern build has one more item and
        ' two copies of this list would drift apart on the first edit.
        Dim row2 As New List(Of Control) From {
            btn_RecentFiles, lbl_File_Number, btn_Prev_File, btn_Next_File,
            btn_Next_Random, btn_Random_Slideshow, btn_Slideshow,
            btn_Move_Table, btn_Rename, bt_Delete, lbl_Zoom,
            btn_Translate, btn_TranslateBrowser}
#If Not NETFRAMEWORK Then
        ' Video-only, and mutually exclusive with the image-only translate buttons -
        ' whichever media is up, only one of them is ever in the row.
        row2.Add(btn_Tracks)
#End If
        toolbar_Row2_Items = row2.ToArray()
    End Sub

    ''' <summary>Localizes the overflow button tooltips. Called from
    ''' InitializeTooltips and LngCh.</summary>
    Friend Sub LocalizeToolbarOverflow()
        If toolTip Is Nothing Then Return
        Dim tip As String = Localization.T("Ещё - кнопки, не поместившиеся в строку.")
        If btn_Overflow IsNot Nothing Then toolTip.SetToolTip(btn_Overflow, tip)
        If btn_Overflow_2 IsNot Nothing Then toolTip.SetToolTip(btn_Overflow_2, tip)
    End Sub

    Private Function EffWidth(c As Control) As Integer
        If c Is cmbox_Media_Folder Then Return c.Width
        If c.AutoSize Then Return Math.Max(c.Width, c.PreferredSize.Width)
        Return c.Width
    End Function

    Private Function SlotWidth(c As Control) As Integer
        Return c.Margin.Left + EffWidth(c) + c.Margin.Right
    End Function

    Private Function CenterTop(c As Control, rowTop As Integer, rowH As Integer) As Integer
        Return rowTop + Math.Max(0, (rowH - c.Height) \ 2)
    End Function

    ''' <summary>Places a control at the running x cursor and advances it.</summary>
    Private Sub PlaceControl(c As Control, ByRef x As Integer, rowTop As Integer, rowH As Integer)
        c.Visible = True
        x += c.Margin.Left
        c.Left = x
        c.Top = CenterTop(c, rowTop, rowH)
        x += EffWidth(c) + c.Margin.Right
    End Sub

    ''' <summary>
    ''' Lays the toolbar out as two rows, shrinking the folder box and spilling a
    ''' row's trailing controls into its overflow menu as needed. Cheap; safe to
    ''' call on every resize / navigation.
    ''' </summary>
    Friend Sub LayoutToolbar()
        If Not modern_Layout_Built OrElse flow_Toolbar Is Nothing Then Return
        If toolbar_Row2_Items Is Nothing OrElse cmbox_Media_Folder Is Nothing Then Return
        If laying_Out_Toolbar Then Return

        laying_Out_Toolbar = True
        Try
            Dim rowH As Integer = toolbar_Row_Height
            Dim pad As Padding = flow_Toolbar.Padding

            ' Fix the panel to exactly two rows (this is what stops the toolbar
            ' from growing down over the image).
            Dim desiredHeight As Integer = pad.Top + rowH + Toolbar_Row_Gap + rowH + pad.Bottom
            If flow_Toolbar.Height <> desiredHeight Then flow_Toolbar.Height = desiredHeight

            Dim innerLeft As Integer = pad.Left
            Dim innerRight As Integer = flow_Toolbar.ClientSize.Width - pad.Right
            Dim rowTop1 As Integer = pad.Top
            Dim rowTop2 As Integer = pad.Top + rowH + Toolbar_Row_Gap

            RebuildOverflowMenu(overflow_Menu, LayoutRow1(rowTop1, rowH, innerLeft, innerRight))
            RebuildOverflowMenu(overflow_Menu_2, LayoutRow2(rowTop2, rowH, innerLeft, innerRight))
        Finally
            laying_Out_Toolbar = False
        End Try
    End Sub

    ''' <summary>Row 1: priority core (checkbox / sort / "Folder:" label) + the
    ''' flexible folder box + the file/folder action tail that overflows.</summary>
    Private Function LayoutRow1(rowTop As Integer, rowH As Integer, innerLeft As Integer, innerRight As Integer) As List(Of Control)
        Dim x As Integer = innerLeft

        For Each c As Control In New Control() {chkbox_Top_Most, cmbox_Sort, lbl_Folder}
            If c IsNot Nothing Then PlaceControl(c, x, rowTop, rowH)
        Next

        Dim folder As Control = cmbox_Media_Folder
        Dim folderMH As Integer = folder.Margin.Horizontal

        Dim ovf As New List(Of Control)()
        For Each c As Control In toolbar_Row1_Tail
            If c Is Nothing Then Continue For
            If toolbar_Hidden_Items.Contains(c) Then
                c.Visible = False : Continue For
            End If
            ovf.Add(c)
        Next
        Dim ovfTotal As Integer = 0
        For Each c As Control In ovf : ovfTotal += SlotWidth(c) : Next

        Dim avail As Integer = innerRight - x

        Dim folderW As Integer
        Dim showOverflow As Boolean
        If (folderMH + Folder_Box_Pref_Width) + ovfTotal <= avail Then
            folderW = Folder_Box_Pref_Width : showOverflow = False
        ElseIf (avail - ovfTotal - folderMH) >= Folder_Box_Min_Width Then
            folderW = avail - ovfTotal - folderMH : showOverflow = False
        Else
            folderW = Folder_Box_Min_Width : showOverflow = True
        End If

        btn_Overflow.Visible = showOverflow
        Dim spaceForOvf As Integer = 0
        If showOverflow Then
            spaceForOvf = avail - (folderMH + Folder_Box_Min_Width) - (btn_Overflow.Margin.Horizontal + EffWidth(btn_Overflow))
        End If

        ' Folder box (flexible width).
        x += folder.Margin.Left
        folder.Width = folderW
        folder.Left = x
        folder.Top = CenterTop(folder, rowTop, rowH)
        folder.Visible = True
        x += folderW + folder.Margin.Right

        Dim menu As New List(Of Control)()
        Dim used As Integer = 0
        Dim overflowing As Boolean = False
        For Each c As Control In ovf
            Dim slot As Integer = SlotWidth(c)
            If showOverflow AndAlso (overflowing OrElse (used + slot) > spaceForOvf) Then
                overflowing = True : c.Visible = False : menu.Add(c)
            Else
                PlaceControl(c, x, rowTop, rowH) : used += slot
            End If
        Next

        If showOverflow Then PinOverflowButton(btn_Overflow, rowTop, rowH, innerRight)
        Return menu
    End Function

    ''' <summary>Row 2: navigation + file actions, overflowing from the end.</summary>
    Private Function LayoutRow2(rowTop As Integer, rowH As Integer, innerLeft As Integer, innerRight As Integer) As List(Of Control)
        Dim x As Integer = innerLeft

        Dim ovf As New List(Of Control)()
        For Each c As Control In toolbar_Row2_Items
            If c Is Nothing Then Continue For
            If toolbar_Hidden_Items.Contains(c) Then
                c.Visible = False : Continue For
            End If
            ovf.Add(c)
        Next
        Dim total As Integer = 0
        For Each c As Control In ovf : total += SlotWidth(c) : Next

        Dim avail As Integer = innerRight - innerLeft
        Dim showOverflow As Boolean = total > avail
        btn_Overflow_2.Visible = showOverflow
        Dim spaceForOvf As Integer = 0
        If showOverflow Then
            spaceForOvf = avail - (btn_Overflow_2.Margin.Horizontal + EffWidth(btn_Overflow_2))
        End If

        Dim menu As New List(Of Control)()
        Dim used As Integer = 0
        Dim overflowing As Boolean = False
        For Each c As Control In ovf
            Dim slot As Integer = SlotWidth(c)
            If showOverflow AndAlso (overflowing OrElse (used + slot) > spaceForOvf) Then
                overflowing = True : c.Visible = False : menu.Add(c)
            Else
                PlaceControl(c, x, rowTop, rowH) : used += slot
            End If
        Next

        If showOverflow Then PinOverflowButton(btn_Overflow_2, rowTop, rowH, innerRight)
        Return menu
    End Function

    ''' <summary>Collapses a toolbar item out of the layout entirely (or restores
    ''' it). Hidden items are skipped by LayoutRow1/LayoutRow2 and never re-shown by
    ''' PlaceControl. Call LayoutToolbar afterwards to re-flow.</summary>
    Friend Sub SetToolbarItemHidden(c As Control, hidden As Boolean)
        If c Is Nothing Then Return
        If hidden Then
            toolbar_Hidden_Items.Add(c)
            c.Visible = False
        Else
            toolbar_Hidden_Items.Remove(c)
        End If
    End Sub

    Private Sub PinOverflowButton(b As Button, rowTop As Integer, rowH As Integer, innerRight As Integer)
        b.Left = innerRight - EffWidth(b) - b.Margin.Right
        b.Top = CenterTop(b, rowTop, rowH)
        b.BringToFront()
    End Sub

    ''' <summary>Rebuilds an overflow dropdown to mirror the hidden buttons
    ''' (clicking a menu item just clicks the underlying button). Non-button
    ''' controls that overflow - the info labels - are simply hidden.</summary>
    Private Sub RebuildOverflowMenu(menu As ContextMenuStrip, items As List(Of Control))
        If menu Is Nothing Then Return
        menu.Items.Clear()

        For Each c As Control In items
            Dim b As Button = TryCast(c, Button)
            If b Is Nothing Then Continue For   ' labels: no menu entry

            Dim item As New ToolStripMenuItem(OverflowText(b)) With {.Enabled = b.Enabled}
            Dim captured As Button = b
            AddHandler item.Click, Sub()
                                       If captured.Enabled Then captured.PerformClick()
                                   End Sub
            menu.Items.Add(item)
        Next
    End Sub

    ''' <summary>Readable, localized label for a button in the overflow menu (the
    ''' toolbar glyphs like ▦/⛶ would be meaningless there).</summary>
    Private Function OverflowText(b As Button) As String
        Dim rus As Boolean = Is_Russian_Language
        Select Case b.Name
            Case "btn_choose_file" : Return Localization.T("Выбрать файл..")
            Case "btn_Select_Folder" : Return Localization.T("Выбрать папку..")
            Case "btn_Review" : Return Localization.T("Обновить папку")
            Case "btn_Panel" : Return Localization.T("Панель изображений")
            Case "btn_Full_Screen" : Return Localization.T("Полный экран")
            Case "btn_Language" : Return Localization.T("Язык интерфейса")
            Case "btn_RecentFiles" : Return Localization.T("Недавние файлы")
            Case "btn_Prev_File" : Return Localization.T("Предыдущий файл")
            Case "btn_Next_File" : Return Localization.T("Следующий файл")
            Case "btn_Next_Random" : Return Localization.T("Случайный файл")
            Case "btn_Random_Slideshow" : Return Localization.T("Случайное слайд-шоу")
            Case "btn_Slideshow" : Return Localization.T("Слайд-шоу")
            Case "btn_Move_Table" : Return Localization.T("Настройки..")
            Case "btn_Rename" : Return Localization.T("Переименовать")
            Case "bt_Delete" : Return Localization.T("Удалить")
            Case "btn_Translate" : Return Localization.T("Перевод")
            Case "btn_TranslateBrowser" : Return Localization.T("Перевод в браузере")
            Case Else : Return b.Text
        End Select
    End Function

    Private Sub btn_Overflow_Click(sender As Object, e As EventArgs) Handles btn_Overflow.Click
        If overflow_Menu IsNot Nothing AndAlso overflow_Menu.Items.Count > 0 Then
            overflow_Menu.Show(btn_Overflow, New Point(0, btn_Overflow.Height))
        End If
    End Sub

    Private Sub btn_Overflow_2_Click(sender As Object, e As EventArgs) Handles btn_Overflow_2.Click
        If overflow_Menu_2 IsNot Nothing AndAlso overflow_Menu_2.Items.Count > 0 Then
            overflow_Menu_2.Show(btn_Overflow_2, New Point(0, btn_Overflow_2.Height))
        End If
    End Sub

    ' Relayout on width changes (docked toolbar follows the form width) and when
    ' the content that drives shrink/overflow changes.
    Private Sub flow_Toolbar_SizeChanged(sender As Object, e As EventArgs) Handles flow_Toolbar.SizeChanged
        LayoutToolbar()
    End Sub

    Private Sub Toolbar_Content_Changed(sender As Object, e As EventArgs) _
        Handles cmbox_Media_Folder.TextChanged, lbl_Info.TextChanged,
                lbl_File_Number.TextChanged, lbl_Zoom.TextChanged,
                lbl_Slideshow_Time.TextChanged
        LayoutToolbar()
    End Sub

End Class
