Option Strict On

Imports Xunit

''' <summary>
''' The one xUnit collection every test class that touches <c>Localization.CurrentCode</c> belongs
''' to.
'''
''' The active UI language is process-global mutable state, and xUnit runs test CLASSES in
''' parallel by default - so three classes that each politely saved and restored it were still
''' reading each other's value halfway through. That was a latent flake before the byte-stable
''' .fmscfg vector was added (the vector pins a localized `accessNote`, which made it fire every
''' run instead of occasionally).
'''
''' The collection also excludes other collections: UI language, theme and process-probe state are
''' process-global, and bounded STA lifecycle checks must not compete with parallel host/crypto fixtures.
''' Save-and-restore stays in each test as well: the collection stops the
''' races, the restore stops a test leaving the process in a language the next class did not ask
''' for.
''' </summary>
<CollectionDefinition(UiLanguageCollection.Name, DisableParallelization:=True)>
Public Class UiLanguageCollection
    Public Const Name As String = "ui-language"
End Class
