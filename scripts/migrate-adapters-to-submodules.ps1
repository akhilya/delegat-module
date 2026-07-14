#Requires -Version 7.0
$ErrorActionPreference = "Stop"

$ProjectsRoot = "C:\Users\ARTEM\projects"
$DashboardRoot = Join-Path $ProjectsRoot "dashboard"
$SdkSource = Join-Path $DashboardRoot "sdk\Delegat.Marketplace.SDK"
$GithubUser = "akhilya"

$SdkRepoPath = Join-Path $ProjectsRoot "delegat-marketplace-sdk"
$SdkRepoName = "delegat-marketplace-sdk"

$Adapters = @(
    @{
        FolderName = "Delegat.Marketplace.Ozon"
        RepoName   = "delegat-adapter-ozon"
        Version    = "1.1.4"
        Key        = "ozon"
    },
    @{
        FolderName = "Delegat.Marketplace.Wildberries"
        RepoName   = "delegat-adapter-wildberries"
        Version    = "1.7.5"
        Key        = "wildberries"
    },
    @{
        FolderName = "Delegat.Marketplace.OpenCart"
        RepoName   = "delegat-adapter-opencart"
        Version    = "1.0.0"
        Key        = "opencart"
    },
    @{
        FolderName = "Delegat.Marketplace.YandexMarket"
        RepoName   = "delegat-adapter-yandex-market"
        Version    = "1.0.0"
        Key        = "yandex"
    }
)

$DotNetGitIgnore = @"
bin/
obj/
.vs/
*.user
*.suo
.DS_Store
"@

