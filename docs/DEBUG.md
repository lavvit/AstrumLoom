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
| `--record <ファイル>` | 入力を記録する |
| `--replay <ファイル>` | 記録した入力を再生する |
| `--tuning <ファイル>` | tuning ファイルのパス（既定 `tuning.txt`） |
| `--out <ディレクトリ>` | スクショ・ログの出力先（既定 `debugout`） |
| `--overlay` / `--no-overlay` | デバッグオーバーレイの初期状態 |
| `--no-log-overlay` | 画面左上のログ表示を消す（スクショを綺麗に撮る用） |
| `--no-hotkeys` | F1〜F6 を無効化 |

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

---

## 関連

- 新規ゲームの作り方と実機デバッグの流れは [WORKFLOW.md](WORKFLOW.md)
- ループや入力まわりの、順番を変えると壊れる決まりごとは [INVARIANTS.md](INVARIANTS.md)
