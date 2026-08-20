# ゲームを作りはじめる

AstrumLoom を使ったゲームは、`tools\newgame.ps1` で雛形を作るところから始めます。
`Program.cs` をゼロから書く必要はありません。

```powershell
.\tools\newgame.ps1 -Name Hoshiori
```

これで次のものが揃います。

```
Hoshiori\
  Hoshiori.csproj    AstrumLoom.GameUtil を参照するだけ
  Program.cs         GameApp.Run を 1 回呼ぶ + セルフテスト計画
  PlayScene.cs       矢印キーで丸が動く、そのまま走るシーン
```

`AstrumLoom.slnx` への登録も済んでいます。あとはビルドして走らせるだけです。

```powershell
.\build.ps1 -Project Hoshiori -Run
```

主なオプション:

| オプション | 意味 |
| --- | --- |
| `-Width` / `-Height` | 論理解像度（既定 1280x720） |
| `-Backend dxlib\|raylib` | 既定のバックエンド |
| `-Title` | ウィンドウタイトル（日本語可） |
| `-Build` | 生成してそのままビルドする |
| `-Force` | 既存ディレクトリを上書きする |

---

## ゲーム側のコードはこれだけ

雛形の `Program.cs` は実質これだけです。

```csharp
[STAThread]
private static int Main(string[] args)
{
    var config = new GameConfig
    {
        Title = "Hoshiori",
        Width = 1280,
        Height = 720,
        FixedUpdate = true,     // 処理落ちしてもゲームの進みが変わらない
        FixedUpdateHz = 60,
    };

    return GameApp.Run(args, config, () => new PlayScene());
}
```

`GameApp.Run` が引き受けるもの:

- コマンドライン引数の解釈（`--backend` / `--selftest` / `--shot-every` …）
- バックエンドの生成（**ゲーム側に `DxLibPlatform` / `RayLibPlatform` の名前が出てこない**）
- ウィンドウが出る前に落ちたときのコンソールへの出力
- 終了コードの決定（セルフテスト失敗なら 1）

バックエンドの切り替えは `GameConfig.GraphicsBackend` か、実行時の `--backend raylib` です。
ゲームのコードは一行も変わりません。

---

## ビルド

```powershell
.\build.ps1                          # ソリューション全体を Debug で
.\build.ps1 -Project Hoshiori        # 1 プロジェクトだけ
.\build.ps1 -Project Hoshiori -Run   # ビルドして起動
.\build.ps1 -Release -Clean          # 掃除してから Release
.\build.ps1 -ShowWarnings            # 警告の中身も表示
```

実行ファイルは `<プロジェクト名>\Debug\` または `<プロジェクト名>\Release\` に出ます。
`Sandbox` だけは特別扱いで、リポジトリ直下の `Debug\` / `Release\` に出ます。

`build.bat` は同じものを cmd から呼ぶだけのラッパーです。

---

## 実機で走らせて証跡を残す

`tools\playtest.ps1` は、ビルド → 実行 → 成果物の収集までをやります。
集めたものは `playtest\<日時>\` に入ります。

```powershell
.\tools\playtest.ps1 -Project Hoshiori                 # 10 秒走らせて 1 秒ごとにスクショ
.\tools\playtest.ps1 -Project Hoshiori -SelfTest       # セルフテストを走らせる
.\tools\playtest.ps1 -Project Hoshiori -Seconds 30 -ShotEvery 30
.\tools\playtest.ps1 -Project Hoshiori -Backend raylib # バックエンドを変えて同じことをする
```

収集されるもの:

```
playtest\20260813_125342\
  shot_000020_auto.png    スクリーンショット（ファイル名の数字は論理フレーム番号）
  stdout.txt              標準出力
  run.log                 ゲーム内ログ
  selftest.log            セルフテストの結果（--SelfTest 時）
  run-info.txt            引数・終了コード・所要時間
