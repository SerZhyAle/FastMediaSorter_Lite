#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

' <summary>
' The decisions of the welcome wizard (spec 184), kept pure: when it runs, which steps it shows and what the
' code defaults are. Nothing here reads the registry or a window - the viewer passes the facts in - so the rules
' that protect a user's file (never between a double-click and its picture, never on an upgrade, every step
' skippable at the code defaults) are provable in a unit test rather than by hand on a fresh VM.
' Spec 211 adds the second pass's pure half: the invitation button's eligibility and the where-pairing that
' ties every step's "Где это менять" text to a real settings row.
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

''' <summary>The server components the finish screen can name (spec 215 section 4), in the order their rows appear.</summary>
Friend Enum WelcomeServer
    ''' <summary>The Android Folder Share server, driven by the Share Manager's worker.</summary>
    ShareServer = 0
    ''' <summary>The Media Exchange Server, administered inside the Share Manager.</summary>
    ExchangeServer = 1
End Enum

''' <summary>What the status pill of a server row says (spec 215 section 5). Absent draws no row and no pill.</summary>
Friend Enum ServerPill
    Absent = 0
    ''' <summary>Installed and its process is up.</summary>
    Green = 1
    ''' <summary>Installed and not known to be up. Unknown lands here, never on green.</summary>
    Amber = 2
End Enum

''' <summary>What a row's button does (spec 215 section 6). Both are the same consent-gated wake of the Share Manager.</summary>
Friend Enum ServerAct
    ''' <summary>Wake the manager with no intent; its own first-run flows greet (212, 213).</summary>
    OpenManager = 0
    ''' <summary>Wake the manager with the add-resource intent; it opens the guided add flow (214).</summary>
    AddResource = 1
End Enum

''' <summary>One row of the installed-servers block: which server, what its pill says, what its button does.</summary>
Friend Structure WelcomeServerRow
    Friend ReadOnly Server As WelcomeServer
    Friend ReadOnly Pill As ServerPill
    Friend ReadOnly Act As ServerAct

    Friend Sub New(server As WelcomeServer, pill As ServerPill, act As ServerAct)
        Me.Server = server
        Me.Pill = pill
        Me.Act = act
    End Sub
End Structure

Friend Module WelcomeRules

    ''' <summary>Process names (no extension) whose existence is the "server is up" fact (spec 215 section 5): the share
    ''' server serves through the worker, the exchange server is its own exe. One name lookup each - nothing is started,
    ''' no handle is kept, no pipe or port is touched.</summary>
    Friend Const ShareWorkerProcessName As String = "fms-share-worker"
    Friend Const ExchangeServerProcessName As String = "fms-exchange-server"

    ''' <summary>
    ''' The server rows the finish screen carries (spec 215 sections 4-6), as a function of the facts the viewer read when
    ''' the wizard opened: a fixed order (the Android Folder Share server first, the Media Exchange Server second), an
    ''' absent component draws no row, and when none is installed the list is empty - the fresh viewer-only install's
    ''' finish screen is exactly 211's. <paramref name="shareUp"/> / <paramref name="exchangeUp"/> are Nothing when the
    ''' probe could not tell, which is amber and never green.
    ''' </summary>
    Friend Function ServerBlock(shareInstalled As Boolean, shareUp As Boolean?,
                                exchangeInstalled As Boolean, exchangeUp As Boolean?) As IReadOnlyList(Of WelcomeServerRow)
        Dim rows As New List(Of WelcomeServerRow)()
        Dim sharePill As ServerPill = StatusFor(shareInstalled, shareUp)
        If sharePill <> ServerPill.Absent Then rows.Add(New WelcomeServerRow(WelcomeServer.ShareServer, sharePill, ActFor(sharePill)))
        Dim exchangePill As ServerPill = StatusFor(exchangeInstalled, exchangeUp)
        If exchangePill <> ServerPill.Absent Then rows.Add(New WelcomeServerRow(WelcomeServer.ExchangeServer, exchangePill, ActFor(exchangePill)))
        Return rows
    End Function

    ''' <summary>The green fact (spec 215 section 5): installed AND the process is up. Anything short of a definite yes
    ''' is amber - a probe that could not tell never overclaims.</summary>
    Friend Function StatusFor(installed As Boolean, processUp As Boolean?) As ServerPill
        If Not installed Then Return ServerPill.Absent
        Return If(processUp.GetValueOrDefault(False), ServerPill.Green, ServerPill.Amber)
    End Function

    ''' <summary>The routing (spec 215 section 6): a green row leads on to adding a resource (214), any other opens the
    ''' manager and lets its own first-run flow lead to green.</summary>
    Friend Function ActFor(pill As ServerPill) As ServerAct
        Return If(pill = ServerPill.Green, ServerAct.AddResource, ServerAct.OpenManager)
    End Function

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
    ''' Whether the viewer's empty media surface may carry the "Первоначальная настройка" button (spec 211 §4.3): the
    ''' same eligibility as <see cref="ShouldRun"/> without the "no file argument" clause - the invitation exists
    ''' exactly for the launch that carried a file and so never sees the wizard. The host re-reads this whenever the
    ''' surface or the profile changes; an exit of the wizard writes <see cref="DoneValue"/>, which turns the answer
    ''' false and takes the button away.
    ''' </summary>
    Friend Function InviteOffered(welcomeDone As Boolean, welcomePending As Boolean, freshProfile As Boolean) As Boolean
        If welcomeDone Then Return False
        Return freshProfile OrElse welcomePending
    End Function

    ''' <summary>
    ''' The settings rows a step's "Где это менять" text names, by the placement catalog's own row ids
    ''' (<c>Table_Form.SettingsGroups.vb</c>). The gate test holds every id against the catalog the settings window
    ''' really builds, so a rework that moves or drops a row the wizard points at fails here before the text can lie
    ''' (spec 211 §5 rule 2). A surface that is not a settings row - the folder menu on W-3, the F1 key on W-6 - has
    ''' no id here; the where-sentence names it in words.
    ''' </summary>
    Friend Function WhereRowIds(wizardStep As WelcomeStep) As String()
        Select Case wizardStep
            Case WelcomeStep.Language : Return New String() {"interface_language"}
            Case WelcomeStep.Appearance : Return New String() {"background_color", "wheel_zooms"}
            Case WelcomeStep.Sorting : Return New String() {"advance_after_copy"}
            Case WelcomeStep.Delete : Return New String() {"delete_to_recycle_bin", "confirm_delete"}
            Case WelcomeStep.Associations : Return New String() {"image_associations", "video_associations"}
            Case WelcomeStep.Finish : Return New String() {"welcome_replay"}
            Case Else : Return Array.Empty(Of String)()
        End Select
    End Function

    ''' <summary>
    ''' The where-path of one row, as the settings window nests it: page caption, group caption, row caption - each
    ''' the very Localization key the window itself renders, so a rename in the window renames the wizard's pointer
    ''' with it (spec 211 §4.1: "exactly as the settings window renders it"). The gate test checks the row caption
    ''' against the window's own caption switch and the group caption against the placement catalog's declarations.
    ''' Always three entries; the window's own name ("Настройки") is prefixed by the form.
    ''' </summary>
    Friend Function WherePathKeys(rowId As String) As String()
        Select Case rowId
            Case "interface_language" : Return New String() {"Общие", "Основные настройки", "Язык интерфейса"}
            Case "background_color" : Return New String() {"Изображения", "Фон изображения", "Цвет фона"}
            Case "wheel_zooms" : Return New String() {"Изображения", "Основные настройки", "Масштабирование колёсиком"}
            Case "advance_after_copy" : Return New String() {"Файлы", "Основные настройки", "Переходить к следующему файлу после копирования"}
            Case "delete_to_recycle_bin" : Return New String() {"Файлы", "Основные настройки", "Удалять в Корзину"}
            Case "confirm_delete" : Return New String() {"Файлы", "Основные настройки", "Спрашивать перед удалением"}
            Case "image_associations" : Return New String() {"Файлы", "Клавиши и ассоциации", "Форматы изображений"}
            Case "video_associations" : Return New String() {"Файлы", "Клавиши и ассоциации", "Форматы видео"}
            Case "welcome_replay" : Return New String() {"Общие", "Запуск", "Показать приветственные шаги снова"}
            Case Else : Return Array.Empty(Of String)()
        End Select
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
