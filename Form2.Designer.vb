<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class Form2
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
        Me.DataGridView1 = New System.Windows.Forms.DataGridView()
        Me.KeyNum = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.MoveToFolder = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.SetOnTop = New System.Windows.Forms.Label()
        Me.CheckBox1 = New System.Windows.Forms.CheckBox()
        Me.UseIndependentThreadForOperationsWithFiles = New System.Windows.Forms.CheckBox()
        CType(Me.DataGridView1, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'DataGridView1
        '
        Me.DataGridView1.AllowUserToAddRows = False
        Me.DataGridView1.AllowUserToDeleteRows = False
        Me.DataGridView1.AllowUserToOrderColumns = True
        Me.DataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize
        Me.DataGridView1.Columns.AddRange(New System.Windows.Forms.DataGridViewColumn() {Me.KeyNum, Me.MoveToFolder})
        Me.DataGridView1.Location = New System.Drawing.Point(0, 2)
        Me.DataGridView1.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
        Me.DataGridView1.MultiSelect = False
        Me.DataGridView1.Name = "DataGridView1"
        Me.DataGridView1.RowHeadersWidth = 62
        Me.DataGridView1.Size = New System.Drawing.Size(664, 406)
        Me.DataGridView1.TabIndex = 3
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
        'CheckBox1
        '
        Me.CheckBox1.AutoSize = True
        Me.CheckBox1.Location = New System.Drawing.Point(18, 417)
        Me.CheckBox1.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
        Me.CheckBox1.Name = "CheckBox1"
        Me.CheckBox1.Size = New System.Drawing.Size(115, 24)
        Me.CheckBox1.TabIndex = 5
        Me.CheckBox1.Text = "Copy mode"
        Me.CheckBox1.UseVisualStyleBackColor = True
        '
        'UseIndependentThreadForOperationsWithFiles
        '
        Me.UseIndependentThreadForOperationsWithFiles.AutoSize = True
        Me.UseIndependentThreadForOperationsWithFiles.Location = New System.Drawing.Point(18, 450)
        Me.UseIndependentThreadForOperationsWithFiles.Name = "UseIndependentThreadForOperationsWithFiles"
        Me.UseIndependentThreadForOperationsWithFiles.Size = New System.Drawing.Size(373, 24)
        Me.UseIndependentThreadForOperationsWithFiles.TabIndex = 6
        Me.UseIndependentThreadForOperationsWithFiles.Text = "Use independent thread for operations with files"
        Me.UseIndependentThreadForOperationsWithFiles.UseVisualStyleBackColor = True
        '
        'Form2
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(9.0!, 20.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(664, 491)
        Me.Controls.Add(Me.UseIndependentThreadForOperationsWithFiles)
        Me.Controls.Add(Me.CheckBox1)
        Me.Controls.Add(Me.SetOnTop)
        Me.Controls.Add(Me.DataGridView1)
        Me.KeyPreview = True
        Me.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.Name = "Form2"
        Me.ShowIcon = False
        Me.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide
        Me.Text = "MoveTo table"
        CType(Me.DataGridView1, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub
    Friend WithEvents DataGridView1 As System.Windows.Forms.DataGridView
    Friend WithEvents SetOnTop As System.Windows.Forms.Label
    Friend WithEvents KeyNum As System.Windows.Forms.DataGridViewTextBoxColumn
    Friend WithEvents MoveToFolder As System.Windows.Forms.DataGridViewTextBoxColumn
    Friend WithEvents CheckBox1 As System.Windows.Forms.CheckBox
    Friend WithEvents UseIndependentThreadForOperationsWithFiles As CheckBox
End Class
