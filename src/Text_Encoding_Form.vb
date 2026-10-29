#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Specification 277 section 6.2: Re-open with Encoding. The list offers <c>TextEncodingDetector.ManualCandidates</c>
''' by group, the preview shows the first five lines of the file read with the selected encoding and says how many
''' characters that encoding cannot read. Nothing is written to the file: the answer is only how to read it.
''' </summary>
Friend NotInheritable Class Text_Encoding_Form
    Inherits Form

    Private NotInheritable Class Choice
        Friend ReadOnly CodePage As Integer
        Private ReadOnly caption As String

        Friend Sub New(codePage As Integer, caption As String)
            Me.CodePage = codePage
            Me.caption = caption
        End Sub

        Public Overrides Function ToString() As String
            Return caption
        End Function
    End Class

    Private ReadOnly choices As New ListBox With {.Name = "enc_List", .Dock = DockStyle.Fill, .IntegralHeight = False}
    Private ReadOnly preview As New TextBox With {.Name = "enc_Preview", .Dock = DockStyle.Fill, .Multiline = True, .ReadOnly = True,
        .WordWrap = False, .ScrollBars = ScrollBars.Horizontal, .TabStop = False}
    Private ReadOnly verdict As New Label With {.Name = "enc_Verdict", .AutoSize = True, .Dock = DockStyle.Fill, .UseMnemonic = False}
    Private ReadOnly probe As Byte()
    Private ReadOnly probeLength As Integer
    Private ReadOnly probeTruncated As Boolean
    Private ReadOnly previewFont As New Font("Consolas", 10.0F)

    ''' <summary>The code page to read the file with, or 0 when the user asked for automatic detection again.</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property SelectedCodePage As Integer

    Friend Sub New(fileName As String, bytes As Byte(), length As Integer, isTruncated As Boolean, current As Integer, canDetect As Boolean)
        probe = If(bytes, New Byte() {})
        probeLength = length
        probeTruncated = isTruncated
        Text = Localization.T("Открыть в другой кодировке")
        StartPosition = FormStartPosition.CenterParent
        MinimumSize = New Size(460, 420)
        ClientSize = New Size(600, 460)
        FormBorderStyle = FormBorderStyle.Sizable
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False

        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 5, .Padding = New Padding(12)}
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 110.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.Controls.Add(New Label With {.Text = Localization.TF("Кодировка для файла {0}", fileName), .AutoSize = True,
            .UseMnemonic = False, .Dock = DockStyle.Fill})
        layout.Controls.Add(choices)
        layout.Controls.Add(New Label With {.Text = Localization.T("Начало файла в этой кодировке"), .AutoSize = True,
            .Dock = DockStyle.Fill, .Padding = New Padding(0, 8, 0, 0)})
        layout.Controls.Add(preview)
        layout.Controls.Add(verdict)
        preview.Font = previewFont

        For Each candidate As EncodingCandidate In TextEncodingDetector.ManualCandidates
            Dim item As New Choice(candidate.CodePage, Localization.TF("{0}: {1}", GroupName(TextEncodingChoice.GroupOf(candidate.CodePage)), candidate.Name))
            choices.Items.Add(item)
            If candidate.CodePage = current Then choices.SelectedItem = item
        Next
        If choices.SelectedIndex < 0 Then choices.SelectedIndex = 0
        AddHandler choices.SelectedIndexChanged, Sub() ShowPreview()
        AddHandler choices.DoubleClick, Sub() Accept()

        Dim answerRow As New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "enc_Buttons", .Padding = New Padding(12, 6, 12, 10)}
        If canDetect Then
            Dim detect As New Button With {.Name = "enc_Detect", .Text = Localization.T("Определять автоматически")}
            AddHandler detect.Click, Sub()
                                         SelectedCodePage = 0
                                         DialogResult = DialogResult.OK
                                     End Sub
            answerRow.Add(detect, DialogButtonRole.Command, "action.reset")
        End If
        Dim openButton As New Button With {.Name = "enc_Open", .Text = Localization.T("Открыть")}
        AddHandler openButton.Click, Sub() Accept()
        Dim cancelButton_ As New Button With {.Name = "enc_Cancel", .Text = Localization.T("Отмена"), .DialogResult = DialogResult.Cancel}
        answerRow.Add(openButton, DialogButtonRole.Affirmative, "action.confirm")
        answerRow.Add(cancelButton_, DialogButtonRole.Dismissing, "action.cancel")

        Controls.Add(layout)
        ' Last in the collection: it reads after the list in the Tab order, and the docked row takes its edge first.
        Controls.Add(answerRow)
        answerRow.Bind(Me)
        ShowPreview()

        ' LAST line of the builder: scaling touches only the children that exist by now.
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DialogLayout.FitContent(Me)
        choices.Focus()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        If disposing Then previewFont.Dispose()
    End Sub

    Private Sub Accept()
        Dim item As Choice = TryCast(choices.SelectedItem, Choice)
        If item Is Nothing Then Return
        SelectedCodePage = item.CodePage
        DialogResult = DialogResult.OK
    End Sub

    Private Sub ShowPreview()
        Dim item As Choice = TryCast(choices.SelectedItem, Choice)
        If item Is Nothing Then Return
        Dim result As TextEncodingPreview = TextEncodingChoice.Preview(probe, probeLength, probeTruncated, item.CodePage)
        preview.Text = String.Join(Environment.NewLine, result.Lines)
        verdict.Text = If(result.UnreadableCount = 0,
            Localization.T("Все знаки начала файла читаются в этой кодировке"),
            Localization.TF("Не читаются в этой кодировке: {0} знаков в начале файла", result.UnreadableCount.ToString()))
    End Sub

    Private Shared Function GroupName(group As TextEncodingGroup) As String
        Select Case group
            Case TextEncodingGroup.Unicode
                Return Localization.T("Юникод")
            Case TextEncodingGroup.Cyrillic
                Return Localization.T("Кириллица")
            Case TextEncodingGroup.Western
                Return Localization.T("Западноевропейские")
            Case Else
                Return Localization.T("Восточноазиатские")
        End Select
    End Function
End Class
#End If
