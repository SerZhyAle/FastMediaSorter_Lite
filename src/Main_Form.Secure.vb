#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO
Imports CancellationTokenSource = System.Threading.CancellationTokenSource
Imports System.Threading.Tasks
Imports System.Windows.Forms

' The viewer's whole side of secure containers
' (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §5.3).
'
' A .fd-sec file is shown the way a JPEG or an MP4 is shown: it is in the folder listing,
' it is navigated to like anything else, and when it comes up the viewer asks for the
' password once, opens the container, discovers from the sealed metadata what is actually
' inside and displays it - with zoom, the perspective background, the OCR overlay and the
' video transport all working as usual.
'
' The one boundary that makes this different from every other display path in the product:
' NO DECRYPTED BYTE EVER REACHES THE DISK. Not a temporary folder, not the decode cache,
' not the OCR cache, not a log. The plaintext exists only inside this process, for as long
' as the file is on screen. Every refusal in §7 and every rule in §5.4 follows from that
' one sentence, and each of them is a code path that exists today and would write a secret
' to disk if nobody stopped it.
'
' What this partial owns: the open attempt, the session credential, the declined set, the
' locked placeholder, the unlock action, and the mapping from an outcome class to a
' message. What it deliberately does NOT own: the format (src/Secure, which knows nothing
' about this application) and the dialog (Secure_Password_Form).
'
' Modern-only: on the net48 fallback a .fd-sec is not even listed.
Partial Public Class Main_Form
    Private secure_Verify_Cancellation As CancellationTokenSource
    Private secure_Verify_Previous_Status As String = ""

    Private Sub CancelSecureVerification()
        secure_Verify_Cancellation?.Cancel()
        secure_Verify_Cancellation = Nothing
    End Sub

    ' ------------------------------------------------------------------ state ----

    ''' <summary>The credential held for the running process, as its normalised UTF-8
    ''' bytes (§5.5) - not a String, which could never be wiped (audit 052 G-19). Memory
    ''' only: it is never written to the registry, to a file, or to the DPAPI store the OCR
    ''' API keys use - that store is a convenience for a third-party service token, and
    ''' this is the user's own data.</summary>
    Private secure_Session_Credential As FdSecCredential

    ''' <summary>When the held credential was last used, for the idle timeout of §9.</summary>
    Private secure_Credential_Touched As Date

    ''' <summary>Enforces the idle timeout while nothing is being opened (G-19): without
    ''' it the timeout was only checked on the next open, so an idle viewer kept the
    ''' credential and the key cache for as long as it sat there.</summary>
    Private secure_Idle_Timer As Timer

    ''' <summary>Whether the placeholder on screen is a LOCKED one - the only state in
    ''' which a click, Enter or the menu asks for a password (audit 052 G-07) - and which
    ''' container it belongs to.</summary>
    Private secure_Placeholder_Locked As Boolean
    Private secure_Placeholder_Path As String = ""

    ''' <summary>How the most recent attempt to show a container ended.</summary>
    Private secure_Last_Attempt As SecureAttempt = SecureAttempt.NotTried

    ''' <summary>Derived keys of containers opened this session, so coming back to one
    ''' costs nothing. Dropped whenever the credential is (§5.5).</summary>
    Private secure_Key_Cache As FdSecKeyCache

    ''' <summary>Containers the user CANCELLED, remembered for as long as this folder is
    ''' open. Passing over one again shows the placeholder without asking; the explicit
    ''' unlock action always asks (§3.3).</summary>
    Private ReadOnly secure_Declined As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>The container currently on screen, and the stream a video is playing from.
    ''' The stream must outlive the media player and is disposed with it.</summary>
    Private secure_Container As FdSecContainer
    Private secure_Content_Stream As Stream
    Private secure_Vlc_Input As LibVLCSharp.Shared.StreamMediaInput

    ''' <summary>The LibVLC media a container is playing from. Every other media is
    ''' released right after Play; this one is kept so its MetaChanged can be watched,
    ''' because LibVLC writes an embedded cover to its art cache when the demuxer opens
    ''' (VlcArtCache, audit 046 G-04). Guarded by secure_Art_Lock - the scrub runs on a
    ''' pool thread.</summary>
    Private secure_Vlc_Media As LibVLCSharp.Shared.Media
    Private ReadOnly secure_Art_Lock As New Object()

    Private secure_Container_Path As String = ""
    Private secure_True_Name As String = ""

    ''' <summary>What the OPEN container turned out to hold, classified from its sealed
    ''' true name by the same MediaKindClassifier every other file goes through. Only
    ''' meaningful while <see cref="secure_Container_Path"/> is set.</summary>
    Private secure_Content_Kind As MediaKind = MediaKind.Other

    ''' <summary>The container waiting for a prompt, and the timer that decides when the
    ''' user has actually settled on it. Holding an arrow key across a folder of containers
    ''' must never produce a queue of modal dialogs (§3.3, R-5).</summary>
    Private secure_Prompt_Pending_Path As String = ""
    Private secure_Prompt_Timer As Timer

    ''' <summary>True while a dialog is up, so nothing re-entrantly opens a second one.</summary>
    Private secure_Prompting As Boolean

    ''' <summary>How many containers a running slideshow skipped, so the status line can
    ''' say so instead of the slideshow looking broken (§3.3).</summary>
    Private secure_Skipped_In_Slideshow As Integer

    ''' <summary>How long the user must stay on a container before it asks. The same
    ''' trailing-edge idea Draw_Perspective uses, and for the same reason: what matters is
    ''' where the navigation SETTLED, not every file it passed through.</summary>
    Private Const Secure_Prompt_Settle_Ms As Integer = 400

    ' ------------------------------------------------------------ is it one? ----

    ''' <summary>Extension only - and that is all anyone can go on. A container carries no
    ''' marker whatsoever (FDSEC-FORMAT §13.4), so the name is the ONLY thing on disk that
    ''' says a file might be one, and even that is only there because its owner kept it.</summary>
    Friend Shared Function IsSecureContainerExtension(extension As String) As Boolean
        Return String.Equals(extension, FdSecFormat.Extension, StringComparison.OrdinalIgnoreCase)
    End Function

    Friend Function IsCurrentFileSecureContainer() As Boolean
        If String.IsNullOrEmpty(Current_File_Name) Then Return False
        Return IsSecureContainerExtension(NavigationRules.ExtensionKey(Current_File_Name))
    End Function

    ''' <summary>
    ''' Is sealed content on screen right now? Not the same question as "is there an open
    ''' container object": a container IMAGE is decoded into memory and the container is
    ''' closed straight away, so the handle does not sit in the way of a move or a delete -
    ''' but what the user is looking at is still a secret, and every rule in §5.4 still
    ''' applies to it.
    ''' </summary>
    Friend Function IsSecureContentOnScreen() As Boolean
        Return secure_Container_Path.Length > 0
    End Function

    ''' <summary>A picture out of a container - so the OCR overlay, zoom and the perspective
    ''' background treat it exactly like any other photograph, which §0 says they must.</summary>
    Friend Function IsSecureImageOnScreen() As Boolean
        Return IsSecureContentOnScreen() AndAlso secure_Content_Kind = MediaKind.Image
    End Function

    ''' <summary>
    ''' What to call the file on screen. Inside an open container that is the sealed true
    ''' name - but only while the user has left <c>SecureShowTrueName</c> on, and never
    ''' when the name is not ours to show. Empty for anything else, so callers can write
    ''' one line and not ask whether a container is involved.
    ''' </summary>
    Friend Function SecureDisplayName(filePath As String) As String
        If String.IsNullOrEmpty(filePath) OrElse Not IsSecureContentOnScreen() Then Return ""
        If Not String.Equals(filePath, secure_Container_Path, StringComparison.OrdinalIgnoreCase) Then Return ""
        If Not GetModernPreferences().SecureShowTrueName Then Return ""
        Return secure_True_Name
    End Function

    ' ----------------------------------------------------------- the credential ----

    Friend Function SecureHasCredential() As Boolean
        If secure_Session_Credential Is Nothing OrElse secure_Session_Credential.IsDisposed Then Return False

        ' The idle timeout of §9. Zero means "until the program closes", which is a choice
        ' about this machine's risk, not a missing value.
        If SecureCredentialExpired() Then
            SecureForgetCredential(announce:=False)
            Return False
        End If
        Return True
    End Function

    ''' <summary>Read-only: has the idle timeout run out? Safe from the worker thread,
    ''' which must not forget anything itself (the prefetch, G-19).</summary>
    Private Function SecureCredentialExpired() As Boolean
        Dim minutes As Integer = GetModernPreferences().SecureRememberMinutes
        Return minutes > 0 AndAlso Date.UtcNow - secure_Credential_Touched > TimeSpan.FromMinutes(minutes)
    End Function

    ''' <summary>
    ''' Drops the credential and every key derived from it, wiping the bytes (G-19).
    ''' Present as a menu item whenever one is held (§5.5): a password the user cannot put
    ''' down is a password they have to close the program to be rid of.
    ''' </summary>
    Friend Sub SecureForgetCredential(Optional announce As Boolean = True)
        CancelSecureVerification()
        Dim held As FdSecCredential = secure_Session_Credential
        secure_Session_Credential = Nothing
        held?.Dispose()
        If secure_Key_Cache IsNot Nothing Then
            secure_Key_Cache.Clear()
            secure_Key_Cache = Nothing
        End If
        secure_Idle_Timer?.Stop()
        If announce Then lbl_Status.Text = Localization.T("Пароль забыт")
    End Sub

    ''' <summary>Takes ownership of <paramref name="credential"/>.</summary>
    Private Sub SecureRememberCredential(credential As FdSecCredential)
        If secure_Session_Credential IsNot Nothing AndAlso Not ReferenceEquals(secure_Session_Credential, credential) Then
            ' A different password replaces the held one; the old keys belong to it.
            secure_Session_Credential.Dispose()
            secure_Key_Cache?.Clear()
        End If
        secure_Session_Credential = credential
        secure_Credential_Touched = Date.UtcNow
        If secure_Key_Cache Is Nothing Then secure_Key_Cache = New FdSecKeyCache()

        If secure_Idle_Timer Is Nothing Then
            secure_Idle_Timer = New Timer() With {.Interval = 30000}
            AddHandler secure_Idle_Timer.Tick, AddressOf Secure_Idle_Timer_Tick
        End If
        secure_Idle_Timer.Start()
    End Sub

    ''' <summary>SecureHasCredential forgets an expired credential as a side effect -
    ''' this is only what asks it when nobody else does.</summary>
    Private Sub Secure_Idle_Timer_Tick(sender As Object, e As EventArgs)
        If Not SecureHasCredential() Then secure_Idle_Timer.Stop()
    End Sub

    ''' <summary>
    ''' G-10: the name a failure surface may show. The sealed true name only while the user
    ''' has SecureShowTrueName on; otherwise the container's own name.
    ''' </summary>
    Private Function SecureShownName(trueName As String, filePath As String) As String
        Return SecurePolicy.ShownName(trueName, filePath, GetModernPreferences().SecureShowTrueName)
    End Function

    ''' <summary>
    ''' G-07: is the placeholder on screen a LOCKED one for the current file? The click, the
    ''' Enter key and the media menu offer "unlock" only then - never for a container that
    ''' failed the length screen, turned out damaged or unsupported, or whose content the
    ''' credential already opened.
    ''' </summary>
    Friend Function IsSecurePlaceholderLocked() As Boolean
        If Not secure_Placeholder_Locked OrElse IsSecureContentOnScreen() Then Return False
        If Not IsCurrentFileSecureContainer() Then Return False
        Return String.Equals(secure_Placeholder_Path, Current_File_Name, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>The folder the declined set belongs to.</summary>
    Private secure_Declined_Folder As String = ""

    ''' <summary>
    ''' Leaving a folder forgets who declined what: the set is about this pass through this
    ''' folder, not a standing refusal (§3.3).
    '''
    ''' Checked here rather than hooked onto every place the folder changes - there are
    ''' six, and a feature whose correctness depends on all of them being remembered is a
    ''' feature that breaks the next time a seventh is added.
    ''' </summary>
    Private Sub SecureNoticeFolder()
        If String.Equals(secure_Declined_Folder, Current_Folder_Path, StringComparison.OrdinalIgnoreCase) Then Return
        secure_Declined_Folder = Current_Folder_Path
        secure_Declined.Clear()
        secure_Skipped_In_Slideshow = 0
    End Sub

    ' -------------------------------------------------------------- displaying ----

    ''' <summary>
    ''' The dispatcher's container branch, called instead of the image/video split.
    '''
    ''' Step 1 is the length screen, and it runs before any dialog: a file that cannot be a
    ''' container is refused as damaged right there, and the user is never asked for a
    ''' password for a file that could not have used one. A refusal that arrives after the
    ''' password was typed is a refusal that wasted a secret (FDSEC-BEHAVIOUR §8.1).
    ''' </summary>
    Private Sub DisplaySecureContainer(filePath As String)
        SecureReleaseCurrent()
        SecureNoticeFolder()

        Dim length As Long
        Try
            length = New FileInfo(filePath).Length
        Catch ex As Exception
            ShowSecurePlaceholder(filePath, ReadFailureText(PathFailure.Classify(ex), filePath), locked:=False)
            Return
        End Try

        If Not FdSecScreen.CouldBeContainer(length) Then
            ShowSecurePlaceholder(filePath, Localization.T("Файл повреждён или неполон"), locked:=False)
            Return
        End If

        ' The session credential is tried SILENTLY, always, before any dialog appears. When
        ' that try ends in anything but the class another password could change - damaged,
        ' unsupported, an I/O failure, a sealed PDF, a picture that would not decode - the
        ' honest placeholder it just drew STAYS: asking again for the password that already
        ' worked would be the lie G-07 found.
        If SecureHasCredential() Then
            If TryShowSecureContent(filePath, secure_Session_Credential) Then Return
            If Not SecurePolicy.ShouldAskForPassword(screenPassed:=True, attempt:=secure_Last_Attempt) Then Return
        End If

        ' A slideshow never prompts. A modal dialog inside an unattended slideshow is a
        ' trap, so the file is skipped and the status line says how many were skipped.
        If is_Slide_Show_Mode Then
            secure_Skipped_In_Slideshow += 1
            ShowSecurePlaceholder(filePath,
                                  Localization.TF("Пропущено защищённых файлов: {0}", secure_Skipped_In_Slideshow.ToString()),
                                  locked:=True)
            Return
        End If

        ShowSecurePlaceholder(filePath, Localization.T("Нужен пароль - нажмите Enter или щёлкните по файлу"), locked:=True)

        ' A container the user cancelled is not asked about again while this folder is
        ' open; the explicit unlock action always asks.
        If secure_Declined.Contains(filePath) Then Return
        SchedulePasswordPrompt(filePath)
    End Sub

    ''' <summary>
    ''' The prompt fires only when navigation has SETTLED and only while the window is
    ''' active. Without both, holding an arrow key across a folder of containers would
    ''' produce one modal dialog per file - the failure R-5 exists for.
    ''' </summary>
    Private Sub SchedulePasswordPrompt(filePath As String)
        secure_Prompt_Pending_Path = filePath
        If secure_Prompt_Timer Is Nothing Then
            secure_Prompt_Timer = New Timer() With {.Interval = Secure_Prompt_Settle_Ms}
            AddHandler secure_Prompt_Timer.Tick, AddressOf Secure_Prompt_Timer_Tick
        End If
        secure_Prompt_Timer.Stop()
        secure_Prompt_Timer.Start()
    End Sub

    Private Sub Secure_Prompt_Timer_Tick(sender As Object, e As EventArgs)
        secure_Prompt_Timer.Stop()
        If secure_Prompting Then Return
        If Not Me.ContainsFocus AndAlso Not Me.Focused Then Return
        If Not String.Equals(secure_Prompt_Pending_Path, Current_File_Name, StringComparison.OrdinalIgnoreCase) Then Return
        AskForPasswordAndOpen(secure_Prompt_Pending_Path, retry:=False)
    End Sub

    ''' <summary>
    ''' The explicit unlock action - Enter, a click on the placeholder, the media menu.
    ''' It ALWAYS asks, even for a container the user declined: declining meant "not now",
    ''' and asking again is exactly what the action is for.
    ''' </summary>
    Friend Sub SecureUnlockCurrent()
        If Not IsCurrentFileSecureContainer() Then Return
        ' Already showing its content - and note this asks about the CONTENT rather than
        ' about an open container object, because an image is decoded into memory and its
        ' container closed at once. Asking the other way would re-prompt for a picture
        ' already on screen.
        If IsSecureContentOnScreen() Then Return
        ' Only a LOCKED placeholder is unlocked (G-07): a damaged, unsupported or unreadable
        ' container keeps its own message and is never answered with a password dialog.
        If Not IsSecurePlaceholderLocked() Then Return

        ' The length screen again, right before the dialog: the file may have changed
        ' since it was listed, and a refusal after the password was typed wastes a secret.
        Dim length As Long
        Try
            length = New FileInfo(Current_File_Name).Length
        Catch ex As Exception
            ShowSecurePlaceholder(Current_File_Name, ReadFailureText(PathFailure.Classify(ex), Current_File_Name), locked:=False)
            Return
        End Try
        If Not FdSecScreen.CouldBeContainer(length) Then
            ShowSecurePlaceholder(Current_File_Name, Localization.T("Файл повреждён или неполон"), locked:=False)
            Return
        End If

        secure_Declined.Remove(Current_File_Name)
        If SecureHasCredential() Then
            If TryShowSecureContent(Current_File_Name, secure_Session_Credential) Then Return
            If Not SecurePolicy.ShouldAskForPassword(screenPassed:=True, attempt:=secure_Last_Attempt) Then Return
        End If
        AskForPasswordAndOpen(Current_File_Name, retry:=False)
    End Sub

    Private Sub AskForPasswordAndOpen(filePath As String, retry As Boolean)
        If secure_Prompting Then Return
        secure_Prompting = True
        Try
            Dim attemptRetry As Boolean = retry
            Do
                Dim answer As DialogResult
                Dim credential As FdSecCredential = Nothing
                Dim remember As Boolean
                Using dialog As New Secure_Password_Form(Path.GetFileName(filePath), attemptRetry)
                    answer = DialogLayer.ShowViewerDialog(dialog, Me)
                    ' Normalised to bytes at once (G-19): the String the TextBox hands back
                    ' is the one copy this code cannot wipe, so it is not kept.
                    If answer = DialogResult.OK Then credential = FdSecCredential.FromText(dialog.Credential)
                    remember = dialog.RememberForSession
                End Using

                ' Escape, the close box and Cancel are one path and change nothing.
                If answer <> DialogResult.OK Then
                    secure_Declined.Add(filePath)
                    Return
                End If

                Dim kept As Boolean = False
                Try
                    ' The user may have navigated away while the dialog was up - the window
                    ' is modal to us, not to the world (a forwarded file opens through
                    ' WM_COPYDATA).
                    If Not String.Equals(filePath, Current_File_Name, StringComparison.OrdinalIgnoreCase) Then Return

                    If TryShowSecureContent(filePath, credential) Then
                        If remember Then
                            SecureRememberCredential(credential)
                            kept = True
                        End If
                        Return
                    End If
                Finally
                    If Not kept Then credential.Dispose()
                End Try

                ' Only the first outcome class is worth asking again about: a damaged or
                ' unsupported container will not open however many passwords are tried, and
                ' TryShowSecureContent has already said which one it was.
                If secure_Last_Attempt <> SecureAttempt.CredentialOrTamper Then Return
                attemptRetry = True
            Loop
        Finally
            secure_Prompting = False
        End Try
    End Sub

    ''' <summary>
    ''' Opens the container and shows what is inside, or reports the failure in the one
    ''' message its outcome class deserves. Returns True only when something is on screen;
    ''' <see cref="secure_Last_Attempt"/> then says how it ended, so the callers can tell
    ''' "try another password" from "this will never open" (G-07).
    ''' </summary>
    Private Function TryShowSecureContent(filePath As String, credential As FdSecCredential) As Boolean
        secure_Last_Attempt = SecureAttempt.Shown
        Dim container As FdSecContainer = Nothing
        ' Argon2id (64 MiB, 4 lanes) and the full decrypt run synchronously below: say so first, and show the wait
        ' cursor, so the pause after "Open" is not a window that looks hung (124 UX-213). The derivation itself stays
        ' on this thread for now - moving it off needs its own re-entrancy design around the display path.
        Notify(Localization.T("Проверяю пароль.."), NotifyLevel.Info)
        ChromeRepaintStatusNow()
        Dim previousCursor As Cursor = Cursor.Current
        Cursor.Current = Cursors.WaitCursor
        Try
            container = FdSecContainer.OpenFile(filePath, credential, keyCache:=SecureKeyCacheFor(credential))
        Catch ex As FdSecException
            secure_Last_Attempt = SecurePolicy.AttemptFromOpen(ex.Outcome)
            ' The raw reason goes to the log; the sealed true name never does, and neither
            ' does the credential (FDSEC-BEHAVIOUR §6 rule 8).
            AppFileLogger.WriteLine("Secure container refused (" & ex.Outcome.ToString() & "): " & filePath)
            ShowSecurePlaceholder(filePath, SecureOutcomeMessage(ex.Outcome),
                                  locked:=(ex.Outcome = FdSecOutcome.CredentialOrTamper))
            Return False
        Catch ex As ObjectDisposedException
            ' The credential was forgotten underneath this call (the idle timer): nothing
            ' was tried with it, so the container is simply locked again.
            secure_Last_Attempt = SecureAttempt.NotTried
            ShowSecurePlaceholder(filePath, Localization.T("Нужен пароль - нажмите Enter или щёлкните по файлу"), locked:=True)
            Return False
        Catch ex As Exception
            ' An I/O failure is NOT one of the three classes and must never be dressed up
            ' as one: "the share went away" is a different fact from "your password is
            ' wrong", and the list must survive it the way every other read does.
            secure_Last_Attempt = SecureAttempt.ReadFailure
            AppFileLogger.LogException("Secure container: opening " & filePath, ex)
            ShowSecurePlaceholder(filePath, ReadFailureText(PathFailure.Classify(ex), filePath), locked:=False)
            Return False
        Finally
            Cursor.Current = previousCursor
        End Try

        Try
            secure_Credential_Touched = Date.UtcNow
            ' Already held to the 1.1 name rule: an unsafe name never gets this far.
            Dim trueName As String = container.Metadata.Name
            Dim kind As MediaKind = KindOf(container.Metadata.ContentExtension())

            Select Case kind
                Case MediaKind.Image
                    If Not ShowSecureImage(container, filePath, trueName) Then
                        container.Dispose()
                        Return False
                    End If
                Case MediaKind.Video, MediaKind.Audio
                    ShowSecurePlayback(container, filePath, trueName, kind)
                Case Else
                    ' Opened, and not media (a sealed PDF). Not a password problem - the
                    ' credential just worked - so this placeholder is not a locked one.
                    secure_Last_Attempt = SecureAttempt.NotMedia
                    container.Dispose()
                    ShowSecurePlaceholder(filePath,
                                          Localization.TF("Формат не поддерживается: {0}", SecureShownName(trueName, filePath)),
                                          locked:=False)
                    Return False
            End Select
            Return True
        Catch ex As Exception
            secure_Last_Attempt = SecureAttempt.DisplayFailure
            AppFileLogger.LogException("Secure container: displaying " & filePath, ex)
            container.Dispose()
            ShowSecurePlaceholder(filePath, Localization.T("Не удалось показать содержимое"), locked:=False)
            Return False
        End Try
    End Function

    ''' <summary>The cache belongs to the held credential. A dialog attempt with some other
    ''' password gets no cache at all - one derivation is the price of guessing, and it is
    ''' the price the format charges deliberately.</summary>
    Private Function SecureKeyCacheFor(credential As FdSecCredential) As FdSecKeyCache
        If secure_Session_Credential Is Nothing OrElse credential Is Nothing Then Return Nothing
        If Not ReferenceEquals(credential, secure_Session_Credential) AndAlso
           Not credential.SameAs(secure_Session_Credential) Then Return Nothing
        Return secure_Key_Cache
    End Function

    ''' <summary>The outcome-to-words map lives in SecureMessages, outside this form, so a
    ''' test can hold it to being exhaustive by type rather than by a chain of Ifs.</summary>
    Friend Shared Function SecureOutcomeMessage(outcome As FdSecOutcome) As String
        Return SecureMessages.ForOutcome(outcome)
    End Function

    ' ---------------------------------------------------------------- images ----

    ''' <summary>
    ''' A container image is read WHOLE into memory and decoded from there, which is what
    ''' the ordinary path does as well - the difference is only where the bytes came from.
    '''
    ''' The decode cache is bypassed entirely, in both directions (§5.4 rule 1): it writes
    ''' decoded payloads to .fmsdec files for exactly the formats a container is most
    ''' likely to hold, and a cache file IS the plaintext.
    ''' </summary>
    Private Function ShowSecureImage(container As FdSecContainer, filePath As String, trueName As String) As Boolean
        ' Zeroed when the picture box lets go of it (G-19), and sized up front so it never
        ' grows - a MemoryStream that grows leaves its earlier copies behind unwiped.
        Dim buffer As MemoryStream = Nothing
        Dim picture As Image = Nothing
        Try
            buffer = New FdSecWipingMemoryStream(CInt(Math.Min(container.ContentLength, Integer.MaxValue)))
            Using content As Stream = container.OpenContentStream()
                content.CopyTo(buffer)
            End Using
            buffer.Position = 0

            Dim extension As String = container.Metadata.ContentExtension()
            If ImageDecoderProvider.Decoder_Backed_Extensions.Contains(extension) Then
                ' AVIF/HEIC go through ImageMagick, whose pixel cache would otherwise spill
                ' the decrypted picture into %TEMP% once it outgrows memory (G-20).
                MagickResourceLimits.EnsureApplied()
                picture = ImageDecoderProvider.Current.DecodeToImage(buffer)
            Else
                Try
                    picture = Image.FromStream(buffer)
                Catch ex As ArgumentException When buffer.Length >= 12 AndAlso ImageDecoderProvider.LooksDecoderBacked(buffer.GetBuffer())
                    buffer.Position = 0
                    MagickResourceLimits.EnsureApplied()
                    picture = ImageDecoderProvider.Current.DecodeToImage(buffer)
                End Try
            End If
            If picture Is Nothing Then
                buffer.Dispose()
                secure_Last_Attempt = SecureAttempt.DisplayFailure
                ShowSecurePlaceholder(filePath, Localization.TF("Не удалось загрузить: {0}", SecureShownName(trueName, filePath)), locked:=False)
                Return False
            End If

            If Is_Exif_AutoRotate Then ApplyExifOrientation(picture)
        Catch ex As FdSecException
            ' A payload chunk that fails its tag is TAMPERING OR DAMAGE and is said as such
            ' (G-09) - not "could not load", which reads as a decoder problem. The head
            ' already authenticated, so the placeholder is not a locked one: another
            ' password would change nothing.
            picture?.Dispose()
            buffer?.Dispose()
            secure_Last_Attempt = SecurePolicy.AttemptFromContent(ex.Outcome)
            AppFileLogger.WriteLine("Secure container content refused (" & ex.Outcome.ToString() & "): " & filePath)
            ShowSecurePlaceholder(filePath, SecureOutcomeMessage(ex.Outcome), locked:=False)
            Return False
        Catch ex As Exception
            picture?.Dispose()
            buffer?.Dispose()
            secure_Last_Attempt = SecureAttempt.DisplayFailure
            AppFileLogger.LogException("Secure container: decoding the image in " & filePath, ex)
            ShowSecurePlaceholder(filePath, Localization.TF("Не удалось загрузить: {0}", SecureShownName(trueName, filePath)), locked:=False)
            Return False
        End Try

        ' The container itself is no longer needed: the picture and its stream are in
        ' memory, and holding the file open would only get in the way of a move or a
        ' delete on the container, which §7 says must keep working.
        container.Dispose()

        SetMediaDisplayKind(MediaKind.Image)
        is_WebBrowser_Visible = False
        PlaceDecodedImage(picture, buffer)
        StartGifLoopPlayback(If(is_PictureBox1_Visible, Picture_Box_1.Image, Picture_Box_2.Image))

        secure_Container_Path = filePath
        secure_True_Name = trueName
        secure_Content_Kind = MediaKind.Image
        secure_Placeholder_Locked = False
        current_Loaded_File_Name = filePath
        UpdateControlVisibility()
        ApplyNewImageScaleMode()
        If is_form_shown Then Draw_Perspective(animate:=True)

        AnnounceSecureContent(trueName)
        Return True
    End Function

    ' ------------------------------------------------------- video and audio ----

    ''' <summary>
    ''' Video and audio play straight out of the container through LibVLCSharp's
    ''' StreamMediaInput, so the decrypting stream IS the media input and no byte is ever
    ''' written anywhere. The stream must outlive the media player and is disposed with it
    ''' by <see cref="SecureReleaseCurrent"/>.
    ''' </summary>
    Private Sub ShowSecurePlayback(container As FdSecContainer, filePath As String, trueName As String, kind As MediaKind)
        secure_Container = container
        secure_Container_Path = filePath
        secure_True_Name = trueName
        secure_Content_Kind = kind
        secure_Content_Stream = container.OpenContentStream()
        ' LibVLC's StreamMediaInput turns an exception into "end of data", so a chunk that
        ' fails its tag would just stop the playback under a status that still says the
        ' file is open. The stream remembers the outcome and says so (G-09).
        Dim decrypting As FdSecStream = TryCast(secure_Content_Stream, FdSecStream)
        If decrypting IsNot Nothing Then AddHandler decrypting.Failed, AddressOf SecureContentStream_Failed
        secure_Vlc_Input = New LibVLCSharp.Shared.StreamMediaInput(secure_Content_Stream)

        secure_Placeholder_Locked = False
        SetMediaDisplayKind(kind)
        PlayVideoWithVlcAsync(filePath)
        AnnounceSecureContent(trueName)
    End Sub

    ''' <summary>LibVLC's reading thread: the message is posted to the UI, and only while
    ''' the same stream is still the one playing.</summary>
    Private Sub SecureContentStream_Failed(sender As Object, outcome As FdSecOutcome)
        AppFileLogger.WriteLine("Secure container content refused during playback (" & outcome.ToString() & ")")
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            Me.BeginInvoke(Sub()
                               If Not ReferenceEquals(sender, secure_Content_Stream) Then Return
                               lbl_Status.Text = SecureOutcomeMessage(outcome)
                           End Sub)
        Catch
            ' The form went away between the check and the post.
        End Try
    End Sub

    ''' <summary>
    ''' The media input for a path, or Nothing when this is an ordinary file. This is what
    ''' CreateVlcMedia asks, and it is the entire video integration: everything downstream
    ''' - the transport, track switching, remembered positions - works on the same objects
    ''' it always did.
    ''' </summary>
    Friend Function SecureMediaInputFor(filePath As String) As LibVLCSharp.Shared.StreamMediaInput
        If secure_Vlc_Input Is Nothing OrElse String.IsNullOrEmpty(filePath) Then Return Nothing
        If Not String.Equals(filePath, secure_Container_Path, StringComparison.OrdinalIgnoreCase) Then Return Nothing
        Return secure_Vlc_Input
    End Function

    ' ------------------------------------------------ LibVLC's art cache (G-04) ----

    ''' <summary>
    ''' Called by PlayVideoWithVlcAsync BEFORE Play, with the media it is about to play.
    ''' For a container's media it starts the art-cache watch and keeps the media (True -
    ''' the caller must not dispose it); for anything else it answers False and changes
    ''' nothing. Before Play, because LibVLC writes the cover while the input starts, and a
    ''' handler attached after Play returned could miss that one event.
    ''' </summary>
    Friend Function WatchSecureMediaArt(filePath As String, media As LibVLCSharp.Shared.Media) As Boolean
        If media Is Nothing OrElse SecureMediaInputFor(filePath) Is Nothing Then Return False
        ReleaseSecureArtWatch()
        SyncLock secure_Art_Lock
            secure_Vlc_Media = media
        End SyncLock
        AddHandler media.MetaChanged, AddressOf SecureVlcMedia_MetaChanged
        Return True
    End Function

    ''' <summary>LibVLC's own event thread: nothing is read back from the media here -
    ''' calling into LibVLC from inside its event is the pattern that deadlocks - so the
    ''' scrub is handed to the pool.</summary>
    Private Sub SecureVlcMedia_MetaChanged(sender As Object, e As LibVLCSharp.Shared.MediaMetaChangedEventArgs)
        If e.MetadataType <> LibVLCSharp.Shared.MetadataType.ArtworkURL Then Return
        Threading.ThreadPool.QueueUserWorkItem(Sub(state) ScrubSecureVlcArt())
    End Sub

    ''' <summary>Deletes whatever LibVLC's art cache now holds for the container's media.
    ''' Harmless when there is nothing (an attachment:// URL not yet extracted, no cover at
    ''' all). The log line never carries the path: the path IS the sealed artist and album.</summary>
    Private Sub ScrubSecureVlcArt()
        SyncLock secure_Art_Lock
            Dim media As LibVLCSharp.Shared.Media = secure_Vlc_Media
            If media Is Nothing Then Return
            Try
                VlcArtCache.Scrub(media.Meta(LibVLCSharp.Shared.MetadataType.ArtworkURL))
            Catch ex As Exception
                AppFileLogger.WriteLine("Secure container: LibVLC's cached cover could not be removed (" & ex.GetType().Name & ")")
            End Try
        End SyncLock
    End Sub

    ''' <summary>Ends the watch: one last scrub (a cover written while the handler was
    ''' being attached, or re-written by a restart), then the media is let go. Called by
    ''' SecureReleaseCurrent and before a new watch starts.</summary>
    Private Sub ReleaseSecureArtWatch()
        Dim media As LibVLCSharp.Shared.Media
        SyncLock secure_Art_Lock
            media = secure_Vlc_Media
            If media Is Nothing Then Return
            Try
                VlcArtCache.Scrub(media.Meta(LibVLCSharp.Shared.MetadataType.ArtworkURL))
            Catch ex As Exception
                AppFileLogger.WriteLine("Secure container: LibVLC's cached cover could not be removed (" & ex.GetType().Name & ")")
            End Try
            secure_Vlc_Media = Nothing
        End SyncLock
        Try
            RemoveHandler media.MetaChanged, AddressOf SecureVlcMedia_MetaChanged
            ' Only this reference: the player holds its own for as long as it plays.
            media.Dispose()
        Catch ex As Exception
            AppFileLogger.LogException("Secure container: releasing the watched media", ex)
        End Try
    End Sub

    ' ------------------------------------------------------------- the placeholder ----

    ''' <summary>
    ''' What a container that is not open looks like: the container's own name, a lock, one
    ''' line saying what is needed, and a way in. It is NOT an error and the status bar does
    ''' not shout (§3.3).
    '''
    ''' The picture box stays visible and holds a drawn placeholder rather than being
    ''' hidden, for the reason ShowUnsupportedFormat gives: the left click - this app's
    ''' "next file" gesture - only lands on a picture box or the form, so with every
    ''' surface hidden the user is stranded on a file they cannot even see.
    ''' </summary>
    Private Sub ShowSecurePlaceholder(filePath As String, message As String, locked As Boolean)
        ReleaseActiveMedia()
        SetMediaDisplayKind(MediaKind.Image)

        Dim box As PictureBox = Picture_Box_1
        If Not is_PictureBox1_Visible AndAlso Not is_PictureBox2_Visible Then
            is_PictureBox1_Visible = True
            is_Second_PictureBox_Active = False
        ElseIf is_PictureBox2_Visible Then
            box = Picture_Box_2
        End If
        is_WebBrowser_Visible = False

        Dim width As Integer = Math.Max(160, box.Width)
        Dim height As Integer = Math.Max(120, box.Height)
        Dim canvas As New Bitmap(width, height)
        Try
            Using g As Graphics = Graphics.FromImage(canvas)
                g.Clear(NeutralBackColor())
                g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit
                Dim ink As Color = If(Form_Color_Scheme = 2, Color.FromArgb(70, 70, 70), Color.FromArgb(210, 210, 210))
                ' ConvertFromUtf32, not ChrW: the padlock is outside the Basic Multilingual
                ' Plane and is a surrogate PAIR, which a Char cannot hold at all.
                Dim glyph As String = If(locked, Char.ConvertFromUtf32(&H1F512), Char.ConvertFromUtf32(&H26A0))
                Using big As New Font("Segoe UI Symbol", Math.Max(18.0F, Math.Min(64.0F, height / 8.0F)))
                    DrawCentred(g, glyph, big, ink, width, height * 0.32F)
                End Using
                Using nameFont As New Font("Segoe UI", 12.0F, FontStyle.Bold)
                    DrawCentred(g, Path.GetFileName(filePath), nameFont, ink, width, height * 0.5F)
                End Using
                Using messageFont As New Font("Segoe UI", 10.0F)
                    DrawCentred(g, message, messageFont, ink, width, height * 0.6F)
                End Using
            End Using
        Catch ex As Exception
            ' GDI+ can throw transiently; a placeholder that failed to draw is not worth
            ' losing the navigation over.
            canvas.Dispose()
            canvas = Nothing
            AppFileLogger.LogException("Secure container: drawing the placeholder", ex)
        End Try

        If box Is Picture_Box_2 Then
            ReleasePictureBoxMedia(2)
            Picture_Box_2.Image = canvas
            is_PictureBox2_Visible = True
            is_PictureBox1_Visible = False
            is_Second_PictureBox_Active = True
        Else
            ReleasePictureBoxMedia(1)
            Picture_Box_1.Image = canvas
            is_PictureBox1_Visible = True
            is_PictureBox2_Visible = False
            is_Second_PictureBox_Active = False
        End If

        ' There is no photograph here to derive a background from, so the previous file's
        ' bars and tint would otherwise stay up over a placeholder that is not it.
        ResetBackgroundToNeutral()
        UpdateControlVisibility()

        secure_Container_Path = ""
        secure_True_Name = ""
        secure_Content_Kind = MediaKind.Other
        secure_Placeholder_Locked = locked
        secure_Placeholder_Path = filePath
        current_Loaded_File_Name = filePath
        lbl_Status.Text = message
    End Sub

    Private Shared Sub DrawCentred(g As Graphics, text As String, font As Font, ink As Color,
                                   width As Integer, top As Single)
        If String.IsNullOrEmpty(text) Then Return
        Dim size As SizeF = g.MeasureString(text, font)
        Using brush As New SolidBrush(ink)
            g.DrawString(text, font, brush, (width - size.Width) / 2.0F, top)
        End Using
    End Sub

    Private Sub AnnounceSecureContent(trueName As String)
        ' "opened, authenticated" - and never "intact". A streaming reader authenticates
        ' every byte it touches and cannot speak for a chunk it never read (§4.2), so the
        ' status line says what it DID. "Verify this container" is the full guarantee.
        If GetModernPreferences().SecureShowTrueName Then
            lbl_Status.Text = Localization.TF("Открыт защищённый файл: {0}", trueName)
        Else
            lbl_Status.Text = Localization.T("Защищённый файл открыт")
        End If
    End Sub

    ' -------------------------------------------------------------- releasing ----

    ''' <summary>
    ''' Lets go of the open container, its content stream and the VLC media input. Called
    ''' from ReleaseActiveMedia, so every file operation and every navigation already goes
    ''' through it - a container that kept its handle would turn a move or a delete on the
    ''' container into an access-denied, and §7 says those keep working.
    ''' </summary>
    Friend Sub SecureReleaseCurrent()
        CancelSecureVerification()
        secure_Prompt_Pending_Path = ""
        secure_Prompt_Timer?.Stop()
        ReleaseSecureArtWatch()

        Dim input As LibVLCSharp.Shared.StreamMediaInput = secure_Vlc_Input
        Dim content As Stream = secure_Content_Stream
        Dim container As FdSecContainer = secure_Container
        secure_Vlc_Input = Nothing
        secure_Content_Stream = Nothing
        secure_Container = Nothing
        secure_Container_Path = ""
        secure_True_Name = ""
        secure_Content_Kind = MediaKind.Other
        secure_Placeholder_Locked = False
        secure_Placeholder_Path = ""

        Dim decrypting As FdSecStream = TryCast(content, FdSecStream)
        If decrypting IsNot Nothing Then RemoveHandler decrypting.Failed, AddressOf SecureContentStream_Failed

        Try
            input?.Dispose()
        Catch ex As Exception
            AppFileLogger.LogException("Secure container: releasing the media input", ex)
        End Try
        Try
            content?.Dispose()
        Catch ex As Exception
            AppFileLogger.LogException("Secure container: releasing the content stream", ex)
        End Try
        Try
            container?.Dispose()
        Catch ex As Exception
            AppFileLogger.LogException("Secure container: closing", ex)
        End Try
    End Sub

    ''' <summary>Called when the program closes: the credential and every key derived from
    ''' it go with the process, and nothing was ever written down to survive it.</summary>
    Friend Sub SecureShutdown()
        SecureReleaseCurrent()
        SecureForgetCredential(announce:=False)
    End Sub

    ' ------------------------------------------------------------- the refusals ----

    ''' <summary>
    ''' The one point that refuses an action that would put the plaintext on disk (§7), and
    ''' SAYS so - a key that silently does nothing reads as a broken program. Returns True
    ''' when the caller must stop.
    ''' </summary>
    Friend Function SecureBlocksWritingAction() As Boolean
        If Not IsSecureContentOnScreen() AndAlso Not IsCurrentFileSecureContainer() Then Return False
        lbl_Status.Text = Localization.T("Для защищённого файла это недоступно: расшифрованные данные не попадают на диск")
        Return True
    End Function

    ''' <summary>
    ''' Whether the OCR and translation caches may touch the disk for what is on screen.
    ''' False for a container: TranslationCache keys on path and write time and persists
    ''' recognised text - which for a container is the text of a secret (§5.4 rule 2).
    ''' </summary>
    Friend Function SecureAllowsDiskCache() As Boolean
        Return Not IsSecureContentOnScreen() AndAlso Not IsCurrentFileSecureContainer()
    End Function

    ''' <summary>
    ''' G-13: automatic OCR never runs on sealed content. With auto mode on, every container
    ''' picture that opened was recognised and its text handed to the translator - possibly
    ''' a remote one - without a per-file action. The explicit T still works.
    ''' </summary>
    Friend Function SecureAllowsAutomaticOcr() As Boolean
        Return SecureAllowsDiskCache()
    End Function

    ''' <summary>
    ''' G-13: before the recognised text of sealed content goes to a translator that is not
    ''' on this machine, the user says so, file by file. A loopback endpoint (the default
    ''' local Ollama) needs no question: nothing leaves. Returns False when the user
    ''' declined, and then says that nothing was sent.
    ''' </summary>
    Friend Function SecureConfirmsTranslatorEndpoint(endpoint As String) As Boolean
        If SecureAllowsDiskCache() Then Return True
        If SecurePolicy.IsLoopbackEndpoint(endpoint) Then Return True

        Dim answer As DialogResult = ViewerBox.Show(
            Me,
            Localization.TF("Текст защищённого файла будет отправлен переводчику на {0}. Продолжить?",
                            SecurePolicy.EndpointHost(endpoint)),
            Localization.T("Защищённый файл"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
        If answer = DialogResult.Yes Then Return True
        lbl_Status.Text = Localization.T("Перевод отменён: текст защищённого файла не отправлен")
        Return False
    End Function

    ''' <summary>
    ''' "Verify this container" (O-2, Ф-S5): read every chunk in order and check the
    ''' recovered bytes against the digest sealed at pack time. It is a visible action and
    ''' not something paid for on every open, because on a large container it is a full
    ''' read - and because the streaming reader's weaker guarantee is stated honestly
    ''' rather than papered over.
    ''' </summary>
    Friend Async Sub SecureVerifyCurrent()
        ' Invoking Verify again cancels the current read; navigation and shutdown do too.
        If secure_Verify_Cancellation IsNot Nothing Then
            CancelSecureVerification()
            lbl_Status.Text = secure_Verify_Previous_Status
            Return
        End If
        If Not IsCurrentFileSecureContainer() Then Return
        If Not SecureHasCredential() Then
            lbl_Status.Text = Localization.T("Нужен пароль - нажмите Enter или щёлкните по файлу")
            Return
        End If

        Dim filePath As String = Current_File_Name
        Dim previous As String = lbl_Status.Text
        secure_Verify_Previous_Status = previous
        Dim credential As FdSecCredential = secure_Session_Credential
        Dim cache As FdSecKeyCache = SecureKeyCacheFor(credential)
        Dim cancellation As New CancellationTokenSource()
        secure_Verify_Cancellation = cancellation
        Dim stillCurrent As Func(Of Boolean) =
            Function() Not IsDisposed AndAlso Not Disposing AndAlso
                       ReferenceEquals(secure_Verify_Cancellation, cancellation) AndAlso
                       String.Equals(Current_File_Name, filePath, StringComparison.OrdinalIgnoreCase)
        Dim progress As New Progress(Of (Completed As Long, Total As Long))(
            Sub(value)
                If stillCurrent() AndAlso Not cancellation.IsCancellationRequested Then
                    lbl_Status.Text = Localization.T("Проверка контейнера..") & " " &
                                      value.Completed.ToString() & "/" & value.Total.ToString()
                End If
            End Sub)
        lbl_Status.Text = Localization.T("Проверка контейнера..")

        Try
            Await Task.Run(
                Sub()
                    cancellation.Token.ThrowIfCancellationRequested()
                    Using probe As FdSecContainer = FdSecContainer.OpenFile(filePath, credential, keyCache:=cache)
                        Dim clock As Diagnostics.Stopwatch = Diagnostics.Stopwatch.StartNew()
                        probe.VerifyAll(cancellation.Token,
                            Sub(completed, total)
                                If clock.ElapsedMilliseconds >= 150 OrElse completed = total Then
                                    DirectCast(progress, IProgress(Of (Long, Long))).Report((completed, total))
                                    clock.Restart()
                                End If
                            End Sub)
                    End Using
                End Sub)
            If stillCurrent() Then lbl_Status.Text = Localization.T("Контейнер цел: все блоки прочитаны и подпись сошлась")
        Catch ex As OperationCanceledException
            If stillCurrent() Then lbl_Status.Text = previous
        Catch ex As FdSecException
            AppFileLogger.WriteLine("Secure container verify failed (" & ex.Outcome.ToString() & "): " & filePath)
            If stillCurrent() Then lbl_Status.Text = SecureOutcomeMessage(ex.Outcome)
        Catch ex As Exception
            AppFileLogger.LogException("Secure container: verifying " & filePath, ex)
            If stillCurrent() Then lbl_Status.Text = ReadFailureText(PathFailure.Classify(ex), filePath)
        Finally
            If ReferenceEquals(secure_Verify_Cancellation, cancellation) Then secure_Verify_Cancellation = Nothing
            cancellation.Dispose()
        End Try
    End Sub

    ' ---------------------------------------------------------------- prefetch ----

    ''' <summary>
    ''' Unseals the next container on the background thread, so that flipping onto it does
    ''' not pay the Argon2id derivation - which IS the visible cost of opening one, and the
    ''' only part of it a prefetch can usefully do in advance. The derived keys land in the
    ''' session cache; the display path then opens the same container for a few hundred
    ''' microseconds and classifies its content for itself.
    '''
    ''' Nothing is decoded here and nothing is handed back. A picture prefetched without
    ''' its sealed true name would have to be placed on screen under the container's name,
    ''' which is precisely the thing this feature exists not to do.
    '''
    ''' Called FROM the worker thread. It reads two fields and writes none; the derivation
    ''' lock inside FdSecKdf keeps it from running beside the display path's own derivation,
    ''' which at 64 MiB and four lanes is not something to do twice at once.
    ''' </summary>
    Private Sub PrefetchSecureContainer(filePath As String)
        Dim credential As FdSecCredential = secure_Session_Credential
        Dim cache As FdSecKeyCache = secure_Key_Cache
        If credential Is Nothing OrElse cache Is Nothing Then Return
        ' The idle timeout holds here too (G-19): an expired credential derives nothing
        ' more. Only the UI thread forgets it; this thread just declines to use it.
        If SecureCredentialExpired() Then Return

        Try
            FdSecContainer.OpenFile(filePath, credential, keyCache:=cache).Dispose()
        Catch ex As Exception
            ' A guess about the next file. The display path reports the reason if the user
            ' actually gets there, so a refusal here is silent on purpose.
        End Try
    End Sub

End Class
#End If
