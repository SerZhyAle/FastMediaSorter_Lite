#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO

' Every question the viewer asks through the dialog layer, built in one place (ticket 124, UX-204/UX-205).
' Pure: it composes sentences and decides which answer is the default, nothing more - so a test can walk the
' whole list and prove that no question's default is the answer that overwrites something.
'
' The standing rule of the project stands: this file changes HOW a question looks, never WHETHER it is asked,
' and it removes no opt-out of a confirmation.

''' <summary>What one of the two files of a name collision looks like.</summary>
Friend Structure AskFileFacts
    Friend ReadOnly Size As Long
    Friend ReadOnly Modified As Date?

    Friend Sub New(size As Long, modified As Date?)
        Me.Size = size
        Me.Modified = modified
    End Sub

    ''' <summary>Reads size and write time; an unreadable file is simply left without them.</summary>
    Friend Shared Function Read(path As String) As AskFileFacts
        Try
            Dim info As New FileInfo(path)
            If info.Exists Then Return New AskFileFacts(info.Length, info.LastWriteTime)
        Catch
        End Try
        Return New AskFileFacts(-1, Nothing)
    End Function

    Friend Function Describe() As String
        Dim size_Text As String = If(Size >= 0, FileDetails.FormatSize(Size), "?")
        Dim date_Text As String = If(Modified.HasValue, FileDetails.FormatTimestamp(Modified.Value), "?")
        Return Localization.TF("{0}, изменён {1}", size_Text, date_Text)
    End Function
End Structure

