#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Threading

' Specification 275 section 12: versioned, bounded position/appearance store. No page text,
' query, selection, cookies or secure locators: the entry type below has no field for them.

''' <summary>One document's remembered reading place and appearance overrides. A value of "no override"
''' (see each default) means the global preference applies.</summary>
Friend NotInheritable Class HtmlReaderEntry
    Public Property Identity As String = ""
    Public Property Length As Long
    Public Property LastWriteUtcTicks As Long
    Public Property Digest As String = ""
    Public Property PageKey As String = ""
    Public Property Anchor As String = ""
    Public Property ScrollRatio As Double
    ''' <summary>0 = no override, otherwise 25-500 percent.</summary>
    Public Property Zoom As Integer
    Public Property Presentation As String = ""
    Public Property Theme As String = ""
    Public Property FontFamily As String = ""
    ''' <summary>-1 = no override.</summary>
    Public Property FontSize As Integer = -1
    ''' <summary>-1 = no override, 0 = full width, otherwise 40-120 characters.</summary>
    Public Property ColumnWidth As Integer = -1
    Public Property Margin As Integer = -1
    ''' <summary>0 = no override, otherwise 1.0-3.0.</summary>
    Public Property LineHeight As Double
    ''' <summary>-1 = no override, 0 = off, 1 = on.</summary>
    Public Property WrapWideContent As Integer = -1
    Public Property LastAccessUtcTicks As Long

    Friend Function Copy() As HtmlReaderEntry
        Return DirectCast(MemberwiseClone(), HtmlReaderEntry)
    End Function

    Friend Function HasPosition() As Boolean
        Return PageKey.Length > 0 OrElse Anchor.Length > 0 OrElse ScrollRatio > 0
    End Function

    Friend Function HasAppearance() As Boolean
        Return Zoom <> 0 OrElse Presentation.Length > 0 OrElse Theme.Length > 0 OrElse FontFamily.Length > 0 OrElse
            FontSize >= 0 OrElse ColumnWidth >= 0 OrElse Margin >= 0 OrElse LineHeight > 0 OrElse WrapWideContent >= 0
    End Function

    Friend Sub ClearPosition()
        PageKey = ""
        Anchor = ""
        ScrollRatio = 0
    End Sub

    Friend Sub ClearAppearance()
        Zoom = 0
        Presentation = ""
        Theme = ""
        FontFamily = ""
        FontSize = -1
        ColumnWidth = -1
        Margin = -1
        LineHeight = 0
        WrapWideContent = -1
    End Sub
End Class

''' <summary>The persistent history file. Record and ResetAppearance change memory only; Save writes it.
''' A file that existed but could not be trusted (malformed JSON, a schema this build does not read, too
''' large, unreadable) puts the store in recovery lock: it is ignored, kept byte for byte and never
''' overwritten by Save. Only an explicit <see cref="Clear"/> replaces it. Identities compare ignoring case,
''' like the other document histories (a path is the usual identity).</summary>
Friend NotInheritable Class HtmlReaderState
    Friend Const SchemaVersion As Integer = 1
    Friend Const MaxFileBytes As Integer = 8 * 1024 * 1024
    Friend Const MaxTextLength As Integer = 2048
    Private Const MaxDigestLength As Integer = 128

    Private Shared ReadOnly parseOptions As New JsonDocumentOptions With {.MaxDepth = 16}
    Private Shared ReadOnly lazyCurrent As New Lazy(Of HtmlReaderState)(Function() CreateCurrent(), LazyThreadSafetyMode.ExecutionAndPublication)

    Private ReadOnly statePath As String
    Private ReadOnly gate As New Object()
    Private ReadOnly entries As New Dictionary(Of String, HtmlReaderEntry)(StringComparer.OrdinalIgnoreCase)
    Private locked As Boolean
    Private diagnosticText As String = ""
    Private dirty As Boolean
    Private lastStamp As Long

    Friend Sub New(path As String)
        statePath = If(path, "")
    End Sub

    Friend Shared Function DefaultPath() As String
        Return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                      "SZA", "FastMediaSorter", "documents", "html-state-v1.json")
    End Function

    ''' <summary>The viewer's store: created and loaded once, on first use.</summary>
    Friend Shared ReadOnly Property Current As HtmlReaderState
        Get
            Return lazyCurrent.Value
        End Get
    End Property

    Private Shared Function CreateCurrent() As HtmlReaderState
        Dim state As New HtmlReaderState(DefaultPath())
        state.Load()
        Return state
    End Function

    ''' <summary>True when the file existed but is kept untouched (see the class remarks).</summary>
    Friend ReadOnly Property IsRecoveryLocked As Boolean
        Get
            SyncLock gate
                Return locked
            End SyncLock
        End Get
    End Property

    ''' <summary>A short English phrase for the last load or save problem, never a path; empty when all is well.</summary>
    Friend ReadOnly Property Diagnostic As String
        Get
            SyncLock gate
                Return diagnosticText
            End SyncLock
        End Get
    End Property

    Friend ReadOnly Property Count As Integer
        Get
            SyncLock gate
                Return entries.Count
            End SyncLock
        End Get
    End Property

    Friend Sub Load()
        SyncLock gate
            entries.Clear()
            locked = False
            diagnosticText = ""
            dirty = False
            Dim bytes As Byte() = Nothing
            Try
                If statePath.Length = 0 OrElse Not File.Exists(statePath) Then Return
                If New FileInfo(statePath).Length > MaxFileBytes Then
                    LockForRecovery("too-large: the reading state file is over its size limit and was kept unchanged")
                    Return
                End If
                bytes = File.ReadAllBytes(statePath)
            Catch ex As Exception
                LockForRecovery("unreadable: the reading state file could not be read and was kept unchanged")
                Return
            End Try
            ParseInto(bytes)
        End SyncLock
    End Sub

    Private Sub LockForRecovery(message As String)
        entries.Clear()
        locked = True
        diagnosticText = message
    End Sub

    Private Sub ParseInto(bytes As Byte())
        Dim offset As Integer = If(bytes.Length >= 3 AndAlso bytes(0) = &HEF AndAlso bytes(1) = &HBB AndAlso bytes(2) = &HBF, 3, 0)
        Try
            Using document As JsonDocument = JsonDocument.Parse(New ReadOnlyMemory(Of Byte)(bytes, offset, bytes.Length - offset), parseOptions)
                Dim root As JsonElement = document.RootElement
                Dim schemaElement As JsonElement
                Dim schema As Integer
                If root.ValueKind <> JsonValueKind.Object OrElse Not root.TryGetProperty("schema", schemaElement) OrElse
                   schemaElement.ValueKind <> JsonValueKind.Number OrElse Not schemaElement.TryGetInt32(schema) Then
                    LockForRecovery("malformed: the reading state file is not a valid state document and was kept unchanged")
                    Return
                End If
                If schema <> SchemaVersion Then
                    LockForRecovery("unsupported-schema: the reading state file has a schema this version does not read and was kept unchanged")
                    Return
                End If
                Dim list As JsonElement
                If Not root.TryGetProperty("entries", list) OrElse list.ValueKind <> JsonValueKind.Array Then
                    LockForRecovery("malformed: the reading state file is not a valid state document and was kept unchanged")
                    Return
                End If
                For Each item As JsonElement In list.EnumerateArray()
                    Dim entry As HtmlReaderEntry = ReadEntry(item)
                    If entry Is Nothing Then Continue For
                    Dim existing As HtmlReaderEntry = Nothing
                    If Not entries.TryGetValue(entry.Identity, existing) OrElse existing.LastAccessUtcTicks <= entry.LastAccessUtcTicks Then
                        entries(entry.Identity) = entry
                    End If
                Next
            End Using
        Catch ex As JsonException
            LockForRecovery("malformed: the reading state file is not valid JSON and was kept unchanged")
            Return
        End Try
        Evict(ModernViewerPreferences.HtmlHistoryLimitMax)
        For Each entry As HtmlReaderEntry In entries.Values
            lastStamp = Math.Max(lastStamp, entry.LastAccessUtcTicks)
        Next
    End Sub

    ' One bad entry (a wrong type, an oversized string) is dropped; the others stay usable.
    Private Shared Function ReadEntry(item As JsonElement) As HtmlReaderEntry
        If item.ValueKind <> JsonValueKind.Object Then Return Nothing
        Dim e As New HtmlReaderEntry()
        Dim zoom, fontSize, columnWidth, margin, wrap As Long
        Dim ratio, lineHeight As Double
        Dim presentation As String = ""
        Dim theme As String = ""
        Dim family As String = ""
        If Not (ReadText(item, "identity", MaxTextLength, e.Identity) AndAlso ReadText(item, "digest", MaxDigestLength, e.Digest) AndAlso
                ReadText(item, "pageKey", MaxTextLength, e.PageKey) AndAlso ReadText(item, "anchor", MaxTextLength, e.Anchor) AndAlso
                ReadText(item, "presentation", 32, presentation) AndAlso ReadText(item, "theme", 32, theme) AndAlso
                ReadText(item, "fontFamily", 256, family) AndAlso
                ReadInteger(item, "length", 0, e.Length) AndAlso ReadInteger(item, "lastWriteUtcTicks", 0, e.LastWriteUtcTicks) AndAlso
                ReadInteger(item, "lastAccess", 0, e.LastAccessUtcTicks) AndAlso
                ReadInteger(item, "zoom", 0, zoom) AndAlso ReadInteger(item, "fontSize", -1, fontSize) AndAlso
                ReadInteger(item, "columnWidth", -1, columnWidth) AndAlso ReadInteger(item, "margin", -1, margin) AndAlso
                ReadInteger(item, "wrap", -1, wrap) AndAlso
                ReadNumber(item, "scrollRatio", ratio) AndAlso ReadNumber(item, "lineHeight", lineHeight)) Then Return Nothing
        e.ScrollRatio = ratio
        e.LineHeight = lineHeight
        e.Zoom = CInt(Math.Max(Integer.MinValue, Math.Min(Integer.MaxValue, zoom)))
        e.FontSize = CInt(Math.Max(Integer.MinValue, Math.Min(Integer.MaxValue, fontSize)))
        e.ColumnWidth = CInt(Math.Max(Integer.MinValue, Math.Min(Integer.MaxValue, columnWidth)))
        e.Margin = CInt(Math.Max(Integer.MinValue, Math.Min(Integer.MaxValue, margin)))
        e.WrapWideContent = CInt(Math.Max(Integer.MinValue, Math.Min(Integer.MaxValue, wrap)))
        e.Presentation = presentation
        e.Theme = theme
        e.FontFamily = family
        Return Sanitize(e)
    End Function

    Private Shared Function ReadText(item As JsonElement, name As String, maxLength As Integer, ByRef value As String) As Boolean
        value = ""
        Dim prop As JsonElement
        If Not item.TryGetProperty(name, prop) OrElse prop.ValueKind = JsonValueKind.Null Then Return True
        If prop.ValueKind <> JsonValueKind.String Then Return False
        Dim text As String = prop.GetString()
        If text Is Nothing OrElse text.Length > maxLength Then Return False
        value = text
        Return True
    End Function

    Private Shared Function ReadInteger(item As JsonElement, name As String, fallback As Long, ByRef value As Long) As Boolean
        value = fallback
        Dim prop As JsonElement
        If Not item.TryGetProperty(name, prop) OrElse prop.ValueKind = JsonValueKind.Null Then Return True
        Return prop.ValueKind = JsonValueKind.Number AndAlso prop.TryGetInt64(value)
    End Function

    Private Shared Function ReadNumber(item As JsonElement, name As String, ByRef value As Double) As Boolean
        value = 0
        Dim prop As JsonElement
        If Not item.TryGetProperty(name, prop) OrElse prop.ValueKind = JsonValueKind.Null Then Return True
        Return prop.ValueKind = JsonValueKind.Number AndAlso prop.TryGetDouble(value)
    End Function

    ''' <summary>A normalized copy, or Nothing when the identity cannot be stored. Numbers are clamped to the
    ''' preference bounds; an unknown enum or an unusable font means "no override".</summary>
    Private Shared Function Sanitize(source As HtmlReaderEntry) As HtmlReaderEntry
        If source Is Nothing OrElse String.IsNullOrEmpty(source.Identity) OrElse source.Identity.Length > MaxTextLength Then Return Nothing
        Dim e As HtmlReaderEntry = source.Copy()
        e.Length = Math.Max(0L, e.Length)
        e.LastWriteUtcTicks = Math.Max(0L, e.LastWriteUtcTicks)
        e.LastAccessUtcTicks = Math.Max(0L, e.LastAccessUtcTicks)
        e.Digest = If(e.Digest, "")
        If e.Digest.Length > MaxDigestLength Then e.Digest = ""
        e.PageKey = If(e.PageKey, "")
        e.Anchor = If(e.Anchor, "")
        If e.PageKey.Length > MaxTextLength OrElse e.Anchor.Length > MaxTextLength Then e.ClearPosition()
        e.ScrollRatio = If(Double.IsNaN(e.ScrollRatio) OrElse e.ScrollRatio < 0, 0, Math.Min(1.0, e.ScrollRatio))

        e.Zoom = If(e.Zoom <= 0, 0, Math.Max(ModernViewerPreferences.HtmlZoomMin, Math.Min(ModernViewerPreferences.HtmlZoomMax, e.Zoom)))
        e.FontSize = If(e.FontSize < 0, -1, Math.Max(ModernViewerPreferences.HtmlFontSizeMin, Math.Min(ModernViewerPreferences.HtmlFontSizeMax, e.FontSize)))
        e.ColumnWidth = If(e.ColumnWidth < 0, -1, If(e.ColumnWidth = 0, 0,
            Math.Max(ModernViewerPreferences.HtmlColumnWidthMin, Math.Min(ModernViewerPreferences.HtmlColumnWidthMax, e.ColumnWidth))))
        e.Margin = If(e.Margin < 0, -1, Math.Min(ModernViewerPreferences.HtmlMarginMax, e.Margin))
        e.LineHeight = If(Double.IsNaN(e.LineHeight) OrElse e.LineHeight <= 0, 0,
            Math.Round(Math.Max(1.0, Math.Min(3.0, e.LineHeight)), 1, MidpointRounding.AwayFromZero))
        e.WrapWideContent = If(e.WrapWideContent < 0, -1, If(e.WrapWideContent = 0, 0, 1))
        e.Presentation = OneOf(e.Presentation, "original", "reader")
        e.Theme = OneOf(e.Theme, "system", "light", "dark", "sepia", "black")
        e.FontFamily = SanitizeFont(e.FontFamily)
        Return e
    End Function

    Private Shared Function OneOf(value As String, ParamArray allowed As String()) As String
        If String.IsNullOrEmpty(value) Then Return ""
        For Each token As String In allowed
            If String.Equals(value, token, StringComparison.OrdinalIgnoreCase) Then Return token
        Next
        Return ""
    End Function

    Private Shared Function SanitizeFont(value As String) As String
        If String.IsNullOrWhiteSpace(value) Then Return ""
        Dim normalized As String = ModernViewerPreferences.NormalizeHtmlFontFamily(value)
        ' The normalizer answers "authored" for a name it refuses; that is only right when "authored" was asked for.
        If String.Equals(normalized, "authored", StringComparison.Ordinal) AndAlso
           Not String.Equals(value.Trim(), "authored", StringComparison.Ordinal) Then Return ""
        Return normalized
    End Function

    Private Shared Function DigestsAgree(a As String, b As String) As Boolean
        Return String.IsNullOrEmpty(a) OrElse String.IsNullOrEmpty(b) OrElse String.Equals(a, b, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared Function VersionMatches(stored As HtmlReaderEntry, version As HtmlSourceVersion) As Boolean
        Return version IsNot Nothing AndAlso stored.Length = version.Length AndAlso stored.LastWriteUtcTicks = version.LastWriteUtcTicks AndAlso
            DigestsAgree(stored.Digest, version.Digest)
    End Function

    ''' <summary>A copy of the remembered entry, or Nothing for an unknown document (and while recovery-locked).
    ''' When the file changed since (length, write time or a known digest differs) the copy keeps the appearance
    ''' overrides and drops the place: a position in a different file is a wrong position.</summary>
    Friend Function Lookup(identity As String, version As HtmlSourceVersion) As HtmlReaderEntry
        If String.IsNullOrEmpty(identity) Then Return Nothing
        SyncLock gate
            Dim stored As HtmlReaderEntry = Nothing
            If locked OrElse Not entries.TryGetValue(identity, stored) Then Return Nothing
            Dim result As HtmlReaderEntry = stored.Copy()
            If Not VersionMatches(stored, version) Then result.ClearPosition()
            Return result
        End SyncLock
    End Function

    ''' <summary>Stores the entry as the newest one. limit 0 writes nothing; above the limit the least recently
    ''' used entries go. A switch that is False keeps NO place (or no appearance) in the stored entry, an older
    ''' one of the same document included; an entry left with neither is not kept at all.</summary>
    Friend Sub Record(entry As HtmlReaderEntry, limit As Integer, rememberPosition As Boolean, rememberAppearance As Boolean)
        If limit <= 0 Then Return
        limit = Math.Min(limit, ModernViewerPreferences.HtmlHistoryLimitMax)
        Dim clean As HtmlReaderEntry = Sanitize(entry)
        If clean Is Nothing Then Return
        SyncLock gate
            If locked Then Return
            If Not rememberPosition Then clean.ClearPosition()
            If Not rememberAppearance Then clean.ClearAppearance()
            If Not clean.HasPosition() AndAlso Not clean.HasAppearance() Then
                If entries.Remove(clean.Identity) Then dirty = True
                Return
            End If
            clean.LastAccessUtcTicks = NextStamp()
            entries(clean.Identity) = clean
            dirty = True
            Evict(limit)
        End SyncLock
    End Sub

    ''' <summary>Removes the document's appearance overrides and keeps its place.</summary>
    Friend Sub ResetAppearance(identity As String)
        If String.IsNullOrEmpty(identity) Then Return
        SyncLock gate
            Dim stored As HtmlReaderEntry = Nothing
            If locked OrElse Not entries.TryGetValue(identity, stored) Then Return
            stored.ClearAppearance()
            If Not stored.HasPosition() Then entries.Remove(identity)
            dirty = True
        End SyncLock
    End Sub

    ''' <summary>The settings "Reset appearance" for every document: all appearance overrides go, every place stays
    ''' (an entry left with neither is dropped). Nothing is written until <see cref="Save"/>.</summary>
    Friend Sub ResetAllAppearance()
        SyncLock gate
            If locked Then Return
            For Each key As String In entries.Keys.ToList()
                Dim stored As HtmlReaderEntry = entries(key)
                If Not stored.HasAppearance() Then Continue For
                stored.ClearAppearance()
                If Not stored.HasPosition() Then entries.Remove(key)
                dirty = True
            Next
        End SyncLock
    End Sub

    ''' <summary>The explicit "clear history" decision: every entry goes and a valid empty file is written, even
    ''' over a recovery-locked one.</summary>
    Friend Sub Clear()
        SyncLock gate
            entries.Clear()
            locked = False
            diagnosticText = ""
            dirty = True
            WriteFile()
        End SyncLock
    End Sub

    ''' <summary>Writes the file atomically when something changed. Never throws; a failure becomes the
    ''' <see cref="Diagnostic"/> and the change stays in memory. A recovery-locked store writes nothing.</summary>
    Friend Sub Save()
        SyncLock gate
            If locked OrElse Not dirty Then Return
            WriteFile()
        End SyncLock
    End Sub

    Private Function NextStamp() As Long
        lastStamp = Math.Max(DateTime.UtcNow.Ticks, lastStamp + 1)
        Return lastStamp
    End Function

    Private Sub Evict(limit As Integer)
        Dim excess As Integer = entries.Count - limit
        If excess <= 0 Then Return
        For Each key As String In entries.Values.OrderBy(Function(item) item.LastAccessUtcTicks).Take(excess).Select(Function(item) item.Identity).ToList()
            entries.Remove(key)
        Next
        dirty = True
    End Sub

    Private Sub WriteFile()
        Dim temp As String = Nothing
        Try
            If statePath.Length = 0 Then Return
            Dim bytes As Byte() = Serialize()
            Dim folder As String = System.IO.Path.GetDirectoryName(statePath)
            If Not String.IsNullOrEmpty(folder) Then System.IO.Directory.CreateDirectory(folder)
            temp = statePath & "." & Guid.NewGuid().ToString("N") & ".tmp"
            File.WriteAllBytes(temp, bytes)
            If File.Exists(statePath) Then
                File.Replace(temp, statePath, Nothing)
            Else
                File.Move(temp, statePath)
            End If
            temp = Nothing
            dirty = False
            diagnosticText = ""
        Catch ex As Exception
            diagnosticText = "save-failed: the reading state could not be written and stays in memory only"
        Finally
            If temp IsNot Nothing Then
                Try
                    File.Delete(temp)
                Catch ex As Exception
                    ' A leftover temp file is harmless; the next save uses a new name.
                End Try
            End If
        End Try
    End Sub

    ' A file the next load would refuse for size is never written: the oldest entries give way first.
    Private Function Serialize() As Byte()
        Dim ordered As List(Of HtmlReaderEntry) = entries.Values.OrderByDescending(Function(item) item.LastAccessUtcTicks).ToList()
        Dim bytes As Byte() = Render(ordered)
        While bytes.Length > MaxFileBytes AndAlso ordered.Count > 0
            Dim drop As Integer = Math.Max(1, ordered.Count \ 10)
            For index As Integer = ordered.Count - drop To ordered.Count - 1
                entries.Remove(ordered(index).Identity)
            Next
            ordered.RemoveRange(ordered.Count - drop, drop)
            bytes = Render(ordered)
        End While
        Return bytes
    End Function

    Private Shared Function Render(list As List(Of HtmlReaderEntry)) As Byte()
        Using stream As New MemoryStream()
            Using writer As New Utf8JsonWriter(stream)
                writer.WriteStartObject()
                writer.WriteNumber("schema", SchemaVersion)
                writer.WriteStartArray("entries")
                For Each e As HtmlReaderEntry In list
                    writer.WriteStartObject()
                    writer.WriteString("identity", e.Identity)
                    writer.WriteNumber("length", e.Length)
                    writer.WriteNumber("lastWriteUtcTicks", e.LastWriteUtcTicks)
                    If e.Digest.Length > 0 Then writer.WriteString("digest", e.Digest)
                    If e.PageKey.Length > 0 Then writer.WriteString("pageKey", e.PageKey)
                    If e.Anchor.Length > 0 Then writer.WriteString("anchor", e.Anchor)
                    If e.ScrollRatio > 0 Then writer.WriteNumber("scrollRatio", e.ScrollRatio)
                    If e.Zoom <> 0 Then writer.WriteNumber("zoom", e.Zoom)
                    If e.Presentation.Length > 0 Then writer.WriteString("presentation", e.Presentation)
                    If e.Theme.Length > 0 Then writer.WriteString("theme", e.Theme)
                    If e.FontFamily.Length > 0 Then writer.WriteString("fontFamily", e.FontFamily)
                    If e.FontSize >= 0 Then writer.WriteNumber("fontSize", e.FontSize)
                    If e.ColumnWidth >= 0 Then writer.WriteNumber("columnWidth", e.ColumnWidth)
                    If e.Margin >= 0 Then writer.WriteNumber("margin", e.Margin)
                    If e.LineHeight > 0 Then writer.WriteNumber("lineHeight", e.LineHeight)
                    If e.WrapWideContent >= 0 Then writer.WriteNumber("wrap", e.WrapWideContent)
                    writer.WriteNumber("lastAccess", e.LastAccessUtcTicks)
                    writer.WriteEndObject()
                Next
                writer.WriteEndArray()
                writer.WriteEndObject()
            End Using
            Return stream.ToArray()
        End Using
    End Function
End Class
#End If
