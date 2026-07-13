Option Strict On

Imports System.Collections.Generic
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Frozen named-pipe wire contract (SPECIFICATION_ANDROID_FOLDER_SHARE.md Appendix A).
''' These pin the exact JSON field names WorkerIpc emits/consumes; a DTO rename would
''' silently break the worker handshake, so it must break a test instead. Mirrors the
''' options WorkerIpc.Send uses (verbatim names on write, case-insensitive on read).
''' </summary>
Public Class WorkerIpcJsonTests

    Private Shared ReadOnly WriteOpts As New JsonSerializerOptions With {
        .DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    }
    Private Shared ReadOnly ReadOpts As New JsonSerializerOptions With {
        .PropertyNameCaseInsensitive = True
    }

    <Fact>
    Public Sub SetSharedFolders_Request_HasExactWireShape()
        Dim req As New WorkerRequest With {
            .type = "SetSharedFolders",
            .folders = New List(Of ShareFolder) From {
                New ShareFolder With {.name = "Photos", .hostPath = "C:\pics", .readOnly = False}
            }
        }
        Dim json As String = JsonSerializer.Serialize(req, WriteOpts)
        Assert.Contains("""schemaVersion"":1", json)
        Assert.Contains("""type"":""SetSharedFolders""", json)
        Assert.Contains("""name"":""Photos""", json)
        Assert.Contains("""hostPath"":""C:\\pics""", json)
        Assert.Contains("""readOnly"":false", json)
    End Sub

    <Fact>
    Public Sub GetStatus_Request_OmitsNullFolders()
        Dim json As String = JsonSerializer.Serialize(New WorkerRequest With {.type = "GetStatus"}, WriteOpts)
        Assert.DoesNotContain("folders", json)  ' WhenWritingNull - only SetSharedFolders carries folders
    End Sub

    <Fact>
    Public Sub Response_DeserializesCamelCase_IntoPascalDtos()
        Dim wire As String =
            "{""schemaVersion"":1,""ok"":true,""status"":{""running"":true,""listenPort"":2222," &
            """fingerprint"":""SHA256:abc"",""username"":""fms"",""password"":""p@ss""," &
            """roots"":[{""name"":""Photos"",""hostPath"":""C:\\pics"",""readOnly"":false}]," &
            """lastError"":"""",""reachability"":{""lanAddress"":""192.168.1.5"",""ipv6Address"":""2001:db8::1""," &
            """externalPortChecked"":true,""externalPortOpen"":true,""isCgnat"":false}}}"

        Dim resp As WorkerResponse = JsonSerializer.Deserialize(Of WorkerResponse)(wire, ReadOpts)
        Assert.NotNull(resp)
        Assert.True(resp.Ok)
        Assert.NotNull(resp.Status)
        Assert.True(resp.Status.Running)
        Assert.Equal(2222, resp.Status.ListenPort)
        Assert.Equal("fms", resp.Status.Username)
        Assert.Single(resp.Status.Roots)
        Assert.Equal("Photos", resp.Status.Roots(0).name)
        Assert.False(resp.Status.Roots(0).readOnly)
        Assert.NotNull(resp.Status.Reachability)
        Assert.Equal("192.168.1.5", resp.Status.Reachability.LanAddress)
        Assert.Equal("2001:db8::1", resp.Status.Reachability.Ipv6Address)
        Assert.True(resp.Status.Reachability.ExternalPortChecked)
        Assert.True(resp.Status.Reachability.ExternalPortOpen)
        Assert.False(resp.Status.Reachability.IsCgnat)
    End Sub

    <Fact>
    Public Sub Response_ErrorEnvelope_Binds()
        Dim wire As String = "{""schemaVersion"":1,""ok"":false,""error"":""schema mismatch""}"
        Dim resp As WorkerResponse = JsonSerializer.Deserialize(Of WorkerResponse)(wire, ReadOpts)
        Assert.False(resp.Ok)
        Assert.Equal("schema mismatch", resp.Error)
        Assert.Null(resp.Status)
    End Sub

End Class
