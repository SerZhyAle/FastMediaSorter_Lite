#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms
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

    ''' <summary>The press that is being gated was a CLICK on the recipients overlay, not a key (125 UX-332): the
    ''' refusal then says "click again", not "press the key again" to someone who never pressed a key. Set by the
    ''' overlay's click handlers around the one call they make; read once, at the top of the gate.</summary>
    Private slot_Press_By_Pointer As Boolean

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
                                            ByRef create_Destination As Boolean,
                                            Optional batchRetry As Action = Nothing) As Boolean
        create_Destination = False

        Dim destination As String = Hardkeys_to_move_mediafile(slot)
        If String.IsNullOrWhiteSpace(destination) Then Return False
        Dim slot_Key As String = SlotKeyCaption(slot)
        Dim by_Pointer As Boolean = slot_Press_By_Pointer

        ' A slot that names a saved resource (007 §4.2): everything that needs a person - the
        ' password, a sentence about a server nobody has verified - happens HERE, on this thread,
        ' before any probe. The probe itself never asks anyone anything.
        If Not RemoteSlotReadyToProbe(slot, destination) Then Return False

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
            BeginSlotProbe(destination, slot, action, batchRetry, by_Pointer)
            Return False
        End If

        If SlotHealthPolicy.IsUsable(verdict.State) Then
            create_Destination = (verdict.State = SlotState.WillBeCreated)
            Return True
        End If

        lbl_Status.Text = SlotHealthText(slot_Key, verdict.State, by_Pointer)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1684: slot " & slot_Key & " refused: " &
                        verdict.State.ToString() & " (" & verdict.Detail & ")")
        Return False
    End Function

    ''' <summary>
    ''' Starts a probe and, when it answers, runs the action again with the fresh verdict in
    ''' hand. The second pass cannot probe again: the answer it reads is younger than the
    ''' retry window and the re-entry flag keeps it from being counted as a press.
    ''' </summary>
    ''' <param name="batchRetry">A batch started this probe (Main_Form.Batch.vb): when the answer
    ''' lands, that call is made again instead of re-entering the single-file action. The
    ''' "file on screen changed" guard does not apply - a batch is about the files it was
    ''' given, not about whatever is displayed.</param>
    Private Sub BeginSlotProbe(destination As String, slot As Integer, action As RecipientActionKind,
                               Optional batchRetry As Action = Nothing, Optional by_Pointer As Boolean = False)
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

                          If batchRetry IsNot Nothing Then
                              ' The retry asks the gate again, with the answer now cached, and
                              ' says the refusal itself when the answer is a bad one.
                              slot_Health_Reentry = True
                              Try
                                  batchRetry()
                              Finally
                                  slot_Health_Reentry = False
                              End Try
                              Return
                          End If

                          If Not String.Equals(pressed_On, Current_File_Name, StringComparison.Ordinal) Then
                              ' The view moved on. Say what the answer was and stop: a bad
                              ' verdict is worth hearing either way, and a good one turns the
                              ' next press into an instant, cached move of the RIGHT file.
                              Dim answer As SlotVerdict = CachedSlotVerdict(destination)
                              If answer IsNot Nothing AndAlso Not SlotHealthPolicy.IsUsable(answer.State) Then
                                  lbl_Status.Text = SlotHealthText(SlotKeyCaption(slot), answer.State, by_Pointer)
                              ElseIf by_Pointer Then
                                  lbl_Status.Text = Localization.TF("каталог {0} доступен - нажмите ещё раз", SlotKeyCaption(slot))
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
                ' A password typed with "remember" ticked is stored once a connection proved it.
                StoreProvedSlotCredentials()
                Try
                    RaiseEvent SlotHealthChanged(Me, EventArgs.Empty)
                Catch ex As Exception
                    ' A repaint subscriber must not cost the verdict or the continuation, but
                    ' its failure is a trail worth keeping.
                    AppFileLogger.LogException("SlotHealthChanged subscriber", ex)
                End Try
                If continuation IsNot Nothing Then continuation()
            End Sub

        ' The exit that does not reach finish: the verdict is still cached (StoreSlotVerdict
        ' takes the lock, so any thread may call it) and the path is released, so the next
        ' press acts on the answer instead of waiting for one that is never coming. The
        ' continuation is dropped - the press it belonged to cannot be answered on screen.
        Dim abandon As Action(Of SlotVerdict) =
            Sub(verdict As SlotVerdict)
                StoreSlotVerdict(key, verdict)
                SyncLock slot_Verdicts
                    slot_Probes_In_Flight.Remove(key)
                End SyncLock
                AppFileLogger.WriteLine("Slot probe [" & verdict.State.ToString() & "] " & key &
                                        " - answer not delivered to the window; cached, not acted on")
            End Sub

        ' The bound (D2). A probe that runs out of time IS unreachable - and the abandoned
        ' task is left to finish on its own: it holds no form state and nothing waits on it.
        ' A server folder gets the connect budget on top (RemoteSlotPolicy.ProbeTimeoutMs).
        Dim guard As Task = Task.WhenAny(work, Task.Delay(RemoteSlotPolicy.ProbeTimeoutMs(key)))
        guard.ContinueWith(
            Sub(done As Task)
                Dim verdict As SlotVerdict
                If work.IsCompleted AndAlso Not work.IsFaulted AndAlso Not work.IsCanceled Then
                    verdict = work.Result
                Else
                    verdict = SlotVerdict.ForState(SlotState.Unreachable, DateTime.UtcNow, "probe timed out")
                End If

                Dim posted As Boolean = False
                Try
                    If Not Me.IsDisposed AndAlso Me.IsHandleCreated Then
                        Me.BeginInvoke(New Action(Sub()
                                                      If Me.IsDisposed Then
                                                          abandon(verdict)
                                                          Return
                                                      End If
                                                      finish(verdict)
                                                  End Sub))
                        posted = True
                    End If
                Catch ex As Exception
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1685: slot probe post failed: " & ex.Message)
                    AppFileLogger.LogException("Slot probe answer could not be posted to the UI thread", ex)
                End Try
                ' No handle at this instant - a full-screen toggle re-creates it - or the post
                ' threw: finish never runs, and the path used to stay "in flight" for good, so
                ' every later press answered "checking.." and started nothing (057.S05-6).
                If Not posted Then abandon(verdict)
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
        Dim slots As RemoteSlotConnections = RemoteSlotConnections.Current
        If ResourceLocator.IsLocator(destination) Then
            ' A folder of a saved resource (007 §4.2): dialled if need be, then asked through its
            ' provider - the folder, and its parent when the folder is missing.
            If slots Is Nothing Then Return SlotVerdict.ForState(SlotState.Invalid, now_Utc, "no slot connections")
            Return slots.Probe(destination, allowCreate)
        End If

        ' A share that needs a login, saved as a resource (007 §5): this process gets its session
        ' first, and the plain-folder probe below then answers exactly as for any other share.
        Dim smb_Failure As ResourceFailure = slots?.EnsureSmbSession(destination)
        If smb_Failure IsNot Nothing AndAlso RemoteSlotPolicy.StateFor(smb_Failure) = SlotState.Denied Then
            Return SlotVerdict.ForState(SlotState.Denied, now_Utc, "share session: " & smb_Failure.ToString())
        End If

        Dim full As String

        Try
            full = Path.GetFullPath(destination)
        Catch ex As Exception
            Return SlotVerdict.ForState(SlotState.Invalid, now_Utc, ex.Message)
        End Try

        Dim leaf As PathFailureKind = TouchDirectory(full)
        If leaf = PathFailureKind.None Then Return SlotVerdict.ForState(SlotState.Ready, now_Utc)

        ' May not LIST is not may not WRITE: a write-only drop-box refuses the listing and
        ' takes the file (057.S05-7). Its attributes are the second witness.
        If leaf = PathFailureKind.Denied Then
            Dim attributes As PathFailureKind = ReadDirectoryAttributes(full)
            Return SlotVerdict.ForState(SlotHealthPolicy.StateAfterDeniedListing(attributes), now_Utc,
                                        "listing denied; attributes: " & attributes.ToString())
        End If

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

    ''' <summary>Is this a folder, asked through its attributes - which need no right to
    ''' list it? None means a folder answered; Invalid that the path names a file.</summary>
    Private Shared Function ReadDirectoryAttributes(folder As String) As PathFailureKind
        Try
            Dim attributes As FileAttributes = File.GetAttributes(folder)
            If (attributes And FileAttributes.Directory) <> FileAttributes.Directory Then Return PathFailureKind.Invalid
            Return PathFailureKind.None
        Catch ex As Exception
            Return PathFailure.Classify(ex)
        End Try
    End Function

    Private Shared Function StateFor(kind As PathFailureKind) As SlotState
        Select Case kind
            Case PathFailureKind.None : Return SlotState.Ready
            Case PathFailureKind.Missing : Return SlotState.Missing
            Case PathFailureKind.Denied, PathFailureKind.Locked : Return SlotState.Denied
            ' Invalid includes ERROR_DIRECTORY - a slot that names a FILE, which used to read
            ' "no connection, press again" for ever (audit 047, C-24).
            Case PathFailureKind.Invalid, PathFailureKind.Exists : Return SlotState.Invalid
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
    Private Shared Function SlotHealthText(slot_Key As String, state As SlotState, Optional fromPointer As Boolean = False) As String
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
                If fromPointer Then Return Localization.TF("! Каталог {0}: нет связи с папкой. Нажмите ещё раз, чтобы повторить проверку.", slot_Key)
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
            ' Nobody pressed anything: a server folder that would need the user first waits for
            ' its key press (007 §4.2).
            If Not MayProbeUnattended(key) Then Continue For
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
        If Not MayProbeUnattended(key) Then
            ' A server folder that needs a password or a verified key first: no opinion until
            ' the slot is used, where the user can be asked (007 §4.2).
            ForgetSlotVerdict(key)
            If onDone IsNot Nothing Then onDone()
            Return
        End If
        SyncLock slot_Verdicts
            If slot_Probes_In_Flight.Contains(key) Then
                ' Already on its way; the SlotHealthChanged event will bring the answer.
                Return
            End If
            slot_Probes_In_Flight.Add(key)
        End SyncLock

        ProbeSlotThen(key, AutoCreateDestinationEnabled(), onDone)
    End Sub


    ' --------------------------------------------------- slots that name a resource (007 §4.2) ----

    ''' <summary>Passwords typed for a slot with "remember" ticked, waiting for the first connection
    ''' that proves them: a credential is stored only after it worked, as when a resource is opened
    ''' (Main_Form.ResourceOpen.vb). UI thread only.</summary>
    Private ReadOnly slot_Credentials_To_Store As New Dictionary(Of String, Tuple(Of ResourceDescriptor, String))(StringComparer.OrdinalIgnoreCase)

    ''' <summary>The connections behind slots that name a saved resource, created on first need and
    ''' published for the routing of FileManager's verbs (which runs on the worker thread).</summary>
    Friend Function EnsureRemoteSlots() As RemoteSlotConnections
        Dim slots As RemoteSlotConnections = RemoteSlotConnections.Current
        If slots IsNot Nothing Then Return slots
        Dim secrets As IResourceSecretStore = resource_Secrets
        slots = New RemoteSlotConnections(
            Function() ResourceRegistry.Load(),
            Function(descriptor As ResourceDescriptor) As String
                Dim read As CredentialReadResult = ResourceCommit.ReadCredential(descriptor, secrets)
                Return If(read.Outcome = CredentialReadOutcome.Ok, read.Secret, Nothing)
            End Function,
            AddressOf DialSlotResource,
            Function(root As String, user As String, password As String) SmbConnectionManager.SharedInstance.AcquireLease(root, user, password))
        RemoteSlotConnections.Current = slots
        Return slots
    End Function

    ''' <summary>
    ''' One connection for a slot: the resource's own connect, with a trust prompt that always says no
    ''' and a pin store that never stores. A slot only ever reaches a server whose key is already
    ''' pinned (the gate refuses any other), so neither is ever asked - and if a key changed, the
    ''' connection is refused as a mismatch, never re-trusted from the middle of sorting.
    ''' </summary>
    Private Shared Function DialSlotResource(descriptor As ResourceDescriptor, password As String,
                                             ct As Threading.CancellationToken) As ConnectOutcome
        Return ResourceConnect.ConnectAsync(descriptor, password,
                                            Function(fingerprint As String, c As Threading.CancellationToken) False,
                                            Function(fingerprint As String) False,
                                            Function() False, ct).GetAwaiter().GetResult()
    End Function

    ''' <summary>
    ''' The part of the gate that may need the user, for a slot that names a saved resource: a
    ''' sentence for a server that is not in the list, has never been verified or is read-only, and
    ''' the password prompt when none is known. True when a probe may go ahead (always, for a plain
    ''' folder).
    ''' </summary>
    Private Function RemoteSlotReadyToProbe(slot As Integer, destination As String) As Boolean
        Dim slots As RemoteSlotConnections = EnsureRemoteSlots()
        Dim descriptor As ResourceDescriptor = Nothing
        Dim remotePath As String = ""
        Dim slot_Key As String = SlotKeyCaption(slot)
        Select Case slots.Locate(destination, descriptor, remotePath)
            Case RemoteSlotProblem.None, RemoteSlotProblem.NotRemote
                Return True
            Case RemoteSlotProblem.UnknownResource
                lbl_Status.Text = Localization.TF("! Каталог {0}: этого сервера нет в списке ресурсов - добавьте его через «Ресурсы»", slot_Key)
            Case RemoteSlotProblem.NotPinned
                lbl_Status.Text = Localization.TF("! Каталог {0}: ключ сервера «{1}» ещё не проверен - откройте этот ресурс один раз", slot_Key, descriptor.DisplayName)
            Case RemoteSlotProblem.ReadOnlyResource
                lbl_Status.Text = Localization.TF("! Каталог {0}: ресурс «{1}» только для чтения", slot_Key, descriptor.DisplayName)
            Case RemoteSlotProblem.NoCredential
                If AskSlotCredential(slots, descriptor) Then
                    ' A fresh password makes any earlier answer - a refused one - worthless.
                    ForgetSlotVerdict(destination)
                    Return True
                End If
                lbl_Status.Text = Localization.T("Подключение отменено")
            Case Else
                lbl_Status.Text = SlotHealthText(slot_Key, SlotState.Invalid)
        End Select
        Return False
    End Function

    ''' <summary>Asks for the password of the resource a slot names. Kept in memory for the run; stored
    ''' through DPAPI only once a connection has proved it (<see cref="StoreProvedSlotCredentials"/>).</summary>
    Private Function AskSlotCredential(slots As RemoteSlotConnections, descriptor As ResourceDescriptor) As Boolean
        Dim retry As Boolean = slots.WasRefused(descriptor.Id)
        Using dialog As New Resource_Credential_Form(descriptor.DisplayName, descriptor.UserName, retry)
            If DialogLayer.ShowViewerDialog(dialog, Me) <> DialogResult.OK Then Return False
            slots.RememberCredential(descriptor.Id, dialog.Credential)
            ' The resource itself opens without asking again this run, as it would after its own prompt.
            resource_Session_Passwords(descriptor.Id) = dialog.Credential
            If dialog.RememberCredential Then
                slot_Credentials_To_Store(descriptor.Id) = Tuple.Create(descriptor, dialog.Credential)
            Else
                slot_Credentials_To_Store.Remove(descriptor.Id)
            End If
        End Using
        Return True
    End Function

    ''' <summary>After a probe: a password waiting to be remembered is stored once its resource has a
    ''' working connection, and dropped once the server refused it.</summary>
    Private Sub StoreProvedSlotCredentials()
        If slot_Credentials_To_Store.Count = 0 Then Return
        Dim slots As RemoteSlotConnections = RemoteSlotConnections.Current
        If slots Is Nothing Then Return
        For Each id As String In slot_Credentials_To_Store.Keys.ToList()
            Dim pending As Tuple(Of ResourceDescriptor, String) = slot_Credentials_To_Store(id)
            If slots.WasRefused(id) Then
                slot_Credentials_To_Store.Remove(id)
            ElseIf slots.IsConnected(id) Then
                slot_Credentials_To_Store.Remove(id)
                Try
                    ResourceCommit.Commit(pending.Item1, pending.Item2, AddressOf ResourceRegistry.HiveRead,
                                          AddressOf ResourceRegistry.HiveWrite, resource_Secrets)
                Catch ex As Exception
                    ' Best effort, as after opening a resource: the slot already works this run.
                    AppFileLogger.WriteLine("Remote slot: the credential could not be stored - " & ex.GetType().Name)
                End Try
            End If
        Next
    End Sub

    Private Sub ForgetSlotVerdict(destination As String)
        If String.IsNullOrWhiteSpace(destination) Then Return
        SyncLock slot_Verdicts
            slot_Verdicts.Remove(destination.Trim())
        End SyncLock
    End Sub

    ''' <summary>May a probe nobody pressed a key for (the pre-warm, the settings grid) ask about this
    ''' destination? Not a server folder that would first need the user - a password, or a server
    ''' nobody has verified: those are asked about when the slot is used.</summary>
    Private Function MayProbeUnattended(destination As String) As Boolean
        Dim descriptor As ResourceDescriptor = Nothing
        Dim remotePath As String = ""
        Select Case EnsureRemoteSlots().Locate(destination, descriptor, remotePath)
            Case RemoteSlotProblem.None, RemoteSlotProblem.NotRemote, RemoteSlotProblem.Invalid, RemoteSlotProblem.ReadOnlyResource
                Return True
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>What the overlay, the menus and the settings grid call a slot: "«Name» /folder" for a
    ''' folder of a saved resource, the path itself for anything else.</summary>
    Friend Function SlotCaption(destination As String) As String
        If Not ResourceLocator.IsLocator(destination) Then Return If(destination, "")
        Dim named As String = EnsureRemoteSlots().Caption(destination)
        Return If(named.Length > 0, named, destination)
    End Function

    ''' <summary>After the resource list was edited: every slot connection is dialled afresh (an
    ''' address, a key or a password may have changed), every answer about a server folder is asked
    ''' again, and the overlay re-reads the names.</summary>
    Friend Sub ResetRemoteSlots()
        Dim slots As RemoteSlotConnections = RemoteSlotConnections.Current
        If slots Is Nothing Then Return
        slots.Reset()
        For slot As Integer = 1 To 10
            Dim destination As String = Hardkeys_to_move_mediafile(slot)
            If ResourceLocator.IsLocator(destination) Then ForgetSlotVerdict(destination)
        Next
        ApplyRecipientsOverlay()
    End Sub

    ''' <summary>The close: every slot connection and share session goes, with the typed passwords.</summary>
    Friend Sub ShutdownRemoteSlots()
        Dim slots As RemoteSlotConnections = RemoteSlotConnections.Current
        RemoteSlotConnections.Current = Nothing
        slots?.Dispose()
    End Sub

End Class
#End If
