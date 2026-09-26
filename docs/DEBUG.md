# デバッグと自動化のリファレンス

AstrumLoom で作ったゲームは、何もしなくても以下のホットキーとコマンドライン引数を持ちます。
`GameApp.Run` を通していれば追加の実装は要りません。

---

## ホットキー

| キー | 動作 |
| --- | --- |
| `F1` | デバッグオーバーレイの表示切り替え（画面右上） |
| `F2` | スクリーンショットを 1 枚撮る |
| `F3` | スロー再生の倍率を巡回（1 → 1/2 → 1/4 → 1/8 → 1） |
| `F4` | 一時停止 / 再開 |
| `F5` | 一時停止したまま 1 フレームだけ進める |
| `F6` | `tuning.txt` を読み直す（無ければ雛形を書き出す） |

ホットキーは**プラットフォームの生入力**を直接見ています。そのため

- 一時停止中でも効く
- 入力再生（`--replay`）中でも効く。しかも記録された入力を汚さない

という性質があります。

無効にしたいときは `GameConfig.EnableDebugHotkeys = false` か `--no-hotkeys` です。
割り当てを変えたいときは `DebugControl.KeyPause` などに別の `Key` を入れてください。

### F1〜F6 がゲーム側と衝突するとき

ゲーム本体が F1〜F6 を使いたい場合は `GameConfig.DebugHotkeyMode`（または `--hotkeys`）で
ホットキーの受け付け方を切り替えられます。

| モード | 動作 |
| --- | --- |
| `Direct`（既定） | 従来どおり F1〜F6 を単独押しで受け付ける |
| `Modifier` | F1〜F6 は修飾キー（既定 Ctrl。`GameConfig.DebugHotkeyModifier`）併用時のみ受け付ける。ゲーム側は素の F キーをそのまま使える |
| `MenuOnly` | 個別ホットキーは無効。デバッグメニュー経由でのみ操作する |
| `Off` | ホットキー・メニューとも全て無効（`EnableDebugHotkeys = false` と同義） |

`Modifier` / `MenuOnly` / `Direct` のいずれでも、**Modifier + `DebugControl.KeyMenu`（既定 Ctrl+F11）**
でデバッグメニューを開閉できます。メニューは ↑↓ で選択、Enter/Space で決定、Esc で閉じます。
既定項目はオーバーレイ表示切替・スクリーンショット・スロー切替・一時停止/再開・コマ送り・
チューニング再読込・閉じるです。ゲーム側から `DebugMenu.Add("項目名", () => ...)` で項目を追加できます。

```csharp
config.DebugHotkeyMode = DebugHotkeyMode.Modifier; // F1〜F6 は Ctrl 併用時のみ
```

コマンドラインからは `--hotkeys <direct|modifier|menu|off>` で指定します。

---

## コマンドライン引数

`--help` でこの一覧が出ます。`--key value` と `--key=value` のどちらでも書けます。

### ウィンドウ・バックエンド

| 引数 | 意味 |
| --- | --- |
| `--backend dxlib\|raylib` | 使用するバックエンド |
| `--dxlib` / `--raylib` | 上の短縮形 |
| `--width <px>` `--height <px>` | 論理解像度 |
| `--scale <倍率>` | ウィンドウ拡大率 |
| `--fullscreen` / `--windowed` | フルスクリーン切り替え |

### タイミング

| 引数 | 意味 |
| --- | --- |
| `--fps <数>` | 目標 FPS（`0` で無制限） |
| `--vsync` / `--no-vsync` | 垂直同期 |
| `--mt` / `--no-mt` | 更新を別スレッドで回すか |
| `--fixed` / `--no-fixed` | 固定ステップ更新 |
| `--hz <数>` | 固定ステップの更新周波数（既定 60） |
| `--seed <数>` | 乱数シード |

### デバッグ・自動化

| 引数 | 意味 |
| --- | --- |
| `--shot-every <N>` | N 論理フレームごとにスクリーンショット |
| `--quit-after <N>` | N 論理フレーム後に自動終了 |
| `--quit-after-sec <秒>` | 指定秒後に自動終了（**ゲーム内時間**。下の注意を参照） |
| `--selftest` | 登録済みテスト計画を自動走行して PASS/FAIL を出す |
| `--turbo` / `--no-turbo` | ロックステップを実時間の待ち無しで走らせる（既定: `--selftest` のときだけ ON） |
| `--record <ファイル>` | 入力を記録する |
| `--replay <ファイル>` | 記録した入力を再生する |
| `--tuning <ファイル>` | tuning ファイルのパス（既定 `tuning.txt`） |
| `--out <ディレクトリ>` | スクショ・ログの出力先（既定 `debugout`） |
| `--overlay` / `--no-overlay` | デバッグオーバーレイの初期状態 |
| `--no-log-overlay` | 画面左上のログ表示を消す（スクショを綺麗に撮る用） |
| `--no-hotkeys` | F1〜F6 を無効化（`--hotkeys off` と同じ） |
| `--hotkeys <direct\|modifier\|menu\|off>` | デバッグホットキーの受け付け方（既定 `direct`） |

