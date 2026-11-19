Option Strict On
Option Explicit On
Option Infer On
Option Compare Binary

Imports System.Runtime.CompilerServices
Imports System.Windows.Forms

''' <summary>
''' Rule S2 of L0361 for <see cref="ListView"/> column widths. A column width is the one size the platform never rescales
''' (not by <c>AutoScaleMode.Dpi</c> and not on a DPI change), and a window built in its constructor resolves
''' <c>LogicalToDeviceUnits</c> at the system dpi, which is not the dpi of the monitor the window ends up on, and without the
''' window's M (<see cref="DpiLayout.ComposedFactorOf"/>, composed by <c>ApplyAutoScale</c> at the END of the builder).
''' A form <see cref="Track"/>s each list right after its columns are added (with the <c>DeviceDpi</c> the widths were
''' resolved at); the widths then follow the live window dpi and M - when the list gets its handle, from the form's
''' <c>OnLoad</c> and from its <c>OnDpiChanged</c> through <see cref="RescaleAll"/> - whatever they are by then (designed,
''' auto-sized or dragged by the user), and a list that is already at that scale is left alone.
''' </summary>
Friend NotInheritable Class ListColumnScale

    Private NotInheritable Class ScaleBox
        Friend Scale As Double

        Friend Sub New(scale As Double)
            Me.Scale = scale
        End Sub
    End Class

    Private Shared ReadOnly Resolved As New ConditionalWeakTable(Of ListView, ScaleBox)()

    Private Sub New()
    End Sub

    ''' <summary>Records that the column widths of <paramref name="list"/> are device pixels resolved by
    ''' <c>LogicalToDeviceUnits</c> at <paramref name="dpi"/> (no M yet), and rescales them when the list gets its handle.</summary>
    Friend Shared Sub Track(list As ListView, dpi As Integer)
        If list Is Nothing OrElse dpi <= 0 Then Return
        Dim existing As ScaleBox = Nothing
        Dim first As Boolean = Not Resolved.TryGetValue(list, existing)
        Resolved.AddOrUpdate(list, New ScaleBox(dpi / UiSizing.LogicalDpi))
        If first Then AddHandler list.HandleCreated, AddressOf OnListHandleCreated
    End Sub

    Private Shared Sub OnListHandleCreated(sender As Object, e As EventArgs)
        Dim list As ListView = TryCast(sender, ListView)
        If list Is Nothing Then Return
        Dim window As Form = list.FindForm()
        Rescale(list, If(window IsNot Nothing, window.DeviceDpi, list.DeviceDpi))
    End Sub

    ''' <summary>Rescales every tracked list at or under <paramref name="root"/> to <paramref name="dpi"/> and the window's M.</summary>
    Friend Shared Sub RescaleAll(root As Control, dpi As Integer)
        If root Is Nothing OrElse dpi <= 0 Then Return
        Dim list As ListView = TryCast(root, ListView)
        If list IsNot Nothing Then Rescale(list, dpi)
        For Each child As Control In root.Controls
            RescaleAll(child, dpi)
        Next
    End Sub

    Friend Shared Sub Rescale(list As ListView, dpi As Integer)
        Dim box As ScaleBox = Nothing
        If dpi <= 0 OrElse Not Resolved.TryGetValue(list, box) Then Return
        Dim target As Double = dpi / UiSizing.LogicalDpi * CDbl(DpiLayout.ComposedFactorOf(list))
        If Math.Abs(target - box.Scale) < 0.0005 Then Return
        Dim previous As Double = box.Scale
        box.Scale = target
        list.BeginUpdate()
        Try
            For Each column As ColumnHeader In list.Columns
                Dim scaled As Integer = ScaledWidth(column.Width, previous, target)
                If scaled <> column.Width Then column.Width = scaled
            Next
        Finally
            list.EndUpdate()
        End Try
    End Sub

    ''' <summary>A width resolved at the scale <paramref name="fromScale"/> as it is at <paramref name="toScale"/> (pure;
    ''' a scale is dpi / 96 times M).</summary>
    Friend Shared Function ScaledWidth(width As Integer, fromScale As Double, toScale As Double) As Integer
        If width <= 0 OrElse fromScale <= 0.0 OrElse toScale <= 0.0 Then Return width
        Return CInt(Math.Round(width * toScale / fromScale, MidpointRounding.AwayFromZero))
    End Function

End Class
