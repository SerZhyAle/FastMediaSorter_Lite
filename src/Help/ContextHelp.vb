#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Windows.Forms

''' <summary>
''' CONTEXT-HELP rules 3, 12, 17: in-app context help resolver and launcher.
''' Both F1 and the corner '?' button resolve through the shipped help map.
''' </summary>
Public NotInheritable Class ContextHelp

    Private Sub New()
    End Sub

    Public Class SurfaceEntry
        Public Property id As String = ""
        Public Property name As String = ""
        Public Property type As String = ""
        Public Property form As String = ""
        Public Property help As String = ""
        Public Property parent As String = ""
        Public Property reason As String = ""
        Public Property pages As List(Of SubordinatePage)
    End Class

    Public Class SubordinatePage
        Public Property id As String = ""
        Public Property name As String = ""
        Public Property anchor As String = ""
    End Class

    Public Class HelpMap
        Public Property schemaVersion As Integer
        Public Property product As String = ""
        Public Property helpRoot As String = ""
        Public Property pageTemplate As String = ""
        Public Property index As String = ""
        Public Property surfaces As List(Of SurfaceEntry) = New List(Of SurfaceEntry)()
    End Class

    Private Shared ReadOnly ShippedMap As New Lazy(Of HelpMap)(AddressOf LoadMap)

    Private Shared Function LoadMap() As HelpMap
        Dim stream As Stream = Nothing
        Try
            ' 1. Try manifest resource from executing or calling assembly
            Dim asm = Assembly.GetExecutingAssembly()
            stream = asm.GetManifestResourceStream("help-map.json")
            If stream Is Nothing Then
                For Each res In asm.GetManifestResourceNames()
                    If res.EndsWith("help-map.json", StringComparison.OrdinalIgnoreCase) Then
                        stream = asm.GetManifestResourceStream(res)
                        Exit For
                    End If
                Next
            End If
            If stream IsNot Nothing Then
                Using reader As New StreamReader(stream, Encoding.UTF8)
                    Return ReadMap(reader.ReadToEnd())
                End Using
            End If

            ' 2. Fall back to local file on disk
            Dim candidates As String() = {
                Path.Combine(AppContext.BaseDirectory, "help-map.json"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "help-map.json"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "help-map.json"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "help-map.json"),
                Path.Combine(Environment.CurrentDirectory, "help-map.json")
            }
            For Each candidate In candidates
                If File.Exists(candidate) Then
                    Return ReadMap(File.ReadAllText(candidate, Encoding.UTF8))
                End If
            Next
        Catch ex As Exception
            AppFileLogger.LogException("ContextHelp.LoadMap", ex)
        Finally
            If stream IsNot Nothing Then stream.Dispose()
        End Try
        Return New HelpMap() With {.schemaVersion = 1}
    End Function

    Public Shared ReadOnly Property Inventory As HelpMap
        Get
            Return ShippedMap.Value
        End Get
    End Property

    Public Shared Function ReadMap(json As String) As HelpMap
        If String.IsNullOrWhiteSpace(json) Then Throw New InvalidDataException("Help map is empty.")
        Dim opts As New JsonSerializerOptions With {.PropertyNameCaseInsensitive = True}
        Dim map = JsonSerializer.Deserialize(Of HelpMap)(json, opts)
        If map Is Nothing OrElse map.schemaVersion <> 1 Then
            Throw New InvalidDataException("Unsupported help map version.")
        End If
        Return map
    End Function

    Public Shared Property Launch As Action(Of String) = Sub(path)
        Process.Start(New ProcessStartInfo(path) With {.UseShellExecute = True})
    End Sub

    Public Shared Function SurfaceId(form As Form) As String
        If form Is Nothing Then Return "index"
        Dim formType = form.GetType()
        Dim typeName = formType.Name

        If typeName = "Table_Form" Then
            Dim prop = formType.GetProperty("ActiveHelpPageId", BindingFlags.Public Or BindingFlags.NonPublic Or BindingFlags.Instance)
            If prop IsNot Nothing Then
                Dim val = TryCast(prop.GetValue(form), String)
                If Not String.IsNullOrEmpty(val) Then Return "settings." & val
            End If
        ElseIf typeName = "Share_Settings_Form" Then
            Dim prop = formType.GetProperty("ActiveHelpPageId", BindingFlags.Public Or BindingFlags.NonPublic Or BindingFlags.Instance)
            If prop IsNot Nothing Then
                Dim val = TryCast(prop.GetValue(form), String)
                If Not String.IsNullOrEmpty(val) Then Return "share-settings." & val
            End If
        End If

        Dim fullName = formType.FullName
        Dim surface = Inventory.surfaces.FirstOrDefault(Function(s) String.Equals(s.type, fullName, StringComparison.Ordinal))
        If surface IsNot Nothing Then Return surface.id
        Return "index"
    End Function

    Public Shared Function Resolve(id As String, languageCode As String, Optional element As String = Nothing) As String
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        Dim currentId As String = id
        Dim surface As SurfaceEntry = Nothing

        While True
            surface = Inventory.surfaces.FirstOrDefault(Function(s) String.Equals(s.id, currentId, StringComparison.Ordinal))
            If surface Is Nothing Then
                For Each s In Inventory.surfaces
                    If s.pages IsNot Nothing Then
                        Dim subPage = s.pages.FirstOrDefault(Function(p) String.Equals(p.id, currentId, StringComparison.Ordinal))
                        If subPage IsNot Nothing Then
                            surface = s
                            If String.IsNullOrEmpty(element) AndAlso Not String.IsNullOrEmpty(subPage.anchor) Then
                                element = subPage.anchor
                            End If
                            Exit For
                        End If
                    End If
                Next
                Exit While
            End If
            If Not seen.Add(currentId) Then Throw New InvalidDataException("Cyclic help inheritance.")
            If surface.form = "none" Then Return Nothing
            If surface.form = "layer" Then Return Nothing
            If surface.form <> "inherit" Then Exit While
            currentId = surface.parent
        End While

        If surface Is Nothing Then Return Nothing

        Dim helpName As String = If(surface IsNot Nothing AndAlso Not String.IsNullOrEmpty(surface.help), surface.help, "index")
        Dim locale As String = If(String.IsNullOrEmpty(languageCode), "en", languageCode.ToLowerInvariant())
        If Not IsSupportedLocale(locale) Then locale = "en"

        Dim target As String = locale & "/" & helpName & ".html"
        If Not HasAsset(target) Then target = "en/" & helpName & ".html"
        If Not HasAsset(target) Then target = "en/index.html"

        If Not String.IsNullOrEmpty(element) AndAlso Regex.IsMatch(element, "\A[a-zA-Z0-9_\-]+\z") Then
            target &= "#" & element
        End If
        Return target
    End Function

    Private Shared Function IsSupportedLocale(code As String) As Boolean
        Return Localization.Codes.Contains(code)
    End Function

    Private Shared Function HasAsset(relative As String) As Boolean
        Dim normalized = relative.Replace("\"c, "/"c)
        ' Check local disk first (docs/help/<relative> or bin/Release/help/<relative>)
        Dim candidates As String() = {
            Path.Combine(AppContext.BaseDirectory, "help", normalized),
            Path.Combine(AppContext.BaseDirectory, "docs", "help", normalized),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "docs", "help", normalized),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "help", normalized),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "help", normalized),
            Path.Combine(Environment.CurrentDirectory, "docs", "help", normalized)
        }
        For Each c In candidates
            If File.Exists(c) Then Return True
        Next
        ' Check manifest resource
        Dim asm = Assembly.GetExecutingAssembly()
        Return asm.GetManifestResourceNames().Any(Function(n) n.EndsWith(normalized.Replace("/"c, "."), StringComparison.OrdinalIgnoreCase))
    End Function

    Public Shared Sub Open(form As Form)
        If form Is Nothing Then Return
        Dim id = SurfaceId(form)
        Dim s = Inventory.surfaces.FirstOrDefault(Function(row) String.Equals(row.id, id, StringComparison.Ordinal))
        If s IsNot Nothing AndAlso s.form = "layer" Then
            Return
        End If
        Dim lang = Localization.CurrentCode
        Open(id, lang, Nothing)
    End Sub

    Public Shared Sub Open(id As String, language As String, Optional element As String = Nothing)
        Dim relative = Resolve(id, language, element)
        If String.IsNullOrEmpty(relative) Then Return
        Try
            Dim path = Extract(relative)
            Launch.Invoke(path)
        Catch ex As Exception
            AppFileLogger.LogException("ContextHelp Open failed", ex)
        End Try
    End Sub

    Private Shared ReadOnly ExtractionLock As New Object()

    Public Shared Function Extract(target As String, Optional cacheRoot As String = Nothing) As String
        SyncLock ExtractionLock
            Return ExtractCore(target, cacheRoot)
        End SyncLock
    End Function

    Private Shared Function ExtractCore(target As String, cacheRoot As String) As String
        Dim parts = target.Split("#"c, 2)
        Dim relativeFile = parts(0).Replace("/"c, Path.DirectorySeparatorChar)
        Dim fragment = If(parts.Length > 1, "#" & parts(1), "")

        ' Check traversal
        Dim fullRelative = Path.GetFullPath(Path.Combine("C:\dummy", relativeFile))
        If Not fullRelative.StartsWith("C:\dummy", StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidDataException("Invalid help asset path.")
        End If

        ' Find source candidate
        Dim localSourceCandidates As String() = {
            Path.Combine(AppContext.BaseDirectory, "help", relativeFile),
            Path.Combine(AppContext.BaseDirectory, "docs", "help", relativeFile),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "docs", "help", relativeFile),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "help", relativeFile),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "help", relativeFile),
            Path.Combine(Environment.CurrentDirectory, "docs", "help", relativeFile)
        }
        Dim existingSource As String = Nothing
        For Each cand In localSourceCandidates
            If File.Exists(cand) Then
                existingSource = Path.GetFullPath(cand)
                Exit For
            End If
        Next

        If Not String.IsNullOrEmpty(cacheRoot) Then
            ' When cacheRoot is explicitly specified, copy existing source or extract resource to cacheRoot
            Directory.CreateDirectory(cacheRoot)
            Dim destFile = Path.Combine(cacheRoot, relativeFile)
            Directory.CreateDirectory(Path.GetDirectoryName(destFile))
            If existingSource IsNot Nothing Then
                File.Copy(existingSource, destFile, True)
            Else
                Dim asm = Assembly.GetExecutingAssembly()
                Dim resName = asm.GetManifestResourceNames().FirstOrDefault(Function(n) n.EndsWith(parts(0).Replace("/"c, "."), StringComparison.OrdinalIgnoreCase))
                If resName IsNot Nothing Then
                    Using stream = asm.GetManifestResourceStream(resName)
                        Using fs As New FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.Read)
                            stream.CopyTo(fs)
                        End Using
                    End Using
                End If
            End If
            Return (New Uri(destFile)).AbsoluteUri & fragment
        End If

        ' If on disk directly, return directly
        If existingSource IsNot Nothing Then
            Return (New Uri(existingSource)).AbsoluteUri & fragment
        End If

        ' Extract from resources to local appdata cache
        Dim root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SZA", "FastMediaSorter", "Help")
        Directory.CreateDirectory(root)
        Dim defaultDest = Path.Combine(root, relativeFile)
        Directory.CreateDirectory(Path.GetDirectoryName(defaultDest))

        Dim asmDef = Assembly.GetExecutingAssembly()
        Dim resDef = asmDef.GetManifestResourceNames().FirstOrDefault(Function(n) n.EndsWith(parts(0).Replace("/"c, "."), StringComparison.OrdinalIgnoreCase))
        If resDef IsNot Nothing Then
            Using stream = asmDef.GetManifestResourceStream(resDef)
                Using fs As New FileStream(defaultDest, FileMode.Create, FileAccess.Write, FileShare.Read)
                    stream.CopyTo(fs)
                End Using
            End Using
        End If

        Return (New Uri(defaultDest)).AbsoluteUri & fragment
    End Function

End Class
#End If
