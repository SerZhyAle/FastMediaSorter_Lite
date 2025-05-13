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
        Me.Label1.Location = New System.Drawing.Point(71, 5)
        Me.Label1.Margin = New System.Windows.Forms.Padding(4, 0, 4, 0)
        Me.Label1.Name = "Label1"
        Me.Label1.Size = New System.Drawing.Size(58, 20)
        Me.Label1.TabIndex = 1
        Me.Label1.Text = "Folder:"
        '
        'Button1
        '
        Me.Button1.Location = New System.Drawing.Point(726, -2)
        Me.Button1.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
        Me.Button1.Name = "Button1"
        Me.Button1.Size = New System.Drawing.Size(42, 35)
        Me.Button1.TabIndex = 1
        Me.Button1.Text = "..."
        Me.Button1.UseVisualStyleBackColor = True
        '
        'Label2
        '
        Me.Label2.Location = New System.Drawing.Point(2, 45)
        Me.Label2.Margin = New System.Windows.Forms.Padding(4, 0, 4, 0)
        Me.Label2.Name = "Label2"
        Me.Label2.Size = New System.Drawing.Size(204, 28)
        Me.Label2.TabIndex = 4
        Me.Label2.Text = "Files: 0"
        '
        'Button2
        '
        Me.Button2.Location = New System.Drawing.Point(214, 37)
        Me.Button2.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
        Me.Button2.Name = "Button2"
        Me.Button2.Size = New System.Drawing.Size(136, 35)
        Me.Button2.TabIndex = 2
        Me.Button2.Text = "<< (P)rev"
        Me.Button2.UseVisualStyleBackColor = True
        '
        'Button3
        '
        Me.Button3.Location = New System.Drawing.Point(352, 37)
        Me.Button3.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
        Me.Button3.Name = "Button3"
        Me.Button3.Size = New System.Drawing.Size(148, 35)
        Me.Button3.TabIndex = 3
        Me.Button3.Text = "(N)ext >>"
        Me.Button3.UseVisualStyleBackColor = True
        '
        'Button4
        '
        Me.Button4.Location = New System.Drawing.Point(952, 38)
        Me.Button4.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
        Me.Button4.Name = "Button4"
        Me.Button4.Size = New System.Drawing.Size(93, 35)
        Me.Button4.TabIndex = 5
        Me.Button4.Text = "(D)elete"
        Me.Button4.UseVisualStyleBackColor = True
        '
        'Button5
        '
        Me.Button5.Location = New System.Drawing.Point(652, 37)
        Me.Button5.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
        Me.Button5.Name = "Button5"
        Me.Button5.Size = New System.Drawing.Size(240, 35)
        Me.Button5.TabIndex = 4
        Me.Button5.Text = "MoveTo table"
        Me.Button5.UseVisualStyleBackColor = True
        '
        'Label3
        '
        Me.Label3.Location = New System.Drawing.Point(6, 77)
        Me.Label3.Margin = New System.Windows.Forms.Padding(4, 0, 4, 0)
        Me.Label3.Name = "Label3"
        Me.Label3.Size = New System.Drawing.Size(1036, 29)
        Me.Label3.TabIndex = 0
        Me.Label3.Text = "current file"
        '
        'StatusL
        '
        Me.StatusL.Location = New System.Drawing.Point(6, 106)
        Me.StatusL.Margin = New System.Windows.Forms.Padding(4, 0, 4, 0)
        Me.StatusL.Name = "StatusL"
        Me.StatusL.Size = New System.Drawing.Size(1036, 26)
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
        Me.FirstRun.Location = New System.Drawing.Point(6, 72)
        Me.FirstRun.Margin = New System.Windows.Forms.Padding(4, 0, 4, 0)
        Me.FirstRun.Name = "FirstRun"
        Me.FirstRun.Size = New System.Drawing.Size(1036, 531)
        Me.FirstRun.TabIndex = 0
        Me.FirstRun.Text = "First run"
        Me.FirstRun.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.FirstRun.Visible = False
        '
        'butI
        '
        Me.butI.Location = New System.Drawing.Point(777, -2)
        Me.butI.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
        Me.butI.Name = "butI"
        Me.butI.Size = New System.Drawing.Size(56, 35)
        Me.butI.TabIndex = 106
        Me.butI.Text = "RE"
        Me.butI.UseVisualStyleBackColor = True
        '
        'Button6
        '
        Me.Button6.Location = New System.Drawing.Point(604, 40)
        Me.Button6.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
        Me.Button6.Name = "Button6"
        Me.Button6.Size = New System.Drawing.Size(48, 29)
        Me.Button6.TabIndex = 107
        Me.Button6.Text = ">>"
        Me.Button6.UseVisualStyleBackColor = True
        '
        'Button7
        '
        Me.Button7.Location = New System.Drawing.Point(836, -2)
        Me.Button7.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
        Me.Button7.Name = "Button7"
        Me.Button7.Size = New System.Drawing.Size(56, 35)
        Me.Button7.TabIndex = 108
        Me.Button7.Text = "^^"
        Me.Button7.UseVisualStyleBackColor = True
        '
        'Button8
        '
        Me.Button8.Font = New System.Drawing.Font("Microsoft Sans Serif", 9.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(204, Byte))
        Me.Button8.Location = New System.Drawing.Point(502, 35)
        Me.Button8.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
        Me.Button8.Name = "Button8"
        Me.Button8.Size = New System.Drawing.Size(54, 35)
        Me.Button8.TabIndex = 111
        Me.Button8.Text = "(Y)Rnd>"
        Me.Button8.UseVisualStyleBackColor = True
        '
        'Label_SLide
        '
        Me.Label_SLide.AutoSize = True
        Me.Label_SLide.Location = New System.Drawing.Point(897, 2)
        Me.Label_SLide.Name = "Label_SLide"
        Me.Label_SLide.Size = New System.Drawing.Size(26, 20)
        Me.Label_SLide.TabIndex = 112
        Me.Label_SLide.Text = "1s"
        '
        'Button9
        '
        Me.Button9.Location = New System.Drawing.Point(555, 40)
        Me.Button9.Margin = New System.Windows.Forms.Padding(4, 5, 4, 5)
        Me.Button9.Name = "Button9"
        Me.Button9.Size = New System.Drawing.Size(48, 29)
        Me.Button9.TabIndex = 113
        Me.Button9.Text = "R>"
        Me.Button9.UseVisualStyleBackColor = True
        '
        'chkTopMost
        '
        Me.chkTopMost.AutoSize = True
        Me.chkTopMost.Location = New System.Drawing.Point(973, 2)
        Me.chkTopMost.Name = "chkTopMost"
        Me.chkTopMost.Size = New System.Drawing.Size(15, 14)
        Me.chkTopMost.TabIndex = 115
        Me.chkTopMost.UseVisualStyleBackColor = True
        '
        'ButtonLNG
        '
        Me.ButtonLNG.Font = New System.Drawing.Font("Microsoft Sans Serif", 7.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(204, Byte))
        Me.ButtonLNG.Location = New System.Drawing.Point(1001, -2)
        Me.ButtonLNG.Name = "ButtonLNG"
        Me.ButtonLNG.Size = New System.Drawing.Size(44, 29)
        Me.ButtonLNG.TabIndex = 116
        Me.ButtonLNG.Text = "RU"
        Me.ButtonLNG.UseVisualStyleBackColor = True
        '
        'ButtonRename
        '
        Me.ButtonRename.Location = New System.Drawing.Point(899, 37)
        Me.ButtonRename.Name = "ButtonRename"
        Me.ButtonRename.Size = New System.Drawing.Size(46, 35)
        Me.ButtonRename.TabIndex = 117
        Me.ButtonRename.Text = "RN"
        Me.ButtonRename.UseVisualStyleBackColor = True
        '
        'TextBox1
        '
        Me.TextBox1.FormattingEnabled = True
        Me.TextBox1.Location = New System.Drawing.Point(126, 1)
        Me.TextBox1.Name = "TextBox1"
        Me.TextBox1.Size = New System.Drawing.Size(593, 28)
        Me.TextBox1.TabIndex = 118
        '
        'SortComboBox
        '
        Me.SortComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.SortComboBox.FormattingEnabled = True
        Me.SortComboBox.Items.AddRange(New Object() {"abc", "xyz", "rnd", ">size", "<size"})
        Me.SortComboBox.Location = New System.Drawing.Point(6, -1)
        Me.SortComboBox.Name = "SortComboBox"
        Me.SortComboBox.Size = New System.Drawing.Size(58, 28)
        Me.SortComboBox.TabIndex = 119
        '
        'WebBrowser1
        '
        Me.WebBrowser1.AllowWebBrowserDrop = False
        Me.WebBrowser1.Location = New System.Drawing.Point(0, 130)
        Me.WebBrowser1.Name = "WebBrowser1"
        Me.WebBrowser1.Size = New System.Drawing.Size(1040, 530)
        Me.WebBrowser1.TabIndex = 109
        Me.WebBrowser1.TabStop = False
        '
        'PictureBox2
        '
        Me.PictureBox2.Location = New System.Drawing.Point(0, 130)
        Me.PictureBox2.Name = "PictureBox2"
        Me.PictureBox2.Size = New System.Drawing.Size(1040, 530)
        Me.PictureBox2.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom
        Me.PictureBox2.TabIndex = 120
        Me.PictureBox2.TabStop = False
        '
        'PictureBox1
        '
        Me.PictureBox1.Location = New System.Drawing.Point(0, 130)
        Me.PictureBox1.Name = "PictureBox1"
        Me.PictureBox1.Size = New System.Drawing.Size(1040, 530)
        Me.PictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom
        Me.PictureBox1.TabIndex = 0
        Me.PictureBox1.TabStop = False
        '
        'Form1
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(9.0!, 20.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(1047, 666)
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
        Me.Margin = New System.Windows.Forms.Padding(2)
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