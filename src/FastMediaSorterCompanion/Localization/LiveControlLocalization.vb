Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

' Capture resource identity while the old locale is still active. Never inspect or translate editor values.
Friend NotInheritable Class LiveControlLocalization
    Private ReadOnly updates As New List(Of Action)()
    Private ReadOnly form As Form
    Private ReadOnly keys As Dictionary(Of String, String)

    Friend Sub New(form As Form)
        Me.form = form
        keys = New Dictionary(Of String, String)(StringComparer.Ordinal)
        For Each entry In Localization.All
            Dim translated = Localization.T(entry.Key)
            If Not keys.ContainsKey(translated) Then keys.Add(translated, entry.Key)
        Next
        Capture(form)
    End Sub

    Private Sub Bind(text As String, apply As Action(Of String))
        Dim key As String = Nothing
        If keys.TryGetValue(text, key) Then updates.Add(Sub() apply(Localization.T(key)))
    End Sub

    Private Sub Capture(control As Control)
        If control.AccessibleName IsNot Nothing Then Bind(control.AccessibleName, Sub(text) control.AccessibleName = text)
        If control.AccessibleDescription IsNot Nothing Then Bind(control.AccessibleDescription, Sub(text) control.AccessibleDescription = text)
        For Each field In form.GetType().GetFields(Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic)
            If field.FieldType IsNot GetType(ToolTip) Then Continue For
            Dim tips = TryCast(field.GetValue(form), ToolTip)
            If tips IsNot Nothing Then Bind(tips.GetToolTip(control), Sub(text) tips.SetToolTip(control, text))
        Next
        If TypeOf control Is Label OrElse TypeOf control Is ButtonBase OrElse TypeOf control Is Form Then
            Bind(control.Text, Sub(text) control.Text = text)
        End If
        Dim section = TryCast(control, CollapsibleSection)
        If section IsNot Nothing Then Bind(section.Title, Sub(text) section.Title = text)
        Dim list = TryCast(control, ListView)
        If list IsNot Nothing Then
            For Each column As ColumnHeader In list.Columns
                Dim captured = column
                Bind(captured.Text, Sub(text) captured.Text = text)
            Next
        End If
        For Each child As Control In control.Controls
            Capture(child)
        Next
    End Sub

    Friend Sub Apply()
        If form.IsDisposed Then Return
        form.SuspendLayout()
        Try
            For Each update In updates
                update()
            Next
            UiLanguage.ApplyTo(form)
            For Each child In Children(form)
                If child.Font.FontFamily.Name <> form.Font.FontFamily.Name Then
                    Dim font As New Font(form.Font.FontFamily, child.Font.Size, child.Font.Style)
                    child.Font = font
                    AddHandler child.Disposed, Sub() font.Dispose()
                End If
            Next
        Finally
            form.ResumeLayout(True)
        End Try
    End Sub

    Private Shared Iterator Function Children(parent As Control) As IEnumerable(Of Control)
        For Each child As Control In parent.Controls
            Yield child
            For Each nested In Children(child)
                Yield nested
            Next
        Next
    End Function
End Class
