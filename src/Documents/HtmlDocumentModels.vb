#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Threading
Imports System.Threading.Tasks

' Specification 275 section 10.1: Windows-internal types shared by the HTML document
' sources, the policy and the host. No WinForms/WebView2 reference and no serialized
' cross-product contract. Dependency direction: models/policy -> sources -> host -> form.

Public Enum HtmlSourceKind
    LocalFile
    ArchiveSnapshot
    ResourceSnapshot
    EpubPackage
    ConverterOutput
    OnlinePage
End Enum

' Section 14 typed outcomes. Ok is the only success; every other value maps to a
' localized, actionable sentence in the form and never carries a path or URL query.
Public Enum HtmlOutcome
    Ok
    Disabled
    SecureRefused
    UnsupportedPlatform
    RuntimeMissing
    LoaderMissing
    LoaderWrongArchitecture
    RequiredApiUnavailable
    RuntimeBlocked
    SourceMissing
    AccessDenied
    SourceChanged
    InvalidEncoding
    InvalidMarkup
    RootEscape
    UnsupportedResource
    LimitExceeded
    NetworkBlocked
    RedirectNeedsApproval
    CertificateRejected
    TimedOut
    Cancelled
    RendererExited
    RendererUnresponsive
End Enum

' What a logical resource is, decided from the source's own metadata (extension for a
' local file, manifest media type for a package), never from a response header.
Public Enum HtmlResourceKind
    Document
    Stylesheet
    Image
    Font
    Other
End Enum

' The request context the Runtime reports, mapped by the host so the policy stays pure.
Public Enum HtmlRequestContext
    Document
    Stylesheet
    Image
    Font
    Other
End Enum

' A clicked or attempted navigation, classified by HtmlDocumentPolicy.ClassifyNavigation.
Public Enum HtmlNavigationAction
    ' Same-origin HTML key: navigate inside the host, push document history.
    Internal
    ' Same document, fragment only: let the Runtime scroll.
    Anchor
    ' Same-origin key that is not HTML: cancel, hand to the typed content opener.
    OpenTyped
    ' http/https outside the session origin: cancel, offer the system browser.
    External
    ' mailto: cancel, explicit external-open action only.
    Mail
    ' javascript:, data:, file:, unknown schemes, executables: cancel silently.
    Blocked
End Enum

Public NotInheritable Class HtmlSourceVersion
    Public Property Length As Long
    Public Property LastWriteUtcTicks As Long
    ' Lowercase hex SHA-256 of the bytes actually read; empty when not yet read.
    Public Property Digest As String = ""

    Public Function Matches(other As HtmlSourceVersion) As Boolean
        Return other IsNot Nothing AndAlso Length = other.Length AndAlso LastWriteUtcTicks = other.LastWriteUtcTicks AndAlso
            String.Equals(Digest, other.Digest, StringComparison.Ordinal)
    End Function
End Class

Public NotInheritable Class HtmlOpenRequest
    Public Property SourceId As String = ""
    Public Property SourceKind As HtmlSourceKind
    Public Property DisplayName As String = ""
    ' Identity used by the existing file-operation/source owner (a path, archive|entry, locator|path or URL).
    Public Property OriginalLocator As String = ""
    ' Logical key of the entry document inside the declared root ("index.html", "chapter/one.xhtml").
    Public Property EntryResourceKey As String = ""
    Public Property SourceVersion As HtmlSourceVersion
    ' Display-only description of the declared root; never used to authorize a path.
    Public Property RootDescriptor As String = ""
    Public Property Provenance As String = ""
    Public Property IsSecure As Boolean
    Public Property IsReadOnly As Boolean = True
End Class

' Section 10.2: every response, find counter, position query and error callback needs the
' complete matching token and an undisposed owner before it touches UI or state.
Public NotInheritable Class HtmlSessionToken
    Public ReadOnly Property MediaGeneration As Integer
    Public ReadOnly Property SessionId As String
    Public ReadOnly Property NavigationVersion As Integer

    Public Sub New(mediaGeneration As Integer, sessionId As String, navigationVersion As Integer)
        Me.MediaGeneration = mediaGeneration
        Me.SessionId = If(sessionId, "")
        Me.NavigationVersion = navigationVersion
    End Sub

    Public Function NextNavigation() As HtmlSessionToken
        Return New HtmlSessionToken(MediaGeneration, SessionId, NavigationVersion + 1)
    End Function

    Public Function Matches(other As HtmlSessionToken) As Boolean
        Return other IsNot Nothing AndAlso MediaGeneration = other.MediaGeneration AndAlso NavigationVersion = other.NavigationVersion AndAlso
            String.Equals(SessionId, other.SessionId, StringComparison.Ordinal)
    End Function

    ' Same top-level document; navigation inside it may have moved on.
    Public Function SameSession(other As HtmlSessionToken) As Boolean
        Return other IsNot Nothing AndAlso MediaGeneration = other.MediaGeneration AndAlso
            String.Equals(SessionId, other.SessionId, StringComparison.Ordinal)
    End Function
End Class

Public NotInheritable Class HtmlResourceResult
    Public Property Status As HtmlOutcome
    Public Property Kind As HtmlResourceKind = HtmlResourceKind.Other
    Public Property MimeType As String = "application/octet-stream"
    ' Immutable in practice: callers never write into a returned array.
    Public Property Bytes As Byte() = New Byte() {}
    Public Property ResourceVersion As HtmlSourceVersion

    Public ReadOnly Property IsOk As Boolean
        Get
            Return Status = HtmlOutcome.Ok
        End Get
    End Property

    Public Shared Function Refused(outcome As HtmlOutcome) As HtmlResourceResult
        Return New HtmlResourceResult With {.Status = If(outcome = HtmlOutcome.Ok, HtmlOutcome.UnsupportedResource, outcome)}
    End Function
End Class

' A key decision: either an allowed logical key or a typed refusal.
Public NotInheritable Class HtmlKeyResolution
    Public Property Status As HtmlOutcome
    Public Property Key As String = ""

    Public ReadOnly Property IsAllowed As Boolean
        Get
            Return Status = HtmlOutcome.Ok
        End Get
    End Property

    Public Shared Function Allowed(key As String) As HtmlKeyResolution
        Return New HtmlKeyResolution With {.Status = HtmlOutcome.Ok, .Key = key}
    End Function

    Public Shared Function Refused(outcome As HtmlOutcome) As HtmlKeyResolution
        Return New HtmlKeyResolution With {.Status = If(outcome = HtmlOutcome.Ok, HtmlOutcome.RootEscape, outcome)}
    End Function
End Class

' Section 10.1 adapter seam. Local files are provided by 275; 274 supplies EPUB and 276 a
' verified converter output. An adapter cannot change host flags or CSP, never accepts a
' filesystem path from HTML/CSS/a link, and owns its leases until Dispose.
Public Interface IHtmlDocumentSource
    Inherits IDisposable

    ReadOnly Property Request As HtmlOpenRequest

    ' The entry document's raw bytes and the version they were read at. Read once per session.
    Function ReadEntryAsync(token As CancellationToken) As Task(Of HtmlResourceResult)

    ' Classify an already parsed logical key (HtmlDocumentPolicy.ParseRequestKey) against this
    ' source: Ok when the key names a resource the source may serve, a refusal otherwise.
    Function ResolveKey(key As String) As HtmlKeyResolution

    ' The resource kind the source itself declares for an allowed key.
    Function KindOf(key As String) As HtmlResourceKind

    ' Bytes of an allowed key, bounded by the per-kind limit. Never a persistent original stream.
    Function ReadResourceAsync(key As String, token As CancellationToken) As Task(Of HtmlResourceResult)
End Interface
#End If
