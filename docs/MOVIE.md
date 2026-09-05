# 動画再生（Movie）

`Movie` は「音つきのテクスチャ」です。`Texture` と同じように描け、`Sound` と同じように
再生位置や音量を触れます。バックエンドごとの実装は次の通りです。

| バックエンド | 実体 | 仕組み |
| --- | --- | --- |
| DxLib | `DxLibMovie` | DxLib の動画グラフィックハンドル（`PlayMovieToGraph` 等） |
| Raylib | `RayLibMovie` | **ffmpeg を子プロセスで回し、生フレームをパイプで受け取る**（本ドキュメント） |

---

## 1. 要件定義

### 1.1 背景

Raylib 自体に動画デコード機能は無く、`RayLibMovie` は長らく「常に再生不可」を返すだけの
スタブでした。`Extend/MovieExtend.cs` に FFMpegCore を使った実装がありますが、
**再生前に全フレームを PNG へ展開する**方式なので、

- 開始までに数十秒〜（1080p/30fps の 10 秒動画で 300 枚の PNG）
- 一時ディレクトリを数百 MB 消費する
- 1 フレームごとに `LoadTexture`（ファイル I/O + PNG デコード）が走る

という難点があり、実用的な再生には向きません。そこで Raylib 側は
**ffmpeg からの逐次デコード（ストリーミング）**として作り直します。

### 1.2 機能要件

| ID | 要件 |
| --- | --- |
| F-1 | mp4 / mkv / webm / avi / mov など、ffmpeg がデコードできる動画を再生できる |
| F-2 | `new Movie(path)` だけで使え、`Draw` / `Play` / `Stop` / `Loop` / `Time` / `Progress` が効く |
| F-3 | 映像は `Texture` と同じ `DrawOptions`（切り出し・基準点・拡縮・回転・反転・色・ブレンド・不透明度）で描ける |
| F-4 | 音声トラックがあれば映像と同期して鳴る。`Volume` / `Pan` / `Pitch` が効く |
| F-5 | 音声が無い動画でも、内部クロックで正しい速度で再生できる |
| F-6 | `Loop = true` で終端から先頭へ継ぎ目なく戻る |
| F-7 | `Time` / `Progress` への代入でシークできる |
| F-8 | `Speed` で再生速度を変えられる |
| F-9 | `Width` / `Height` / `Length`（ミリ秒）が読める |

### 1.3 非機能要件

| ID | 要件 |
| --- | --- |
| N-1 | **再生開始まで待たされない**。ロードは非同期で、待っている間もゲームは 60fps で回る |
| N-2 | ネイティブ（raylib）API を触るのはメインスレッドのみ。他スレッドからの `new` / `Dispose` も安全 |
| N-3 | 使用メモリはフレームバッファ数本ぶんに収まる（全フレームを展開しない） |
| N-4 | ffmpeg が無い環境では**落ちずに** `IsFailed = true` になり、ログに導入方法を出す |
| N-5 | `Dispose` で子プロセス・スレッドが必ず止まる。一時ファイルも消す（音声 WAV を掴んでいる間は裏でリトライし、それでも残った分は次回起動時に 10 分以上前のものとして掃除する） |
| N-6 | `AstrumLoom.RayLib` に新しい NuGet 依存を増やさない（ffmpeg は外部実行ファイルとして呼ぶ） |

### 1.4 対象外（今回やらないこと）

- ハードウェアデコード（NVDEC/QSV）の明示指定
- 字幕・複数音声トラックの切り替え
- 可変フレームレート（VFR）の厳密な PTS 追従。**CFR に正規化**して扱う
- ffmpeg 実行ファイルの同梱・自動ダウンロード（ライセンスの都合。導入は利用者側）

### 1.5 ffmpeg の探索順（N-4 / 導入方法）

`FFmpegTool` が次の順で `ffmpeg.exe` / `ffprobe.exe` を探し、最初に見つかったものを使います。

1. `FFmpegTool.Directory`（コードから明示指定）
2. 環境変数 `ASTRUMLOOM_FFMPEG`（実行ファイルそのもの、またはそれが入ったフォルダ）
3. 実行ファイルと同じ場所の `ffmpeg\bin` / `ffmpeg` / `tools\ffmpeg\bin`
4. `PATH`
5. winget 版（`%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg*\**\bin`）

見つからなければ再生は失敗扱いになり、次のログが 1 度だけ出ます。

