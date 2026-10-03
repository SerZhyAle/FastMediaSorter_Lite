Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The host half of WORKER-IPC section 7's conformance artifact - ticket 035 phase 5.
'''
''' Section 7 asks for a recorded exchange per request type: the request bytes, and a response the
''' host must accept. The two halves have different owners. The REQUEST bytes are this host's:
''' they are what its own serializer puts on the pipe, so they are generated here, by the product
''' code, and pinned byte for byte. The RESPONSE half can only honestly come from the worker's
''' harness - the one implementation that produces a response nobody hand-wrote - so it is
''' vendored, digest-checked, once the owner publishes it.
'''
''' Generated, never hand-edited (the catalog's RULES section 9). To regenerate after a deliberate
''' wire change, run the suite with FMS_REGENERATE_WORKER_IPC=1: the generator rewrites
''' tests/Companion.Tests/TestData/worker-ipc/requests/ from the cases below, PROVENANCE.txt
''' included, and the pin then holds the new bytes. A diff in those files is a wire change and
''' reads like one in review.
''' </summary>
Public Class WorkerIpcConformanceTests

    Private Const RegenerateVariable As String = "FMS_REGENERATE_WORKER_IPC"

    ''' <summary>Every request type this host sends, each built the way the product builds it.
    ''' File name = case name + ".json".</summary>
    Friend Shared Function RequestCases() As SortedDictionary(Of String, WorkerRequest)
        Dim c As New SortedDictionary(Of String, WorkerRequest)(StringComparer.Ordinal)
        c("get-status") = New WorkerRequest With {.type = "GetStatus"}
        ' Two roots, both readOnly values, a backslash and a non-ASCII name - the escaping of
        ' the last two is exactly what a second implementation gets wrong.
        c("set-shared-folders") = New WorkerRequest With {
            .type = "SetSharedFolders",
            .folders = New List(Of ShareFolder) From {
                New ShareFolder With {.name = "Photos", .hostPath = "C:\Users\me\Pictures", .readOnly = False},
                New ShareFolder With {.name = "Фото", .hostPath = "D:\Архив\Фото", .readOnly = True}
            }
        }
        c("start-server") = New WorkerRequest With {.type = "StartServer"}
        c("stop-server") = New WorkerRequest With {.type = "StopServer"}
        c("reset-stats") = New WorkerRequest With {.type = "ResetStats"}
        ' The policy push as the product builds it: every knob, then no recorded port (the
        ' port OMITTED - rule 4), then the port alone.
        c("set-network-policy-full") = ShareController.NetworkPolicyRequest(10, True, 2222)
        c("set-network-policy-partial") = ShareController.NetworkPolicyRequest(10, False, ShareSettings.UnsetPort)
        c("set-network-policy-port-only") = New WorkerRequest With {.type = "SetNetworkPolicy", .port = 2222}
        c("disk-share") = New WorkerRequest With {.type = "disk-share", .owner = "filedo", .action = "share", .containerPath = "D:\Demo\photos.fdd", .rootName = "Photos", .readOnly = True}
        c("disk-unshare") = New WorkerRequest With {.type = "disk-share", .owner = "filedo", .action = "unshare", .containerPath = "D:\Demo\photos.fdd", .drainTimeout = 30}
        c("disk-open") = New WorkerRequest With {.type = "disk-open", .owner = "filedo", .containerPath = "D:\Demo\photos.fdd", .password = "synthetic-vector-credential"}
        c("disk-close") = New WorkerRequest With {.type = "disk-close", .owner = "filedo", .containerPath = "D:\Demo\photos.fdd", .force = False, .drainTimeout = 30}
        c("disk-autostart-on") = New WorkerRequest With {.type = "disk-autostart", .owner = "filedo", .containerPath = "D:\Demo\photos.fdd", .enable = True, .consent = True, .password = "synthetic-vector-credential"}
        c("disk-autostart-off") = New WorkerRequest With {.type = "disk-autostart", .owner = "filedo", .containerPath = "D:\Demo\photos.fdd", .enable = False}
        c("disk-status") = New WorkerRequest With {.type = "disk-status", .owner = "filedo", .containerPath = "D:\Demo\photos.fdd"}
        c("disk-list") = New WorkerRequest With {.type = "disk-list", .owner = "filedo"}
        Return c
    End Function

    Private Shared Function RequestsDirInOutput() As String
        Return Path.Combine(AppContext.BaseDirectory, "TestData", "worker-ipc", "requests")
    End Function

    Private Shared Function ResponsesDirInOutput() As String
        Return Path.Combine(AppContext.BaseDirectory, "TestData", "worker-ipc", "responses")
    End Function

    ''' <summary>The generator. A no-op unless asked for - then it writes into the SOURCE tree,
    ''' never the build output, so the result is what gets committed.</summary>
    <Fact>
    Public Sub Generate_RequestFixtures_WhenAsked()
        If Environment.GetEnvironmentVariable(RegenerateVariable) <> "1" Then Return
        Dim root As String = FindRepoRoot()
        Assert.False(root Is Nothing, "repository root not found above " & AppContext.BaseDirectory)
        Dim dir As String = Path.Combine(root, "tests", "Companion.Tests", "TestData", "worker-ipc", "requests")
        Directory.CreateDirectory(dir)
        For Each f As String In Directory.GetFiles(dir, "*.json")
            File.Delete(f)
        Next

        Dim provenance As New StringBuilder()
        provenance.Append(ProvenanceHeader())
        For Each kv As KeyValuePair(Of String, WorkerRequest) In RequestCases()
            Dim bytes As Byte() = WorkerIpc.SerializeRequest(kv.Value)
            File.WriteAllBytes(Path.Combine(dir, kv.Key & ".json"), bytes)
            provenance.Append("  SHA-256  ").Append(HexDigest(bytes)).Append("  ").Append(kv.Key).Append(".json").Append(vbLf)
        Next
        File.WriteAllText(Path.Combine(dir, "PROVENANCE.txt"), provenance.ToString(), New UTF8Encoding(False))
    End Sub

    ''' <summary>The pin: every case's committed file holds exactly the bytes the product
    ''' serializer emits today, the set of files is exactly the set of cases, and PROVENANCE.txt
    ''' carries each file's digest.</summary>
    <Fact>
    Public Sub RequestFixtures_MatchTheProductSerializer_ByteForByte()
        If Environment.GetEnvironmentVariable(RegenerateVariable) = "1" Then Return ' being rewritten
        Dim dir As String = RequestsDirInOutput()
        Assert.True(Directory.Exists(dir), "missing " & dir & " - run with " & RegenerateVariable & "=1 once")

        Dim cases As SortedDictionary(Of String, WorkerRequest) = RequestCases()
        Dim onDisk As String() = Directory.GetFiles(dir, "*.json").
            Select(Function(f) Path.GetFileNameWithoutExtension(f)).
            OrderBy(Function(n) n, StringComparer.Ordinal).ToArray()
        Assert.Equal(cases.Keys.ToArray(), onDisk)

        Dim provenance As String = File.ReadAllText(Path.Combine(dir, "PROVENANCE.txt"))
        For Each kv As KeyValuePair(Of String, WorkerRequest) In cases
            Dim expected As Byte() = WorkerIpc.SerializeRequest(kv.Value)
            Dim committed As Byte() = File.ReadAllBytes(Path.Combine(dir, kv.Key & ".json"))
            Assert.True(expected.SequenceEqual(committed),
                        kv.Key & ".json differs from what the product serializer emits:" & vbLf &
                        "  committed: " & Encoding.UTF8.GetString(committed) & vbLf &
                        "  product  : " & Encoding.UTF8.GetString(expected))
            Assert.Contains(HexDigest(committed) & "  " & kv.Key & ".json", provenance)
        Next
    End Sub

    ''' <summary>Two properties of the pinned bytes that a reader of the files should not have
    ''' to rediscover: an unset knob is absent (rule 4), and every request carries the version.</summary>
    <Fact>
    Public Sub RequestFixtures_OmitUnsetKnobs_AndCarryTheVersion()
        For Each kv As KeyValuePair(Of String, WorkerRequest) In RequestCases()
            Dim text As String = Encoding.UTF8.GetString(WorkerIpc.SerializeRequest(kv.Value))
            Assert.StartsWith("{""schemaVersion"":" & WorkerIpc.RequestSchema(kv.Value).ToString() & ",""type"":", text)
            Assert.DoesNotContain(":null", text)
        Next
        Dim partialPolicy As String = Encoding.UTF8.GetString(WorkerIpc.SerializeRequest(RequestCases()("set-network-policy-partial")))
        Assert.DoesNotContain("""port""", partialPolicy)
    End Sub

    ''' <summary>
    ''' The response half: recorded worker responses the host must accept, digest-checked
    ''' against their PROVENANCE.txt before a byte is trusted - the shape of the vendored fd-sec
    ''' vectors. The folder is empty until the worker's owner publishes the exchange vectors
    ''' (catalog proposal sidecar-control/PROPOSAL-2026-09-24-exchange-vectors.md), so this is
    ''' SKIPPED BY NAME rather than passed on nothing. Vendor the files into
    ''' TestData/worker-ipc/responses/ and remove the Skip.
    ''' </summary>
    <Fact(Skip:="WORKER-IPC section 7 response half: no worker-recorded responses vendored yet - the worker repository owns them (ticket 035 phase 5.3)")>
    Public Sub ResponseVectors_AreAccepted()
        Dim dir As String = ResponsesDirInOutput()
        Dim provenance As String = File.ReadAllText(Path.Combine(dir, "PROVENANCE.txt"))
        Dim files As String() = Directory.GetFiles(dir, "*.json")
        Assert.NotEmpty(files)
        For Each f As String In files
            Dim bytes As Byte() = File.ReadAllBytes(f)
            Assert.Contains(HexDigest(bytes) & "  " & Path.GetFileName(f), provenance)
            ' Accepted = read without a transport-level failure; a higher version is refused
            ' by type, never by accident.
            Try
                Dim resp As WorkerResponse = WorkerIpc.ParseResponse(Encoding.UTF8.GetString(bytes))
                Assert.NotNull(resp)
            Catch refused As WorkerVersionRefusedException
                Assert.True(refused.WorkerVersion > WorkerIpc.SchemaVersion)
            End Try
        Next
    End Sub

    ' --- helpers --------------------------------------------------------------------

    Private Shared Function HexDigest(bytes As Byte()) As String
        Return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
    End Function

    Private Shared Function ProvenanceHeader() As String
        Return String.Join(vbLf, {
            "WORKER-IPC request fixtures - the host half of section 7",
            "==========================================================",
            "",
            "Generated, never hand-edited. Each file is the exact byte sequence the Share Manager's",
            "own serializer (WorkerIpc.SerializeRequest) writes to the pipe for one request type,",
            "as the product builds that request. No trailing newline: the pipe carries none.",
            "",
            "Contract   : WORKER-IPC 0.10 + DISK-SHARE 1.3 (pointers: docs/contracts/)",
            "Credentials: synthetic vector literals, never user credentials",
            "Generator  : WorkerIpcConformanceTests.Generate_RequestFixtures_WhenAsked",
            "Command    : set FMS_REGENERATE_WORKER_IPC=1, then run tests/Companion.Tests",
            "Pinned by  : WorkerIpcConformanceTests.RequestFixtures_MatchTheProductSerializer_ByteForByte",
            "",
            "Digests",
            "-------",
            ""}) & vbLf
    End Function

    Private Shared Function FindRepoRoot() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Return Nothing
    End Function

End Class
