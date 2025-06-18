<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class Main_Form
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(Main_Form))
        Me.lbl_Folder = New System.Windows.Forms.Label()
        Me.btn_Select_Folder = New System.Windows.Forms.Button()
        Me.lbl_File_Number = New System.Windows.Forms.Label()
        Me.btn_Prev_File = New System.Windows.Forms.Button()
        Me.btn_Next_File = New System.Windows.Forms.Button()
        Me.bt_Delete = New System.Windows.Forms.Button()
        Me.btn_Move_Table = New System.Windows.Forms.Button()
        Me.lbl_Current_File = New System.Windows.Forms.Label()
        Me.lbl_Status = New System.Windows.Forms.Label()
        Me.lbl_Help_Info = New System.Windows.Forms.Label()
        Me.btn_Review = New System.Windows.Forms.Button()
        Me.btn_Slideshow = New System.Windows.Forms.Button()
        Me.btn_Full_Screen = New System.Windows.Forms.Button()
        Me.Web_Browser = New System.Windows.Forms.WebBrowser()
        Me.btn_Next_Random = New System.Windows.Forms.Button()
        Me.lbl_Slideshow_Time = New System.Windows.Forms.Label()
        Me.btn_Random_Slideshow = New System.Windows.Forms.Button()
        Me.chkbox_Top_Most = New System.Windows.Forms.CheckBox()
        Me.btn_Language = New System.Windows.Forms.Button()
        Me.btn_Rename = New System.Windows.Forms.Button()
        Me.cmbox_Media_Folder = New System.Windows.Forms.ComboBox()
        Me.cmbox_Sort = New System.Windows.Forms.ComboBox()
        Me.Picture_Box_2 = New System.Windows.Forms.PictureBox()
        Me.Picture_Box_1 = New System.Windows.Forms.PictureBox()
        CType(Me.Picture_Box_2, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.Picture_Box_1, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'lbl_Folder
        '
        Me.lbl_Folder.AutoSize = True
        Me.lbl_Folder.Location = New System.Drawing.Point(65, 4)
        Me.lbl_Folder.Margin = New System.Windows.Forms.Padding(2, 0, 2, 0)
        Me.lbl_Folder.Name = "lbl_Folder"
        Me.lbl_Folder.Size = New System.Drawing.Size(58, 20)
        Me.lbl_Folder.TabIndex = 1
        Me.lbl_Folder.Text = "Folder:"
        '
        'btn_Select_Folder
        '
        Me.btn_Select_Folder.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom
        Me.btn_Select_Folder.Location = New System.Drawing.Point(484, -1)
        Me.btn_Select_Folder.Margin = New System.Windows.Forms.Padding(2, 3, 2, 3)
        Me.btn_Select_Folder.Name = "btn_Select_Folder"
        Me.btn_Select_Folder.Size = New System.Drawing.Size(28, 23)
        Me.btn_Select_Folder.TabIndex = 1
        Me.btn_Select_Folder.Text = "..."
        Me.btn_Select_Folder.UseVisualStyleBackColor = True
        '
        'lbl_File_Number
        '
        Me.lbl_File_Number.BackColor = System.Drawing.Color.Transparent
        Me.lbl_File_Number.Location = New System.Drawing.Point(2, 29)
        Me.lbl_File_Number.Margin = New System.Windows.Forms.Padding(2, 0, 2, 0)
        Me.lbl_File_Number.Name = "lbl_File_Number"
        Me.lbl_File_Number.Size = New System.Drawing.Size(136, 18)
        Me.lbl_File_Number.TabIndex = 4
        Me.lbl_File_Number.Text = "Files: 0"
        '
        'btn_Prev_File
        '
        Me.btn_Prev_File.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.btn_Prev_File.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.btn_Prev_File.Location = New System.Drawing.Point(142, 24)
        Me.btn_Prev_File.Margin = New System.Windows.Forms.Padding(2, 3, 2, 3)
        Me.btn_Prev_File.Name = "btn_Prev_File"
        Me.btn_Prev_File.Size = New System.Drawing.Size(90, 23)
        Me.btn_Prev_File.TabIndex = 2
        Me.btn_Prev_File.Text = "< (P)rev"
        Me.btn_Prev_File.UseVisualStyleBackColor = True
        '
        'btn_Next_File
        '
        Me.btn_Next_File.ImageAlign = System.Drawing.ContentAlignment.MiddleRight
        Me.btn_Next_File.Location = New System.Drawing.Point(234, 24)
        Me.btn_Next_File.Margin = New System.Windows.Forms.Padding(2, 3, 2, 3)
        Me.btn_Next_File.Name = "btn_Next_File"
        Me.btn_Next_File.Size = New System.Drawing.Size(98, 23)
        Me.btn_Next_File.TabIndex = 3
        Me.btn_Next_File.Text = "(N)ext >"
        Me.btn_Next_File.UseVisualStyleBackColor = True
        '
        'bt_Delete
        '
        Me.bt_Delete.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.bt_Delete.Location = New System.Drawing.Point(634, 25)
        Me.bt_Delete.Margin = New System.Windows.Forms.Padding(2, 3, 2, 3)
        Me.bt_Delete.Name = "bt_Delete"
        Me.bt_Delete.Size = New System.Drawing.Size(62, 23)
        Me.bt_Delete.TabIndex = 5
        Me.bt_Delete.Text = "(D)elete"
        Me.bt_Delete.UseVisualStyleBackColor = True
        '
        'btn_Move_Table
        '
        Me.btn_Move_Table.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.btn_Move_Table.Location = New System.Drawing.Point(434, 24)
        Me.btn_Move_Table.Margin = New System.Windows.Forms.Padding(2, 3, 2, 3)
        Me.btn_Move_Table.Name = "btn_Move_Table"
        Me.btn_Move_Table.Size = New System.Drawing.Size(160, 23)
        Me.btn_Move_Table.TabIndex = 4
        Me.btn_Move_Table.Text = "MoveTo table"
        Me.btn_Move_Table.UseVisualStyleBackColor = True
        '
        'lbl_Current_File
        '
        Me.lbl_Current_File.AutoSize = True
        Me.lbl_Current_File.BackColor = System.Drawing.Color.Transparent
        Me.lbl_Current_File.Location = New System.Drawing.Point(4, 45)
        Me.lbl_Current_File.Margin = New System.Windows.Forms.Padding(2, 0, 2, 0)
        Me.lbl_Current_File.Name = "lbl_Current_File"
        Me.lbl_Current_File.Size = New System.Drawing.Size(83, 20)
        Me.lbl_Current_File.TabIndex = 0
        Me.lbl_Current_File.Text = "current file"
        '
        'lbl_Status
        '
        Me.lbl_Status.AutoSize = True
        Me.lbl_Status.BackColor = System.Drawing.Color.Transparent
        Me.lbl_Status.Location = New System.Drawing.Point(4, 62)
        Me.lbl_Status.Margin = New System.Windows.Forms.Padding(2, 0, 2, 0)
        Me.lbl_Status.Name = "lbl_Status"
        Me.lbl_Status.Size = New System.Drawing.Size(53, 20)
        Me.lbl_Status.TabIndex = 100
        Me.lbl_Status.Text = "status"
        '
        'lbl_Help_Info
        '
        Me.lbl_Help_Info.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.lbl_Help_Info.BackColor = System.Drawing.Color.White
        Me.lbl_Help_Info.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D
        Me.lbl_Help_Info.FlatStyle = System.Windows.Forms.FlatStyle.Popup
        Me.lbl_Help_Info.Font = New System.Drawing.Font("Microsoft Sans Serif", 10.0!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(204, Byte))
        Me.lbl_Help_Info.ForeColor = System.Drawing.Color.Black
        Me.lbl_Help_Info.Location = New System.Drawing.Point(4, 47)
        Me.lbl_Help_Info.Margin = New System.Windows.Forms.Padding(2, 0, 2, 0)
        Me.lbl_Help_Info.Name = "lbl_Help_Info"
        Me.lbl_Help_Info.Size = New System.Drawing.Size(690, 344)
        Me.lbl_Help_Info.TabIndex = 0
        Me.lbl_Help_Info.Text = "First run"
        Me.lbl_Help_Info.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.lbl_Help_Info.Visible = False
        '
        'btn_Review
        '
        Me.btn_Review.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom
        Me.btn_Review.Location = New System.Drawing.Point(518, -1)
        Me.btn_Review.Margin = New System.Windows.Forms.Padding(2, 3, 2, 3)
        Me.btn_Review.Name = "btn_Review"
        Me.btn_Review.Size = New System.Drawing.Size(38, 23)
        Me.btn_Review.TabIndex = 106
        Me.btn_Review.Text = "RE"
        Me.btn_Review.UseVisualStyleBackColor = True
        '
        'btn_Slideshow
        '
        Me.btn_Slideshow.Location = New System.Drawing.Point(402, 26)
        Me.btn_Slideshow.Margin = New System.Windows.Forms.Padding(2, 3, 2, 3)
        Me.btn_Slideshow.Name = "btn_Slideshow"
        Me.btn_Slideshow.Size = New System.Drawing.Size(32, 20)
        Me.btn_Slideshow.TabIndex = 107
        Me.btn_Slideshow.Text = ">>"
        Me.btn_Slideshow.UseVisualStyleBackColor = True
        '
        'btn_Full_Screen
        '
        Me.btn_Full_Screen.BackgroundImage = CType(resources.GetObject("btn_Full_Screen.BackgroundImage"), System.Drawing.Image)
        Me.btn_Full_Screen.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom
        Me.btn_Full_Screen.Location = New System.Drawing.Point(558, -1)
        Me.btn_Full_Screen.Margin = New System.Windows.Forms.Padding(2, 3, 2, 3)
        Me.btn_Full_Screen.Name = "btn_Full_Screen"
        Me.btn_Full_Screen.Size = New System.Drawing.Size(38, 23)
        Me.btn_Full_Screen.TabIndex = 108
        Me.btn_Full_Screen.Text = "^^"
        Me.btn_Full_Screen.UseVisualStyleBackColor = True
        '
        'Web_Browser
        '
        Me.Web_Browser.AllowWebBrowserDrop = False
        Me.Web_Browser.Location = New System.Drawing.Point(0, 84)
        Me.Web_Browser.Margin = New System.Windows.Forms.Padding(2)
        Me.Web_Browser.Name = "Web_Browser"
        Me.Web_Browser.Size = New System.Drawing.Size(694, 344)
        Me.Web_Browser.TabIndex = 109
        Me.Web_Browser.TabStop = False
        '
        'btn_Next_Random
        '
        Me.btn_Next_Random.Font = New System.Drawing.Font("Microsoft Sans Serif", 9.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(204, Byte))
        Me.btn_Next_Random.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.btn_Next_Random.Location = New System.Drawing.Point(334, 23)
        Me.btn_Next_Random.Margin = New System.Windows.Forms.Padding(2, 3, 2, 3)
        Me.btn_Next_Random.Name = "btn_Next_Random"
        Me.btn_Next_Random.Size = New System.Drawing.Size(36, 23)
        Me.btn_Next_Random.TabIndex = 111
        Me.btn_Next_Random.Text = "(Y)Rnd>"
        Me.btn_Next_Random.UseVisualStyleBackColor = True
        '
        'lbl_Slideshow_Time
        '
        Me.lbl_Slideshow_Time.AutoSize = True
        Me.lbl_Slideshow_Time.BackColor = System.Drawing.Color.Transparent
        Me.lbl_Slideshow_Time.Location = New System.Drawing.Point(598, 1)
        Me.lbl_Slideshow_Time.Margin = New System.Windows.Forms.Padding(2, 0, 2, 0)
        Me.lbl_Slideshow_Time.Name = "lbl_Slideshow_Time"
        Me.lbl_Slideshow_Time.Size = New System.Drawing.Size(26, 20)
        Me.lbl_Slideshow_Time.TabIndex = 112
        Me.lbl_Slideshow_Time.Text = "1s"
        '
        'btn_Random_Slideshow
        '
        Me.btn_Random_Slideshow.Location = New System.Drawing.Point(370, 26)
        Me.btn_Random_Slideshow.Margin = New System.Windows.Forms.Padding(2, 3, 2, 3)
        Me.btn_Random_Slideshow.Name = "btn_Random_Slideshow"
        Me.btn_Random_Slideshow.Size = New System.Drawing.Size(32, 20)
        Me.btn_Random_Slideshow.TabIndex = 113
        Me.btn_Random_Slideshow.Text = "R>"
        Me.btn_Random_Slideshow.UseVisualStyleBackColor = True
        '
        'chkbox_Top_Most
        '
        Me.chkbox_Top_Most.AutoSize = True
        Me.chkbox_Top_Most.Location = New System.Drawing.Point(0, 0)
        Me.chkbox_Top_Most.Margin = New System.Windows.Forms.Padding(2)
        Me.chkbox_Top_Most.Name = "chkbox_Top_Most"
        Me.chkbox_Top_Most.Size = New System.Drawing.Size(15, 14)
        Me.chkbox_Top_Most.TabIndex = 115
        Me.chkbox_Top_Most.UseVisualStyleBackColor = True
        '
        'btn_Language
        '
        Me.btn_Language.Font = New System.Drawing.Font("Microsoft Sans Serif", 7.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(204, Byte))
        Me.btn_Language.Location = New System.Drawing.Point(668, -1)
        Me.btn_Language.Margin = New System.Windows.Forms.Padding(2)
        Me.btn_Language.Name = "btn_Language"
        Me.btn_Language.Size = New System.Drawing.Size(30, 20)
        Me.btn_Language.TabIndex = 116
        Me.btn_Language.Text = "RU"
        Me.btn_Language.UseVisualStyleBackColor = True
        '
        'btn_Rename
        '
        Me.btn_Rename.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center
        Me.btn_Rename.Location = New System.Drawing.Point(600, 24)
        Me.btn_Rename.Margin = New System.Windows.Forms.Padding(2)
        Me.btn_Rename.Name = "btn_Rename"
        Me.btn_Rename.Size = New System.Drawing.Size(30, 23)
        Me.btn_Rename.TabIndex = 117
        Me.btn_Rename.Text = "RN"
        Me.btn_Rename.UseVisualStyleBackColor = True
        '
        'cmbox_Media_Folder
        '
        Me.cmbox_Media_Folder.DropDownHeight = 300
        Me.cmbox_Media_Folder.FormattingEnabled = True
        Me.cmbox_Media_Folder.IntegralHeight = False
        Me.cmbox_Media_Folder.Location = New System.Drawing.Point(108, 1)
        Me.cmbox_Media_Folder.Margin = New System.Windows.Forms.Padding(2)
        Me.cmbox_Media_Folder.Name = "cmbox_Media_Folder"
        Me.cmbox_Media_Folder.Size = New System.Drawing.Size(372, 21)
        Me.cmbox_Media_Folder.TabIndex = 118
        '
        'cmbox_Sort
        '
        Me.cmbox_Sort.DropDownHeight = 146
        Me.cmbox_Sort.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.cmbox_Sort.FormattingEnabled = True
        Me.cmbox_Sort.IntegralHeight = False
        Me.cmbox_Sort.ItemHeight = 13
        Me.cmbox_Sort.Items.AddRange(New Object() {"abc", "xyz", "rnd", ">size", "<size", "<time", ">time", "<0123", ">3210"})
        Me.cmbox_Sort.Location = New System.Drawing.Point(21, -1)
        Me.cmbox_Sort.Margin = New System.Windows.Forms.Padding(2)
        Me.cmbox_Sort.Name = "cmbox_Sort"
        Me.cmbox_Sort.Size = New System.Drawing.Size(40, 21)
        Me.cmbox_Sort.TabIndex = 119
        '
        'Picture_Box_2
        '
        Me.Picture_Box_2.InitialImage = Nothing
        Me.Picture_Box_2.Location = New System.Drawing.Point(0, 84)
        Me.Picture_Box_2.Margin = New System.Windows.Forms.Padding(2)
        Me.Picture_Box_2.Name = "Picture_Box_2"
        Me.Picture_Box_2.Size = New System.Drawing.Size(694, 344)
        Me.Picture_Box_2.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom
        Me.Picture_Box_2.TabIndex = 120
        Me.Picture_Box_2.TabStop = False
        '
        'Picture_Box_1
        '
        Me.Picture_Box_1.InitialImage = Nothing
        Me.Picture_Box_1.Location = New System.Drawing.Point(0, 84)
        Me.Picture_Box_1.Margin = New System.Windows.Forms.Padding(2)
        Me.Picture_Box_1.Name = "Picture_Box_1"
        Me.Picture_Box_1.Size = New System.Drawing.Size(694, 344)
        Me.Picture_Box_1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom
        Me.Picture_Box_1.TabIndex = 0
        Me.Picture_Box_1.TabStop = False
        '
        'the_Main_Form
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(698, 433)
        Me.Controls.Add(Me.cmbox_Media_Folder)
        Me.Controls.Add(Me.cmbox_Sort)
        Me.Controls.Add(Me.btn_Rename)
        Me.Controls.Add(Me.btn_Move_Table)
        Me.Controls.Add(Me.btn_Slideshow)
        Me.Controls.Add(Me.btn_Random_Slideshow)
        Me.Controls.Add(Me.btn_Next_Random)
        Me.Controls.Add(Me.btn_Next_File)
        Me.Controls.Add(Me.btn_Prev_File)
        Me.Controls.Add(Me.btn_Review)
        Me.Controls.Add(Me.btn_Select_Folder)
        Me.Controls.Add(Me.btn_Full_Screen)
        Me.Controls.Add(Me.chkbox_Top_Most)
        Me.Controls.Add(Me.btn_Language)
        Me.Controls.Add(Me.bt_Delete)
        Me.Controls.Add(Me.lbl_Slideshow_Time)
        Me.Controls.Add(Me.lbl_Status)
        Me.Controls.Add(Me.lbl_Current_File)
        Me.Controls.Add(Me.lbl_File_Number)
        Me.Controls.Add(Me.lbl_Folder)
        Me.Controls.Add(Me.lbl_Help_Info)
        Me.Controls.Add(Me.Web_Browser)
        Me.Controls.Add(Me.Picture_Box_2)
        Me.Controls.Add(Me.Picture_Box_1)
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.KeyPreview = True
        Me.Margin = New System.Windows.Forms.Padding(2, 1, 2, 1)
        Me.Name = "the_Main_Form"
        Me.Text = "Fast Media Sorter by SZA"
        CType(Me.Picture_Box_2, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.Picture_Box_1, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub

    Friend WithEvents lbl_Help_Info As System.Windows.Forms.Label
    Friend WithEvents lbl_Folder As System.Windows.Forms.Label
    Friend WithEvents btn_Select_Folder As System.Windows.Forms.Button
    Friend WithEvents lbl_File_Number As System.Windows.Forms.Label
    Friend WithEvents btn_Prev_File As System.Windows.Forms.Button
    Friend WithEvents btn_Next_File As System.Windows.Forms.Button
    Friend WithEvents bt_Delete As System.Windows.Forms.Button
    Friend WithEvents btn_Move_Table As System.Windows.Forms.Button
    Friend WithEvents lbl_Current_File As System.Windows.Forms.Label
    Friend WithEvents lbl_Status As System.Windows.Forms.Label
    Friend WithEvents btn_Review As System.Windows.Forms.Button
    Friend WithEvents btn_Slideshow As Button
    Friend WithEvents btn_Full_Screen As Button
    Friend WithEvents Web_Browser As WebBrowser
    Friend WithEvents btn_Next_Random As Button
    Friend WithEvents lbl_Slideshow_Time As Label
    Friend WithEvents btn_Random_Slideshow As Button
    Friend WithEvents chkbox_Top_Most As CheckBox
    Friend WithEvents btn_Language As Button
    Friend WithEvents btn_Rename As Button
    Friend WithEvents cmbox_Media_Folder As ComboBox
    Friend WithEvents cmbox_Sort As ComboBox
    Friend WithEvents Picture_Box_2 As PictureBox
    Friend WithEvents Picture_Box_1 As PictureBox
End Class