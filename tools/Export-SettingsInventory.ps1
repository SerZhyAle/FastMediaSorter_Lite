<#
.SYNOPSIS
Regenerates the current settings catalog region in the compatibility inventory from production sources.
.DESCRIPTION
The group declarations own placement; bound services own defaults and persistence. This export does
not load a user profile or write settings. Caption/hint and dependency references remain executable
source references rather than a second translation or validation table.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$groups = [IO.File]::ReadAllText((Join-Path $repo 'src/Table_Form.SettingsGroups.vb'))
$expanded = [IO.File]::ReadAllText((Join-Path $repo 'src/Table_Form.ExpandedSettings.vb'))
$modern = [IO.File]::ReadAllText((Join-Path $repo 'src/ModernViewerPreferences.vb'))
$ocr = [IO.File]::ReadAllText((Join-Path $repo 'src/OcrTranslateSettings.vb'))
$legacy = [IO.File]::ReadAllText((Join-Path $repo 'src/Main_Form.Lifecycle.vb'))
$bindings = @{}
$constraints = @{}
foreach ($match in [regex]::Matches($expanded, 'Add(?:Preference\w+|NavigationKindCheck)\(\w+,\s*"(?<id>[^"]+)"(?<body>[\s\S]*?)Function\(\) Main_Form.GetModernPreferences\(\)\.(?<property>\w+)')) {
    $bindings[$match.Groups['id'].Value] = $match.Groups['property'].Value
    $body = $match.Groups['body'].Value
    $tokens = @([regex]::Matches($body, 'Choice\("([^"]*)"') | ForEach-Object { $_.Groups[1].Value })
    if ($tokens.Count -gt 0) { $constraints[$match.Groups['id'].Value] = 'tokens: ' + ($tokens -join ', ') }
    elseif ($body.Trim().StartsWith(',')) {
        $arguments = ($body -split "`r?`n")[0].Trim().Trim(',').Trim()
        if ($arguments) { $constraints[$match.Groups['id'].Value] = 'range: ' + $arguments }
    }
}
$bindings.interface_scale = 'InterfaceScaleRelativePercent'
$bindings.advance_after_copy = 'AdvanceAfterCopy'
$bindings.allow_new_windows = 'AllowNewWindows'
$bindings.included_extensions = 'IncludedExtensions'
$bindings.document_extensions = 'DocumentExtensions'
$bindings.other_extensions = 'OtherExtensions'
$legacyKeys = @{
    recipients_overlay='ShowRecipientsOverlay'; background_color='color_scheme'; perspective='isPerspective'
    dynamic_perspective='DynamicPerspective'; animated_perspective='AnimatedPerspective'; halo_speed='AnimatedPerspectiveSpeed'
    show_picture_size='ShowPictureSizes'; show_file_size='ShowFileSizes'; show_file_date='ShowFileDates'
    show_info_overlay='ShowInfoOverlay'; wheel_zooms='WheelZooms'; slideshow_interval='SlideshowIntervalSec'
    thumbnail_size='Picture_Box_Width_At_Panel'; exif_rotate='ExifAutoRotate'; hq_scaling='HighQualityScaling'
    video_loop='IsVideoLoop'; video_mute='VideoMuted'; video_volume='VideoVolume'; no_confirmation='NoRequestBeforeFileOperation'
}
$ocrBindings = @{
    ocr_enabled='Enabled'; ocr_auto='AutoMode'; ocr_source='SourceLang'; ocr_quality='OcrModelQuality'
    ocr_mode='OcrPageMode'; ocr_provider='Provider'; ocr_endpoint='Endpoint'; ocr_model='OllamaModel'
    ocr_api='ApiKey'; ocr_target='TargetLang'; ocr_opacity='OverlayOpacity'; ocr_overlay_visible='OverlayVisible'; ocr_disk_cache='DiskCache'
}
$special = @{
    interface_language='Localization / UiLanguage; first-run Windows language; live'
    ui_theme='ThemeService / UiTheme; system; live across applications'
    custom_hotkeys='ModernViewerPreferences.CustomHotkeysJson; empty map; explicit editor commit'
    settings_export_personal='Per-export choice; False; not persisted'
}
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('<!-- settings-catalog:start -->')
$lines.Add('## Current catalog generated from production declarations')
$lines.Add('')
$lines.Add('Regenerate this region with `tools/Export-SettingsInventory.ps1`. It contains no profile values.')
$lines.Add('Every id uses `ModernSettingTitle(id)` and `ModernSettingDescription(id)` for its localized caption/hint;')
$lines.Add('the matching constructor in `Table_Form.ModernLayout.vb` or `Table_Form.ExpandedSettings.vb` owns its editor,')
$lines.Add('numeric range/step, units and stable choice tokens. `Table_Form.Ocr.vb` owns OCR dependencies and busy states;')
$lines.Add('`SyncDynamicPerspectiveEnabled` owns halo dependencies. Those authorities are reused by the actual controls')
$lines.Add('and search, so translation, defaults, ranges and persistence are never copied into another profile model.')
$lines.Add('')
$lines.Add('Modern preference booleans/choices/numbers apply live through their existing handlers. Interface scale')
$lines.Add('requires reopening settings; startup options affect subsequent starts; initial image/video/track choices')
$lines.Add('apply when opening media; disk budgets affect subsequent cache use. OCR text edits commit on blur/close,')
$lines.Add('validating nonempty HTTP(S) endpoints before applying. Invalid pending input survives regrouping and language')
$lines.Add('refresh. Empty endpoint/model retains the existing automatic behavior. Secrets remain masked and DPAPI-protected.')
$lines.Add('Actions are entry points, not preferences: no stored default/range, and opening a dialog does not execute them.')
$lines.Add('')
$lines.Add('| Stable id | Page / group | Persistence / default authority | Values / validation authority |')
$lines.Add('| --- | --- | --- | --- |')
$count = 0
foreach ($group in [regex]::Matches($groups, 'AddSettingsGroup\("(?<page>[^"]+)", "(?<group>[^"]+)", "[^"]+", (?:True|False),(?<rows>[\s\S]*?)\)')) {
    foreach ($row in [regex]::Matches($group.Groups['rows'].Value, '"([^"]+)"')) {
        $id = $row.Groups[1].Value
        $authority = 'Action/information; matching row constructor (no preference default)'
        if ($bindings.ContainsKey($id)) {
            $property = $bindings[$id]
            $decl = [regex]::Match($modern, ('Public Property ' + [regex]::Escape($property) + ' As (?<type>\w+)(?: = (?<default>[^\r\n]+))?'))
            if (-not $decl.Success) { throw "Missing modern property for $id" }
            $default = $decl.Groups['default'].Value
            if (-not $default) { $default = if ($decl.Groups['type'].Value -eq 'Boolean') { 'False' } else { 'type default' } }
            $authority = "ModernViewerPreferences.$property; $($decl.Groups['type'].Value); default $default; Load/Save key $property"
        } elseif ($legacyKeys.ContainsKey($id)) {
            $key = $legacyKeys[$id]
            $decl = [regex]::Match($legacy, ('GetSetting\(App_name, Second_App_Name, "' + [regex]::Escape($key) + '", "(?<default>[^"]*)"\)'))
            if (-not $decl.Success) { throw "Missing legacy key for $id" }
            $authority = "Main_Form.Lifecycle; key $key; storage default '$($decl.Groups['default'].Value)'"
            if ($id -eq 'video_volume') { $authority += '; UI percent 0-100, shared with audio' }
            if ($id -eq 'thumbnail_size') { $authority += '; paired Picture_Box_Height_At_Panel' }
        } elseif ($ocrBindings.ContainsKey($id)) {
            $property = $ocrBindings[$id]
            $decl = [regex]::Match($ocr, ('Public Property ' + [regex]::Escape($property) + ' As (?<type>\w+) = (?<default>[^\r\n]+)'))
            $authority = "OcrTranslateSettings.$property; " + ($decl.Groups['default'].Value -replace "\s+'[^\r\n]*$", '') + '; Load/Save owns key'
        } elseif ($special.ContainsKey($id)) { $authority = $special[$id] }
        $authority = $authority.Replace('|', '\|')
        $values = if ($constraints.ContainsKey($id)) { $constraints[$id] } elseif ($bindings.ContainsKey($id)) { 'Bound property type; matching row constructor / Normalize' } elseif ($legacyKeys.ContainsKey($id)) { 'Original native editor bounds/choices; Lifecycle loader validation' } elseif ($ocrBindings.ContainsKey($id)) { 'OcrTranslateSettings.Load; Table_Form.Ocr editor/dependency rules' } else { 'Matching row constructor' }
        $lines.Add("| ``$id`` | $($group.Groups['page'].Value) / $($group.Groups['group'].Value) | $authority | $values |")
        $count++
    }
}
$lines.Add('')
$lines.Add('Recipients additionally persist the grid slots under `MoveOn1` through `MoveOn9` and `MoveOn0`;')
$lines.Add('DEL is an action. Resources use the shared resource registry and dedicated editors (ticket 118). About')
$lines.Add('contains information/links, the MIT license and the existing explicit support-package dialog.')
$lines.Add('<!-- settings-catalog:end -->')
$path = Join-Path $repo 'docs/guides/SETTINGS_WINDOW_INVENTORY_DOTNET10.md'
$existing = [IO.File]::ReadAllText($path)
$existing = [regex]::Replace($existing, '(?s)\r?\n<!-- settings-catalog:start -->.*?<!-- settings-catalog:end -->\r?\n?', '')
[IO.File]::WriteAllText($path, $existing.TrimEnd() + "`r`n`r`n" + ($lines -join "`r`n") + "`r`n", [Text.UTF8Encoding]::new($false))
Write-Output "Exported $count placed settings/action ids from current sources."
