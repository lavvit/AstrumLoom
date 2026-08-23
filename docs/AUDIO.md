# AstrumLoom.Audio — コードだけで音を作って鳴らすモジュール

`ISound` はどちらのバックエンド（DxLib/raylib）でもファイルパス起点なので、このモジュールは
**合成した PCM を 16bit WAV にしてキャッシュディレクトリへ焼き、既存の `new Sound(path)` で鳴らす**方式を取っている。
`IGamePlatform` にも既存バックエンドにも一切手を入れていない。

```csproj
<ProjectReference Include="..\Audio\AstrumLoom.Audio.csproj" />
```
（`Game\AstrumLoom.GameUtil.csproj` は既にこの参照を持っているので、ゲーム側で追加設定は不要。）

```csharp
using AstrumLoom.Audio;

Audio.PrewarmAll();               // 起動時に全プリセットを焼いておく（任意。無くても初回再生時に自動で焼く）

// 毎フレーム、Draw の先頭で呼ぶこと（後述）。
Audio.Update();

Audio.Play(SfxId.Coin);
Audio.Play(SfxId.Shot, volume: 0.8, pitch: 1.1, pan: -0.3);
```

---

## 1. 効果音プリセット一覧（SfxId）

| SfxId | 音色の要点 |
| --- | --- |
| Shot | 矩形波、高→中音へ短く落ちるブリップ |
| ShotHeavy | 鋸波の低い衝撃 + 短周期ノイズの破裂を重ねた 2 層 |
| Hit | バンドパスのノイズ + 低い三角波の打撃音、2 層 |
| Explode | ホワイトノイズをローパスで下方掃引、ドライブ少々 |
| ExplodeBig | Explode 相当のノイズ層 + サブベースの正弦波スイープ、2 層・長め |
| Damage | 鋸波を指数カーブで下降、デチューンで濁らせた不協和音 |
| Pickup | 三角波が低→高へ素直に上がる |
| Coin | 高い矩形ブリップを 2 発、少し遅らせて重ねる（定番のコイン音） |
| PowerUp | 鋸波が長く上昇、ビブラート＋ローパス掃引 |
| Decide | 短い矩形チャープ（上昇） |
| Cancel | 短い矩形チャープ（下降） |
| Cursor | 極短い三角波のクリック |
| Warning | 矩形の持続音を GateHz で刻んでビープ化 |
| Charge | 鋸波が指数的に長く上昇、フィルタ共振を効かせて溜め感を出す |
| Laser | パルス列を指数下降＋リングモジュレータでチリチリさせる |
| Whoosh | ピンクノイズをバンドパスで掃引、風切り音 |
| Bounce | 正弦波を指数上昇、短く弾む |
| Break | 短周期ノイズ + 三角ノイズの破砕音、2 層 |
| Heal | 三角波 + 緩い FM とビブラート、柔らかい上昇 |
| Alarm | 矩形の持続音を GateHz で刻んだ警報 |
| Chime | 金属音波形 + FM、長い減衰の鐘 |
| Thud | 正弦波の低い衝撃、ドライブで芯を作る |
| Sparkle | 金属音波形を高音・デチューンで 2 層重ねた煌めき |
| Zap | 矩形を指数下降、リングモジュレータ＋ビットクラッシュで電撃感 |

すべて音色として完全に異なることを `AudioSelfCheck` がサンプル列のハッシュ突き合わせで検算している
（ピーク値・実効値だけでは矩形波のデューティ違いなどを見分けられないため、それだけに頼っていない）。

---

## 2. SfxDesc パラメータ表

`Audio\Synth\SfxDesc.cs`。**追加パラメータは「0（または `FilterKind.None`）なら無効」**という規約なので、
`SfxDesc.Default with { ... }` で差分だけ書けば、既存の音を壊さずに新しい項目を増やせる。