### ゲームごとの引数を足す

`Startup.Register` で登録すると、そのゲーム固有の引数を「不明な引数」にせず受け取れます。
`--help` にも並びます。登録は `GameApp.Run` より前に行ってください。

```csharp
Startup.Register("demo", false, "自動操縦で遊ばせる");
Startup.Register("start-wave", true, "開始ウェーブ");

// 受け取り側
var options = Startup.Parse(args);
bool demo = options.Flag("demo");
int wave  = (int)options.Number("start-wave", 1);
string s  = options.Text("mode", "normal");
```

| 呼び出し | 意味 |
| --- | --- |
| `Register(name, takesValue, description)` | 引数を 1 つ登録する。`--` は付けても付けなくてもよい |
| `options.Flag(name)` | 値を取らない引数が指定されたか |
| `options.Text(name, fallback)` | 値を文字列で読む |
| `options.Number(name, fallback)` | 値を数として読む |
| `Startup.HelpText` | 共通オプション＋登録したぶんの一覧 |

共通オプションと同じ名前（`seed` や `out` など）は登録できません。黙って上書きされて
`--seed` が効かなくなるのを防ぐため、登録時に例外になります。

### `GameApp.Run` を使わない構成で自動化を有効にする

バックエンドを 1 つに固定したい、`AstrumLoom.Game`（と RayLib）を参照したくない、といった理由で
自前でプラットフォームを作って `AstrumCore.Boot` を呼んでいる場合でも、引数の解釈は Core の
`Startup` だけで完結します。**この 6 行を書かない限り、`--selftest` も `--replay` も
`--shot-every` も一切効きません**（引数が読まれないため）。

```csharp
ConsoleBridge.Attach();                  // WinExe でも親のコンソールへログと結果を返す
var options = Startup.Parse(args);
config.Apply(options);                   // ★ プラットフォームを作る「前」に呼ぶ
if (config.Seed.HasValue) Randomize.Seed(config.Seed.Value);
var platform = new DxLibPlatform(config);
AstrumCore.Boot(config, platform, scene, options);
```

`config.Apply` を呼び忘れて `Boot` に `options` だけ渡した場合は、`Boot` が代わりに適用します。
固定ステップ・ロックステップ・単一スレッド・seed・再生ファイルの読み込みはそれで間に合いますが、
解像度・拡大率・FPS 上限・VSync・ターボは**プラットフォーム生成時に読まれる**ので手遅れです。
どの指定が効かなかったかは警告に出ます。

フラグ以外の引数（台データのパスなど）は `options.Unknown` に入ります。
`--help` を自分で出すなら `options.ShowHelp` と `Startup.HelpText` を使ってください。

---

## 3 つの時間モード

`GameConfig` の設定で、ゲーム更新の進み方が変わります。

| モード | 設定 | `Update` に渡る dt | 使いどころ |
| --- | --- | --- | --- |
| 可変 | 既定 | 実測の経過時間 | 手早く動かしたいとき |
| 固定ステップ | `FixedUpdate = true` | 常に `1 / FixedUpdateHz` | 通常のゲーム。処理落ちしても進みが変わらない |
| ロックステップ | `FixedUpdate` + `LockStep` | 常に `1 / FixedUpdateHz` | 記録・再生・セルフテスト |

固定ステップは実時間との差をためて、1 ループで最大 `MaxCatchUpSteps` 回まで追いつきます。
ロックステップは実時間を一切見ず、1 ループにつき必ず 1 論理フレームだけ進めます。
だから同じ入力からは必ず同じ結果が出ます。

`--selftest` / `--record` / `--replay` を指定すると、
**再現性のために自動でロックステップ + 単一スレッドへ切り替わります**（`--mt` で上書き可）。
切り替わったことはログに出ます。

### ターボ（自動テストを実時間より速く走らせる）

ロックステップは **1 ループ＝1 論理フレーム**です。つまりループの目標 FPS が
そのままゲームの進む速さになります。`TargetFps = 60` のままだと、
600 フレームぶんの検証に実時間でもきっちり 10 秒かかります。

そこで `--selftest` のときは既定で**ループの FPS 上限と垂直同期を外します**（ターボ）。
論理フレームの中身は 1 ミリも変わらないので、テスト結果も記録の再生も同じです。
速くなるのは実時間の待ち時間だけです。

