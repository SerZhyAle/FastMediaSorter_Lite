Option Strict On

Imports Xunit

''' <summary>Tests that switch the process-wide language, or compare several localized
''' results, must not overlap other collections that can change Localization.CurrentCode.</summary>
<CollectionDefinition("LocalizationState", DisableParallelization:=True)>
Public NotInheritable Class LocalizationStateCollection
End Class