| 項目 | 意味 | 無効値 |
| --- | --- | --- |
| `FreqStart` / `FreqEnd` / `FreqSweepCurve` | 周波数の開始/終了と掃引カーブ（Linear/Exponential） | — |
| `Wave` / `Duty` | 波形の種類とデューティ比（Square/PulseTrain のみ有効） | — |
| `Duration` | 音の長さ（秒） | — |
| `Envelope`（`Adsr`） | Attack/Decay/SustainLevel/SustainTime/Release（秒） | — |
| `Volume` | 音量倍率 | — |
| `FmRatio` / `FmIndex` / `FmIndexDecay` | 2op FM のモジュレータ比・変調指数・指数の減衰 | `FmIndex = 0` |
| `DetuneCents` | もう1系統、指定セントずらして重ねる | `0` |
| `RingModHz` | リングモジュレータの周波数 | `0` |
| `Drive` | tanh 歪みの強さ（0..1） | `0` |
| `CrushBits` | ビットクラッシュの量子化ビット数 | `0` |
| `CrushRateDivide` | サンプル&ホールドの間引き倍率 | `0`（`1`以下も無効） |
| `Filter` / `FilterCutoffStart` / `FilterCutoffEnd` / `FilterResonance` | フィルタ種別と掃引・共振 | `FilterKind.None` |
| `GateHz` / `GateDuty` | 刻み再生（トレモロ的なオンオフ） | `GateHz = 0` |
| `VibratoHz` / `VibratoDepthCents` | ビブラートの速さと深さ | `VibratoHz = 0` |
| `Pan` | プリセット側の基準パン（-1..1） | `0` |

**波形（Wave）10種**: Sine / Square / Saw / Triangle / WhiteNoise / PinkNoise / Metallic（非整数倍音） /
ShortNoise（LFSR、8bit風） / TriangleNoise（三角波でホールドしたノイズ） / PulseTrain（短いパルス列）。

最大 3 層まで `SfxLayers` で重ねられる（層ごとに独立した `SfxDesc` とオフセット秒・音量）。層合成後は
tanh でソフトクリップして -1..1 に収める。

---

## 3. 「作りたい音」からの逆引き表

| 作りたい印象 | 使う道具 |
| --- | --- |
| 硬い/電子的なビープ | `Wave = Square`、Duty 0.5 |
| 柔らかい/丸い音 | `Wave = Triangle` か `Sine` |
| ブザー・攻撃的な音 | `Wave = Saw`、`Drive` を少し足す |
| 金属的な鐘・衝突音 | `Wave = Metallic`、`FmIndex` を軽く足す |
| 8bit風のノイズ | `Wave = ShortNoise` |
| 爆発・衝撃の芯 | `WhiteNoise` + `Filter = LowPass` を高→低へ掃引 |
| 風切り音・シュー | `PinkNoise` + `Filter = BandPass` を掃引 |
| レーザー・ビーム | 高→低の指数掃引 + `RingModHz` |
| 電撃・グリッチ | `RingModHz` + `CrushBits`/`CrushRateDivide` |
| きらめき・魔法 | `Metallic` を 2 層、`DetuneCents` と `VibratoHz` |
| 警報・ビープ連打 | `GateHz` で刻む（`Duration` は長めに） |
| 溜め・チャージ音 | `Duration` を長く、周波数を指数上昇、`FilterResonance` を高めに |
| 分厚い/デチューンされた音 | `DetuneCents` を 5〜20 程度 |
| コイン・決定音の「軽さ」 | 短い `Duration`、`Envelope` の Attack をほぼ 0、Decay を短く |

---

## 4. BGM の書き方

`Audio\Bgm\Score.cs` / `Instrument.cs` / `Sequencer.cs`。

```csharp
using AstrumLoom.Audio.Bgm;

var lead = new BgmTrack
{
    Instrument = InstrumentKind.SquareLead,
    Volume = 0.8,
    Pan = -0.1,
    EchoDelayBeats = 0.75,   // 0 なら無効
    EchoGain = 0.3,
    Notes = BgmBuild.Melody(
    [
        ("C4", 1), ("E4", 1), ("G4", 1), (null, 1),   // null = 休符
        ("A4", 2), ("G4", 2),
    ]),
};

var kick = new BgmTrack
{
    Instrument = InstrumentKind.Kick,
    Volume = 0.9,
    Notes = BgmBuild.DrumPattern("x...x...x...x...", 0.25), // 16分音符、'X' はアクセント
};

var score = new BgmScore
{
    Bpm = 128,
    Beats = 16,     // 1ループぶんの拍数（4/4で4小節なら16）
    Swing = 0.15,   // 0 で無効
    Tracks = [lead, kick],
};

Audio.PlayBgm(score, volume: 0.7);
```

