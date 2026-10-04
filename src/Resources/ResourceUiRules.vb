#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' The pure decisions behind the resource UI's shared pieces (ticket 118): the toolbar
''' button's visibility is a function of the registry count and the archive state, nothing
''' else - so "no resources, no button" is one tested rule rather than a condition repeated
''' at every trigger. The triggers themselves (registry writes, archive enter/leave, the
''' settings window closing) live in the forms; they only ever call here.
''' </summary>
Friend Module ResourceUiRules

    ''' <summary>The «Ресурс» toolbar button exists only while at least one resource is registered
    ''' and no archive is open. Hidden, never a disabled stub.</summary>
    Friend Function ResourceButtonShouldShow(registeredCount As Integer, archiveMode As Boolean) As Boolean
        Return registeredCount > 0 AndAlso Not archiveMode
    End Function

End Module
#End If
