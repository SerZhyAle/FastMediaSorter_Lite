'sza130806
Option Strict On

Public Class Form2
    Private setThisOnTop As Boolean = False

    Private Sub Form2_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        SaveSetting(appName, secName, "SetOnTop", If(setThisOnTop, "1", "0"))
    End Sub

    Private Sub Form2_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        CheckBox1.Checked = copyMode
        DataGridView1.Rows.Clear()
        DataGridView1.Rows.Add()
        DataGridView1.Item(0, 0).Value = "DEL"
        DataGridView1.Item(0, 0).ReadOnly = True
        DataGridView1.Item(1, 0).Value = If(lngRus, "Удаление файла", "Delete file")
        For z As Integer = 1 To 10
            DataGridView1.Rows.Add()
            DataGridView1.Item(0, z).Value = z.ToString()
            DataGridView1.Item(1, z).Value = If(moveOnKey(z) Is Nothing, "", moveOnKey(z))
        Next
        DataGridView1.Item(0, 10).Value = "0"
        If lngRus Then
            Me.Text = "Таблица каталогов-получателей переноса/копирования"
            DataGridView1.Columns(0).HeaderText = "клавиша"
            DataGridView1.Columns(1).HeaderText = "каталог-получатель"
            CheckBox1.Text = "Режим копирования файлов (не перенос)"
            UseIndependentThreadForOperationsWithFiles.Text = "Использовать независимые потоки для операций с файлами"
        Else
            Me.Text = "Table of dest folder for moving/copy"
            DataGridView1.Columns(0).HeaderText = "KEY"
            DataGridView1.Columns(1).HeaderText = "destanation folder"
            CheckBox1.Text = "COPY mode (files are not moving)"
            UseIndependentThreadForOperationsWithFiles.Text = "Use independent thread for operations with files"
        End If
        Dim SetOnTopS As String = GetSetting(appName, secName, "SetOnTop", "1")
        If SetOnTopS = "1" Then
            setThisOnTop = True
        Else
            setThisOnTop = False
        End If
        TopOrNot()
    End Sub

    Private Sub DataGridView1_CellMouseDoubleClick(sender As Object, e As DataGridViewCellMouseEventArgs) Handles DataGridView1.CellMouseDoubleClick
        If e.ColumnIndex = 0 Then
            Form1.DoKey(e.RowIndex)
        Else
            If e.RowIndex > 0 Then
                Dim folderBrowse As New FolderBrowserDialog()
                folderBrowse.SelectedPath = moveOnKey(e.RowIndex)
                Dim textKey As String = e.RowIndex.ToString
                If textKey = "10" Then textKey = "0"
                folderBrowse.Description = If(lngRus, "Укажите каталог переноса/копирования для клавиши " + textKey, "Select dest folder for key " + textKey)
                If folderBrowse.ShowDialog() = Windows.Forms.DialogResult.OK Then
                    moveOnKey(e.RowIndex) = folderBrowse.SelectedPath
                    DataGridView1.Item(1, e.RowIndex).Value = moveOnKey(e.RowIndex)
                    DataGridView1.Refresh()
                End If
            End If
        End If
    End Sub

    Private Sub DataGridView1_CellEndEdit(sender As Object, e As DataGridViewCellEventArgs) Handles DataGridView1.CellEndEdit
        If DataGridView1.Item(1, e.RowIndex).Value Is Nothing Then
            moveOnKey(e.RowIndex) = ""
        Else
            moveOnKey(e.RowIndex) = DataGridView1.Item(1, e.RowIndex).Value.ToString()
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
        Form1.KeybUse(e)
    End Sub

    Private Sub CheckBox1_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBox1.CheckedChanged
        copyMode = CheckBox1.Checked
    End Sub
End Class