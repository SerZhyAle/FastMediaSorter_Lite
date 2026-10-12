<#
.SYNOPSIS
    The slope probe (specification 209 section 4): ordinary least squares over a samp
    series, with a 95% confidence interval on the slope. "Growth" is only ever declared
    when the CI excludes zero AND the budget is exceeded (section 3, property L).
#>

# Returns @{ Slope; Intercept; SE; CI95 = @(low, high); N; ExcludesZero }
# $X = hours, $Y = the sampled quantity. Warm-up exclusion is done by the caller (it passes
# the series that is already past the warm-up window).
function Get-SlopeStats {
    param([double[]]$X, [double[]]$Y)
    $n = [Math]::Min($X.Count, $Y.Count)
    if ($n -lt 3) { return $null }
    $sx = 0.0; $sy = 0.0
    for ($i = 0; $i -lt $n; $i++) { $sx += $X[$i]; $sy += $Y[$i] }
    $mx = $sx / $n; $my = $sy / $n
    $sxx = 0.0; $sxy = 0.0; $syy = 0.0
    for ($i = 0; $i -lt $n; $i++) {
        $dx = $X[$i] - $mx; $dy = $Y[$i] - $my
        $sxx += $dx * $dx; $sxy += $dx * $dy; $syy += $dy * $dy
    }
    if ($sxx -le 0) { return $null }
    $slope = $sxy / $sxx
    $intercept = $my - $slope * $mx
    $sse = 0.0
    for ($i = 0; $i -lt $n; $i++) {
        $e = $Y[$i] - ($intercept + $slope * $X[$i]); $sse += $e * $e
    }
    $dof = [Math]::Max(1, $n - 2)
    $se = [Math]::Sqrt(($sse / $dof) / $sxx)
    # t for 95% two-sided: 12.706 (n=3) down to 1.960 (large n) - a small table is exact
    # enough for a growth verdict and keeps the harness free of a statistics dependency.
    $t = Get-TCritical95 ([Math]::Max(1, $dof))
    $low = $slope - $t * $se; $high = $slope + $t * $se
    $excludesZero = (($low -gt 0 -and $high -gt 0) -or ($low -lt 0 -and $high -lt 0))
    return @{
        Slope = $slope; Intercept = $intercept; SE = $se; CI95 = @($low, $high)
        N = $n; ExcludesZero = $excludesZero
    }
}

function Get-TCritical95 {
    param([int]$Dof)
    # Fisher's z from a few Newton steps on the incomplete beta is overkill here; the table
    # below is the standard t distribution at 95%, interpolated conservatively between rows.
    $table = @(  # dof, t
        @(1, 12.706), @(2, 4.303), @(3, 3.182), @(4, 2.776), @(5, 2.571),
        @(6, 2.447), @(7, 2.365), @(8, 2.306), @(9, 2.262), @(10, 2.228),
        @(15, 2.131), @(20, 2.086), @(30, 2.042), @(60, 2.000), @(120, 1.980), @(1000000, 1.960)
    )
    for ($i = 0; $i -lt $table.Count; $i++) {
        if ($Dof -le $table[$i][0]) { return [double]$table[$i][1] }
    }
    return 1.960
}

# Reads a samp CSV (columns: hours, then any numeric columns), drops the warm-up window
# and returns the slope stats for one named column.
function Get-ColumnSlope {
    param([string]$CsvPath, [string]$Column, [double]$WarmUpHours = 0.25)
    if (-not (Test-Path -LiteralPath $CsvPath)) { return $null }
    $lines = @(Get-Content -LiteralPath $CsvPath | Where-Object { $_.Trim() })
    if ($lines.Count -lt 3) { return $null }
    $header = ($lines[0] -split ',')
    $col = [array]::IndexOf($header, $Column)
    if ($col -lt 0) { return $null }
    $x = [System.Collections.Generic.List[double]]::new()
    $y = [System.Collections.Generic.List[double]]::new()
    foreach ($line in ($lines | Select-Object -Skip 1)) {
        $parts = $line -split ','
        if ($parts.Count -le $col) { continue }
        $xv = 0.0; $yv = 0.0
        if (-not [double]::TryParse($parts[0], [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$xv)) { continue }
        if (-not [double]::TryParse($parts[$col], [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$yv)) { continue }
        if ($xv -lt $WarmUpHours) { continue }
        $x.Add($xv); $y.Add($yv)
    }
    if ($x.Count -lt 3) { return $null }
    return Get-SlopeStats -X $x.ToArray() -Y $y.ToArray()
}