$SdkCiYml = @"
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          token: `${{ secrets.SUBMODULE_PAT || github.token }}

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      - name: Build SDK
        run: dotnet build Delegat.Marketplace.SDK/Delegat.Marketplace.SDK.csproj -c Release
"@

$AdapterCiYml = @"
name: CI

on:
  push:
    branches: [main]
    tags: ['v*']
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          submodules: recursive
          token: `${{ secrets.SUBMODULE_PAT || github.token }}

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      - name: Build adapter
        run: dotnet build -c Release

      - name: Publish DLL on release tag
        if: startsWith(github.ref, 'refs/tags/v')
        run: |
          dotnet publish -c Release -o ./out
          cp ./out/*.dll ./`${{ github.event.repository.name }}.dll || cp ./out/Delegat.*.dll ./

      - uses: softprops/action-gh-release@v2
        if: startsWith(github.ref, 'refs/tags/v')
        with:
          files: ./out/*.dll
"@

function Copy-ProjectSources {
    param(
        [string]$Source,
        [string]$Destination
    )
    if (Test-Path $Destination) {
        Remove-Item $Destination -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    robocopy $Source $Destination /E /XD bin obj .vs /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
}

function Ensure-GitRepo {
    param([string]$Path)
    if (-not (Test-Path (Join-Path $Path ".git"))) {
        git -C $Path init -b main
    }
}

function Ensure-GithubRepo {
    param(
        [string]$RepoName,
        [string]$Description
    )
    $fullName = "$GithubUser/$RepoName"
    gh repo view $fullName 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Creating GitHub repo $fullName ..."
        gh repo create $fullName --private --description $Description
    }
}

function Fix-AdapterCsproj {
    param([string]$RepoPath)
    $csproj = Get-ChildItem -Path $RepoPath -Filter "*.csproj" | Select-Object -First 1
    if (-not $csproj) { throw "No csproj in $RepoPath" }
    $content = Get-Content $csproj.FullName -Raw
    $content = $content -replace '\.\.\\\.\.\\sdk\\Delegat\.Marketplace\.SDK\\Delegat\.Marketplace\.SDK\.csproj',
        'sdk\Delegat.Marketplace.SDK\Delegat.Marketplace.SDK.csproj'
    Set-Content -Path $csproj.FullName -Value $content -NoNewline
}

Write-Host "=== 1. SDK repository ==="
Copy-ProjectSources -Source $SdkSource -Destination (Join-Path $SdkRepoPath "Delegat.Marketplace.SDK")
Set-Content (Join-Path $SdkRepoPath "VERSION") -Value "1.0.0"
Set-Content (Join-Path $SdkRepoPath ".gitignore") -Value $DotNetGitIgnore
Set-Content (Join-Path $SdkRepoPath "README.md") -Value @"
# Delegat Marketplace SDK

SDK for building Delegat marketplace adapter plugins.

## Build

``````bash
dotnet build Delegat.Marketplace.SDK/Delegat.Marketplace.SDK.csproj -c Release
``````
"@
New-Item -ItemType Directory -Force -Path (Join-Path $SdkRepoPath ".github\workflows") | Out-Null
Set-Content (Join-Path $SdkRepoPath ".github\workflows\ci.yml") -Value $SdkCiYml

Ensure-GitRepo -Path $SdkRepoPath
Ensure-GithubRepo -RepoName $SdkRepoName -Description "Delegat Marketplace SDK"
git -C $SdkRepoPath add -A
git -C $SdkRepoPath diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    git -C $SdkRepoPath commit -m "Initial SDK repository with PluginStability support"
}
git -C $SdkRepoPath remote remove origin 2>$null
git -C $SdkRepoPath remote add origin "git@github.com:$GithubUser/$SdkRepoName.git"
git -C $SdkRepoPath push -u origin main --force

Write-Host "=== 2. Adapter repositories ==="
foreach ($adapter in $Adapters) {
    $source = Join-Path $DashboardRoot "plugins\$($adapter.FolderName)"
    $repoPath = Join-Path $ProjectsRoot $adapter.RepoName
    Write-Host "Migrating $($adapter.RepoName) ..."

    Copy-ProjectSources -Source $source -Destination $repoPath
    Fix-AdapterCsproj -RepoPath $repoPath

    Set-Content (Join-Path $repoPath "VERSION") -Value $adapter.Version
    Set-Content (Join-Path $repoPath "adapter.json") -Value (@{
        key        = $adapter.Key
        version    = $adapter.Version
        stability  = "Experimental"
        folderName = $adapter.FolderName
    } | ConvertTo-Json -Depth 3)
    Set-Content (Join-Path $repoPath ".gitignore") -Value $DotNetGitIgnore
    Set-Content (Join-Path $repoPath "README.md") -Value @"
# $($adapter.FolderName)

Delegat marketplace adapter ($($adapter.Key)).

## Setup

``````bash
git clone --recurse-submodules git@github.com:$GithubUser/$($adapter.RepoName).git
dotnet build -c Release
``````

## Version

See ``VERSION`` and ``adapter.json``.
"@
    New-Item -ItemType Directory -Force -Path (Join-Path $repoPath ".github\workflows") | Out-Null
    Set-Content (Join-Path $repoPath ".github\workflows\ci.yml") -Value $AdapterCiYml

    Ensure-GitRepo -Path $repoPath
    if (-not (Test-Path (Join-Path $repoPath "sdk\.git"))) {
        git -C $repoPath submodule add "git@github.com:$GithubUser/$SdkRepoName.git" sdk
    }
    Ensure-GithubRepo -RepoName $adapter.RepoName -Description "Delegat adapter: $($adapter.Key)"
    git -C $repoPath add -A
    git -C $repoPath diff --cached --quiet
    if ($LASTEXITCODE -ne 0) {
        git -C $repoPath commit -m "Initial adapter repository ($($adapter.Version), Experimental)"
    }
    git -C $repoPath remote remove origin 2>$null
    git -C $repoPath remote add origin "git@github.com:$GithubUser/$($adapter.RepoName).git"
    git -C $repoPath push -u origin main --force
    git -C $repoPath tag "v$($adapter.Version)" -f
    git -C $repoPath push origin "v$($adapter.Version)" --force
}

Write-Host "=== Done. Adapter repos created under $ProjectsRoot ==="