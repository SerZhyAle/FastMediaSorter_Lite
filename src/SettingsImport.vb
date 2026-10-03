#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text

''' <summary>
''' What an imported settings profile would do, worked out BEFORE anything is asked or written
''' (103, 105 L12). Both candidate results are computed from the already NORMALIZED document, so a
''' preview never shows an out-of-range number the live model could not take, and the user sees
''' the policy that will actually apply: Replace (the document wins everywhere) or Merge (the
''' document wins only where it differs from the shipped defaults).
''' </summary>
Friend Module SettingsImportPlan

    Friend NotInheritable Class Plan
        ''' <summary>current -> imported: what "replace" changes.</summary>
        Friend ReadOnly Property ReplaceChanges As List(Of String)
        ''' <summary>current -> merged: what "merge" changes.</summary>
        Friend ReadOnly Property MergeChanges As List(Of String)
        ''' <summary>The model "merge" would apply (normalized).</summary>
        Friend ReadOnly Property Merged As ModernViewerPreferences

        Friend Sub New(replaceChanges As List(Of String), mergeChanges As List(Of String), merged As ModernViewerPreferences)
            Me.ReplaceChanges = replaceChanges
            Me.MergeChanges = mergeChanges
            Me.Merged = merged
        End Sub
    End Class

    Friend Function Build(current As ModernViewerPreferences, imported As ModernViewerPreferences) As Plan
        ' Compared against the NORMALIZED current profile (a clone: the live one is not touched), so a
        ' difference listed here is a real one and not a stored value that normalization would move.
        Dim baseline As ModernViewerPreferences = Clone(current)
        baseline.Normalize()
        ' The document is normalized the same way (FromJson already did it; this is a no-op then).
        Dim document As ModernViewerPreferences = Clone(imported)
        document.Normalize()
        Dim merged As ModernViewerPreferences = Merge(baseline, document)
        Return New Plan(Describe(baseline, document), Describe(baseline, merged), merged)
    End Function

    ''' <summary>The document wins only where it says something other than the factory default -
    ''' "merge" means "do not wipe what I did not send". This is NOT field-presence merging: an
    ''' imported value equal to the default cannot replace a current non-default.</summary>
    Friend Function Merge(current As ModernViewerPreferences, imported As ModernViewerPreferences) As ModernViewerPreferences
        Dim defaults As New ModernViewerPreferences()
        Dim merged As ModernViewerPreferences = Clone(current)
        For Each prop As Reflection.PropertyInfo In ModernViewerPreferences.WritableProperties()
            Dim fromFile As Object = prop.GetValue(imported)
            If Equals(fromFile, prop.GetValue(defaults)) Then Continue For
            prop.SetValue(merged, fromFile)
        Next
        merged.Normalize()
        Return merged
    End Function

    Friend Function Clone(source As ModernViewerPreferences) As ModernViewerPreferences
        Dim copy As New ModernViewerPreferences()
        For Each prop As Reflection.PropertyInfo In ModernViewerPreferences.WritableProperties()
            prop.SetValue(copy, prop.GetValue(source))
        Next
        Return copy
    End Function

    Friend Function Describe(current As ModernViewerPreferences, after As ModernViewerPreferences) As List(Of String)
        Dim changes As New List(Of String)()
        For Each prop As Reflection.PropertyInfo In ModernViewerPreferences.WritableProperties()
            Dim before As Object = prop.GetValue(current)
            Dim updated As Object = prop.GetValue(after)
            If Equals(before, updated) Then Continue For
            changes.Add(prop.Name & ": " & Convert.ToString(before) & " -> " & Convert.ToString(updated))
        Next
        Return changes
    End Function

End Module

''' <summary>
''' The pre-import rollback copy of the CURRENT profile (057.S14-3). It holds folder paths and
''' video positions, so it lives only in the app-private folder - never beside the file being
''' imported, and never in a shared TEMP: if private storage is unavailable the import does not
''' happen. A file is created exclusively (an earlier backup is never overwritten), flushed, read
''' back and compared before the import may change anything. Only a file this call created is
''' ever deleted. Retention is deliberately not decided here (Q105-1): nothing is expired.
''' </summary>
Friend Module SettingsImportBackupStore

    Friend Const MaxNameAttempts As Integer = 3

    Friend Function DefaultDirectory() As String
        Return Path.Combine(AppPaths.LocalAppDataRoot(), "settings-backups")
    End Function

    ''' <summary>Creates the backup and returns its full path. Throws (and leaves no partial file
    ''' of its own) when it cannot be created, written, flushed or read back identical.</summary>
    Friend Function Create(current As ModernViewerPreferences, directory As String,
                           Optional openNew As Func(Of String, Stream) = Nothing,
                           Optional readAll As Func(Of String, String) = Nothing) As String
        If current Is Nothing Then Throw New ArgumentNullException(NameOf(current))
        If String.IsNullOrEmpty(directory) Then Throw New ArgumentException("No backup directory.", NameOf(directory))

        Dim open As Func(Of String, Stream) = If(openNew, AddressOf OpenExclusive)
        Dim read As Func(Of String, String) = If(readAll, AddressOf File.ReadAllText)

        System.IO.Directory.CreateDirectory(directory)
        Dim expected As String = current.ExportJson(includePersonalData:=True)
        Dim timestamp As String = DateTime.Now.ToString("yyyyMMdd-HHmmss", Globalization.CultureInfo.InvariantCulture)

        Dim created As String = Nothing
        Dim stream As Stream = Nothing
        For attempt As Integer = 1 To MaxNameAttempts
            Dim candidate As String = Path.Combine(directory,
                "settings-backup-" & timestamp & "-" & Guid.NewGuid().ToString("N").Substring(0, 8) & ".fms-settings.json")
            Try
                stream = open(candidate)
                created = candidate
                Exit For
            Catch ex As IOException When File.Exists(candidate)
                ' The name was taken by someone else: never overwritten, try another.
            End Try
        Next
        If created Is Nothing Then Throw New IOException("A backup file name could not be reserved.")

        Try
            Using stream
                Dim bytes As Byte() = New UTF8Encoding(False).GetBytes(expected)
                stream.Write(bytes, 0, bytes.Length)
                stream.Flush()
                If TypeOf stream Is FileStream Then DirectCast(stream, FileStream).Flush(True)
            End Using

            ' Read back and compare: a backup that cannot be restored is worse than none.
            Dim restored As ModernViewerPreferences = ModernViewerPreferences.FromJson(read(created))
            If Not String.Equals(restored.ExportJson(includePersonalData:=True), expected, StringComparison.Ordinal) Then
                Throw New IOException("The backup does not read back identical to the profile.")
            End If
            Return created
        Catch
            Try
                If File.Exists(created) Then File.Delete(created)
            Catch
            End Try
            Throw
        End Try
    End Function

    Private Function OpenExclusive(path As String) As Stream
        Return New FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)
    End Function

    ''' <summary>Restores through the ordinary validated import, never a registry or file swap.</summary>
    Friend Function Restore(backupPath As String) As ModernViewerPreferences
        Return ModernViewerPreferences.FromJson(File.ReadAllText(backupPath))
    End Function

End Module
#End If
