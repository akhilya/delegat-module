$files = Get-ChildItem -Path src -Recurse -File -Include *.vue,*.js,*.ts | Where-Object { $_.FullName -notmatch 'locales' }
$found = @()
foreach ($f in $files) {
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $content = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ($content -match "[\u0400-\u04FF]") {
        $found += $f.FullName
    }
}
Write-Output ("COUNT=" + $found.Count)
$found | Out-File -Encoding utf8 cyrillic_files.txt
