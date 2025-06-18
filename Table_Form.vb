'sza130806
'sza250617
Option Strict On

Public Class Table_Form
    Private set_This_Form_Top_Most As Boolean = False

    Private Sub Form2_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        SaveSetting(App_name, Second_App_Name, "SetOnTop", If(set_This_Form_Top_Most, "1", "0"))
    End Sub

    Private Sub Form2_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n00-2: the_Table_Form_Load")

        cmbox_color_schema.Items.Clear()
        cmbox_color_schema.Items.Add(If(Is_Russian_Language, "По углу", "By corner")) '0
        cmbox_color_schema.Items.Add(If(Is_Russian_Language, "Черный", "Black")) '1
        cmbox_color_schema.Items.Add(If(Is_Russian_Language, "Белый", "White")) '2
        cmbox_color_schema.Items.Add(If(Is_Russian_Language, "По краю", "By side")) '3
        cmbox_color_schema.Items.Add(If(Is_Russian_Language, "По верху", "By top")) '4
        cmbox_color_schema.Items.Add(If(Is_Russian_Language, "По низу", "By buttom")) '5
        cmbox_color_schema.SelectedIndex = Form_Color_Scheme

        chkb_show_pic_size.Checked = Main_Form.Is_to_show_picture_sizes
        chkb_is_to_show_file_datetime.Checked = Main_Form.is_to_show_file_datetime
        chkb_show_file_size.Checked = Main_Form.Is_to_show_file_sizes

        chb_perspectiva.Text = If(Is_Russian_Language, "Перспектива", "Perspective")

        chkbox_Copy_Mode.Checked = Is_Copying_not_Moving
        Data_Grid_View.Rows.Clear()
        Data_Grid_View.Rows.Add()
        Data_Grid_View.Item(0, 0).Value = "DEL"
        Data_Grid_View.Item(0, 0).ReadOnly = True
        Data_Grid_View.Item(1, 0).Value = If(Is_Russian_Language, "Удаление файла", "Delete file")
        For z As Integer = 1 To 10
            Data_Grid_View.Rows.Add()
            Data_Grid_View.Item(0, z).Value = z.ToString()
            Data_Grid_View.Item(1, z).Value = If(Hardkeys_to_move_mediafile(z), "")
        Next

        Data_Grid_View.Item(0, 10).Value = "0"
        If Is_Russian_Language Then
            Me.Text = "Таблица каталогов-получателей переноса/копирования"
            Data_Grid_View.Columns(0).HeaderText = "клавиша"
            Data_Grid_View.Columns(1).HeaderText = "каталог-получатель"
            chkbox_Copy_Mode.Text = "Режим копирования файлов (не перенос)"
            chkbox_Independent_Thread_For_File_Operation.Text = "Использовать независимые потоки для операций с файлами"
            Tab_Page_1.Text = "Каталоги-получатели"
            Tab_Page_2.Text = "Настройки"
            lbl_Color.Text = "Цвет фона:"
            chkb_show_pic_size.Text = "Показывать размер изображений"
            chkb_show_file_size.Text = "Показывать размер файлов"
            chkb_is_to_show_file_datetime.Text = "Показывать дату и время файла"
        Else
            Me.Text = "Table of dest folder for moving/copy"
            Data_Grid_View.Columns(0).HeaderText = "KEY"
            Data_Grid_View.Columns(1).HeaderText = "destanation folder"
            chkbox_Copy_Mode.Text = "COPY mode (files are not moving)"
            chkbox_Independent_Thread_For_File_Operation.Text = "Use independent thread for operations with files"
            Tab_Page_1.Text = "Dest folders"
            Tab_Page_2.Text = "Settings"
            lbl_Color.Text = "Background color:"
            chkb_show_pic_size.Text = "Show picture sizes"
            chkb_show_file_size.Text = "Show file sizes"
            chkb_is_to_show_file_datetime.Text = "Show file datetime"
        End If

        Dim SetOnTopS As String = GetSetting(App_name, Second_App_Name, "SetOnTop", "1")
        set_This_Form_Top_Most = SetOnTopS = "1"

        chb_perspectiva.Checked = Is_Pespective

        TopOrNot()
    End Sub

    Private Sub DataGridView1_CellMouseDoubleClick(sender As Object, e As DataGridViewCellMouseEventArgs) Handles Data_Grid_View.CellMouseDoubleClick
        If e.ColumnIndex = 0 Then
            Main_Form.DoKey(e.RowIndex)
        Else
            If e.RowIndex > 0 Then
                Dim folderBrowse As New FolderBrowserDialog()
                folderBrowse.SelectedPath = Hardkeys_to_move_mediafile(e.RowIndex)
                Dim textKey As String = e.RowIndex.ToString
                If textKey = "10" Then textKey = "0"
                folderBrowse.Description = If(Is_Russian_Language, "Укажите каталог переноса/копирования для клавиши " + textKey, "Select dest folder for key " + textKey)
                If folderBrowse.ShowDialog() = Windows.Forms.DialogResult.OK Then
                    Hardkeys_to_move_mediafile(e.RowIndex) = folderBrowse.SelectedPath
                    Data_Grid_View.Item(1, e.RowIndex).Value = Hardkeys_to_move_mediafile(e.RowIndex)
                    Data_Grid_View.Refresh()
                End If
            End If
        End If
    End Sub

    Private Sub DataGridView1_CellEndEdit(sender As Object, e As DataGridViewCellEventArgs) Handles Data_Grid_View.CellEndEdit
        If Data_Grid_View.Item(1, e.RowIndex).Value Is Nothing Then
            Hardkeys_to_move_mediafile(e.RowIndex) = ""
        Else
            Hardkeys_to_move_mediafile(e.RowIndex) = Data_Grid_View.Item(1, e.RowIndex).Value.ToString()
        End If
    End Sub

    Private Sub SetOnTop_Click(sender As Object, e As EventArgs) Handles SetOnTop.Click
        set_This_Form_Top_Most = Not set_This_Form_Top_Most
        TopOrNot()
    End Sub

    Private Sub TopOrNot()
        Me.TopMost = set_This_Form_Top_Most
        If set_This_Form_Top_Most Then
            SetOnTop.Text = If(Is_Russian_Language, "отключить поверх окон", "set OFF top this table")
        Else
            SetOnTop.Text = If(Is_Russian_Language, "держать поверх окон", "set ON Top this table")
        End If
    End Sub

    Private Sub Form2_KeyDown(sender As Object, e As KeyEventArgs) Handles MyBase.KeyDown
        Main_Form.KeybUse(e, Main_Form.GetWas_slideshow())
    End Sub

    Private Sub CheckBox1_CheckedChanged(sender As Object, e As EventArgs) Handles chkbox_Copy_Mode.CheckedChanged
        Is_Copying_not_Moving = chkbox_Copy_Mode.Checked
    End Sub

    Private Sub Data_Grid_View_CellContentClick(sender As Object, e As DataGridViewCellEventArgs) Handles Data_Grid_View.CellContentClick

    End Sub

    Private Sub Cmbox_color_schema_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbox_color_schema.SelectedIndexChanged
        Form_Color_Scheme = cmbox_color_schema.SelectedIndex
    End Sub

    Private Sub Chb_perspectiva_CheckedChanged(sender As Object, e As EventArgs) Handles chb_perspectiva.CheckedChanged
        Is_Pespective = chb_perspectiva.Checked
    End Sub

    Private Sub Chkb_show_pic_size_CheckedChanged(sender As Object, e As EventArgs) Handles chkb_show_pic_size.CheckedChanged
        Main_Form.Is_to_show_picture_sizes = chkb_show_pic_size.Checked
    End Sub

    Private Sub Chkb_is_to_show_file_datetime_CheckedChanged(sender As Object, e As EventArgs) Handles chkb_is_to_show_file_datetime.CheckedChanged
        Main_Form.Is_to_show_file_datetime = chkb_is_to_show_file_datetime.Checked
    End Sub

    Private Sub Chkb_show_file_size_CheckedChanged(sender As Object, e As EventArgs) Handles chkb_show_file_size.CheckedChanged
        Main_Form.Is_to_show_file_sizes = chkb_show_file_size.Checked
    End Sub
End Class