Option Strict On

Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Cryptography.X509Certificates

''' <summary>
''' Whether a downloaded executable is Authenticode-signed by the publisher we expect -
''' WinVerifyTrust for "the signature is valid and chains to a trusted root", then the signing
''' certificate's common name for "and it is THEIR signature". Both, because a valid signature
''' from anybody else is exactly what a planted file would carry.
'''
''' Written for the Ollama installer (audit 051 E-14), the one executable the viewer downloads
''' and starts itself. Audit 050 D-13 closed without a second caller: the runtime packages are
''' pinned by SHA-256 and the VC++ runtime is only a link the user opens (ticket 037 Ф-3.3). A
''' future executable download should reuse this module rather than grow a second one.
''' </summary>
Friend Module AuthenticodeCheck

    ' WINTRUST_ACTION_GENERIC_VERIFY_V2
    Private ReadOnly GenericVerifyV2 As New Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE")

    Private Const WTD_UI_NONE As UInteger = 2
    Private Const WTD_REVOKE_NONE As UInteger = 0
    Private Const WTD_CHOICE_FILE As UInteger = 1
    Private Const WTD_STATEACTION_VERIFY As UInteger = 1
    Private Const WTD_STATEACTION_CLOSE As UInteger = 2
    ' Never go to the network for revocation from here: the check must not hang the UI on a
    ' machine without internet, and the chain and signature are what this is about.
    Private Const WTD_CACHE_ONLY_URL_RETRIEVAL As UInteger = &H1000UI

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure WinTrustFileInfo
        Public cbStruct As UInteger
        <MarshalAs(UnmanagedType.LPWStr)> Public pcwszFilePath As String
        Public hFile As IntPtr
        Public pgKnownSubject As IntPtr
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure WinTrustData
        Public cbStruct As UInteger
        Public pPolicyCallbackData As IntPtr
        Public pSIPClientData As IntPtr
        Public dwUIChoice As UInteger
        Public fdwRevocationChecks As UInteger
        Public dwUnionChoice As UInteger
        Public pFile As IntPtr
        Public dwStateAction As UInteger
        Public hWVTStateData As IntPtr
        Public pwszURLReference As IntPtr
        Public dwProvFlags As UInteger
        Public dwUIContext As UInteger
        Public pSignatureSettings As IntPtr
    End Structure

    <DllImport("wintrust.dll", EntryPoint:="WinVerifyTrust", CharSet:=CharSet.Unicode)>
    Private Function WinVerifyTrust(hwnd As IntPtr, ByRef pgActionID As Guid, pWVTData As IntPtr) As Integer
    End Function

    ''' <summary>
    ''' True only when <paramref name="filePath"/> carries a valid Authenticode signature whose
    ''' signing certificate's common name is exactly <paramref name="expectedSigner"/>.
    ''' Anything unexpected - no signature, a broken one, another signer, an unreadable file -
    ''' is False; this never throws.
    ''' </summary>
    Friend Function IsSignedBy(filePath As String, expectedSigner As String) As Boolean
        If String.IsNullOrEmpty(filePath) OrElse String.IsNullOrEmpty(expectedSigner) Then Return False
        Try
            If Not File.Exists(filePath) Then Return False
            If Not SignatureIsTrusted(filePath) Then Return False
            Return String.Equals(SignerCommonName(filePath), expectedSigner, StringComparison.Ordinal)
        Catch ex As Exception
            AppFileLogger.LogException("Authenticode check", ex)
            Return False
        End Try
    End Function

    ''' <summary>The signing certificate's common name, or "" when the file is not signed.</summary>
    Friend Function SignerCommonName(filePath As String) As String
        Try
#Disable Warning SYSLIB0057 ' The signer of a signed FILE, which is what this API exists for.
            Using signer As X509Certificate = X509Certificate.CreateFromSignedFile(filePath),
                  cert As New X509Certificate2(signer)
#Enable Warning SYSLIB0057
                Return cert.GetNameInfo(X509NameType.SimpleName, False)
            End Using
        Catch
            Return ""
        End Try
    End Function

    Private Function SignatureIsTrusted(filePath As String) As Boolean
        Dim fileInfo As New WinTrustFileInfo With {
            .cbStruct = CUInt(Marshal.SizeOf(GetType(WinTrustFileInfo))),
            .pcwszFilePath = filePath
        }
        Dim filePtr As IntPtr = Marshal.AllocHGlobal(Marshal.SizeOf(GetType(WinTrustFileInfo)))
        Dim dataPtr As IntPtr = IntPtr.Zero
        Dim fileMarshaled As Boolean = False
        Try
            Marshal.StructureToPtr(fileInfo, filePtr, False)
            fileMarshaled = True
            Dim data As New WinTrustData With {
                .cbStruct = CUInt(Marshal.SizeOf(GetType(WinTrustData))),
                .dwUIChoice = WTD_UI_NONE,
                .fdwRevocationChecks = WTD_REVOKE_NONE,
                .dwUnionChoice = WTD_CHOICE_FILE,
                .pFile = filePtr,
                .dwStateAction = WTD_STATEACTION_VERIFY,
                .dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL
            }
            dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(GetType(WinTrustData)))
            Marshal.StructureToPtr(data, dataPtr, False)

            Dim action As Guid = GenericVerifyV2
            Dim result As Integer = WinVerifyTrust(IntPtr.Zero, action, dataPtr)

            ' Release the state WinVerifyTrust kept for the verify call.
            data = CType(Marshal.PtrToStructure(dataPtr, GetType(WinTrustData)), WinTrustData)
            data.dwStateAction = WTD_STATEACTION_CLOSE
            Marshal.StructureToPtr(data, dataPtr, True)
            Dim closeResult As Integer = WinVerifyTrust(IntPtr.Zero, action, dataPtr)
            If closeResult <> 0 Then Debug.WriteLine("Authenticode: state close returned " & closeResult.ToString(Globalization.CultureInfo.InvariantCulture))

            Return result = 0
        Finally
            If dataPtr <> IntPtr.Zero Then Marshal.FreeHGlobal(dataPtr)
            If fileMarshaled Then Marshal.DestroyStructure(filePtr, GetType(WinTrustFileInfo))
            Marshal.FreeHGlobal(filePtr)
        End Try
    End Function

End Module
