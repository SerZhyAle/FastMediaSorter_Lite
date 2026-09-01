#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports Microsoft.Win32

' The impure half of the delete policy: the facts about a volume, and the sentences
' built from the decision they lead to.
' 017_SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md §3.2 and §3.7. Modern build only.
'
' Kept out of DeletePolicy.vb on purpose - that file has to stay free of the registry
' and DriveInfo so the rule matrix can be proven in a test. What lives here is
' everything that has to ask Windows, plus the text, which has to ask Localization.

''' <summary>
''' What kind of volume a path lives on, and whether its bin will really take a file.
''' Cached per volume root for the session: a probe on a dead share must not cost a
''' DriveInfo timeout per keypress (the failure mode the queue already has, §6.6).
''' </summary>
Friend Module DeleteVolumeProbe

    Private ReadOnly facts_By_Root As New Dictionary(Of String, DeleteVolumeFacts)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly facts_Lock As New Object()

    Private Const Bit_Bucket_Key As String = "Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket"
    Private Const Policy_Key As String = "Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"

    ''' <summary>
    ''' The volume a path belongs to, as a cache key: "\\p7\down" for a UNC path, "C:\"
    ''' for a local one. Empty when the path cannot be resolved at all.
    ''' </summary>
    ' NOTE: the parameter is anyPath, not path - VB is case-insensitive, so a parameter
    ' called "path" shadows System.IO.Path and every Path.GetFullPath in the body becomes
    ' a member call on a String.
    Friend Function VolumeRootOf(anyPath As String) As String
        If String.IsNullOrWhiteSpace(anyPath) Then Return ""

        Dim full As String
        Try
            full = Path.GetFullPath(anyPath)
        Catch
            Return ""
        End Try

        If full.StartsWith("\\", StringComparison.Ordinal) Then
            ' \\server\share\a\b -> \\server\share. Anything shorter is not a usable
            ' UNC path and is left to the caller's Unknown.
            Dim parts As String() = full.Substring(2).Split("\"c)
            If parts.Length < 2 OrElse parts(0).Length = 0 OrElse parts(1).Length = 0 Then Return ""
            Return "\\" & parts(0) & "\" & parts(1)
        End If

        Try
            Return Path.GetPathRoot(full)
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' The facts for the volume this path lives on. Never throws: everything it cannot
    ''' establish comes back as Unknown, which rule 5 of the policy turns into the
    ''' honest, scarier wording rather than a promised bin.
    ''' </summary>
    Friend Function FactsFor(anyPath As String) As DeleteVolumeFacts
        Dim root As String = VolumeRootOf(anyPath)
        If root = "" Then Return New DeleteVolumeFacts()

        SyncLock facts_Lock
            Dim cached As DeleteVolumeFacts = Nothing
            If facts_By_Root.TryGetValue(root, cached) Then Return cached
        End SyncLock

        Dim fresh As DeleteVolumeFacts = Probe(root)

        SyncLock facts_Lock
            facts_By_Root(root) = fresh
        End SyncLock
        Return fresh
    End Function

    ' The cache lives for the session and is never invalidated, deliberately. A drive
    ' letter that is remapped mid-session almost always keeps its KIND - one removable
    ' stick replaced by another, one share by another - and the kind is what the verdict
    ' hangs on. The residual case is §6.1's known limit: a stale prediction can only pick
    ' a different sentence, never a different file.

    Private Function Probe(root As String) As DeleteVolumeFacts
        Dim facts As New DeleteVolumeFacts()

        ' UNC FIRST, and not as a shortcut: New DriveInfo("\\server\share") throws, so
        ' the drive layer cannot answer this question at all. A MAPPED drive (Z: ->
        ' \\p7\down) is the mirror case and is caught by DriveType below - which is
        ' exactly why a naive path.StartsWith("\\") test is not enough on its own: the
        ' same share is reachable both ways.
        If root.StartsWith("\\", StringComparison.Ordinal) Then
            facts.Kind = DeleteVolumeKind.Network
            Return facts
        End If

        Try
            Select Case New DriveInfo(root).DriveType
                Case DriveType.Fixed
                    facts.Kind = DeleteVolumeKind.FixedDisk
                Case DriveType.Network
                    facts.Kind = DeleteVolumeKind.Network
                Case DriveType.Removable
                    facts.Kind = DeleteVolumeKind.Removable
                Case Else
                    ' CDRom, Ram, NoRootDirectory, Unknown: no bin, and nothing gained
                    ' by pretending we know which of them it is.
                    facts.Kind = DeleteVolumeKind.Unknown
            End Select
        Catch
            facts.Kind = DeleteVolumeKind.Unknown
        End Try

        ' Only a fixed disk can have a bin to switch off or fill up; for everything else
        ' the policy has already decided and the registry read would be wasted I/O.
        If facts.Kind = DeleteVolumeKind.FixedDisk Then ReadBinSettings(root, facts)

        Return facts
    End Function

    ''' <summary>
    ''' Best effort, in this order: the per-volume key, the global one, then policy.
    ''' Absent means "not disabled" - the bin is on by default and guessing otherwise
    ''' would put a permanent-delete warning in front of every ordinary deletion.
    ''' </summary>
    Private Sub ReadBinSettings(root As String, facts As DeleteVolumeFacts)
        Try
            Dim volume_Guid As String = VolumeGuidOf(root)
            If volume_Guid <> "" Then
                Using key As RegistryKey = Registry.CurrentUser.OpenSubKey(Bit_Bucket_Key & "\Volume\" & volume_Guid)
                    If key IsNot Nothing Then
                        If ReadDword(key, "NukeOnDelete") = 1 Then facts.BinDisabled = True
                        Dim capacity_Mb As Integer = ReadDword(key, "MaxCapacity")
                        ' 0 is a real answer here and means "keeps nothing", not "unknown".
                        If capacity_Mb >= 0 Then facts.BinQuotaBytes = CLng(capacity_Mb) * 1024L * 1024L
                    End If
                End Using
            End If

            If Not facts.BinDisabled Then
                Using key As RegistryKey = Registry.CurrentUser.OpenSubKey(Bit_Bucket_Key)
                    If key IsNot Nothing AndAlso ReadDword(key, "NukeOnDelete") = 1 Then facts.BinDisabled = True
                End Using
            End If

            If Not facts.BinDisabled Then
                If PolicyDisablesBin(Registry.CurrentUser) OrElse PolicyDisablesBin(Registry.LocalMachine) Then
                    facts.BinDisabled = True
                End If
            End If
        Catch
            ' A registry we cannot read is not evidence of anything. Leaving the defaults
            ' in place means the deletion is described as recyclable and the shell gets
            ' the final word - §6.1, the prediction only ever picks the wording.
        End Try
    End Sub

    Private Function PolicyDisablesBin(hive As RegistryKey) As Boolean
        Using key As RegistryKey = hive.OpenSubKey(Policy_Key)
            Return key IsNot Nothing AndAlso ReadDword(key, "NoRecycleFiles") = 1
        End Using
    End Function

    ''' <summary>-1 when the value is absent or is not a number.</summary>
    Private Function ReadDword(key As RegistryKey, name As String) As Integer
        Try
            Dim raw As Object = key.GetValue(name)
            If raw Is Nothing Then Return -1
            Return Convert.ToInt32(raw, Globalization.CultureInfo.InvariantCulture)
        Catch
            Return -1
        End Try
    End Function

    ''' <summary>
    ''' "{6a05b4d5-....}" for a mount point, which is how the BitBucket key names its
    ''' per-volume subkeys. The API answers "\\?\Volume{GUID}\", so the braces are cut
    ''' out of it rather than assumed to sit at a fixed offset.
    ''' </summary>
    Private Function VolumeGuidOf(root As String) As String
        Dim mount_Point As String = root
        If Not mount_Point.EndsWith("\", StringComparison.Ordinal) Then mount_Point &= "\"

        Dim buffer As New StringBuilder(64)
        If Not GetVolumeNameForVolumeMountPoint(mount_Point, buffer, CUInt(buffer.Capacity)) Then Return ""

        Dim name As String = buffer.ToString()
        Dim open_At As Integer = name.IndexOf("{"c)
        Dim close_At As Integer = name.IndexOf("}"c)
        If open_At < 0 OrElse close_At <= open_At Then Return ""
        Return name.Substring(open_At, close_At - open_At + 1)
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function GetVolumeNameForVolumeMountPoint(lpszVolumeMountPoint As String,
                                                      lpszVolumeName As StringBuilder,
                                                      cchBufferLength As UInteger) As Boolean
    End Function

End Module

''' <summary>
''' What the user is told, built from the decision and nothing else - which is what
''' keeps the question and the status line from ever disagreeing (invariant 2), and
''' what makes "permanently" always come with a reason (invariant 3).
''' </summary>
Friend Module DeleteText

    ''' <summary>The question for one file.</summary>
    Friend Function ConfirmOne(decision As DeleteDecision, fileName As String) As String
        If decision Is Nothing OrElse Not decision.IsPermanent Then
            Return Localization.TF("Удалить файл '{0}' в Корзину?", fileName)
        End If

        Select Case decision.Reason
            Case PermanentReason.NoBinOnNetwork
                Return Localization.TF("Файл '{0}' будет удалён безвозвратно: на сетевом диске Корзины нет.", fileName)
            Case PermanentReason.NoBinOnRemovable
                Return Localization.TF("Файл '{0}' будет удалён безвозвратно: на съёмном носителе Корзины нет.", fileName)
            Case PermanentReason.BinDisabledOnVolume
                Return Localization.TF("Файл '{0}' будет удалён безвозвратно: Корзина отключена для этого диска.", fileName)
            Case PermanentReason.FileExceedsBinQuota
                Return Localization.TF("Файл '{0}' будет удалён безвозвратно: он больше, чем вмещает Корзина.", fileName)
            Case PermanentReason.VolumeUnknown
                Return Localization.TF("Файл '{0}' будет удалён безвозвратно: это расположение не поддерживает Корзину.", fileName)
            Case Else
                ' UserAsked: they held Shift, or switched the bin off. No lecture about
                ' the volume - they know what they asked for.
                Return Localization.TF("Удалить файл '{0}' безвозвратно, минуя Корзину?", fileName)
        End Select
    End Function

    ''' <summary>
    ''' The question for a selection. One dialog, one outcome: the environmental reasons
    ''' collapse into a single sentence here, because a list of five different reasons
    ''' for one Yes/No is noise, not honesty.
    ''' </summary>
    Friend Function ConfirmMany(decision As DeleteDecision, count As Integer) As String
        If decision Is Nothing OrElse Not decision.IsPermanent Then
            Return Localization.TF("Удалить {0} файл(ов) в Корзину?", count)
        End If

        If decision.Reason = PermanentReason.UserAsked Then
            Return Localization.TF("Удалить {0} файл(ов) безвозвратно, минуя Корзину?", count)
        End If

        Return Localization.TF("{0} файл(ов) будет удалено безвозвратно: Корзина здесь недоступна.", count)
    End Function

    ''' <summary>The status line, which has to agree with the question that preceded it.</summary>
    Friend Function StatusOne(decision As DeleteDecision, filePath As String) As String
        If decision Is Nothing OrElse Not decision.IsPermanent Then
            Return Localization.TF("удалён в Корзину: {0}", filePath)
        End If
        Return Localization.TF("удалён безвозвратно: {0}", filePath)
    End Function

End Module
#End If
