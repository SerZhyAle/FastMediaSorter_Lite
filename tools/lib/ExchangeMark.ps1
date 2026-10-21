# Read DEVICE-EXCHANGE item CW from the existing UI string table, never a second term list.
function Get-ExchangeMarkTerms([string]$RepoRoot) {
    $source = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src/Localization/Localization.DeviceVocabulary.vb'))
    $row = [regex]::Match($source, '(?m)^\s*Add\("Сервер обмена",[^\r\n]+').Value
    $values = @([regex]::Matches($row, '"((?:[^"]|"")*)"') | ForEach-Object { $_.Groups[1].Value.Replace('""', '"') })
    $codes = @('ru', 'en', 'uk', 'de', 'it', 'es', 'fr', 'pt', 'ar', 'hi', 'bn', 'ur', 'zh')
    if ($values.Count -ne $codes.Count) { throw 'DEVICE-EXCHANGE item CW must provide all 13 locales' }
    $terms = @{}
    for ($i = 0; $i -lt $codes.Count; $i++) { $terms[$codes[$i]] = $values[$i] }
    return $terms
}
