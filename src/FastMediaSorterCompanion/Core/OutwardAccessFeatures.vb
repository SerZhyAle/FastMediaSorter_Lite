Option Strict On

' Ticket 239, item CR-13 (owner decision Q239-2): the one switch every outward-access surface of the Share Manager reads.
'
' "Outward access" is what hands a folder to somebody who has no account of their own: the guest invitation (a guest kind of
' invitation, single-use or shared) and the link with a password. ANYWHERE-ACCESS amendment L is the contract this gate is held
' under; the reason the surfaces are closed is that the Share Manager's file server cannot yet limit a login to ONE folder, so
' a guest account or a link would be bound to a folder only in the words on the screen, not in what the server enforces.
' The engine stays in the tree and keeps its tests (ExchangePublisher link and guest methods, ExchangeSealedAccess): only the
' entry points ask this class, so nothing is created behind a closed surface and nothing is deleted from the engine.
'
' <see cref="Offered"/> is a compile-time constant and it is FALSE. A surface that is closed is SHOWN as not available with the
' one honest line of <see cref="Reason"/> (no date is promised); withdrawing what already exists stays possible, because taking
' access back is never the dangerous direction. OutwardAccessGateTests holds the constant (it flips only deliberately) and that
' every entry point reads this class.
'
' Mirrors ExchangeFeatures.Offered and LiveBroadcastFeatures.Offered, minus the developer switch: there is no way to open this
' gate from the environment on purpose.

Friend NotInheritable Class OutwardAccessFeatures

    Private Sub New()
    End Sub

    ''' <summary>FALSE: the guest invitation and the link with a password are closed (ticket 239, Q239-2; ANYWHERE-ACCESS amendment L).
    ''' The one line that opens them again is this constant, changed together with the test that pins it.</summary>
    Public Const Offered As Boolean = False

    ' Test seam, the same shape as ExchangeFeatures: the gate for the CURRENT flow of execution only, so a test that flips it
    ' cannot be seen by another test running in parallel. Friend: no program surface can set it.
    Private Shared ReadOnly _flowOffered As New System.Threading.AsyncLocal(Of Boolean?)()

    Private NotInheritable Class FlowScope
        Implements IDisposable

        Private ReadOnly _previous As Boolean?

        Public Sub New(previous As Boolean?)
            _previous = previous
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            _flowOffered.Value = _previous
        End Sub
    End Class

    ''' <summary>Test seam: the gate as <paramref name="offered"/> says, for this flow of execution, until the result is disposed.</summary>
    Friend Shared Function ForFlow(offered As Boolean) As IDisposable
        Dim scope As New FlowScope(_flowOffered.Value)
        _flowOffered.Value = offered
        Return scope
    End Function

    ''' <summary>True when the outward-access surfaces may create anything in this run.</summary>
    Friend Shared Function IsOffered() As Boolean
        If _flowOffered.Value.HasValue Then Return _flowOffered.Value.Value
        Return Offered
    End Function

    ''' <summary>The one line shown wherever a closed surface stands: honest about the cause, no date.</summary>
    Friend Shared Function Reason() As String
        Return Localization.T("Недоступно: файловый сервер программы пока не умеет ограничить вход одной папкой.")
    End Function

End Class
