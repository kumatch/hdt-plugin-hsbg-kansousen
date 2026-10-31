<#
.SYNOPSIS
  HsbgKansousen プラグインの配布用 zip を作る。

.DESCRIPTION
  Release ビルドの HsbgKansousen.dll だけを HsbgKansousen\HsbgKansousen.dll の構造で zip にする（HDT 同梱 DLL は入れない）。
  HDT の Options > Tracker > Plugins に zip をドロップすると Plugins\HsbgKansousen\HsbgKansousen.dll に展開される。
  出力: dist\HsbgKansousen-<version>.zip

.EXAMPLE
  .\scripts\package-plugin.ps1
  .\scripts\package-plugin.ps1 -SkipBuild
#>
[CmdletBinding()]
param(
    [string]$HdtPath,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'plugin\HsbgKansousen\HsbgKansousen.csproj'
$dll = Join-Path $repoRoot 'plugin\HsbgKansousen\bin\Release\HsbgKansousen.dll'
$distDir = Join-Path $repoRoot 'dist'

if (-not $SkipBuild) {
    if (-not $HdtPath) {
        $hdtRoot = Join-Path $env:LOCALAPPDATA 'HearthstoneDeckTracker'
        $candidates = Get-ChildItem -Path $hdtRoot -Directory -Filter 'app-*' -ErrorAction SilentlyContinue |
            Where-Object { Test-Path (Join-Path $_.FullName 'HearthstoneDeckTracker.exe') } |
            Sort-Object { [version]($_.Name.Substring(4)) } -Descending
        if (-not $candidates) { throw "HDT が見つかりません: $hdtRoot\app-*" }
        $HdtPath = $candidates[0].FullName
    }
    Write-Host "Building Release against $HdtPath ..."
    & dotnet build $project -c Release -p:HdtPath="$HdtPath" --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build に失敗しました (exit $LASTEXITCODE)" }
}

if (-not (Test-Path $dll)) { throw "ビルド成果物が見つかりません: $dll" }
$version = (Get-Item $dll).VersionInfo.ProductVersion
if (-not $version) { $version = (Get-Item $dll).VersionInfo.FileVersion }
$version = ($version -split '\+')[0] -replace '[^0-9A-Za-z\.\-]', ''

# DLL に手元の絶対パス（ユーザー名を含む）が残っていないか（Directory.Build.props の PathMap が効いているか）
$dllBytes = [System.IO.File]::ReadAllBytes($dll)
$dllTexts = @(
    [System.Text.Encoding]::ASCII.GetString($dllBytes),
    [System.Text.Encoding]::Unicode.GetString($dllBytes, 0, $dllBytes.Length),
    [System.Text.Encoding]::Unicode.GetString($dllBytes, 1, $dllBytes.Length - 1)
)
foreach ($needle in @($repoRoot, $env:USERPROFILE) | Where-Object { $_ }) {
    foreach ($text in $dllTexts) {
        if ($text.IndexOf($needle, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "DLL に手元のパスが入っています: $needle（PathMap を確認し、ビルドし直してください）"
        }
    }
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$zip = Join-Path $distDir "HsbgKansousen-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

# Compress-Archive は Windows PowerShell 5.1 だとエントリ名の区切りが '\' になるので、ZipArchive で直接組む（区切りは '/'）
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $archive, $dll, 'HsbgKansousen/HsbgKansousen.dll', [System.IO.Compression.CompressionLevel]::Optimal)
}
finally {
    $archive.Dispose()
}

Write-Host ''
Write-Host "作成しました: $zip" -ForegroundColor Green
Write-Host '中身:'
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $archive.Entries | ForEach-Object { Write-Host ("  {0}  {1:N0} bytes" -f $_.FullName, $_.Length) }
}
finally {
    $archive.Dispose()
}
Write-Host ''
Write-Host 'インストール: HDT の Options > Tracker > Plugins に、この zip をドラッグ&ドロップする'
