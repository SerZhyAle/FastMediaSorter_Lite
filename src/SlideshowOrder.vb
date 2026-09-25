Option Strict On

Imports System.Collections.Generic

''' <summary>
''' "Which file does the slideshow show next", as pure arithmetic (audit 048, A-04).
'''
''' The shuffle-without-repeats order used to be honoured only when the slideshow STARTED
''' and by the "random file" button; every timer tick went through a separate branch that
''' always drew plain random with repeats - the same file twice in a row included - so the
''' option the user picked did nothing after the first slide. One function for every caller
''' is what keeps the two from drifting apart again.
'''
''' Both builds compile it; only the mainline ever asks for the shuffle cycle (the setting
''' is a .NET 10 preference), so the x86 viewer keeps its plain random.
''' </summary>
Public NotInheritable Class ShuffleCycle

    Private queue As New Queue(Of Integer)()
    Private cycle_Total As Integer

    ''' <summary>Forgets the current cycle - a new list starts a new one.</summary>
    Public Sub Reset()
        queue.Clear()
        cycle_Total = 0
    End Sub

    ''' <summary>
    ''' The next index of a cycle that visits every index of a list of <paramref name="total"/>
    ''' exactly once before any repeats. A new cycle never starts with
    ''' <paramref name="current"/>, so the seam between two cycles never shows the same file
    ''' twice in a row. A changed list size starts a new cycle.
    ''' </summary>
    Public Function NextIndex(total As Integer, current As Integer, rng As Random) As Integer
        If total <= 1 Then Return 0
        If cycle_Total <> total OrElse queue.Count = 0 Then
            Dim values As New List(Of Integer)(total)
            For i As Integer = 0 To total - 1
                If i <> current Then values.Add(i)
            Next
            For i As Integer = values.Count - 1 To 1 Step -1
                Dim j As Integer = rng.Next(i + 1)
                Dim swap As Integer = values(i)
                values(i) = values(j)
                values(j) = swap
            Next
            If values.Count = 0 Then values.Add(0)
            queue = New Queue(Of Integer)(values)
            cycle_Total = total
        End If
        Return queue.Dequeue()
    End Function

End Class

Public Module SlideshowOrder

    ''' <summary>
    ''' The index the slideshow moves to from <paramref name="current"/>.
    ''' Sequential wraps round to the first file; random draws with repeats; the shuffle
    ''' cycle draws without repeats (<see cref="ShuffleCycle"/>).
    ''' </summary>
    Public Function NextIndex(is_Random As Boolean, is_Shuffle_Cycle As Boolean,
                              total As Integer, current As Integer,
                              rng As Random, cycle As ShuffleCycle) As Integer
        If total <= 0 Then Return 0
        If Not is_Random Then
            Dim following As Integer = current + 1
            If following < 0 OrElse following > total - 1 Then following = 0
            Return following
        End If
        If is_Shuffle_Cycle AndAlso cycle IsNot Nothing Then Return cycle.NextIndex(total, current, rng)
        Return rng.Next(0, total)
    End Function

End Module
