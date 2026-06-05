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
            lbl_Help_Info.Text = " Програма для быстрого переноса/копирования изображений по папкам." & Chr(10) & Chr(10) &
                "Сначала заполните таблицу каталогов-получателей по клавишам 1,2,3.. - 0. " & Chr(10) &
                "Затем укажите каталог-источник для сортировки. " & Chr(10) &
                "Продвигайтесь по файлам с помощью стрелок, P/N (PgDn/PgUp) или кликов/скролла мыши. " & Chr(10) &
                "Стрелки вверх-вниз: +10-10 и Shift+ PgDn/PgUp: + 100/ - 100 файлов" & Chr(10) &
                "Y- случайно, S- случайное слайдшоу, I- слайдшоу. " & Chr(10) &
                "R/T для поворота картинки. " & Chr(10) &
                "F3 для просмотра пагнли изображений папки. " & Chr(10) &
                "F6 для переименования файла. " & Chr(10) &
                "Или за счет переноса/копирования по папкам клавишами (1,2,3.. - 0). " & Chr(10) &
                "Или за счет удаления текущего файла (del). " & Chr(10) &
                "Окно таблицы можно закрепить и щелкать мышью по колонке с цифрой. " & Chr(10) &
                "(U) -вернуть последный перенесенный файл (удалить скопированный). " & Chr(10) & Chr(10) &
                " Щелкните на этот текст (F1) для того, чтобы он исчез."
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
            lbl_Help_Info.Text = " Program for fast image sorting." & Chr(10) & Chr(10) &
                "First fill dest folders table for keys: 1,2.. - 0. " & Chr(10) &
                "After set folder with you unsorted files. " & Chr(10) &
                "Go with files by P/N (PgDn/PgUp) keys or mouse clicks/scroll. " & Chr(10) &
                "Up/Down- +10-10 and Shift+ PgDn/PgUp- + 100/ - 100 files" & Chr(10) &
                "Y- random, S- random slide, I- slide. " & Chr(10) &
                "Or move/copy files into dest folders by keys (1,2.. - 0). " & Chr(10) &
                "Or by deleting files (del key). " & Chr(10) &
                "R/T to rotate the image. " & Chr(10) &
                "F3 to see the panel of folder's images. " & Chr(10) &
                "F6 to rename the file. " & Chr(10) &
                "You can lock Window with folders table and click on key numbers. " & Chr(10) &
                "(U)ndo last moved action (delete copying file). " & Chr(10) & Chr(10) &
                " Click on this text (F1) for hide it."

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0040: English is set")
        End If


    End Sub

    Private Sub ButtonLNG_Click(sender As Object, e As EventArgs) Handles btn_Language.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2020: btn_Language")

        Is_Russian_Language = Not Is_Russian_Language
        btn_Language.Text = If(Is_Russian_Language, "EN", "RU")
        LngCh()
        Table_Form.LngCh()
        'ReadShowMediaFile("SetFile")
    End Sub

End Class