- `--no-turbo` … 等速に戻す。演出を目で追いたいときに使う
- `--turbo` … `--replay` でも上限を外す（既定は等速。人が見て確かめるためのモードなので）
- `--fps` / `--vsync` / `--no-vsync` を明示したときは、そちらが優先される
- `--record` は**人が手で遊びながら**記録するので、`--turbo` を明示しない限り常に等速

実測（ayaori のセルフテスト 406 フレーム）:

| 走らせ方 | 実時間 |
| --- | --- |
| `--selftest --no-turbo`（従来） | 19.9 秒 |
| `--selftest`（ターボ既定 ON） | 11.8 秒 |
| Release ビルド + `--selftest` | 4.0 秒 |

ターボを効かせても縮まないぶんは、ループの待ちではなく**中身のコスト**です。
上の表のように Debug ビルドの実行コストが大きいので、
証跡を早く回したいときは `playtest.ps1 -Release` を使ってください。
（`TargetFps = 0` で作ってあるゲームは元から上限が無いので、ターボでは変わりません。）

`--quit-after-sec` が数えるのは**ゲーム内時間**（論理フレームの dt の合計）です。
可変モードなら実時間と一致しますが、ロックステップでは 1 フレーム＝常に `1/hz` 秒として数えるので、
処理が重いと実時間ではもっとかかります。実時間で確実に打ち切りたいときは
`tools\playtest.ps1` を使ってください（タイムアウトで強制終了し、終了コード 124 を返します）。

ゲーム側からは次のもので状態を読めます。

```csharp
AstrumCore.DeltaTime      // 直近の論理フレームの dt（固定ステップなら常に一定）
AstrumCore.FrameCount     // 実行した論理フレーム数（一時停止中は増えない）
AstrumCore.DrawFrameCount // 描画フレーム数
AstrumCore.IsFixedStep    // 固定ステップで走っているか
```

---

## デバッグオーバーレイ

`F1` で出る画面右上のパネルです。画面左上は `Log` が使うので、ぶつからないようにしてあります。

出るもの: バックエンド名 / FPS（最小〜最大） / 論理フレーム番号 / dt またはステップ幅 /
解像度 / 現在のシーン名 / 一時停止・スローの状態 / `● REC` `▶ REPLAY` / スクショ枚数 / tuning の件数 / 時刻。

ゲーム固有の情報を足したいときは `Overlay` を継承して `Compose` を上書きします。
`Draw` ごと差し替えると共通部分が消えるので、行を足したいだけなら `Compose` のほうです。

```csharp
internal sealed class MyOverlay : Overlay
{
    protected override void Compose(List<(string text, Color color)> lines)
    {
        base.Compose(lines);   // 共通の行
        lines.Add(($"敵 {EnemyPool.ActiveCount} 体", Color.White));
    }
}

// シーンの Enable などで
Overlay.Set(new MyOverlay());
```

---

## 画面のログ

`Log.Write` / `Log.Debug` / `Log.Warning` / `Log.Error` は、コンソールと画面左上の両方に出ます。

| プロパティ | 意味 |
| --- | --- |
| `Log.DrawOnScreen` | 画面に出すか（`--no-log-overlay` で false） |
| `Log.ScreenSeconds` | 画面に残す秒数（既定 10） |
| `Log.MaxLogCount` | 画面に同時に出す最大行数（既定 30） |
| `Log.MaxStoredCount` | メモリに保持する最大件数（既定 2000） |
| `Log.IncludeInfo` | Info レベルを画面に出すか |

`Debug` レベルの扱いには癖があります。

- コンソールには常に出ます。
- 画面には Debug ビルドでのみ出ます（Release ビルドでは出ません）。
- `Log.Save` が書き出すファイルには、**どのビルドでも入りません**。
  `playtest.ps1` が集める `run.log` に Debug 行が無いのはこのためです。全部欲しいときは `stdout.txt` を見てください。

---

## スクリーンショット

```csharp
Snapshot.Request("ボス撃破");   // 次の描画フレームで保存される
```

保存先は `Snapshot.Directory`（`--out` で設定されます）。
ファイル名は `shot_<論理フレーム番号 6 桁>_<名前>.png` です。

保存はオーバーレイとログを描いたあと、フレームを閉じる直前に行われます。
つまり**画面に見えているものがそのまま撮れます**。ログを消したいときは `--no-log-overlay` です。

要求はどのスレッドから出しても構いません。実際の保存は必ず描画スレッドで行われます。
保留が 8 件を超えると、取りこぼしを黙って捨てずに警告を出します。

---

## 落ちたとき

実行中に例外が出ると、赤い画面にスレッド名・例外の種類・メッセージ・スタックトレースが出て、
6 秒後に自動で閉じます。同じ内容はコンソールとログにも残ります。

