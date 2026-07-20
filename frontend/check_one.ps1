param($Path)
$bytes = [System.IO.File]::ReadAllBytes($Path)
$text = [System.Text.Encoding]::UTF8.GetString($bytes)
if ($text -match "[\u0400-\u04FF]") {
    Write-Output "HAS CYRILLIC"
    $lines = $text -split "`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match "[\u0400-\u04FF]") {
            Write-Output ("Line " + ($i+1) + ": " + $lines[$i])
        }
    }
} else {
    Write-Output "NO CYRILLIC"
}
