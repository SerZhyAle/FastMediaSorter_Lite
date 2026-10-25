#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq

' Ticket 263 T-2 (199 section 3.1: "primary monitor or selected window"): the window route of the screen
' source, the pure half. The Share Manager enumerates the top-level windows with plain Win32 calls
' (LiveWindowEnumerator) and hands over one LiveWindowCandidate per window; THIS class decides which of
' them the picker offers, how each is labelled, and how a remembered choice is found again. No device,
' no Win32, no process: the suite drives it with hand-made candidates.
'
' A window handle is not stable across runs (nor across the window's own restarts), so the product
' remembers WHAT the person picked - the title and the name of the process - and looks the window up
' again when the broadcast starts. If nothing matches, the start is refused with one sentence; it never
' falls back to a monitor (what is on air must be what the person chose).

''' <summary>One top-level window as Win32 reported it. Plain facts, no judgement.</summary>
Friend Class LiveWindowCandidate
    Public Property Handle As IntPtr
    Public Property Title As String = ""
    ''' <summary>The process name without ".exe" ("" when Windows would not tell).</summary>
    Public Property ProcessName As String = ""
    Public Property ProcessId As Integer
    Public Property ClassName As String = ""
    Public Property IsVisible As Boolean = True
    ''' <summary>DWMWA_CLOAKED: a window the shell hides on purpose (a suspended app, another virtual
    ''' desktop) is "visible" to IsWindowVisible and must not be offered.</summary>
    Public Property IsCloaked As Boolean
    Public Property IsToolWindow As Boolean
    Public Property IsMinimized As Boolean
End Class

''' <summary>One row of the window picker.</summary>
Friend Class LiveWindowChoice
    Public Property Handle As IntPtr
    Public Property Title As String = ""
    Public Property ProcessName As String = ""
    Public Property IsMinimized As Boolean

    ''' <summary>"Title - process name", what the picker shows.</summary>
    Public ReadOnly Property Label As String
        Get
            Return LiveWindowCatalog.LabelOf(Title, ProcessName)
        End Get
    End Property

    ''' <summary>The identity the product remembers and looks up again: title (exact) and process name
    ''' (case-insensitive).</summary>
    Public ReadOnly Property Key As String
        Get
            Return LiveWindowCatalog.KeyOf(Title, ProcessName)
        End Get
    End Property
End Class

Friend NotInheritable Class LiveWindowCatalog

    ''' <summary>Start refusal reason code: the remembered window is not open any more, or it is minimised
    ''' (a minimised window has no picture to size the stream by). The window turns it into one sentence.</summary>
    Public Const ReasonUnavailable As String = "window-unavailable"

    ''' <summary>End reason code: the captured window was closed (the capture item's Closed event).</summary>
    Public Const ReasonClosed As String = "window-closed"

    ''' <summary>The shell's own surfaces: they have titles ("Program Manager") but are not something a
    ''' person means by "a window".</summary>
    Private Shared ReadOnly ShellClasses As String() = {"Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"}

    Private Sub New()
    End Sub

    Public Shared Function LabelOf(title As String, processName As String) As String
        If String.IsNullOrEmpty(processName) Then Return If(title, "")
        Return String.Concat(If(title, ""), " - ", processName)
    End Function

    Public Shared Function KeyOf(title As String, processName As String) As String
        Return String.Concat(If(title, ""), ChrW(0), If(processName, "").ToLowerInvariant())
    End Function

    ''' <summary>The rules of the picker: visible, not cloaked, not the Share Manager's own process, a real
    ''' title (an empty or blank title is skipped - this also drops every tool window without a title),
    ''' not a shell surface. Minimised windows stay in the list (the person may restore one before Start).</summary>
    Public Shared Function Qualifies(candidate As LiveWindowCandidate, ownProcessId As Integer) As Boolean
        If candidate Is Nothing Then Return False
        If Not candidate.IsVisible Then Return False
        If candidate.IsCloaked Then Return False
        If ownProcessId > 0 AndAlso candidate.ProcessId = ownProcessId Then Return False
        If String.IsNullOrWhiteSpace(candidate.Title) Then Return False
        For Each shellClass As String In ShellClasses
            If String.Equals(candidate.ClassName, shellClass, StringComparison.Ordinal) Then Return False
        Next
        Return True
    End Function

    ''' <summary>The picker's rows: the qualifying candidates, one row per (title, process) - the lookup is
    ''' by that identity, so a second identical row would be unreachable - ordered by title, then process.
    ''' The first of equal windows in the enumeration order (the topmost) is the one kept.</summary>
    Public Shared Function Pick(candidates As IEnumerable(Of LiveWindowCandidate), ownProcessId As Integer) As List(Of LiveWindowChoice)
        Dim rows As New List(Of LiveWindowChoice)()
        If candidates Is Nothing Then Return rows
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        For Each candidate As LiveWindowCandidate In candidates
            If Not Qualifies(candidate, ownProcessId) Then Continue For
            Dim row As New LiveWindowChoice With {
                .Handle = candidate.Handle,
                .Title = candidate.Title,
                .ProcessName = If(candidate.ProcessName, ""),
                .IsMinimized = candidate.IsMinimized
            }
            If seen.Add(row.Key) Then rows.Add(row)
        Next
        Return rows.OrderBy(Function(row) row.Title, StringComparer.OrdinalIgnoreCase).
                    ThenBy(Function(row) row.ProcessName, StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    ''' <summary>Finds the remembered window again: the exact title and the process name (case-insensitive).
    ''' Nothing when it is not open - the caller refuses the start, it never picks something else.</summary>
    Public Shared Function Find(choices As IEnumerable(Of LiveWindowChoice), title As String, processName As String) As LiveWindowChoice
        If choices Is Nothing OrElse String.IsNullOrEmpty(title) Then Return Nothing
        Dim key As String = KeyOf(title, processName)
        For Each choice As LiveWindowChoice In choices
            If String.Equals(choice.Key, key, StringComparison.Ordinal) Then Return choice
        Next
        Return Nothing
    End Function

End Class
#End If
