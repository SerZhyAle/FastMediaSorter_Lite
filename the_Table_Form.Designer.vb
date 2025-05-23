<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class the_Table_Form
    Inherits System.Windows.Forms.Form

    'Форма переопределяет dispose для очистки списка компонентов.
    <System.Diagnostics.DebuggerNonUserCode()> _
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Является обязательной для конструктора форм Windows Forms
    Private components As System.ComponentModel.IContainer

    'Примечание: следующая процедура является обязательной для конструктора форм Windows Forms
    'Для ее изменения используйте конструктор форм Windows Form.  
    'Не изменяйте ее в редакторе исходного кода.
    <System.Diagnostics.DebuggerStepThrough()> _
    Private Sub InitializeComponent()
        Me.Data_Grid_View = New System.Windows.Forms.DataGridView()
        Me.KeyNum = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.MoveToFolder = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.SetOnTop = New System.Windows.Forms.Label()
        Me.chkbox_Copy_Mode = New System.Windows.Forms.CheckBox()
        Me.chkbox_Independent_Thread_For_File_Operation = New System.Windows.Forms.CheckBox()
        CType(Me.Data_Grid_View, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'Data_Grid_View
        '
        Me.Data_Grid_View.AllowUserToAddRows = False
        Me.Data_Grid_View.AllowUserToDeleteRows = False
        Me.Data_Grid_View.AllowUserToOrderColumns = True
        Me.Data_Grid_View.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize
        Me.Data_Grid_View.Columns.AddRange(New System.Windows.Forms.DataGridViewColumn() {Me.KeyNum, Me.MoveToFolder})
        Me.Data_Grid_View.Location = New System.Drawing.Point(0, 2)
        Me.Data_Grid_View.Margin = New System.Windows.Forms.Padding(5, 6, 5, 6)
        Me.Data_Grid_View.MultiSelect = False
        Me.Data_Grid_View.Name = "Data_Grid_View"
        Me.Data_Grid_View.RowHeadersWidth = 62
        Me.Data_Grid_View.Size = New System.Drawing.Size(885, 508)
        Me.Data_Grid_View.TabIndex = 3
        '
        'KeyNum
        '
        Me.KeyNum.HeaderText = "KEY"
        Me.KeyNum.MinimumWidth = 8
        Me.KeyNum.Name = "KeyNum"
        Me.KeyNum.ReadOnly = True
        Me.KeyNum.Width = 65
        '
        'MoveToFolder
        '
        Me.MoveToFolder.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill
        Me.MoveToFolder.HeaderText = "move to folder"
        Me.MoveToFolder.MinimumWidth = 8
        Me.MoveToFolder.Name = "MoveToFolder"
        '
        'SetOnTop
        '
        Me.SetOnTop.Font = New System.Drawing.Font("Microsoft Sans Serif", 9.0!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(204, Byte))
        Me.SetOnTop.ForeColor = System.Drawing.SystemColors.ActiveCaption
        Me.SetOnTop.Location = New System.Drawing.Point(509, 4)
        Me.SetOnTop.Margin = New System.Windows.Forms.Padding(5, 0, 5, 0)
        Me.SetOnTop.Name = "SetOnTop"
        Me.SetOnTop.Size = New System.Drawing.Size(376, 32)
        Me.SetOnTop.TabIndex = 4
        Me.SetOnTop.Text = "set this table ontop"
        '
        'chkbox_Copy_Mode
        '
        Me.chkbox_Copy_Mode.AutoSize = True
        Me.chkbox_Copy_Mode.Location = New System.Drawing.Point(24, 521)
        Me.chkbox_Copy_Mode.Margin = New System.Windows.Forms.Padding(5, 6, 5, 6)
        Me.chkbox_Copy_Mode.Name = "chkbox_Copy_Mode"
        Me.chkbox_Copy_Mode.Size = New System.Drawing.Size(153, 29)
        Me.chkbox_Copy_Mode.TabIndex = 5
        Me.chkbox_Copy_Mode.Text = "Copy mode"
        Me.chkbox_Copy_Mode.UseVisualStyleBackColor = True
        '
        'chkbox_Independent_Thread_For_File_Operation
        '
        Me.chkbox_Independent_Thread_For_File_Operation.AutoSize = True
        Me.chkbox_Independent_Thread_For_File_Operation.Location = New System.Drawing.Point(24, 562)
        Me.chkbox_Independent_Thread_For_File_Operation.Margin = New System.Windows.Forms.Padding(4)
        Me.chkbox_Independent_Thread_For_File_Operation.Name = "chkbox_Independent_Thread_For_File_Operation"
        Me.chkbox_Independent_Thread_For_File_Operation.Size = New System.Drawing.Size(501, 29)
        Me.chkbox_Independent_Thread_For_File_Operation.TabIndex = 6
        Me.chkbox_Independent_Thread_For_File_Operation.Text = "Use independent thread for operations with files"
        Me.chkbox_Independent_Thread_For_File_Operation.UseVisualStyleBackColor = True
        '
        'the_Table_Form
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(12.0!, 25.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(885, 614)
        Me.Controls.Add(Me.chkbox_Independent_Thread_For_File_Operation)
        Me.Controls.Add(Me.chkbox_Copy_Mode)
        Me.Controls.Add(Me.SetOnTop)
        Me.Controls.Add(Me.Data_Grid_View)
        Me.KeyPreview = True
        Me.Margin = New System.Windows.Forms.Padding(5, 6, 5, 6)
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.Name = "the_Table_Form"
        Me.ShowIcon = False
        Me.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide
        Me.Text = "MoveTo table"
        CType(Me.Data_Grid_View, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub
    Friend WithEvents Data_Grid_View As System.Windows.Forms.DataGridView
    Friend WithEvents SetOnTop As System.Windows.Forms.Label
    Friend WithEvents KeyNum As System.Windows.Forms.DataGridViewTextBoxColumn
    Friend WithEvents MoveToFolder As System.Windows.Forms.DataGridViewTextBoxColumn
    Friend WithEvents chkbox_Copy_Mode As System.Windows.Forms.CheckBox
    Friend WithEvents chkbox_Independent_Thread_For_File_Operation As CheckBox
End Class
