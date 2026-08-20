# AstrumLoom

**AstrumLoom** は、C# / .NET 向けに開発しているマルチメディア・ゲームライブラリです。

DXLib、Raylib、MonoGame などのゲームライブラリでよく使われる機能を参考にしつつ、描画・音声・入力・フォントなどをひとつの扱いやすい API にまとめることを目的としています。

> **「使いやすさ」と「拡張性」を両立し、ゲーム側がバックエンドの細かな違いを意識しなくても使えるライブラリを作る。**

現在は開発中です。

---

## ✨ Features

AstrumLoom では、ゲーム制作で頻繁に使用する機能を統一された形で扱えるようにすることを目指しています。

### Graphics

- テクスチャ読み込み
- 画像描画
- 拡大・縮小
- 回転描画
- 加算 / 乗算などのブレンド
- 色指定
- 白色透過
- 三角形などのプリミティブ描画
- スプライトバッチによる描画最適化

ゲーム側からレンダリングバックエンドの詳細を極力意識せずに利用できる構造を目指しています。

---

### Font / Text

FreeType を利用した文字描画機能を実装しています。

対応・実装中の機能：

- TrueType / OpenType フォント
- フォントキャッシュ
- 文字色
- 縁取り
- ドロップシャドウ
- 左寄せ / 中央寄せ / 右寄せ
- 折り返し
- 文字幅計測

ゲームUIで必要になりやすい、

- 縁取り文字
- 影付き文字
- センタリング
- サイズ計算

などを簡単に扱えることを重視しています。

---

### Audio

ゲーム向けの音声再生機能を提供します。

対応形式・機能：

- WAV
- OGG
- MP3
- SE再生
- BGM再生
- ストリーミング再生
- 同一SEの多重再生
- Volume
- Pan
- Pitch
- Playback Rate

短い効果音と長時間のBGMを、それぞれ適切な方式で再生できる構造を採用しています。

---

### Input

キーボード・マウス入力をゲームループから扱いやすい形式で管理します。

- Keyboard
- Mouse
- 押下状態
- 押した瞬間
- 離した瞬間
- 入力状態の蓄積防止
- ドラッグ＆ドロップ
- スレッドセーフな入力管理

単純な現在状態だけでなく、

```text
Press
Down
Release
```

のようなゲームで頻繁に必要になる入力判定を扱いやすくすることを目指しています。

---

### Async Loading

画像・音声などのリソースを非同期で読み込むための共通基盤を用意しています。

`AsyncLoadableBase` を中心として、

```text
未ロード
  ↓
ロード中
  ↓
ロード完了
```

というリソース状態を共通化します。

主な目的：

- 非同期ロード
- ロード状態管理
- タイムアウト管理
- メインスレッド処理との分離
- リソースクラス間での共通処理

ゲーム開始時やシーン切り替え時に大量のリソースを読み込む場合でも、ゲーム側の実装を複雑にしない設計を目指しています。

---

## 🧵 Design Concept

AstrumLoom では、特定の描画ライブラリへゲームロジックが強く依存しない構造を重視しています。

```text
Game
 │
 │ AstrumLoom API
 ▼
AstrumLoom
 │
 ├─ Graphics
 ├─ Audio
 ├─ Font
 ├─ Input
 ├─ Resource
 └─ Backend
```

ゲーム側は AstrumLoom の API を使用し、実際の描画や音声処理については内部実装へ任せます。

これにより、

```text
ゲームコード
     ↓
AstrumLoom
     ↓
描画 / 音声 / OS / API
```

という依存方向を維持します。

### AstrumLoom が目指すもの

#### Simple

ゲーム側から簡単に使えること。

```csharp
// 「何を描画したいか」を中心に記述できるAPIを目指す。
```

内部のGPU APIやレンダリング処理を毎回意識する必要がない構造にします。

#### Flexible

必要になれば低レイヤーへアクセスできること。

単純化のために拡張性を犠牲にするのではなく、高度なゲームやツールでも利用できる構造を目指します。

#### Backend Independent

ゲームコードとバックエンドを可能な限り分離します。

将来的に実装方式を変更しても、AstrumLoom を利用するゲーム側への影響を最小限に抑えることを目標としています。

---

## 📦 Main Modules

AstrumLoom は、おおまかに次の領域で構成されています。

| Module | Description |
| --- | --- |
| Graphics | 2D描画・テクスチャ・プリミティブ |
| Font | フォント読み込み・文字描画 |
| Audio | BGM / SE再生 |
| Input | キーボード・マウス入力 |
| Resource | リソース管理 |
| Async | 非同期ロード |
| Timer | 時間・ゲームタイマー |
| Backend | 実際の描画 / 音声処理との接続 |
| Debug | セルフテスト・入力の記録/再生・スクショ・tuning・オーバーレイ |
| App | 起動口（引数の解釈とバックエンド生成） |

プロジェクトの対応は次のとおりです。