```
Movie: ffmpeg が見つかりません。`winget install Gyan.FFmpeg` で導入するか、
環境変数 ASTRUMLOOM_FFMPEG に ffmpeg.exe のパスを設定してください。
```

---

## 2. 設計

```
                 [ 準備スレッド ]                    [ デコードスレッド ]
new RayLibMovie ─▶ ffprobe で 幅/高さ/fps/尺/音声有無
                 ─▶ 音声があれば ffmpeg で WAV へ抽出 ─▶ RayLibSound(streaming)
                 ─▶ ffmpeg -f rawvideo -pix_fmt rgba - ──▶ 1 フレーム = W*H*4 バイトを
                                                            読んでキューへ（最大 8 本）
[ メインスレッド ] Pump()
   ├ 時計を進める（音声があれば音声の再生位置が正、無ければ Stopwatch）
   ├ キューの先頭フレームの表示時刻 <= 現在時刻 の間だけ取り出す（遅れていれば読み捨て＝コマ落ち）
   └ 最後に取り出したフレームを UpdateTexture でテクスチャへ転送
```

要点:

- **フレームの表示時刻**は「シーク基準 + 連番 / fps」。ffmpeg 側で `fps=<実測 fps>` を
  かけて CFR に正規化しているので、連番だけで時刻が決まります（1.4 の通り VFR は非対応）。
- **バッファは使い回す**。`byte[]` は `ConcurrentBag` のプールから取り、表示後に戻します。
  1 フレーム 1080p RGBA = 約 8MB なので、毎フレーム確保すると GC が悲鳴を上げます。
- **テクスチャは 1 枚**。`UpdateTexture` で中身だけ差し替えます（毎フレーム作り直さない）。
- **シークは ffmpeg の再起動**。`-ss` を入力側に付けて開き直し、連番の基準をずらします。
- **ループ**は終端でシーク 0 と同じ処理。音声がある場合は音声も先頭へ戻します。終端の判定には
  「デコーダの世代番号」を使います。シークでデコーダを起動し直した直後は前の世代のスレッドが
  「読み切った」と報告してくることがあり、それを終端と誤認するとシーク先ではなく先頭へ戻ってしまいます。
- 音声は「一度 WAV に焼いてから `RayLibSound`」です。raylib の Music は生 PCM ストリームを
  直接受け取れないため、映像と同じ二重パイプにはしていません。抽出が終わるまでは
  `IsReady` にならないので、尺の長い動画ほど再生開始までの待ちが伸びます（待っている間もゲームは止まりません）。

---

## 3. 使い方

```csharp
var movie = new Movie("Assets/sample.mp4");
movie.Loop = true;

// 更新
movie.Pump();      // 毎フレーム呼ぶ（デコード済みフレームの取り込み・時計の更新）
if (movie.Enable) movie.Play();

// 描画（Texture と同じ）
movie.Draw(640, 360, new DrawOption { Point = ReferencePoint.Center, Scale = 0.5 });

movie.Dispose();
```

| メンバ | 意味 |
| --- | --- |
| `Enable` | 再生・描画できる状態か（準備完了かつ映像が生きている） |
| `IsReady` / `IsFailed` / `Loaded` | 非同期ロードの状態。`Loaded` は「成否が確定した」 |
| `Time` | 再生位置（ミリ秒）。代入でシーク |
| `Progress` | 0.0〜1.0 の再生位置。代入でシーク |
| `Length` | 尺（ミリ秒）。取れないコンテナでは 0 |
| `Speed` | 再生速度。1.0 が等倍 |

---

## 4. 動作検証

Sandbox の 8 番「動画の見本帳（ffmpeg ストリーミング）」が確認用シーンです。

```powershell
# 検証用の mp4（音つき 6 秒 / 音なし 3 秒）を作る。ffmpeg が要る。
.\tools\make-sandbox-assets.ps1 -Force

# 実機で走らせて PASS/FAIL とスクショを取る
.\tools\playtest.ps1 -Backend raylib -SelfTest
```

セルフテストで見ている項目:

- 動画が読めて `Width` / `Height` / `Length` が期待値どおりか
- `Play` 後に映像フレームが実際に進んでいるか（デコード済みフレーム数の増加）
- 再生位置が進んでいるか
- シーク（`Progress = 0.5`）が効くか
- 音なし動画でも再生位置が進むか
- ループで先頭へ戻るか
- ffmpeg が無い環境で `IsFailed` になり、例外で落ちないか
