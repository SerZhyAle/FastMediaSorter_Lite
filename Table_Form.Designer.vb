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
        Me.Tab_Control = New System.Windows.Forms.TabControl()
        Me.Tab_Page_1 = New System.Windows.Forms.TabPage()
        Me.Tab_Page_2 = New System.Windows.Forms.TabPage()
        Me.chkb_show_file_size = New System.Windows.Forms.CheckBox()
        Me.chkb_is_to_show_file_datetime = New System.Windows.Forms.CheckBox()
        Me.chkb_show_pic_size = New System.Windows.Forms.CheckBox()
        CType(Me.Data_Grid_View, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.Tab_Control.SuspendLayout()
        Me.Tab_Page_1.SuspendLayout()
        Me.Tab_Page_2.SuspendLayout()
        Me.SuspendLayout()
        '
        'Data_Grid_View
        '
        Me.Data_Grid_View.AllowUserToAddRows = False
        Me.Data_Grid_View.AllowUserToDeleteRows = False
        Me.Data_Grid_View.AllowUserToOrderColumns = True
        Me.Data_Grid_View.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize
        Me.Data_Grid_View.Columns.AddRange(New System.Windows.Forms.DataGridViewColumn() {Me.KeyNum, Me.MoveToFolder})
        Me.Data_Grid_View.Location = New System.Drawing.Point(0, 0)
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
        Me.chkbox_Copy_Mode.Location = New System.Drawing.Point(4, 17)
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
        Me.chkbox_Independent_Thread_For_File_Operation.Location = New System.Drawing.Point(4, 49)
        Me.chkbox_Independent_Thread_For_File_Operation.Name = "chkbox_Independent_Thread_For_File_Operation"
        Me.chkbox_Independent_Thread_For_File_Operation.Size = New System.Drawing.Size(366, 24)
        Me.chkbox_Independent_Thread_For_File_Operation.TabIndex = 6
        Me.chkbox_Independent_Thread_For_File_Operation.Text = "Use independent thread for operations with files"
        Me.chkbox_Independent_Thread_For_File_Operation.UseVisualStyleBackColor = True
        '
        'cmbox_color_schema
        '
        Me.cmbox_color_schema.FormattingEnabled = True
        Me.cmbox_color_schema.Location = New System.Drawing.Point(140, 76)
        Me.cmbox_color_schema.Name = "cmbox_color_schema"
        Me.cmbox_color_schema.Size = New System.Drawing.Size(145, 28)
        Me.cmbox_color_schema.TabIndex = 7
        '
        'lbl_Color
        '
        Me.lbl_Color.AutoSize = True
        Me.lbl_Color.Location = New System.Drawing.Point(0, 79)
        Me.lbl_Color.Name = "lbl_Color"
        Me.lbl_Color.Size = New System.Drawing.Size(46, 20)
        Me.lbl_Color.TabIndex = 8
        Me.lbl_Color.Text = "Color"
        '
        'chb_perspectiva
        '
        Me.chb_perspectiva.AutoSize = True
        Me.chb_perspectiva.Location = New System.Drawing.Point(4, 112)
        Me.chb_perspectiva.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
        Me.chb_perspectiva.Name = "chb_perspectiva"
        Me.chb_perspectiva.Size = New System.Drawing.Size(148, 24)
        Me.chb_perspectiva.TabIndex = 9
        Me.chb_perspectiva.Text = "Show pespective"
        Me.chb_perspectiva.TextAlign = System.Drawing.ContentAlignment.MiddleRight
        Me.chb_perspectiva.UseVisualStyleBackColor = True
        '
        'Tab_Control
        '
        Me.Tab_Control.Controls.Add(Me.Tab_Page_1)
        Me.Tab_Control.Controls.Add(Me.Tab_Page_2)
        Me.Tab_Control.Location = New System.Drawing.Point(0, 3)
        Me.Tab_Control.Name = "Tab_Control"
        Me.Tab_Control.SelectedIndex = 0
        Me.Tab_Control.Size = New System.Drawing.Size(664, 435)
        Me.Tab_Control.TabIndex = 10
        '
        'Tab_Page_1
        '
        Me.Tab_Page_1.Controls.Add(Me.Data_Grid_View)
        Me.Tab_Page_1.Location = New System.Drawing.Point(4, 29)
        Me.Tab_Page_1.Name = "Tab_Page_1"
        Me.Tab_Page_1.Padding = New System.Windows.Forms.Padding(3)
        Me.Tab_Page_1.Size = New System.Drawing.Size(656, 402)
        Me.Tab_Page_1.TabIndex = 0
        Me.Tab_Page_1.Text = "Directional keys"
        Me.Tab_Page_1.UseVisualStyleBackColor = True
        '
        'Tab_Page_2
        '
        Me.Tab_Page_2.Controls.Add(Me.chkb_show_file_size)
        Me.Tab_Page_2.Controls.Add(Me.chkb_is_to_show_file_datetime)
        Me.Tab_Page_2.Controls.Add(Me.chkb_show_pic_size)
        Me.Tab_Page_2.Controls.Add(Me.cmbox_color_schema)
        Me.Tab_Page_2.Controls.Add(Me.chkbox_Copy_Mode)
        Me.Tab_Page_2.Controls.Add(Me.chb_perspectiva)
        Me.Tab_Page_2.Controls.Add(Me.chkbox_Independent_Thread_For_File_Operation)
        Me.Tab_Page_2.Controls.Add(Me.lbl_Color)
        Me.Tab_Page_2.Location = New System.Drawing.Point(4, 29)
        Me.Tab_Page_2.Name = "Tab_Page_2"
        Me.Tab_Page_2.Padding = New System.Windows.Forms.Padding(3)
        Me.Tab_Page_2.Size = New System.Drawing.Size(656, 402)
        Me.Tab_Page_2.TabIndex = 1
        Me.Tab_Page_2.Text = "Options"
        Me.Tab_Page_2.UseVisualStyleBackColor = True
        '
        'chkb_show_file_size
        '
        Me.chkb_show_file_size.AutoSize = True
        Me.chkb_show_file_size.Location = New System.Drawing.Point(4, 207)
        Me.chkb_show_file_size.Name = "chkb_show_file_size"
        Me.chkb_show_file_size.Size = New System.Drawing.Size(124, 24)
        Me.chkb_show_file_size.TabIndex = 12
        Me.chkb_show_file_size.Text = "Show file size"
        Me.chkb_show_file_size.UseVisualStyleBackColor = True
        '
        'chkb_is_to_show_file_datetime
        '
        Me.chkb_is_to_show_file_datetime.AutoSize = True
        Me.chkb_is_to_show_file_datetime.Location = New System.Drawing.Point(4, 176)
        Me.chkb_is_to_show_file_datetime.Name = "chkb_is_to_show_file_datetime"
        Me.chkb_is_to_show_file_datetime.Size = New System.Drawing.Size(134, 24)
        Me.chkb_is_to_show_file_datetime.TabIndex = 11
        Me.chkb_is_to_show_file_datetime.Text = "Show datetime"
        Me.chkb_is_to_show_file_datetime.UseVisualStyleBackColor = True
        '
        'chkb_show_pic_size
        '
        Me.chkb_show_pic_size.AutoSize = True
        Me.chkb_show_pic_size.Location = New System.Drawing.Point(4, 145)
        Me.chkb_show_pic_size.Name = "chkb_show_pic_size"
        Me.chkb_show_pic_size.Size = New System.Drawing.Size(152, 24)
        Me.chkb_show_pic_size.TabIndex = 10
        Me.chkb_show_pic_size.Text = "Show picture size"
        Me.chkb_show_pic_size.UseVisualStyleBackColor = True
        '
        'Table_Form
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(9.0!, 20.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(664, 441)
        Me.Controls.Add(Me.SetOnTop)
        Me.Controls.Add(Me.Tab_Control)
        Me.KeyPreview = True
        Me.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.Name = "Table_Form"
        Me.ShowIcon = False
        Me.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide
        Me.Text = "MoveTo table"
        CType(Me.Data_Grid_View, System.ComponentModel.ISupportInitialize).EndInit()
        Me.Tab_Control.ResumeLayout(False)
        Me.Tab_Page_1.ResumeLayout(False)
        Me.Tab_Page_2.ResumeLayout(False)
        Me.Tab_Page_2.PerformLayout()
        Me.ResumeLayout(False)

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
    Friend WithEvents Tab_Control As TabControl
    Friend WithEvents Tab_Page_1 As TabPage
    Friend WithEvents Tab_Page_2 As TabPage
    Friend WithEvents chkb_show_pic_size As CheckBox
    Friend WithEvents chkb_is_to_show_file_datetime As CheckBox
    Friend WithEvents chkb_show_file_size As CheckBox
End Class
