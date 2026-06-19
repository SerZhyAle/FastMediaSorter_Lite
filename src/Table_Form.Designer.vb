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
        Me.lbl_Grid_Hint = New System.Windows.Forms.Label()
        Me.KeyNum = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.MoveToFolder = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.chkbox_Copy_Mode = New System.Windows.Forms.CheckBox()
        Me.chkbox_Independent_Thread_For_File_Operation = New System.Windows.Forms.CheckBox()
        Me.cmbox_color_schema = New System.Windows.Forms.ComboBox()
        Me.lbl_Color = New System.Windows.Forms.Label()
        Me.chb_perspectiva = New System.Windows.Forms.CheckBox()
        Me.Tab_Control = New System.Windows.Forms.TabControl()
        Me.Tab_Page_1 = New System.Windows.Forms.TabPage()
        Me.Tab_Page_2 = New System.Windows.Forms.TabPage()
        Me.Tab_Page_3 = New System.Windows.Forms.TabPage()
        Me.Tab_Page_4 = New System.Windows.Forms.TabPage()
        Me.grp_Background = New System.Windows.Forms.GroupBox()
        Me.grp_OnScreen = New System.Windows.Forms.GroupBox()
        Me.grp_Slideshow = New System.Windows.Forms.GroupBox()
        Me.grp_Panel = New System.Windows.Forms.GroupBox()
        Me.grp_Quality = New System.Windows.Forms.GroupBox()
        Me.grp_Video = New System.Windows.Forms.GroupBox()
        Me.grp_FileOps = New System.Windows.Forms.GroupBox()
        Me.grp_Integration = New System.Windows.Forms.GroupBox()
        Me.grp_Window = New System.Windows.Forms.GroupBox()
        Me.grp_Language = New System.Windows.Forms.GroupBox()
        Me.btn_Language = New System.Windows.Forms.Button()
        Me.cmb_Picture_Size = New System.Windows.Forms.ComboBox()
        Me.lbl_Picture_at_Panel_Size = New System.Windows.Forms.Label()
        Me.lbl_Slideshow_Interval = New System.Windows.Forms.Label()
        Me.num_Slideshow_Interval = New System.Windows.Forms.NumericUpDown()
        Me.lbl_Video_Volume = New System.Windows.Forms.Label()
        Me.num_Video_Volume = New System.Windows.Forms.NumericUpDown()
        Me.chk_Video_Mute = New System.Windows.Forms.CheckBox()
        Me.chkb_no_request_before_file_operation = New System.Windows.Forms.CheckBox()
        Me.chkb_show_file_size = New System.Windows.Forms.CheckBox()
        Me.chkb_video_loop = New System.Windows.Forms.CheckBox()
        Me.chkb_is_to_show_file_datetime = New System.Windows.Forms.CheckBox()
        Me.chkb_show_pic_size = New System.Windows.Forms.CheckBox()
        Me.chk_Exif_AutoRotate = New System.Windows.Forms.CheckBox()
        Me.chk_Hq_Scaling = New System.Windows.Forms.CheckBox()
        Me.chk_Show_Info_Overlay = New System.Windows.Forms.CheckBox()
        Me.SetOnTop = New System.Windows.Forms.CheckBox()
        Me.LinkLabel1 = New System.Windows.Forms.LinkLabel()
        Me.btn_Set_As_Default = New System.Windows.Forms.Button()
        Me.btn_Set_As_Default_Video = New System.Windows.Forms.Button()
        Me.btn_OcrTranslate = New System.Windows.Forms.Button()
        CType(Me.Data_Grid_View, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.Tab_Control.SuspendLayout()
        Me.Tab_Page_1.SuspendLayout()
        Me.Tab_Page_2.SuspendLayout()
        Me.Tab_Page_3.SuspendLayout()
        Me.Tab_Page_4.SuspendLayout()
        Me.grp_Background.SuspendLayout()
        Me.grp_OnScreen.SuspendLayout()
        Me.grp_Slideshow.SuspendLayout()
        Me.grp_Panel.SuspendLayout()
        Me.grp_Quality.SuspendLayout()
        Me.grp_Video.SuspendLayout()
        Me.grp_FileOps.SuspendLayout()
        Me.grp_Integration.SuspendLayout()
        Me.grp_Window.SuspendLayout()
        Me.grp_Language.SuspendLayout()
        CType(Me.num_Slideshow_Interval, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.num_Video_Volume, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'Data_Grid_View
        '
        Me.Data_Grid_View.AllowUserToAddRows = False
        Me.Data_Grid_View.AllowUserToDeleteRows = False
        Me.Data_Grid_View.AllowUserToResizeRows = False
        Me.Data_Grid_View.Anchor = CType(((System.Windows.Forms.AnchorStyles.Top _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.Data_Grid_View.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize
        Me.Data_Grid_View.Columns.AddRange(New System.Windows.Forms.DataGridViewColumn() {Me.KeyNum, Me.MoveToFolder})
        Me.Data_Grid_View.Location = New System.Drawing.Point(8, 8)
        Me.Data_Grid_View.MultiSelect = False
        Me.Data_Grid_View.Name = "Data_Grid_View"
        Me.Data_Grid_View.RowHeadersVisible = False
        Me.Data_Grid_View.ScrollBars = System.Windows.Forms.ScrollBars.None
        Me.Data_Grid_View.Size = New System.Drawing.Size(660, 300)
        Me.Data_Grid_View.TabIndex = 0
        '
        'lbl_Grid_Hint
        '
        Me.lbl_Grid_Hint.AutoSize = True
        Me.lbl_Grid_Hint.ForeColor = System.Drawing.SystemColors.GrayText
        Me.lbl_Grid_Hint.Location = New System.Drawing.Point(8, 316)
        Me.lbl_Grid_Hint.Name = "lbl_Grid_Hint"
        Me.lbl_Grid_Hint.Size = New System.Drawing.Size(0, 15)
        Me.lbl_Grid_Hint.TabIndex = 1
        Me.lbl_Grid_Hint.Text = "hint"
        '
        'KeyNum
        '
        Me.KeyNum.HeaderText = "KEY"
        Me.KeyNum.MinimumWidth = 8
        Me.KeyNum.Name = "KeyNum"
        Me.KeyNum.ReadOnly = True
        Me.KeyNum.Width = 90
        '
        'MoveToFolder
        '
        Me.MoveToFolder.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill
        Me.MoveToFolder.HeaderText = "move to folder"
        Me.MoveToFolder.MinimumWidth = 8
        Me.MoveToFolder.Name = "MoveToFolder"
        '
        'Tab_Control
        '
        Me.Tab_Control.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.Tab_Control.Controls.Add(Me.Tab_Page_1)
        Me.Tab_Control.Controls.Add(Me.Tab_Page_2)
        Me.Tab_Control.Controls.Add(Me.Tab_Page_3)
        Me.Tab_Control.Controls.Add(Me.Tab_Page_4)
        Me.Tab_Control.Location = New System.Drawing.Point(8, 8)
        Me.Tab_Control.Name = "Tab_Control"
        Me.Tab_Control.SelectedIndex = 0
        Me.Tab_Control.Size = New System.Drawing.Size(684, 424)
        Me.Tab_Control.TabIndex = 0
        '
        'Tab_Page_1
        '
        Me.Tab_Page_1.Controls.Add(Me.lbl_Grid_Hint)
        Me.Tab_Page_1.Controls.Add(Me.Data_Grid_View)
        Me.Tab_Page_1.Location = New System.Drawing.Point(4, 29)
        Me.Tab_Page_1.Name = "Tab_Page_1"
        Me.Tab_Page_1.Padding = New System.Windows.Forms.Padding(4)
        Me.Tab_Page_1.Size = New System.Drawing.Size(676, 391)
        Me.Tab_Page_1.TabIndex = 0
        Me.Tab_Page_1.Text = "Destination folders"
        Me.Tab_Page_1.UseVisualStyleBackColor = True
        '
        'Tab_Page_2
        '
        Me.Tab_Page_2.Controls.Add(Me.grp_Panel)
        Me.Tab_Page_2.Controls.Add(Me.grp_Slideshow)
        Me.Tab_Page_2.Controls.Add(Me.grp_OnScreen)
        Me.Tab_Page_2.Controls.Add(Me.grp_Background)
        Me.Tab_Page_2.Location = New System.Drawing.Point(4, 29)
        Me.Tab_Page_2.Name = "Tab_Page_2"
        Me.Tab_Page_2.Padding = New System.Windows.Forms.Padding(4)
        Me.Tab_Page_2.Size = New System.Drawing.Size(676, 391)
        Me.Tab_Page_2.TabIndex = 1
        Me.Tab_Page_2.Text = "Viewing"
        Me.Tab_Page_2.UseVisualStyleBackColor = True
        '
        'Tab_Page_3
        '
        Me.Tab_Page_3.Controls.Add(Me.grp_Video)
        Me.Tab_Page_3.Controls.Add(Me.grp_Quality)
        Me.Tab_Page_3.Location = New System.Drawing.Point(4, 29)
        Me.Tab_Page_3.Name = "Tab_Page_3"
        Me.Tab_Page_3.Padding = New System.Windows.Forms.Padding(4)
        Me.Tab_Page_3.Size = New System.Drawing.Size(676, 391)
        Me.Tab_Page_3.TabIndex = 2
        Me.Tab_Page_3.Text = "Video and quality"
        Me.Tab_Page_3.UseVisualStyleBackColor = True
        '
        'Tab_Page_4
        '
        Me.Tab_Page_4.Controls.Add(Me.grp_Language)
        Me.Tab_Page_4.Controls.Add(Me.grp_Window)
        Me.Tab_Page_4.Controls.Add(Me.grp_Integration)
        Me.Tab_Page_4.Controls.Add(Me.grp_FileOps)
        Me.Tab_Page_4.Location = New System.Drawing.Point(4, 29)
        Me.Tab_Page_4.Name = "Tab_Page_4"
        Me.Tab_Page_4.Padding = New System.Windows.Forms.Padding(4)
        Me.Tab_Page_4.Size = New System.Drawing.Size(676, 391)
        Me.Tab_Page_4.TabIndex = 3
        Me.Tab_Page_4.Text = "Files and system"
        Me.Tab_Page_4.UseVisualStyleBackColor = True
        '
        'grp_Background
        '
        Me.grp_Background.Controls.Add(Me.lbl_Color)
        Me.grp_Background.Controls.Add(Me.cmbox_color_schema)
        Me.grp_Background.Controls.Add(Me.chb_perspectiva)
        Me.grp_Background.Location = New System.Drawing.Point(8, 8)
        Me.grp_Background.Name = "grp_Background"
        Me.grp_Background.Size = New System.Drawing.Size(656, 92)
        Me.grp_Background.TabIndex = 0
        Me.grp_Background.TabStop = False
        Me.grp_Background.Text = "Background"
        '
        'grp_OnScreen
        '
        Me.grp_OnScreen.Controls.Add(Me.chkb_show_pic_size)
        Me.grp_OnScreen.Controls.Add(Me.chkb_is_to_show_file_datetime)
        Me.grp_OnScreen.Controls.Add(Me.chkb_show_file_size)
        Me.grp_OnScreen.Controls.Add(Me.chk_Show_Info_Overlay)
        Me.grp_OnScreen.Location = New System.Drawing.Point(8, 108)
        Me.grp_OnScreen.Name = "grp_OnScreen"
        Me.grp_OnScreen.Size = New System.Drawing.Size(656, 104)
        Me.grp_OnScreen.TabIndex = 1
        Me.grp_OnScreen.TabStop = False
        Me.grp_OnScreen.Text = "On-screen info"
        '
        'grp_Slideshow
        '
        Me.grp_Slideshow.Controls.Add(Me.lbl_Slideshow_Interval)
        Me.grp_Slideshow.Controls.Add(Me.num_Slideshow_Interval)
        Me.grp_Slideshow.Location = New System.Drawing.Point(8, 220)
        Me.grp_Slideshow.Name = "grp_Slideshow"
        Me.grp_Slideshow.Size = New System.Drawing.Size(656, 62)
        Me.grp_Slideshow.TabIndex = 2
        Me.grp_Slideshow.TabStop = False
        Me.grp_Slideshow.Text = "Slideshow"
        '
        'grp_Panel
        '
        Me.grp_Panel.Controls.Add(Me.lbl_Picture_at_Panel_Size)
        Me.grp_Panel.Controls.Add(Me.cmb_Picture_Size)
        Me.grp_Panel.Location = New System.Drawing.Point(8, 290)
        Me.grp_Panel.Name = "grp_Panel"
        Me.grp_Panel.Size = New System.Drawing.Size(656, 58)
        Me.grp_Panel.TabIndex = 3
        Me.grp_Panel.TabStop = False
        Me.grp_Panel.Text = "Thumbnail panel"
        '
        'grp_Quality
        '
        Me.grp_Quality.Controls.Add(Me.chk_Exif_AutoRotate)
        Me.grp_Quality.Controls.Add(Me.chk_Hq_Scaling)
        Me.grp_Quality.Location = New System.Drawing.Point(8, 8)
        Me.grp_Quality.Name = "grp_Quality"
        Me.grp_Quality.Size = New System.Drawing.Size(656, 70)
        Me.grp_Quality.TabIndex = 0
        Me.grp_Quality.TabStop = False
        Me.grp_Quality.Text = "Image quality"
        '
        'grp_Video
        '
        Me.grp_Video.Controls.Add(Me.chkb_video_loop)
        Me.grp_Video.Controls.Add(Me.chk_Video_Mute)
        Me.grp_Video.Controls.Add(Me.lbl_Video_Volume)
        Me.grp_Video.Controls.Add(Me.num_Video_Volume)
        Me.grp_Video.Location = New System.Drawing.Point(8, 86)
        Me.grp_Video.Name = "grp_Video"
        Me.grp_Video.Size = New System.Drawing.Size(656, 148)
        Me.grp_Video.TabIndex = 1
        Me.grp_Video.TabStop = False
        Me.grp_Video.Text = "Video"
        '
        'grp_FileOps
        '
        Me.grp_FileOps.Controls.Add(Me.chkbox_Copy_Mode)
        Me.grp_FileOps.Controls.Add(Me.chkbox_Independent_Thread_For_File_Operation)
        Me.grp_FileOps.Controls.Add(Me.chkb_no_request_before_file_operation)
        Me.grp_FileOps.Location = New System.Drawing.Point(8, 8)
        Me.grp_FileOps.Name = "grp_FileOps"
        Me.grp_FileOps.Size = New System.Drawing.Size(656, 132)
        Me.grp_FileOps.TabIndex = 0
        Me.grp_FileOps.TabStop = False
        Me.grp_FileOps.Text = "File operations"
        '
        'grp_Integration
        '
        Me.grp_Integration.Controls.Add(Me.btn_Set_As_Default)
        Me.grp_Integration.Controls.Add(Me.btn_Set_As_Default_Video)
        Me.grp_Integration.Controls.Add(Me.btn_OcrTranslate)
        Me.grp_Integration.Location = New System.Drawing.Point(8, 148)
        Me.grp_Integration.Name = "grp_Integration"
        Me.grp_Integration.Size = New System.Drawing.Size(656, 152)
        Me.grp_Integration.TabIndex = 1
        Me.grp_Integration.TabStop = False
        Me.grp_Integration.Text = "Associations and integration"
        '
        'grp_Window
        '
        Me.grp_Window.Controls.Add(Me.SetOnTop)
        Me.grp_Window.Location = New System.Drawing.Point(8, 308)
        Me.grp_Window.Name = "grp_Window"
        Me.grp_Window.Size = New System.Drawing.Size(320, 70)
        Me.grp_Window.TabIndex = 2
        Me.grp_Window.TabStop = False
        Me.grp_Window.Text = "Window"
        '
        'grp_Language
        '
        Me.grp_Language.Controls.Add(Me.btn_Language)
        Me.grp_Language.Location = New System.Drawing.Point(344, 308)
        Me.grp_Language.Name = "grp_Language"
        Me.grp_Language.Size = New System.Drawing.Size(320, 70)
        Me.grp_Language.TabIndex = 3
        Me.grp_Language.TabStop = False
        Me.grp_Language.Text = "Language"
        '
        'lbl_Color
        '
        Me.lbl_Color.AutoSize = True
        Me.lbl_Color.Location = New System.Drawing.Point(16, 32)
        Me.lbl_Color.Name = "lbl_Color"
        Me.lbl_Color.Size = New System.Drawing.Size(35, 15)
        Me.lbl_Color.TabIndex = 0
        Me.lbl_Color.Text = "Color"
        '
        'cmbox_color_schema
        '
        Me.cmbox_color_schema.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.cmbox_color_schema.FormattingEnabled = True
        Me.cmbox_color_schema.Location = New System.Drawing.Point(150, 28)
        Me.cmbox_color_schema.Name = "cmbox_color_schema"
        Me.cmbox_color_schema.Size = New System.Drawing.Size(180, 23)
        Me.cmbox_color_schema.TabIndex = 1
        '
        'chb_perspectiva
        '
        Me.chb_perspectiva.AutoSize = True
        Me.chb_perspectiva.Location = New System.Drawing.Point(16, 62)
        Me.chb_perspectiva.Name = "chb_perspectiva"
        Me.chb_perspectiva.Size = New System.Drawing.Size(108, 19)
        Me.chb_perspectiva.TabIndex = 2
        Me.chb_perspectiva.Text = "Show pespective"
        Me.chb_perspectiva.UseVisualStyleBackColor = True
        '
        'chkb_show_pic_size
        '
        Me.chkb_show_pic_size.AutoSize = True
        Me.chkb_show_pic_size.Location = New System.Drawing.Point(16, 28)
        Me.chkb_show_pic_size.Name = "chkb_show_pic_size"
        Me.chkb_show_pic_size.Size = New System.Drawing.Size(115, 19)
        Me.chkb_show_pic_size.TabIndex = 0
        Me.chkb_show_pic_size.Text = "Show picture size"
        Me.chkb_show_pic_size.UseVisualStyleBackColor = True
        '
        'chkb_is_to_show_file_datetime
        '
        Me.chkb_is_to_show_file_datetime.AutoSize = True
        Me.chkb_is_to_show_file_datetime.Location = New System.Drawing.Point(16, 62)
        Me.chkb_is_to_show_file_datetime.Name = "chkb_is_to_show_file_datetime"
        Me.chkb_is_to_show_file_datetime.Size = New System.Drawing.Size(105, 19)
        Me.chkb_is_to_show_file_datetime.TabIndex = 1
        Me.chkb_is_to_show_file_datetime.Text = "Show datetime"
        Me.chkb_is_to_show_file_datetime.UseVisualStyleBackColor = True
        '
        'chkb_show_file_size
        '
        Me.chkb_show_file_size.AutoSize = True
        Me.chkb_show_file_size.Location = New System.Drawing.Point(340, 28)
        Me.chkb_show_file_size.Name = "chkb_show_file_size"
        Me.chkb_show_file_size.Size = New System.Drawing.Size(98, 19)
        Me.chkb_show_file_size.TabIndex = 2
        Me.chkb_show_file_size.Text = "Show file size"
        Me.chkb_show_file_size.UseVisualStyleBackColor = True
        '
        'chk_Show_Info_Overlay
        '
        Me.chk_Show_Info_Overlay.AutoSize = True
        Me.chk_Show_Info_Overlay.Location = New System.Drawing.Point(340, 62)
        Me.chk_Show_Info_Overlay.Name = "chk_Show_Info_Overlay"
        Me.chk_Show_Info_Overlay.Size = New System.Drawing.Size(180, 19)
        Me.chk_Show_Info_Overlay.TabIndex = 3
        Me.chk_Show_Info_Overlay.Text = "Overlay name + position on image"
        Me.chk_Show_Info_Overlay.UseVisualStyleBackColor = True
        '
        'lbl_Slideshow_Interval
        '
        Me.lbl_Slideshow_Interval.AutoSize = True
        Me.lbl_Slideshow_Interval.Location = New System.Drawing.Point(16, 28)
        Me.lbl_Slideshow_Interval.Name = "lbl_Slideshow_Interval"
        Me.lbl_Slideshow_Interval.Size = New System.Drawing.Size(130, 15)
        Me.lbl_Slideshow_Interval.TabIndex = 0
        Me.lbl_Slideshow_Interval.Text = "Slideshow interval (s):"
        '
        'num_Slideshow_Interval
        '
        Me.num_Slideshow_Interval.Location = New System.Drawing.Point(250, 24)
        Me.num_Slideshow_Interval.Maximum = New Decimal(New Integer() {120, 0, 0, 0})
        Me.num_Slideshow_Interval.Minimum = New Decimal(New Integer() {1, 0, 0, 0})
        Me.num_Slideshow_Interval.Name = "num_Slideshow_Interval"
        Me.num_Slideshow_Interval.Size = New System.Drawing.Size(70, 23)
        Me.num_Slideshow_Interval.TabIndex = 1
        Me.num_Slideshow_Interval.Value = New Decimal(New Integer() {10, 0, 0, 0})
        '
        'lbl_Video_Volume
        '
        Me.lbl_Video_Volume.AutoSize = True
        Me.lbl_Video_Volume.Location = New System.Drawing.Point(16, 100)
        Me.lbl_Video_Volume.Name = "lbl_Video_Volume"
        Me.lbl_Video_Volume.Size = New System.Drawing.Size(120, 15)
        Me.lbl_Video_Volume.TabIndex = 2
        Me.lbl_Video_Volume.Text = "Default volume (%):"
        '
        'num_Video_Volume
        '
        Me.num_Video_Volume.Location = New System.Drawing.Point(250, 96)
        Me.num_Video_Volume.Maximum = New Decimal(New Integer() {100, 0, 0, 0})
        Me.num_Video_Volume.Name = "num_Video_Volume"
        Me.num_Video_Volume.Size = New System.Drawing.Size(70, 23)
        Me.num_Video_Volume.TabIndex = 3
        Me.num_Video_Volume.Value = New Decimal(New Integer() {100, 0, 0, 0})
        '
        'chk_Video_Mute
        '
        Me.chk_Video_Mute.AutoSize = True
        Me.chk_Video_Mute.Location = New System.Drawing.Point(16, 62)
        Me.chk_Video_Mute.Name = "chk_Video_Mute"
        Me.chk_Video_Mute.Size = New System.Drawing.Size(150, 19)
        Me.chk_Video_Mute.TabIndex = 1
        Me.chk_Video_Mute.Text = "Muted by default"
        Me.chk_Video_Mute.UseVisualStyleBackColor = True
        '
        'chk_Exif_AutoRotate
        '
        Me.chk_Exif_AutoRotate.AutoSize = True
        Me.chk_Exif_AutoRotate.Location = New System.Drawing.Point(16, 28)
        Me.chk_Exif_AutoRotate.Name = "chk_Exif_AutoRotate"
        Me.chk_Exif_AutoRotate.Size = New System.Drawing.Size(150, 19)
        Me.chk_Exif_AutoRotate.TabIndex = 0
        Me.chk_Exif_AutoRotate.Text = "Auto-rotate by EXIF"
        Me.chk_Exif_AutoRotate.UseVisualStyleBackColor = True
        '
        'chk_Hq_Scaling
        '
        Me.chk_Hq_Scaling.AutoSize = True
        Me.chk_Hq_Scaling.Location = New System.Drawing.Point(340, 28)
        Me.chk_Hq_Scaling.Name = "chk_Hq_Scaling"
        Me.chk_Hq_Scaling.Size = New System.Drawing.Size(170, 19)
        Me.chk_Hq_Scaling.TabIndex = 1
        Me.chk_Hq_Scaling.Text = "High-quality scaling"
        Me.chk_Hq_Scaling.UseVisualStyleBackColor = True
        '
        'chkb_video_loop
        '
        Me.chkb_video_loop.AutoSize = True
        Me.chkb_video_loop.Location = New System.Drawing.Point(16, 28)
        Me.chkb_video_loop.Name = "chkb_video_loop"
        Me.chkb_video_loop.Size = New System.Drawing.Size(86, 19)
        Me.chkb_video_loop.TabIndex = 0
        Me.chkb_video_loop.Text = "Loop videos"
        Me.chkb_video_loop.UseVisualStyleBackColor = True
        '
        'lbl_Picture_at_Panel_Size
        '
        Me.lbl_Picture_at_Panel_Size.AutoSize = True
        Me.lbl_Picture_at_Panel_Size.Location = New System.Drawing.Point(16, 24)
        Me.lbl_Picture_at_Panel_Size.Name = "lbl_Picture_at_Panel_Size"
        Me.lbl_Picture_at_Panel_Size.Size = New System.Drawing.Size(110, 15)
        Me.lbl_Picture_at_Panel_Size.TabIndex = 0
        Me.lbl_Picture_at_Panel_Size.Text = "Picture at Panel size:"
        '
        'cmb_Picture_Size
        '
        Me.cmb_Picture_Size.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.cmb_Picture_Size.FormattingEnabled = True
        Me.cmb_Picture_Size.Location = New System.Drawing.Point(250, 20)
        Me.cmb_Picture_Size.Name = "cmb_Picture_Size"
        Me.cmb_Picture_Size.Size = New System.Drawing.Size(180, 23)
        Me.cmb_Picture_Size.TabIndex = 1
        '
        'chkbox_Copy_Mode
        '
        Me.chkbox_Copy_Mode.AutoSize = True
        Me.chkbox_Copy_Mode.Location = New System.Drawing.Point(16, 28)
        Me.chkbox_Copy_Mode.Name = "chkbox_Copy_Mode"
        Me.chkbox_Copy_Mode.Size = New System.Drawing.Size(88, 19)
        Me.chkbox_Copy_Mode.TabIndex = 0
        Me.chkbox_Copy_Mode.Text = "Copy mode"
        Me.chkbox_Copy_Mode.UseVisualStyleBackColor = True
        '
        'chkbox_Independent_Thread_For_File_Operation
        '
        Me.chkbox_Independent_Thread_For_File_Operation.AutoSize = True
        Me.chkbox_Independent_Thread_For_File_Operation.Location = New System.Drawing.Point(16, 62)
        Me.chkbox_Independent_Thread_For_File_Operation.Name = "chkbox_Independent_Thread_For_File_Operation"
        Me.chkbox_Independent_Thread_For_File_Operation.Size = New System.Drawing.Size(300, 19)
        Me.chkbox_Independent_Thread_For_File_Operation.TabIndex = 1
        Me.chkbox_Independent_Thread_For_File_Operation.Text = "Use independent thread for operations with files"
        Me.chkbox_Independent_Thread_For_File_Operation.UseVisualStyleBackColor = True
        '
        'chkb_no_request_before_file_operation
        '
        Me.chkb_no_request_before_file_operation.AutoSize = True
        Me.chkb_no_request_before_file_operation.Location = New System.Drawing.Point(16, 96)
        Me.chkb_no_request_before_file_operation.Name = "chkb_no_request_before_file_operation"
        Me.chkb_no_request_before_file_operation.Size = New System.Drawing.Size(260, 19)
        Me.chkb_no_request_before_file_operation.TabIndex = 2
        Me.chkb_no_request_before_file_operation.Text = "Do not request user before file operations"
        Me.chkb_no_request_before_file_operation.UseVisualStyleBackColor = True
        '
        'btn_Set_As_Default
        '
        Me.btn_Set_As_Default.Location = New System.Drawing.Point(16, 28)
        Me.btn_Set_As_Default.Name = "btn_Set_As_Default"
        Me.btn_Set_As_Default.Size = New System.Drawing.Size(624, 34)
        Me.btn_Set_As_Default.TabIndex = 0
        Me.btn_Set_As_Default.Text = "Set as default image viewer"
        Me.btn_Set_As_Default.UseVisualStyleBackColor = True
        '
        'btn_Set_As_Default_Video
        '
        Me.btn_Set_As_Default_Video.Location = New System.Drawing.Point(16, 68)
        Me.btn_Set_As_Default_Video.Name = "btn_Set_As_Default_Video"
        Me.btn_Set_As_Default_Video.Size = New System.Drawing.Size(624, 34)
        Me.btn_Set_As_Default_Video.TabIndex = 1
        Me.btn_Set_As_Default_Video.Text = "Register as default video player"
        Me.btn_Set_As_Default_Video.UseVisualStyleBackColor = True
        '
        'btn_OcrTranslate
        '
        Me.btn_OcrTranslate.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Bold)
        Me.btn_OcrTranslate.Location = New System.Drawing.Point(16, 108)
        Me.btn_OcrTranslate.Name = "btn_OcrTranslate"
        Me.btn_OcrTranslate.Size = New System.Drawing.Size(624, 34)
        Me.btn_OcrTranslate.TabIndex = 2
        Me.btn_OcrTranslate.Text = "OCR & Translate"
        Me.btn_OcrTranslate.UseVisualStyleBackColor = True
        '
        'SetOnTop
        '
        Me.SetOnTop.AutoSize = True
        Me.SetOnTop.Location = New System.Drawing.Point(16, 30)
        Me.SetOnTop.Name = "SetOnTop"
        Me.SetOnTop.Size = New System.Drawing.Size(140, 19)
        Me.SetOnTop.TabIndex = 0
        Me.SetOnTop.Text = "Keep this window on top"
        Me.SetOnTop.UseVisualStyleBackColor = True
        '
        'btn_Language
        '
        Me.btn_Language.Location = New System.Drawing.Point(16, 24)
        Me.btn_Language.Name = "btn_Language"
        Me.btn_Language.Size = New System.Drawing.Size(90, 34)
        Me.btn_Language.TabIndex = 0
        Me.btn_Language.Text = "RU"
        Me.btn_Language.UseVisualStyleBackColor = True
        '
        'LinkLabel1
        '
        Me.LinkLabel1.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.LinkLabel1.AutoSize = True
        Me.LinkLabel1.Location = New System.Drawing.Point(12, 440)
        Me.LinkLabel1.Name = "LinkLabel1"
        Me.LinkLabel1.Size = New System.Drawing.Size(57, 15)
        Me.LinkLabel1.TabIndex = 1
        Me.LinkLabel1.TabStop = True
        Me.LinkLabel1.Text = "InfoLabel"
        '
        'Table_Form
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(7.0!, 15.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(204, Byte))
        Me.ClientSize = New System.Drawing.Size(700, 466)
        Me.Controls.Add(Me.LinkLabel1)
        Me.Controls.Add(Me.Tab_Control)
        Me.KeyPreview = True
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.MinimumSize = New System.Drawing.Size(560, 420)
        Me.Name = "Table_Form"
        Me.ShowIcon = False
        Me.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
        Me.Text = "Settings"
        CType(Me.Data_Grid_View, System.ComponentModel.ISupportInitialize).EndInit()
        Me.Tab_Control.ResumeLayout(False)
        Me.Tab_Page_1.ResumeLayout(False)
        Me.Tab_Page_1.PerformLayout()
        Me.Tab_Page_2.ResumeLayout(False)
        Me.Tab_Page_3.ResumeLayout(False)
        Me.Tab_Page_4.ResumeLayout(False)
        Me.grp_Background.ResumeLayout(False)
        Me.grp_Background.PerformLayout()
        Me.grp_OnScreen.ResumeLayout(False)
        Me.grp_OnScreen.PerformLayout()
        Me.grp_Slideshow.ResumeLayout(False)
        Me.grp_Slideshow.PerformLayout()
        Me.grp_Panel.ResumeLayout(False)
        Me.grp_Panel.PerformLayout()
        Me.grp_Quality.ResumeLayout(False)
        Me.grp_Quality.PerformLayout()
        Me.grp_Video.ResumeLayout(False)
        Me.grp_Video.PerformLayout()
        Me.grp_FileOps.ResumeLayout(False)
        Me.grp_FileOps.PerformLayout()
        Me.grp_Integration.ResumeLayout(False)
        Me.grp_Window.ResumeLayout(False)
        Me.grp_Window.PerformLayout()
        Me.grp_Language.ResumeLayout(False)
        CType(Me.num_Slideshow_Interval, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.num_Video_Volume, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub
    Friend WithEvents Data_Grid_View As System.Windows.Forms.DataGridView
    Friend WithEvents lbl_Grid_Hint As Label
    Friend WithEvents SetOnTop As System.Windows.Forms.CheckBox
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
    Friend WithEvents Tab_Page_3 As TabPage
    Friend WithEvents Tab_Page_4 As TabPage
    Friend WithEvents grp_Background As GroupBox
    Friend WithEvents grp_OnScreen As GroupBox
    Friend WithEvents grp_Slideshow As GroupBox
    Friend WithEvents grp_Panel As GroupBox
    Friend WithEvents grp_Quality As GroupBox
    Friend WithEvents grp_Video As GroupBox
    Friend WithEvents grp_FileOps As GroupBox
    Friend WithEvents grp_Integration As GroupBox
    Friend WithEvents grp_Window As GroupBox
    Friend WithEvents grp_Language As GroupBox
    Friend WithEvents chkb_show_pic_size As CheckBox
    Friend WithEvents chkb_is_to_show_file_datetime As CheckBox
    Friend WithEvents chkb_show_file_size As CheckBox
    Friend WithEvents chkb_video_loop As CheckBox
    Friend WithEvents chkb_no_request_before_file_operation As CheckBox
    Friend WithEvents chk_Exif_AutoRotate As CheckBox
    Friend WithEvents chk_Hq_Scaling As CheckBox
    Friend WithEvents chk_Show_Info_Overlay As CheckBox
    Friend WithEvents chk_Video_Mute As CheckBox
    Friend WithEvents lbl_Picture_at_Panel_Size As Label
    Friend WithEvents cmb_Picture_Size As ComboBox
    Friend WithEvents lbl_Slideshow_Interval As Label
    Friend WithEvents num_Slideshow_Interval As NumericUpDown
    Friend WithEvents lbl_Video_Volume As Label
    Friend WithEvents num_Video_Volume As NumericUpDown
    Friend WithEvents btn_Language As Button
    Friend WithEvents LinkLabel1 As LinkLabel
    Friend WithEvents btn_Set_As_Default As Button
    Friend WithEvents btn_Set_As_Default_Video As Button
    Friend WithEvents btn_OcrTranslate As Button
End Class