Friend NotInheritable Class AskSpecs

    Private Sub New()
    End Sub

    ''' <summary>The index of "Replace" in <see cref="NameCollision"/>.</summary>
    Friend Const CollisionReplace As Integer = 0
    ''' <summary>The index of "Keep both" in <see cref="NameCollision"/>.</summary>
    Friend Const CollisionKeepBoth As Integer = 1
    ''' <summary>The index of "Skip" - also the default and the answer of Escape.</summary>
    Friend Const CollisionSkip As Integer = 2

    ''' <summary>The destination already holds a file of this name. The default - and Escape - is "Skip": the
    ''' Space or Enter typed for the next picture lands on this window, and replacing takes a click or the
    ''' accelerator (audit 046 C-01). Both files' size and date are on the window, so the question is not blind.</summary>
    Friend Shared Function NameCollision(fileName As String, existing As AskFileFacts, incoming As AskFileFacts) As AskSpec
        Dim detail As String =
            Localization.TF("Уже в папке: {0}", existing.Describe()) & vbLf &
            Localization.TF("Этот файл: {0}", incoming.Describe())
        Return New AskSpec(
            Localization.T("Совпадение имён"),
            Localization.TF("В папке назначения уже есть файл {0}.", fileName),
            AskIcon.Warning,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Заменить"), destructive:=True, role:=ButtonGlyphRole.Caution, glyphId:="action.replace"),
                New AskButton(Localization.T("Оставить оба"), glyphId:="action.copy"),
                New AskButton(Localization.T("Пропустить"), role:=ButtonGlyphRole.Dismissing, glyphId:="action.skip")},
            defaultIndex:=CollisionSkip, cancelIndex:=CollisionSkip, detail:=detail)
    End Function

    ''' <summary>The standing "replace" policy is about to overwrite something - asked once a session. The default
    ''' is the way out.</summary>
    Friend Shared Function ReplacePolicyWarning(fileName As String) As AskSpec
        Return New AskSpec(
            Localization.T("Совпадение имён"),
            Localization.TF("В настройках выбрано «заменять»: файл {0} в папке назначения будет перезаписан. Продолжить? Это предупреждение показывается один раз за сеанс.", fileName),
            AskIcon.Warning,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Заменить"), destructive:=True, role:=ButtonGlyphRole.Caution, glyphId:="action.replace"),
                New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=1, cancelIndex:=1)
    End Function

    Friend Const ImportReplace As Integer = 0
    Friend Const ImportMerge As Integer = 1
    Friend Const ImportCancel As Integer = 2

    ''' <summary>The settings import preview: replace everything the file describes, merge only what differs from
    ''' the defaults, or leave. Cancel is the default, as it was.</summary>
    Friend Shared Function SettingsImport(summary As String) As AskSpec
        Return New AskSpec(
            Localization.T("Настройки"),
            summary,
            AskIcon.Question,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Заменить"), destructive:=True, role:=ButtonGlyphRole.Irreversible, glyphId:="action.replace"),
                New AskButton(Localization.T("Объединить"), role:=ButtonGlyphRole.Affirmative, glyphId:="action.import"),
                New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=ImportCancel, cancelIndex:=ImportCancel,
            detail:=Localization.T("Заменить - взять все настройки из файла. Объединить - взять только то, что отличается от значений по умолчанию."))
    End Function

    Friend Const ConflictSwap As Integer = 0
    Friend Const ConflictClear As Integer = 1
    Friend Const ConflictCancel As Integer = 2

    ''' <summary>A shortcut that already belongs to another action: swap, take it away from that action, or leave
    ''' everything. Nothing here destroys a file; the first button stays the default, as before.</summary>
    Friend Shared Function HotkeyConflict(combo As String, ownerCaption As String) As AskSpec
        Return New AskSpec(
            Localization.T("Сочетания клавиш"),
            Localization.TF("Сочетание {0} уже назначено действию «{1}».", combo, ownerCaption),
            AskIcon.Question,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Обменять сочетания"), role:=ButtonGlyphRole.Affirmative, glyphId:="action.swap"),
                New AskButton(Localization.T("Снять у того действия"), glyphId:="action.remove"),
                New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=ConflictSwap, cancelIndex:=ConflictCancel)
    End Function

    Friend Const EditorCloseSave As Integer = 0
    Friend Const EditorCloseDiscard As Integer = 1
    Friend Const EditorCloseCancel As Integer = 2

    ''' <summary>Closing the image editor with unsaved edits (126 UX-605). The answers are
    ''' NAMED - the old Yes/No box made the user press "No" to mean "keep editing", which
    ''' is a question about the buttons rather than about the work. Both real answers are
    ''' destructive in their own way ("Сохранить" overwrites the original, "Не сохранять"
    ''' throws the edits away), so the default - and Escape - is "Отмена" (audit 046 C-01:
    ''' a stray Enter from the editing session must not land on either).</summary>
    Friend Shared Function EditorUnsavedClose() As AskSpec
        Return New AskSpec(
            Localization.T("Правки не сохранены"),
            Localization.T("Закрыть редактор и потерять несохранённые правки?"),
            AskIcon.Warning,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Сохранить"), destructive:=True, role:=ButtonGlyphRole.Affirmative, glyphId:="action.save"),
                New AskButton(Localization.T("Не сохранять"), destructive:=True, role:=ButtonGlyphRole.Irreversible, glyphId:="action.revert"),
                New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=EditorCloseCancel, cancelIndex:=EditorCloseCancel)
    End Function

    Friend Const TextEditUnsavedSave As Integer = 0
    Friend Const TextEditUnsavedDiscard As Integer = 1
    Friend Const TextEditUnsavedCancel As Integer = 2

    ''' <summary>Leaving a text with unsaved edits (277 section 7.1 item 3): every way out of the document asks this one
    ''' question. Keeps the image editor's shape: the default and Escape stay in the text.</summary>
    Friend Shared Function TextEditUnsaved(fileName As String) As AskSpec
        Return New AskSpec(
            Localization.T("Правки не сохранены"),
            Localization.TF("В файле {0} есть несохранённые правки.", fileName),
            AskIcon.Warning,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Сохранить"), destructive:=True, role:=ButtonGlyphRole.Affirmative, glyphId:="action.save"),
                New AskButton(Localization.T("Не сохранять"), destructive:=True, role:=ButtonGlyphRole.Irreversible, glyphId:="action.revert"),
                New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=TextEditUnsavedCancel, cancelIndex:=TextEditUnsavedCancel)
    End Function

    Friend Const TextEditConflictReload As Integer = 0
    Friend Const TextEditConflictSaveAs As Integer = 1
    Friend Const TextEditConflictOverwrite As Integer = 2
    Friend Const TextEditConflictCancel As Integer = 3

    ''' <summary>Another program changed the file under the editor (277 section 7.3 item 1): nothing was written. The body
    ''' says what each acting answer costs, so the named answer is the confirmation itself.</summary>
    Friend Shared Function TextEditConflict(fileName As String) As AskSpec
        Return New AskSpec(
            Localization.T("Файл изменён другой программой"),
            Localization.TF("Файл {0} изменился на диске после открытия. Загрузка с диска отбросит ваши правки, перезапись заменит ту версию.", fileName),
            AskIcon.Warning,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Загрузить с диска"), destructive:=True, role:=ButtonGlyphRole.Irreversible, glyphId:="action.refresh"),
                New AskButton(Localization.T("Сохранить как.."), glyphId:="action.save"),
                New AskButton(Localization.T("Перезаписать"), destructive:=True, role:=ButtonGlyphRole.Irreversible, glyphId:="action.replace"),
                New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=TextEditConflictCancel, cancelIndex:=TextEditConflictCancel)
    End Function

    Friend Const TextEditMissingWrite As Integer = 0
    Friend Const TextEditMissingSaveAs As Integer = 1
    Friend Const TextEditMissingCancel As Integer = 2

    ''' <summary>The file is gone from the disk while the editor holds its text: nothing was written.</summary>
    Friend Shared Function TextEditMissing(fileName As String) As AskSpec
        Return New AskSpec(
            Localization.T("Файла больше нет"),
            Localization.TF("Файл {0} удалён или перемещён другой программой. Ваши правки целы.", fileName),
            AskIcon.Warning,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Записать заново"), role:=ButtonGlyphRole.Affirmative, glyphId:="action.save"),
                New AskButton(Localization.T("Сохранить как.."), glyphId:="action.save"),
                New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=TextEditMissingCancel, cancelIndex:=TextEditMissingCancel)
    End Function

    Friend Const TextEditUnencodableUtf8 As Integer = 0
    Friend Const TextEditUnencodableReplace As Integer = 1
    Friend Const TextEditUnencodableCancel As Integer = 2

    ''' <summary>A character of the text is not in the file's encoding (277 section 6.2): nothing was written, and nothing is
    ''' ever replaced silently - the substitution is its own named answer with its count on it.</summary>
    Friend Shared Function TextEditUnencodable(encodingName As String, characters As IReadOnlyList(Of String), count As Integer) As AskSpec
        Return New AskSpec(
            Localization.T("Символы не помещаются в кодировку"),
            Localization.TF("Символов вне кодировки {0}: {1}. Файл не записан.", encodingName, count),
            AskIcon.Warning,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Сохранить в UTF-8"), role:=ButtonGlyphRole.Affirmative, glyphId:="action.save"),
                New AskButton(Localization.TF("Заменить на ? ({0})", count), destructive:=True, role:=ButtonGlyphRole.Irreversible, glyphId:="action.replace"),
                New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=TextEditUnencodableCancel, cancelIndex:=TextEditUnencodableCancel,
            detail:=String.Join(" ", characters))
    End Function

    Friend Const TextOpenBinaryShow As Integer = 0
    Friend Const TextOpenBinaryCancel As Integer = 1

    ''' <summary>"Open as Text" on a file that looks binary (277 section 4.1, VM-08): it says so before anything is shown. Only
    ''' looking follows, so the default is the answer the user asked for.</summary>
    Friend Shared Function TextOpenBinary(fileName As String) As AskSpec
        Return New AskSpec(
            Localization.T("Бинарный файл"),
            Localization.TF("Файл {0} похож на двоичные данные, а не на текст. Он откроется только для просмотра: управляющие байты будут показаны значками, править его будет нельзя.", fileName),
            AskIcon.Warning,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Показать как текст"), role:=ButtonGlyphRole.Affirmative, glyphId:="action.show-code"),
                New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=TextOpenBinaryShow, cancelIndex:=TextOpenBinaryCancel)
    End Function

    Friend Const TextDraftRestore As Integer = 0
    Friend Const TextDraftDelete As Integer = 1
    Friend Const TextDraftLater As Integer = 2

    ''' <summary>A file opens that has a recovery draft saying something else (277 section 7.4 item 2). Escape keeps the
    ''' draft for the next open: a draft goes only by a confirmed save or by the user's own answer.</summary>
    Friend Shared Function TextDraftFound(fileName As String, savedAt As String) As AskSpec
        Return New AskSpec(
            Localization.T("Найден черновик правок"),
            Localization.TF("Для файла {0} сохранён черновик несохранённых правок от {1}.", fileName, savedAt),
            AskIcon.Question,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Восстановить черновик"), role:=ButtonGlyphRole.Affirmative, glyphId:="action.undo"),
                New AskButton(Localization.T("Удалить черновик"), destructive:=True, role:=ButtonGlyphRole.Irreversible, glyphId:="action.delete"),
                New AskButton(Localization.T("Не сейчас"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=TextDraftRestore, cancelIndex:=TextDraftLater)
    End Function

    ''' <summary>Put every shortcut back to the factory binding. The default is the way out.</summary>
    Friend Shared Function ResetHotkeys() As AskSpec
        Return New AskSpec(
            Localization.T("Сочетания клавиш"),
            Localization.T("Вернуть все сочетания к значениям по умолчанию?"),
            AskIcon.Question,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Вернуть к умолчаниям"), destructive:=True, role:=ButtonGlyphRole.Irreversible, glyphId:="action.reset"),
                New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=1, cancelIndex:=1)
    End Function

    Friend Const ProfileResetExecute As Integer = 0
    Friend Const ProfileResetCancel As Integer = 1

    ''' <summary>169: Reset all viewer settings on the About page. Destructive button with Cancel as safe default.</summary>
    Friend Shared Function ProfileResetConfirm() As AskSpec
        Return New AskSpec(
            Localization.T("Сброс настроек"),
            Localization.T("Сбросить все настройки просмотрщика к значениям по умолчанию?"),
            AskIcon.Warning,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Сбросить и закрыть"), destructive:=True, role:=ButtonGlyphRole.Irreversible, glyphId:="action.reset"),
                New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=ProfileResetCancel, cancelIndex:=ProfileResetCancel,
            detail:=Localization.T("Программа закроется, а настройки вернутся к умолчаниям при следующем запуске. Получатели (клавиши 0-9 и Delete), вид, видео, OCR и положение окон будут сброшены. Язык, тема, пароли, список ресурсов и история останутся. Прежние значения сохранятся в файл."))
    End Function

    Friend Const HtmlHistoryClearExecute As Integer = 0
    Friend Const HtmlHistoryClearCancel As Integer = 1

    ''' <summary>275: Clear HTML history on the Documents page. Names the scope (host-owned records only, never the
    ''' documents), Clear is destructive and Cancel is the default.</summary>
    Friend Shared Function HtmlHistoryClear() As AskSpec
        Return New AskSpec(
            Localization.T("Очистить историю HTML-страниц"),
            Localization.T("Удалить позиции чтения и оформление отдельных HTML-страниц?"),
            AskIcon.Warning,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Очистить"), destructive:=True, role:=ButtonGlyphRole.Irreversible, glyphId:="action.delete"),
                New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=HtmlHistoryClearCancel, cancelIndex:=HtmlHistoryClearCancel,
            detail:=Localization.T("Удаляются только записи программы. Сами документы и общие настройки не изменятся."))
    End Function

    ''' <summary>The index of "Close anyway" in <see cref="CloseWithUnfinishedFileOperations"/>.</summary>
    Friend Const CloseAnyway As Integer = 0
    ''' <summary>The index of "Wait longer" - also the default and the answer of Escape and the close box.</summary>
    Friend Const CloseKeepWaiting As Integer = 1

    ''' <summary>Closing the viewer while file operations are still running (ticket 243, Q-8, owner decision
    ''' 2026-10-27: a product-owned window, because a system box can open behind a viewer pinned "on top").
    ''' The old Yes/No box made "No" mean "close and drop the queue" and Escape could not be given a meaning; the
    ''' answers are NAMED here, the safe one is the default and the answer of Escape and the close box, and the
    ''' acting answer is a click or its accelerator away - so one stray Escape keeps the program open.</summary>
    Friend Shared Function CloseWithUnfinishedFileOperations(outstanding As Integer) As AskSpec
        Return New AskSpec(
            Localization.T("Закрытие программы"),
            Localization.TF("Файловые операции ещё выполняются: {0}.", outstanding),
            AskIcon.Warning,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Закрыть всё равно"), destructive:=True, role:=ButtonGlyphRole.Caution, glyphId:="nav.exit"),
                New AskButton(Localization.T("Подождать ещё"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=CloseKeepWaiting, cancelIndex:=CloseKeepWaiting,
            detail:=Localization.T("Если закрыть программу сейчас, незавершённые операции не выполнятся."))
    End Function

    ' --- prompts ---------------------------------------------------------------------------

    ''' <summary>Rename. Every refusal that used to be a bare status line after the window had closed - and had
    ''' thrown the typed name away - is now the validator's answer, shown beside the field with the text kept.</summary>
    ''' <param name="nameWithoutExtension">What the field starts with.</param>
    ''' <param name="extension">The extension the file keeps (with its dot), only to compose the target.</param>
    ''' <param name="targetExists">Whether a file of this complete name is already in the folder (a case-only
    ''' change of the same file is not a collision - the caller decides).</param>
    Friend Shared Function RenamePrompt(nameWithoutExtension As String, extension As String,
                                        targetExists As Func(Of String, Boolean)) As PromptSpec
        Dim validator As Func(Of String, String) =
            Function(text As String) As String
                If String.IsNullOrWhiteSpace(text) Then Return Localization.T("Введите имя файла")
                If Not FileOpRules.IsValidNewFileName(text) Then
                    Return NotifyRules.Classify(Localization.T("! Недопустимое имя файла: символы \ / : * ? < > | в нём нельзя")).Text
                End If
                If String.Equals(text & extension, nameWithoutExtension & extension, StringComparison.Ordinal) Then
                    Return NotifyRules.Classify(Localization.T("! Имя не изменено")).Text
                End If
                If targetExists IsNot Nothing AndAlso targetExists(text & extension) Then
                    Return NotifyRules.Classify(Localization.T("! Файл с таким именем уже есть")).Text
                End If
                Return Nothing
            End Function
        Return New PromptSpec(Localization.T("Переименование файла"), Localization.T("Введите новое имя файла:"),
                              nameWithoutExtension, Localization.T("Переименовать"), validator,
                              hint:=Localization.TF("Расширение {0} сохранится.", If(String.IsNullOrEmpty(extension), "-", extension)))
    End Function

    ''' <summary>Go to a file by its number. The range is the validator's business, so a mistyped number keeps the
    ''' field and says why instead of closing on a box titled "Error".</summary>
    Friend Shared Function JumpToNumberPrompt(currentNumber As Integer, total As Integer) As PromptSpec
        Dim validator As Func(Of String, String) =
            Function(text As String) As String
                Dim number As Integer
                If Not Integer.TryParse(If(text, String.Empty).Trim(), Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, number) Then
                    Return Localization.T("Неверный номер файла.")
                End If
                If number < 1 OrElse number > total Then Return Localization.T("Номер файла вне диапазона.")
                Return Nothing
            End Function
        Return New PromptSpec(Localization.T("Перейти к файлу"), Localization.T("Введите номер файла:"),
                              currentNumber.ToString(Globalization.CultureInfo.InvariantCulture), Localization.T("Перейти"), validator,
                              hint:=Localization.TF("От 1 до {0}", total))
    End Function

    ''' <summary>Open a URL. The instruction and the example addresses have their own room; Cancel and an empty
    ''' string are different answers (an empty one is refused with a reason, Cancel returns Nothing).</summary>
    Friend Shared Function OpenUrlPrompt(lastUrl As String) As PromptSpec
        Dim validator As Func(Of String, String) =
            Function(text As String) As String
                If String.IsNullOrWhiteSpace(text) Then Return Localization.T("Введите адрес")
                Return Nothing
            End Function
        Dim examples As String = "smb://server/share/movie.mkv" & vbLf & "http://server/video.mp4" & vbLf & "sftp://user@server/path/movie.mkv" & vbLf & vbLf &
                                 Localization.T("Обычные сетевые папки (\\сервер\шара\..) открываются кнопкой «Выбрать файл..» как есть.")
        Return New PromptSpec(Localization.T("Открыть URL"), Localization.T("Адрес видеопотока или файла на сервере:"),
                              lastUrl, Localization.T("Открыть"), validator, hint:=examples, selectAll:=True)
    End Function

    Friend Const DocumentConvertBrowser As Integer = 0
    Friend Const DocumentConvertOnly As Integer = 1
    Friend Const DocumentConvertLocal As Integer = 2
    Friend Const DocumentConvertPaid As Integer = 3
    Friend Const DocumentConvertCancel As Integer = 4

    ''' <summary>276 T-2: the job panel of the document command. Names the source and the converter that will run,
    ''' offers the modes the producer supports; the remembered mode is the default and Escape cancels. The paid mode asks
    ''' its own per-run question before anything starts (Step 5.1). The detail names where the verified page opens
    ''' (Step 4.1), the scope of an editor snapshot and the recognition the job asks for, never more than the request says
    ''' (OCR-INVOCATION 1.2 item D).</summary>
    Friend Shared Function DocumentConvertPanel(fileName As String, converterVersion As String, rememberedMode As String,
                                                Optional opensInViewer As Boolean = False, Optional recognitionForced As Boolean = False,
                                                Optional scopeNote As String = "") As AskSpec
        Dim defaultIndex As Integer = DocumentConvertBrowser
        If rememberedMode = DocHtmlJobArguments.ModeConvert Then defaultIndex = DocumentConvertOnly
        If rememberedMode = DocHtmlJobArguments.ModeLocal Then defaultIndex = DocumentConvertLocal
        If rememberedMode = DocHtmlJobArguments.ModePaid Then defaultIndex = DocumentConvertPaid
        Return New AskSpec(
            Localization.T("Обработать в doc-html-translate"),
            Localization.TF("Сделать из «{0}» HTML-страницу в doc-html-translate?", fileName),
            AskIcon.Question,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Открыть в браузере"), role:=ButtonGlyphRole.Affirmative, glyphId:="nav.open-external"),
                New AskButton(Localization.T("Только преобразовать"), glyphId:="action.convert"),
                New AskButton(Localization.T("Перевести через Ollama"), glyphId:="action.translate"),
                New AskButton(Localization.T("Перевести через Google"), glyphId:="action.translate"),
                New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=defaultIndex, cancelIndex:=DocumentConvertCancel,
            detail:=Localization.TF("Программа: {0}", converterVersion) & Environment.NewLine &
                If(String.IsNullOrEmpty(scopeNote), "", scopeNote & Environment.NewLine) &
                If(opensInViewer, Localization.T("Страница откроется в просмотрщике поверх документа."),
                   Localization.T("Страница откроется в браузере; перевести её можно командой браузера «Перевести».")) & " " &
                Localization.T("«Только преобразовать» не переводит текст. «Перевести через Ollama» переводит его локальной моделью Ollama, «Перевести через Google» - платной службой Google; обе настраиваются в doc-html-translate.") & " " &
                If(recognitionForced, Localization.T("Распознавание текста запрашивается для всех страниц, исходный файл не изменяется."),
                   Localization.T("Распознавание текста запрашивается для сканов, исходный файл не изменяется.")))
    End Function

    Friend Const DocumentPaidSend As Integer = 0
    Friend Const DocumentPaidCancel As Integer = 1

    ''' <summary>276 Step 5.1, section 5: the per-run approval of a paid translation. It names the provider, what is sent
    ''' and the cap; the cap is the converter's own guard (OCR-INVOCATION 1.2 item G). Cancel is the default and the
    ''' answer of Escape, so nothing is spent on a reflex Enter.</summary>
    Friend Shared Function DocumentPaidApproval(fileName As String, costLimit As Integer, Optional scopeNote As String = "") As AskSpec
        Return New AskSpec(
            Localization.T("Перевести через Google"),
            Localization.TF("Отправить текст «{0}» на перевод в Google?", fileName),
            AskIcon.Warning,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Отправить и перевести"), role:=ButtonGlyphRole.Affirmative, glyphId:="action.translate"),
                New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=DocumentPaidCancel, cancelIndex:=DocumentPaidCancel,
            detail:=If(String.IsNullOrEmpty(scopeNote), "", scopeNote & Environment.NewLine) &
                Localization.TF("Перевод платный, расход идёт по вашему ключу Google, сохранённому в doc-html-translate. Предел на этот запуск: {0} $; если оценка выше, doc-html-translate не начнёт перевод. Текст уходит в Google, исходный файл не изменяется.", costLimit))
    End Function

    Friend Const DocumentPaidOpenWindow As Integer = 0
    Friend Const DocumentPaidNoLimitCancel As Integer = 1

    ''' <summary>276 Step 5.1, section 5: no cap is set, so the viewer starts no paid work itself; the converter's own
    ''' window asks about the cost (OCR-INVOCATION 1.2 item G). Cancel is the default and the answer of Escape.</summary>
    Friend Shared Function DocumentPaidNoLimit(fileName As String) As AskSpec
        Return New AskSpec(
            Localization.T("Перевести через Google"),
            Localization.T("Предел расходов на перевод через Google не задан, поэтому перевод делается в окне doc-html-translate."),
            AskIcon.Info,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Открыть окно doc-html-translate"), role:=ButtonGlyphRole.Affirmative, glyphId:="nav.open-external"),
                New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=DocumentPaidNoLimitCancel, cancelIndex:=DocumentPaidNoLimitCancel,
            detail:=Localization.TF("В окне выберите файл «{0}»: doc-html-translate сама спросит о расходах. Чтобы переводить отсюда, задайте предел в настройках: Документы > doc-html-translate.", fileName))
    End Function

    Friend Const DocumentConverterInstall As Integer = 0
    Friend Const DocumentConverterChoose As Integer = 1
    Friend Const DocumentConverterCheckAgain As Integer = 2
    Friend Const DocumentConverterCancel As Integer = 3

    ''' <summary>276 T-2, journey step 3: the converter is missing or did not answer. Install opens winget (else the
    ''' Store page) and installs nothing silently; the user can also point at the program or check again after an
    ''' install made outside the viewer.</summary>
    Friend Shared Function DocumentConverterMissing(notRunnable As Boolean, chosenPath As String) As AskSpec
        Dim detail As String
        If notRunnable Then
            detail = Localization.TF("Программа не ответила на проверку версии: {0}", chosenPath)
        ElseIf Not String.IsNullOrEmpty(chosenPath) Then
            detail = Localization.TF("Выбранная программа не найдена: {0}", chosenPath)
        Else
            detail = Localization.T("doc-html-translate не найдена на этом компьютере.")
        End If
        Return New AskSpec(
            Localization.T("Обработать в doc-html-translate"),
            Localization.T("Для этой команды нужна бесплатная программа doc-html-translate: она делает из документа HTML-страницу, распознаёт текст сканов и может перевести его."),
            AskIcon.Info,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Установить"), role:=ButtonGlyphRole.Affirmative, glyphId:="action.install"),
                New AskButton(Localization.T("Выбрать программу.."), glyphId:="action.choose-file"),
                New AskButton(Localization.T("Проверить снова"), glyphId:="action.refresh"),
                New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=DocumentConverterInstall, cancelIndex:=DocumentConverterCancel, detail:=detail)
    End Function

    Friend Const DocumentJobStop As Integer = 0
    Friend Const DocumentJobContinue As Integer = 1

    ''' <summary>276 T-3: the command pressed while the viewer's own job still runs. Continue is the default and the
    ''' answer of Escape; Stop ends only the process the viewer started (section 6).</summary>
    Friend Shared Function DocumentJobRunning(fileName As String) As AskSpec
        Return New AskSpec(
            Localization.T("Обработать в doc-html-translate"),
            Localization.TF("doc-html-translate ещё обрабатывает «{0}».", fileName),
            AskIcon.Question,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Остановить"), destructive:=True, role:=ButtonGlyphRole.Caution, glyphId:="action.cancel"),
                New AskButton(Localization.T("Продолжить"), glyphId:="action.run")},
            defaultIndex:=DocumentJobContinue, cancelIndex:=DocumentJobContinue,
            detail:=Localization.T("Если остановить, незаконченная страница не откроется; следующий запуск сделает её заново."))
    End Function

    Friend Const DocumentResultsClear As Integer = 0
    Friend Const DocumentResultsCancel As Integer = 1

    ''' <summary>276 Step 3.2, section 6 quota: the pages a browser may still show fill the space, and the viewer never
    ''' deletes such a page by itself. Clear is the user's own decision; Escape cancels the new job.</summary>
    Friend Shared Function DocumentResultsFull(usedMb As Long, budgetMb As Integer) As AskSpec
        Return New AskSpec(
            Localization.T("Обработать в doc-html-translate"),
            Localization.TF("Место для готовых страниц занято: {0} из {1} МБ.", usedMb, budgetMb),
            AskIcon.Warning,
            New List(Of AskButton) From {
                New AskButton(Localization.T("Очистить готовые страницы"), destructive:=True, role:=ButtonGlyphRole.Caution, glyphId:="action.clear-all"),
                New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)},
            defaultIndex:=DocumentResultsCancel, cancelIndex:=DocumentResultsCancel,
            detail:=Localization.T("Страницы, открытые в браузере, сами не удаляются. Очистка удалит все готовые страницы doc-html-translate; исходные документы не меняются."))
    End Function

    ''' <summary>Every call-site question that has a fixed shape, for the test that walks them all.</summary>
    Friend Shared Function All() As IReadOnlyList(Of AskSpec)
        Dim none As New AskFileFacts(1024, New Date(2026, 1, 2, 3, 4, 5))
        Return New List(Of AskSpec) From {
            NameCollision("photo.jpg", none, none),
            ReplacePolicyWarning("photo.jpg"),
            SettingsImport("1"),
            HotkeyConflict("Ctrl+K", "Action"),
            ResetHotkeys(),
            ProfileResetConfirm(),
            EditorUnsavedClose(),
            TextEditUnsaved("note.txt"),
            TextEditConflict("note.txt"),
            TextEditMissing("note.txt"),
            TextEditUnencodable("Windows-1251", New String() {"U+5B57"}, 1),
            TextDraftFound("note.txt", "14:32"),
            HtmlHistoryClear(),
            DocumentConvertPanel("book.epub", "doc-html-translate 1.0", DocHtmlJobArguments.ModeBrowser),
            DocumentConvertPanel("book.epub", "doc-html-translate 1.0", DocHtmlJobArguments.ModeLocal, opensInViewer:=True, recognitionForced:=True),
            DocumentConvertPanel("notes.txt", "doc-html-translate 1.0", DocHtmlJobArguments.ModePaid, scopeNote:="selection"),
            DocumentPaidApproval("notes.selection.txt", 5, "selection"),
            DocumentPaidNoLimit("book.epub"),
            DocumentConverterMissing(False, ""),
            DocumentJobRunning("book.epub"),
            DocumentResultsFull(1100, 1024),
            CloseWithUnfinishedFileOperations(2)}
    End Function

End Class
#End If
