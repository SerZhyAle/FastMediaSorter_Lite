#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports Xunit

''' <summary>
''' The encrypted temp store of the animation videos (242_SPECIFICATION_ANIMATED_GRAPHICS_THROUGH_TEMPORARY_ENCRYPTED_VIDEO.md
''' rules 16-19, step T-3), on a real temp folder: the names and the sweep decision (pure), then the mechanics -
''' a sealed container is read back through the real reader, lives exactly as long as its lease, is opened by a
''' second handle while held, and leaves nothing behind when sealing fails at any point.
''' </summary>
Public Class AnimationVideoStoreTests
    Implements IDisposable

    Private Const SampleToken As String = "0123456789abcdef"
    Private Const Marker As String = "ftypisom-moov-mdat-RECOGNISABLE"

    Private ReadOnly root As String = Path.Combine(Path.GetTempPath(), "fms-avstore-test-" & Guid.NewGuid().ToString("N"))
    Private ReadOnly stores As New List(Of AnimationVideoStore)()

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each store As AnimationVideoStore In stores
            Try
                store.Dispose()
            Catch
            End Try
        Next
        Try
            If Directory.Exists(root) Then Directory.Delete(root, True)
        Catch
        End Try
    End Sub

    Private Function NewStore(Optional writer As AnimationVideoStore.SealWriter = Nothing,
                              Optional log As List(Of String) = Nothing) As AnimationVideoStore
        Dim hook As Action(Of String) = Nothing
        If log IsNot Nothing Then hook = Sub(message As String) log.Add(message)
        Return Register(If(writer Is Nothing,
                           New AnimationVideoStore(root, 4242, hook),
                           New AnimationVideoStore(root, 4242, hook, New AnimationVideoStore.SealSeam(writer))))
    End Function

    Private Function Register(store As AnimationVideoStore) As AnimationVideoStore
        stores.Add(store)
        Return store
    End Function

    ''' <summary>The real sealing writer, remembering the credential the store hands it - the only way a test sees the
    ''' per-run credential now that the store no longer exposes it.</summary>
    Private Shared Function CapturingWriter(captured As List(Of FdSecCredential)) As AnimationVideoStore.SealWriter
        Return Function(plain, destination, credential, token)
                   captured.Add(credential)
                   Return FdSecWriter.Write(plain, AnimationVideoStoreRules.SealedTrueName, credential, destination, token)
               End Function
    End Function

    ''' <summary>The bytes of a container that is held open by its writer (the share flags the store itself uses).</summary>
    Private Shared Function ReadRaw(containerPath As String) As Byte()
        Using second As New FileStream(containerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
            Return ReadAll(second)
        End Using
    End Function

    ''' <summary>Three chunks of pseudo-random bytes with a recognisable run planted at the start, the middle
    ''' and the end, so "no plaintext on the disk" has something to look for.</summary>
    Private Shared Function Mp4Like(size As Integer) As Byte()
        Dim bytes(size - 1) As Byte
        Call New Random(20261018).NextBytes(bytes)
        Dim planted As Byte() = Encoding.ASCII.GetBytes(Marker)
        For Each offset As Integer In {0, size \ 3, size \ 2, size - 64}
            Array.Copy(planted, 0, bytes, offset, planted.Length)
        Next
        Return bytes
    End Function

    Private Shared Function IndexOfRun(haystack As Byte(), needle As Byte()) As Integer
        For i As Integer = 0 To haystack.Length - needle.Length
            If haystack(i) <> needle(0) Then Continue For
            Dim same As Boolean = True
            For j As Integer = 1 To needle.Length - 1
                If haystack(i + j) <> needle(j) Then
                    same = False
                    Exit For
                End If
            Next
            If same Then Return i
        Next
        Return -1
    End Function

    ''' <summary>A delete-on-close close is synchronous, but a scanner can hold the name for a moment.</summary>
    Private Shared Function WaitUntil(condition As Func(Of Boolean)) As Boolean
        For attempt As Integer = 0 To 150
            If condition() Then Return True
            Thread.Sleep(20)
        Next
        Return condition()
    End Function

    Private Shared Function FilesIn(folder As String) As String()
        If Not Directory.Exists(folder) Then Return Array.Empty(Of String)()
        Return Directory.GetFiles(folder)
    End Function

    Private Shared Function ReadAll(stream As Stream) As Byte()
        Using buffer As New MemoryStream()
            stream.CopyTo(buffer)
            Return buffer.ToArray()
        End Using
    End Function

    Private Shared Function Slice(source As Byte(), offset As Integer, count As Integer) As Byte()
        Dim part(count - 1) As Byte
        Array.Copy(source, offset, part, 0, count)
        Return part
    End Function

    ''' <summary>After a failed seal with nothing else held: no file, no held lease and NO run folder - the store
    ''' removed it itself (spec 242 rule 27, "leaves nothing"; audit L-6). The helper deletes nothing: a folder that
    ''' is still there is a failure.</summary>
    Private Shared Sub AssertNothingLeft(store As AnimationVideoStore)
        Assert.True(WaitUntil(Function() FilesIn(store.RunFolder).Length = 0), "a container was left on the disk")
        Assert.Equal(0, store.LiveCount)
        Assert.True(WaitUntil(Function() Not Directory.Exists(store.RunFolder)), "an empty run folder was left behind")
    End Sub

    ' ---- the pure rules -----------------------------------------------------------------------------------------

    <Fact>
    Public Sub A_folder_name_round_trips_and_a_container_name_is_noise()
        Dim name As String = AnimationVideoStoreRules.FolderName(31337, SampleToken)
        Assert.Equal("31337-" & SampleToken, name)
        Dim pid As Integer
        Dim token As String = ""
        Assert.True(AnimationVideoStoreRules.TryParseFolderName(name, pid, token))
        Assert.Equal(31337, pid)
        Assert.Equal(SampleToken, token)

        Dim fresh As String = AnimationVideoStoreRules.NewToken()
        Assert.True(AnimationVideoStoreRules.TryParseFolderName(AnimationVideoStoreRules.FolderName(1, fresh), pid, token))
        Assert.Equal(fresh, token)

        Dim first As String = AnimationVideoStoreRules.NewContainerFileName()
        Dim second As String = AnimationVideoStoreRules.NewContainerFileName()
        Assert.Equal(32, first.Length)
        Assert.DoesNotContain(".", first)
        Assert.True(AnimationVideoStoreRules.IsContainerFileName(first))
        Assert.NotEqual(first, second)
        Assert.Equal("", Path.GetExtension(first))
        ' Only hex: nothing of an original's name, of the sealed name or of a media type can be in it.
        Assert.False(AnimationVideoStoreRules.IsContainerFileName("animation.mp4"))
        Assert.False(AnimationVideoStoreRules.IsContainerFileName(first & ".mp4"))
        Assert.False(AnimationVideoStoreRules.IsContainerFileName(first.ToUpperInvariant()))

        Assert.Equal("animation.mp4", AnimationVideoStoreRules.SealedTrueName)
        Assert.Equal("C:\Temp\FastMediaSorter-av", AnimationVideoStoreRules.DefaultRoot("C:\Temp"))
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("-")>
    <InlineData("123")>
    <InlineData("123-")>
    <InlineData("-123-0123456789abcdef")>
    <InlineData("0-0123456789abcdef")>
    <InlineData("007-0123456789abcdef")>
    <InlineData("+123-0123456789abcdef")>
    <InlineData("12 3-0123456789abcdef")>
    <InlineData("123-0123456789ABCDEF")>
    <InlineData("123-0123456789abcde")>
    <InlineData("123-0123456789abcdef0")>
    <InlineData("123-0123456789abcdeg")>
    <InlineData("123-0123456789abcdef-")>
    <InlineData("99999999999-0123456789abcdef")>
    <InlineData("١٢٣-0123456789abcdef")>
    <InlineData("..")>
    <InlineData("123-..\..\0123456789")>
    <InlineData("123-0123456789abcdef\..")>
    <InlineData("not-ours")>
    Public Sub A_hostile_folder_name_is_not_parsed(name As String)
        Dim pid As Integer = -1
        Dim token As String = "x"
        Assert.False(AnimationVideoStoreRules.TryParseFolderName(name, pid, token))
        Assert.Equal(0, pid)
        Assert.Equal("", token)
        ' and so it is never swept, however old and whoever "owns" it
        Assert.False(AnimationVideoStoreRules.ShouldSweep(name, Date.UtcNow.AddYears(-5), Date.UtcNow, Function(p) False))
    End Sub

    <Fact>
    Public Sub The_credential_is_long_random_and_per_run()
        Dim first As String = AnimationVideoStoreRules.NewCredentialText()
        Assert.True(Encoding.UTF8.GetByteCount(first) >= AnimationVideoStoreRules.CredentialMinUtf8Bytes)
        Assert.NotEqual(first, AnimationVideoStoreRules.NewCredentialText())

    End Sub

    ''' <summary>Audit L-3: the store no longer hands its credential out. What is left to prove is behaviour - a
    ''' container is bound to ITS store's credential: another store (another run) cannot open it, and the same video
    ''' sealed by two stores is two unrelated containers. The credential each store seals under (seen only through the
    ''' test seam) is long enough for the cheap key-derivation branch and differs per store.</summary>
    <Fact>
    Public Sub A_container_opens_only_under_its_own_stores_credential_and_two_stores_seal_unrelated_files()
        Dim heldA As New List(Of FdSecCredential)()
        Dim heldB As New List(Of FdSecCredential)()
        Dim a As AnimationVideoStore = NewStore(CapturingWriter(heldA))
        Dim b As AnimationVideoStore = NewStore(CapturingWriter(heldB))
        Dim payload As Byte() = Mp4Like(300000)

        Using leaseA As AnimationVideoLease = a.Seal(payload),
              leaseB As AnimationVideoLease = b.Seal(payload)
            Dim credentialA As FdSecCredential = heldA.Single()
            Dim credentialB As FdSecCredential = heldB.Single()
            Assert.True(credentialA.HeldBytesForTest.Length >= AnimationVideoStoreRules.CredentialMinUtf8Bytes)
            Assert.True(credentialB.HeldBytesForTest.Length >= AnimationVideoStoreRules.CredentialMinUtf8Bytes)
            Assert.False(credentialA.SameAs(credentialB))

            Dim fileA As String = FilesIn(a.RunFolder).Single()
            Dim fileB As String = FilesIn(b.RunFolder).Single()
            Assert.NotEqual(Path.GetDirectoryName(fileA), Path.GetDirectoryName(fileB))
            Assert.NotEqual(ReadRaw(fileA), ReadRaw(fileB))

            Using own As FdSecContainer = FdSecContainer.OpenFile(fileA, credentialA)
                own.VerifyAll()
            End Using
            Dim refused As FdSecException = Assert.Throws(Of FdSecException)(
                Sub() FdSecContainer.OpenFile(fileA, credentialB))
            Assert.Equal(FdSecOutcome.CredentialOrTamper, refused.Outcome)
            Assert.Throws(Of FdSecException)(Sub() FdSecContainer.OpenFile(fileB, credentialA))
            ' and a credential from nowhere opens nothing either
            Using stranger As FdSecCredential = FdSecCredential.FromText(AnimationVideoStoreRules.NewCredentialText())
                Assert.Throws(Of FdSecException)(Sub() FdSecContainer.OpenFile(fileA, stranger))
            End Using
        End Using
    End Sub

    ''' <summary>Audit L-4 and rule 25: the writer seam is reachable only through <c>SealSeam</c> and its own
    ''' constructor, and neither name occurs anywhere under <c>src\</c> but in the store's own file. A production caller
    ''' that wanted to bypass the sealing writer would have to name one of them.</summary>
    <Fact>
    Public Sub The_writer_seam_is_named_nowhere_in_the_product_but_the_store_itself()
        Dim srcFolder As String = SourceFolder()
        Dim storeFile As String = Path.GetFullPath(Path.Combine(srcFolder, "Video", "AnimationVideoStore.vb"))
        Assert.True(File.Exists(storeFile))
        Dim scanned As Integer = 0
        For Each candidate As String In Directory.EnumerateFiles(srcFolder, "*.vb", SearchOption.AllDirectories)
            Dim full As String = Path.GetFullPath(candidate)
            If String.Equals(full, storeFile, StringComparison.OrdinalIgnoreCase) Then Continue For
            If full.Contains("\obj\", StringComparison.OrdinalIgnoreCase) OrElse full.Contains("\bin\", StringComparison.OrdinalIgnoreCase) Then Continue For
            scanned += 1
            Dim text As String = File.ReadAllText(full)
            Assert.False(text.Contains("SealSeam", StringComparison.Ordinal), full & " names the store's writer seam")
            Assert.False(text.Contains("SealWriter", StringComparison.Ordinal), full & " names the store's writer delegate")
        Next
        Assert.True(scanned > 100, "the scan found too few source files to mean anything: " & scanned)
    End Sub

    ''' <summary>The same property on the type itself: exactly one constructor mentions a writer, and the one the
    ''' product calls takes none.</summary>
    <Fact>
    Public Sub Only_one_constructor_takes_a_writer_and_the_product_one_takes_none()
        Dim flags As System.Reflection.BindingFlags = System.Reflection.BindingFlags.Instance Or System.Reflection.BindingFlags.Public Or System.Reflection.BindingFlags.NonPublic
        Dim constructors = GetType(AnimationVideoStore).GetConstructors(flags)
        Dim withWriter = constructors.Where(Function(c) c.GetParameters().Any(Function(p) p.ParameterType = GetType(AnimationVideoStore.SealSeam) OrElse p.ParameterType = GetType(AnimationVideoStore.SealWriter))).ToArray()
        Assert.Single(withWriter)
        Assert.Equal(4, withWriter(0).GetParameters().Length)
        Dim product = constructors.Where(Function(c) Not withWriter.Contains(c)).ToArray()
        Assert.Single(product)
        Assert.DoesNotContain(product(0).GetParameters(), Function(p) p.ParameterType.Name.Contains("Seal", StringComparison.Ordinal))
    End Sub

    Private Shared Function SourceFolder() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "Video", "AnimationVideoStore.vb")
            If File.Exists(candidate) Then Return Path.Combine(dir.FullName, "src")
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src\Video\AnimationVideoStore.vb not found above " & AppContext.BaseDirectory)
    End Function

    <Fact>
    Public Sub The_sweep_decision_is_dead_pid_or_a_day_old_and_never_a_foreign_name()
        Dim current As Date = New Date(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc)
        Dim name As String = AnimationVideoStoreRules.FolderName(500, SampleToken)
        Dim live As Func(Of Integer, Boolean) = Function(pid) pid = 500
        Dim dead As Func(Of Integer, Boolean) = Function(pid) False

        Assert.True(AnimationVideoStoreRules.ShouldSweep(name, current.AddMinutes(-1), current, dead))
        Assert.False(AnimationVideoStoreRules.ShouldSweep(name, current.AddMinutes(-1), current, live))
        Assert.False(AnimationVideoStoreRules.ShouldSweep(name, current.AddHours(-23.9), current, live))
        Assert.True(AnimationVideoStoreRules.ShouldSweep(name, current.AddHours(-24), current, live))
        Assert.True(AnimationVideoStoreRules.ShouldSweep(name, current.AddDays(-9), current, live))
        ' a clock that ran backwards is not a reason to delete a live run
        Assert.False(AnimationVideoStoreRules.ShouldSweep(name, current.AddHours(3), current, live))
        ' not ours: untouched whatever the question says
        Assert.False(AnimationVideoStoreRules.ShouldSweep("Thumbs.db", current.AddDays(-9), current, dead))
        Assert.False(AnimationVideoStoreRules.ShouldSweep("500-short", current.AddDays(-9), current, dead))
    End Sub

    ' ---- the mechanics ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub A_sealed_video_reads_back_through_the_playback_stream_and_seeks()
        Dim payload As Byte() = Mp4Like(3000000)
        Dim store As AnimationVideoStore = NewStore()
        Using lease As AnimationVideoLease = store.Seal(payload)
            Assert.Equal(payload.LongLength, lease.Length)
            Dim stream As Stream = lease.OpenPlaybackStream()
            Assert.True(stream.CanSeek)
            Assert.False(stream.CanWrite)
            Assert.Equal(payload.LongLength, stream.Length)
            Assert.Equal(payload, ReadAll(stream))

            Dim middle As Integer = payload.Length \ 2
            Assert.Equal(CLng(middle), stream.Seek(middle, SeekOrigin.Begin))
            Dim window(4095) As Byte
            Assert.Equal(window.Length, stream.Read(window, 0, window.Length))
            Assert.Equal(Slice(payload, middle, window.Length), window)

            Assert.Equal(0L, stream.Seek(0, SeekOrigin.Begin))
            Dim head(63) As Byte
            Assert.Equal(head.Length, stream.Read(head, 0, head.Length))
            Assert.Equal(Slice(payload, 0, head.Length), head)

            ' a second stream over the same container is its own cursor
            Dim other As Stream = lease.OpenPlaybackStream()
            Assert.Equal(0L, other.Position)
            Assert.Equal(CLng(head.Length), stream.Position)
        End Using
    End Sub

    <Fact>
    Public Sub The_container_exists_while_held_is_named_by_noise_and_is_gone_after_dispose()
        Dim store As AnimationVideoStore = NewStore()
        Dim lease As AnimationVideoLease = store.Seal(Mp4Like(200000))
        Try
            Dim files As String() = FilesIn(store.RunFolder)
            Assert.Single(files)
            Dim name As String = Path.GetFileName(files(0))
            Assert.True(AnimationVideoStoreRules.IsContainerFileName(name))
            Assert.Equal("", Path.GetExtension(name))
            Dim pid As Integer
            Dim token As String = ""
            Assert.True(AnimationVideoStoreRules.TryParseFolderName(Path.GetFileName(store.RunFolder), pid, token))
            Assert.Equal(4242, pid)
            Assert.Equal(root, Path.GetDirectoryName(store.RunFolder))
            Assert.Equal(1, store.LiveCount)
        Finally
            lease.Dispose()
        End Try
        Assert.True(WaitUntil(Function() FilesIn(store.RunFolder).Length = 0), "the container outlived its lease")
        Assert.Equal(0, store.LiveCount)
    End Sub

    ''' <summary>The share-flag question of the design, answered on the real reader and the exact flags: a writer
    ''' handle opened CreateNew + DeleteOnClose + ReadWrite|Delete does not stop FdSecContainer.OpenFile (read,
    ''' share ReadWrite|Delete) from opening and verifying the file, and closing both deletes it.</summary>
    <Fact>
    Public Sub The_reader_opens_a_file_held_by_a_delete_on_close_writer_handle()
        Directory.CreateDirectory(root)
        Dim path_ As String = Path.Combine(root, "held")
        Dim payload As Byte() = Mp4Like(1500000)
        Dim credential As FdSecCredential = FdSecCredential.FromText(AnimationVideoStoreRules.NewCredentialText())
        Try
            Using writerHandle As New FileStream(path_, FileMode.CreateNew, FileAccess.Write,
                                                 FileShare.ReadWrite Or FileShare.Delete, 4096, FileOptions.DeleteOnClose)
                FdSecWriter.Write(payload, "animation.mp4", credential, writerHandle)
                writerHandle.Flush()
                Using container As FdSecContainer = FdSecContainer.OpenFile(path_, credential)
                    container.VerifyAll()
                    Assert.Equal(payload.LongLength, container.ContentLength)
                    Using content As Stream = container.OpenContentStream()
                        Assert.Equal(payload, ReadAll(content))
                    End Using
                End Using
                Assert.True(File.Exists(path_))
            End Using
            Assert.True(WaitUntil(Function() Not File.Exists(path_)), "closing the writer handle did not delete the file")
        Finally
            credential.Dispose()
        End Try
    End Sub

    <Fact>
    Public Sub A_second_handle_opens_the_container_by_name_while_it_is_held()
        Dim held As New List(Of FdSecCredential)()
        Dim store As AnimationVideoStore = NewStore(CapturingWriter(held))
        Using lease As AnimationVideoLease = store.Seal(Mp4Like(400000))
            Dim containerPath As String = FilesIn(store.RunFolder).Single()
            Using second As New FileStream(containerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                Dim head(31) As Byte
                Assert.Equal(head.Length, second.Read(head, 0, head.Length))
                Assert.True(second.Length >= 4096)
            End Using
            ' and the reader can open it again, independently of the lease's own
            Using again As FdSecContainer = FdSecContainer.OpenFile(containerPath, held.Single())
                again.VerifyAll()
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub No_plaintext_marker_reaches_the_container_on_disk()
        Dim payload As Byte() = Mp4Like(3000000)
        Assert.True(IndexOfRun(payload, Encoding.ASCII.GetBytes(Marker)) >= 0)
        Dim store As AnimationVideoStore = NewStore()
        Using lease As AnimationVideoLease = store.Seal(payload)
            Dim containerPath As String = FilesIn(store.RunFolder).Single()
            Dim raw As Byte()
            Using second As New FileStream(containerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                raw = ReadAll(second)
            End Using
            Assert.True(raw.Length >= payload.Length)
            Assert.Equal(0L, raw.LongLength Mod 4096)
            Assert.Equal(-1, IndexOfRun(raw, Encoding.ASCII.GetBytes(Marker)))
            Assert.Equal(-1, IndexOfRun(raw, Encoding.ASCII.GetBytes("ftypisom")))
            Assert.Equal(-1, IndexOfRun(raw, Encoding.ASCII.GetBytes("animation")))
            ' a stretch of the payload itself is not in the file either
            Dim stretch(63) As Byte
            Array.Copy(payload, 1000000, stretch, 0, stretch.Length)
            Assert.Equal(-1, IndexOfRun(raw, stretch))
        End Using
    End Sub

    <Fact>
    Public Sub Dispose_is_idempotent_and_closes_the_streams_it_handed_out()
        Dim store As AnimationVideoStore = NewStore()
        Dim lease As AnimationVideoLease = store.Seal(Mp4Like(100000))
        Dim stream As Stream = lease.OpenPlaybackStream()
        Assert.False(lease.IsDisposed)
        lease.Dispose()
        lease.Dispose()
        Assert.True(lease.IsDisposed)
        Assert.False(stream.CanRead)
        Assert.Throws(Of ObjectDisposedException)(Sub() lease.OpenPlaybackStream())
        Assert.True(WaitUntil(Function() FilesIn(store.RunFolder).Length = 0))
        Assert.Equal(0, store.LiveCount)
    End Sub

    <Fact>
    Public Sub A_cancelled_token_leaves_nothing()
        Dim store As AnimationVideoStore = NewStore()
        Using cts As New CancellationTokenSource()
            cts.Cancel()
            Assert.ThrowsAny(Of OperationCanceledException)(Sub() store.Seal(Mp4Like(5000), cts.Token))
        End Using
        AssertNothingLeft(store)
    End Sub

    <Fact>
    Public Sub A_cancel_between_chunks_leaves_nothing()
        Using cts As New CancellationTokenSource()
            Dim writer As AnimationVideoStore.SealWriter =
                Function(plain, destination, credential, token) FdSecWriter.Write(
                    plain, "animation.mp4", credential, New CancelOnWriteStream(destination, cts), token)
            Dim log As New List(Of String)()
            Dim store As AnimationVideoStore = NewStore(writer, log)
            Assert.ThrowsAny(Of OperationCanceledException)(Sub() store.Seal(Mp4Like(3000000), cts.Token))
            AssertNothingLeft(store)
            ' a cancel is not a failure to report
            Assert.DoesNotContain(log, Function(line) line.Contains("failed"))
        End Using
    End Sub

    <Fact>
    Public Sub A_writer_that_throws_in_the_middle_leaves_nothing()
        Dim writer As AnimationVideoStore.SealWriter =
            Function(plain, destination, credential, token)
                destination.Write(New Byte(9999) {}, 0, 10000)
                Throw New IOException("the disk is full")
            End Function
        Dim log As New List(Of String)()
        Dim store As AnimationVideoStore = NewStore(writer, log)
        Assert.Throws(Of IOException)(Sub() store.Seal(Mp4Like(5000)))
        AssertNothingLeft(store)
        Assert.Contains(log, Function(line) line.Contains("IOException"))
        ' a message can carry a path: only the type and code are logged
        Assert.DoesNotContain(log, Function(line) line.Contains("disk is full"))
    End Sub

    <Fact>
    Public Sub A_container_that_does_not_verify_is_refused_and_removed()
        Dim writer As AnimationVideoStore.SealWriter =
            Function(plain, destination, credential, token)
                Using sealedOnce As New MemoryStream()
                    FdSecWriter.Write(plain, "animation.mp4", credential, sealedOnce)
                    Dim bytes As Byte() = sealedOnce.ToArray()
                    bytes(bytes.Length \ 2) = CByte(bytes(bytes.Length \ 2) Xor &HFF)
                    destination.Write(bytes, 0, bytes.Length)
                    Return bytes.LongLength
                End Using
            End Function
        Dim store As AnimationVideoStore = NewStore(writer)
        Assert.ThrowsAny(Of FdSecException)(Sub() store.Seal(Mp4Like(3000000)))
        AssertNothingLeft(store)
    End Sub

    <Fact>
    Public Sub A_container_sealed_over_other_bytes_is_refused_and_removed()
        Dim writer As AnimationVideoStore.SealWriter =
            Function(plain, destination, credential, token) FdSecWriter.Write(
                New Byte(plain.Length - 1) {}, "animation.mp4", credential, destination)
        Dim store As AnimationVideoStore = NewStore(writer)
        Assert.Throws(Of InvalidDataException)(Sub() store.Seal(Mp4Like(50000)))
        AssertNothingLeft(store)
    End Sub

    <Fact>
    Public Sub A_container_sealed_under_another_name_is_refused_and_removed()
        Dim writer As AnimationVideoStore.SealWriter =
            Function(plain, destination, credential, token) FdSecWriter.Write(
                plain, "holiday.mp4", credential, destination)
        Dim store As AnimationVideoStore = NewStore(writer)
        Assert.Throws(Of InvalidDataException)(Sub() store.Seal(Mp4Like(50000)))
        AssertNothingLeft(store)
    End Sub

    <Fact>
    Public Sub An_empty_video_is_refused_before_any_file_is_made()
        Dim store As AnimationVideoStore = NewStore()
        Assert.Throws(Of ArgumentException)(Sub() store.Seal(New Byte(-1) {}))
        Assert.Empty(FilesIn(store.RunFolder))
        Assert.False(Directory.Exists(store.RunFolder))
    End Sub

    <Fact>
    Public Sub The_store_holds_at_most_the_current_and_one_prepared_container()
        Dim store As AnimationVideoStore = NewStore()
        Dim first As AnimationVideoLease = store.Seal(Mp4Like(60000))
        Dim second As AnimationVideoLease = store.Seal(Mp4Like(70000))
        Try
            Assert.Equal(2, store.LiveCount)
            Assert.Throws(Of InvalidOperationException)(Sub() store.Seal(Mp4Like(80000)))
            Assert.Equal(2, FilesIn(store.RunFolder).Length)
            first.Dispose()
            Assert.Equal(1, store.LiveCount)
            Using third As AnimationVideoLease = store.Seal(Mp4Like(80000))
                Assert.Equal(80000L, third.Length)
                Assert.Equal(2, store.LiveCount)
            End Using
        Finally
            first.Dispose()
            second.Dispose()
        End Try
    End Sub

    <Fact>
    Public Sub ReleaseAll_disposes_every_lease_removes_the_folder_and_leaves_the_store_usable()
        Dim store As AnimationVideoStore = NewStore()
        Dim first As AnimationVideoLease = store.Seal(Mp4Like(60000))
        Dim second As AnimationVideoLease = store.Seal(Mp4Like(70000))
        Dim stream As Stream = second.OpenPlaybackStream()

        store.ReleaseAll()
        Assert.True(first.IsDisposed)
        Assert.True(second.IsDisposed)
        Assert.False(stream.CanRead)
        Assert.Equal(0, store.LiveCount)
        Assert.True(WaitUntil(Function() Not Directory.Exists(store.RunFolder)), "the run folder was not removed")
        store.ReleaseAll()

        Using again As AnimationVideoLease = store.Seal(Mp4Like(1000))
            Assert.True(Directory.Exists(store.RunFolder))
            Assert.Single(FilesIn(store.RunFolder))
        End Using

        store.Dispose()
        store.Dispose()
        Assert.False(Directory.Exists(store.RunFolder))
        Assert.Throws(Of ObjectDisposedException)(Sub() store.Seal(Mp4Like(1000)))
    End Sub

    <Fact>
    Public Sub The_log_hook_never_sees_the_credential_or_the_sealed_name()
        Dim log As New List(Of String)()
        Dim seen As New List(Of FdSecCredential)()
        Dim failing As AnimationVideoStore.SealWriter =
            Function(plain, destination, credential, token)
                seen.Add(credential)
                Throw New InvalidOperationException("holiday.mp4 animation.mp4")
            End Function
        Dim store As AnimationVideoStore = NewStore(failing, log)
        Assert.Throws(Of InvalidOperationException)(Sub() store.Seal(Mp4Like(1000)))
        Dim credentialText As String = Encoding.UTF8.GetString(seen.Single().HeldBytesForTest)
        store.ReleaseAll()
        Assert.NotEmpty(log)
        For Each line As String In log
            Assert.DoesNotContain(credentialText, line)
            Assert.DoesNotContain("animation.mp4", line)
            Assert.DoesNotContain("holiday", line)
        Next
    End Sub

    <Fact>
    Public Sub A_failed_seal_gives_its_place_back_so_failures_never_use_up_the_ceiling()
        Dim failing As AnimationVideoStore.SealWriter =
            Function(plain, destination, credential, token)
                Throw New IOException("boom")
            End Function
        Dim store As AnimationVideoStore = NewStore(failing)
        For attempt As Integer = 1 To AnimationVideoStore.MaxLeases * 3
            Assert.Throws(Of IOException)(Sub() store.Seal(Mp4Like(1000)))
        Next
        AssertNothingLeft(store)
    End Sub

    ''' <summary>Audit L-6: the folder a failed seal made goes with it - but only when nothing else lives in it. A
    ''' failure while another container is held must leave the folder (and the held container) alone.</summary>
    <Fact>
    Public Sub A_failed_seal_keeps_the_run_folder_while_another_container_is_held()
        Dim calls As Integer = 0
        Dim flaky As AnimationVideoStore.SealWriter =
            Function(plain, destination, credential, token)
                calls += 1
                If calls = 2 Then Throw New IOException("second one fails")
                Return FdSecWriter.Write(plain, AnimationVideoStoreRules.SealedTrueName, credential, destination, token)
            End Function
        Dim store As AnimationVideoStore = NewStore(flaky)
        Using first As AnimationVideoLease = store.Seal(Mp4Like(60000))
            Assert.Throws(Of IOException)(Sub() store.Seal(Mp4Like(70000)))
            Assert.Equal(1, store.LiveCount)
            Assert.True(Directory.Exists(store.RunFolder))
            Assert.Single(FilesIn(store.RunFolder))
            Using stream As Stream = first.OpenPlaybackStream()
                Assert.Equal(60000L, stream.Length)
            End Using
        End Using
        ' the held one released: nothing but the empty folder is left, and a release of the store takes that too
        store.ReleaseAll()
        AssertNothingLeft(store)
    End Sub

    ''' <summary>Audit L-6 (b): the viewer exits (the store is disposed) while a seal is still writing. The seal fails,
    ''' closes its file, and the folder does not outlive it.</summary>
    <Fact>
    Public Sub A_dispose_in_the_middle_of_a_seal_leaves_no_file_and_no_folder()
        Dim holder As AnimationVideoStore = Nothing
        Dim midWrite As AnimationVideoStore.SealWriter =
            Function(plain, destination, credential, token)
                destination.Write(New Byte(4999) {}, 0, 5000)
                holder.Dispose()
                Return 5000L
            End Function
        holder = NewStore(midWrite)
        Assert.ThrowsAny(Of Exception)(Sub() holder.Seal(Mp4Like(5000)))
        AssertNothingLeft(holder)
    End Sub

    ''' <summary>Audit L-7: a foreign log hook is never run while the store's lock is held. The hook here asks the store
    ''' a question from ANOTHER thread (which needs the lock) and waits for the answer; if ReleaseAll still held the
    ''' lock while logging, the question could not be answered until ReleaseAll returned.</summary>
    <Fact>
    Public Sub ReleaseAll_does_not_call_the_log_hook_while_holding_the_lock()
        Dim answered As New List(Of Boolean)()
        Dim store As AnimationVideoStore = Nothing
        Dim hook As Action(Of String) =
            Sub(message As String)
                answered.Add(System.Threading.Tasks.Task.Run(Function() store.LiveCount).Wait(TimeSpan.FromSeconds(10)))
            End Sub
        store = Register(New AnimationVideoStore(root, 4242, hook))

        Using lease As AnimationVideoLease = store.Seal(Mp4Like(5000))
            ' a stray file that is not ours keeps the folder from going, so ReleaseAll has something to report
            File.WriteAllBytes(Path.Combine(store.RunFolder, "stray"), New Byte(9) {})
            store.ReleaseAll()
        End Using

        Assert.NotEmpty(answered)
        Assert.All(answered, Sub(reachable) Assert.True(reachable, "the store's lock was held while the hook ran"))
        Directory.Delete(store.RunFolder, True)
    End Sub

    ' ---- the sweep ----------------------------------------------------------------------------------------------

    Private Function MakeRunFolder(name As String, Optional age As TimeSpan = Nothing) As String
        Dim folder As String = Path.Combine(root, name)
        Directory.CreateDirectory(folder)
        File.WriteAllBytes(Path.Combine(folder, "0123456789abcdef0123456789abcdef"), New Byte(99) {})
        If age <> TimeSpan.Zero Then Directory.SetLastWriteTimeUtc(folder, Date.UtcNow - age)
        Return folder
    End Function

    <Fact>
    Public Sub The_sweep_removes_dead_and_day_old_run_folders_and_keeps_the_rest()
        Directory.CreateDirectory(root)
        Dim dead As String = MakeRunFolder(AnimationVideoStoreRules.FolderName(100, SampleToken))
        Dim live As String = MakeRunFolder(AnimationVideoStoreRules.FolderName(200, SampleToken))
        Dim liveButOld As String = MakeRunFolder(AnimationVideoStoreRules.FolderName(300, SampleToken), TimeSpan.FromHours(25))
        Dim foreign As String = MakeRunFolder("keep-me")
        Dim lookalike As String = MakeRunFolder("100-NOTHEXNOTHEXNOTH", TimeSpan.FromDays(30))
        Dim looseFile As String = Path.Combine(root, AnimationVideoStoreRules.FolderName(100, "fedcba9876543210"))
        File.WriteAllBytes(looseFile, New Byte(9) {})

        Dim log As New List(Of String)()
        Dim removed As Integer = AnimationVideoStore.Sweep(root, Date.UtcNow, Function(pid) pid = 200 OrElse pid = 300,
                                                           Sub(line) log.Add(line))
        Assert.Equal(2, removed)
        Assert.False(Directory.Exists(dead))
        Assert.True(Directory.Exists(live))
        Assert.False(Directory.Exists(liveButOld))
        Assert.True(Directory.Exists(foreign))
        Assert.True(Directory.Exists(lookalike))
        Assert.True(File.Exists(looseFile))
        Assert.Contains(log, Function(line) line.Contains("swept 2"))
    End Sub

    <Fact>
    Public Sub The_sweep_of_a_missing_root_is_nothing()
        Assert.Equal(0, AnimationVideoStore.Sweep(Path.Combine(root, "nowhere"), Date.UtcNow, Function(pid) False))
        Assert.Equal(0, AnimationVideoStore.Sweep("", Date.UtcNow, Function(pid) False))
        Assert.Throws(Of ArgumentNullException)(Sub() AnimationVideoStore.Sweep(root, Date.UtcNow, Nothing))
    End Sub

    <Fact>
    Public Sub A_folder_that_refuses_to_go_stays_without_an_error_and_goes_on_the_next_sweep()
        Directory.CreateDirectory(root)
        Dim stuck As String = MakeRunFolder(AnimationVideoStoreRules.FolderName(100, SampleToken))
        Dim other As String = MakeRunFolder(AnimationVideoStoreRules.FolderName(101, SampleToken))
        Dim heldFile As String = Path.Combine(stuck, "held")
        Dim log As New List(Of String)()

        Dim removed As Integer
        Using held As New FileStream(heldFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None)
            held.WriteByte(7)
            held.Flush()
            removed = AnimationVideoStore.Sweep(root, Date.UtcNow, Function(pid) False, Sub(line) log.Add(line))
            ' the file nobody could delete is untouched, and the sweep did not need to read it
            Assert.True(File.Exists(heldFile))
            held.Position = 0
            Assert.Equal(7, held.ReadByte())
        End Using

        Assert.Equal(1, removed)
        Assert.False(Directory.Exists(other))
        Assert.True(Directory.Exists(stuck))
        Assert.Contains(log, Function(line) line.Contains("was not removed"))

        Assert.Equal(1, AnimationVideoStore.Sweep(root, Date.UtcNow, Function(pid) False))
        Assert.False(Directory.Exists(stuck))
    End Sub

    ''' <summary>The sweep never opens a file (rule 18): the source of its body names no file API at all, only the
    ''' directory ones, so a future "just check the container header" cannot slip in unnoticed.</summary>
    <Fact>
    Public Sub The_sweep_body_names_no_file_api()
        Dim body As String = SweepBody()
        For Each forbidden As String In {"FileStream", "File.Open", "File.Read", "File.Exists", "OpenRead", "OpenFile",
                                         "ReadAll", "GetFiles", "EnumerateFiles", "EnumerateFileSystemEntries", "StreamReader"}
            Assert.DoesNotContain(forbidden, body)
        Next
        Assert.Contains("Directory.Delete(", body)
    End Sub

    <Fact>
    Public Sub The_store_source_has_no_product_dependency()
        Dim source As String = StoreSource()
        For Each forbidden As String In {"AppFileLogger", "Common_Module", "Main_Form", "Registry", "My.Settings", "Localization"}
            Assert.DoesNotContain(forbidden, source)
        Next
    End Sub

    Private Shared Function StoreSource() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "Video", "AnimationVideoStore.vb")
            If File.Exists(candidate) Then Return File.ReadAllText(candidate)
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src\Video\AnimationVideoStore.vb not found above " & AppContext.BaseDirectory)
    End Function

    Private Shared Function SweepBody() As String
        Dim source As String = StoreSource()
        Dim start As Integer = source.IndexOf("Friend Shared Function Sweep(", StringComparison.Ordinal)
        Assert.True(start >= 0, "Sweep not found")
        Dim finish As Integer = source.IndexOf("End Function", start, StringComparison.Ordinal)
        Assert.True(finish > start)
        Return source.Substring(start, finish - start)
    End Function

    ''' <summary>Cancels the token as soon as the first bytes are written, so the writer stops BETWEEN chunks with
    ''' a partial container already on the disk.</summary>
    Private NotInheritable Class CancelOnWriteStream
        Inherits Stream

        Private ReadOnly inner As Stream
        Private ReadOnly source As CancellationTokenSource

        Friend Sub New(inner As Stream, source As CancellationTokenSource)
            Me.inner = inner
            Me.source = source
        End Sub

        Public Overrides ReadOnly Property CanRead As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides ReadOnly Property CanSeek As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides ReadOnly Property CanWrite As Boolean
            Get
                Return True
            End Get
        End Property

        Public Overrides ReadOnly Property Length As Long
            Get
                Throw New NotSupportedException()
            End Get
        End Property

        Public Overrides Property Position As Long
            Get
                Throw New NotSupportedException()
            End Get
            Set(value As Long)
                Throw New NotSupportedException()
            End Set
        End Property

        Public Overrides Sub Write(buffer() As Byte, offset As Integer, count As Integer)
            inner.Write(buffer, offset, count)
            source.Cancel()
        End Sub

        Public Overrides Sub Flush()
            inner.Flush()
        End Sub

        Public Overrides Function Read(buffer() As Byte, offset As Integer, count As Integer) As Integer
            Throw New NotSupportedException()
        End Function

        Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
            Throw New NotSupportedException()
        End Function

        Public Overrides Sub SetLength(value As Long)
            Throw New NotSupportedException()
        End Sub
    End Class

End Class
#End If
