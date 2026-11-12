#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Globalization

''' <summary>Which document reader is on screen, i.e. which item list the document panel carries (specification 280).</summary>
Friend Enum DocumentReaderKind
    None = 0
    Pdf
    Epub
    Html
    Text
End Enum

''' <summary>How an item of the document panel behaves. The glue maps these to the chrome's own item kinds.</summary>
Friend Enum DocumentItemKind
    ''' <summary>One command.</summary>
    Button
    ''' <summary>A pane or a mode; the item is checked while it is open.</summary>
    Toggle
    ''' <summary>Opens a menu of choices, the current one checked.</summary>
    MenuButton
    ''' <summary>The page or chapter number: a readout "3 / 120" that is also a command - a click begins editing in place.</summary>
    Position
    ''' <summary>The in-page find: the same embedded editor in text mode.</summary>
    FindBox
    ''' <summary>Text only.</summary>
    Readout
End Enum

Friend Enum PositionParseStatus
    ''' <summary>A number inside 1..total (maybe clamped, see <see cref="PositionParse.WasClamped"/>).</summary>
    Accepted
    ''' <summary>Nothing typed: the old value stays, no message.</summary>
    Empty
    ''' <summary>Something that is not a run of digits: the old value stays and the status strip says why.</summary>
    NotANumber
    ''' <summary>The document has no pages or chapters to go to.</summary>
    NoRange
End Enum

Friend Structure PositionParse
    Public Status As PositionParseStatus
    ''' <summary>1..total when <see cref="Status"/> is Accepted, otherwise 0.</summary>
    Public Value As Integer
    ''' <summary>The typed number was outside 1..total and was pulled in, as the numeric box it replaces did.</summary>
    Public WasClamped As Boolean
End Structure

