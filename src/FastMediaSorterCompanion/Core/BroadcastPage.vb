Option Strict On

''' <summary>
''' Whether the Share Manager offers the StreamsPlayer broadcasts page at all (spec 159).
'''
''' Two independent facts decide it and BOTH must hold: this build is one that offers the page
''' (<see cref="Offered"/>, compiled in), and the worker answering right now states it serves the broadcast
''' verbs (<see cref="BroadcastController.IsSupported"/>). Nothing is ever shown disabled: while either fact is
''' missing the tray item, the main window's button and every other entry are simply absent.
''' </summary>
Public Module BroadcastPage

    ''' <summary>
    ''' The build offers the page. Flipped to True on 2026-10-12, in the SAME change that re-vendored a worker
    ''' which serves the five broadcast verbs (196 phase 1, built in <c>fms_companion</c>: <c>internal/httpgw</c> +
    ''' <c>broadcasts.json</c>), so a host half never ships alone. It is a constant, not a setting, and
    ''' <c>BroadcastWorkerCapabilityTests</c> still fails the suite if the vendored binary and this flag ever
    ''' disagree - that test is what stops the host half shipping alone.
    ''' </summary>
    Public Const Offered As Boolean = True

    ''' <summary>The decision, with the build's answer passed in so a test can exercise both.</summary>
    Public Function ShouldOffer(status As WorkerStatus, offeredByBuild As Boolean) As Boolean
        Return offeredByBuild AndAlso BroadcastController.IsSupported(status)
    End Function

    ''' <summary>The decision production code asks: this build, and the last status the worker gave.</summary>
    Public Function ShouldOffer(status As WorkerStatus) As Boolean
        Return ShouldOffer(status, Offered)
    End Function

End Module
