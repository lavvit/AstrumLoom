<#
.SYNOPSIS
    AstrumLoom を使う新しいゲームプロジェクトのひな型を作ります。

.DESCRIPTION
    csproj・Program.cs・最初のシーンを生成し、AstrumLoom.slnx に登録します。
    生成物はそのままビルドして走り、--selftest も通ります。

.EXAMPLE
    .\tools\newgame.ps1 -Name Hoshiori
    Hoshiori\ を作ってソリューションに追加する。

.EXAMPLE
    .\tools\newgame.ps1 -Name Hoshiori -Width 640 -Height 360 -Backend raylib -Build
    解像度と既定バックエンドを指定して、そのままビルドまでやる。
#>
[CmdletBinding()]
param(
    # プロジェクト名。そのままディレクトリ名・実行ファイル名・名前空間になる。
    [Parameter(Mandatory = $true)]
    [string]$Name,
    # 論理解像度。
    [int]$Width = 1280,
    [int]$Height = 720,
    # 既定のバックエンド。
    [ValidateSet('dxlib', 'raylib')]
    [string]$Backend = 'dxlib',
    # ウィンドウタイトル。省略時は Name。
    [string]$Title = '',
    # 既存のディレクトリを上書きする。
    [switch]$Force,
    # 生成後にビルドする。
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Write-Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Ok($text)   { Write-Host "    $text" -ForegroundColor Green }
function Write-Bad($text)  { Write-Host "    $text" -ForegroundColor Red }

# --- 名前の検査 --------------------------------------------------------------
if ($Name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
    Write-Bad "プロジェクト名は英字またはアンダースコアで始まる英数字にしてください: '$Name'"
    exit 2
}
$reserved = @('Core', 'DXLib', 'RayLib', 'Extend', 'Game', 'tools', 'Debug', 'Release', 'playtest')
if ($reserved -contains $Name) {
    Write-Bad "'$Name' は AstrumLoom 側で使っている名前なので指定できません。"
    exit 2
}
if (-not $Title) { $Title = $Name }

$dir = Join-Path $root $Name
if ((Test-Path $dir) -and -not $Force) {
    Write-Bad "$dir は既にあります。上書きするなら -Force を付けてください。"
    exit 2
}
New-Item -ItemType Directory -Path $dir -Force | Out-Null

# --- 書き出しヘルパ（C# は UTF-8 BOM 付きで保存する） ------------------------
function Save-Utf8Bom([string]$path, [string]$text) {
    $text = $text -replace "`r?`n", "`r`n"
    [System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($true))
    Write-Ok ("生成: " + $path.Substring($root.Length + 1))
}

$backendEnum = if ($Backend -eq 'raylib') { 'RayLib' } else { 'DxLib' }

# --- csproj ------------------------------------------------------------------
$csproj = @'
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>__NAME__</RootNamespace>
    <AssemblyName>__NAME__</AssemblyName>

    <!-- このプロジェクト自身の直下 <プロジェクト>\Debug\ に出す。 -->
    <BaseOutputPath>.\</BaseOutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Debug|AnyCPU'">
    <DebugType>embedded</DebugType>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Release|AnyCPU'">
    <DebugType>embedded</DebugType>
  </PropertyGroup>

  <ItemGroup>
    <!-- GameUtil が Core / DXLib / RayLib / Extend をまとめて連れてくる。 -->
    <ProjectReference Include="..\Game\AstrumLoom.GameUtil.csproj" />
  </ItemGroup>

</Project>
'@
Save-Utf8Bom (Join-Path $dir "$Name.csproj") ($csproj -replace '__NAME__', $Name)

# --- Program.cs --------------------------------------------------------------
$program = @'
using AstrumLoom;

namespace __NAME__;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var config = new GameConfig
        {
            Title = "__TITLE__",
            Width = __WIDTH__,
            Height = __HEIGHT__,
            TargetFps = 60,
            VSync = true,
            GraphicsBackend = GraphicsBackendKind.__BACKEND__,

            // 固定ステップにしておくと、処理落ちしてもゲームの進みが変わらない。
            FixedUpdate = true,
            FixedUpdateHz = 60,
        };

        DefineSelfTest();

        // 引数の解釈もバックエンドの生成も GameApp が引き受ける。
        // --help / --selftest / --shot-every などは何もしなくても使える。
        return GameApp.Run(args, config, () => new PlayScene());
    }

    /// <summary>--selftest で走るテスト計画。壊れたら気づけるように、少しずつ足していく。</summary>
    private static void DefineSelfTest()
    {
        SelfTest.Wait(30);
        SelfTest.Check("シーンが立ち上がっている", () => Scene.NowScene is PlayScene);
        SelfTest.Shot("boot");

        // 右キーを 30 フレーム押して、自機が右に動いたことを確かめる。
        SelfTest.Do("開始位置を覚える", () => PlayScene.TestProbe.Remember());
        SelfTest.Do("右キーを押す", () => VirtualInput.Press(Key.Right));
        SelfTest.Wait(30);
        SelfTest.Do("右キーを離す", () => VirtualInput.Release(Key.Right));
        SelfTest.Check("右に動いた", () => PlayScene.TestProbe.MovedRight());
        SelfTest.Shot("moved");

        SelfTest.Check("致命的エラーが出ていない", () => !AstrumCore.HasFatalError);
    }
}
'@
$program = $program `
    -replace '__NAME__', $Name `
    -replace '__TITLE__', $Title `
    -replace '__WIDTH__', $Width `
    -replace '__HEIGHT__', $Height `
    -replace '__BACKEND__', $backendEnum
Save-Utf8Bom (Join-Path $dir 'Program.cs') $program

# --- PlayScene.cs ------------------------------------------------------------
$scene = @'
using AstrumLoom;

namespace __NAME__;

/// <summary>最初のシーン。ここを書き換えてゲームにしていく。</summary>
internal sealed class PlayScene : Scene
{
    private double _x;
    private double _y;
    private IFont? _font;

    public override void Enable()
    {
        base.Enable();

        // フォントは Windows のシステムフォントを実行時に読む（外部アセット不要）。
        _font = FontHandle.Create(FontHandle.SystemFont, 24, edge: 2);
        if (_font != null) Drawing.DefaultFont = _font;

        _x = AstrumCore.Width / 2.0;
        _y = AstrumCore.Height / 2.0;
        TestProbe.Bind(this);
    }

    public override void Update()
    {
        base.Update();

        if (Key.Esc.Push()) AstrumCore.End();

        // tuning.txt に「player.speed = 400」と書けば、実行中でも即座に効く。
        double speed = Tune.Get("player.speed", 240.0);
        double dt = AstrumCore.DeltaTime;

        double dx = 0, dy = 0;
        if (Key.Left.Hold()) dx -= 1;
        if (Key.Right.Hold()) dx += 1;
        if (Key.Up.Hold()) dy -= 1;
        if (Key.Down.Hold()) dy += 1;

        // 斜めだけ速くならないように正規化する。
        if (dx != 0 && dy != 0)
        {
            const double invSqrt2 = 0.70710678118;
            dx *= invSqrt2;
            dy *= invSqrt2;
        }

        _x = Math.Clamp(_x + dx * speed * dt, 0, AstrumCore.Width);
        _y = Math.Clamp(_y + dy * speed * dt, 0, AstrumCore.Height);
    }

    public override void Draw()
    {
        base.Draw();

        Drawing.Fill(new Color(12, 14, 22));

        double radius = Tune.Get("player.radius", 18.0);
        Drawing.Circle(_x, _y, radius, new Color(120, 200, 255));
        Drawing.Circle(_x, _y, radius, new Color(200, 235, 255), thickness: 2);

        // 画面左上は Log が使うので、HUD は下に置く。
        int line = Drawing.FontSize() + 6;
        double bottom = AstrumCore.Height - 20;
        Drawing.Text(20, bottom - line,
            "矢印キーで移動 / F1 オーバーレイ / F2 スクショ / F4 一時停止 / Esc 終了",
            new Color(150, 165, 190), point: ReferencePoint.BottomLeft);
        Drawing.Text(20, bottom - line * 2, "__TITLE__", Color.White,
            point: ReferencePoint.BottomLeft);
    }

    /// <summary>セルフテストから内部状態を見るための小さな窓。</summary>
    internal static class TestProbe
    {
        private static PlayScene? _scene;
        private static double _rememberedX;

        internal static void Bind(PlayScene scene) => _scene = scene;
        internal static void Remember() => _rememberedX = _scene?._x ?? 0;
        internal static bool MovedRight() => _scene != null && _scene._x > _rememberedX + 1;
    }
}
'@
$scene = $scene -replace '__NAME__', $Name -replace '__TITLE__', $Title
Save-Utf8Bom (Join-Path $dir 'PlayScene.cs') $scene

# --- .slnx に登録 ------------------------------------------------------------
$slnx = Join-Path $root 'AstrumLoom.slnx'
if (Test-Path $slnx) {
    $text = [System.IO.File]::ReadAllText($slnx)
    $entry = "  <Project Path=""$Name/$Name.csproj"" />"
    if ($text -match [regex]::Escape("$Name/$Name.csproj")) {
        Write-Ok "AstrumLoom.slnx には登録済み"
    } else {
        $text = $text -replace '(?m)^\s*</Solution>', "$entry`r`n</Solution>"
        [System.IO.File]::WriteAllText($slnx, $text, [System.Text.UTF8Encoding]::new($false))
        Write-Ok 'AstrumLoom.slnx に登録'
    }
} else {
    Write-Bad "AstrumLoom.slnx が見つからないので登録を飛ばしました。"
}

Write-Host ''
Write-Step "$Name を作りました"
Write-Host @"
    次の一手:
      .\build.ps1 -Project $Name -Run          ビルドして起動
      .\tools\playtest.ps1 -Project $Name -SelfTest   セルフテストを走らせる
      .\tools\playtest.ps1 -Project $Name      10 秒走らせてスクショを集める

    ゲーム本体は $Name\PlayScene.cs から書き始めてください。
"@ -ForegroundColor Gray

if ($Build) {
    & (Join-Path $root 'build.ps1') -Project $Name
    exit $LASTEXITCODE
}
exit 0