`GameApp.Run` の戻り値は、致命的エラーがあれば `1` です。
ウィンドウが出る前に落ちた場合は画面に何も出せないので、コンソールにだけ出ます
（WinExe でも PowerShell から起動していれば見えます）。

## 起動時間を測る

`--boot-timing` を付けて起動すると、最初の 1 枚が描き終わった時点で内訳が出ます。

```
Sandbox.exe --boot-timing --quit-after-sec 1
```

```
=== 起動時間の内訳 ===
  ランタイムの立ち上がり（Main に入るまで）            229.0 ms
  DxLib_Init                                1015.3 ms
  最初の描画                                  320.7 ms
  合計                                      1736.7 ms
```

段階を足したいときは `BootTimer.Mark("名前")` を呼ぶだけです。記録は常に行い、
出力だけをオプションで切り替えています。「計測したときだけ通る道」を作らないためで、
そうしないと測った値が普段の起動と違うものになります。

**`Mark` が出すのは「前の記録からの差」です。**間に挟まった別の処理もその段階に乗るので、
ある処理そのものにかかった時間を知りたいときは `Mark` ではなく

```csharp
using (BootTimer.Measure("フォントファイルを読む"))
    bytes = File.ReadAllBytes(path);
```

を使ってください。`└` 付きの行として内訳に並び、同じ名前で何度も呼ぶと合算と回数が出ます。
この区別は実際に一度引っかかりました。`Mark` だけで見ていたときは
「2 本目のフォントのファイル読みに 627 ms」と読めましたが、`Measure` で計り直すと
ファイル読みは 14 ms で、その 600 ms は 2 本目を作るまでの間に走っていた別の処理でした。

数字はばらつきます。1 回目は必ず遅く（ディスクと Defender）、同じ設定でも
1000〜1800 ms の幅で動きます。2 つの設定を比べるときは、**交互に何度も回して中央値**を見てください。
まとめて片方ずつ回すと、その間だけマシンが忙しかったことが差に化けます。

---

### 起動時間について分かっていること（2026-09-26 計測）

暖まった状態の中央値で、プロセス開始から最初の 1 枚まで **DxLib 約 0.96 秒 / raylib 約 1.17 秒**。
内訳の大半はこちらから削れない場所にある。

| 段階 | DxLib | raylib |
|---|---:|---:|
| ネイティブの初期化（`DxLib_Init` / `InitWindow`＋`InitAudioDevice`） | 約 790 ms | 約 690 ms |
| ランタイムの立ち上がり（Main に入るまで） | 130〜280 ms | 130〜280 ms |
| フォント | 約 200 ms | 約 140 ms |
| 最初の描画 | 10〜30 ms | 50〜250 ms |

- ネイティブの初期化が最大で、managed 側からは手が出ない。
- ランタイムの立ち上がりは ReadyToRun で publish すると縮む（中央値 1275→1113 ms、最小 1001→801 ms）。
- raylib のフォントは、1 本あたり 60〜75 ms の **アトラス焼き**（約 3300 字）が本体だった。
  焼き上がりは毎回同じなので、**`.fontcache` へ保存して 2 回目以降は読むだけにしてある**
  （[FontAtlasCache.cs](../RayLib/FontAtlasCache.cs)）。2 本で 142 ms → 37 ms（読み 8 ms + GPU へ載せる 29 ms）。
  起動全体では中央値 1332 → 1231 ms。
  - 鍵はフォントのパス・更新時刻・長さ・大きさ・焼く字の集合。フォントが差し替われば別の鍵になる。
  - 壊れたファイルや古い版は読まずに焼き直す（切り詰めたファイルで確認済み）。
  - 1 本 4〜8 MB ある。`.audiocache` と同じで、使わなくなった鍵のファイルは自動では消えない。
  - ファイル読み（Yu Gothic の .ttc は 13 MB）も同じパスなら 1 回で済ませる。
- **「漢字は出会ったときに足す」方式（mediadeck の `ui_font_want` と同じ考え方）は試して取り下げた。**
  起動時に焼く字を 591 字まで減らすとフォントの取得は 210 ms → 34 ms になるが、
  最初の 1〜2 コマで漢字が `?` で描かれる。焼き直しは溜まった字をまとめて 1 回で処理しても、
  描くだけで測らない文字列があるぶん 1 フレームでは終わらない。
  raylib は 1 枚のアトラスを丸ごと焼き直す作りなので、後から足すことができない。

---

---

## 関連

- 新規ゲームの作り方と実機デバッグの流れは [WORKFLOW.md](WORKFLOW.md)
- ループや入力まわりの、順番を変えると壊れる決まりごとは [INVARIANTS.md](INVARIANTS.md)
