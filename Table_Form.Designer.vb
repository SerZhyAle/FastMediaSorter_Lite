<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class Table_Form
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
        Me.chb_perspectiva = New System.Windows.Forms.CheckBox()
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
        Me.Data_Grid_View.Location = New System.Drawing.Point(0, 1)
        Me.Data_Grid_View.MultiSelect = False
        Me.Data_Grid_View.Name = "Data_Grid_View"
        Me.Data_Grid_View.RowHeadersWidth = 62
        Me.Data_Grid_View.Size = New System.Drawing.Size(443, 264)
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
        Me.SetOnTop.Location = New System.Drawing.Point(255, 2)
        Me.SetOnTop.Name = "SetOnTop"
        Me.SetOnTop.Size = New System.Drawing.Size(188, 17)
        Me.SetOnTop.TabIndex = 4
        Me.SetOnTop.Text = "set this table ontop"
        '
        'chkbox_Copy_Mode
        '
        Me.chkbox_Copy_Mode.AutoSize = True
        Me.chkbox_Copy_Mode.Location = New System.Drawing.Point(12, 271)
        Me.chkbox_Copy_Mode.Name = "chkbox_Copy_Mode"
        Me.chkbox_Copy_Mode.Size = New System.Drawing.Size(79, 17)
        Me.chkbox_Copy_Mode.TabIndex = 5
        Me.chkbox_Copy_Mode.Text = "Copy mode"
        Me.chkbox_Copy_Mode.UseVisualStyleBackColor = True
        '
        'chkbox_Independent_Thread_For_File_Operation
        '
        Me.chkbox_Independent_Thread_For_File_Operation.AutoSize = True
        Me.chkbox_Independent_Thread_For_File_Operation.Location = New System.Drawing.Point(12, 292)
        Me.chkbox_Independent_Thread_For_File_Operation.Margin = New System.Windows.Forms.Padding(2)
        Me.chkbox_Independent_Thread_For_File_Operation.Name = "chkbox_Independent_Thread_For_File_Operation"
        Me.chkbox_Independent_Thread_For_File_Operation.Size = New System.Drawing.Size(250, 17)
        Me.chkbox_Independent_Thread_For_File_Operation.TabIndex = 6
        Me.chkbox_Independent_Thread_For_File_Operation.Text = "Use independent thread for operations with files"
        Me.chkbox_Independent_Thread_For_File_Operation.UseVisualStyleBackColor = True
        '
        'cmbox_color_schema
        '
        Me.cmbox_color_schema.FormattingEnabled = True
        Me.cmbox_color_schema.Location = New System.Drawing.Point(354, 270)
        Me.cmbox_color_schema.Margin = New System.Windows.Forms.Padding(2)
        Me.cmbox_color_schema.Name = "cmbox_color_schema"
        Me.cmbox_color_schema.Size = New System.Drawing.Size(82, 21)
        Me.cmbox_color_schema.TabIndex = 7
        '
        'lbl_Color
        '
        Me.lbl_Color.AutoSize = True
        Me.lbl_Color.Location = New System.Drawing.Point(312, 272)
        Me.lbl_Color.Margin = New System.Windows.Forms.Padding(2, 0, 2, 0)
        Me.lbl_Color.Name = "lbl_Color"
        Me.lbl_Color.Size = New System.Drawing.Size(31, 13)
        Me.lbl_Color.TabIndex = 8
        Me.lbl_Color.Text = "Color"
        '
        'chb_perspectiva
        '
        Me.chb_perspectiva.AutoSize = True
        Me.chb_perspectiva.CheckAlign = System.Drawing.ContentAlignment.MiddleRight
        Me.chb_perspectiva.Location = New System.Drawing.Point(328, 292)
        Me.chb_perspectiva.Name = "chb_perspectiva"
        Me.chb_perspectiva.Size = New System.Drawing.Size(108, 17)
        Me.chb_perspectiva.TabIndex = 9
        Me.chb_perspectiva.Text = "Show pespective"
        Me.chb_perspectiva.TextAlign = System.Drawing.ContentAlignment.MiddleRight
        Me.chb_perspectiva.UseVisualStyleBackColor = True
        '
        'the_Table_Form
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(443, 332)
        Me.Controls.Add(Me.chb_perspectiva)
        Me.Controls.Add(Me.lbl_Color)
        Me.Controls.Add(Me.cmbox_color_schema)
        Me.Controls.Add(Me.chkbox_Independent_Thread_For_File_Operation)
        Me.Controls.Add(Me.chkbox_Copy_Mode)
        Me.Controls.Add(Me.SetOnTop)
        Me.Controls.Add(Me.Data_Grid_View)
        Me.KeyPreview = True
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
    Friend WithEvents chb_perspectiva As CheckBox
End Class
