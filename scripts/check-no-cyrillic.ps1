#!/usr/bin/env pwsh
# Fails (exit 1) if any Cyrillic character is found in backend/**/*.cs or plugins/**/*.cs
# (excluding bin/ and obj/ build output). Used to keep C# sources English-only.

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$pattern = '[\u0400-\u04FF\u0500-\u052F\u2DE0-\u2DFF\uA640-\uA69F]'

$roots = @(
    (Join-Path $repoRoot 'backend'),
    (Join-Path $repoRoot 'plugins')
)

$files = foreach ($root in $roots) {
    if (Test-Path $root) {
        Get-ChildItem -Path $root -Recurse -Filter '*.cs' -File |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    }
}

$offenders = @()
foreach ($file in $files) {
    $lineNo = 0
    foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
        $lineNo++
        if ($line -cmatch $pattern) {
            $offenders += [PSCustomObject]@{
                File = $file.FullName.Substring($repoRoot.Length + 1)
                Line = $lineNo
                Text = $line.Trim()
            }
        }
    }
}

if ($offenders.Count -gt 0) {
    Write-Host "Cyrillic characters found in C# sources:" -ForegroundColor Red
    foreach ($o in $offenders) {
        Write-Host ("  {0}:{1}: {2}" -f $o.File, $o.Line, $o.Text)
    }
    Write-Host ""
    Write-Host ("Total: {0} line(s) across {1} file(s)." -f $offenders.Count, ($offenders | Select-Object -ExpandProperty File -Unique).Count) -ForegroundColor Red
    exit 1
}

Write-Host "OK: no Cyrillic characters found in backend/**/*.cs or plugins/**/*.cs" -ForegroundColor Green
exit 0
