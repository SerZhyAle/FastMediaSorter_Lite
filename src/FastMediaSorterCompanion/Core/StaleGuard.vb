Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Threading

''' <summary>
''' Supersedes obsolete asynchronous work (SETTINGS-NAVIGATION 2.0 section 5 rule 3: "rapid navigation MUST cancel or
''' supersede obsolete page work so an old result cannot replace the current destination"). One instance per KIND of load
''' that repaints a page (the hosting probe, the exchange state, the accounts read ...): the load takes a token with
''' <see cref="Begin"/> before it awaits anything, and when the work returns it paints only if
''' <see cref="IsCurrent"/> still says the token is the newest. A newer request of the same kind, or an
''' <see cref="Invalidate"/> (the window was disposed, the thing the result described is gone), makes every older token
''' stale. Pure and thread-safe: a pool thread may call any member while the UI thread calls another.
'''
''' This is for LOADS that repaint a page. A user-initiated one-shot action (Apply, Add account, import a certificate)
''' runs to completion and reports; it does not take a token.
''' </summary>
Friend NotInheritable Class StaleGuard

    ' A Long never wraps in practice, and 0 is the generation of a guard nobody has begun: no token is ever 0.
    Private _generation As Long

    ''' <summary>Starts a new request: every earlier token becomes stale. The returned token is current until the next
    ''' <see cref="Begin"/> or <see cref="Invalidate"/>.</summary>
    Friend Function Begin() As Long
        Return Interlocked.Increment(_generation)
    End Function

    ''' <summary>True when nothing has superseded or invalidated the request that returned this token. A token of 0
    ''' (never issued) is never current.</summary>
    Friend Function IsCurrent(token As Long) As Boolean
        Return token <> 0L AndAlso Interlocked.Read(_generation) = token
    End Function

    ''' <summary>Makes every outstanding token stale without starting a request (the window is closing, or the state the
    ''' pending results describe is gone).</summary>
    Friend Sub Invalidate()
        Interlocked.Increment(_generation)
    End Sub

    ''' <summary>The generation now: 0 until the first <see cref="Begin"/> or <see cref="Invalidate"/>. For tests and logs.</summary>
    Friend ReadOnly Property Generation As Long
        Get
            Return Interlocked.Read(_generation)
        End Get
    End Property

End Class
