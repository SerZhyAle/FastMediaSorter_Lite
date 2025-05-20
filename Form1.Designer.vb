<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class Form1
    Inherits System.Windows.Forms.Form

    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private components As System.ComponentModel.IContainer

    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(Form1))
        Me.Label1 = New System.Windows.Forms.Label()
        Me.Button1 = New System.Windows.Forms.Button()
        Me.Label2 = New System.Windows.Forms.Label()
        Me.Button2 = New System.Windows.Forms.Button()
        Me.Button3 = New System.Windows.Forms.Button()
        Me.Button4 = New System.Windows.Forms.Button()
        Me.Button5 = New System.Windows.Forms.Button()
        Me.Label3 = New System.Windows.Forms.Label()
        Me.StatusL = New System.Windows.Forms.Label()
        Me.FirstRun = New System.Windows.Forms.Label()
        Me.butI = New System.Windows.Forms.Button()
        Me.Button6 = New System.Windows.Forms.Button()
        Me.Button7 = New System.Windows.Forms.Button()
        Me.WebBrowser1 = New System.Windows.Forms.WebBrowser()
        Me.Button8 = New System.Windows.Forms.Button()
        Me.Label_SLide = New System.Windows.Forms.Label()
        Me.Button9 = New System.Windows.Forms.Button()
        Me.chkTopMost = New System.Windows.Forms.CheckBox()
        Me.ButtonLNG = New System.Windows.Forms.Button()
        Me.ButtonRename = New System.Windows.Forms.Button()
        Me.TextBox1 = New System.Windows.Forms.ComboBox()
        Me.SortComboBox = New System.Windows.Forms.ComboBox()
        Me.PictureBox2 = New System.Windows.Forms.PictureBox()
        Me.PictureBox1 = New System.Windows.Forms.PictureBox()
        CType(Me.PictureBox2, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PictureBox1, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'Label1
        '
        Me.Label1.AutoSize = True
        Me.Label1.Location = New System.Drawing.Point(95, 6)
        Me.Label1.Margin = New System.Windows.Forms.Padding(5, 0, 5, 0)
        Me.Label1.Name = "Label1"
        Me.Label1.Size = New System.Drawing.Size(79, 25)
        Me.Label1.TabIndex = 1
        Me.Label1.Text = "Folder:"
        '
        'Button1
        '
        Me.Button1.Location = New System.Drawing.Point(968, -2)
        Me.Button1.Margin = New System.Windows.Forms.Padding(5, 6, 5, 6)
        Me.Button1.Name = "Button1"
        Me.Button1.Size = New System.Drawing.Size(56, 44)
        Me.Button1.TabIndex = 1
        Me.Button1.Text = "..."
        Me.Button1.UseVisualStyleBackColor = True
        '
        'Label2
        '
        Me.Label2.Location = New System.Drawing.Point(3, 56)
        Me.Label2.Margin = New System.Windows.Forms.Padding(5, 0, 5, 0)
        Me.Label2.Name = "Label2"
        Me.Label2.Size = New System.Drawing.Size(272, 35)
        Me.Label2.TabIndex = 4
        Me.Label2.Text = "Files: 0"
        '
        'Button2
        '
        Me.Button2.Location = New System.Drawing.Point(285, 46)
        Me.Button2.Margin = New System.Windows.Forms.Padding(5, 6, 5, 6)
        Me.Button2.Name = "Button2"
        Me.Button2.Size = New System.Drawing.Size(181, 44)
        Me.Button2.TabIndex = 2
        Me.Button2.Text = "<< (P)rev"
        Me.Button2.UseVisualStyleBackColor = True
        '
        'Button3
        '
        Me.Button3.Location = New System.Drawing.Point(469, 46)
        Me.Button3.Margin = New System.Windows.Forms.Padding(5, 6, 5, 6)
        Me.Button3.Name = "Button3"
        Me.Button3.Size = New System.Drawing.Size(197, 44)
        Me.Button3.TabIndex = 3
        Me.Button3.Text = "(N)ext >>"
        Me.Button3.UseVisualStyleBackColor = True
        '
        'Button4
        '
        Me.Button4.Location = New System.Drawing.Point(1269, 48)
        Me.Button4.Margin = New System.Windows.Forms.Padding(5, 6, 5, 6)
        Me.Button4.Name = "Button4"
        Me.Button4.Size = New System.Drawing.Size(124, 44)
        Me.Button4.TabIndex = 5
        Me.Button4.Text = "(D)elete"
        Me.Button4.UseVisualStyleBackColor = True
        '
        'Button5
        '
        Me.Button5.Location = New System.Drawing.Point(869, 46)
        Me.Button5.Margin = New System.Windows.Forms.Padding(5, 6, 5, 6)
        Me.Button5.Name = "Button5"
        Me.Button5.Size = New System.Drawing.Size(320, 44)
        Me.Button5.TabIndex = 4
        Me.Button5.Text = "MoveTo table"
        Me.Button5.UseVisualStyleBackColor = True
        '
        'Label3
        '
        Me.Label3.Location = New System.Drawing.Point(8, 96)
        Me.Label3.Margin = New System.Windows.Forms.Padding(5, 0, 5, 0)
        Me.Label3.Name = "Label3"
        Me.Label3.Size = New System.Drawing.Size(1381, 36)
        Me.Label3.TabIndex = 0
        Me.Label3.Text = "current file"
        '
        'StatusL
        '
        Me.StatusL.AutoSize = True
        Me.StatusL.BackColor = System.Drawing.Color.Transparent
        Me.StatusL.Location = New System.Drawing.Point(8, 132)
        Me.StatusL.Margin = New System.Windows.Forms.Padding(5, 0, 5, 0)
        Me.StatusL.Name = "StatusL"
        Me.StatusL.Size = New System.Drawing.Size(70, 25)
        Me.StatusL.TabIndex = 100
        Me.StatusL.Text = "status"
        '
        'FirstRun
        '
        Me.FirstRun.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.FirstRun.BackColor = System.Drawing.Color.White
        Me.FirstRun.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D
        Me.FirstRun.FlatStyle = System.Windows.Forms.FlatStyle.Popup
        Me.FirstRun.Font = New System.Drawing.Font("Microsoft Sans Serif", 12.0!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(204, Byte))
        Me.FirstRun.ForeColor = System.Drawing.Color.Black
        Me.FirstRun.Location = New System.Drawing.Point(8, 90)
        Me.FirstRun.Margin = New System.Windows.Forms.Padding(5, 0, 5, 0)
        Me.FirstRun.Name = "FirstRun"
        Me.FirstRun.Size = New System.Drawing.Size(1381, 664)
        Me.FirstRun.TabIndex = 0
        Me.FirstRun.Text = "First run"
        Me.FirstRun.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.FirstRun.Visible = False
        '
        'butI
        '
        Me.butI.Location = New System.Drawing.Point(1036, -2)
        Me.butI.Margin = New System.Windows.Forms.Padding(5, 6, 5, 6)
        Me.butI.Name = "butI"
        Me.butI.Size = New System.Drawing.Size(75, 44)
        Me.butI.TabIndex = 106
        Me.butI.Text = "RE"
        Me.butI.UseVisualStyleBackColor = True
        '
        'Button6
        '
        Me.Button6.Location = New System.Drawing.Point(805, 50)
        Me.Button6.Margin = New System.Windows.Forms.Padding(5, 6, 5, 6)
        Me.Button6.Name = "Button6"
        Me.Button6.Size = New System.Drawing.Size(64, 36)
        Me.Button6.TabIndex = 107
        Me.Button6.Text = ">>"
        Me.Button6.UseVisualStyleBackColor = True
        '
        'Button7
        '
        Me.Button7.Location = New System.Drawing.Point(1115, -2)
        Me.Button7.Margin = New System.Windows.Forms.Padding(5, 6, 5, 6)
        Me.Button7.Name = "Button7"
        Me.Button7.Size = New System.Drawing.Size(75, 44)
        Me.Button7.TabIndex = 108
        Me.Button7.Text = "^^"
        Me.Button7.UseVisualStyleBackColor = True
        '
        'WebBrowser1
        '
        Me.WebBrowser1.AllowWebBrowserDrop = False
        Me.WebBrowser1.Location = New System.Drawing.Point(0, 162)
        Me.WebBrowser1.Margin = New System.Windows.Forms.Padding(4)
        Me.WebBrowser1.Name = "WebBrowser1"
        Me.WebBrowser1.Size = New System.Drawing.Size(1387, 662)
        Me.WebBrowser1.TabIndex = 109
        Me.WebBrowser1.TabStop = False
        '
        'Button8
        '
        Me.Button8.Font = New System.Drawing.Font("Microsoft Sans Serif", 9.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(204, Byte))
        Me.Button8.Location = New System.Drawing.Point(669, 44)
        Me.Button8.Margin = New System.Windows.Forms.Padding(5, 6, 5, 6)
        Me.Button8.Name = "Button8"
        Me.Button8.Size = New System.Drawing.Size(72, 44)
        Me.Button8.TabIndex = 111
        Me.Button8.Text = "(Y)Rnd>"
        Me.Button8.UseVisualStyleBackColor = True
        '
        'Label_SLide
        '
        Me.Label_SLide.AutoSize = True
        Me.Label_SLide.Location = New System.Drawing.Point(1196, 2)
        Me.Label_SLide.Margin = New System.Windows.Forms.Padding(4, 0, 4, 0)
        Me.Label_SLide.Name = "Label_SLide"
        Me.Label_SLide.Size = New System.Drawing.Size(35, 25)
        Me.Label_SLide.TabIndex = 112
        Me.Label_SLide.Text = "1s"
        '
        'Button9
        '
        Me.Button9.Location = New System.Drawing.Point(740, 50)
        Me.Button9.Margin = New System.Windows.Forms.Padding(5, 6, 5, 6)
        Me.Button9.Name = "Button9"
        Me.Button9.Size = New System.Drawing.Size(64, 36)
        Me.Button9.TabIndex = 113
        Me.Button9.Text = "R>"
        Me.Button9.UseVisualStyleBackColor = True
        '
        'chkTopMost
        '
        Me.chkTopMost.AutoSize = True
        Me.chkTopMost.Location = New System.Drawing.Point(1297, 2)
        Me.chkTopMost.Margin = New System.Windows.Forms.Padding(4)
        Me.chkTopMost.Name = "chkTopMost"
        Me.chkTopMost.Size = New System.Drawing.Size(28, 27)
        Me.chkTopMost.TabIndex = 115
        Me.chkTopMost.UseVisualStyleBackColor = True
        '
        'ButtonLNG
        '
        Me.ButtonLNG.Font = New System.Drawing.Font("Microsoft Sans Serif", 7.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(204, Byte))
        Me.ButtonLNG.Location = New System.Drawing.Point(1335, -2)
        Me.ButtonLNG.Margin = New System.Windows.Forms.Padding(4)
        Me.ButtonLNG.Name = "ButtonLNG"
        Me.ButtonLNG.Size = New System.Drawing.Size(59, 36)
        Me.ButtonLNG.TabIndex = 116
        Me.ButtonLNG.Text = "RU"
        Me.ButtonLNG.UseVisualStyleBackColor = True
        '
        'ButtonRename
        '
        Me.ButtonRename.Location = New System.Drawing.Point(1199, 46)
        Me.ButtonRename.Margin = New System.Windows.Forms.Padding(4)
        Me.ButtonRename.Name = "ButtonRename"
        Me.ButtonRename.Size = New System.Drawing.Size(61, 44)
        Me.ButtonRename.TabIndex = 117
        Me.ButtonRename.Text = "RN"
        Me.ButtonRename.UseVisualStyleBackColor = True
        '
        'TextBox1
        '
        Me.TextBox1.FormattingEnabled = True
        Me.TextBox1.Location = New System.Drawing.Point(168, 1)
        Me.TextBox1.Margin = New System.Windows.Forms.Padding(4)
        Me.TextBox1.Name = "TextBox1"
        Me.TextBox1.Size = New System.Drawing.Size(789, 33)
        Me.TextBox1.TabIndex = 118
        '
        'SortComboBox
        '
        Me.SortComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.SortComboBox.FormattingEnabled = True
        Me.SortComboBox.Items.AddRange(New Object() {"abc", "xyz", "rnd", ">size", "<size"})
        Me.SortComboBox.Location = New System.Drawing.Point(8, -1)
        Me.SortComboBox.Margin = New System.Windows.Forms.Padding(4)
        Me.SortComboBox.Name = "SortComboBox"
        Me.SortComboBox.Size = New System.Drawing.Size(76, 33)
        Me.SortComboBox.TabIndex = 119
        '
        'PictureBox2
        '
        Me.PictureBox2.Location = New System.Drawing.Point(0, 162)
        Me.PictureBox2.Margin = New System.Windows.Forms.Padding(4)
        Me.PictureBox2.Name = "PictureBox2"
        Me.PictureBox2.Size = New System.Drawing.Size(1387, 662)
        Me.PictureBox2.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom
        Me.PictureBox2.TabIndex = 120
        Me.PictureBox2.TabStop = False
        '
        'PictureBox1
        '
        Me.PictureBox1.Location = New System.Drawing.Point(0, 162)
        Me.PictureBox1.Margin = New System.Windows.Forms.Padding(4)
        Me.PictureBox1.Name = "PictureBox1"
        Me.PictureBox1.Size = New System.Drawing.Size(1387, 662)
        Me.PictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom
        Me.PictureBox1.TabIndex = 0
        Me.PictureBox1.TabStop = False
        '
        'Form1
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(12.0!, 25.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(1396, 832)
        Me.Controls.Add(Me.TextBox1)
        Me.Controls.Add(Me.SortComboBox)
        Me.Controls.Add(Me.ButtonRename)
        Me.Controls.Add(Me.Button5)
        Me.Controls.Add(Me.Button6)
        Me.Controls.Add(Me.Button9)
        Me.Controls.Add(Me.Button8)
        Me.Controls.Add(Me.Button3)
        Me.Controls.Add(Me.Button2)
        Me.Controls.Add(Me.butI)
        Me.Controls.Add(Me.Button1)
        Me.Controls.Add(Me.Button7)
        Me.Controls.Add(Me.chkTopMost)
        Me.Controls.Add(Me.ButtonLNG)
        Me.Controls.Add(Me.Button4)
        Me.Controls.Add(Me.Label_SLide)
        Me.Controls.Add(Me.StatusL)
        Me.Controls.Add(Me.Label3)
        Me.Controls.Add(Me.Label2)
        Me.Controls.Add(Me.Label1)
        Me.Controls.Add(Me.FirstRun)
        Me.Controls.Add(Me.WebBrowser1)
        Me.Controls.Add(Me.PictureBox2)
        Me.Controls.Add(Me.PictureBox1)
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.KeyPreview = True
        Me.Margin = New System.Windows.Forms.Padding(3, 2, 3, 2)
        Me.Name = "Form1"
        Me.Text = "Fast Media Sorter by SZA"
        CType(Me.PictureBox2, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PictureBox1, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub

    Friend WithEvents FirstRun As System.Windows.Forms.Label
    Friend WithEvents Label1 As System.Windows.Forms.Label
    Friend WithEvents Button1 As System.Windows.Forms.Button
    Friend WithEvents Label2 As System.Windows.Forms.Label
    Friend WithEvents Button2 As System.Windows.Forms.Button
    Friend WithEvents Button3 As System.Windows.Forms.Button
    Friend WithEvents Button4 As System.Windows.Forms.Button
    Friend WithEvents Button5 As System.Windows.Forms.Button
    Friend WithEvents Label3 As System.Windows.Forms.Label
    Friend WithEvents StatusL As System.Windows.Forms.Label
    Friend WithEvents butI As System.Windows.Forms.Button
    Friend WithEvents Button6 As Button
    Friend WithEvents Button7 As Button
    Friend WithEvents WebBrowser1 As WebBrowser
    Friend WithEvents Button8 As Button
    Friend WithEvents Label_SLide As Label
    Friend WithEvents Button9 As Button
    Friend WithEvents chkTopMost As CheckBox
    Friend WithEvents ButtonLNG As Button
    Friend WithEvents ButtonRename As Button
    Friend WithEvents TextBox1 As ComboBox
    Friend WithEvents SortComboBox As ComboBox
    Friend WithEvents PictureBox2 As PictureBox
    Friend WithEvents PictureBox1 As PictureBox
End Class