```

ゲームがハングしても `playtest.ps1` は必ず戻ってきます（タイムアウトで強制終了し、終了コード 124 を返します）。

---

## バグを完全に再現する

入力を記録しておくと、まったく同じ操作をあとから何度でも再生できます。

```powershell
.\tools\playtest.ps1 -Project Hoshiori -Record run1.txt   # 手で遊んで記録
.\tools\playtest.ps1 -Project Hoshiori -Replay run1.txt   # 同じ入力で再生
```

記録中と再生中は、再現性のために自動で

- 単一スレッド更新
- 固定ステップ更新（1 ループ 1 論理フレーム、実時間を見ない）

に切り替わります。この 2 つが揃っているので、**再生したときの画面は記録したときと 1 ドットも変わりません**。

記録ファイルは読める形式です。変化のあったフレームだけが並びます。

```
# AstrumLoom input recording
v 1
hz 60
seed -
backend DxLib
size 960x540
1 - 229,359 0 0
33 Right 229,359 0 0
63 - 229,359 0 0
end 63
```

`<フレーム> <押されているキー> <マウスX>,<マウスY> <ホイール累計> <ボタンのビット>` です。

---

## 数値を実行中に触る

`Tune.Get` で読んだ値は `tuning.txt` から差し替えられます。ゲームを止める必要はありません。

```csharp
double speed = Tune.Get("player.speed", 240.0);
double radius = Tune.Get("player.radius", 18.0);
```

一度でも `Tune.Get` を通った値は、終了時に `tuning.txt` へ雛形として書き出されます。

```
# AstrumLoom tuning
# このファイルを保存すると、実行中のゲームに即座に反映されます。
# 書式: キー = 値   ( # 以降はコメント )

player.radius = 18
player.speed = 240
```

あとはこのファイルを開いて数字を書き換えて保存するだけです。0.25 秒以内に反映されます。
`F6` を押すと即座に読み直します。

---

## テスト計画を書く

`--selftest` で走る計画は、`GameApp.Run` を呼ぶ前に並べておきます。

```csharp
SelfTest.Wait(30);
SelfTest.Check("シーンが立ち上がっている", () => Scene.NowScene is PlayScene);
SelfTest.Shot("boot");

SelfTest.Do("右キーを押す", () => VirtualInput.Press(Key.Right));
SelfTest.Wait(30);
SelfTest.Do("右キーを離す", () => VirtualInput.Release(Key.Right));
SelfTest.Check("右に動いた", () => PlayScene.TestProbe.MovedRight());
```

使えるもの:

| 呼び出し | 意味 |
| --- | --- |
| `Wait(frames)` | 指定フレーム待つ |
| `Hold(key, frames)` | キーを指定フレーム押しっぱなしにする |
| `Tap(key)` | キーを 1 フレーム押す |
| `Shot(name)` | スクリーンショットを撮る |
| `Check(label, () => 条件)` | 条件を検査して PASS/FAIL を記録 |
| `Do(label, () => 処理)` | 任意の処理を 1 回実行（例外は FAIL になる） |
| `Record(label, passed)` | 結果を直接記録する |

注意点がひとつあります。`VirtualInput.Press` の効果は**次のフレームの入力確定から**反映されます。
押した直後に `Check` を置くと、まだ届いていません。必ず `Wait` を挟んでください。

失敗があるとプロセスの終了コードが 1 になるので、`playtest.ps1` や CI からそのまま判定できます。

---

## いまのところの制約

「バックエンドの違いを意識しなくてよい」を目指していますが、まだ揃っていない箇所があります。

### ウィンドウのリサイズと論理解像度

`AstrumCore.Width` / `Height` は `GameConfig` に書いた**論理解像度**を返します。
描画座標はこの空間で書きます。

- **DxLib**: `SetGraphMode` で描画面を論理解像度に固定し、ウィンドウ側で拡大するので、
  ウィンドウの大きさが変わっても座標はそのままで通ります。
- **RayLib**: 論理解像度と実ウィンドウの間にスケーリングが入っていません。
  ウィンドウをリサイズすると、描画は左上基準のまま実ピクセルで出ます。

RayLib で作るときは、当面**ウィンドウサイズを変えない前提**で書いてください。
`GameConfig.Resizable` は RayLib 側で意図どおりに効いていません（`docs/KNOWN-ISSUES.md` の
「真偽不明のまま残っている指摘」に `RayLibPlatform.cs:29` として挙がっています）。

差が出る箇所を見つけたら、両方のバックエンドで同じ手順のスクリーンショットを撮って
並べるのがいちばん早い切り分けです。

```powershell
.\tools\playtest.ps1 -Project Hoshiori -Backend dxlib  -Seconds 5
.\tools\playtest.ps1 -Project Hoshiori -Backend raylib -Seconds 5
```

---

## 関連

- 既知の不具合と修正状況は [KNOWN-ISSUES.md](KNOWN-ISSUES.md)
- デバッグ用のホットキーとコマンドライン引数の一覧は [DEBUG.md](DEBUG.md)
- ループや入力まわりの、順番を変えると壊れる決まりごとは [INVARIANTS.md](INVARIANTS.md)
- ライブラリ全体の設計思想は [../README.md](../README.md)
