Option Strict On

Imports System.Net
Imports System.Net.Sockets

''' <summary>
''' Finds a port that is free right now, for the one button that needs it: the share port
''' is a guaranteed setting, so when the chosen number turns out to be taken the user has to
''' pick another - and "pick another" should not mean guessing.
'''
''' Deliberately NOT an OS-assigned port (a <c>TcpListener</c> on 0 would be one line): the
''' OS hands out numbers from the dynamic range above 49152, which is the range every
''' outgoing connection on the machine also draws from, so it is the worst possible home for
''' a number that has to hold for months. Candidates come from the registered range instead,
''' which is what the UI hint recommends.
'''
''' The answer is advisory by nature - anything can take the port between this probe and the
''' worker's bind - and that is fine: the worker is authoritative, retries, and says so when
''' it fails. This only has to beat guessing.
''' </summary>
Public Module FreePortFinder

    ''' <summary>Where candidates are drawn from. Comfortably inside the registered range
    ''' (1024-49151), above the crowded low end where databases, dev servers and printer
    ''' software live.</summary>
    Public Const CandidateFloor As Integer = 20000
    Public Const CandidateCeiling As Integer = 45000

    Private ReadOnly Rng As New Random()

    ''' <summary>
    ''' A free port in the registered range, or <see cref="ShareSettings.UnsetPort"/> when
    ''' several tries all collided (effectively never - it would take a machine with tens of
    ''' thousands of listeners). <paramref name="avoid"/> is skipped even if free: it is the
    ''' number the user is trying to get away from.
    ''' </summary>
    Public Function FindFree(Optional avoid As Integer = 0, Optional attempts As Integer = 40) As Integer
        For i As Integer = 1 To Math.Max(1, attempts)
            Dim candidate As Integer
            SyncLock Rng
                candidate = Rng.Next(CandidateFloor, CandidateCeiling + 1)
            End SyncLock
            If candidate = avoid Then Continue For
            If IsFree(candidate) Then Return candidate
        Next
        Return ShareSettings.UnsetPort
    End Function

    ''' <summary>
    ''' Whether a listener can be opened on the port right now. Bound on the SAME wildcard
    ''' address the worker uses (<c>IPAddress.IPv6Any</c> with dual-stack, i.e. "all
    ''' interfaces"): probing 127.0.0.1 instead would call a port free that another program
    ''' holds on every other address, which is the usual way this kind of check lies.
    ''' Failure of any kind means "not free" - including the Hyper-V/WSL/Docker excluded
    ''' ranges, where the bind is refused with nothing listening at all.
    ''' </summary>
    Public Function IsFree(port As Integer) As Boolean
        If port < ShareSettings.MinFixedPort OrElse port > ShareSettings.MaxFixedPort Then Return False
        Dim probe As Socket = Nothing
        Try
            probe = New Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp)
            probe.DualMode = True
            ' No ExclusiveAddressUse=False and no ReuseAddress: on Windows that option lets
            ' one process bind over another's live listener, so a probe using it would
            ' report a port free precisely when it is not.
            probe.Bind(New IPEndPoint(IPAddress.IPv6Any, port))
            Return True
        Catch
            Return False
        Finally
            If probe IsNot Nothing Then probe.Dispose()
        End Try
    End Function

End Module
