#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Threading.Tasks

''' <summary>
''' The live half of 011_SPECIFICATION_SLOT_HEALTH_AND_HONEST_FAILURES_DOTNET10.md Ф2/Ф3: the
''' probe that asks a destination whether it is alive, the cache that means a dead slot is
''' asked once rather than once per keypress, and the sentences that name the reason.
'''
''' The rules it exists to keep (§7):
'''   4. the probe never runs on the UI thread and is bounded by Probe_Timeout_Ms;
'''   5. a dead slot costs at most one probe per Bad_Ttl_Seconds however often it is pressed;
'''   6. a refused operation is REFUSED - never redirected to a fallback destination;
'''   8. the refusal happens before anything is released, mutated or queued.
'''
''' The pure policy behind it - the TTLs, the retry window, "should this press pay for a
''' probe" - is SlotHealth.vb, so all of that is provable without a NAS.
''' </summary>
Partial Public Class Main_Form

    ''' <summary>Verdicts by destination PATH (D3). Two slots pointing at the same share
    ''' share one answer, and the overlay and the settings grid read this same table
    ''' instead of asking again.</summary>
    Private ReadOnly slot_Verdicts As New Dictionary(Of String, SlotVerdict)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Paths a probe is currently out on. Without it a burst of presses during
    ''' the two seconds a dead share takes to answer would start one probe each.</summary>
    Private ReadOnly slot_Probes_In_Flight As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>When each slot was last pressed, for the double-press retry (D4).</summary>
    Private ReadOnly slot_Last_Press_Utc As New Dictionary(Of Integer, DateTime)()

    ''' <summary>Set while the action is being re-entered by a probe's continuation, so the
    ''' re-entry is not counted as a second press (which would look like a retry and ask for
    ''' another probe, forever).</summary>
    Private slot_Health_Reentry As Boolean

    ''' <summary>Raised whenever a verdict lands, so the surfaces that only ever read the
    ''' CACHE - the recipients overlay and the settings grid - can restate themselves without
    ''' polling and without probing.</summary>
    Friend Event SlotHealthChanged As EventHandler

    ''' <summary>The cached answer for a path, or Nothing when none has been asked for.
    ''' Reading NEVER probes: opening the overlay must not be able to cost a timeout
    ''' (§3.5).</summary>
    Friend Function CachedSlotVerdict(destination As String) As SlotVerdict
        If String.IsNullOrWhiteSpace(destination) Then Return Nothing
        Dim verdict As SlotVerdict = Nothing
        SyncLock slot_Verdicts
            slot_Verdicts.TryGetValue(destination.Trim(), verdict)
        End SyncLock
        Return verdict
    End Function

    ''' <summary>The cached state for a slot index, for the surfaces that show it. Returns
    ''' Nothing when the slot has never been probed - which is NOT the same as healthy, and
    ''' the callers draw it as "no opinion" rather than as a fault.</summary>
    Friend Function CachedSlotStateForSlot(slot As Integer) As SlotVerdict
        If slot < 1 OrElse slot > 10 Then Return Nothing
        Return CachedSlotVerdict(Hardkeys_to_move_mediafile(slot))
    End Function

    Private Sub StoreSlotVerdict(destination As String, verdict As SlotVerdict)
        If String.IsNullOrWhiteSpace(destination) OrElse verdict Is Nothing Then Return
        SyncLock slot_Verdicts
            slot_Verdicts(destination.Trim()) = verdict
        End SyncLock
    End Sub

    ''' <summary>
    ''' The gate every recipient action passes (§3.4). Returns True when the action may go
    ''' ahead; False means it must not - either it was refused with a reason on the status
    ''' line, or a probe was started and the action will be re-entered when the answer lands.
    '''
    ''' Nothing has been released, mutated or queued by the time this is asked (invariant 8),
    ''' which is the whole point: a refusal costs the user a sentence, not twenty files
    ''' rolling back into the list minutes later.
    ''' </summary>
    Private Function SlotHealthAllowsAction(slot As Integer, action As RecipientActionKind,
                                            ByRef create_Destination As Boolean) As Boolean
        create_Destination = False

        Dim destination As String = Hardkeys_to_move_mediafile(slot)
        If String.IsNullOrWhiteSpace(destination) Then Return False
        Dim slot_Key As String = SlotKeyCaption(slot)

        Dim now_Utc As DateTime = DateTime.UtcNow
        Dim repeat_Press As Boolean = False
        If slot_Health_Reentry Then
            ' A continuation, not a press. It must neither count as a retry nor overwrite
            ' the press time the real presses are measured against.
            slot_Health_Reentry = False
        Else
            Dim last_Press As DateTime = DateTime.MinValue
            slot_Last_Press_Utc.TryGetValue(slot, last_Press)
            repeat_Press = SlotHealthPolicy.IsRepeatPress(now_Utc, last_Press)
            slot_Last_Press_Utc(slot) = now_Utc
        End If

        Dim verdict As SlotVerdict = CachedSlotVerdict(destination)
        If SlotHealthPolicy.ShouldProbe(now_Utc, verdict, repeat_Press) Then
            BeginSlotProbe(destination, slot, action)
            Return False
        End If

        If SlotHealthPolicy.IsUsable(verdict.State) Then
            create_Destination = (verdict.State = SlotState.WillBeCreated)
            Return True
        End If

        lbl_Status.Text = SlotHealthText(slot_Key, verdict.State)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1684: slot " & slot_Key & " refused: " &
                        verdict.State.ToString() & " (" & verdict.Detail & ")")
        Return False
    End Function

    ''' <summary>
    ''' Starts a probe and, when it answers, runs the action again with the fresh verdict in
    ''' hand. The second pass cannot probe again: the answer it reads is younger than the
    ''' retry window and the re-entry flag keeps it from being counted as a press.
    ''' </summary>
    Private Sub BeginSlotProbe(destination As String, slot As Integer, action As RecipientActionKind)
        Dim key As String = destination.Trim()

        SyncLock slot_Verdicts
            If slot_Probes_In_Flight.Contains(key) Then
                ' Already asked. Say so and stop - pressing again while the answer is on
                ' its way must not start a second probe (invariant 5).
                lbl_Status.Text = Localization.TF("проверяю каталог {0}..", SlotKeyCaption(slot))
                Return
            End If
            slot_Probes_In_Flight.Add(key)
        End SyncLock

        lbl_Status.Text = Localization.TF("проверяю каталог {0}..", SlotKeyCaption(slot))
        Dim allow_Create As Boolean = AutoCreateDestinationEnabled()

        ' The file the user was looking at when they pressed the key. A probe can take up to
        ' two seconds, and Space keeps working throughout - so without this the action would
        ' land on whatever happened to be on screen when the answer arrived, which is a file
        ' the user never chose. Sorting is exactly the loop where that is unforgivable.
        Dim pressed_On As String = Current_File_Name

        ProbeSlotThen(key, allow_Create,
                      Sub()
                          ' Back on the UI thread with the verdict already cached.
                          If Me.IsDisposed Then Return

                          If Not String.Equals(pressed_On, Current_File_Name, StringComparison.Ordinal) Then
                              ' The view moved on. Say what the answer was and stop: a bad
                              ' verdict is worth hearing either way, and a good one turns the
                              ' next press into an instant, cached move of the RIGHT file.
                              Dim answer As SlotVerdict = CachedSlotVerdict(destination)
                              If answer IsNot Nothing AndAlso Not SlotHealthPolicy.IsUsable(answer.State) Then
                                  lbl_Status.Text = SlotHealthText(SlotKeyCaption(slot), answer.State)
                              Else
                                  lbl_Status.Text = Localization.TF("каталог {0} доступен - нажмите клавишу ещё раз", SlotKeyCaption(slot))
                              End If
                              Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") &
                                              " w1686: slot probe answered after the file changed - action not repeated")
                              Return
                          End If

                          slot_Health_Reentry = True
                          Try
                              ExecuteRecipientAction(slot, action)
                          Finally
                              ' Unconditionally: an early return inside the action (no file,
                              ' archive mode) must not leave the flag set for the next press.
                              slot_Health_Reentry = False
                          End Try
                      End Sub)
    End Sub

    ''' <summary>
    ''' Probes one path off the UI thread, caches the answer, then runs
    ''' <paramref name="continuation"/> on the UI thread. The continuation may be Nothing -
    ''' that is the pre-warm, which only wants the cache filled.
    ''' </summary>
    Private Sub ProbeSlotThen(destination As String, allowCreate As Boolean, continuation As Action)
        Dim key As String = destination.Trim()

        Dim work As Task(Of SlotVerdict) = Task.Run(Function() ProbeSlot(key, allowCreate))

        Dim finish As Action(Of SlotVerdict) =
            Sub(verdict As SlotVerdict)
                StoreSlotVerdict(key, verdict)
                SyncLock slot_Verdicts
                    slot_Probes_In_Flight.Remove(key)
                End SyncLock
                AppFileLogger.WriteLine("Slot probe [" & verdict.State.ToString() & "] " & key &
                                        If(String.IsNullOrEmpty(verdict.Detail), "", " - " & verdict.Detail))
                Try
                    RaiseEvent SlotHealthChanged(Me, EventArgs.Empty)
                Catch
                End Try
                If continuation IsNot Nothing Then continuation()
            End Sub

        ' The bound (D2). A probe that runs out of time IS unreachable - and the abandoned
        ' task is left to finish on its own: it holds no form state and nothing waits on it.
        Dim guard As Task = Task.WhenAny(work, Task.Delay(SlotHealthPolicy.Probe_Timeout_Ms))
        guard.ContinueWith(
            Sub(done As Task)
                Dim verdict As SlotVerdict
                If work.IsCompleted AndAlso Not work.IsFaulted AndAlso Not work.IsCanceled Then
                    verdict = work.Result
                Else
                    verdict = SlotVerdict.ForState(SlotState.Unreachable, DateTime.UtcNow, "probe timed out")
                End If

                Try
                    If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
                    Me.BeginInvoke(New Action(Sub()
                                                  If Me.IsDisposed Then Return
                                                  finish(verdict)
                                              End Sub))
                Catch ex As Exception
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1685: slot probe post failed: " & ex.Message)
                End Try
            End Sub)
    End Sub

    ''' <summary>
    ''' The probe itself - worker thread, one attempt, no sleeps, no retry ladder (D2), and
    ''' it touches no form state.
    '''
    ''' It asks by ENUMERATING rather than with Directory.Exists, because Directory.Exists
    ''' swallows the very exception the answer depends on: it returns False for a deleted
    ''' folder AND for a share that stopped answering, which are the two cases this whole
    ''' feature exists to tell apart. The first entry is all that is fetched, so the cost on
    ''' a healthy folder is one round trip whatever is in it.
    '''
    ''' D6 lives in the parent branch: the final segment may be created, and only when its
    ''' parent answered. That is the difference between "the folder for this session did not
    ''' exist yet" and "I typed a share name wrong and the application built it for me".
    ''' </summary>
    Private Shared Function ProbeSlot(destination As String, allowCreate As Boolean) As SlotVerdict
        Dim now_Utc As DateTime = DateTime.UtcNow
        Dim full As String

        Try
            full = Path.GetFullPath(destination)
        Catch ex As Exception
            Return SlotVerdict.ForState(SlotState.Invalid, now_Utc, ex.Message)
        End Try

        Dim leaf As PathFailureKind = TouchDirectory(full)
        If leaf = PathFailureKind.None Then Return SlotVerdict.ForState(SlotState.Ready, now_Utc)

        If leaf = PathFailureKind.Missing Then
            Dim parent As String = Nothing
            Try
                parent = Path.GetDirectoryName(full)
            Catch
            End Try

            If Not String.IsNullOrEmpty(parent) Then
                Dim above As PathFailureKind = TouchDirectory(parent)
                If above = PathFailureKind.None Then
                    Return SlotVerdict.ForState(If(allowCreate, SlotState.WillBeCreated, SlotState.Missing),
                                                now_Utc, "leaf missing, parent reachable")
                End If
                ' The parent is the honest witness: if IT is unreachable, the leaf's
                ' absence says nothing about the leaf - the same reasoning ReadFailure
                ' applies on the read path (§10.2).
                If above <> PathFailureKind.Missing Then Return SlotVerdict.ForState(StateFor(above), now_Utc, "parent: " & above.ToString())
            End If

            Return SlotVerdict.ForState(SlotState.Missing, now_Utc)
        End If

        Return SlotVerdict.ForState(StateFor(leaf), now_Utc)
    End Function

    ''' <summary>Asks a directory one question and reports what it said. None means it
    ''' answered.</summary>
    Private Shared Function TouchDirectory(folder As String) As PathFailureKind
        Try
            Using walker As IEnumerator(Of String) = Directory.EnumerateFileSystemEntries(folder).GetEnumerator()
                walker.MoveNext()
            End Using
            Return PathFailureKind.None
        Catch ex As Exception
            Return PathFailure.Classify(ex)
        End Try
    End Function

    Private Shared Function StateFor(kind As PathFailureKind) As SlotState
        Select Case kind
            Case PathFailureKind.None : Return SlotState.Ready
            Case PathFailureKind.Missing : Return SlotState.Missing
            Case PathFailureKind.Denied : Return SlotState.Denied
            Case PathFailureKind.Invalid : Return SlotState.Invalid
            Case Else
                ' Transport, OutOfMemory, Content, Unknown. For a DIRECTORY the honest
                ' reading of anything unclassified is "it did not answer" - which is also
                ' the only one of the states that invites the user to try again.
                Return SlotState.Unreachable
        End Select
    End Function

    ''' <summary>The key the user actually presses for this slot - "0", not "10".</summary>
    Private Shared Function SlotKeyCaption(slot As Integer) As String
        If slot = 10 Then Return "0"
        Return slot.ToString()
    End Function

    Friend Function AutoCreateDestinationEnabled() As Boolean
        Dim prefs As ModernViewerPreferences = GetModernPreferences()
        Return prefs Is Nothing OrElse prefs.CreateMissingDestination
    End Function

    ''' <summary>The refusal, naming the slot AND the reason (§3.4). The retry hint rides on
    ''' the unreachable case only - it is the one state where pressing again is the right
    ''' thing to do, because it is the one that a woken NAS changes.</summary>
    Private Shared Function SlotHealthText(slot_Key As String, state As SlotState) As String
        Select Case state
            Case SlotState.Missing
                Return Localization.TF("! Каталог {0} не найден", slot_Key)
            Case SlotState.Denied
                Return Localization.TF("! Каталог {0}: нет доступа", slot_Key)
            Case SlotState.Invalid
                Return Localization.TF("! Каталог {0}: недопустимый путь", slot_Key)
            Case SlotState.NotConfigured
                Return Localization.TF("! Каталог {0} не задан", slot_Key)
            Case Else
                Return Localization.TF("! Каталог {0}: нет связи с папкой. Нажмите клавишу ещё раз, чтобы повторить проверку.", slot_Key)
        End Select
    End Function

    ''' <summary>The same reason in three words, for the overlay tooltip and the grid
    ''' (§3.5). Empty when there is nothing to say - the state is fine, or nobody has
    ''' asked yet.</summary>
    Friend Function SlotHealthNote(verdict As SlotVerdict) As String
        If verdict Is Nothing Then Return ""
        Select Case verdict.State
            Case SlotState.Ready : Return ""
            Case SlotState.WillBeCreated : Return Localization.T("будет создан")
            Case SlotState.Missing : Return Localization.T("не найден")
            Case SlotState.Denied : Return Localization.T("нет доступа")
            Case SlotState.Invalid : Return Localization.T("недопустимый путь")
            Case SlotState.NotConfigured : Return ""
            Case Else : Return Localization.T("нет связи")
        End Select
    End Function

    ''' <summary>True when the cache holds a verdict saying this destination cannot take a
    ''' file. "Nobody asked yet" is deliberately NOT bad - an unprobed slot is drawn
    ''' normally, because dimming what we have not checked would be a lie of the same shape
    ''' as the one this feature removes.</summary>
    Friend Function SlotLooksUnusable(destination As String) As Boolean
        Dim verdict As SlotVerdict = CachedSlotVerdict(destination)
        Return verdict IsNot Nothing AndAlso Not SlotHealthPolicy.IsUsable(verdict.State)
    End Function

    ''' <summary>
    ''' Fills the cache in the background so the common case is warm before the first press
    ''' (§3.3). Called after the settings window closes, after a slot is edited, and once at
    ''' startup - never on the UI thread, and it delays nothing: the first image is already
    ''' on screen by the time the first probe returns.
    ''' </summary>
    Friend Sub PrewarmSlotHealth()
        Dim allow_Create As Boolean = AutoCreateDestinationEnabled()
        For slot As Integer = 1 To 10
            Dim destination As String = Hardkeys_to_move_mediafile(slot)
            If String.IsNullOrWhiteSpace(destination) Then Continue For

            Dim key As String = destination.Trim()
            SyncLock slot_Verdicts
                If slot_Probes_In_Flight.Contains(key) Then Continue For
                slot_Probes_In_Flight.Add(key)
            End SyncLock

            ProbeSlotThen(key, allow_Create, Nothing)
        Next
    End Sub

    ''' <summary>
    ''' Re-asks about one destination and reports back - the configuration surfaces' entry
    ''' point (§4, Ф4). It always probes: the user has just changed the path, so a cached
    ''' answer about the previous one is worth nothing.
    ''' </summary>
    Friend Sub RefreshSlotHealth(destination As String, onDone As Action)
        If String.IsNullOrWhiteSpace(destination) Then
            If onDone IsNot Nothing Then onDone()
            Return
        End If

        Dim key As String = destination.Trim()
        SyncLock slot_Verdicts
            If slot_Probes_In_Flight.Contains(key) Then
                ' Already on its way; the SlotHealthChanged event will bring the answer.
                Return
            End If
            slot_Probes_In_Flight.Add(key)
        End SyncLock

        ProbeSlotThen(key, AutoCreateDestinationEnabled(), onDone)
    End Sub

End Class
#End If
