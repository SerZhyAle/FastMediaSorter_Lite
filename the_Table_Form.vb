'sza130806
Option Strict On

Public Class the_Table_Form
    Private setThisOnTop As Boolean = False

    Private Sub Form2_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        SaveSetting(appName, secName, "SetOnTop", If(setThisOnTop, "1", "0"))
    End Sub

    Private Sub Form2_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        chkbox_Copy_Mode.Checked = copyMode
        Data_Grid_View.Rows.Clear()
        Data_Grid_View.Rows.Add()
        Data_Grid_View.Item(0, 0).Value = "DEL"
        Data_Grid_View.Item(0, 0).ReadOnly = True
        Data_Grid_View.Item(1, 0).Value = If(lngRus, "Удаление файла", "Delete file")
        For z As Integer = 1 To 10
            Data_Grid_View.Rows.Add()
            Data_Grid_View.Item(0, z).Value = z.ToString()
            Data_Grid_View.Item(1, z).Value = If(moveOnKey(z) Is Nothing, "", moveOnKey(z))
        Next
        Data_Grid_View.Item(0, 10).Value = "0"
        If lngRus Then
            Me.Text = "Таблица каталогов-получателей переноса/копирования"
            Data_Grid_View.Columns(0).HeaderText = "клавиша"
            Data_Grid_View.Columns(1).HeaderText = "каталог-получатель"
            chkbox_Copy_Mode.Text = "Режим копирования файлов (не перенос)"
            chkbox_Independent_Thread_For_File_Operation.Text = "Использовать независимые потоки для операций с файлами"
        Else
            Me.Text = "Table of dest folder for moving/copy"
            Data_Grid_View.Columns(0).HeaderText = "KEY"
            Data_Grid_View.Columns(1).HeaderText = "destanation folder"
            chkbox_Copy_Mode.Text = "COPY mode (files are not moving)"
            chkbox_Independent_Thread_For_File_Operation.Text = "Use independent thread for operations with files"
        End If
        Dim SetOnTopS As String = GetSetting(appName, secName, "SetOnTop", "1")
        If SetOnTopS = "1" Then
            setThisOnTop = True
        Else
            setThisOnTop = False
        End If
        TopOrNot()
    End Sub

    Private Sub DataGridView1_CellMouseDoubleClick(sender As Object, e As DataGridViewCellMouseEventArgs) Handles Data_Grid_View.CellMouseDoubleClick
        If e.ColumnIndex = 0 Then
            the_Main_Form.DoKey(e.RowIndex)
        Else
            If e.RowIndex > 0 Then
                Dim folderBrowse As New FolderBrowserDialog()
                folderBrowse.SelectedPath = moveOnKey(e.RowIndex)
                Dim textKey As String = e.RowIndex.ToString
                If textKey = "10" Then textKey = "0"
                folderBrowse.Description = If(lngRus, "Укажите каталог переноса/копирования для клавиши " + textKey, "Select dest folder for key " + textKey)
                If folderBrowse.ShowDialog() = Windows.Forms.DialogResult.OK Then
                    moveOnKey(e.RowIndex) = folderBrowse.SelectedPath
                    Data_Grid_View.Item(1, e.RowIndex).Value = moveOnKey(e.RowIndex)
                    Data_Grid_View.Refresh()
                End If
            End If
        End If
    End Sub

    Private Sub DataGridView1_CellEndEdit(sender As Object, e As DataGridViewCellEventArgs) Handles Data_Grid_View.CellEndEdit
        If Data_Grid_View.Item(1, e.RowIndex).Value Is Nothing Then
            moveOnKey(e.RowIndex) = ""
        Else
            moveOnKey(e.RowIndex) = Data_Grid_View.Item(1, e.RowIndex).Value.ToString()
        End If
    End Sub

    Private Sub SetOnTop_Click(sender As Object, e As EventArgs) Handles SetOnTop.Click
        setThisOnTop = Not setThisOnTop
        TopOrNot()
    End Sub

    Private Sub TopOrNot()
        Me.TopMost = setThisOnTop
        If setThisOnTop Then
            SetOnTop.Text = If(lngRus, "отключить поверх окон", "set OFF top this table")
        Else
            SetOnTop.Text = If(lngRus, "держать поверх окон", "set ON Top this table")
        End If
    End Sub

    Private Sub Form2_KeyDown(sender As Object, e As KeyEventArgs) Handles MyBase.KeyDown
        the_Main_Form.KeybUse(e)
    End Sub

    Private Sub CheckBox1_CheckedChanged(sender As Object, e As EventArgs) Handles chkbox_Copy_Mode.CheckedChanged
        copyMode = chkbox_Copy_Mode.Checked
    End Sub

    Private Sub Data_Grid_View_CellContentClick(sender As Object, e As DataGridViewCellEventArgs) Handles Data_Grid_View.CellContentClick

    End Sub
End Class