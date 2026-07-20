param($ListFile, $OutFile)
$files = Get-Content $ListFile
$sb = New-Object System.Text.StringBuilder
foreach ($path in $files) {
    if ([string]::IsNullOrWhiteSpace($path)) { continue }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $content = [System.Text.Encoding]::UTF8.GetString($bytes)
    $lines = $content -split "`n"
    [void]$sb.AppendLine("=== $path ===")
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match "[\u0400-\u04FF]") {
            [void]$sb.AppendLine((($i+1).ToString() + ": " + $lines[$i].Trim()))
        }
    }
}
[System.IO.File]::WriteAllText($OutFile, $sb.ToString(), [System.Text.Encoding]::UTF8)
