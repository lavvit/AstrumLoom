<#
.SYNOPSIS
    実機でゲームを走らせて、スクリーンショットとログを playtest\<日時>\ に集めます。

.DESCRIPTION
    ビルドしてから自動化フラグ付きで起動し、終了後に成果物をまとめて 1 か所に置きます。
    ハングしても必ず戻ってくるように、タイムアウトで強制終了します。

.EXAMPLE
    .\tools\playtest.ps1
    Sandbox を 10 秒ぶん走らせ、1 秒ごとにスクショを撮る。

.EXAMPLE
    .\tools\playtest.ps1 -SelfTest
    セルフテストを走らせて PASS/FAIL を判定する。失敗すると終了コード 1。

.EXAMPLE
    .\tools\playtest.ps1 -Project MyGame -Backend raylib -Seconds 30

.EXAMPLE
    .\tools\playtest.ps1 -Record run1.txt      # 手で遊んで記録
    .\tools\playtest.ps1 -Replay run1.txt      # まったく同じ入力で再生
#>
[CmdletBinding()]
param(
    # 対象プロジェクト名。既定は Sandbox。
    [string]$Project = 'Sandbox',
    # dxlib / raylib。省略時はゲームの既定値。
    [ValidateSet('dxlib', 'raylib', '')]
    [string]$Backend = '',
    # セルフテストを走らせる。
    [switch]$SelfTest,
    # 走らせる秒数（SelfTest 時は無視）。
    [double]$Seconds = 10,
    # スクショの間隔（論理フレーム）。0 で撮らない。
    [int]$ShotEvery = 60,
    # 入力を記録するファイル名。
    [string]$Record = '',
    # 入力を再生するファイル名。
    [string]$Replay = '',
    # ビルドを省略する。
    [switch]$NoBuild,
    # Release 構成で動かす。
    [switch]$Release,
    # 追加の引数。
    [string]$Extra = '',
    # 強制終了までの余裕（秒）。
    [double]$TimeoutMargin = 60
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$configuration = if ($Release) { 'Release' } else { 'Debug' }

function Write-Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Ok($text)   { Write-Host "    $text" -ForegroundColor Green }
function Write-Bad($text)  { Write-Host "    $text" -ForegroundColor Red }
function Write-Info($text) { Write-Host "    $text" -ForegroundColor Gray }

# --- ビルド ------------------------------------------------------------------
if (-not $NoBuild) {
    # 名前付きで渡すにはハッシュテーブルでスプラットする（配列だと位置引数になる）。
    $buildParams = @{ Project = $Project }
    if ($Release) { $buildParams['Release'] = $true }
    & (Join-Path $root 'build.ps1') @buildParams
    if ($LASTEXITCODE -ne 0) { Write-Bad 'ビルドに失敗したので中止します。'; exit 1 }
}

# --- 実行ファイルを探す ------------------------------------------------------
$exeName = "$Project.exe"

# 新規ゲームは <プロジェクト>\Debug\ に、Sandbox はリポジトリ直下の Debug\ に出る。
# 両方を順に見て、それでも無ければ念のため再帰検索でフォールバックする。
$exe = $null
$projectExe = Join-Path (Join-Path (Join-Path $root $Project) $configuration) $exeName
if (Test-Path $projectExe) { $exe = $projectExe }
if (-not $exe) {
    $rootExe = Join-Path (Join-Path $root $configuration) $exeName
    if (Test-Path $rootExe) { $exe = $rootExe }
}
if (-not $exe) {
    $found = Get-ChildItem $root -Recurse -Filter $exeName -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match [regex]::Escape($configuration) } |
        Select-Object -First 1
    if ($found) { $exe = $found.FullName }
}
if (-not $exe -or -not (Test-Path $exe)) {
    Write-Bad "実行ファイルが見つかりません: $exeName"
    exit 1
}
$workDir = Split-Path $exe -Parent

# --- 収集先 ------------------------------------------------------------------
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$collect = Join-Path (Join-Path $root 'playtest') $stamp
New-Item -ItemType Directory -Path $collect -Force | Out-Null

# --- 引数を組み立てる --------------------------------------------------------
$runArgs = @('--out', $collect)
if ($Backend) { $runArgs += @('--backend', $Backend) }

