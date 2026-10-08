#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

' <summary>
' The decisions of the welcome wizard (spec 184), kept pure: when it runs, which steps it shows and what the
' code defaults are. Nothing here reads the registry or a window - the viewer passes the facts in - so the rules
' that protect a user's file (never between a double-click and its picture, never on an upgrade, every step
' skippable at the code defaults) are provable in a unit test rather than by hand on a fresh VM.
' Modern build only, like the wizard itself.
' </summary>

''' <summary>The steps, in the order the user meets them (spec 184 section 4). The count is a hard cap.</summary>
Friend Enum WelcomeStep
    Language = 0
    Appearance = 1
    Sorting = 2
    [Delete] = 3
    Associations = 4
    Finish = 5
End Enum

Friend Module WelcomeRules

    ''' <summary>Recorded on every exit of the wizard (finish, skip, Esc, close), so the trigger cannot re-fire by accident.</summary>
    Friend Const DoneValue As String = "WelcomeDone"

    ''' <summary>
    ''' Set by a first launch that carried a file, which by spec 184 T-2 gets no wizard. The very next launch with no
    ''' file shows it. Without this value the close of that first session would write the hive, and the next launch
    ''' would no longer look like a fresh profile.
    ''' </summary>
    Friend Const PendingValue As String = "WelcomePending"

    ''' <summary>
    ''' The background scheme a fresh install really gets: Main_Form.Lifecycle reads "color_scheme" with the default "1",
    ''' which is Black (Utils.ColorSchemeFromSetting, the settings window's "Чёрный" entry). Spec 184 section 2 says
    ''' "dynamic"; the code is the authority on what the user gets today, and the wizard preselects this value.
    ''' </summary>
    Friend Const DefaultColorScheme As Integer = 1

    ''' <summary>The value the wheel reads when nothing is stored: flipping files, not zooming (Lifecycle "WheelZooms" "0").</summary>
    Friend Const DefaultWheelZooms As Boolean = False

    ''' <summary>
    ''' Whether the wizard runs on this launch. A launch with a file argument never gets it (T-2, the picture is the
    ''' user's intent). A launch with a stored DoneValue never gets it (T-5). Otherwise it runs for a fresh profile
    ''' (T-1, T-4) or for a deferred one (the pending value). An upgrade has values and no pending value, so it is
    ''' never shown (T-3).
    ''' </summary>
    Friend Function ShouldRun(welcomeDone As Boolean, welcomePending As Boolean, freshProfile As Boolean, hasFileArgument As Boolean) As Boolean
        If hasFileArgument OrElse welcomeDone Then Return False
        Return freshProfile OrElse welcomePending
    End Function

    ''' <summary>
    ''' Whether a launch must leave a note for the next one: a fresh profile started with a file. The note is written
    ''' straight away, because the same session's close is what would otherwise erase the evidence of freshness.
    ''' </summary>
    Friend Function DeferToNextLaunch(welcomeDone As Boolean, freshProfile As Boolean, hasFileArgument As Boolean) As Boolean
        Return hasFileArgument AndAlso freshProfile AndAlso Not welcomeDone
    End Function

    ''' <summary>
    ''' Whether a registry value counts as the VIEWER's own. A fresh profile is one where none of these exist. The Share
    ''' Manager writes its own "Share_" values into the same hive, and the wizard's own markers and the shared language
    ''' value do not say that the viewer has run before.
    ''' </summary>
    Friend Function IsViewerValue(name As String) As Boolean
        If String.IsNullOrEmpty(name) Then Return False
        Select Case name
            Case DoneValue, PendingValue, Localization.UiLanguageValue, Localization.LegacyRussianValue, "UiTheme"
                Return False
        End Select
        Return Not name.StartsWith("Share_", StringComparison.Ordinal)
    End Function

    ''' <summary>True when any of the names is one of the viewer's own values (see <see cref="IsViewerValue"/>).</summary>
    Friend Function HasViewerValues(names As IEnumerable(Of String)) As Boolean
        If names Is Nothing Then Return False
        For Each name As String In names
            If IsViewerValue(name) Then Return True
        Next
        Return False
    End Function

    ''' <summary>
    ''' The steps this build shows. The packaged (MSIX) build has no association step at all: the OS owns associations
    ''' there, and <see cref="Main_Form.IsPackagedApp"/> is the one place that knows it.
    ''' </summary>
    Friend Function StepsFor(isPackaged As Boolean) As IReadOnlyList(Of WelcomeStep)
        Dim steps As New List(Of WelcomeStep) From {WelcomeStep.Language, WelcomeStep.Appearance, WelcomeStep.Sorting, WelcomeStep.Delete}
        If Not isPackaged Then steps.Add(WelcomeStep.Associations)
        steps.Add(WelcomeStep.Finish)
        Return steps
    End Function

    ''' <summary>The index of the finish step inside <paramref name="steps"/>: the last one, always.</summary>
    Friend Function FinishIndex(steps As IReadOnlyList(Of WelcomeStep)) As Integer
        Return steps.Count - 1
    End Function

    ''' <summary>
    ''' Whether the chosen key-1 folder may be stored. An empty folder is allowed: it means "not bound", which is what
    ''' "Очистить" asks for. A folder that does not exist is refused: the picker only offers existing folders, but one
    ''' can vanish between the pick and Next (a network share), and a binding to nothing would fail on the first press.
    ''' </summary>
    Friend Function KeyOneMayBind(folder As String, folderExists As Boolean) As Boolean
        Return String.IsNullOrWhiteSpace(folder) OrElse folderExists
    End Function

End Module

#End If
