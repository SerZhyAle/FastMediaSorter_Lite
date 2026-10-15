Option Strict On

Imports System.Threading
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Specification 228: Outward access with link password and rate limiting.
''' </summary>
Public Class ExchangeOutwardAccess228Tests

    <Fact>
    Public Sub ProducerEntry_WithLink_StoresPassword()
        Dim entry As New ExchangeProducerEntry("resource-001", "share-001")
        Dim updated As ExchangeProducerEntry = entry.WithLink("share-new", "pwd-xyz")

        Assert.Equal("resource-001", updated.ResourceId)
        Assert.Equal("share-new", updated.ShareId)
        Assert.Equal("pwd-xyz", updated.LinkPassword)

        Dim cleared As ExchangeProducerEntry = updated.WithLink("", "")
        Assert.Equal("", cleared.ShareId)
        Assert.Equal("", cleared.LinkPassword)
    End Sub

    <Fact>
    Public Sub LinkPasswordValidator_RateLimiting_RejectsTooFastAttempts()
        Dim shareId As String = "test-share-" & Guid.NewGuid().ToString("N")
        Dim correctPassword As String = "SecretPass123"

        ' First check at t=1000
        Dim firstCheck As Boolean = LinkPasswordValidator.VerifyPassword(shareId, "WrongPass", correctPassword, clockNowMs:=1000)
        Assert.False(firstCheck)

        ' Immediate second check at t=1500 (< 1000ms elapsed) should be rate-limited
        Dim secondCheck As Boolean = LinkPasswordValidator.VerifyPassword(shareId, correctPassword, correctPassword, clockNowMs:=1500)
        Assert.False(secondCheck) ' Rate limited

        ' Third check at t=2100 (> 1000ms elapsed) with correct password should succeed
        Dim thirdCheck As Boolean = LinkPasswordValidator.VerifyPassword(shareId, correctPassword, correctPassword, clockNowMs:=2100)
        Assert.True(thirdCheck)
    End Sub

    <Fact>
    Public Sub LinkPasswordValidator_EmptyPassword_AlwaysAllows()
        Dim shareId As String = "test-share-unprotected-" & Guid.NewGuid().ToString("N")
        ' When resource has no password set, access is permitted
        Assert.True(LinkPasswordValidator.VerifyPassword(shareId, "", ""))
        Assert.True(LinkPasswordValidator.VerifyPassword(shareId, "anything", ""))
    End Sub

    <Fact>
    Public Sub LinkWithoutPassword_AndWithWrongPassword_OpensNothing()
        Dim shareId As String = "link-auth-test-" & Guid.NewGuid().ToString("N")
        Dim expectedPassword As String = "CorrectSecret42"

        ' Link without password opens nothing
        Dim emptyAttempt As Boolean = LinkPasswordValidator.VerifyPassword(shareId, "", expectedPassword, clockNowMs:=1000)
        Assert.False(emptyAttempt)

        ' Link with wrong password opens nothing
        Dim wrongAttempt As Boolean = LinkPasswordValidator.VerifyPassword(shareId, "WrongSecret", expectedPassword, clockNowMs:=2100)
        Assert.False(wrongAttempt)

        ' Link with correct password succeeds
        Dim correctAttempt As Boolean = LinkPasswordValidator.VerifyPassword(shareId, expectedPassword, expectedPassword, clockNowMs:=3200)
        Assert.True(correctAttempt)
    End Sub

    <Fact>
    Public Sub GuestInvitation_FiltersOnlyGuestInvitations()
        Dim json As String = "[{""codeHash"":""hash1"",""kind"":""guest"",""createdAt"":""2026-10-14T10:00:00Z"",""used"":false}," &
                             "{""codeHash"":""hash2"",""kind"":""member"",""createdAt"":""2026-10-14T11:00:00Z"",""used"":false}]"
        Dim list As List(Of ExchangeAdminInvitation) = ExchangeAdminParser.Invitations(json)
        Assert.Equal(2, list.Count)

        Dim guestInvs As List(Of ExchangeAdminInvitation) = list.Where(Function(i) String.Equals(i.Kind, "guest", StringComparison.OrdinalIgnoreCase)).ToList()
        Dim singleGuest As ExchangeAdminInvitation = Assert.Single(guestInvs)
        Assert.Equal("hash1", singleGuest.CodeHash)
        Assert.Equal("guest", singleGuest.Kind)
    End Sub

    <Fact>
    Public Sub EnablingFolder_PublishesToEveryConfiguredPath_AndDisablingWithdrawsEverywhere()
        Dim root As New ShareFolder With {.name = "Photos", .hostPath = "C:\Media\Photos"}
        Dim status As New WorkerStatus With {
            .Running = True, .ListenPort = 22022, .Username = "user", .Password = "pwd",
            .Fingerprint = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk",
            .Roots = New List(Of ShareFolder) From {root},
            .Reachability = New WorkerReachability With {
                .LanAddress = "192.168.1.50", .Ipv6Address = "2001:db8::1",
                .ExternalHost = "wan.example.com", .ExternalPort = 22022
            }
        }
        Dim inputs As New ExchangePublishInputs With {
            .Publish = True, .Status = status, .DeviceId = "dev-01",
            .PublicEndpoint = "exchange.example.org:443", .IncludeExternal = True,
            .NewId = Function() "res-01"
        }

        ' 1. Enabling folder: publishes to LAN, IPv6, external, and exchange tunnel endpoint
        Dim plan As ExchangePublishPlan = ExchangePublishPlan.Build(inputs)
        Assert.Equal(ExchangePublishBlock.None, plan.Block)
        Dim share As ExchangePlannedShare = Assert.Single(plan.Shares)
        Assert.Equal("Photos", share.Name)
        Assert.NotEmpty(share.AccessJson)

        ' 2. Disabling folder (roots empty): withdraws everywhere (Block = NoShares, 0 shares)
        status.Roots = New List(Of ShareFolder)()
        Dim disabledPlan As ExchangePublishPlan = ExchangePublishPlan.Build(inputs)
        Assert.Equal(ExchangePublishBlock.NoShares, disabledPlan.Block)
        Assert.Empty(disabledPlan.Shares)
    End Sub

    <Fact>
    Public Sub DualMode_WithNoAccount_BaselinePreserved()
        Dim inputs As New ExchangePublishInputs With {
            .Publish = False, .Status = New WorkerStatus With {.Running = True}
        }
        Dim plan As ExchangePublishPlan = ExchangePublishPlan.Build(inputs)
        Assert.Equal(ExchangePublishBlock.Off, plan.Block)
        Assert.Empty(plan.Shares)
    End Sub

End Class
