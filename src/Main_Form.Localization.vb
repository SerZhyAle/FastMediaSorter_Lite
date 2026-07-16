Option Strict On

Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports Microsoft.Win32
Imports System.Diagnostics

Partial Public Class Main_Form

    Public Sub LngCh()
        If lbl_Status.Text = "status" Then lbl_Status.Text = ""

        If Is_Russian_Language Then
            lbl_Folder.Text = "Каталог:"
            btn_Prev_File.Text = "<< пред(PgUp)"
            btn_Next_File.Text = "след(PgDn) >>"
            bt_Delete.Text = "удалить (del)"
            btn_Move_Table.Text = "Настройки"
            lbl_Help_Info.Text = " Программа для быстрого переноса/копирования изображений по папкам. Да, та самая, которой вам не хватало." & Chr(10) & Chr(10) &
                "Сначала заполните таблицу каталогов-получателей по клавишам 1,2,3.. - 0. " & Chr(10) &
                "Затем укажите каталог-источник для сортировки (тот самый, где творится беспорядок). " & Chr(10) &
                "Продвигайтесь по файлам с помощью стрелок, P/N (PgDn/PgUp) или кликов/скролла мыши. " & Chr(10) &
                "Стрелки вверх/вниз: -10/+10 и Shift+ PgDn/PgUp: +100/-100 файлов" & Chr(10) &
                "Y- случайно, S- слайдшоу, I- случайное слайдшоу. " & Chr(10) &
                "R/Shift+R для поворота картинки, если фотограф держал камеру боком (T тоже, пока не включён OCR-перевод). " & Chr(10) &
                ZoomHelpLine() &
                "F3 для просмотра панели изображений папки. " & Chr(10) &
                "F6 для переименования файла. " & Chr(10) &
                "Или раскидайте файлы по папкам клавишами (1,2,3.. - 0) - по одному нажатию на файл. " & Chr(10) &
                "Или удалите текущий файл (del), если он того заслужил. " & Chr(10) &
                "Окно таблицы можно закрепить и щёлкать мышью по колонке с цифрой. " & Chr(10) &
                "(U) - вернуть последний перенесённый файл (на случай, если рука дрогнула). " & Chr(10) & Chr(10) &
                " Щёлкните на этот текст (F1), чтобы он деликатно исчез."
            btn_Language.Text = "EN"

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0030: Russian is set")
        Else
            btn_Language.Text = "RU"
            Is_Russian_Language = False
            lbl_Folder.Text = "Folder:"
            btn_Prev_File.Text = "<< (P)rev"
            btn_Next_File.Text = "(N)ext >>"
            bt_Delete.Text = "(D)elete"
            btn_Move_Table.Text = "Settings"
            lbl_Help_Info.Text = " Program for fast image sorting. Yes, the one you've been missing." & Chr(10) & Chr(10) &
                "First fill the destination-folders table for keys: 1,2.. - 0. " & Chr(10) &
                "Then point it at the folder with your unsorted files (you know the one). " & Chr(10) &
                "Move through files with P/N (PgDn/PgUp) keys or mouse clicks/scroll. " & Chr(10) &
                "Up/Down: -10/+10 and Shift+ PgDn/PgUp: +100/-100 files" & Chr(10) &
                "Y- random, S- slide, I- random slide. " & Chr(10) &
                "Or move/copy files into dest folders by keys (1,2.. - 0) - one keypress per file. " & Chr(10) &
                "Or delete files (del key), if they had it coming. " & Chr(10) &
                "R/Shift+R to rotate the image, for when the photographer held the camera sideways (T too, unless OCR translation is on). " & Chr(10) &
                ZoomHelpLine() &
                "F3 to see the panel of folder's images. " & Chr(10) &
                "F6 to rename the file. " & Chr(10) &
                "You can pin the folders-table window and click on the key numbers. " & Chr(10) &
                "(U)ndo the last move (for when your hand slipped). " & Chr(10) & Chr(10) &
                " Click on this text (F1) to make it tactfully disappear."

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0040: English is set")
        End If

#If Not NETFRAMEWORK Then
        LocalizeShareEntryPoint()
        LocalizeOpenUrlEntryPoint()
        LocalizeVideoTracks()
#End If
        LocalizeToolbarOverflow()
        LocalizeBrowserTranslate()

        ' Button captions just changed width - re-flow the single-row toolbar.
        LayoutToolbar()

    End Sub

    ''' <summary>
    ''' The zoom line of the first-run help, or "" where there is nothing to promise.
    ''' The classic zoom model is modern-only (the x86 viewer keeps the historical
    ''' mechanics - SPECIFICATION_ZOOM_PAN_CLASSIC_DOTNET10.md), so the x86 help must
    ''' not advertise keys that do nothing there. Ends with its own newline so the
    ''' surrounding text closes up when it is empty.
    ''' </summary>
    Private Function ZoomHelpLine() As String
#If NETFRAMEWORK Then
        Return ""
#Else
        If Is_Russian_Language Then
            Return "Масштаб: серый + и - (от курсора), серый / - вписать, серый * - 100 %; колесо можно переключить на масштаб в настройках. " & Chr(10) &
                   "У видео: A - следующая звуковая дорожка, V - следующие субтитры (и кнопка ""Дорожки"", когда есть из чего выбрать). " & Chr(10)
        End If
        Return "Zoom: grey + and - (at the cursor), grey / to fit, grey * for 100 %; the wheel can be switched to zoom in Settings. " & Chr(10) &
               "For video: A - next audio track, V - next subtitles (and a ""Tracks"" button when there is a choice). " & Chr(10)
#End If
    End Function

    ''' <summary>
    ''' First-run UI language: follow the Windows display language. The app only
    ''' ships Russian and English, so the system counts as Russian only when its
    ''' display language is Russian; every other language falls back to English.
    ''' Once the user toggles the language (saved under the "Is_Russian_Language"
    ''' registry value) this probe is no longer consulted.
    ''' </summary>
    Friend Shared Function SystemDefaultIsRussian() As Boolean
        Try
            ' CurrentUICulture is the user-chosen Windows display language. Fall
            ' back to the OS-installed UI culture only when it is unusable (the
            ' invariant culture has an empty name).
            Dim ui = System.Globalization.CultureInfo.CurrentUICulture
            If ui Is Nothing OrElse ui.Name.Length = 0 Then
                ui = System.Globalization.CultureInfo.InstalledUICulture
            End If

            Return ui IsNot Nothing AndAlso
                   String.Equals(ui.TwoLetterISOLanguageName, "ru", StringComparison.OrdinalIgnoreCase)
        Catch
            ' If culture probing fails, default to English (the broader audience).
            Return False
        End Try
    End Function

    Private Sub ButtonLNG_Click(sender As Object, e As EventArgs) Handles btn_Language.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2020: btn_Language")

        Is_Russian_Language = Not Is_Russian_Language
        btn_Language.Text = If(Is_Russian_Language, "EN", "RU")
        LngCh()
        Table_Form.LngCh()
        'ReadShowMediaFile("SetFile")
    End Sub

End Class