**声部の音色（8種+）**: SquareLead / PulseLead / SawBass / TriangleBass / Bell / Pad / Pluck / Organ
**ドラム（6種+）**: Kick / Snare / HihatClosed / HihatOpen / Tom / Crash
（`InstrumentKind.IsDrum(kind)` で判定できる。ドラムは `BgmNote.MidiNote` を無視して固定の音を鳴らす。）

音名は `"C4"`（中央ハ、MIDI 60）のように書く。シャープは `"A#3"`、フラットは `"Bb2"`。

`Sequencer.Render` は**継ぎ目なくループする長さで焼く**。ループ長より最大2秒長めのバッファへ全音を描き込み、
はみ出した末尾（リリースの余韻やエコー）をループ先頭へ折り返して加算してから、ちょうどループ長に切り詰めている。
これをしないと、減衰の長い Bell/Pad やエコー付きトラックで「2周目の頭で本来聞こえるはずの残響」が欠ける。

---

## 5. スレッドの作法

`Sandbox\SoundDemo.cs` と同じ約束を踏襲している。

- raylib の音 API はメインスレッド専用。`GameConfig.UseMultiThreadUpdate = true` だと Update は別スレッドで回るので、
  `Audio.Play()` / `Audio.PlayBgm()` はジョブを `ConcurrentQueue<Action>` に積むだけにしてある。
- 実際に `Sound.Play()` / `Sound.PlayStream()` を叩くのは、**Draw の先頭から毎フレーム呼ぶ `Audio.Update()`**の中。
  呼び忘れると効果音もBGMも一切鳴らない。
- `Loop = true` でも `Play()` 一発では2周目が来ない。`Audio.Update()` が毎フレーム `PlayStream()` を呼んでいる。
- `new Sound(path)` は非同期ロードなので、`Play()` 要求時点でまだ `Enable == false` のことがある。
  `Audio` はそのぶんを `_pending` に積んで毎フレーム再挑戦し、2秒（`PendingTimeoutSeconds`）で諦める
  （無限に溜め続けない）。

```csharp
public override void Draw()
{
    Audio.Update();   // ★ 必ず先頭で
    // ...通常の描画...
}
```

---

## 6. キャッシュの仕組み（AudioCache）

- 記述子（`SfxLayers` / `BgmScore`）の全パラメータを安定した順序でテキスト化し、SHA256 でハッシュ化してファイル名にする。
  `double` は `"R"`（往復可能表現）で書き出すので、同じ値からは常に同じ文字列 → 同じハッシュになる。
- 出力先は `<AppContext.BaseDirectory>\.audiocache\<hash>.wav`。既に存在すれば合成をスキップする
  （2回目以降の起動コストはファイル存在チェックだけ）。
- ディレクトリを作れない環境では一時ディレクトリへフォールバックし、それも駄目なら `Log.Warning` を出して
  以降は無音（ボイス配列が空のまま）として扱う。**例外を投げてゲームを落とすことはしない。**
- 合成に使う乱数（ノイズ波形）は `SfxDesc` の内容から決定論的に導出したシードを使う。
  `HashCode.Combine` や `string.GetHashCode` はプロセスごとに変わりうる（ハッシュランダム化）ため使っていない。
  ここが揺れると同じ音が毎回違う波形になり、キャッシュそのものが破綻する。

---

## 7. 自己検算（AudioSelfCheck）

`AudioSelfCheck.Verify(out string detail)` はゲームを起動せずに（描画にもバックエンドにも依らず）呼べる。

- 全プリセットを合成し、**サンプル列のハッシュ**で完全一致を検出する（音色の配線し忘れを検知する本命）。
- NaN/Inf は**潰す前に**件数を数える（潰した後に数えると常に0件になり検算の意味が無くなる）。
- 全プリセットが無音でない（実効値が閾値以上）・クリップしていない（`|x| <= 1.0` 近辺）。
- プリセット間のレベル差が極端でないこと（実効値が全プリセット中央値の 1/8 未満なら赤）。
  ある音だけ Volume を極端に落とすと他の音に埋もれて実質無音になる、という事故を検知するため。
- フィルタが共振最大でも発散しない（インパルス+ノイズを共振1.0で流し続けて確認）。
- BGM がループ長ちょうどで焼けていること、ループ継ぎ目の段差（末尾サンプルと先頭サンプルの差）が小さいこと。
- WAV の往復（`float[] → WAV → 読み戻し`）が 16bit 量子化誤差の範囲に収まること。

失敗内容はすべて `detail` に日本語で列挙される。
