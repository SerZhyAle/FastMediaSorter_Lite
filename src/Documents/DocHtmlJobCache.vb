#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Text.Json

''' <summary>The result-shaping choices of one document job (specification 276 sections 5 and 7.2), beside the mode.
''' Empty strings mean "not chosen": the flag is not sent and the producer's own default applies.</summary>
Public NotInheritable Class DocHtmlJobOptions
    ''' <summary><c>-ocr</c>: recognition forced. False leaves the producer's automatic recognition of scans.</summary>
    Public Property OcrForce As Boolean
    Public Property OcrLanguages As String = ""
    Public Property SourceLanguage As String = ""
    Public Property TargetLanguage As String = ""
    Public Property LocalModel As String = ""
    ''' <summary>The paid mode's per-run cap in US dollars (<c>-max-cost</c>); 0 = not set, and then no paid job is built.
    ''' An approval, not a result-shaping choice, so reuse does not compare it.</summary>
    Public Property PaidCostLimit As Double
End Class

''' <summary>The viewer's own record of one job, written into the job folder beside (never inside) the converter's
''' output: what was asked, from which source state, and whether the page was handed to a browser. It is what makes
''' leases survive a restart and what reuse compares (section 6, 7.3 rule 5). The converter never reads it.</summary>
Public NotInheritable Class DocHtmlJobTicket
    Public Property SchemaVersion As Integer = 1
    Public Property Mode As String = ""
    Public Property OcrForce As Boolean
    Public Property OcrLanguages As String = ""
    Public Property SourceLanguage As String = ""
    Public Property TargetLanguage As String = ""
    Public Property LocalModel As String = ""
    Public Property SourcePath As String = ""
    Public Property SourceLength As Long
    Public Property SourceWriteUtcTicks As Long
    ''' <summary>The page reached a browser: a tab may still show it, so no automatic cleanup removes the folder.</summary>
    Public Property Leased As Boolean
End Class

''' <summary>Specification 276 Step 3.2: the job cache under the viewer's own folder - persistent leases, retention,
''' the quota, Clear, and reuse of a finished page. Every folder it deletes is a direct child of the cache root; a
''' leased folder and the running job's folder are never deleted by an automatic pass.</summary>
Public NotInheritable Class DocHtmlJobCache
    Public Const TicketFileName As String = ".fms-document-job.json"
    Private Const MaxTicketBytes As Integer = 64 * 1024

    Private Sub New()
    End Sub

    ''' <summary>The ticket of a request, before the source has been measured.</summary>
    Public Shared Function TicketFor(sourcePath As String, mode As String, options As DocHtmlJobOptions) As DocHtmlJobTicket
        Dim o = If(options, New DocHtmlJobOptions())
        Return New DocHtmlJobTicket With {.Mode = If(mode, ""), .OcrForce = o.OcrForce, .OcrLanguages = If(o.OcrLanguages, ""),
            .SourceLanguage = If(o.SourceLanguage, ""), .TargetLanguage = If(o.TargetLanguage, ""), .LocalModel = If(o.LocalModel, ""),
            .SourcePath = If(sourcePath, "")}
    End Function

    Public Shared Function Serialize(ticket As DocHtmlJobTicket) As String
        Return JsonSerializer.Serialize(ticket)
    End Function

    ''' <summary>Nothing for anything that is not a ticket this code wrote: unparsable, oversized or a newer schema.</summary>
    Public Shared Function Parse(json As String) As DocHtmlJobTicket
        If String.IsNullOrWhiteSpace(json) OrElse json.Length > MaxTicketBytes Then Return Nothing
        Try
            Dim ticket = JsonSerializer.Deserialize(Of DocHtmlJobTicket)(json)
            If ticket Is Nothing OrElse ticket.SchemaVersion <> 1 OrElse Not DocHtmlJobArguments.IsKnownMode(ticket.Mode) Then Return Nothing
            Return ticket
        Catch ex As JsonException
            Return Nothing
        Catch ex As NotSupportedException
            Return Nothing
        End Try
    End Function

    ''' <summary>The engine a mode makes the converter record (OCR-INVOCATION 1.2 item C, <c>options.engine</c>).</summary>
    Public Shared Function ExpectedEngine(mode As String) As String
        If String.Equals(mode, DocHtmlJobArguments.ModeLocal, StringComparison.Ordinal) Then Return "ollama"
        If String.Equals(mode, DocHtmlJobArguments.ModePaid, StringComparison.Ordinal) Then Return "google"
        Return "none"
    End Function

    ''' <summary>Section 7.3 rule 5: the same source state and the same result-shaping choices. The languages and the
    ''' model shape the page only when the converter translates (the producer drops them otherwise, 2.3), so outside
    ''' the local mode they are not compared.</summary>
    Public Shared Function SameRequest(earlier As DocHtmlJobTicket, request As DocHtmlJobTicket) As Boolean
        If earlier Is Nothing OrElse request Is Nothing Then Return False
        If Not String.Equals(earlier.Mode, request.Mode, StringComparison.Ordinal) Then Return False
        If earlier.OcrForce <> request.OcrForce Then Return False
        If Not String.Equals(earlier.OcrLanguages, request.OcrLanguages, StringComparison.OrdinalIgnoreCase) Then Return False
        If Not DocHtmlResultReader.SamePath(earlier.SourcePath, request.SourcePath) Then Return False
        If earlier.SourceLength <> request.SourceLength OrElse earlier.SourceWriteUtcTicks <> request.SourceWriteUtcTicks Then Return False
        If String.Equals(request.Mode, DocHtmlJobArguments.ModeLocal, StringComparison.Ordinal) Then
            If Not String.Equals(earlier.SourceLanguage, request.SourceLanguage, StringComparison.OrdinalIgnoreCase) Then Return False
            If Not String.Equals(earlier.TargetLanguage, request.TargetLanguage, StringComparison.OrdinalIgnoreCase) Then Return False
            If Not String.Equals(earlier.LocalModel, request.LocalModel, StringComparison.Ordinal) Then Return False
        ElseIf String.Equals(request.Mode, DocHtmlJobArguments.ModePaid, StringComparison.Ordinal) Then
            If Not String.Equals(earlier.SourceLanguage, request.SourceLanguage, StringComparison.OrdinalIgnoreCase) Then Return False
            If Not String.Equals(earlier.TargetLanguage, request.TargetLanguage, StringComparison.OrdinalIgnoreCase) Then Return False
        End If
        Return True
    End Function

    ''' <summary>A finished, verified page of the same request, recorded by the converter with the engine the mode asks
    ''' for; a translating run is reused only when its translation was full (2.3: partial work is always rebuilt).</summary>
    Public Shared Function IsReusable(earlier As DocHtmlJobTicket, result As DocHtmlResult, request As DocHtmlJobTicket) As Boolean
        If result Is Nothing OrElse result.State <> DocHtmlResultState.Completed OrElse result.EntryPath.Length = 0 Then Return False
        If Not SameRequest(earlier, request) Then Return False
        Dim engine = ExpectedEngine(request.Mode)
        If Not String.Equals(result.Engine, engine, StringComparison.Ordinal) Then Return False
        Return engine = "none" OrElse String.Equals(result.Translation, "full", StringComparison.Ordinal)
    End Function

    ''' <summary>Retention (section 5, DocumentResultRetentionDays): a folder last written before the cut-off goes,
    ''' unless it is leased or is the running job's.</summary>
    Public Shared Function IsExpired(lastWriteUtc As DateTime, leased As Boolean, nowUtc As DateTime, retentionDays As Integer) As Boolean
        If leased OrElse retentionDays <= 0 Then Return False
        Return lastWriteUtc < nowUtc.AddDays(-retentionDays)
    End Function

    ' ---- File-system half: every call below never throws and touches only direct children of the cache root. ----

    Public Shared Function ReadTicket(jobRoot As String) As DocHtmlJobTicket
        Try
            Dim file = Path.Combine(jobRoot, TicketFileName)
            Dim info As New FileInfo(file)
            If Not info.Exists OrElse info.Length > MaxTicketBytes Then Return Nothing
            Return Parse(IO.File.ReadAllText(file))
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                   TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException
            Return Nothing
        End Try
    End Function

    ''' <summary>Written to a temporary name and moved over the old one, so a crash leaves the old ticket or the new.</summary>
    Public Shared Function WriteTicket(jobRoot As String, ticket As DocHtmlJobTicket) As Boolean
        Try
            Dim target = Path.Combine(jobRoot, TicketFileName)
            Dim temp = target & ".tmp"
            IO.File.WriteAllText(temp, Serialize(ticket))
            IO.File.Move(temp, target, overwrite:=True)
            Return True
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                   TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException
            Return False
        End Try
    End Function

    ''' <summary>Marks a job folder as handed to a browser.</summary>
    Public Shared Function Lease(jobRoot As String) As Boolean
        Dim ticket = ReadTicket(jobRoot)
        If ticket Is Nothing Then Return False
        If ticket.Leased Then Return True
        ticket.Leased = True
        Return WriteTicket(jobRoot, ticket)
    End Function

    ''' <summary>The job folders, newest first. A reparse point is never a job folder.</summary>
    Public Shared Function JobFolders(cacheRoot As String) As List(Of DirectoryInfo)
        Dim result As New List(Of DirectoryInfo)()
        Try
            If String.IsNullOrEmpty(cacheRoot) OrElse Not Directory.Exists(cacheRoot) Then Return result
            For Each folder In New DirectoryInfo(cacheRoot).GetDirectories()
                If (folder.Attributes And FileAttributes.ReparsePoint) = 0 Then result.Add(folder)
            Next
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                   TypeOf ex Is System.Security.SecurityException
        End Try
        result.Sort(Function(a, b) b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc))
        Return result
    End Function

    Public Shared Function LeasedFolders(cacheRoot As String) As List(Of String)
        Dim result As New List(Of String)()
        For Each folder In JobFolders(cacheRoot)
            Dim ticket = ReadTicket(folder.FullName)
            If ticket IsNot Nothing AndAlso ticket.Leased Then result.Add(folder.FullName)
        Next
        Return result
    End Function

    ''' <summary>The newest earlier job whose page can be shown again for this request, or Nothing.</summary>
    Public Shared Function FindReusable(cacheRoot As String, request As DocHtmlJobTicket) As (JobRoot As String, Result As DocHtmlResult)?
        For Each folder In JobFolders(cacheRoot)
            Dim earlier = ReadTicket(folder.FullName)
            If Not SameRequest(earlier, request) Then Continue For
            Dim result = DocHtmlResultReader.Read(folder.FullName, request.SourcePath)
            If IsReusable(earlier, result, request) Then Return (folder.FullName, result)
        Next
        Return Nothing
    End Function

    ''' <summary>Removes the expired folders that are neither leased nor <paramref name="running"/>.</summary>
    Public Shared Function ApplyRetention(cacheRoot As String, retentionDays As Integer, nowUtc As DateTime, running As String) As Integer
        Dim removed As Integer
        For Each folder In JobFolders(cacheRoot)
            If DocHtmlResultReader.SamePath(folder.FullName, running) Then Continue For
            Dim ticket = ReadTicket(folder.FullName)
            Dim leased = ticket IsNot Nothing AndAlso ticket.Leased
            If IsExpired(NewestWrite(folder), leased, nowUtc, retentionDays) AndAlso DeleteFolder(folder) Then removed += 1
        Next
        Return removed
    End Function

    ''' <summary>The explicit Clear (section 6): every job folder except the running one, leased ones included - the
    ''' user asked for it. Returns how many folders went and how many could not be removed (in use).</summary>
    Public Shared Function ClearAll(cacheRoot As String, running As String) As (Removed As Integer, Kept As Integer)
        Dim removed, kept As Integer
        For Each folder In JobFolders(cacheRoot)
            If DocHtmlResultReader.SamePath(folder.FullName, running) Then Continue For
            If DeleteFolder(folder) Then removed += 1 Else kept += 1
        Next
        Return (removed, kept)
    End Function

    ''' <summary>The bytes under the cache root, for the quota check after the trim.</summary>
    Public Shared Function TotalBytes(cacheRoot As String) As Long
        Dim total As Long
        For Each folder In JobFolders(cacheRoot)
            Try
                For Each file In folder.EnumerateFiles("*", SearchOption.AllDirectories)
                    total += file.Length
                Next
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                       TypeOf ex Is System.Security.SecurityException
            End Try
        Next
        Return total
    End Function

    Private Shared Function NewestWrite(folder As DirectoryInfo) As DateTime
        Dim newest = folder.LastWriteTimeUtc
        Try
            For Each file In folder.EnumerateFiles("*", SearchOption.AllDirectories)
                If file.LastWriteTimeUtc > newest Then newest = file.LastWriteTimeUtc
            Next
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                   TypeOf ex Is System.Security.SecurityException
            ' A folder that cannot be measured counts as written now, so it is not expired by guesswork.
            Return DateTime.MaxValue
        End Try
        Return newest
    End Function

    Private Shared Function DeleteFolder(folder As DirectoryInfo) As Boolean
        Try
            folder.Delete(True)
            Return True
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                   TypeOf ex Is System.Security.SecurityException
            Return False
        End Try
    End Function

    ''' <summary>Section 6 Save result: the whole result folder (the page, its pictures and styles, the converter's record)
    ''' copied under <paramref name="destinationParent"/> as a new folder named after the result, with " (2)", " (3)" .. when
    ''' the name is taken - nothing in the destination is overwritten. The copy is written under a temporary name and renamed
    ''' only when complete, so a failed save never leaves a folder that looks finished. A reparse point inside the result
    ''' refuses the save. Returns the new folder; throws an IOException (or the access error) when the copy fails.</summary>
    Public Shared Function SaveResultCopy(resultFolder As String, destinationParent As String) As String
        Dim source As New DirectoryInfo(resultFolder)
        If Not source.Exists OrElse (source.Attributes And FileAttributes.ReparsePoint) <> 0 Then Throw New DirectoryNotFoundException()
        If Not Directory.Exists(destinationParent) Then Throw New DirectoryNotFoundException()
        Dim staging = Path.Combine(destinationParent, "." & source.Name & "." & Guid.NewGuid().ToString("N").Substring(0, 8) & ".partial")
        Try
            CopyTree(source, Directory.CreateDirectory(staging))
            Dim target = FreeFolderName(destinationParent, source.Name)
            Directory.Move(staging, target)
            Return target
        Catch
            If Directory.Exists(staging) Then DeleteFolder(New DirectoryInfo(staging))
            Throw
        End Try
    End Function

    ''' <summary>The first of "name", "name (2)", "name (3)" .. that names nothing in <paramref name="parent"/>.</summary>
    Public Shared Function FreeFolderName(parent As String, name As String) As String
        Dim candidate = Path.Combine(parent, name)
        Dim number As Integer = 2
        While Directory.Exists(candidate) OrElse File.Exists(candidate)
            candidate = Path.Combine(parent, String.Format(Globalization.CultureInfo.InvariantCulture, "{0} ({1})", name, number))
            number += 1
        End While
        Return candidate
    End Function

    Private Shared Sub CopyTree(source As DirectoryInfo, target As DirectoryInfo)
        For Each entry In source.EnumerateFileSystemInfos()
            If (entry.Attributes And FileAttributes.ReparsePoint) <> 0 Then Throw New IOException()
            Dim folder = TryCast(entry, DirectoryInfo)
            If folder IsNot Nothing Then
                CopyTree(folder, target.CreateSubdirectory(folder.Name))
            Else
                DirectCast(entry, FileInfo).CopyTo(Path.Combine(target.FullName, entry.Name), overwrite:=False)
            End If
        Next
    End Sub

End Class
#End If
