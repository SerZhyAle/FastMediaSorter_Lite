#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic

' Opening policy and execution guard for the file manager (258_SPECIFICATION_FILE_MANAGER_CONTENT_SETTINGS_AND_SAFE_OPEN.md, Slice C1).
' Pure decision module: governs navigation, viewer handoff, external opening and blocking of executables/scripts.
' Modern build only.

Public Enum ManagerOpenKind
    ''' <summary>Enter folder, stay in manager.</summary>
    Navigate = 0
    ''' <summary>Explicit handoff to the viewer; close F3 through existing chosen-file flow.</summary>
    ViewerHandoff = 1
    ''' <summary>Open via system default handler or picker; manager stays open.</summary>
    ExternalOpen = 2
    ''' <summary>Blocked by the execution guard; no shell open or picker. Explanation shown.</summary>
    Blocked = 3
    ''' <summary>Refused (e.g. symlink/reparse point, query failure or invalid target).</summary>
    Refused = 4
End Enum

Public NotInheritable Class ManagerOpenDecision

    Public Sub New(kind As ManagerOpenKind, usePicker As Boolean, reason As String, blockedExtension As String, canRevealInExplorer As Boolean)
        Me.Kind = kind
        Me.UsePicker = usePicker
        Me.Reason = If(reason, "")
        Me.BlockedExtension = If(blockedExtension, "")
        Me.CanRevealInExplorer = canRevealInExplorer
    End Sub

    Public ReadOnly Property Kind As ManagerOpenKind
    Public ReadOnly Property UsePicker As Boolean
    Public ReadOnly Property Reason As String
    Public ReadOnly Property BlockedExtension As String
    Public ReadOnly Property CanRevealInExplorer As Boolean

    Friend Shared Function Navigate() As ManagerOpenDecision
        Return New ManagerOpenDecision(ManagerOpenKind.Navigate, False, "", "", False)
    End Function

    Friend Shared Function ViewerHandoff() As ManagerOpenDecision
        Return New ManagerOpenDecision(ManagerOpenKind.ViewerHandoff, False, "", "", False)
    End Function

    Friend Shared Function ExternalOpen(usePicker As Boolean) As ManagerOpenDecision
        Return New ManagerOpenDecision(ManagerOpenKind.ExternalOpen, usePicker, "", "", False)
    End Function

    Friend Shared Function Blocked(extension As String, reason As String, canReveal As Boolean) As ManagerOpenDecision
        Return New ManagerOpenDecision(ManagerOpenKind.Blocked, False, reason, extension, canReveal)
    End Function

    Friend Shared Function Refused(reason As String) As ManagerOpenDecision
        Return New ManagerOpenDecision(ManagerOpenKind.Refused, False, reason, "", False)
    End Function

End Class

Public Module ManagerOpenPolicy

    ''' <summary>Mandatory deny set: Windows executables, command scripts, Windows Script Host and installers.</summary>
    Friend ReadOnly MandatoryDenyExtensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".exe", ".com", ".scr", ".pif", ".bat", ".cmd", ".ps1", ".psm1", ".psd1",
        ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".msc", ".msi", ".msp", ".mst",
        ".cpl", ".reg", ".lnk", ".url", ".appref-ms"
    }

    ''' <summary>Script deny set: script languages, setup scripts, app packages and archives with active manifests.</summary>
    Friend ReadOnly ScriptDenyExtensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".py", ".pyw", ".rb", ".pl", ".sh", ".bash", ".sct", ".scf", ".inf", ".ins",
        ".isp", ".jar", ".application", ".appx", ".appxbundle", ".msix", ".msixbundle"
    }

    ''' <summary>Combined deny set of all blocked executable, script and shortcut extensions.</summary>
    Friend ReadOnly AllBlockedExtensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    Sub New()
        AllBlockedExtensions.UnionWith(MandatoryDenyExtensions)
        AllBlockedExtensions.UnionWith(ScriptDenyExtensions)
    End Sub

    ''' <summary>
    ''' Extracts the extension for the execution guard.
    ''' Normalizes Windows trailing dots and spaces before evaluating the extension,
    ''' separately from media classification.
    ''' </summary>
    Friend Function NormalizeExtensionForGuard(nameOrPath As String) As String
        If String.IsNullOrWhiteSpace(nameOrPath) Then Return ""
        ' Normalize Windows trailing dots and spaces
        Dim name As String = nameOrPath.Trim().TrimEnd("."c, " "c)
        ' Check also without repeated temporary suffixes (.temp_copy, .download)
        name = MediaKindClassifier.NormalizeSuffix(name).TrimEnd("."c, " "c)

        Dim slash As Integer = name.LastIndexOfAny(New Char() {"/"c, "\"c})
        Dim dot As Integer = name.LastIndexOf("."c)
        If dot <= slash Then Return ""
        Return name.Substring(dot).ToLowerInvariant()
    End Function

    ''' <summary>Checks whether the given extension belongs to the static deny set.</summary>
    Friend Function IsDenySetExtension(extension As String) As Boolean
        If String.IsNullOrEmpty(extension) Then Return False
        If Not extension.StartsWith(".", StringComparison.Ordinal) Then extension = "." & extension
        Return AllBlockedExtensions.Contains(extension)
    End Function

    ''' <summary>
    ''' Evaluates activation intent for a manager entry.
    ''' Every default open, Open with, toolbar activation and keyboard activation calls this policy
    ''' with the original entry name and final materialized path.
    ''' </summary>
    Friend Function Evaluate(entry As ManagerEntry, nonMediaAction As String,
                             Optional isDangerousAssoc As Func(Of String, Boolean) = Nothing) As ManagerOpenDecision
        If entry Is Nothing Then
            Return ManagerOpenDecision.Refused(Localization.T("Элемент не существует"))
        End If

        ' Symlinks/reparse points are never entered or followed (257 / 258).
        If entry.IsLink Then
            Return ManagerOpenDecision.Refused(Localization.T("Переход по символическим ссылкам не поддерживается"))
        End If

        ' Folders: navigate, stay in manager.
        If entry.IsDirectory Then
            Return ManagerOpenDecision.Navigate()
        End If

        Return EvaluateFile(entry.Name, entry.DiskPath.Length > 0, nonMediaAction, isDangerousAssoc)
    End Function

    ''' <summary>
    ''' Pure decision for an individual file entry.
    ''' </summary>
    Friend Function EvaluateFile(nameOrPath As String, hasLocalPath As Boolean, nonMediaAction As String,
                                 Optional isDangerousAssoc As Func(Of String, Boolean) = Nothing) As ManagerOpenDecision
        Dim ext As String = NormalizeExtensionForGuard(nameOrPath)

        ' 1. Execution guard: static deny set.
        If IsDenySetExtension(ext) Then
            Return ManagerOpenDecision.Blocked(ext, Localization.T("Запуск программ и сценариев заблокирован в целях безопасности"), hasLocalPath)
        End If

        ' 2. Execution guard: AssocIsDangerous query if delegate supplied.
        If isDangerousAssoc IsNot Nothing AndAlso ext.Length > 0 Then
            Try
                If isDangerousAssoc(ext) Then
                    Return ManagerOpenDecision.Blocked(ext, Localization.T("Тип файла признан опасным системой и заблокирован"), hasLocalPath)
                End If
            Catch ex As Exception
                ' If the shell risk query fails, refuse external opening with a reason.
                AppFileLogger.LogException("ManagerOpenPolicy.EvaluateFile: isDangerousAssoc", ex)
                Return ManagerOpenDecision.Refused(Localization.T("Не удалось проверить безопасность типа файла"))
            End Try
        End If

        ' 3. Supported media: explicit handoff to viewer.
        Dim mediaKind As MediaKind = MediaKindClassifier.KindOf(nameOrPath)
        If ManagerContentPolicy.IsSupportedMedia(ext, mediaKind) Then
            Return ManagerOpenDecision.ViewerHandoff()
        End If

        ' 4. Non-media files (documents, books, unsupported containers, unknown): external shell open.
        Dim usePicker As Boolean = String.Equals(nonMediaAction, "openWith", StringComparison.OrdinalIgnoreCase)
        Return ManagerOpenDecision.ExternalOpen(usePicker)
    End Function

    ''' <summary>
    ''' Rechecks a materialized file path after download or cache placement before external launch.
    ''' Returns True if safe to launch, False if blocked or dangerous.
    ''' </summary>
    Friend Function RecheckMaterialized(localPath As String,
                                        Optional isDangerousAssoc As Func(Of String, Boolean) = Nothing) As Boolean
        If String.IsNullOrWhiteSpace(localPath) Then Return False
        Dim ext As String = NormalizeExtensionForGuard(localPath)
        If IsDenySetExtension(ext) Then Return False
        If isDangerousAssoc IsNot Nothing AndAlso ext.Length > 0 Then
            Try
                If isDangerousAssoc(ext) Then Return False
            Catch
                Return False
            End Try
        End If
        Return True
    End Function

End Module
#End If
