Imports System.IO
Imports System.Reflection

''' <summary>
''' Reads assets bundled into the Companion exe as embedded resources - the
''' Companion equivalent of LITE's <c>RuntimeBootstrap.OpenBundledAsset</c>. Only
''' the offline port-forward guide is bundled at Ф1; UI assets (flag glyphs) join
''' in Ф2. Resources are keyed by their <c>LogicalName</c> in the .vbproj.
''' </summary>
Public Module CompanionAssets

    ''' <summary>Opens a bundled asset stream, or Nothing if it is not present.</summary>
    Public Function OpenBundledAsset(logicalName As String) As Stream
        Return Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName)
    End Function

End Module
