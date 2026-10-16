#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO

''' <summary>
''' One intake for every input: typed/pasted text, config files (.fmscfg, .json, .txt),
''' image files (.png, .jpg, .jpeg, .bmp, .webp), and clipboard text or screenshots (spec 227 rule 1).
''' An image goes through the QR decoder, then the decoded text through the same readers as typed text.
''' Decoded text is never logged (spec 227 rule 6: secret holder).
''' </summary>
Friend NotInheritable Class PhoneConnectionIntake

    Private Sub New()
    End Sub

    Friend Const MaxFileBytes As Long = 1024L * 1024L

    Private Shared ReadOnly ImageExtensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".png", ".jpg", ".jpeg", ".bmp", ".webp"
    }

    Private Shared ReadOnly ConfigExtensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".fmscfg", ".json", ".txt"
    }

    ''' <summary>True if the extension is an image format accepted by the intake.</summary>
    Friend Shared Function IsImageFile(path As String) As Boolean
        If String.IsNullOrWhiteSpace(path) Then Return False
        Dim ext As String = System.IO.Path.GetExtension(path)
        Return ImageExtensions.Contains(ext)
    End Function

    ''' <summary>True if the extension is any supported intake file (config or image).</summary>
    Friend Shared Function IsSupportedFile(path As String) As Boolean
        If String.IsNullOrWhiteSpace(path) Then Return False
        Dim ext As String = System.IO.Path.GetExtension(path)
        Return ConfigExtensions.Contains(ext) OrElse ImageExtensions.Contains(ext)
    End Function

    ''' <summary>Unified parser for text: routes FMSSFTP codes through FmsSftpPlans.Read and others through FmsConfigReader.ReadText.</summary>
    Friend Shared Function ReadText(text As String) As FmsConfigReadResult
        If String.IsNullOrWhiteSpace(text) Then
            Return New FmsConfigReadResult With {.Failure = FmsConfigFailure.Empty}
        End If
        Dim trimmed As String = text.Trim()
        ' a pairing code is recognised whatever the exchange gate says: FMSSFTP1 is plain SFTP, and the gate only decides whether the
        ' `x` tunnel path of a v2 code is kept (FmsSftpPlans.Read)
        If FmsSftpPlans.LooksLikeCode(trimmed) Then
            Return FmsSftpPlans.Read(trimmed)
        Else
            Return FmsConfigReader.ReadText(trimmed)
        End If
    End Function

    ''' <summary>Reads an intake file: decodes QR code if image, otherwise reads text.</summary>
    Friend Shared Function ReadFile(path As String) As FmsConfigReadResult
        If String.IsNullOrWhiteSpace(path) Then
            Return New FmsConfigReadResult With {.Failure = FmsConfigFailure.Empty}
        End If

        If IsImageFile(path) Then
            Dim qr As QrDecodeResult = QrCodeDecoder.DecodeFile(path)
            If qr.Succeeded Then
                Return ReadText(qr.Text)
            End If
            Return FromDecoder(qr)
        End If

        Try
            Dim info As New FileInfo(path)
            If info.Length > MaxFileBytes Then
                Return New FmsConfigReadResult With {.Failure = FmsConfigFailure.TooLarge}
            End If
            Dim bytes As Byte() = File.ReadAllBytes(path)
            Dim text As String = System.Text.Encoding.UTF8.GetString(bytes)
            Return ReadText(text)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return New FmsConfigReadResult With {.Failure = FmsConfigFailure.Empty}
        End Try
    End Function

    ''' <summary>Reads clipboard intake: decodes QR if screenshot image, otherwise reads text.</summary>
    Friend Shared Function ReadClipboard() As FmsConfigReadResult
        Dim qr As QrDecodeResult = QrCodeDecoder.DecodeClipboard()
        If qr.Succeeded Then
            Return ReadText(qr.Text)
        End If
        Return FromDecoder(qr)
    End Function

    ''' <summary>The decoder's refusal carried whole: the class tells the reader which bucket it falls in,
    ''' the Detail keeps the decoder's own sentence so an image with no QR and one with several say
    ''' different things (spec 227 section 5) instead of one damaged-file line for everything.</summary>
    Private Shared Function FromDecoder(qr As QrDecodeResult) As FmsConfigReadResult
        Dim failure As FmsConfigFailure
        Select Case qr.Outcome
            Case QrDecodeOutcome.TooLarge : failure = FmsConfigFailure.TooLarge
            Case QrDecodeOutcome.MultipleQrs : failure = FmsConfigFailure.InvalidJson
            Case Else : failure = FmsConfigFailure.Empty
        End Select
        Return New FmsConfigReadResult With {.Failure = failure, .Detail = qr.FailureMessage}
    End Function

End Class
#End If
