<#
.SYNOPSIS
    AstrumLoom をビルドします。

.DESCRIPTION
    ソリューション全体、または指定したプロジェクトだけをビルドします。
    ビルド後にそのまま実行することもできます。

.EXAMPLE
    .\build.ps1
    Debug 構成でソリューション全体をビルドする。

.EXAMPLE
    .\build.ps1 -Run
    ビルドして Sandbox を起動する。

.EXAMPLE
    .\build.ps1 -Project MyGame -Run -RunArgs '--backend raylib'
    MyGame をビルドして Raylib バックエンドで起動する。

.EXAMPLE
    .\build.ps1 -Release -Clean
    いったん掃除してから Release でビルドする。
#>
[CmdletBinding()]
param(
    # ビルドするプロジェクト名（ディレクトリ名）。省略時はソリューション全体。
    [string]$Project,
    # Release 構成でビルドする。
    [switch]$Release,
    # ビルド前に obj/bin を掃除する。
    [switch]$Clean,
    # ビルド後に実行する。
    [switch]$Run,
    # 実行時に渡す引数。($Args は PowerShell の自動変数なので使えない)
    [string]$RunArgs = '',
    # 警告の中身も表示する。
    [switch]$ShowWarnings
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$configuration = if ($Release) { 'Release' } else { 'Debug' }

function Write-Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Ok($text)   { Write-Host "    $text" -ForegroundColor Green }
function Write-Bad($text)  { Write-Host "    $text" -ForegroundColor Red }

# --- ビルド対象を決める ------------------------------------------------------
if ($Project) {
    $dir = Join-Path $root $Project
    if (-not (Test-Path $dir)) {
        Write-Bad "プロジェクト '$Project' が見つかりません: $dir"
        Write-Host "利用可能: $((Get-ChildItem $root -Directory | Where-Object { Get-ChildItem $_.FullName -Filter *.csproj -File } | Select-Object -ExpandProperty Name) -join ', ')"
        exit 2
    }
    $csproj = Get-ChildItem $dir -Filter *.csproj -File | Select-Object -First 1
    if (-not $csproj) { Write-Bad "csproj が見つかりません: $dir"; exit 2 }
    $target = $csproj.FullName
} else {
    $target = Join-Path $root 'AstrumLoom.slnx'
}

# --- 掃除 --------------------------------------------------------------------
if ($Clean) {
    Write-Step '掃除中'
    Get-ChildItem $root -Directory -Recurse -Include obj,bin |
        Where-Object { $_.FullName -notmatch '\\\.git\\' } |
        ForEach-Object {
            Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    Write-Ok '完了'
}

# --- ビルド ------------------------------------------------------------------
Write-Step "ビルド ($configuration): $(Split-Path $target -Leaf)"
$verbosity = if ($ShowWarnings) { 'normal' } else { 'minimal' }
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$output = & dotnet build $target -c $configuration -v $verbosity --nologo 2>&1
$code = $LASTEXITCODE
$sw.Stop()

$errors   = $output | Select-String -Pattern ': error '   -SimpleMatch
$warnings = $output | Select-String -Pattern ': warning ' -SimpleMatch

if ($code -ne 0) {
    $output | ForEach-Object { Write-Host $_ }
    Write-Bad "ビルド失敗 ($($errors.Count) エラー / $([math]::Round($sw.Elapsed.TotalSeconds,1)) 秒)"
    exit 1
}

# 増分ビルドでは再コンパイルされなかったプロジェクトの警告は出てこない。
# 「0 件」を「警告が無い」と誤読しないよう、そこを明示する。
$uniqueWarnings = $warnings | ForEach-Object { $_.Line.Trim() } | Sort-Object -Unique
$warnText = if ($uniqueWarnings.Count -eq 0) {
    '今回コンパイルした範囲に警告なし'
} else {
    "警告 $($uniqueWarnings.Count) 件"
}
Write-Ok "成功  $warnText / $([math]::Round($sw.Elapsed.TotalSeconds,1)) 秒"
if ($ShowWarnings -and $uniqueWarnings.Count -gt 0) {
    $uniqueWarnings | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkYellow }
}

# --- 実行 --------------------------------------------------------------------
if (-not $Run) { exit 0 }

$exeName = if ($Project) { "$Project.exe" } else { 'Sandbox.exe' }

# 新規ゲームは <プロジェクト>\Debug\ に、Sandbox はリポジトリ直下の Debug\ に出る。
# 両方を順に見て、それでも無ければ念のため再帰検索でフォールバックする。
$exe = $null
if ($Project) {
    $projectExe = Join-Path (Join-Path (Join-Path $root $Project) $configuration) $exeName
    if (Test-Path $projectExe) { $exe = $projectExe }
}
if (-not $exe) {
    $rootExe = Join-Path (Join-Path $root $configuration) $exeName
    if (Test-Path $rootExe) { $exe = $rootExe }
}
if (-not $exe) {
    $fallback = Get-ChildItem $root -Recurse -Filter $exeName -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match [regex]::Escape($configuration) } |
        Select-Object -First 1
    if ($fallback) { $exe = $fallback.FullName }
}
if (-not $exe -or -not (Test-Path $exe)) {
    Write-Bad "実行ファイルが見つかりません: $exeName"
    exit 1
}

Write-Step "実行: $exeName $RunArgs"
if ($RunArgs) {
    & $exe ($RunArgs -split '\s+')
} else {
    & $exe
}
exit $LASTEXITCODE
