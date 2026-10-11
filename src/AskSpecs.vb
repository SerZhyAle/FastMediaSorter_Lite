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
                New AskButton(Localization.T("Пропустить"), role:=ButtonGlyphRole.Dismissing, glyphId:="media.next")},
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
            EditorUnsavedClose()}
    End Function

End Class
#End If
