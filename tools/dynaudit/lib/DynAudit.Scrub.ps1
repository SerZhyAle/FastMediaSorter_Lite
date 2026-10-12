<#
.SYNOPSIS
    Redaction on capture, not later (specification 209 section 12 item 2): every artifact
    line the harness writes goes through here, so no run folder ever holds a real profile
    path, the machine name, or a secret.
#>

# Replaces the current user's profile prefixes and account name with <USER>, the machine
# name with <HOST>, and anything shaped like a credential with <hidden>. Applied to every
# string that lands in an artifact - CSV fields, JSON payloads, transcripts, log copies.
function Get-ScrubbedText {
    param([string]$Text)
    if ($null -eq $Text) { return '' }
    $t = $Text
    foreach ($pair in @(
            @($env:USERPROFILE, '<USER>'),
            @($env:LOCALAPPDATA, '<USER>'),
            @($env:APPDATA,     '<USER>'),
            @($env:TEMP,        '<USER>\Temp'),
            @($env:TMP,         '<USER>\Temp')
        )) {
        if ($pair[0]) { $t = $t.Replace($pair[0], $pair[1]) }
    }
    if ($env:USERNAME) {
        # The bare account name can survive inside a path that was not matched by a prefix.
        $t = $t -replace [regex]::Escape($env:USERNAME), '<USER>'
    }
    if ($env:COMPUTERNAME) { $t = $t.Replace($env:COMPUTERNAME, '<HOST>') }
    # The LogPackage allow-list discipline: a line that names a secret is redacted here and
    # counted as an H12 failure by the err probe when it comes out of the product.
    $t = $t -replace '(?i)(password|passwd|token|apikey|api_key|secret)(\s*[=:]?\s*)\S+', '$1$2<hidden>'
    return $t
}

function Write-ScrubbedLine {
    param([string]$Path, [string]$Line)
    [IO.File]::AppendAllText($Path, (Get-ScrubbedText $Line) + "`r`n")
}

function Write-ScrubbedLines {
    param([string]$Path, [string[]]$Lines)
    foreach ($l in $Lines) { Write-ScrubbedLine $Path $l }
}

# Writes a CSV artifact whose one text column (the first) is scrubbed; numeric columns pass
# through unchanged. Everything the harness records goes to disk through one of these.
function Write-ScrubbedCsv {
    param([string]$Path, [string[]]$Header, [object[][]]$Rows)
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.Append((($Header | ForEach-Object { $_ }) -join ','))
    foreach ($row in $Rows) {
        $cells = for ($i = 0; $i -lt $row.Count; $i++) {
            $v = "$($row[$i])"
            if ($i -eq 0) { $v = Get-ScrubbedText $v }
            if ($v -match '[,"]') { '"' + ($v.Replace('"', '""')) + '"' } else { $v }
        }
        [void]$sb.Append("`r`n" + ($cells -join ','))
    }
    [IO.File]::WriteAllText($Path, (Get-ScrubbedText $sb.ToString()))
}

function Write-ScrubbedJson {
    param([string]$Path, $Object)
    $json = $Object | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($Path, (Get-ScrubbedText $json))
}
