#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Text
Imports Xunit

''' <summary>
''' The secure-boundary rows of audit 052 (docs/specifications/052_SPECIFICATION_AUDIT_ARCHIVE_SECURE_EDITOR.md
''' §5): each of these was a path that skipped a rule 026 states - an outcome class merged
''' in the UI, a sealed name on a failure surface, a tamper reported as a decode problem,
''' a credential that could not be wiped, a chunk size that overflowed instead of being
''' refused, sealed text in a developer dump. Modern-only, like src/Secure.
''' </summary>
Public Class SecureAuditTests

    Private Const Credential As String = "an audit credential"
    Private Const TrueName As String = "sealed-holiday.jpg"
    Private Const Alignment As Integer = 512
    Private Const ChunkSize As Integer = 1024

    Private Shared Function Payload(count As Integer) As Byte()
        Dim bytes(count - 1) As Byte
        For i As Integer = 0 To count - 1
            bytes(i) = CByte((i * 31 + 7) And &HFF)
        Next
        Return bytes
    End Function

    ' --------------------------------------------------------------- G-07 ----

    ''' <summary>The unlock decision over (screen passed, how the last try ended): a
    ''' password is asked for only when one could make a difference.</summary>
    <Theory>
    <InlineData(True, SecureAttempt.NotTried, True)>
    <InlineData(True, SecureAttempt.CredentialOrTamper, True)>
    <InlineData(True, SecureAttempt.Shown, False)>
    <InlineData(True, SecureAttempt.ContentAltered, False)>
    <InlineData(True, SecureAttempt.Damaged, False)>
    <InlineData(True, SecureAttempt.Unsupported, False)>
    <InlineData(True, SecureAttempt.NotMedia, False)>
    <InlineData(True, SecureAttempt.ReadFailure, False)>
    <InlineData(True, SecureAttempt.DisplayFailure, False)>
    <InlineData(False, SecureAttempt.NotTried, False)>
    <InlineData(False, SecureAttempt.CredentialOrTamper, False)>
    Public Sub A_password_is_asked_for_only_when_it_could_help(screenPassed As Boolean, attempt As SecureAttempt, expected As Boolean)
        Assert.Equal(expected, SecurePolicy.ShouldAskForPassword(screenPassed, attempt))
    End Sub

    ''' <summary>Every attempt value is covered by the decision above - a new one added to
    ''' the enum without a row here fails, rather than silently defaulting to "ask".</summary>
    <Fact>
    Public Sub Only_two_attempts_ever_lead_to_a_dialog()
        Dim asking = [Enum].GetValues(GetType(SecureAttempt)).Cast(Of SecureAttempt)().
            Where(Function(a) SecurePolicy.ShouldAskForPassword(True, a)).ToArray()
        Assert.Equal(New SecureAttempt() {SecureAttempt.NotTried, SecureAttempt.CredentialOrTamper}, asking)
    End Sub

    ''' <summary>A 1000-byte file cannot be a container, so it never gets a dialog: the
    ''' length screen is the gate, and it says no.</summary>
    <Fact>
    Public Sub A_file_that_fails_the_length_screen_is_never_asked_about()
        Dim screen As Boolean = FdSecScreen.CouldBeContainer(1000)
        Assert.False(screen)
        Assert.False(SecurePolicy.ShouldAskForPassword(screen, SecureAttempt.NotTried))
    End Sub

    ''' <summary>A tag failure while reading the CONTENT is the CredentialOrTamper class for
    ''' the user, but never a reason to ask for another password - the head already
    ''' authenticated.</summary>
    <Fact>
    Public Sub A_content_tag_failure_is_not_a_credential_question()
        Assert.Equal(SecureAttempt.ContentAltered, SecurePolicy.AttemptFromContent(FdSecOutcome.CredentialOrTamper))
        Assert.Equal(SecureAttempt.CredentialOrTamper, SecurePolicy.AttemptFromOpen(FdSecOutcome.CredentialOrTamper))
        Assert.Equal(SecureAttempt.Damaged, SecurePolicy.AttemptFromContent(FdSecOutcome.Damaged))
    End Sub

    ' --------------------------------------------------------------- G-09 ----

    ''' <summary>A flipped byte in chunk 1 is refused as CredentialOrTamper when that chunk
    ''' is read, and the stream remembers it - which is what lets playback say so instead of
    ''' just stopping.</summary>
    <Fact>
    Public Sub A_flipped_byte_in_chunk_one_is_reported_as_its_class_and_remembered()
        Dim content As Byte() = Payload(3000)
        Dim packed As Byte() = FdSecTestContainer.Pack(content, "clip.mp4", Credential,
                                                       FdSecTestContainer.FastProfile, Alignment, ChunkSize)
        packed(FdSecTestContainer.ChunkOffset(1, Alignment, ChunkSize) + 10) = packed(FdSecTestContainer.ChunkOffset(1, Alignment, ChunkSize) + 10) Xor CByte(&H40)

        Using container As FdSecContainer = FdSecContainer.Open(New MemoryStream(packed, writable:=False), Credential,
                                                                 ownsSource:=True, profile:=FdSecTestContainer.FastProfile)
            Using stream As FdSecStream = DirectCast(container.OpenContentStream(), FdSecStream)
                Dim raised As FdSecOutcome? = Nothing
                AddHandler stream.Failed, Sub(sender, outcome) raised = outcome

                ' Chunk 0 is intact and reads fine.
                Dim first(99) As Byte
                Assert.Equal(100, stream.Read(first, 0, first.Length))
                Assert.Null(stream.LastFailure)

                stream.Position = ChunkSize          ' inside chunk 1 (capacity 1008)
                Dim ex As FdSecException = Assert.Throws(Of FdSecException)(
                    Sub() stream.Read(New Byte(99) {}, 0, 100))
                Assert.Equal(FdSecOutcome.CredentialOrTamper, ex.Outcome)
                Assert.Equal(FdSecOutcome.CredentialOrTamper, stream.LastFailure)
                Assert.Equal(FdSecOutcome.CredentialOrTamper, raised)
            End Using
        End Using
    End Sub

    ' --------------------------------------------------------------- G-10 ----

    <Fact>
    Public Sub With_the_setting_off_a_failure_names_the_container_not_the_sealed_name()
        Dim container As String = Path.Combine("D:\photos", "IMG_0001.fd-sec")
        Assert.Equal("IMG_0001.fd-sec", SecurePolicy.ShownName(TrueName, container, showTrueName:=False))
        Assert.Equal(TrueName, SecurePolicy.ShownName(TrueName, container, showTrueName:=True))

        ' The three failure sentences of the display path, built the way it builds them.
        Dim shown As String = SecurePolicy.ShownName(TrueName, container, showTrueName:=False)
        For Each message As String In {
                Localization.TF("Формат не поддерживается: {0}", shown),
                Localization.TF("Не удалось загрузить: {0}", shown)}
            Assert.DoesNotContain(TrueName, message)
            Assert.Contains("IMG_0001.fd-sec", message)
        Next
        Assert.DoesNotContain(TrueName, Localization.T("Не удалось показать содержимое"))
    End Sub

    ' --------------------------------------------------------------- G-13 ----

    <Theory>
    <InlineData("", True)>
    <InlineData("http://localhost:11434", True)>
    <InlineData("localhost:5000", True)>
    <InlineData("http://127.0.0.1:5000/translate", True)>
    <InlineData("http://127.8.9.10", True)>
    <InlineData("http://[::1]:11434", True)>
    <InlineData("https://libretranslate.com", False)>
    <InlineData("http://192.168.1.20:11434", False)>
    <InlineData("http://ollama-box:11434", False)>
    <InlineData("::::not a url", False)>
    Public Sub Only_a_loopback_translator_counts_as_this_machine(endpoint As String, expected As Boolean)
        Assert.Equal(expected, SecurePolicy.IsLoopbackEndpoint(endpoint))
    End Sub

    ' --------------------------------------------------------------- E-12 ----

    <Fact>
    Public Sub Sealed_text_never_reaches_the_developer_dump()
        Assert.False(OcrDiagnostics.ShouldDump(dumpEnabled:=True, mayPersist:=False))
        Assert.False(OcrDiagnostics.ShouldDump(dumpEnabled:=False, mayPersist:=True))
        Assert.True(OcrDiagnostics.ShouldDump(dumpEnabled:=True, mayPersist:=True))
    End Sub

    ' --------------------------------------------------------------- G-18 ----

    ''' <summary>S = 2^31 is a legal u32 multiple of A, and no .NET array holds it: refused
    ''' by name as UNSUPPORTED, never an OverflowException (which no outcome class is).</summary>
    <Theory>
    <InlineData(&H80000000UI)>
    <InlineData(&HFFFFFE00UI)>
    Public Sub A_chunk_size_past_what_memory_can_hold_is_unsupported(declared As UInteger)
        Dim packed As Byte() = FdSecTestContainer.Pack(Payload(40), "a.bin", Credential,
                                                       FdSecTestContainer.FastProfile, Alignment, ChunkSize,
                                                       declaredChunkSize:=declared)
        Dim ex As FdSecException = Assert.Throws(Of FdSecException)(
            Sub() FdSecContainer.Open(New MemoryStream(packed, writable:=False), Credential,
                                      ownsSource:=True, profile:=FdSecTestContainer.FastProfile).Dispose())
        Assert.Equal(FdSecOutcome.Unsupported, ex.Outcome)
    End Sub

    ' --------------------------------------------------------------- G-19 ----

    <Fact>
    Public Sub Forgetting_the_credential_leaves_no_bytes_behind()
        Dim held As FdSecCredential = FdSecCredential.FromText(Credential)
        Dim bytes As Byte() = held.HeldBytesForTest
        Assert.Contains(bytes, Function(b) b <> 0)

        held.Dispose()

        Assert.True(held.IsDisposed)
        Assert.All(bytes, Sub(b) Assert.Equal(CByte(0), b))
        Assert.Throws(Of ObjectDisposedException)(Sub() held.CopyBytes())
    End Sub

    <Fact>
    Public Sub A_credential_held_as_bytes_opens_what_the_typed_one_opens()
        Dim content As Byte() = Payload(2500)
        Dim packed As Byte() = FdSecTestContainer.Pack(content, TrueName, Credential,
                                                       FdSecTestContainer.FastProfile, Alignment, ChunkSize)
        Using held As FdSecCredential = FdSecCredential.FromText(Credential)
            Using container As FdSecContainer = FdSecContainer.Open(New MemoryStream(packed, writable:=False), held,
                                                                     ownsSource:=True, profile:=FdSecTestContainer.FastProfile)
                Using stream As Stream = container.OpenContentStream(), copy As New MemoryStream()
                    stream.CopyTo(copy)
                    Assert.Equal(content, copy.ToArray())
                End Using
            End Using
            ' The open derived from a copy: the held credential is still whole.
            Assert.False(held.IsDisposed)
            Assert.True(held.SameAs(FdSecCredential.FromText(Credential)))
            Assert.False(held.SameAs(FdSecCredential.FromText(Credential & " ")))
        End Using
    End Sub

    <Fact>
    Public Sub The_plaintext_buffer_is_zeroed_when_it_is_let_go()
        Dim stream As New FdSecWipingMemoryStream(64)
        stream.Write(Payload(64), 0, 64)
        Dim buffer As Byte() = stream.GetBuffer()
        Assert.Contains(buffer, Function(b) b <> 0)

        stream.Dispose()

        Assert.All(buffer, Sub(b) Assert.Equal(CByte(0), b))
    End Sub

    ' --------------------------------------------------------------- G-20 ----

    <Fact>
    Public Sub ImageMagick_may_not_spill_a_pixel_cache_to_disk()
        MagickResourceLimits.EnsureApplied()
        Assert.Equal(0UL, MagickResourceLimits.CurrentDiskLimit())
    End Sub

End Class
#End If