if ($SelfTest) {
    $runArgs += '--selftest'
    $limitSeconds = 300
} else {
    if ($ShotEvery -gt 0) { $runArgs += @('--shot-every', "$ShotEvery") }
    $runArgs += @('--quit-after-sec', $Seconds.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    $limitSeconds = $Seconds
}
if ($Record) { $runArgs += @('--record', (Join-Path $collect $Record)) }
if ($Replay) {
    # 過去の playtest からも探す
    $replayPath = $Replay
    if (-not (Test-Path $replayPath)) {
        $hit = Get-ChildItem (Join-Path $root 'playtest') -Recurse -Filter (Split-Path $Replay -Leaf) -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($hit) { $replayPath = $hit.FullName }
    }
    if (-not (Test-Path $replayPath)) { Write-Bad "再生ファイルが見つかりません: $Replay"; exit 1 }
    $replayPath = (Resolve-Path $replayPath).Path
    $runArgs += @('--replay', $replayPath)
    Write-Info "再生元: $replayPath"
}
if ($Extra) { $runArgs += ($Extra -split '\s+' | Where-Object { $_ }) }

Write-Step "実行: $exeName $($runArgs -join ' ')"

# --- 起動（タイムアウト付き） ------------------------------------------------
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $exe
$psi.WorkingDirectory = $workDir
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
$psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8

# Windows PowerShell 5.1 は .NET Framework 上で動くので ArgumentList が無い。
# 空白を含む引数（収集先のパスなど）を自分で括る。
function Format-Arg([string]$value) {
    if ($value -match '[\s"]') { '"' + ($value -replace '"', '\"') + '"' } else { $value }
}
$psi.Arguments = (($runArgs | ForEach-Object { Format-Arg ([string]$_) }) -join ' ')

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$proc = [System.Diagnostics.Process]::Start($psi)
# 先に非同期で読み始めないと、パイプが詰まって相互待ちになる。
$outTask = $proc.StandardOutput.ReadToEndAsync()
$errTask = $proc.StandardError.ReadToEndAsync()

$timeoutMs = [int](($limitSeconds + $TimeoutMargin) * 1000)
$timedOut = $false
if (-not $proc.WaitForExit($timeoutMs)) {
    $timedOut = $true
    Write-Bad "タイムアウト ($([math]::Round($timeoutMs/1000,1)) 秒) — 強制終了します"
    try { $proc.Kill($true) } catch { }
    [void]$proc.WaitForExit(5000)
}
$sw.Stop()

$stdout = $outTask.Result
$stderr = $errTask.Result
$exitCode = if ($timedOut) { 124 } else { $proc.ExitCode }

# --- 成果物をまとめる --------------------------------------------------------
$stdout | Set-Content (Join-Path $collect 'stdout.txt') -Encoding UTF8
if ($stderr) { $stderr | Set-Content (Join-Path $collect 'stderr.txt') -Encoding UTF8 }

$meta = @(
    "実行日時   : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    "実行ファイル: $exe"
    "引数       : $($runArgs -join ' ')"
    "構成       : $configuration"
    "終了コード : $exitCode$(if ($timedOut) { ' (タイムアウト)' })"
    "所要時間   : $([math]::Round($sw.Elapsed.TotalSeconds,2)) 秒"
)
$meta | Set-Content (Join-Path $collect 'run-info.txt') -Encoding UTF8

# --- 報告 --------------------------------------------------------------------
Write-Host ''
if ($stdout) { $stdout.TrimEnd() -split "`r?`n" | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray } }
if ($stderr) { $stderr.TrimEnd() -split "`r?`n" | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkYellow } }
Write-Host ''

$shots = Get-ChildItem $collect -Filter 'shot_*.png' -File -ErrorAction SilentlyContinue
Write-Step "収集先: $collect"
Write-Info "スクショ $($shots.Count) 枚 / 所要 $([math]::Round($sw.Elapsed.TotalSeconds,1)) 秒"
Get-ChildItem $collect -File | Sort-Object Name | ForEach-Object {
    Write-Info ("  {0,-30} {1,10:N0} B" -f $_.Name, $_.Length)
}

if ($exitCode -eq 0) {
    Write-Ok "終了コード 0"
} else {
    Write-Bad "終了コード $exitCode"
}
exit $exitCode
