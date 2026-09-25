Option Strict On

Imports System.Security.Principal

''' <summary>
''' The rules of the WM_COPYDATA hand-off between the viewer's own processes - the one
''' design audit 053 asked for in place of three separate fixes (F-03, F-06, F-07).
'''
''' - Every forward carries <see cref="Tag"/> in dwData, and the receiver acts only on a
'''   message that carries it. WM_COPYDATA itself authenticates nothing, so without the
'''   tag any process that could reach the window made it open a path of its choosing.
''' - The receiver answers a handled forward with <see cref="Handled_Result"/>, and the
'''   sender counts a delivery only on that answer. A WinForms process owns helper
'''   windows (the GDI+ window, the broadcast window) that answer WM_COPYDATA with 0 at
'''   once; counting any successful send as a delivery cancelled the new launch while
'''   the file went nowhere.
''' - The UIPI message filter is relaxed only when the process is elevated, because only
'''   then is there a lower-integrity sender we want (a double-click in the ordinary
'''   Explorer). At medium integrity relaxing it admitted Low-integrity senders.
''' </summary>
Friend Module SingleInstanceProtocol

    ''' <summary>"FMSV". Arbitrary, fixed, and the same in both exes.</summary>
    Friend Const Tag As Integer = &H464D5356

    Friend Const Handled_Result As Integer = 1

    Friend ReadOnly Property Tag_Pointer As IntPtr
        Get
            Return New IntPtr(Tag)
        End Get
    End Property

    ''' <summary>True for a WM_COPYDATA that one of our own processes sent.</summary>
    Friend Function IsViewerCopyData(dwData As IntPtr) As Boolean
        Return dwData = Tag_Pointer
    End Function

    ''' <summary>A forward is delivered only when the send succeeded AND the window that
    ''' received it said it handled it - never on a bare successful send.</summary>
    Friend Function IsDelivered(sendSucceeded As Boolean, result As IntPtr) As Boolean
        Return sendSucceeded AndAlso result = New IntPtr(Handled_Result)
    End Function

    Private elevatedValue As Boolean?

    ''' <summary>Whether this process runs with an elevated (administrator) token. Under
    ''' UAC the filtered token carries Administrators as deny-only, so IsInRole answers
    ''' False there; it answers True only when the token really is elevated. Read once:
    ''' a token does not change elevation during its life.</summary>
    Friend Function IsProcessElevated() As Boolean
        If Not elevatedValue.HasValue Then
            Dim elevated As Boolean = False
            Try
                Using identity As WindowsIdentity = WindowsIdentity.GetCurrent()
                    elevated = New WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)
                End Using
            Catch ex As Exception
                AppFileLogger.LogException("Elevation check", ex)
            End Try
            elevatedValue = elevated
        End If
        Return elevatedValue.Value
    End Function

    ''' <summary>The filter is relaxed only for an elevated process (F-07). Kept as a
    ''' function of its input so the rule itself is testable.</summary>
    Friend Function ShouldAdmitLowerIntegrityMessages(isElevated As Boolean) As Boolean
        Return isElevated
    End Function

End Module
