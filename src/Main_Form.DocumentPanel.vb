#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

' The document readers' floating panel (specification 280): one more surface of the viewer's chrome, on the same pin,
' place, hot-zone, overflow and keyboard-ring algebra as the two rows, the status strip and the window commands. It exists
' once for the window and is built when the first document is shown. While no reader is on screen it is not in
' chrome_Panels at all - so it is neither in the F10 ring nor in any hot-zone calculation, as section 3.1 asks. The reader
' that is on screen hands its items to ShowDocumentPanel; the panel owns nothing of a reader.
Partial Public Class Main_Form

    ''' <summary>The panel's window name: an explicit case in the pin algebra, never the status fallback.</summary>
    Private Const Document_Panel_Name As String = "chrome_Document"

    Private chrome_Document_Panel As ChromePanel
    Private chrome_Document_Edit As ChromeEditBox
    Private chrome_Document_Edit_Item As ChromeItem
    Private chrome_Document_Edit_Accept As Func(Of String, Boolean, Boolean)
    Private document_Panel_Kind As DocumentReaderKind

    ''' <summary>One command of a reader on the panel (280): what it does and how its state is read. The glue makes a
    ''' <see cref="ChromeItem"/> of it and re-reads the state in <see cref="SyncDocumentPanel"/>; a reader owns nothing else.</summary>
    Private NotInheritable Class DocumentCommand
        Public Action As Action
        Public IsChecked As Func(Of Boolean)
        Public IsEnabled As Func(Of Boolean)
        Public IsVisible As Func(Of Boolean)
        ''' <summary>The text the item shows now (a position, a choice, a readout); Nothing = the item's own words.</summary>
        Public Text As Func(Of String)
        ''' <summary>Every text the item can show, so its width is the widest and no neighbour moves while it changes.</summary>
        Public Reserved As Func(Of String())
        ''' <summary>A menu button's rows, built when it opens; the current choice is checked.</summary>
        Public Menu As Func(Of IEnumerable(Of ChromeMenuItem))
    End Class

    Private document_Commands As Dictionary(Of String, DocumentCommand)
    Private document_Items As Dictionary(Of String, ChromeItem)
    ''' <summary>The reader that asked for the panel; kept while the chrome is not built yet, so the panel appears with it.</summary>
    Private document_Wanted_Kind As DocumentReaderKind
    ''' <summary>The top of the side pane the last time a reader laid it out, in the media area (-1 = not laid out).</summary>
    Private document_Pane_Top As Integer = -1

    ''' <summary>A document reader owns the panel right now (it is in the chrome's list of panels).</summary>
    Friend Function DocumentPanelPresent() As Boolean
        Return chrome_Document_Panel IsNot Nothing AndAlso chrome_Panels.Contains(chrome_Document_Panel)
    End Function

    ''' <summary>The panel's embedded editor is open, so the keyboard is the editor's.</summary>
    Private Function DocumentEditHasFocus() As Boolean
        Return chrome_Document_Edit IsNot Nothing AndAlso chrome_Document_Edit.IsEditing
    End Function

    ''' <summary>The panel's top, directly under the lowest ANCHORED top row (a row the user has moved is not in the band and
    ''' leaves its place to the next one); with neither row anchored it takes the top slot (280 section 3.4).</summary>
    Private Function DocumentPanelTop(media As Rectangle, inset As Integer, factor As Single) As Integer
        Dim bottom As Integer = Integer.MinValue
        For Each other As ChromePanel In chrome_Panels
            If other.Name <> "chrome_Row1" AndAlso other.Name <> "chrome_Row2" Then Continue For
            If ChromePanelIsFree(other) Then Continue For
            bottom = Math.Max(bottom, ChromeAnchoredRect(other).Bottom)
        Next
        Return If(bottom = Integer.MinValue, media.Top + inset, bottom + UiScale.Px(ChromeMetrics.Gap, factor))
    End Function

    ''' <summary>The window's name for a screen reader (the strings the old reader strips carried), by reader.</summary>
    Private Function DocumentPanelTitle() As String
        Dim key As String = DocumentPanelRules.PanelNameKey(document_Panel_Kind)
        Return If(key.Length = 0, Localization.T("Документ"), Localization.T(key))
    End Function

    Private Sub BuildDocumentPanel()
        Dim panel As New ChromePanel(Document_Panel_Name)
        chrome_Document_Panel = panel
        panel.Host.Owner = Me
        AdmitLowerIntegrityMessages(panel.Host)
        ' A file dropped on the panel opens like a file dropped on the picture (the same wiring as the rows, 124 UX-212).
        For Each target As Control In New Control() {panel.Host, panel.Host.Surface}
            target.AllowDrop = True
            AddHandler target.DragEnter, AddressOf Form1_DragEnter
            AddHandler target.DragDrop, AddressOf Form1_DragDrop
        Next
        panel.Host.Surface.OverflowId = "btn_Overflow_3"
        panel.Pin.Action = Sub() ChromeTogglePin(panel)
        SyncChromePin(panel)
        panel.Host.Surface.PinItem = panel.Pin
        panel.Host.Surface.SetItems(Nothing)
        AddHandler panel.Host.Surface.ItemInvoked, AddressOf Chrome_ItemInvoked
        ' The wheel over the panel goes the same way as over the picture beside it (the document branches of MouseUse).
        AddHandler panel.Host.Surface.MouseWheel, Sub(sender, args) MouseUse(args)
        AddHandler panel.Host.Surface.OverflowRequested, Sub(sender, args) ChromeShowOverflow(panel)
        AddHandler panel.Host.Surface.MoveGripPress, Sub(sender, args) ChromeTakeKeyboardForViewer()
        AddHandler panel.Host.Surface.LayoutChanged, Sub() DocumentEditFollow()

        ' The embedded editor (the position box, the page find). A click never activates the host except for this
        ' deliberate edit, and the keyboard goes back to the viewer when it ends - the path box's route.
        chrome_Document_Edit = New ChromeEditBox() With {.Name = "doc_Editor"}
        panel.Host.Surface.Controls.Add(chrome_Document_Edit)
        AddHandler chrome_Document_Edit.EditingStarted,
            Sub()
                ChromeLeaveKeyboardMode()
                panel.Host.AllowActivation = True
                panel.Host.Activate()
            End Sub
        AddHandler chrome_Document_Edit.EditingEnded,
            Sub()
                chrome_Document_Edit_Item = Nothing
                chrome_Document_Edit_Accept = Nothing
                panel.Host.AllowActivation = False
                ChromeReturnKeyboardToViewer(panel)
                panel.Host.Surface.Invalidate()
            End Sub
        AddHandler chrome_Document_Edit.Accepted, AddressOf DocumentEditAccepted
    End Sub

    ''' <summary>Where the panel stands in the list: directly after row 2, so the ring reads row 1, row 2, the document
    ''' panel, the status strip, the window commands, and none of the code that indexes the first two rows moves.</summary>
    Private Function DocumentPanelInsertIndex() As Integer
        For index As Integer = 0 To chrome_Panels.Count - 1
            If chrome_Panels(index).Name = "chrome_Row2" Then Return index + 1
        Next
        Return Math.Min(chrome_Panels.Count, 2)
    End Function

    ''' <summary>A document reader is on screen: the panel carries its items. Called again by the next reader (or by the
    ''' same one when its items change) with the new list. A panel that was not shown is put in the ring's list, configured
    ''' like its neighbours and revealed once, so the commands that stood on the old strip are found.</summary>
    Friend Sub ShowDocumentPanel(kind As DocumentReaderKind, items As IEnumerable(Of ChromeItem))
        If kind = DocumentReaderKind.None Then
            HideDocumentPanel()
            Return
        End If
        If chrome_Panels.Count = 0 Then Return
        If chrome_Document_Panel Is Nothing Then BuildDocumentPanel()
        Dim panel As ChromePanel = chrome_Document_Panel
        If chrome_Document_Edit.IsEditing Then chrome_Document_Edit.EndEdit(True)
        document_Panel_Kind = kind
        panel.Host.Surface.SetItems(items)
        Dim added As Boolean = Not chrome_Panels.Contains(panel)
        If added Then
            ' The ring's index would point at another panel once the list changes under it.
            ChromeLeaveKeyboardMode()
            chrome_Panels.Insert(DocumentPanelInsertIndex(), panel)
            ChromeConfigure()
            panel.Wanted = True
            panel.LastActivity = chrome_Clock.ElapsedMilliseconds
        Else
            panel.Host.Text = DocumentPanelTitle()
            panel.Host.AccessibleName = panel.Host.Text
            panel.Host.Surface.LayoutItems()
            PlaceChromePanels()
        End If
    End Sub

    ''' <summary>No document reader is on screen any more: the panel leaves the ring and the hot zone and its window goes at once
    ''' (no fade for a panel that has nothing to cover). It is kept for the next document and keeps its pin.</summary>
    Friend Sub HideDocumentPanel()
        document_Commands = Nothing
        document_Items = Nothing
        document_Wanted_Kind = DocumentReaderKind.None
        document_Pane_Top = -1
        If Not DocumentPanelPresent() Then Return
        Dim panel As ChromePanel = chrome_Document_Panel
        If chrome_Document_Edit.IsEditing Then chrome_Document_Edit.EndEdit(True)
        ChromeLeaveKeyboardMode()
        panel.Wanted = False
        panel.Alpha = 0.0R
        panel.TargetAlpha = 0.0R
        If panel.Host.Visible Then panel.Host.Visible = False
        panel.AppliedOpacity = -1.0R
        chrome_Panels.Remove(panel)
        document_Panel_Kind = DocumentReaderKind.None
    End Sub

    ''' <summary>Lays the embedded editor over <paramref name="item"/> and gives it the keyboard. <paramref name="accept"/> gets the
    ''' typed text and whether Shift was held, and answers True when the edit is over (a position that parsed) or False to keep
    ''' it open (the find: Enter goes to the next match and the editor stays).</summary>
    Friend Sub BeginDocumentEdit(item As ChromeItem, text As String, mode As ChromeEditMode,
                                 accept As Func(Of String, Boolean, Boolean))
        If chrome_Document_Edit Is Nothing OrElse Not DocumentPanelPresent() OrElse item Is Nothing Then Return
        If item.Bounds.IsEmpty OrElse chrome_Document_Edit.IsEditing Then Return
        chrome_Document_Edit_Item = item
        chrome_Document_Edit_Accept = accept
        chrome_Document_Edit.Configure(chrome_Theme, chrome_Document_Panel.Host.Surface.ScaleFactor,
                                       Localization.IsRightToLeft(), chrome_Text_Font, If(item.AccessibleName.Length > 0, item.AccessibleName, item.Caption))
        chrome_Document_Edit.BeginEdit(item.Bounds, text, mode)
    End Sub

    Private Sub DocumentEditAccepted(sender As Object, e As EventArgs)
        Dim accept As Func(Of String, Boolean, Boolean) = chrome_Document_Edit_Accept
        If accept Is Nothing Then
            chrome_Document_Edit.EndEdit(True)
            Return
        End If
        Dim finished As Boolean
        Try
            finished = accept(chrome_Document_Edit.Value, chrome_Document_Edit.AcceptedWithShift)
        Catch ex As Exception
            AppFileLogger.LogException("Chrome: accepting a document panel edit", ex)
            finished = True
        End Try
        If finished Then chrome_Document_Edit.EndEdit(False)
    End Sub

    ''' <summary>The panel was laid out again while the editor is open (a language, a scale, a width): the editor follows its
    ''' item, or ends when the item has gone into the "more" menu.</summary>
    Private Sub DocumentEditFollow()
        If chrome_Document_Edit Is Nothing OrElse Not chrome_Document_Edit.IsEditing OrElse chrome_Document_Edit_Item Is Nothing Then Return
        If chrome_Document_Edit_Item.Bounds.IsEmpty Then
            chrome_Document_Edit.EndEdit(True)
        Else
            chrome_Document_Edit.Follow(chrome_Document_Edit_Item.Bounds)
        End If
    End Sub

    ' --- the reader's side: commands -> items -----------------------------------------------------------------------

    Private Shared Function DocumentChromeKind(id As String) As ChromeItemKind
        Select Case DocumentPanelRules.KindOf(id)
            Case DocumentItemKind.Toggle : Return ChromeItemKind.Toggle
            Case DocumentItemKind.MenuButton : Return ChromeItemKind.MenuButton
            Case DocumentItemKind.Readout : Return ChromeItemKind.Text
            Case Else : Return ChromeItemKind.Button ' a position box and a find box are pressed to begin editing in place
        End Select
    End Function

    ''' <summary>A reader is on screen: its commands become the panel's items, in the reader's one fixed reading order. A command
    ''' the reader does not supply is not on the panel (the rows carry previous / next file and the settings button). Called
    ''' once when the reader opens; the state is re-read by <see cref="SyncDocumentPanel"/>.</summary>
    Private Sub ShowDocumentCommands(kind As DocumentReaderKind, commands As Dictionary(Of String, DocumentCommand))
        document_Commands = commands
        document_Wanted_Kind = kind
        document_Items = New Dictionary(Of String, ChromeItem)(StringComparer.Ordinal)
        Dim items As New List(Of ChromeItem)()
        For Each id As String In DocumentPanelRules.ItemIds(kind)
            Dim command As DocumentCommand = Nothing
            If Not commands.TryGetValue(id, command) Then Continue For
            Dim item As New ChromeItem(id, DocumentChromeKind(id)) With {
                .GlyphId = DocumentPanelRules.GlyphId(id),
                .MirrorInRtl = DocumentPanelRules.MirrorsInRtl(id),
                .NeverOverflows = DocumentPanelRules.IsPinnedCore(kind, id),
                .KeepsSlotWhenHidden = DocumentPanelRules.IsConditional(kind, id),
                .Action = command.Action}
            If command.Menu IsNot Nothing Then
                Dim menu As Func(Of IEnumerable(Of ChromeMenuItem)) = command.Menu
                item.Action = Sub() ChromeShowMenu(item, menu())
            End If
            ApplyDocumentItem(item, command)
            document_Items(id) = item
            items.Add(item)
        Next
        ShowDocumentPanel(kind, items)
        DocumentPaneFollowsChrome()
    End Sub

    ''' <summary>The chrome was built after the reader opened (a document given on the command line): the panel is shown now.</summary>
    Private Sub ReshowDocumentPanel()
        If document_Wanted_Kind = DocumentReaderKind.None OrElse document_Commands Is Nothing OrElse DocumentPanelPresent() Then Return
        ShowDocumentCommands(document_Wanted_Kind, document_Commands)
    End Sub

    Private Sub ApplyDocumentItem(item As ChromeItem, command As DocumentCommand)
        Dim key As String = DocumentPanelRules.CaptionKey(item.Id)
        Dim words As String = If(key.Length > 0, Localization.T(key), "")
        Dim shown As String = If(command.Text IsNot Nothing, command.Text(), words)
        item.Caption = shown
        item.Tooltip = words
        ' A value the item shows (a position, a choice) is part of what a screen reader says.
        item.AccessibleName = If(command.Text IsNot Nothing AndAlso words.Length > 0 AndAlso shown.Length > 0 AndAlso shown <> words,
                                 Localization.TF("{0}: {1}", words, shown), If(words.Length > 0, words, shown))
        If command.Reserved IsNot Nothing Then item.ReservedTexts = command.Reserved()
        If command.IsChecked IsNot Nothing Then item.Checked = command.IsChecked()
        If command.IsEnabled IsNot Nothing Then item.Enabled = command.IsEnabled()
        If command.IsVisible IsNot Nothing Then item.Visible = command.IsVisible()
    End Sub

    ''' <summary>The reader's state changed (the page, a pane, a choice, the context line) or the language did: the items read it
    ''' again and the panel is laid out and placed once.</summary>
    Private Sub SyncDocumentPanel()
        If Not DocumentPanelPresent() OrElse document_Commands Is Nothing OrElse document_Items Is Nothing Then Return
        For Each pair As KeyValuePair(Of String, ChromeItem) In document_Items
            Dim command As DocumentCommand = Nothing
            If document_Commands.TryGetValue(pair.Key, command) Then ApplyDocumentItem(pair.Value, command)
        Next
        Dim panel As ChromePanel = chrome_Document_Panel
        panel.Host.Text = DocumentPanelTitle()
        panel.Host.AccessibleName = panel.Host.Text
        panel.Host.Surface.LayoutItems()
        panel.Host.Surface.Invalidate()
        PlaceChromePanels()
    End Sub

    ''' <summary>The F10 ring is on the document panel: the keys are the chrome's, not the reader's and not the sort keys.</summary>
    Private Function ChromeKeyboardOnDocumentPanel() As Boolean
        If Not chrome_Keyboard_Mode OrElse Not DocumentPanelPresent() Then Return False
        Return chrome_Focus_Panel >= 0 AndAlso chrome_Focus_Panel < chrome_Panels.Count AndAlso chrome_Panels(chrome_Focus_Panel) Is chrome_Document_Panel
    End Function

    ''' <summary>Begins editing a page or chapter number in place over its item. A number outside 1..total is pulled in; a
    ''' non-number leaves the old value and says why in the status strip; nothing typed changes nothing (280 section 4.2).</summary>
    Private Sub BeginDocumentPositionEdit(id As String, current As Integer, total As Integer, moveTo As Action(Of Integer))
        Dim item As ChromeItem = Nothing
        If total < 1 OrElse document_Items Is Nothing OrElse Not document_Items.TryGetValue(id, item) Then Return
        BeginDocumentEdit(item, Math.Max(1, Math.Min(total, current)).ToString(System.Globalization.CultureInfo.InvariantCulture), ChromeEditMode.Digits,
            Function(text As String, shift As Boolean) As Boolean
                Dim parse As PositionParse = DocumentPanelRules.ParsePosition(text, total)
                If parse.Status = PositionParseStatus.Accepted Then
                    moveTo(parse.Value)
                ElseIf parse.Status = PositionParseStatus.NotANumber Then
                    Notify(Localization.TF("Введите число от 1 до {0}", total), NotifyLevel.Warning)
                End If
                Return True
            End Function)
    End Sub

    ' --- the side pane under a pinned top panel (Q280-1 default) --------------------------------------------------------

    ''' <summary>The top of a reader's side pane in the media area: below the lowest PINNED top panel, so the pane's own tab
    ''' strip is never hidden by it; 0 when nothing is pinned or the chrome is not on screen. The page itself fills the whole
    ''' area and a pinned panel may cover its top lines, as it does for a picture (280 section 3 rule 5).</summary>
    Private Function DocumentPaneTop() As Integer
        If panel_Media Is Nothing OrElse Not ChromeUsable() Then Return 0
        Dim bottom As Integer
        For Each other As ChromePanel In chrome_Panels
            If other.Name <> "chrome_Row1" AndAlso other.Name <> "chrome_Row2" AndAlso other.Name <> Document_Panel_Name Then Continue For
            If Not ChromePanelPinned(other) OrElse ChromePanelIsFree(other) Then Continue For
            bottom = Math.Max(bottom, panel_Media.RectangleToClient(ChromeAnchoredRect(other)).Bottom)
        Next
        Return Math.Max(0, bottom)
    End Function

    ''' <summary>A pin or a place of the top panels changed: the reader that has a side pane lays it out again when its top moved.</summary>
    Private Sub DocumentPaneFollowsChrome()
        If Not DocumentPanelPresent() Then Return
        ' The reader's layout records the top it used in document_Pane_Top, so a layout from a resize counts too.
        If DocumentPaneTop() = document_Pane_Top Then Return
        Select Case document_Panel_Kind
            Case DocumentReaderKind.Pdf : LayoutPdfReader(Me, EventArgs.Empty)
        End Select
    End Sub

    ''' <summary>The window closes: the panel's window is released even when no document is on screen (it is then not in
    ''' <c>chrome_Panels</c>, so the chrome's own teardown does not reach it).</summary>
    Private Sub TearDownDocumentPanel()
        If chrome_Document_Panel Is Nothing Then Return
        Try
            chrome_Document_Panel.Host.Close()
            chrome_Document_Panel.Host.Dispose()
        Catch
        End Try
        chrome_Document_Panel = Nothing
        chrome_Document_Edit = Nothing
        chrome_Document_Edit_Item = Nothing
        chrome_Document_Edit_Accept = Nothing
        document_Panel_Kind = DocumentReaderKind.None
    End Sub
End Class
#End If