| プロジェクト | 役割 | 参照 |
| --- | --- | --- |
| `Core` | バックエンド非依存の本体。`Core/Debug/` に実機デバッグの道具 | なし |
| `DXLib` | DxLib バックエンド | Core |
| `RayLib` | Raylib バックエンド | Core |
| `Extend` | Skin / Animation / Movie などの上位機能 | Core |
| `Game` (`AstrumLoom.GameUtil`) | 起動層。`GameApp.Run` | Core + 全バックエンド |
| `Sandbox` | 機能確認用のデモ | GameUtil |

**ゲーム側が参照するのは `Game` だけ**です。ここが唯一バックエンドを全部知っている層で、
その先のゲームコードにはバックエンドの型名が出てきません。

> 実際のプロジェクト構造については開発に合わせて変更される可能性があります。

---

## 🚀 Getting Started

AstrumLoom は現在開発中のため、正式な NuGet パッケージとしての配布方法はまだ確定していません。
開発環境では、リポジトリの中に新しいゲームプロジェクトを作って始めます。

```powershell
.\tools\newgame.ps1 -Name Hoshiori
.\build.ps1 -Project Hoshiori -Run
```

これだけで、矢印キーで動くシーンが立ち上がります。生成されるのは 3 ファイルです。

```text
Hoshiori\
  Hoshiori.csproj    AstrumLoom.GameUtil を参照するだけ
  Program.cs         GameApp.Run を 1 回呼ぶ
  PlayScene.cs       ここからゲームを書き始める
```

ゲーム側が参照するのは `AstrumLoom.GameUtil` の 1 つだけです。
これが Core / DXLib / RayLib / Extend をまとめて連れてくるので、
**ゲームのコードにバックエンドの型名が出てきません**。

詳しい手順は [docs/WORKFLOW.md](docs/WORKFLOW.md) にあります。

---

## 💡 Basic Usage

ゲームループはライブラリ側が持ちます。ゲーム側が書くのは「設定」と「シーン」だけです。

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

```csharp
internal sealed class PlayScene : Scene
{
    private double _x, _y;

    public override void Enable()
    {
        _x = AstrumCore.Width / 2.0;
        _y = AstrumCore.Height / 2.0;
    }

    public override void Update()
    {
        if (Key.Esc.Push()) AstrumCore.End();

        // tuning.txt に書けば実行中でも差し替わる値
        double speed = Tune.Get("player.speed", 240.0);
        if (Key.Right.Hold()) _x += speed * AstrumCore.DeltaTime;
        if (Key.Left.Hold())  _x -= speed * AstrumCore.DeltaTime;
    }

    public override void Draw()
    {
        Drawing.Fill(new Color(12, 14, 22));
        Drawing.Circle(_x, _y, 18, new Color(120, 200, 255));
    }
}
```

`GameApp.Run` はコマンドライン引数の解釈とバックエンドの生成を引き受けます。
そのため、上のコードは何も足さなくても `--backend raylib` で Raylib に切り替わり、
`--selftest` や `--shot-every 60` といったデバッグ用の引数を受け付けます。

---

## 🧭 Debug & Tooling

実機で動かしながら直す、という作業のための道具が最初から入っています。

| キー | 動作 |
| --- | --- |
| `F1` | デバッグオーバーレイ |
| `F2` | スクリーンショット |
| `F3` | スロー（1/2 → 1/4 → 1/8） |
| `F4` | 一時停止 |
| `F5` | コマ送り |
| `F6` | `tuning.txt` の再読み込み |

```powershell
.\tools\playtest.ps1 -Project Hoshiori -SelfTest    # テスト計画を自動走行して PASS/FAIL
.\tools\playtest.ps1 -Project Hoshiori              # 10 秒走らせてスクショとログを収集
.\tools\playtest.ps1 -Project Hoshiori -Record r.txt  # 入力を記録
.\tools\playtest.ps1 -Project Hoshiori -Replay r.txt  # まったく同じ操作を再生
```

記録・再生とセルフテストのあいだは、自動でロックステップ（1 ループ 1 論理フレーム、実時間を見ない）
＋単一スレッドに切り替わります。だから**再生した画面は記録したときと 1 ドットも変わりません**。
バグを踏んだ操作をファイルとして残せる、ということです。

`tuning.txt` に書いた数値は、ゲームを止めずに反映されます。

```text
player.speed  = 240
player.radius = 18
```

引数とホットキーの全一覧は [docs/DEBUG.md](docs/DEBUG.md) にあります。

---

## 🎮 Example Use Cases

AstrumLoom は特に以下のような用途を想定しています。

### 2D Games

- リズムゲーム
- シューティング
- アクション
- パズルゲーム
- RPG
- シミュレーションゲーム

### Game Tools

- ゲームエディタ
- デバッグツール
- リソースビューア
- 譜面エディタ
- UIエディタ

### Slot / Arcade Style Games

大量の、

- 画像
- アニメーション
- SE
- BGM
- UI
- エフェクト

を扱うゲームについても使いやすい構造を目指しています。

---

## 🖼 Rendering

AstrumLoom の描画システムでは、ゲーム側から描画命令を渡し、それを内部でまとめて処理する構造を採用しています。

特に2Dゲームでは大量の画像描画が発生するため、スプライトバッチなどを利用して描画回数を抑えることを重視しています。

今後は、

