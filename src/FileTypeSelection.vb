#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

''' <summary>
''' What the "Show these file types" dialog may hand back (105 L05, 057.S15-7). Pure, so the
''' dialog's OK button and its result are decided by one function that the tests drive.
'''
''' The stored representation is deliberately unchanged: an EMPTY list means "all supported
''' formats" (§3.4). That is exactly why ticking nothing must not be accepted - it would write
''' the same empty list and silently turn "nothing" into "everything".
''' </summary>
Friend Module FileTypeSelection

    Friend NotInheritable Class Verdict
        ''' <summary>False for zero ticked types: OK stays disabled and Enter cannot bypass it.</summary>
        Friend ReadOnly Property CanAccept As Boolean
        ''' <summary>The extensions to store: empty = every supported format, otherwise the subset
        ''' in the order they were ticked.</summary>
        Friend ReadOnly Property Selection As List(Of String)

        Friend Sub New(canAccept As Boolean, selection As List(Of String))
            Me.CanAccept = canAccept
            Me.Selection = selection
        End Sub
    End Class

    Friend Function Evaluate(totalLeaves As Integer, checkedExtensions As IEnumerable(Of String)) As Verdict
        Dim chosen As New List(Of String)()
        If checkedExtensions IsNot Nothing Then
            For Each ext As String In checkedExtensions
                If Not String.IsNullOrEmpty(ext) Then chosen.Add(ext)
            Next
        End If
        If chosen.Count = 0 Then Return New Verdict(False, New List(Of String)())
        If chosen.Count >= totalLeaves Then Return New Verdict(True, New List(Of String)())
        Return New Verdict(True, chosen)
    End Function

    ''' <summary>The list to show as ticked when the dialog opens: the stored empty list is
    ''' "everything", a stored subset is exactly that subset.</summary>
    Friend Function TickedOnOpen(stored As IEnumerable(Of String), allExtensions As IEnumerable(Of String)) As HashSet(Of String)
        Dim storedSet As New HashSet(Of String)(If(stored, New List(Of String)()), StringComparer.OrdinalIgnoreCase)
        If storedSet.Count = 0 Then Return New HashSet(Of String)(allExtensions, StringComparer.OrdinalIgnoreCase)
        Return storedSet
    End Function

End Module
#End If