''' <summary>
''' The pure half of the document panel (specification 280): what each reader puts on it, in which fixed reading
''' order, which items never go into the "»" menu, which are conditional, and what the page / chapter box accepts.
''' No form, no timer, no localization lookup - the captions are the KEYS of the localization table (the Russian
''' source strings) and the glue passes them through <c>Localization.T</c>.
''' </summary>
Friend NotInheritable Class DocumentPanelRules

    Private Sub New()
    End Sub

    ' --- item ids -------------------------------------------------------------------------------------------------

    Friend Const IdBack As String = "doc_Back"
    Friend Const IdForward As String = "doc_Forward"
    Friend Const IdReload As String = "doc_Reload"
    Friend Const IdPrevPage As String = "doc_PrevPage"
    Friend Const IdNextPage As String = "doc_NextPage"
    Friend Const IdPagePosition As String = "doc_PagePosition"
    Friend Const IdPrevChapter As String = "doc_PrevChapter"
    Friend Const IdNextChapter As String = "doc_NextChapter"
    Friend Const IdChapterPosition As String = "doc_ChapterPosition"
    Friend Const IdPaneOutline As String = "doc_PaneOutline"
    Friend Const IdPaneSearch As String = "doc_PaneSearch"
    Friend Const IdPaneText As String = "doc_PaneText"
    Friend Const IdPaneBookmarks As String = "doc_PaneBookmarks"
    Friend Const IdPaneThumbnails As String = "doc_PaneThumbnails"
    Friend Const IdAddBookmark As String = "doc_AddBookmark"
    Friend Const IdCopy As String = "doc_Copy"
    Friend Const IdSpeak As String = "doc_Speak"
    Friend Const IdStopSpeech As String = "doc_StopSpeech"
    Friend Const IdRotate As String = "doc_Rotate"
    Friend Const IdLayout As String = "doc_Layout"
    Friend Const IdColorMode As String = "doc_ColorMode"
    Friend Const IdResetAppearance As String = "doc_ResetAppearance"
    Friend Const IdOpenWith As String = "doc_OpenWith"
    Friend Const IdTranslate As String = "doc_Translate"
    Friend Const IdFind As String = "doc_Find"
    Friend Const IdFindPrev As String = "doc_FindPrev"
    Friend Const IdFindNext As String = "doc_FindNext"
    Friend Const IdFindCount As String = "doc_FindCount"
    Friend Const IdZoomOut As String = "doc_ZoomOut"
    Friend Const IdZoomValue As String = "doc_ZoomValue"
    Friend Const IdZoomIn As String = "doc_ZoomIn"
    Friend Const IdZoomReset As String = "doc_ZoomReset"
    Friend Const IdFitWidth As String = "doc_FitWidth"
    Friend Const IdReaderMode As String = "doc_ReaderMode"
    Friend Const IdViewSource As String = "doc_ViewSource"
    Friend Const IdOpenInBrowser As String = "doc_OpenInBrowser"
    Friend Const IdPrint As String = "doc_Print"
    Friend Const IdBlocked As String = "doc_Blocked"
    ''' <summary>276 Step 4.1: the actions of a doc-html-translate result shown in the HTML view (original, browser, folder, save,
    ''' process again). Present only while such a result is on screen.</summary>
    Friend Const IdConvertResult As String = "doc_ConvertResult"
    Friend Const IdReturnToFiles As String = "doc_ReturnToFiles"
    Friend Const IdContext As String = "doc_Context"
    Friend Const IdTextEdit As String = "doc_TextEdit"
    Friend Const IdTextSave As String = "doc_TextSave"
    Friend Const IdTextSaveAs As String = "doc_TextSaveAs"
    Friend Const IdTextExcerpt As String = "doc_TextExcerpt"
    Friend Const IdTextExternal As String = "doc_TextExternal"
    Friend Const IdTextFind As String = "doc_TextFind"
    Friend Const IdTextReplace As String = "doc_TextReplace"
    Friend Const IdTextEncoding As String = "doc_TextEncoding"
    Friend Const IdTextLineEnding As String = "doc_TextLineEnding"
    Friend Const IdTextMarkdown As String = "doc_TextMarkdown"

    ''' <summary>The commands that rows 1 and 2 already carry while a document is on screen: they are never repeated on the
    ''' panel (280 section 4, T-0 record section 3).</summary>
    Private Shared ReadOnly RowCommandIds As String() = {"doc_PrevFile", "doc_NextFile", "doc_Settings"}

    ' --- reading order --------------------------------------------------------------------------------------------

    Private Shared ReadOnly PdfGroups As String()() = {
        New String() {IdBack, IdPrevPage, IdPagePosition, IdNextPage},
        New String() {IdPaneOutline, IdPaneSearch, IdPaneText, IdPaneBookmarks, IdPaneThumbnails, IdAddBookmark, IdSpeak, IdStopSpeech},
        New String() {IdRotate, IdLayout, IdColorMode, IdResetAppearance},
        New String() {IdOpenWith, IdTranslate},
        New String() {IdContext}}

    Private Shared ReadOnly EpubGroups As String()() = {
        New String() {IdBack, IdPrevChapter, IdChapterPosition, IdNextChapter},
        New String() {IdPaneOutline, IdPaneSearch, IdPaneBookmarks, IdAddBookmark, IdCopy},
        New String() {IdSpeak, IdStopSpeech},
        New String() {IdResetAppearance},
        New String() {IdOpenWith, IdTranslate},
        New String() {IdContext}}

    Private Shared ReadOnly HtmlGroups As String()() = {
        New String() {IdBack, IdForward, IdReload},
        New String() {IdFind, IdFindPrev, IdFindNext, IdFindCount},
        New String() {IdZoomOut, IdZoomValue, IdZoomIn, IdFitWidth},
        New String() {IdReaderMode, IdResetAppearance},
        New String() {IdViewSource, IdOpenInBrowser, IdPrint},
        New String() {IdBlocked},
        New String() {IdConvertResult},
        New String() {IdReturnToFiles},
        New String() {IdContext}}

    Private Shared ReadOnly TextGroups As String()() = {
        New String() {IdTextEdit, IdTextSave, IdTextSaveAs, IdTextExcerpt, IdTextExternal},
        New String() {IdTextFind, IdTextReplace},
        New String() {IdTextEncoding, IdTextLineEnding, IdTextMarkdown},
        New String() {IdZoomOut, IdZoomIn, IdZoomReset},
        New String() {IdContext}}

    ''' <summary>The items of a reader as groups, each group in reading order. A group is a run that belongs together
    ''' (the glue may give it a little air); the overflow treats the groups as one flat reading order.</summary>
    Friend Shared Function Groups(kind As DocumentReaderKind) As IReadOnlyList(Of String())
        Select Case kind
            Case DocumentReaderKind.Pdf : Return PdfGroups
            Case DocumentReaderKind.Epub : Return EpubGroups
            Case DocumentReaderKind.Html : Return HtmlGroups
            Case DocumentReaderKind.Text : Return TextGroups
            Case Else : Return New String()() {}
        End Select
    End Function

    ''' <summary>Every item id of a reader in the one fixed reading order - the same set and order at every width.</summary>
    Friend Shared Function ItemIds(kind As DocumentReaderKind) As IReadOnlyList(Of String)
        Dim ids As New List(Of String)()
        For Each group As String() In Groups(kind)
            ids.AddRange(group)
        Next
        Return ids
    End Function

    ''' <summary>True for an id some floating row already carries for a document.</summary>
    Friend Shared Function IsRowCommand(id As String) As Boolean
        Return Array.IndexOf(RowCommandIds, id) >= 0
    End Function

    ' --- the pinned core and the conditional items ----------------------------------------------------------------

    ''' <summary>The items that never collapse into the "»" menu: the position box of a paged reader and the page find.
    ''' If only the core is left and still does not fit, it stays and the host clips it.</summary>
    Friend Shared Function IsPinnedCore(kind As DocumentReaderKind, id As String) As Boolean
        Select Case kind
            Case DocumentReaderKind.Pdf : Return id = IdPagePosition
            Case DocumentReaderKind.Epub : Return id = IdChapterPosition
            Case DocumentReaderKind.Html : Return id = IdFind
            Case Else : Return False
        End Select
    End Function

    ''' <summary>An item that is present only sometimes keeps its place as soft air while it is hidden, so no neighbour moves:
    ''' the blocked-content menu appears when something was blocked, "view source" only for a file (not an online page);
    ''' in a text the saving half belongs to the editor, the zoom to the reader and the excerpt and the external program
    ''' to a file over the editing limit.</summary>
    Friend Shared Function IsConditional(kind As DocumentReaderKind, id As String) As Boolean
        If kind = DocumentReaderKind.Html Then Return id = IdBlocked OrElse id = IdViewSource OrElse id = IdConvertResult
        If kind <> DocumentReaderKind.Text Then Return False
        Select Case id
            Case IdTextSave, IdTextSaveAs, IdTextExcerpt, IdTextExternal, IdTextReplace, IdTextLineEnding, IdTextMarkdown, IdZoomOut, IdZoomIn, IdZoomReset
                Return True
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>The readout that gives its room up first when the row is short.</summary>
    Friend Shared Function ReleasedFirst(id As String) As Boolean
        Return id = IdContext
    End Function

    ' --- what an item is -------------------------------------------------------------------------------------------

    Friend Shared Function KindOf(id As String) As DocumentItemKind
        Select Case id
            Case IdPaneOutline, IdPaneSearch, IdPaneText, IdPaneBookmarks, IdPaneThumbnails, IdReaderMode, IdTextEdit, IdTextMarkdown
                Return DocumentItemKind.Toggle
            Case IdLayout, IdColorMode, IdBlocked, IdConvertResult, IdTextEncoding, IdTextLineEnding
                Return DocumentItemKind.MenuButton
            Case IdPagePosition, IdChapterPosition
                Return DocumentItemKind.Position
            Case IdFind
                Return DocumentItemKind.FindBox
            Case IdContext, IdFindCount
                Return DocumentItemKind.Readout
            Case Else
                Return DocumentItemKind.Button
        End Select
    End Function

    ''' <summary>The localization KEY (the Russian source string) of an item's caption, tooltip and accessible name; every
    ''' one is already in the 13-language table, so the panel adds no word. "" for an item that has no fixed words (the
    ''' context readout and the blocked-content count are formatted by the glue). The translate command is a product
    ''' name and stays as it is.</summary>
    Friend Shared Function CaptionKey(id As String) As String
        Select Case id
            Case IdBack : Return "Назад"
            Case IdForward : Return "Вперёд"
            Case IdReload : Return "Обновить"
            Case IdPrevPage : Return "Предыдущая страница"
            Case IdNextPage : Return "Следующая страница"
            Case IdPagePosition : Return "Номер страницы"
            Case IdPrevChapter : Return "Предыдущая глава"
            Case IdNextChapter : Return "Следующая глава"
            Case IdChapterPosition : Return "Номер главы"
            Case IdPaneOutline : Return "Оглавление"
            Case IdPaneSearch : Return "Поиск"
            Case IdPaneText : Return "Текст"
            Case IdPaneBookmarks : Return "Закладки"
            Case IdPaneThumbnails : Return "Миниатюры страниц"
            Case IdAddBookmark : Return "Добавить закладку"
            Case IdCopy : Return "Копировать"
            Case IdSpeak : Return "Чтение вслух"
            Case IdStopSpeech : Return "Остановить"
            Case IdRotate : Return "Повернуть вправо"
            Case IdLayout : Return "Режим просмотра"
            Case IdColorMode : Return "Цвет страницы"
            Case IdResetAppearance : Return "Сбросить оформление"
            Case IdOpenWith : Return "Открыть с помощью"
            Case IdTranslate, IdConvertResult : Return "doc-html-translate"
            Case IdFind : Return "Поиск на странице"
            Case IdFindPrev : Return "Предыдущее совпадение"
            Case IdFindNext : Return "Следующее совпадение"
            Case IdFindCount : Return "Совпадения"
            Case IdZoomOut : Return "Уменьшить"
            Case IdZoomValue, IdZoomReset : Return "Сбросить масштаб"
            Case IdZoomIn : Return "Увеличить"
            Case IdFitWidth : Return "По ширине"
            Case IdReaderMode : Return "Оформление для чтения"
            Case IdViewSource : Return "Исходный текст"
            Case IdOpenInBrowser : Return "Открыть в браузере"
            Case IdPrint : Return "Печать"
            Case IdReturnToFiles : Return "Вернуться к файлам"
            Case IdTextEdit : Return "Править"
            Case IdTextSave : Return "Сохранить"
            Case IdTextSaveAs : Return "Сохранить как.."
            Case IdTextExcerpt : Return "Сохранить выделенное как новый файл"
            Case IdTextExternal : Return "Открыть во внешней программе"
            Case IdTextFind : Return "Найти"
            Case IdTextReplace : Return "Заменить"
            Case IdTextEncoding : Return "Кодировка"
            Case IdTextLineEnding : Return "Концы строк"
            Case IdTextMarkdown : Return "Оформленный вид"
            Case Else : Return ""
        End Select
    End Function

    ''' <summary>The accessible name of the whole panel for a reader (the strings the old strips carried).</summary>
    Friend Shared Function PanelNameKey(kind As DocumentReaderKind) As String
        Select Case kind
            Case DocumentReaderKind.Pdf : Return "Чтение PDF"
            Case DocumentReaderKind.Epub : Return "Полное чтение EPUB"
            Case DocumentReaderKind.Html : Return "Просмотр HTML-страницы"
            Case DocumentReaderKind.Text : Return "Документ"
            Case Else : Return ""
        End Select
    End Function

    ''' <summary>The choices a menu item offers, as (stored value, caption key); the current one is checked by the glue.
    ''' Empty for an item that is not a fixed-choice menu.</summary>
    Friend Shared Function MenuChoices(id As String) As KeyValuePair(Of String, String)()
        Select Case id
            Case IdLayout
                Return New KeyValuePair(Of String, String)() {
                    New KeyValuePair(Of String, String)("single", "Одна страница"),
                    New KeyValuePair(Of String, String)("continuous", "Непрерывно"),
                    New KeyValuePair(Of String, String)("spread", "Разворот")}
            Case IdColorMode
                Return New KeyValuePair(Of String, String)() {
                    New KeyValuePair(Of String, String)("original", "Оригинал"),
                    New KeyValuePair(Of String, String)("night", "Ночь"),
                    New KeyValuePair(Of String, String)("sepia", "Сепия")}
            Case Else
                Return New KeyValuePair(Of String, String)() {}
        End Select
    End Function

    ''' <summary>The catalogue glyph of an item (<c>assets/glyphs</c>), or "" when the catalogue has no meaning for it: such an
    ''' item keeps its word as the caption (280 section 3 rule 7, T-0 record section 4). Nothing is drawn by hand.</summary>
    Friend Shared Function GlyphId(id As String) As String
        Select Case id
            Case IdBack : Return "nav.back"
            Case IdForward : Return "nav.forward"
            Case IdReload : Return "action.refresh"
            Case IdPaneSearch : Return "action.search"
            Case IdPaneThumbnails : Return "view.grid"
            Case IdResetAppearance : Return "action.reset"
            Case IdOpenWith, IdOpenInBrowser : Return "nav.open-external"
            Case IdTranslate, IdConvertResult : Return "action.translate"
            Case IdCopy : Return "action.copy"
            Case IdStopSpeech : Return "media.stop"
            Case IdBlocked : Return "status.warning"
            Case IdReturnToFiles : Return "content.folder"
            Case IdSpeak : Return "media.volume"
            Case IdTextEdit : Return "action.edit"
            Case IdTextSave : Return "action.save"
            Case IdTextExcerpt : Return "action.export"
            Case IdTextExternal : Return "nav.open-external"
            Case IdTextFind : Return "action.search"
            Case IdTextReplace : Return "action.replace"
            Case Else : Return ""
        End Select
    End Function

    ''' <summary>A history arrow points the other way under right-to-left reading.</summary>
    Friend Shared Function MirrorsInRtl(id As String) As Boolean
        Return id = IdBack OrElse id = IdForward
    End Function

    ' --- the position box -----------------------------------------------------------------------------------------

    ''' <summary>The format of the position readout: numbers only, no word, so it needs no translation of its own.</summary>
    Friend Const PositionReadoutFormat As String = "{0} / {1}"

    ''' <summary>"3 / 120"; "" when the document has nothing to number. The current number is pulled into 1..total.</summary>
    Friend Shared Function PositionReadout(current As Integer, total As Integer, format As String) As String
        If total < 1 Then Return ""
        Dim shown As Integer = Math.Min(total, Math.Max(1, current))
        Return String.Format(CultureInfo.InvariantCulture, format, shown, total)
    End Function

    ''' <summary>The widest text the readout can take for a document of this size, so the item keeps one width while the
    ''' number changes.</summary>
    Friend Shared Function PositionReadoutReserved(total As Integer, format As String) As String
        Return PositionReadout(total, total, format)
    End Function

    ''' <summary>What a typed position means. Digits only (any script's decimal digits - Arabic-Indic, Devanagari, Bengali
    ''' and the like are digits for the users of those languages), surrounding white space ignored (a pasted line brings
    ''' its line end). A number outside 1..total is pulled in like the numeric box did, however long the digit string.
    ''' Never throws.</summary>
    Friend Shared Function ParsePosition(text As String, total As Integer) As PositionParse
        Dim result As New PositionParse()
        If total < 1 Then
            result.Status = PositionParseStatus.NoRange
            Return result
        End If
        Dim trimmed As String = If(text, "").Trim()
        If trimmed.Length = 0 Then
            result.Status = PositionParseStatus.Empty
            Return result
        End If
        Dim value As Long = 0
        For Each ch As Char In trimmed
            If Char.GetUnicodeCategory(ch) <> UnicodeCategory.DecimalDigitNumber Then
                result.Status = PositionParseStatus.NotANumber
                Return result
            End If
            ' Once the number is past the last page more digits cannot bring it back; stop growing so a 40-digit
            ' string never overflows.
            If value <= total Then value = value * 10 + CharUnicodeInfo.GetDecimalDigitValue(ch)
        Next
        result.Status = PositionParseStatus.Accepted
        If value < 1 Then
            result.Value = 1
            result.WasClamped = True
        ElseIf value > total Then
            result.Value = total
            result.WasClamped = True
        Else
            result.Value = CInt(value)
        End If
        Return result
    End Function

End Class
#End If
