#If Not NETFRAMEWORK Then
Option Strict On

' The viewer's half of RecycleBinIo, split out in ticket 222 so the other half can be linked into the Share Manager.
' It goes through FileManager (resource routing), which only the viewer carries.

Partial Friend Module RecycleBinIo

    ''' <summary>
    ''' Deletes exactly the way the decision said it would. The one place where the
    ''' outcome the user was shown turns into an action - a second executor somewhere
    ''' else is how the two would start to disagree (invariant 1).
    '''
    ''' Returns True when the decision said "Recycle Bin" and the shell deleted the file for
    ''' good instead (audit 057.S05-2): the F3 panel's bulk delete used to believe the
    ''' prediction and report "to the Recycle Bin" for a file nobody can get back. The check
    ''' is the queue path's own (047 C-09), so the two surfaces cannot disagree about it.
    ''' </summary>
    Friend Function DeleteAs(path As String, decision As DeleteDecision) As Boolean
        If decision IsNot Nothing AndAlso decision.Outcome = DeleteOutcome.Recycle Then
            Return SendToBinChecked(path, DateTime.UtcNow)
        End If
        DeleteFile(path)
        Return False
    End Function

End Module
#End If
