<#
.SYNOPSIS
  HsbgKansousen プラグインをビルドして HDT の Plugins フォルダに配置する。

.DESCRIPTION
  1. HDT (HearthstoneDeckTracker.exe) が起動していたら中断する (起動中は DLL がロックされる)
  2. %LOCALAPPDATA%\HearthstoneDeckTracker\app-* から最新版を検出し、-p:HdtPath で dotnet build に渡す
  3. bin\<Configuration>\HsbgKansousen.dll (+ .pdb) を %APPDATA%\HearthstoneDeckTracker\Plugins\HsbgKansousen\ にコピーする

.EXAMPLE
  .\scripts\deploy-plugin.ps1
  .\scripts\deploy-plugin.ps1 -Configuration Debug
  .\scripts\deploy-plugin.ps1 -HdtPath "$env:LOCALAPPDATA\HearthstoneDeckTracker\app-1.58.3"
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$HdtPath,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'plugin\HsbgKansousen\HsbgKansousen.csproj'
$destDir = Join-Path $env:APPDATA 'HearthstoneDeckTracker\Plugins\HsbgKansousen'

# 1. HDT が起動していないか
$hdt = Get-Process -Name HearthstoneDeckTracker -ErrorAction SilentlyContinue
if ($hdt) {
    Write-Host ''
    Write-Host 'Hearthstone Deck Tracker が起動中です。プラグイン DLL がロックされるため配置できません。' -ForegroundColor Yellow
    Write-Host '  タスクトレイの HDT アイコンを右クリック → Exit で終了してから、もう一度実行してください。' -ForegroundColor Yellow
    Write-Host ''
    exit 1
}

# 2. HDT のインストール先 (最新の app-<version>)
if (-not $HdtPath) {
    $hdtRoot = Join-Path $env:LOCALAPPDATA 'HearthstoneDeckTracker'
    $candidates = Get-ChildItem -Path $hdtRoot -Directory -Filter 'app-*' -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'HearthstoneDeckTracker.exe') } |
        Sort-Object { [version]($_.Name.Substring(4)) } -Descending
    if (-not $candidates) {
        throw "HDT が見つかりません: $hdtRoot\app-*\HearthstoneDeckTracker.exe"
    }
    $HdtPath = $candidates[0].FullName
}
$exe = Join-Path $HdtPath 'HearthstoneDeckTracker.exe'
$hdtVersion = (Get-Item $exe).VersionInfo.ProductVersion
Write-Host "HDT: $HdtPath (v$hdtVersion)"

# 3. ビルド
if (-not $SkipBuild) {
    Write-Host "Building ($Configuration)..."
    & dotnet build $project -c $Configuration -p:HdtPath="$HdtPath" --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build に失敗しました (exit $LASTEXITCODE)"
    }
}

# 4. 配置
$binDir = Join-Path $repoRoot "plugin\HsbgKansousen\bin\$Configuration"
$dll = Join-Path $binDir 'HsbgKansousen.dll'
if (-not (Test-Path $dll)) {
    throw "ビルド成果物が見つかりません: $dll"
}
New-Item -ItemType Directory -Force -Path $destDir | Out-Null
Copy-Item -Path $dll -Destination $destDir -Force
# 0.11.0 で HsbgLogger から改名した。古い DLL が残ると同じゲームを 2 つのプラグインが記録するので取り除く
# （HDT は起動時に app-<version>\Plugins を %APPDATA% の Plugins に合わせるので、ここを消せば向こうも消える）
$legacyDir = Join-Path $env:APPDATA 'HearthstoneDeckTracker\Plugins\HsbgLogger'
if (Test-Path $legacyDir) {
    Remove-Item -Path $legacyDir -Recurse -Force
    Write-Host "改名前のプラグインを取り除きました: $legacyDir" -ForegroundColor Yellow
}
$pdb = Join-Path $binDir 'HsbgKansousen.pdb'
if (Test-Path $pdb) { Copy-Item -Path $pdb -Destination $destDir -Force }

# HDT は起動時に %APPDATA%\...\Plugins を app-<version>\Plugins へ同期するので、ここへ置くだけでよい
Write-Host ''
Write-Host "配置しました: $destDir" -ForegroundColor Green
Get-ChildItem $destDir | ForEach-Object { Write-Host ("  {0}  {1:N0} bytes  {2}" -f $_.Name, $_.Length, $_.LastWriteTime) }
Write-Host ''
Write-Host '次の手順:'
Write-Host '  1. HDT を起動する'
Write-Host '  2. Options > Tracker > Plugins で「HsbgKansousen」にチェックを入れる（改名後の初回は未チェック）'
Write-Host '  3. Battlegrounds を 1 ゲームプレイする'
Write-Host '  4. 設定: Options > Tracker > Plugins の「Settings」（出力先の既定は %USERPROFILE%\Documents\HSBG-Logs、spike ダンプは <出力先>\.spike\）'
Write-Host "     ログ  : $env:APPDATA\HearthstoneDeckTracker\HsbgKansousen\plugin.log"
