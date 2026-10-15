#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Net.NetworkInformation
Imports System.Threading

''' <summary>
''' Tracks network change notifications from Windows (NetworkChange.NetworkAddressChanged and NetworkAvailabilityChanged).
''' Each network change increments the CurrentEpoch counter so any cached winner or in-flight channel state is invalidated,
''' and the next request races again from the top (spec 228 rule 2).
''' </summary>
Friend Module NetworkChangeTracker

    Private _epoch As Long = 1
    Private _listening As Boolean = False
    Private ReadOnly _lock As New Object()

    Friend Sub EnsureListening()
        SyncLock _lock
            If _listening Then Return
            Try
                AddHandler NetworkChange.NetworkAddressChanged, AddressOf OnNetworkChanged
                AddHandler NetworkChange.NetworkAvailabilityChanged, AddressOf OnNetworkChanged
                _listening = True
            Catch ex As Exception
                ' Running in test environment or restricted security context where network events cannot be hooked
            End Try
        End SyncLock
    End Sub

    Private Sub OnNetworkChanged(sender As Object, e As EventArgs)
        BumpEpoch()
    End Sub

    Friend Function CurrentEpoch() As Long
        Return Interlocked.Read(_epoch)
    End Function

    ''' <summary>Manually bump the epoch (used in tests to simulate a network change).</summary>
    Friend Sub BumpEpoch()
        Interlocked.Increment(_epoch)
    End Sub

    ''' <summary>Resets the epoch counter (used in tests).</summary>
    Friend Sub ResetForTesting()
        Interlocked.Exchange(_epoch, 1)
    End Sub

End Module
#End If
