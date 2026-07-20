param([string[]]$Paths)
foreach ($p in $Paths) {
    Write-Output ("=== " + $p + " ===")
    $bytes = [System.IO.File]::ReadAllBytes($p)
    $content = [System.Text.Encoding]::UTF8.GetString($bytes)
    $lines = $content -split "`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match "[\u0400-\u04FF]") {
            Write-Output (($i+1).ToString() + ": " + $lines[$i].Trim())
        }
    }
}
