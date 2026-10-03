#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System
Imports System.Text

Namespace Streams

    ''' <summary>
    ''' Channel identity and URL normalizer implementing STREAM-BANK 2.1 / 03 §2.5 row identity fold.
    ''' </summary>
    Friend Module StreamIdentity

        ''' <summary>
        ''' Computes the canonical channel identity key for deduplication.
        ''' Folds:
        ''' - Scheme and host case-insensitivity
        ''' - http and https fold to a common token ("http*")
        ''' - Default ports (80 for http, 443 for https, 554 for rtsp) are omitted
        ''' - Trailing slashes on path are stripped (except root path "/")
        ''' - Query string and fragment are normalized
        ''' </summary>
        Public Function GetIdentityKey(rawUrl As String) As String
            If String.IsNullOrWhiteSpace(rawUrl) Then Return String.Empty

            Dim uriObj As Uri = Nothing
            If Not Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, uriObj) Then
                Return rawUrl.Trim().ToLowerInvariant()
            End If

            Dim scheme As String = uriObj.Scheme.ToLowerInvariant()
            Dim schemeToken As String = scheme
            If scheme = "http" OrElse scheme = "https" Then
                schemeToken = "http*"
            End If

            Dim host As String = uriObj.Host.ToLowerInvariant()
            Dim port As Integer = uriObj.Port
            Dim isDefaultPort As Boolean = (scheme = "http" AndAlso port = 80) OrElse
                                           (scheme = "https" AndAlso port = 443) OrElse
                                           (scheme = "rtsp" AndAlso port = 554) OrElse
                                           (port = -1)

            Dim hostPort As String = host
            If Not isDefaultPort AndAlso port > 0 Then
                hostPort = host & ":" & port.ToString()
            End If

            Dim path As String = uriObj.AbsolutePath
            If path.Length > 1 AndAlso path.EndsWith("/"c) Then
                path = path.TrimEnd("/"c)
            End If

            Dim query As String = uriObj.Query
            Dim sb As New StringBuilder()
            sb.Append(schemeToken)
            sb.Append("://")
            sb.Append(hostPort)
            sb.Append(path)
            If Not String.IsNullOrEmpty(query) Then
                sb.Append(query)
            End If

            Return sb.ToString()
        End Function

        ''' <summary>
        ''' Formats a clean canonical URL for output.
        ''' Strips redundant default ports and trailing slashes while preserving the chosen scheme.
        ''' </summary>
        Public Function CanonicalizeUrl(rawUrl As String) As String
            If String.IsNullOrWhiteSpace(rawUrl) Then Return String.Empty

            Dim uriObj As Uri = Nothing
            If Not Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, uriObj) Then
                Return rawUrl.Trim()
            End If

            Dim scheme As String = uriObj.Scheme.ToLowerInvariant()
            Dim host As String = uriObj.Host.ToLowerInvariant()
            Dim port As Integer = uriObj.Port
            Dim isDefaultPort As Boolean = (scheme = "http" AndAlso port = 80) OrElse
                                           (scheme = "https" AndAlso port = 443) OrElse
                                           (scheme = "rtsp" AndAlso port = 554) OrElse
                                           (port = -1)

            Dim hostPort As String = host
            If Not isDefaultPort AndAlso port > 0 Then
                hostPort = host & ":" & port.ToString()
            End If

            Dim path As String = uriObj.AbsolutePath
            If path.Length > 1 AndAlso path.EndsWith("/"c) Then
                path = path.TrimEnd("/"c)
            End If

            Dim query As String = uriObj.Query
            Dim sb As New StringBuilder()
            sb.Append(scheme)
            sb.Append("://")
            sb.Append(hostPort)
            sb.Append(path)
            If Not String.IsNullOrEmpty(query) Then
                sb.Append(query)
            End If

            Return sb.ToString()
        End Function

    End Module

End Namespace
#End If
