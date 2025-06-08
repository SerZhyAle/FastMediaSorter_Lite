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
        Me.cmbox_color_schema = New System.Windows.Forms.ComboBox()
        Me.lbl_Color = New System.Windows.Forms.Label()
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
        Me.Data_Grid_View.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
        Me.Data_Grid_View.MultiSelect = False
        Me.Data_Grid_View.Name = "Data_Grid_View"
        Me.Data_Grid_View.RowHeadersWidth = 62
        Me.Data_Grid_View.Size = New System.Drawing.Size(664, 406)
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
        Me.SetOnTop.Location = New System.Drawing.Point(382, 3)
        Me.SetOnTop.Margin = New System.Windows.Forms.Padding(4, 0, 4, 0)
        Me.SetOnTop.Name = "SetOnTop"
        Me.SetOnTop.Size = New System.Drawing.Size(282, 26)
        Me.SetOnTop.TabIndex = 4
        Me.SetOnTop.Text = "set this table ontop"
        '
        'chkbox_Copy_Mode
        '
        Me.chkbox_Copy_Mode.AutoSize = True
        Me.chkbox_Copy_Mode.Location = New System.Drawing.Point(18, 417)
        Me.chkbox_Copy_Mode.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
        Me.chkbox_Copy_Mode.Name = "chkbox_Copy_Mode"
        Me.chkbox_Copy_Mode.Size = New System.Drawing.Size(108, 24)
        Me.chkbox_Copy_Mode.TabIndex = 5
        Me.chkbox_Copy_Mode.Text = "Copy mode"
        Me.chkbox_Copy_Mode.UseVisualStyleBackColor = True
        '
        'chkbox_Independent_Thread_For_File_Operation
        '
        Me.chkbox_Independent_Thread_For_File_Operation.AutoSize = True
        Me.chkbox_Independent_Thread_For_File_Operation.Location = New System.Drawing.Point(18, 450)
        Me.chkbox_Independent_Thread_For_File_Operation.Name = "chkbox_Independent_Thread_For_File_Operation"
        Me.chkbox_Independent_Thread_For_File_Operation.Size = New System.Drawing.Size(366, 24)
        Me.chkbox_Independent_Thread_For_File_Operation.TabIndex = 6
        Me.chkbox_Independent_Thread_For_File_Operation.Text = "Use independent thread for operations with files"
        Me.chkbox_Independent_Thread_For_File_Operation.UseVisualStyleBackColor = True
        '
        'cmbox_color_schema
        '
        Me.cmbox_color_schema.FormattingEnabled = True
        Me.cmbox_color_schema.Location = New System.Drawing.Point(531, 415)
        Me.cmbox_color_schema.Name = "cmbox_color_schema"
        Me.cmbox_color_schema.Size = New System.Drawing.Size(121, 28)
        Me.cmbox_color_schema.TabIndex = 7
        '
        'lbl_Color
        '
        Me.lbl_Color.AutoSize = True
        Me.lbl_Color.Location = New System.Drawing.Point(468, 418)
        Me.lbl_Color.Name = "lbl_Color"
        Me.lbl_Color.Size = New System.Drawing.Size(46, 20)
        Me.lbl_Color.TabIndex = 8
        Me.lbl_Color.Text = "Color"
        '
        'the_Table_Form
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(9.0!, 20.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(664, 511)
        Me.Controls.Add(Me.lbl_Color)
        Me.Controls.Add(Me.cmbox_color_schema)
        Me.Controls.Add(Me.chkbox_Independent_Thread_For_File_Operation)
        Me.Controls.Add(Me.chkbox_Copy_Mode)
        Me.Controls.Add(Me.SetOnTop)
        Me.Controls.Add(Me.Data_Grid_View)
        Me.KeyPreview = True
        Me.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
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
    Friend WithEvents cmbox_color_schema As ComboBox
    Friend WithEvents lbl_Color As Label
End Class