- 描画コマンド整理
- マテリアル
- シェーダー
- RenderTarget
- PostEffect

などへ拡張できる構造を想定しています。

---

## 🔊 Audio System

音声は用途によって処理方法を分けています。

### Sound Effect

短時間のSEでは複数音を同時に再生できるよう、再生インスタンスを管理します。

```text
Sound
 ├─ Instance 1
 ├─ Instance 2
 └─ Instance 3
```

同じSEを連続して再生した場合でも、前の音を強制的に停止しない構造を想定しています。

### BGM

長いBGMについてはストリーミング再生を利用し、必要以上に大きな音声データをメモリへ展開しないようにします。

---

## 📝 Text Rendering

ゲームでは通常のGUIアプリより複雑な文字表現が必要になります。

AstrumLoom では、

```text
Shadow
   ↓
Outline
   ↓
Text
```

のように複数の描画処理を組み合わせ、ゲーム向けの文字表現を作れるようにしています。

例えば、

```text
┌──────────────────┐
│                  │
│      SCORE       │
│     123456       │
│                  │
└──────────────────┘
```

のような中央配置UIについても、描画側で扱いやすくすることを目標としています。

---

## ⚙️ Resource Management

Texture、Sound、Font などはゲーム内で大量に生成されるため、AstrumLoom ではリソース管理も重要な機能として扱います。

想定しているライフサイクル：

```text
Create
  ↓
Loading
  ↓
Ready
  ↓
Use
  ↓
Dispose
```

非同期ロード中のリソースについても状態を確認できるようにし、安全に利用できる仕組みを整備しています。

---

## 🎬 Video

FFmpeg を利用した動画再生機能を予定しています。

想定形式：

```text
MP4
 ↓
FFmpeg
 ↓
Video Frame
 ↓
AstrumLoom Texture
 ↓
Rendering
```

ゲーム内ムービーや背景動画などで利用できる仕組みを目指しています。

---

## 🔧 Development Status

AstrumLoom は現在も開発中です。

主に以下の機能を開発・改善しています。

- Graphics API
- Sprite Batch
- Texture
- Font
- Audio
- Input
- Async Resource Loading
- Timer
- Video
- Backend abstraction

APIについては今後変更される可能性があります。

---

## ⚠️ Known Issues / Research Topics

コードを読んで「実際に壊れる経路を追えた」ものの一覧は [docs/KNOWN-ISSUES.md](docs/KNOWN-ISSUES.md) にあります。
修正済みかどうかの印つきです。

そのほか、現在以下のような項目について調査・改善を行っています。

### Text Rendering

- 縁取り品質
- フォントキャッシュ
- 文字幅計算
- 高頻度な文字描画時のパフォーマンス

### Graphics

- 三角形などのプリミティブ描画
- SpriteBatch の最適化
- ブレンド処理
- 色補間

### Color

OKLCH などを利用した色補間について、Hue が180度付近を跨ぐ場合の補間処理を調整しています。

### Video

FFmpeg を利用した MP4 再生機能を検討・実装中です。

---

## 🗺 Roadmap

今後は以下のような機能を予定しています。

- [ ] Graphics API の安定化
- [ ] Texture API の整理
- [ ] SpriteBatch の最適化
- [ ] Font API の安定化
- [ ] Audio API の拡張
- [ ] Input API の整理
- [ ] Async Resource System の改善
- [ ] Shader
- [ ] RenderTarget
- [ ] Post Effect
- [ ] Video / FFmpeg
- [x] Debug Overlay
- [x] 実機デバッグの道具（セルフテスト / 入力の記録・再生 / スクショ自動化 / tuning ホットリロード）
- [x] プロジェクト雛形の生成とビルドスクリプト
- [ ] Game Editor 向けAPI
- [x] ドキュメント整備（[WORKFLOW](docs/WORKFLOW.md) / [DEBUG](docs/DEBUG.md)）
- [x] サンプルプロジェクト（`Sandbox` と `newgame.ps1` の雛形）
- [ ] NuGet パッケージ化

---

## 🧪 Stability

現時点では開発版です。

```text
API Stability : Experimental
Production    : Not Recommended Yet
```

内部API・クラス名・名前空間などは予告なく変更される場合があります。

---

## 🤝 Contributing

現在はライブラリ設計および基盤機能を優先して開発しています。

バグ報告や改善案については、リポジトリの Issue が利用可能になった場合、そちらを利用してください。

---

## 📄 License

ライセンスはプロジェクト内の `LICENSE` ファイルを参照してください。

`LICENSE` がまだ存在しない場合、正式公開前にライセンスを設定してください。

---

## 🌌 About the Name

**AstrumLoom**

- `Astrum` — 星・星空
- `Loom` — 織機・織り上げるもの

複数のシステムやバックエンドをひとつのライブラリとして「織り合わせる」というコンセプトを込めた名前です。

```text
Graphics ─┐
Audio ────┤
Input ────┤
Font ─────┼── AstrumLoom ── Game
Video ────┤
Tools ────┘
```

さまざまな機能をひとつに織り上げ、ゲームを作る側が「作りたいもの」に集中できるライブラリを目指しています。