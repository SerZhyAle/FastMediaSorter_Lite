Option Strict On

Imports System.Text.Json

''' <summary>
''' Per-shared-root resource parameters for the .fmscfg schema v2 export
''' (Android contract S1002, COMPANION_EXPORT_SPEC §3): how the imported resource
''' is configured on the phone - name, type/profile, media set, scan conditions,
''' destination flag, comment, PIN, slideshow interval. Defaults mirror the
''' Android import defaults exactly, so an all-default instance emits a pure v1
''' root and the whole .fmscfg can stay schemaVersion 1 (old Android apps keep
''' importing it - see ShareConfigBuilder).
''' </summary>
Public Class ShareRootParams

    ''' <summary>Resource name on the phone; "" = use the folder name (v1 behavior).</summary>
    Public Property Label As String = ""

    ''' <summary>Profile token (§3.1): none | audio_library | video_library |
    ''' photo_storage | documents | all_files.</summary>
    Public Property Profile As String = "none"

    ''' <summary>Explicit media-type tokens (§3.2); empty = the profile decides.</summary>
    Public Property MediaTypes As List(Of String) = New List(Of String)()

    Public Property ScanSubdirectories As Boolean = True
    Public Property ShowSubfoldersAsItems As Boolean = False
    Public Property ShowHiddenFiles As Boolean = False
    Public Property AllFiles As Boolean = False

    ''' <summary>Read-only access for the phone (browse/download only, no upload,
    ''' rename, delete or move-into). Default False - a newly shared folder is
    ''' writable so the phone can sort/copy into it out of the box; the user ticks
    ''' "read-only" in the resource dialog to lock a folder to browsing. Exported as
    ''' the explicit per-root <c>readOnly</c> field and enforced by the SFTP server.
    ''' A destination (<see cref="IsDestination"/>) is inherently writable, so it
    ''' also forces this off.</summary>
    Public Property IsReadOnly As Boolean = False

    ''' <summary>Register as a copy/move destination on the phone. Also flips the
    ''' SFTP share to writable (§5) - the destination must accept writes, so it
    ''' overrides <see cref="IsReadOnly"/>.</summary>
    Public Property IsDestination As Boolean = False

    ''' <summary>SOFT read-only (§4.5.4 п.2): a client HINT only - the exported
    ''' <c>readOnly</c> is set true so the phone shows the resource as read-only, but
    ''' the SFTP server is NOT locked (writes stay allowed unless <see cref="IsReadOnly"/>
    ''' is also set). This is distinct from HARD read-only (<see cref="IsReadOnly"/>,
    ''' which the server enforces). The divergence is always in the SAFE direction -
    ''' advertised RO can be STRICTER than the server, never looser - so the phone
    ''' never shows a write action the server would then refuse.</summary>
    Public Property SoftReadOnly As Boolean = False

    ''' <summary>Whether the user picked an explicit destination chip color.</summary>
    Public Property HasDestinationColor As Boolean = False
    ''' <summary>Signed 32-bit ARGB (e.g. -14575885 = 0xFF2196F3); best-effort (§5).</summary>
    Public Property DestinationColorArgb As Integer = 0

    Public Property Comment As String = ""

    ''' <summary>PIN locking the resource in the app. Travels in the file/QR in
    ''' plaintext (§6) whenever set - it only pre-configures the phone's own
    ''' per-resource lock, so there is no separate "keep it out of the export"
    ''' opt-out: leave it blank here and set it on the phone instead.</summary>
    Public Property AccessPin As String = ""

    ''' <summary>Seconds between slideshow images; 10 = the app default (omitted on export).</summary>
    Public Property SlideshowInterval As Integer = 10

    ''' <summary>Effective SFTP writability of this root: writable when it is not
    ''' explicitly read-only, OR it is a destination (which must accept writes and so
    ''' overrides <see cref="IsReadOnly"/>). This is the SINGLE definition of the
    ''' per-root writable flag - the exported <c>readOnly</c> (ShareConfigBuilder),
    ''' the worker SetSharedFolders push (both the Share tab and the quick wizard) and
    ''' the resume reconciliation all derive readOnly from it, so what the .fmscfg
    ''' tells the phone can never disagree with the SFTP server's real permissions
    ''' (the "Move deletes the original -&gt; rm permission denied" contract bug).</summary>
    Public Function IsWritable() As Boolean
        Return (Not IsReadOnly) OrElse IsDestination
    End Function

    ''' <summary>The value of the exported <c>readOnly</c> field the phone is told:
    ''' true when the server enforces read-only (<see cref="IsWritable"/> = false) OR
    ''' the user asked for the soft client-hint (<see cref="SoftReadOnly"/>). Always
    ''' &gt;= the server's real restriction (never advertises writable-when-locked).</summary>
    Public Function AdvertisedReadOnly() As Boolean
        Return (Not IsWritable()) OrElse SoftReadOnly
    End Function

    ''' <summary>True when every field is at the Android import default - nothing to
    ''' emit, the root stays pure v1.</summary>
    Public Function IsDefault() As Boolean
        Return Label.Trim().Length = 0 AndAlso
               (Profile.Length = 0 OrElse Profile = "none") AndAlso
               (MediaTypes Is Nothing OrElse MediaTypes.Count = 0) AndAlso
               ScanSubdirectories AndAlso
               Not ShowSubfoldersAsItems AndAlso
               Not ShowHiddenFiles AndAlso
               Not AllFiles AndAlso
               Not IsReadOnly AndAlso
               Not SoftReadOnly AndAlso
               Not IsDestination AndAlso
               Not HasDestinationColor AndAlso
               Comment.Trim().Length = 0 AndAlso
               AccessPin.Length = 0 AndAlso
               (SlideshowInterval <= 0 OrElse SlideshowInterval = 10)
    End Function

    ''' <summary>Deep copy (the store hands out copies so dialog edits never leak
    ''' into the persisted map before OK). Also normalizes nulls left behind by
    ''' JSON deserialization.</summary>
    Public Function Clone() As ShareRootParams
        Return New ShareRootParams With {
            .Label = If(Label, ""),
            .Profile = If(String.IsNullOrEmpty(Profile), "none", Profile),
            .MediaTypes = If(MediaTypes Is Nothing, New List(Of String)(), New List(Of String)(MediaTypes)),
            .ScanSubdirectories = ScanSubdirectories,
            .ShowSubfoldersAsItems = ShowSubfoldersAsItems,
            .ShowHiddenFiles = ShowHiddenFiles,
            .AllFiles = AllFiles,
            .IsReadOnly = IsReadOnly,
            .SoftReadOnly = SoftReadOnly,
            .IsDestination = IsDestination,
            .HasDestinationColor = HasDestinationColor,
            .DestinationColorArgb = DestinationColorArgb,
            .Comment = If(Comment, ""),
            .AccessPin = If(AccessPin, ""),
            .SlideshowInterval = SlideshowInterval
        }
    End Function

End Class

''' <summary>
''' Registry-backed store of per-root export params, keyed by the folder's host
''' path (case-insensitive) - one JSON blob in the app's registry store
''' (Share_RootParams, mirrors the ShareSettings pattern). The folder LIST still
''' belongs to the worker (shares.json); only LITE-side export metadata lives
''' here. Params survive a folder being unchecked or re-added and are dropped
''' when its row is removed from the Share tab.
''' </summary>
Public Module ShareRootParamsStore

    Private Const RegKey As String = "Share_RootParams"
    Private _map As Dictionary(Of String, ShareRootParams)

    ' System.Text.Json replaces JavaScriptSerializer (spec §6.2). This blob is
    ' LITE-side storage, but the format must stay readable across the upgrade:
    ' STJ (like JavaScriptSerializer) emits verbatim PascalCase property names and
    ' a Dictionary(Of String, T) as a JSON object, so old blobs written by the
    ' pre-Companion LITE still deserialize (spec §9.1). Case-insensitive for safety.
    Private ReadOnly JsonOpts As New JsonSerializerOptions With {.PropertyNameCaseInsensitive = True}

    ''' <summary>Params for a host path - always a copy, never the stored instance;
    ''' defaults when the path has no entry.</summary>
    Public Function GetFor(hostPath As String) As ShareRootParams
        Dim p As ShareRootParams = Nothing
        If Not String.IsNullOrEmpty(hostPath) AndAlso Map().TryGetValue(hostPath, p) AndAlso p IsNot Nothing Then
            Return p.Clone()
        End If
        Return New ShareRootParams()
    End Function

    ''' <summary>Stores (a copy of) the params; an all-default value removes the
    ''' entry instead, keeping the blob minimal.</summary>
    Public Sub SetFor(hostPath As String, value As ShareRootParams)
        If String.IsNullOrEmpty(hostPath) Then Return
        If value Is Nothing OrElse value.IsDefault() Then
            Map().Remove(hostPath)
        Else
            Map()(hostPath) = value.Clone()
        End If
        Save()
    End Sub

    ''' <summary>Drops the entry (the folder row was removed from the Share tab).</summary>
    Public Sub RemoveFor(hostPath As String)
        If String.IsNullOrEmpty(hostPath) Then Return
        If Map().Remove(hostPath) Then Save()
    End Sub

    Private Function Map() As Dictionary(Of String, ShareRootParams)
        If _map Is Nothing Then
            _map = New Dictionary(Of String, ShareRootParams)(StringComparer.OrdinalIgnoreCase)
            Try
                Dim raw As String = GetSetting(App_name, Second_App_Name, RegKey, "")
                If raw.Length > 0 Then
                    Dim loaded As Dictionary(Of String, ShareRootParams) =
                        JsonSerializer.Deserialize(Of Dictionary(Of String, ShareRootParams))(raw, JsonOpts)
                    If loaded IsNot Nothing Then
                        For Each kv As KeyValuePair(Of String, ShareRootParams) In loaded
                            If kv.Value IsNot Nothing AndAlso Not String.IsNullOrEmpty(kv.Key) Then
                                _map(kv.Key) = kv.Value.Clone() ' Clone() normalizes nulls
                            End If
                        Next
                    End If
                End If
            Catch
                ' A corrupt blob must never break the Share tab - start clean.
            End Try
        End If
        Return _map
    End Function

    Private Sub Save()
        Try
            SaveSetting(App_name, Second_App_Name, RegKey, JsonSerializer.Serialize(_map, JsonOpts))
        Catch
        End Try
    End Sub

End Module
