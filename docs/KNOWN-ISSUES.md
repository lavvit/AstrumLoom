# 既知の問題

2026-08-13〜14 にコード全体を領域ごとに読み、1 件ずつ独立した検証を通した結果です。
ここに載っているのは「実際に壊れる経路をコードで追えた」ものだけです。

- ✅ … 修正済み
- ⬜ … 未着手

## 2026-08-20 の一斉修正

未着手だった 65 件のうち **58 件を修正**しました。両バックエンド（RayLib / DxLib）で
`--selftest` が全件 PASS、素の MT ループ 20 秒でも致命エラーなしを確認済みです。

残る ⬜ の 7 件は、機械的に直すと別の場所が静かに壊れるので意図的に残してあります:

| 残した項目 | 残した理由 |
| --- | --- |
| `Drawing.DefaultScale` 系 3 件（Core / DXLib / RayLib） | 「拡大率をどの層で誰が適用するか」を三者で決め直す話。片方だけ直すと既存の絵が全部ずれる |
| `IInput` の Buffer() と Update() が別ループ回数で回る | ループ構造そのものの変更で、全バックエンドの入力実装を巻き込む |
| `IMouse` にだけ Buffer() が無い | 同上。IMouse に Buffer() を足すインターフェース変更が要る |
| 呼び出しごとの edgecolor が Edge>0 のフォントでしか効かない | Core 側は EdgeColor を正しく渡せている。直すのはバックエンドの縁取りハンドル生成側で、両方に一時ハンドルの仕組みが要る |
| Sandbox のオーバーレイの分岐が成立しない | `LoadCheckScene` がどこからも生成されないデッドコード。実害ゼロで、消すか繋ぐかは Sandbox の設計判断 |

## 第一次監査で確定したもの

### 重大（13 件 / 修正済み 13 件）

#### ✅ Log.LogMessages が素の List<> のまま複数スレッドから読み書きされる

`Core/Log.cs:7`

UseMultiThreadUpdate=true の状態で更新スレッドが Log.Write した瞬間に描画スレッドが Log.Draw を実行すると、`InvalidOperationException: Collection was modified; enumeration operation may not execute` が出る。Log.Draw は Game.cs:174 の try の中なので HandleFatal("Draw") に流れ、ログを 1 行出しただけでアプリが致命エラー画面に落ちる。さらに Add 同士が競合すると List 内部配列の書き潰し（ログの消失）や ArgumentOutOfRangeException も起きる。

#### ✅ Sleep.Update が更新スレッドから毎フレーム Platform.SetVSync を呼ぶ（ウィンドウ API のスレッド違反 / DxLib では毎フレーム垂直帰線待ち）

`Core/AstrumCore.cs:272`

RayLib バックエンド＋UseMultiThreadUpdate=true で、放置して SleepDurationMs を超える／VSync をトグルすると、更新スレッドから glfwSetWindowAttrib 相当が呼ばれて未定義動作（GLFW のスレッドアサート、またはウィンドウ状態の破損）になる。DxLib バックエンドでは VSync=true のとき更新スレッドが SetVSync→WaitVSync(1) で毎フレーム最大 16ms 止まり、更新レートがモニタ refresh に張り付いて描画と二重待ちになる。

#### ✅ TextEnter.IsCancel が二度と false に戻らず、以後テキスト入力が永久に死ぬ

`Core/Input.cs:269`

一度でも KeyInput.Cancel() を呼ぶと、その後 ActivateText() で入力を開始しても、次フレームの GetText()/Enter() が呼ぶ TextEnter.Update で必ず 481 行が効いて即キャンセルされる。プロセスが終わるまでテキスト入力が一切使えなくなる（AstrumCore.Platform.TextInput.IsCancel を外から false に戻す以外に復帰手段が無い）。

#### ✅ 入力文字が変わっていないと Enter の確定が握り潰される

`Core/Input.cs:241`

Sandbox/Input.cs:113-127 の流れそのままで再現する。T キーで空文字列の入力を開始し、何も打たずに Enter を押すと、KeyInput.Enter が false のまま _textActive が true に張り付き、一方 KeyInput.Typing は false になる。以後 T を押しても Sandbox/Input.cs:113 の !_textActive で弾かれ、二度と入力を開始できない。既存文字列を編集して元に戻した場合も同じ。

#### ✅ checkReconversion が長い方の長さで両方の配列を添字アクセスして例外、握り潰して 0 を返す

`Core/Text.cs:245`

GetJpEncoding は 50KB（maxSize、Text.cs:70）だけ読むので、切り口が多バイト文字の途中や先行バイトに当たると再変換後の長さが変わる。この時 133-135 行の「末尾以外が同一なら同一とみなす」救済は一致長を必要とするのに 0 しか来ないため効かず、138 行で null を返す。結果 GetEncoding が 66 行で shift_jis にフォールバックし、50KB を超える BOM 無し UTF-8 の日本語テキストが全文文字化けする。

#### ✅ Drawing.MakeTexture が描画内容を捨てる完全なスタブ

`Core/Drawing.cs:323`

`var t = Drawing.MakeTexture(() => Drawing.Box(0,0,64,64,Color.Red), 64, 64);` を実行すると、スクラッチ実行で確認したとおり W=0 H=0 Enable=False Path='' が返る。以後 t.Draw() は何も描かず、t.Expand(...)/t.DrawSize(...) は 0 除算で Scale=∞ になる。例外は出ないので気付けない。

#### ✅ Texture.DrawSize がサイズ指定を無視し、さらに Scale を破壊する

`Core/Texture.cs:68`

128x128 のテクスチャに `tex.Opacity = 0.5; tex.Point = ReferencePoint.Center; tex.DrawSize(100, 100, new Size(32, 32));` を呼ぶと、32x32 ではなく 128x128 が、左上基準・不透明で描かれる。さらにその後の `tex.Draw(0,0)` は Scale が 0.25 のままになり縮んで描画される。

#### ✅ Drawing.Graphics が null を非 null として返す（唯一のコンパイル警告 CS8603）

`Core/Drawing.cs:7`

AstrumCore.Boot より前（Main での config 組み立て中、Scene の static 初期化子、ユニットテスト）に `Drawing.DefaultFont` や `Drawing.TextSize("x")` を呼ぶと NullReferenceException。スクラッチ実行で両方とも NRE を確認済み。

#### ✅ テクスチャのハンドルが永久に解放されない（Dispose が丸ごと no-op）

`DXLib/DxLibTexture.cs:63`

シーン切替のたびに new Texture(path) / Dispose() を繰り返すと、DeleteGraph が一度も呼ばれず VRAM 上のグラフィックハンドルが増え続ける。状態も State_Disposed にならないので Enable が true のまま残り、破棄したはずのテクスチャが描画できてしまう。最終的に LoadGraph が失敗して以後のテクスチャが真っ黒になる。

#### ✅ サウンドのハンドルが永久に解放されない（同上）

`DXLib/DxLibSound.cs:47`

BGM/SE を読み直すたびにサウンドメモリが解放されず蓄積する。ストリーミング未対応（後述）で全曲がメモリ展開されるため、曲を数十回切り替えると数百MB単位で増える。

#### ✅ UTime（Update 用時計）の TargetFps が誰にも設定されず、更新スレッドが無制限に回る

`DXLib/DxLibPlatform.cs:51`

`new GameConfig { TargetFps = 60, UseMultiThreadUpdate = true }` で DxLibPlatform を使うと、GameRunner.UpdateLoop（Core/Game.cs:101-115）が待ちなしで回り、game.Update(dt) が毎秒数千〜数万回呼ばれる。CPU 1コアが張り付き、フレーム単位でカウントしているゲームロジックが数百倍速で進む。同じ config を RayLib バックエンドにすると 60Hz に収まるため、バックエンドを変えると挙動が変わる。

#### ✅ UTime にも TargetFps を入れるためシングルスレッド時に FPS が半減する

`RayLib/RayLibPlatform.cs:42`

GameConfig { TargetFps = 60, UseMultiThreadUpdate = false } で RayLib バックエンドを起動すると、Update と Draw が各 16.6ms 待つため実測 30FPS になる（同設定の DxLib は 60FPS）。

#### ✅ TapMoveTolerance により最初の押下位置から 3px 離れると以後クリックが取れない

`RayLib/RayLibControll.cs:45`

起動後 (100,100) で左クリック→離す→(400,300) へ移動して左クリック、で Mouse.Push/Hold/Left が一切 true にならない。同様に押したまま 3px 以上ドラッグすると押下中でも Released→None になりドラッグ操作が成立しない。

### 中（31 件 / 修正済み 27 件）

#### ✅ 初期化中（game.Initialize / Scene.Start）の例外だけ致命エラー画面に乗らず、素の未処理例外になる

`Core/Game.cs:24`

シーンの Enable() 内でフォントやテクスチャの読み込みに失敗して例外が出ると、FatalErrorInfo も致命エラー画面も出ず、AstrumCore.Boot（AstrumCore.cs:50）を突き抜けて Main まで飛ぶ。ウィンドウは using の Dispose で即座に閉じ、実機ではコンソール出力を見ない限り原因が何も分からない。

#### ✅ Log.Save に相対ファイル名を渡すと ArgumentException で落ちる（try の外）

`Core/Log.cs:37`

`Log.Save("Log.txt")` または `Log.Save("")` を呼ぶと `ArgumentException: Path cannot be the empty string or all whitespace.` が投げられる。終了処理でログを保存しようとした瞬間に、保存もされず例外で落ちる。

#### ✅ LogMessages が無制限に増え続け、Write も Draw も全件走査する

`Core/Log.cs:18`

毎フレーム 1 行ログを出すゲームを 1 時間動かすと 21.6 万件になり、Log.Write 1 回が 21.6 万件走査、Log.Draw が毎フレーム 21.6 万件の LINQ になってフレームレートが実用外まで落ちる。メモリも解放されない。

#### ✅ Counter.Time が常に DateTime.MinValue を返す（AddMilliseconds の戻り値を捨てている）

`Core/Time.cs:305`

どんな Value でも Counter.Time は 0001/01/01 00:00:00 を返す。経過時間表示に使うと常に同じ値が出る（TimeConvert 経由の CTime は正しく動くので、片方だけ壊れていて気づきにくい）。

#### ✅ Counter が終端に達しても while が回り続け、Ended が 1 回の Tick で何度も発火する

`Core/Time.cs:118`

`new Counter(0, 10, 1000)`（1ms 刻み・非ループ）を 50ms 間 Tick しなかったあとに Tick() すると、Ended が 1 回ではなく約 40 回発火する。Ended で「次のシーンへ遷移」や「音を鳴らす」を書いていると多重発火する。

#### ✅ Counter を長時間 Tick しないと次の Tick で数百万回スピンする

`Core/Time.cs:101`

Interval=1（1µs）のループカウンタを 10 秒間 Tick しないでおく（ウィンドウ最小化、ブレークポイント停止、重いロード）と、次の Tick() で 1000 万回ループし、その間 Looped イベントが 1 万回発火する。フレームが数百 ms〜秒単位で固まる。

#### ✅ FpsCounter._times を更新スレッドと描画スレッドが同時に触る（Max/Min には保護すら無い）

`Core/FPS.cs:49`

マルチスレッド更新時に FpsCounter.ToString() や GetMaxFPS/GetMinFPS を UpdateFPS に対して呼ぶと、List.ToArray() が内部 _size と _items の不整合を踏んで ArgumentException（Destination array is not long enough）を投げ、そのまま HandleFatal に流れる。GetFPS 側は例外を握り潰す代わりに 0 を返すので、FPS 表示が不定期に 0 に落ちる。

#### ✅ Sleep の初期値 0 が「OS 起動時刻」を意味してしまい、起動直後からスリープ状態になりうる

`Core/AstrumCore.cs:255`

PC を 30 秒以上（Sandbox は SleepDurationMs=30000）動かしている状態で、別ウィンドウにフォーカスがあるまま、あるいはウィンドウ生成直後でまだフォーカスが来ていない状態で起動すると、初回の Sleep.Update で即スリープ判定になり VSync が強制 ON、Sleeping=true のまま最初のキー入力かマウス移動まで動く。ベンチや自動テストのように誰も入力しない実行では最後までスリープモードのまま。

#### ✅ ドラッグ＆ドロップの取得をゲーム更新スレッドで行っている

`Core/AstrumCore.cs:16`

UseMultiThreadUpdate=true で RayLib バックエンドを使い、ファイルをドロップすると、更新スレッドの GetDroppedFiles() とメインスレッドの EndDrawing が競合して、ドロップが取りこぼされるか、解放済みの文字列配列を読んで文字化け／AccessViolation になる。

#### ✅ 同じシーンへ Change すると Enable 直後に自分自身を Disable して子が全消去される

`Core/Scene.cs:129`

「現在のシーンをリスタートする」目的で `Scene.Change(Scene.NowScene)` と書くと、Enable で組み立てた子シーンがその場で全部消え、以後 Update/Draw が空回りする（例外は出ないので原因が分かりにくい）。

#### ⬜ IInput の Buffer() と Update() が別スレッド・別ループ回数で回り、キー入力が落ちる

`Core/Input.cs:125`

UseMultiThreadUpdate=true で描画 144Hz・更新 60Hz にして 10ms 程度キーを叩くと、Buffer() が押下と解放の両方を _now に書いた後に Update() が走るため _state が 0 のままになり、Push も Left も一度も観測されない。入力が丸ごと消える（逆に更新が速い場合は Push が観測されるだけで実害は無いので、症状はフレームレート次第で変わる）。

#### ✅ 押下時間の Dictionary を更新スレッドで書きながら描画スレッドから読む

`Core/Input.cs:121`

UseMultiThreadUpdate=true にし、Sandbox/Input.cs:141 と同じように Draw() 内で KeyBoard.Draw() を呼びながらキーを連打する。Dictionary がリサイズしている最中のバケット配列を TryGetValue が読むため、InvalidOperationException、誤った値の返却、最悪 TryGetValue が戻らない（無限ループ）のいずれかが起きる。

#### ✅ Repeat() が「読むと消費される」クエリになっていて、同一フレームの2人目は必ず false

`Core/Input.cs:180`

2つの UI（例：メニューAとメニューB、あるいは Update 側と Draw 側）が同時に Key.Down.Repeat(150) を見ていると、先に評価された方だけがスクロールし、もう片方はキーを押し続けても永久にリピートを受け取れない。呼び出し順を変えると挙動が入れ替わるため原因も追いにくい。

#### ✅ TextEnter.Update の ESC キャンセル分岐が到達不能（Typing ゲートで常に false）

`Core/Input.cs:462`

枠組み側の ESC キャンセルが一切機能しない。ESC を自前で見ていない ITextInput 実装（Commit()/Cancel() だけ実装したバックエンドや将来の実装）を差すと、ESC を押しても入力から抜けられず、残る手段は KeyInput.Cancel() だけ。そしてそれを呼ぶと本レポートの1件目のバグ（IsCancel が戻らない）を踏む。

#### ⬜ IMouse にだけ Buffer() が無く、マウスだけ更新スレッドからバックエンドを直接サンプリングする

`Core/Mouse.cs:16`

UseMultiThreadUpdate=true にして描画1回の間に更新が2回走ると、raylib が EndDrawing まで保持しているホイール値を 2 回読むため、1ノッチのスクロールが WheelTotal に 2 回加算され、Mouse.Wheel != 0 の判定（Sandbox/Input.cs:63）も 2 回成立して 1 回のスクロールが 2 回分として扱われる。加えて GLFW の入力状態をメインスレッド外から読むことになる。

#### ✅ IJoyPad.Index の基準がバックエンドで違う（RayLib は 0 始まり、DxLib は 1 始まり）

`Core/Pad.cs:15`

pad.Index を設定ファイルに保存して次回 Pad.GetJoyPad(saved) で復元するコードは、RayLib では正しく動くが DxLib では 1 つずれたパッドを返すか null になる。Pad.List の一覧表示も DxLib だけ 1 始まりになり、Sandbox/Input.cs:172-176 のように配列添字と並べて出すと番号がずれる。

#### ✅ Text.Save が CodePages プロバイダ未登録のまま Encoding.GetEncoding を呼ぶ

`Core/Text.cs:40`

起動してからまだ一度も Text.Read / Text.GetEncoding を通していない状態で Text.Save(list, path, "shift_jis") を呼ぶと ArgumentException（'shift_jis' is not a supported encoding name）で落ちる。先に何かファイルを読んでいたかどうかで成否が変わるため、再現条件が実行順に依存する。

#### ⬜ Drawing.DefaultScale が中途半端に配線され、バックエンドで挙動が食い違う

`Core/Drawing.cs:327`

`Drawing.DefaultScale = 2.0;` にして `tex.Draw(100,100); Drawing.Box(100,100,50,50);` を描くと、DxLib では テクスチャが (200,200) に 2 倍サイズ、RayLib では (200,200) に等倍サイズ、Box は両方 (100,100) に等倍。同じコードで 3 通りの結果になる。

#### ✅ Drawing.Point が既定の太さで何も描かない（int 除算で半径 0）

`Core/Drawing.cs:13`

`Drawing.Point(100, 100, Color.White);`（thickness 既定 1）が完全に不可視。thickness=3 を指定しても半径 1 の円になり、指定した太さの 1/3 程度の点しか出ない。

#### ✅ Texture.Draw(Point, DrawOption?) が option 引数を黙って捨てる

`Core/Texture.cs:35`

`tex.Draw(new Point(10,20), new DrawOption { Opacity = 0.3, Color = Color.Red });` が不透明度も色も反映されず、インスタンスの既定 Option でそのまま描かれる。

#### ✅ HSBColor が負の色相を折り返さず、Shift(h:負) が別の色になる

`Core/Color.cs:176`

スクラッチ実行で確認: `Color.FromHSB(-30,1,1)` は R:255 G:0 B:0（純赤）を返す。正しくは 330 度＝R:255 G:0 B:127。同じく `Color.Red.Shift(h:-30)` が赤のまま変化せず、`Color.Red.Shift(h:330)` だけが期待どおり R:255 G:0 B:127 になる。

#### ✅ 色 1 個の Gradation は GetColor が必ず例外を投げる

`Core/ColorExtend.cs:277`

スクラッチ実行で確認: `new Gradation(new[]{ Color.Red }).GetColor(0.5f)` および `GetColor(0f)` が InvalidOperationException。Drawing.Gradation（Drawing.cs:228）は 1 列ごとに GetColor を呼ぶので、単色グラデーションを渡すと描画フレームの途中で例外になり GameRunner.HandleFatal（Game.cs:180）でゲームが落ちる。

#### ✅ Drawing.Polygon に空の点列を渡すと IndexOutOfRangeException

`Core/Drawing.cs:218`

スクラッチ実行で確認: `Drawing.Polygon(Array.Empty<(double,double)>())` が IndexOutOfRangeException。動的に生成した頂点リスト（可視判定で全部落ちたなど）が空になった瞬間にゲームが落ちる。

#### ✅ Color.Parse(int) がアルファ 0x80 以上の ARGB を必ず不透明に潰す

`Core/Color.cs:156`

スクラッチ実行で確認: `Color.Parse(unchecked((int)0x80112233))` が R:17 G:34 B:51 A:255 を返す。期待は A:128。半透明の色定数を ARGB の整数リテラルで持たせている箇所が全部不透明になる。

#### ✅ Drawing.Gradation の汎用パスがピクセル数ぶんのドローコールと色空間変換を発行する

`Core/Drawing.cs:307`

`Drawing.Gradation(0, 0, 640, 360, grad, rotate: 45)` を毎フレーム呼ぶと 230,400 回の GetColor と 230,400 回の DrawBox が 1 フレームに発生し、60fps が維持できない。回転を 0 度にした途端 640 回に減るので、原因が回転角だと気付きにくい。

#### ✅ config.VSync = true のとき垂直同期が永久に有効化されない

`DXLib/DxLibPlatform.cs:34`

config.VSync = true で起動すると、Sleep.Update() が毎フレーム Platform.SetVSync(true) を呼ぶ（Core/AstrumCore.cs:272）が実際の DxLib 側は VSync 無効のまま。ScreenFlip が待たないのでティアリングが出続け、代わりに Update スレッドが WaitVSync(1) でモニタ周期にブロックされるという逆の挙動になる。

#### ✅ VSync 有効時の目標 FPS 計算が両分岐とも monitorFps になっている

`DXLib/DxLibPlatform.cs:128`

config.TargetFps = 30、144Hz モニタの環境で VSync を on にする（または SleepDurationMs 経過で Sleep.Update が自動的に SetVSync(true) を呼ぶ）と、Time/UTime.TargetFps が 30 ではなく 144 に書き換わり、放置から復帰するまでゲームが 4.8 倍速で進む。

#### ✅ 毎秒 6 回ほどコントローラの再列挙（ReSetupJoypad）が走る

`DXLib/DxLibPad.cs:20`

60FPS なら毎秒およそ 6 フレーム分（各秒の先頭 100ms）で ReSetupJoypad が呼ばれ、DirectInput/XInput のデバイス再初期化が発生する。1 秒周期でフレームスパイクが出て、再列挙の瞬間にパッド入力が取りこぼされる。FPS が上がるほど呼び出し回数も増える（無制限なら 1 秒あたり数百回）。

#### ⬜ Drawing.DefaultScale をテクスチャだけが適用し、図形・文字は無視する

`DXLib/DxLibTexture.cs:139`

`Drawing.DefaultScale = 2.0` にすると、スプライトだけが 2 倍の大きさ・2 倍の座標に飛び、同じ座標に描いている Drawing.Box の枠や Drawing.Text のラベルは元の位置・大きさのまま残る。UI とスプライトが完全にずれる。

#### ✅ Loop = true でも Play() ではループしない／シーク位置から再生できない

`DXLib/DxLibSound.cs:190`

`sound.Loop = true; sound.Play();` は 1 回鳴って止まる（PlayStream を毎フレーム呼ばない限りループしない）。また `sound.Time = 30000; sound.Play();` は 30 秒地点ではなく先頭から再生される。

#### ✅ DisposeTx が成功パスで return true を落としており毎回 Failed 扱いになる

`RayLib/RayLibTexture.cs:88`

ファイルから読んだテクスチャを Dispose するたびに「RayLibTexture Dispose returned false.」の警告が出て、状態が Disposed ではなく Failed になり IsFailed が true に化ける。

### 軽微（13 件 / 修正済み 11 件）

#### ✅ 非アクティブ中は移動差分を更新しないので Speed が固まり、復帰時に跳ねる

`Core/Mouse.cs:41`

マウスを速く動かしている最中に Alt+Tab で他ウィンドウへ移ると Speed > 200 のまま固定され、Mouse.Draw の高速移動エフェクト（Mouse.cs:229-239 の白い全画面十字）が復帰まで描かれ続ける。復帰した瞬間も、非アクティブ中に画面端から端まで動かした分の差分が 1 フレームに乗って同じエフェクトが暴発する。

#### ✅ IsTouchPad を累積値 WheelTotal の小数部で判定していて、貼り付いたり取りこぼしたりする

`Core/Mouse.cs:75`

タッチパッドで少しスクロールした後に普通のホイールマウスへ持ち替えても IsTouchPad は true のまま返り続ける。逆に +0.5 を 2 回だけスクロールしたタッチパッドは WheelTotal が 1.0 になり false を返す。さらに float に無限に足し込むため長時間セッションでは小数部そのものが量子化で消える。

#### ⬜ 呼び出しごとの edgecolor は FontSpec.Edge>0 のフォントでしか効かない

`Core/Font.cs:61`

フォントを差し替えていない状態で `Drawing.Text(10, 10, "HP", Color.White, Color.Black);` と書いても縁取りが一切出ない。エラーもログも出ず、背景に同化した文字が読めないまま気付けない。

#### ✅ Circle の 2 つのオーバーロードで塗り／枠線の既定が逆

`Core/Drawing.cs:88`

`Drawing.Circle(100, 100, 8, Color.Red)` は塗り円、`Drawing.Circle(new LayoutUtil.Point(100,100), 8, Color.Red)` は輪郭だけの円になる。座標を Point に置き換えるリファクタをしただけで見た目が変わる。

#### ✅ LCM が int で静かにオーバーフローし、GCD は int.MinValue で例外

`Core/Math.cs:19`

スクラッチ実行で確認: `MathExtend.LCM(65536, 65535)` が 65536 を返す（正解は 4294901760）。互いに素なのに最小公倍数が片方より小さいという矛盾した値が、例外なしで下流に流れる。`MathExtend.GCD(int.MinValue, 0)` は OverflowException。

#### ✅ BigNum が負の値で指数表現を失い、桁が展開されてしまう

`Core/Math.cs:502`

スクラッチ実行で確認: `new BigNum(-2, 5).ToString()` が "-2000000000000000.00"（期待は "-2.00Q"）、`new BigNum(-2500000.0).ToString()` が "-2500000.00"（期待は "-2.50M"）。正の値は "2.00Q" / "2.50M" と正しいので、負債やマイナス収支を表示した瞬間だけ桁区切り表記が壊れる。

#### ✅ SystemFontResolver.Resolve が bold=false のときに Bold 実体を選びうる

`Core/Font.cs:209`

`FontHandle.Create("Arial", 16)` が arial.ttf ではなく arialbd.ttf を掴む環境がある。同様に "Yu Gothic" が "Yu Gothic UI Semilight" に一致しうる。実行環境のフォント登録順でしか結果が決まらず、開発機と配布先で字面が変わる。

#### ✅ 縁取り用フォントハンドル(_edgehandle)が Dispose で解放されない

`DXLib/DxLibFont.cs:140`

`FontHandle.Create(name, 24, edge: 2)`（Sandbox/Program.cs:13 と同じ形）で作ったフォントを Dispose しても DxLib のフォントハンドルが 1 個残る。サイズ違いの縁取りフォントを動的に作り直すコード（テキストサイズをアニメーションさせる用途など）ではハンドルが上限まで増える。

#### ✅ 回転中心(Position)に Math.Abs をかけていて負の基準点が指定できない

`DXLib/DxLibTexture.cs:137`

画像の外側を軸に回したくて `texture.Position = new Point(-16, 0)` を指定すると、DxLib では +16 の位置（画像内側）を軸に回転し、RayLib では -16 で回転する。同じコードでバックエンドごとに違う絵になる。

#### ✅ GetFrequency のフォールバックがコピペミスで常に 1 を返す

`DXLib/DxLibSound.cs:178`

GetFrequencySoundMem が 0 以下を返すサウンド（読み込み直後や取得できない形式）で Frequency が 1 または int.MinValue になり、Update() の `_speed = (float)GetFrequency() / Frequency;`（121行目）が 44100 倍などの無意味な値になる。Sound.Speed / Pitch の表示・利用が壊れる。

#### ✅ LoadSound の streaming 引数が完全に無視される

`DXLib/DxLibSound.cs:11`

`platform.LoadSound("bgm.ogg", streaming: true)` としても全曲が非圧縮で常駐メモリに展開される。5 分の 44.1kHz ステレオで約 50MB。曲数が増えるとメモリを食い切る。

#### ✅ フォント名が空文字のとき既定フォントへフォールバックしない

`DXLib/DxLibFont.cs:170`

Drawing.DefaultFont（IFont 未設定時のフォールバック、Core/Drawing.cs:156）が GDI のフェイス名なしマッチで選ばれた任意のフォントになる。日本語グリフを持たないフォントに当たると、フォント未指定の Drawing.Text が日本語で豆腐になる。

#### ✅ Vibrate の pan（左右振り分け）が計算だけして捨てられている

`DXLib/DxLibPad.cs:140`

`pad.Vibrate(pan: -1f, strength: 1f, length: 200f)`（左だけ振動させたい）としても左右同じ強さで振動する。pan をどの値にしても結果が変わらない。

---

## 実装中に見つけて直したもの

上の監査とは別に、デバッグ機構を作りながら踏んだものです。

- ✅ `Core/Randomize.cs` `Seed()` が `lock (_random)` した直後に `_random` を差し替えており、
  以後のロックが別インスタンスを掴むため排他になっていなかった。専用のロックオブジェクトに変更。
- ✅ `RayLib/RayLibGraphic.cs` スクリーンショットが必ず真っ黒になっていた。
  raylib は描画命令をバッチに溜めて `EndDrawing` で流すので、フレーム途中で読むと
  `ClearBackground` 直後の状態を読んでしまう。`Rlgl.DrawRenderBatchActive()` で先に流すようにした。
  なお描画自体は正常で、壊れていたのはキャプチャだけだった。
- ✅ `Core/Debug/DebugSession.cs` `--shot-every` を描画スレッドで判定していたため、
  固定ステップのキャッチアップで 1 描画フレームに論理フレームが複数進むと倍数を飛ばして撮り逃していた。
  論理フレーム側で判定するように移動。
- ✅ `Core/Debug/InputCapture.cs` 入力の記録を「入力確定より前」にコミットしていたため、
  記録が 1 フレームずれて再生が合わなかった。BeginFrame / EndFrame に分離。
- ✅ `Core/AstrumCore.cs` `AstrumCore.VSync` セッターが更新スレッドから直接 `Platform.SetVSync` を
  叩いていた（ウィンドウ API はメインスレッド専用）。`RequestToMainThread` 経由に変更。
- ✅ `DXLib/DxLibInput.cs` `DxLibTextInput.Update()` が `Text.Contains('')` と
  `Text[..Text.IndexOf('')]` で `Text` プロパティを2回読んでいた。`Text` は毎回
  `GetKeyInputString` でネイティブの入力バッファを読み直す実装なので、2回の呼び出しの間に
  タイピングでバッファが変わりうる。1回目は制御文字を含んでいたのに2回目には無くなっていると
  `IndexOf` が `-1` を返し、`Text[..-1]` が `Substring` に負の長さを渡して
  `ArgumentOutOfRangeException` で Fatal Error に落ちる。実機でテキスト入力中に発生を確認した。
  1回だけ読んでローカル変数に固定するよう修正。
- ✅ `Sandbox/Input.cs`・`Sandbox/GameTemp.cs` 素の `List<T>`/`Queue<T>` を
  更新スレッド(Update)と描画スレッド(Draw)で共有しており、Draw が列挙中に Update が
  Clear/Add/RemoveAt すると `ArgumentOutOfRangeException` で落ちていた。`--selftest` は
  自動でロックステップ+単一スレッドになるためこの種のレースは検出できない。参照ごと差し替える
  （新しいリストを作ってフィールドへ代入）方式と `ConcurrentQueue<T>` へ変更して解消。

---

## 再検証した指摘

第一次監査では検証が最後まで走らず真偽不明だった 34 件を、現在のコードに対して判定し直したものです。

### いま実際に壊れるもの（26 件 / 修正済み 24 件）

2026-08-20 の一斉修正で 24 件を修正済みにしました。残る ⬜ の 2 件は
`Drawing.DefaultScale` の配線と Sandbox のデッドコードで、いずれも設計判断が要ります。

#### ✅ [medium] マルチスレッド構成で RequestToMainThread が 1 フレームに 1 件しか処理されない

`Core/Game.cs:89`

**壊れ方**: `UseMultiThreadUpdate=true`（Sandbox の既定）のとき、メインスレッドの描画ループは
`if (_mainThreadActions.TryDequeue(out var action))` と `if` 1 回ぶんしか掃かない。
更新スレッドから `AstrumCore.RequestToMainThread` で同一フレームに複数のアクションを積むと、
2 件目以降は次フレーム以降へずれ込み、積んだ量が多いほど遅延が伸びる。
更新スレッドは TargetFps=0 だと毎秒数万〜十万回まわるのに対し、描画ループは掃くのが 1 件/フレームなので、
定常的に積み続けるとキューは際限なく伸びる。

**根拠**: Core/Game.cs:88-96 のメインスレッドループは
`if (_mainThreadActions.TryDequeue(out var action)) { try { action(); } catch (...) { HandleFatal(...); } }`
であり `while` ではない。`_mainThreadActions`（Core/Game.cs:292）への Enqueue は
`RequestToMainThread`（Core/Game.cs:298-304）経由で、呼び出し元がメインスレッドでなければ
無条件でキューに積むだけ。Sandbox の音の見本帳（`Sandbox/SoundDemo.cs`）はこの経路を避けるため、
自前の `ConcurrentQueue<Action>` を Draw の中で `while (_soundJobs.TryDequeue(...))` と全部掃く実装にしている。

**直し方の方針**: メインスレッドループの `if` を `while` にして、そのフレームに積まれた分は
1 フレームで掃き切る（無限ループを避けるため、ループ開始時点のキュー件数だけ処理する形にするとよい）。

#### ✅ [high] Animation のコンストラクタが必ず NotImplementedException を投げる

`Extend/Animation.cs:9`

**壊れ方**: 誰かが`new Animation(path)`を呼んだ瞬間、Path代入直後の`Loop = isLoop;`でNotImplementedExceptionが飛び、Animationクラスは現状一度もインスタンス化できない。

**根拠**: Animation.cs:9-14 `public Animation(string path, bool isLoop = true) { Path = path; Loop = isLoop; Load(); }`。Loopプロパティは Animation.cs:56 `public bool Loop { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }`。よってコンストラクタ内の`Loop = isLoop;`がsetterを呼んだ瞬間に無条件で例外が飛ぶ。Time/Volume/Pan/Pitch/Speed/IsPlaying/Option/Play()/Stop()/PlayStream()/Draw()も同様に全てthrowのスタブ(48-62行)。なおリポジトリ全体をgrepしても`new Animation(`の呼び出しは現状どこにも無く、このAnimation.cs自体`git status`上untracked(未コミットの新規ファイル)であり、IMovie実装を作り始めた途中段階のスタブである可能性が高い。

**直し方の方針**: Loopを自動実装プロパティ(バッキングフィールド付き)にしてisLoopを保持するだけにする。他のIMovieメンバーも実装が追いつくまでは、少なくともコンストラクタから間接的に呼ばれるLoopのようなメンバーだけは動く状態にしておく。

#### ✅ [high] SkinList が Directory.Exists のガードより前に GetDirectories を呼ぶ

`Extend/Skin.cs:732`

**壊れ方**: "System" スキンフォルダがまだ存在しない状態(初回起動やスキン未配置のプロジェクト)で SkinList() が呼ばれると Directory.GetDirectories が System.IO.DirectoryNotFoundException を投げる。SkinList() は SkinPath プロパティ(DefaultSkin が空のとき SkinList()[0] を参照)経由でLoad()/LoadConfig()などSkinの主要機能全てから間接的に呼ばれるため、影響範囲は大きい。

**根拠**: 729: string inifile = "Skin.ini";
730: string targetdir = FilePath("System");
731:
732: if (_skinList.Count < Directory.GetDirectories(targetdir).Length)
733:     _skinList = [];
734: if (_skinList.Count > 0) return _skinList;
735:
736: List<string> list = [];
737: if (Directory.Exists(targetdir))

732行目の GetDirectories は 737行目の Directory.Exists ガードより前に無条件で実行される。targetdir("System"フォルダ)を作成する処理はリポジトリ全体を grep しても見当たらない。

**直し方の方針**: 1回目のディレクトリ数取得も Directory.Exists でガードする。例: int count = Directory.Exists(targetdir) ? Directory.GetDirectories(targetdir).Length : 0; if (_skinList.Count < count) _skinList = [];

#### ✅ [high] 進捗ログ計算がゼロ除算する

`Extend/Skin.cs:282`

**壊れ方**: Skin.Load(inque:true) で非同期読み込みキューに積んだ総アイテム数(QueMax)が1〜9個の小規模スキンで、まだ一部のTexture/Sound等が非同期ロード完了前(count < total)にFinishLoad()が再度呼ばれると即座に例外で落ちる。本リポジトリ内には現在 inque:true で呼び出す既存コードは見当たらないが、public APIとしてこの進捗表示自体がまさにその用途のために実装されている。

**根拠**: 280: if (count < total && QueMax > 0)
281: {
282:     int l = count / (int)(QueMax / 10.0);

QueMax > 0 のガードはあるが、QueMax が1〜9のとき QueMax/10.0 は1.0未満になり (int)キャストで0へ切り捨てられる。count と (int)(...) はどちらも int なので count / 0 は System.DivideByZeroException を実際に投げる(浮動小数点のNaNにはならない)。

**直し方の方針**: 0除算を避けるため、例えば int step = Math.Max(1, (int)(QueMax / 10.0)); としてから count / step を計算する、あるいは Math.Ceiling を使う。

#### ✅ [high] Speed が Opus 専用の OpusOriginalFrequency 属性に依存し、非Opusファイルではget側がゼロ除算、set側は指定値が無視される

`Extend/SoundExtend.cs:385`

**壊れ方**: wav/mp3/oggなど非Opusファイル(このプロジェクトで実際に使われている全形式)でSpeedを読むと、origFreq=0のため freq/0 = float.PositiveInfinityが返る(例外にはならないが完全に無意味な値)。Speedに書き込むと newFreq = origFreq(0)*value = 0 となり、Bass.ChannelSetAttribute(Frequency, 0)は「元のレートに戻す」という意味で成功してしまうため例外も出ないまま常に元の再生速度(等倍)にリセットされる。つまりSpeedに何を設定しても再生速度は変化しない。

**根拠**: 377-402行目:
```
377 public double Speed
378 {
379     get
380     {
381         EnsureReadyForChannel();
382         if (!Enable) return 1;
383         Bass.ChannelGetAttribute(_stream, ChannelAttribute.Frequency, out float freq);
384         Bass.ChannelGetAttribute(_stream, ChannelAttribute.OpusOriginalFrequency, out float origFreq);
385         return freq / origFreq;
386     }
387     set
388     {
...
395         Bass.ChannelGetAttribute(_stream, ChannelAttribute.OpusOriginalFrequency, out float origFreq);
396         float newFreq = origFreq * (float)value;
397         if (!Bass.ChannelSetAttribute(_stream, ChannelAttribute.Frequency, newFreq))
398         {
399             throw new InvalidOperationException($"速度の設定に失敗しました: {Bass.LastError}");
400         }
401     }
402 }
```
ManagedBass.xml: `<member name="F:ManagedBass.ChannelAttribute.OpusOriginalFrequency"> BassOpus: The sample rate of an Opus stream's source material. ... This attribute is read-only, so cannot be modified.` と明記されておりOpus専用。非Opusストリームでは Bass.ChannelGetAttribute が失敗し、C#のout引数 origFreq は既定値0fのまま返る(locals initによりゼロクリアされている)。
また ChannelAttribute.Frequency の説明: `The sample rate of a channel... 0 = original rate (when the channel was created).` — 0は「元のレートに戻す」有効な特殊値のため、398行目の Bass.ChannelSetAttribute(Frequency, 0) はエラーにならず成功する。
リポジトリ内で `.opus` ファイルは一切使われていない(grep 0件、docs/KNOWN-ISSUES.md自身の記述を除く)。

**直し方の方針**: Load()成功直後に Bass.ChannelGetAttribute(_stream, ChannelAttribute.Frequency, out var baseFreq) で元の周波数を一度だけ取得してフィールドにキャッシュし、Opus専用のOpusOriginalFrequencyの代わりにそのキャッシュ値を分母・基準として使うようにする。

#### ✅ [high] Pitch セッターが BASS_FX 専用属性 ChannelAttribute.Pitch を素の BASS ストリームに設定しようとして必ず例外を投げる

`Extend/SoundExtend.cs:371`

**壊れ方**: Enableな(=ロード済みの)SoundExtendのPitchに何らかの値を代入すると、素のBASSストリームには BASS_ATTRIB_TEMPO_PITCH が登録されていないため Bass.ChannelSetAttribute は必ず false を返し、371-374行目で InvalidOperationException が投げられる。呼び出し元がcatchしていなければクラッシュする。get側(363行目)も同じ属性を読むため常に0を返す(こちらは例外にはならないが無意味な値)。

**根拠**: 357-376行目:
```
357 public double Pitch
358 {
...
366     set
367     {
368         EnsureReadyForChannel();
369         if (!Enable) return;
370         float p = Math.Clamp((float)value, -12f, 12f); // BASS のピッチ範囲に合わせる
371         if (!Bass.ChannelSetAttribute(_stream, ChannelAttribute.Pitch, p))
372         {
373             throw new InvalidOperationException($"ピッチの設定に失敗しました: {Bass.LastError}");
374         }
375     }
376 }
```
ManagedBass.xml の該当定義: `<member name="F:ManagedBass.ChannelAttribute.Pitch"> BassFx Tempo: The Pitch in semitones (-60..0..+60).` — 公式にBASS_FXのテンポチャンネル(BassFx.TempoCreateで作成)専用属性であることが明記されている。
このプロジェクトの Load()(74行目)は `int stream = Bass.CreateStream(Path, 0, 0, flags);` という素のBASSストリームしか作らず、BassFx.TempoCreateの呼び出しはリポジトリ全体で0件。Extend/AstrumLoom.Extend.csproj(24-27行目)も `ManagedBass 4.0.2` のみを参照し `ManagedBass.Fx` は参照されておらず、埋め込みリソースも bass.dll のみで bass_fx.dll は存在しない。

**直し方の方針**: ManagedBass.Fx を追加しbass_fx.dllを同梱した上で、ロード時に BassFx.TempoCreate で元のストリームをラップしたテンポチャンネルを作ってから Pitch/Speed 属性を使うように変更する。もしくはテンポ非依存のピッチ変更が不要なら、Frequency属性ベースの疑似ピッチ実装に統一する。

#### ✅ [high] 重複キーのある設定ファイルで ItemDictionary が例外を投げる

`Extend/TextConf.cs:8`

**壊れ方**: Skin.ini等の設定ファイル中の通常設定行(Texture:/Sound:/Number:/Font:/Exo: 以外の行)に同名キー(大文字小文字違いも同一視)が2回以上出現すると、Load()はチェックせず両方をItemsに追加する。その状態でSkin.Load()が成功するとFinishLoad()→LogExport()→Configs.ItemDictionaryが呼ばれ、`Items.ToDictionary(..., OrdinalIgnoreCase)`が重複キーでArgumentExceptionを送出し、キャッチされずにスキン読み込み(ひいてはゲーム起動)全体が落ちる。

**根拠**: TextConf.cs:6-8 `private List<ConfItem> Items { get; set; } = []; public Dictionary<string, string> ItemDictionary => Items.ToDictionary(i => i.Name, i => i.Value, StringComparer.OrdinalIgnoreCase);`。Load()(104-129行)は重複キーの検査を一切せず `Items.Add(new(key, value, lastComment ?? ""));` を無条件に実行するため、同名(大文字小文字無視で同一)のキー行が複数あれば Items に複数エントリが残る。呼び出し元は Skin.cs:688 `var conf = Configs.ItemDictionary;`(LogExport()内)のみで、これは Skin.cs:295 `Text.Save(logpath, LogExport());`、すなわち FinishLoad() の中でスキン読み込み成功時に必ず実行される。FinishLoad()/LogExport() の呼び出し経路(Skin.cs:156,263-298)にtry/catchは存在しない。

**直し方の方針**: ToDictionaryの代わりに手動ループで構築し、重複時は「後勝ち」または「先勝ち」を明示して上書き/無視する。あるいはLoad()側で同名キー追加時に既存エントリを更新するようにする。

#### ✅ [high] DecorateOptionの参照ハッシュ(既定GetHashCode)をキャッシュキーに使っており装飾テキストのキャッシュが機能しない

`Extend/TextSprite.cs:231`

**壊れ方**: TextSprites.DrawDeco、またはTextSprites.Draw(...,DecorateOption decorate,...)をグラデーション/テクスチャ装飾テキストの描画で毎フレーム呼び出す実装(装飾テキストAPIのごく自然な使い方)を書いた場合、キャッシュが常にミスし、毎フレーム新規Textureの確保・破棄が発生してキャッシュ機構が実質無効化され、パフォーマンス劣化とGCプレッシャーを招く。

**根拠**: GetCacheKey(string text, IFont font, DecorateText.DecorateOption decorate)(228-233行目): `string decoKey = decorate.GetHashCode().ToString();`(231行目) → `return $"{text}__{fontKey}__{decoKey}";`(232行目)。DecorateText.DecorateOption(Extend/DecorateText.cs:57-64)は`Gradation? Gradation`と`Texture? Texture`を持つ普通のclassでEquals/GetHashCodeのオーバーライドが無く、既定のオブジェクトID基準のGetHashCodeが使われる。よって中身(Gradation/Texture)が同一でも`new DecorateOption(...)`するたびに異なるキーになり、Get(152-163行目)は`_cache.TryGetValue(key, out var sprite)`(155行目)が常に失敗して毎回`new TextSprite(...)`(157行目)を生成し、直前のエントリは次のDisposeUnused(164-180行目)で破棄される。ただしgrepで確認した限り、現状のコードベースでTextSprites.Draw(...,DecorateOption,...)/DrawDecoを実際に呼んでいる箇所は見当たらず(Sandbox/Load.cs:103-104でDecorateOptionを生成しているが以降未使用の変数で終わっている)、現時点では発火していない潜在バグ。

**直し方の方針**: DecorateOptionにGradation/Textureの中身に基づくEquals/GetHashCodeを実装する(またはrecord/record structにする)、もしくはGetCacheKeyでGradation/Texture自体のオブジェクトIDや内容から個別にキーを組み立てる。

#### ✅ [high] 非同期ロード時に LoadTx と Pump(_pendingBytes) が二重にテクスチャを生成しリークする

`RayLib/RayLibTexture.cs:163`

**壊れ方**: テクスチャをメインスレッド以外（例: UseMultiThreadUpdate=true の更新スレッド）から Load() した場合、必ず (a) `_deferred` 処理経由の LoadTx() による `Raylib.LoadTexture(Path)` 呼び出しと、(b) バックグラウンドで読んだバイト列を使う `Pump()` の `LoadTextureFromImage` 呼び出しの、両方が実行される。どちらのタイミングが先でも、後発の代入が `Native` を上書きし、先発の GPU テクスチャハンドルは一度も UnloadTexture されず永久にリークする。非同期でロードするテクスチャすべてで確実に起きる（レースではなく設計上必ず発生）。長時間稼働・大量アセットのゲームで VRAM/テクスチャハンドル枯渇につながる。

**根拠**: RayLibTexture.cs:94 `public void Load() => LoadAsync(this, LoadTx, LoadBackGround);` / :157-176 `public void Pump() { PumpAsync(); ... if (_pendingBytes != null) { ... Native = Raylib.LoadTextureFromImage(img); ... WriteState(State_Success); } }`。Core/Host.cs:120-146 `LoadAsync` は非メインスレッド呼び出し時に `Task.Run(_bgloadfuncs.Load)`（=LoadBackGround、バイト列読み込みのみ）を走らせつつ `_deferred=true; WriteState(State_Loading)` を返す。Core/Host.cs:176-187 `PumpAsync()` はメインスレッドで `_deferred` が立っていると無条件に `LoadAsync()`（引数null＝元の `_loadfunc`=LoadTx を再実行）を呼ぶ。LoadTx (RayLibTexture.cs:122) は `Native = Raylib.LoadTexture(Path)` で実ファイルから直接テクスチャを作る。つまり Pump() は毎回 `PumpAsync()`（内部でLoadTxを実行しテクスチャAを生成）→ 直後に `_pendingBytes != null` ならテクスチャBを生成 → Nativeを上書き、という2段構えになっている。

**直し方の方針**: AsyncLoadableBase側で `_bgloadfuncs` が設定されている場合は `PumpAsync()` の deferred フォールバックで `_loadfunc`（LoadTx）を再実行しないようにする。あるいは RayLibTexture.Pump() の `_pendingBytes` 分岐に入る直前に `if (Native.Id != 0) Raylib.UnloadTexture(Native);` を入れて既存ハンドルを解放してから上書きする。

#### ✅ [medium] Rate=0のときStart()のInterval計算が(int)キャストのオーバーフローで巨大な負値になり、アニメーションが実質フリーズする

`Anime/Exo.cs`（旧 `Extend/ExoAnimation.cs:605`。Exo/AnimObjectsはAnimeプロジェクトへ移設済み）

**壊れ方**: Interval=-2147483648がCounterに渡ると、Tick()(Core/Time.cs 99-150行目)は `Interval >= 0` がfalseのため125行目の逆方向分岐に入り、ループ条件は `diffTime >= -Interval` すなわち `diffTime >= 2147483648`(マイクロ秒、約35.8分)になる。通常のフレーム間隔(数ミリ秒〜数十ミリ秒)ではこの条件を満たすことはまず無く、Valueが実質的に変化しないためアニメーションが止まって見える。

**根拠**: 旧605行目: `counter = new Counter(1, Length, (int)(1000.0 * (1000.0 / Rate)), IsLoop);`。Rateは未設定時デフォルト0。Rate=0のとき `1000.0 / Rate` はdouble演算のため例外にならず `double.PositiveInfinity` になり、`1000.0 * Infinity` もInfinityのまま。.NETの仕様上、doubleのInfinityを(int)キャストすると `int.MinValue`(-2147483648)になる。Core/Time.cs 30-32行目の `NormalizeInterval` は `interval == 0 ? 1 : interval` というガードのみで、0そのものではなくオーバーフロー由来の巨大な負値(-2147483648)には反応しない。

**解消済み**: `Anime/Exo.cs` の `Start()` で `int rate = Rate > 0 ? Rate : 30;` によりフォールバックしている（Extend/Anime分離時点で既に対処済みのコードをそのまま移設）。あわせて今回の移設で `Time`/`EndTime` が `counter.Value * Rate`（フレーム→秒の換算として誤り）になっていたバグも見つかり、`/ Rate`（Rate=0時は0を返すガード付き）に修正した。

#### ✅ [medium] file= 再利用時に textureFileNames と imageObjects のインデックスがずれ、誤ったテクスチャを参照する

`Anime/Loaders/ExoLoader.cs`（旧 `Extend/ExoAnimation.cs:331`。Extend/Anime分離でパース処理はExoLoader/Aup2Loaderへ移設済み）

**壊れ方**: レイヤーAが file=a.png の後に中間点(file=なし)を1つ持ち、続けて別レイヤーBが file=b.png(新規)を読み込み、さらに後続のレイヤーCが file=b.png を再利用したケース: imageObjects=[A(a.png), 中間点(a.pngをコピー), B(b.png), C(未設定)], textureFileNames=["a.png","b.png"] となる。Cの file=b.png 処理時 `textureFileNames.IndexOf("b.png")` は1を返すが `imageObjects[1]` は中間点オブジェクト(a.pngのテクスチャ)でありBではない。結果、Cにはb.pngではなくa.pngのテクスチャが割り当てられる。例外は出ないが誤った画像が表示される。

**根拠**: 旧329-331行目: `imageObject.Texture = !textureFileNames.Contains(fileName) ? new Texture(Path.GetDirectoryName(FilePath) + @"\" + fileName) : imageObjects[textureFileNames.IndexOf(fileName)].Texture;` と338行目 `textureFileNames.Add(fileName);` は file= 行が出現するたびに無条件で実行される。一方 imageObjects への追加は `_name=画像ファイル` 検出時に行われ、file= 行の有無とは無関係。中間点(キーフレーム)の ImageObject は file= 行を持たずに imageObjects に追加される実装になっているため、imageObjects.Count は textureFileNames.Count より先行して増えることがあり、両リストのインデックスは1対1で対応しない。

**解消済み**: `Anime/Loaders/ExoLoader.cs`（および新規の `Anime/Loaders/Aup2Loader.cs`）では `Dictionary<string, Texture> textureByFileName` でファイル名からTextureを直接引く方式に変更済み（Extend/Anime分離時点で既に対処済みのコードをそのまま移設）。imageObjectsとの暗黙のインデックス対応には一切依存しない。

#### ✅ [medium] Skin.DefaultFont の指定が計算だけされて捨てられている

`Extend/Skin.cs:153`

**壊れ方**: スキンフォルダに default.ttf が存在しない場合(あるいは Skin.DefaultFont を明示的に別フォントへ上書きしていても)、常に raylib 内蔵のデフォルトフォント(日本語グリフを持たない)にフォールバックしてしまい、意図したフォント指定が無視される。クラッシュはしないが、CJKテキストが豆腐/文字化けになる形で表面化しうる。

**根拠**: 150: string fontpath = Path.Combine(skinPath, "default.ttf");
151: string defaultFont = DefaultFont ??
152:     (File.Exists(fontpath) ? fontpath : FHandle.SystemFont);
153: SetFont(Path.Combine(skinPath, "default.ttf"));

defaultFont はファイル内で他に一切参照されない(grep 'defaultFont' で 151行の代入のみヒット)。SetFont には計算結果ではなくリテラルの Path.Combine(skinPath, "default.ttf") がそのまま渡っている。RayLibFont.GetFont(Extend側からはFontHandle.Create経由、実体はRayLib/RayLibFont.cs:334-344) は font引数がFile.Existsで見つからない場合、spec.NameOrPath(=フォルダの絶対パス文字列そのもの)をフォント名としてSystemFontResolver.Resolveに渡すため必ず失敗し、GetFontDefault()(raylib内蔵のASCIIのみの既定フォント)にフォールバックする。Skin.DefaultFontによる上書きもFHandle.SystemFontへのフォールバックも機能しない。

**直し方の方針**: SetFont(Path.Combine(skinPath, "default.ttf")) を SetFont(defaultFont) に置き換える。ただし DefaultFont プロパティは既定値が null ではなく "" (非nullable string) なので `DefaultFont ?? (...)` の ?? は実質死んでおり常に左辺を返す。あわせて `string.IsNullOrEmpty(DefaultFont) ? (File.Exists(fontpath) ? fontpath : FHandle.SystemFont) : DefaultFont` のように空文字判定に直す必要がある。

#### ✅ [medium] Update() を Play()/PlayStream() なしで毎フレーム直接呼ぶと再生が始まらず Time が0に固定される

`Extend/SoundExtend.cs:293`

**壊れ方**: SoundExtend を ISound 経由でなく直接保持しているコード(例: 将来 MovieExtend 的なコードを書く際)が、フレームワーク内の他のUpdate()系メソッド(RayLibSound.Update()やMouse.Update()等、毎フレーム呼べば動く設計)と同じ感覚で PlayStream() の代わりに Update() を毎フレーム呼んでしまうと、例外もログも出ないままサウンドが一切再生されず、Timeを見ても常に0。デバッグが難しい静かな失敗になる。

**根拠**: 267行目 `private bool _played = false;` が初期値。Update()(268-295行目)は次の通り:
```
268 public void Update()
269 {
270     Pump();
271     if (!Enable) return;
272     if (_played)
273     { ... }
289     else
290     {
291         if (Loop) // ループ時にフラグをリセットして再生
292             _played = false;
293         Time = 0;
294     }
295 }
```
_played を true にするのは Play()(227行目 `_played = true;`)と PlayStream()(265行目)のみで、Update() 自身は一度も呼んでいない(Bass.ChannelPlay も呼ばない)。従って Play()/PlayStream() を一度も経由せず Update() だけを毎フレーム呼ぶと、_played は永遠に false のまま289-294行目のelse分岐に毎回入り、293行目の `Time = 0;` が実行され続ける一方でストリームは一切再生開始されない(=Timeは常に0のまま)。
なお ISound インターフェース(Core/Sound.cs 3-20行目)には Update() は宣言されておらず、公開ラッパー Sound クラス(Core/Sound.cs)にも Update() の委譲は無い(Pump()のみ委譲)。リポジトリ全体をgrepしても Update() を外部から直接呼んでいる箇所は無く(PlayStream()内部の261行目のみ)、Extend/MovieExtend.cs は SoundExtend を直接保持しているが Play()/IsPlaying/Time を使い Update() は呼んでいない。

**直し方の方針**: Update() が _played=false の場合に Play() 相当の処理へフォールバックするか、PlayStream() のロジックへ統合してUpdate()単独呼び出しでも初回再生が始まるようにする。あるいはUpdate()をinternalにして直接呼び出しをコンパイル時に禁止し、XMLドキュメントでPlayStream()経由を必須と明記する。

#### ✅ [medium] キャッシュ済みTextSpriteのEdgeColor変更が描画テクスチャに反映されない

`Extend/TextSprite.cs:195`

**壊れ方**: 同一text/font/colorでedgeColorだけ変えてTextSprites.Draw(またはDrawDeco)を連続フレーム呼び出しした場合(例: 点滅する警告テキストの縁色を切り替える演出)、表示される縁色はそのキャッシュキーで最初に生成された時点のEdgeColorのまま固まり、以降の変更が一切画面に反映されない。

**根拠**: TextSprites.Draw内: `sprite.Point = point;`(194) の直後 `if (edgeColor.HasValue) sprite.EdgeColor = edgeColor.Value;`(195) はプロパティを直接書き換えるのみ。TextSprite.EdgeColorは単純な自動プロパティ(128行目: `public Color? EdgeColor { get; set; } = null;`)で副作用なし。UpdateTextureIfNeeded(105-126行目)は`if (!_dirty) return;`(107行目)でガードされており、`_dirty`が立つのはコンストラクタ(27,40,52,60行目)・SetTextでテキストが変わった時のみ(57行目 `if (Text == text) return;` によりガード)・RecreateRenderTextureIfNeededでサイズが変わった時のみ(95,101行目)。EdgeColorはGetCacheKey(text,font,color)(222-227行目)のキーにも含まれないため、同じtext/font/colorでedgeColorだけ変えて呼んでも同一キャッシュインスタンスが再利用され、_dirtyが立たずUpdateTextureIfNeededが早期returnし、Font.Drawで焼き込み済み(120,122行目 `Font?.Draw(e, e, Text, ..., edgecolor: EdgeColor)`)の古い縁色テクスチャがそのまま描画され続ける。対照的にPoint/Blend/OpacityはDraw(double,double)(71-73行目)で毎回_textureオブジェクトに直接再設定されるため問題なく反映される。

**直し方の方針**: EdgeColor変更時にも_dirtyを立てる仕組みが必要。TextSprite側でEdgeColorのsetterに変更検知を入れて_dirty=trueにする、またはTextSprites.Draw側で旧EdgeColorと比較してsprite側に反映後dirty化する、もしくはキャッシュキーにedgeColorも含める。

#### ✅ [medium] IGraphics.Text の基準点オフセットの符号が IFont 実装と逆

`RayLib/RayLibGraphic.cs:136`

**壊れ方**: Drawing.DefaultText(x, y, text, color, ReferencePoint.Center などTopLeft以外, ...) のように IGraphics.Text（RayLibGraphic.Text）へ非TopLeftのアンカーで直接到達すると、テキストが中心/右寄せ/下寄せ等の意図と逆方向にオフセットされて描画される。ただし現状のコールサイトを見る限り、通常の Drawing.Text() は DefaultFont.Draw（IFont.Draw、正しい加算側）を経由するため実害は限定的で、Drawing.DefaultText を非TopLeftのpointで直接呼ぶ、またはフォント無効時（Enable=false）にRayLibFont.Draw内のフォールバック経由でIGraphics.Text（常にTopLeft固定なので現状は無害）以外の経路——具体的には Graphics.Text/IGraphics.Text を非TopLeftで直接呼ぶ利用コード——が現れた時点で顕在化する。実装として符号が逆であること自体は現在のコードで確実に確認できる。

**根拠**: Text() では 135-136行目で `var anchorOffset = LayoutUtil.GetAnchorOffset(options.Point, size.X, size.Y); var pnt = new LayoutUtil.Point(x, y) - anchorOffset;` と、アンカーオフセットを『引いて』いる。一方 GetAnchorOffset の定義（Core/Graphic.cs 274-288行目）は Center で (-w/2, -h/2) のように『中心に合わせるために足すべき値（負数）』を返す設計で、実際 RayLib/RayLibFont.cs の IFont.Draw（248-250行目 `var off = GetAnchorOffset(...); drawX = (int)(x + off.X); drawY = (int)(y + off.Y);`）や DXLib/DxLibFont.cs の Draw（66-68行目、同じく `x + off.X`）はどちらも『足して』いる。RayLibGraphic.Text だけが符号反転しており、Center 指定時は本来 (x-w/2, y-h/2) になるべきところが (x+w/2, y+h/2) になり、意図と逆方向（右下）にずれる。DXLib/DxLibGraphic.cs の Text（133-134行目、同じく `x - offset.X`）も同じ符号ミスを共有しているが、対象ファイルである RayLibGraphic.cs としては real。

**直し方の方針**: 136行目を `var pnt = new LayoutUtil.Point(x, y) + anchorOffset;`（減算ではなく加算）に直し、RayLibFont.Draw / DxLibFont.Draw と符号を揃える。DXLib側 DxLibGraphic.cs の同型コード（133-134行目）も同じ修正が必要（別ファイルなので今回のスコープ外だが一貫性のため要検討）。

#### ✅ [medium] 枠線の Circle / Oval が thickness を無視する

`RayLib/RayLibGraphic.cs:79`

**壊れ方**: DrawOptions.Thickness を 3 や 5 などに設定して Fill=false の Circle/Oval を描画しても、実際には常に細い1px相当の線で描かれる。太い枠線（選択強調、フォーカスリングなど）を意図したUIコードが見た目上壊れる。クラッシュや例外は起きない純粋な描画不整合。

**根拠**: Circle() は 75行目で `int thickness = Math.Max(1, options.Thickness);` を計算しているが、Fill=false の枠線描画は 79行目 `else DrawCircleLines((int)Math.Round(x), (int)Math.Round(y), (float)radius, col);` のみで、thickness をどこにも渡していない。同様に Oval() も 85行目で thickness を計算しているのに、枠線描画は 89行目 `else DrawEllipseLines((int)x, (int)y, (int)rx, (int)ry, col);` で thickness 引数がない。raylib の DrawCircleLines / DrawEllipseLines はそもそも太さ引数を取らないシグネチャ（center, radius(H/V), color のみ）なので、options.Thickness に何を入れても常に 1px 相当の細線になる。Box()（68-69行目）や Triangle()（120-122行目、DrawLineEx に thickness を渡している）と挙動が明確に食い違っており、Circle/Oval だけ枠線太さが効かない。

**直し方の方針**: Circle は raylib の DrawRing(center, radius - thickness/2f, radius + thickness/2f, 0, 360, segments, col) を使えば太さを再現できる。Oval には直接対応する太さ付きAPIが無いため、DrawEllipseLines を thickness 分だけ半径をずらして複数回描く（または DrawEllipseLines の代わりに外周・内周2つの楕円点列からストリップポリゴンを自前で作る）といった実装が必要。

#### ✅ [medium] RayLibTextInput が Enter/Esc/Backspace/文字入力を buffered state 経由でなく raylib の IsKeyPressed/GetCharPressed 直呼びで処理している

`RayLib/RayLibInput.cs:280`

**壊れ方**: UseMultiThreadUpdate=true(Sandboxの既定構成)でTキーを押してテキスト入力を開始し、タイピング中に文字を打つと、メインスレッドが EndDrawing 内で raylib のネイティブなキー状態配列・charPressedQueue を書き換えている最中に、更新スレッドが同じネイティブ状態を排他制御なしに IsKeyPressed/GetCharPressed で読む。GLFW側もこれらの関数について『Access is not synchronized』としており、C#側で例外にはならず黙って壊れる。実害はタイピング中の文字の欠落・重複、Enter/Escの取りこぼしや二重検出という形で出る。ネイティブ側は固定長キュー(MAX_CHAR_PRESSED_QUEUE)で単純なint読み書きのため即クラッシュには直結しないが、正しさは保証されない。

**根拠**: 同ファイル 218〜298行目の RayLibTextInput クラスは、9〜216行目の RayLibInput クラス(Buffer()で_now/_prevをサンプリングし、Update()で_stateへ遷移させ、GetBufferedStateで公開する仕組み)と別クラスで、_state配列を一切参照しない独自フィールドしか持たない。実際に 253行目 `if (IsKeyPressed(KeyboardKey.Enter))`、258行目 `IsKeyPressed(KeyboardKey.Escape)`、267/277行目 `GetCharPressed()`、280行目 `if (IsKeyPressed(KeyboardKey.Backspace) && Cursor > 0)` と、raylibのグローバル関数を直接呼んでいる。対照的に呼び出し元 Core/Input.cs の TextEnter.Update() は同じメソッドの470行目で `if (Key.Esc.Push() && Option.EscapeCancelable)` と、buffered state 経由(拡張メソッドPush→_input.GetKeyDown→GetBufferedState)のEscも別途見ており、同一メソッド内で経路が二重化している。スレッド面: Sandbox/Program.cs:65 で `UseMultiThreadUpdate = true` が既定。Core/AstrumCore.cs:249 `MultiThreading => WindowConfig.UseMultiThreadUpdate` によりtrueになると、Core/Game.cs の UpdateLoop(専用スレッド)が game.Update() を回す。Sandbox/Input.cs の InputTestScene.Update()→UpdateTextInput()(113〜127行目)は KeyInput.ActivateText/Enter を呼び、これが TextEnter.Update()→_impl.Update()/_impl.KeyState(=RayLibTextInput)を更新スレッド上で実行する。一方、raylibの実ポーリング(PollInputEvents、キー状態配列と文字キューを実際に書き換える処理)はRayLib/RayLibGraphic.cs:40 `public void EndFrame() => EndDrawing();` を通じて Draw()(メインスレッド)側でのみ発生する。

**直し方の方針**: Enter/Esc/Backspaceの判定は既にTextEnter.cs:470が使っている buffered state (Key.Esc.Push() 相当)に統一し、文字入力もメインスレッドのBuffer()内でGetCharPressed()をポンプしてスレッドセーフなキューに積み、RayLibTextInput.Update()はそのキューを読むだけにする。

#### ✅ [medium] GetButton(int)がraylibのGamepadButton列挙をそのままcastしており、DxLib版のビット順と一致しない

`RayLib/RayLibPad.cs:131`

**壊れ方**: 同じ物理パッドの同じボタンを押しても、DxLibバックエンドとRayLibバックエンドとでpad.Button[N]/pad.IsPushed(N)が真になるNが異なる(例: 十字下はDxLibでN=0、RayLibでは0番目に対応する物理ボタンが無い)。Game/AstrumLoom.GameUtil.csprojのコメントが明言する『ゲーム側はCoreだけを参照すればDxLib/RayLibどちらでも動く』という前提が、ボタン番号に関しては成立しない。現状の実害はSandbox/Input.csのデバッグパネルの表示番号がバックエンドで変わる点にとどまり、ゲームロジックが特定indexへ意味を固定した時点で顕在化する。

**根拠**: 131行目 `private static GamepadButton GetButton(int index) => (GamepadButton)index;` を77〜90行目のBuffer()が `IsGamepadButtonDown(Index, GetButton(i))` の形でButton[24]全域(64行目)に適用している。実際にRaylib-cs 7.0.2(RayLib/AstrumLoom.RayLib.csproj:18で参照しているバージョン)をビルドしてGamepadButton列挙を列挙した結果: Unknown=0, LeftFaceUp=1, LeftFaceRight=2, LeftFaceDown=3, LeftFaceLeft=4, RightFaceUp=5, RightFaceRight=6, RightFaceDown=7, RightFaceLeft=8, LeftTrigger1=9, LeftTrigger2=10, RightTrigger1=11, RightTrigger2=12, MiddleLeft=13, Middle=14, MiddleRight=15, LeftThumb=16, RightThumb=17。一方 DXLib/DxLibPad.cs:86-90 は `(input & (1 << i)) > 0` でGetJoypadInputStateのビットを直読みしており、DXLib/DxDLL.cs:645-684 の定数(PAD_INPUT_DOWN=1(bit0), LEFT=2(bit1), RIGHT=4(bit2), UP=8(bit3), PAD_INPUT_A/PAD_INPUT_1=16(bit4), B/2=32(bit5), C/3=64(bit6), X/4=128(bit7)...)に従う。つまりindex=0はRayLibでは『Unknown(対応する物理ボタン無し)』、DxLibでは『十字下』を意味し、index=4はRayLibで『十字左』、DxLibで『ボタンA/1』を意味するというように、同じButton[N]のNが指す物理ボタンが全面的に食い違う。実際に観測できる経路として Sandbox/Input.cs:194-197 のDrawControllerPanelが `pad.Button.Select((value, index) => value != 0 ? index.ToString() : null)` で押されているボタンのindexをそのまま表示しており、同じ物理ボタンを押しても表示される番号がバックエンドで変わる。ただしリポジトリ全体を`.IsPushed(`等でgrepしても、特定indexに固定の意味(例:『0=決定』)を割り当てて使っているゲームコードは現状ゼロ。

**直し方の方針**: RayLibPad側にもDxLibPad同様の共通ボタン順マッピングテーブルを用意してGetButton(int)をそこ経由にする、あるいはIJoyPadに共通の論理ボタンenumを設けて両バックエンドがそこへ変換するようにする。

#### ✅ [medium] RayLibController.Count / List / GetJoyPad が _lock を取らずに _joyPads を読む一方、Buffer()/Update() はロックしている

`RayLib/RayLibPad.cs:11`

**壊れ方**: コントローラーの物理的な接続/切断が発生した瞬間、メインスレッドの Buffer()→SetController() が _joyPads.Add/RemoveAll でリストの内部配列・versionを書き換えるのと同時に、更新スレッドの game.Update() 内で Pad.Count/List/GetJoyPad(ロック無し)を呼ぶと、List<T> は非スレッドセーフなため LINQ の Select/FirstOrDefault が列挙中に version 不整合を検出して InvalidOperationException("Collection was modified")を投げうる。これは Game.cs の Update(IGame game) の catch(Exception)→HandleFatal("Update") を経由し、致命エラー画面に落ちる(同種の失敗モードは本リポジトリで既に確認済みのLog.LogMessages/FpsCounterの競合バグと同型)。接続/切断が起きない定常状態では_joyPads.RemoveAllが未マッチ時にversionを変えないため顕在化しにくく、再現には「多重スレッド更新+ホットプラグのタイミング」が必要。

**根拠**: 9〜11行目 `public int Count => _joyPads.Count;` / `public string[] List => [.. _joyPads.Select(...)];` / `public IJoyPad? GetJoyPad(int index) => _joyPads.FirstOrDefault(...);` はいずれも _lock を経由しない。対して 35〜46行目 Buffer() は `lock (_lock) { SetController(); ... }` で SetController()(16〜34行目、_joyPads.Add/RemoveAllで実際にリストを変更する)を保護し、47〜56行目 Update() も `lock (_lock)` で列挙している。Core/Pad.cs:52-56 の静的ラッパー(Pad.Count/List/GetJoyPad)は素通しで、Sandbox/Input.cs:71 `int count = Pad.Count;`、:94 `var pad = Pad.GetJoyPad(_selectedPad);`、:171 `string[] names = Pad.List` がゲームコード(InputTestScene.Update()/Draw())から呼ばれる。Sandbox/Program.cs:65 で UseMultiThreadUpdate=true が既定のため、Core/Game.cs の Update(IGame game) は更新スレッドで、Draw(IGame game) はメインスレッドで並行に走り、Pad.Update()(Game.cs:148)は更新スレッド側のgame.Update()経由でCore/Pad.cs:71-76相当のUpdateControllerStateを、RayLibPlatform.PollEvents()(Game.cs:274 MainUpdate→platform.PollEvents→Controller.Buffer())はメインスレッド側でそれぞれ実行する。

**直し方の方針**: Count/List/GetJoyPad の実装を Buffer()/Update() と同じ _lock で包み、読み取りも書き込みと同じ排他ドメインに揃える。

#### ✅ [medium] Resizable=false のとき ConfigFlags.UndecoratedWindow をセットし、タイトルバー等の装飾が消える

`RayLib/RayLibPlatform.cs:27`

**壊れ方**: GraphicsBackend=RayLib かつ config.Resizable=false でゲームを起動すると、意図した『サイズ変更不可だが通常の見た目』のウィンドウではなく、タイトルバー・枠・閉じるボタンが全て消えたウィンドウになる。SetExitKey(0)と重なりマウス操作での終了手段も消える。逆にResizable=true(既定)でも実際にはWindowResizableフラグが立たないため常に固定サイズウィンドウになる。

**根拠**: RayLibPlatform.cs 27-30行: 'if (!config.Resizable)\n{\n    SetWindowState(ConfigFlags.UndecoratedWindow); // 例：必要なら調整\n}' — Resizable=true(既定)側にはリサイズ可否を制御するフラグ(WindowResizable相当)が一切設定されず、Resizable=falseの側だけがUndecoratedWindow(装飾なし=タイトルバー・枠・閉じるボタンが消えるraylibフラグ)をセットしている。Core/Config.cs:10 'public bool Resizable { get; set; } = true;' で既定はtrue。全文検索 'Resizable\s*=\s*false' はリポジトリ内0件だが、公開APIなので値を渡せば確実にこの経路を通る。docs/KNOWN-ISSUES.md:477 も別件の棄却理由の中で同じ事実(!Resizable側だけUndecoratedWindowをセットし、既定true側は何もしない)を独立に確認済み。同コンストラクタ35行目 SetExitKey(0) によりESC終了も無効化されているため、Resizable=false指定時はマウスでウィンドウを閉じる手段が失われる。

**直し方の方針**: Resizableの真偽はそのままConfigFlags.WindowResizableの設定/非設定に対応させる。装飾(タイトルバー)の有無を制御したいなら別のプロパティ(例: Undecorated)として分離する。

#### ✅ [medium] SE のループ再開処理が到達しない位置にある

`RayLib/RayLibSound.cs:209`

**壊れ方**: Loop=true にした効果音(SE, 非ストリーミング)を Play()→PlayStream() のパターンで自動ループさせようとしても、再生が自然に終わった時点で _played が true のままなので、ループ再開分岐(209-210行目)に一切到達せず、SEは1回再生されたきり自動では再開しない。呼び出し側で明示的に Stop()→Play() をし直さない限りループしない。

**根拠**: Update()(160-214行目)。`if (_played)` の else 節(207-213行目) にだけ `if (Loop) _played = false;`(209-210行目) というループ再開の仕込みがある。この else 節は _played が false のときにしか実行されない。ところが _played が true→false に戻るのは Play()(285行目, true化) と Stop()(299行目, false化) のみで、SE 再生分岐(194-205行目, _streamloaded==false のケース)では `IsSoundPlaying(Sfx)` が false（再生終了）になっても _played をリセットするコードが無い。したがって Play() で一度 true になった _played は、呼び出し側が Stop() を呼ばない限り true のまま固定され、SE の自然な再生終了経由では 207-213 行目のループ再開分岐に到達できない。

**直し方の方針**: SE分岐(194-205行目)で `IsSoundPlaying(Sfx)` が false になった（再生が終わった）タイミングで `_played = false;` を立てる。さらに、その場で Loop なら PlaySound を呼び直す（もしくは次フレームの else 節に処理を委ねるだけでなく、そこで実際に Play() を呼ぶよう分岐を追加する）ことで、_played のフラグリセットだけでなく実際の再生再開まで確実につなげる。

#### ✅ [medium] 非同期ロード経路が streaming 指定を無視して常に Music を読み直す

`RayLib/RayLibSound.cs:129`

**壊れ方**: RayLibSound をメインスレッド以外から生成/Load した場合（AsyncLoadableBase のバックグラウンドロード経路が使われるケース）に streaming:false を指定しても、Pump() の完了処理で必ず LoadMusicStream(Path) が呼ばれて Music がロードされたまま残る。ワンショットのSEを大量に非同期ロードする設計だと、期待していたメモリ節約（Music非保持）が効かず、Sfx分に加えてMusicストリーム分のメモリを常に食う。

**根拠**: Pump() 内、非同期ロード完了処理(119-136行目)で `_pendingBytes != null` のとき Sfx を組み立てた直後に `Music = LoadMusicStream(Path);`(129行目) を無条件に呼んでいる。streaming フラグを一切参照していない。対照的に同期経路の LoadSfx(64-90行目) は `if (!streaming) { UnloadMusicStream(Music); Music = default; }`(83-88行目) で明示的に streaming=false を尊重して Music を解放している。さらに LoadBackGround(bool streaming)(91-104行目) は streaming 引数を受け取るが本体で一度も使っていない。Load()→LoadAsync(this, LoadSfx(streaming), LoadBackGround(streaming))(62-63行目) の呼び出しはメインスレッド外から呼ばれた場合に Task.Run 経由で LoadBackGround が動き(Core/Host.cs 126-140行目)、その完了を Pump() が拾う設計なので、非同期ロード時に限って streaming=false を指定しても Music が常駐する。

**直し方の方針**: Load(bool streaming) 時点で streaming フラグをインスタンスフィールドに保存しておき、Pump() の非同期完了処理でも LoadSfx と同じ分岐（streaming が false なら LoadMusicStream 後に即 UnloadMusicStream して Music を default に戻す）を入れる。

#### ⬜ [medium] Drawing.DefaultScale が座標(fx,fy)にしか掛からず拡大率(origin/destW/destH)に掛かっていない

`RayLib/RayLibTexture.cs:236`

**壊れ方**: ゲーム側が `Drawing.DefaultScale` を1.0以外（仮想解像度→実ウィンドウサイズのスケーリングなど、Texture.ScaledSizeが前提とする用途）に設定すると、RayLibバックエンドでは描画位置だけが defscale 倍に移動し、テクスチャの表示サイズは等倍のまま変わらない。DXLibバックエンドでは位置もサイズも defscale 倍になるため、同一コードでバックエンド間の見た目が食い違う。現状 `Drawing.DefaultScale` を内部で自動設定している箇所はなく、ユーザーコードが明示的に使った時にのみ顕在化する。

**根拠**: RayLibTexture.cs:207-209 `float defscale = (float)Drawing.DefaultScale; float fx = (float)(x * defscale); float fy = (float)(y * defscale);` の後、:215-218 `origin` は `point.X * Math.Abs(w)` のみで defscale を含まず、:236-237 `float destW = (float)(rect.Width * Math.Abs(w)); float destH = (float)(rect.Height * Math.Abs(h));` にも defscale が掛かっていない。対して DXLib/DxLibTexture.cs:148-152 は `float defscale = ...; float fx = (float)(x*defscale); float fy=(float)(y*defscale); (double w,double h)=use.Scale; w *= defscale; h *= defscale;` と拡大率側にも掛けている。さらに Core/Texture.cs:190-193 `ScaledSize => (Width*Scale*Drawing.DefaultScale, Height*Scale*Drawing.DefaultScale)` はサイズにも DefaultScale が掛かる前提。docs/KNOWN-ISSUES.md:193-197 に「Drawing.DefaultScale=2.0 で DxLibは(200,200)に2倍サイズ、RayLibは(200,200)に等倍サイズ」という実機再現記録あり。

**直し方の方針**: DxLibTexture.cs同様、`(double w, double h) = use.Scale; w *= defscale; h *= defscale;` を追加し、origin/destW/destHの計算をその後の w,h から求めるようにする（位置と拡大率の両方に defscale を反映）。

#### ✅ [medium] RenderTexture の上下反転補正が Flip.Y の有無を無視して無条件適用され、Flip.Y=true 時に srcRect が範囲外になる

`RayLib/RayLibTexture.cs:229`

**壊れ方**: rect=(0,0,W,H)のフル矩形で計算すると、Flip.Y=falseかつRenderTexture経由の場合は補正後 `srcRect=(0, H, W, -H)` となり有効範囲[0,H]内に収まる（正しい単発反転）。しかしFlip.Y=trueかつRenderTexture経由の場合、tyで一度反転された `srcRect=(0,0,W,-H)` にさらに同じ補正が掛かり `srcRect=(0, -H, W, +H)` となる。これはY方向のサンプリング範囲が[-H, 0]となり、テクスチャの有効領域[0,H]を完全に外れる。RenderTexture由来のITextureに対してFlip.Y=trueを指定する組み合わせ（オフスクリーン合成結果を上下反転して描く、水面反射など）で必ず発生し、意図した反転にならないばかりかサンプリング範囲そのものが不正になる。

**根拠**: RayLibTexture.cs:212 `(int tx, int ty) = (use.Flip.X ? -1 : 1, use.Flip.Y ? -1 : 1);` :220-226 `var rect = use.Rectangle ?? new(0,0,Width,Height); var srcRect = new Rectangle(rect.X, rect.Y, rect.Width*tx, rect.Height*ty);` :228-233 `// Render経由の場合上下反転するので補正\nif (_renderTex.Id != 0) { srcRect.Y += srcRect.Height; srcRect.Height *= -1; }`。Flip.Yの有無に関わらず `_renderTex.Id != 0` だけを条件にこの補正が走る。

**直し方の方針**: RenderTexture補正とFlip.Yを独立した2回の符号反転として順に適用するのではなく、`bool netFlip = use.Flip.Y ^ (_renderTex.Id != 0);` のように実効反転の要否をXORで一度だけ求め、その結果に応じて `srcRect.Y`/`srcRect.Height` を1回だけ決定する形に書き換える。

#### ✅ [medium] テクスチャ再生成で旧テクスチャを破棄していない

`Sandbox/Load.cs:132`

**壊れ方**: LoadCheckScene 表示中に R キーを繰り返し押してテクスチャを再生成すると、旧 Texture ラッパーはGC回収・ファイナライザ実行まで解放されず、ネイティブ側（DxLib既定バックエンド）のグラフィックハンドル解放が不定時間遅延する。

**根拠**: Load.cs:128-138: `if (_regenerate) { _regenerate = false; _tex = new Texture(new LayoutUtil.Size(90, 30), () => { ... }); Log.Write("Texture regenerated."); }` — 代入前に `_tex?.Dispose()` を呼んでいない。Core/Texture.cs:105-111 で `Texture` は IDisposable かつファイナライザ実装（`~Texture() => Dispose();` / `Dispose()` は `AstrumCore.RequestDispose(_texture!)`）を持つため即座には壊れないが、GC.AddMemoryPressure の呼び出しがリポジトリ内に無く（grep 0件）、小さな管理対象オブジェクトのためGCが走らずネイティブハンドル解放が遅延しうる。同一プロジェクトの Sandbox/Resourses.cs:41-44 は `_tex?.Dispose(); _bgtex?.Dispose(); _flash?.Dispose();` と明示的破棄を徹底しており、Load.cs だけがこの規約から逸脱している。

**直し方の方針**: 代入前に `_tex?.Dispose();` を追加する（Resourses.cs と同じパターン）。

#### ✅ [low] _streaming が代入のみで一度も読まれない（CS0414 相当）

`RayLib/RayLibSound.cs:155`

**壊れ方**: 動作は壊れないが、実質的に何の意味も持たない書き込み専用フィールドがコンパイラ警告(CS0414)を出し続ける。恐らく IsPlaying のキャッシュ用途などで導入しようとして配線し忘れた設計の残骸と見られる。

**根拠**: `private bool _streaming = false;`(155行目) の宣言後、ファイル全体を grep すると代入箇所は 173, 192, 200, 204, 211, 300 行目のみで、get する箇所は一切ない。RayLibSound は partial class ではなく本ファイル1箇所にしか定義がなく（`class RayLibSound` はこの1ファイルのみ）、ISound インタフェース(Core/Sound.cs)にも対応する `Streaming` 系プロパティは存在しない。実際に使われている再生判定は `_streamloaded`(59行目, Music.FrameCount>0) と `IsPlaying`(269行目, IsMusicStreamPlaying/IsSoundPlaying を都度呼ぶ実装) であり、_streaming とは別物。

**直し方の方針**: フィールドを削除するか、本来の意図（再生中フラグのキャッシュ等）に沿って IsPlaying や _played のリセット判定に実際に使う実装へ差し替える。

#### ⬜ [low] オーバーレイの分岐条件が絶対に成立しない

`Sandbox/Overlay.cs:14`

**壊れ方**: 常時: この分岐には絶対に入らず、意図された「軽量パネルへの切り替え」が常にスキップされフルのデバッグパネルが描画され続ける。ただし LoadCheckScene 自体がどこからも起動されないデッドコードなので、現状の実害はゼロ。

**根拠**: Overlay.cs:14 `if (Scene.NowScene is SimpleTestGame { Child.Name: "LoadCheckScene" })`。SimpleTestGame.Child（Program.cs:20 `public Scene? Child => _scene;`）の _scene は Program.cs:14 の Enable() 内で `_scene = new TextureDemoScene();` としか代入されず、Update/Draw/Drag含め他に再代入箇所は無い。リポジトリ全体を `LoadCheckScene` で grep すると、ヒットは Load.cs:6 のクラス宣言自体と Overlay.cs:14 の文字列リテラルのみで、`new LoadCheckScene(...)` は一度も呼ばれていない（Activator.CreateInstance 等の動的生成も無し）。Core/Scene.cs:110 `public string Name => GetType().Name ?? "";` により、Child.Name が "LoadCheckScene" になるには Child が実際に LoadCheckScene インスタンスである必要があるが、その経路が存在しない。

**直し方の方針**: LoadCheckScene を実際に SimpleTestGame の子シーンとして切り替えられるようにする（シーン切替の仕組みを追加）か、比較先を実際に使われているシーン名に修正する。

### 一部だけ正しいもの（2 件 / 修正済み 2 件）

主張の一部は成立しますが、結論や深刻度が違います。

#### ✅ [medium] リサイズ/拡大率フィルターのX=・Y=処理が「拡大率=が最初に来る」前提でFilters.Last()を無防備に呼ぶ

`Anime/Loaders/ExoLoader.cs`（旧 `Extend/ExoAnimation.cs:445`。パース処理はExoLoaderへ移設済み）

**壊れ方**: この判定は『実際の.exoファイルで、リサイズ/拡大率フィルターの行が本当に 拡大率=→X=→Y= の順で出力されるか』に依存する。もし実際の順序がX=,Y=が拡大率=より先であれば、このフィルターを含む.exoを読み込むたびに例外を投げる。

**根拠**: 旧414-476行目(FilterType.Scaleブロック): 拡大率=行だけが `ScaleFilter scaleFilter = new(); currentObject.Filters.Add(scaleFilter);` でインスタンスを生成しており、X=とY=のハンドラは `var FilterObject = (ScaleFilter)currentObject.Filters.Last();` として既存インスタンスの取得のみを行い、生成もnull/空チェックも一切行っていなかった。

**解消済み**: `Anime/Loaders/ExoLoader.cs` では X=/Y= のハンドラを `currentObject.Filters.OfType<ScaleFilter>().LastOrDefault()` に変更し、nullなら新規生成してAddする形にしている（Extend/Anime分離時点で既に対処済みのコードをそのまま移設）。3つのキーのどれが最初に来ても安全に動く。

#### ✅ [medium] ファイナライザからのDispose呼び出しが非スレッドセーフな static Dictionary(Skin.Textures)を直接書き換える

`Extend/NumAnim.cs:183`

**壊れ方**: NumAnimation インスタンスが明示的に Dispose() されずガベージコレクトされた場合、.NETの専用ファイナライザスレッド上で ~NumAnimation() が走り、Skin.RemoveTexture 経由で Skin.Textures(非スレッドセーフな Dictionary)の Keys 列挙および Remove をメインスレッドと非同期に実行する。ちょうどメインスレッドが Skin.Texture()/Skin.Load()/Skin.CheckResource()/Skin.AddTexture() などで同じ辞書を読み書きしているタイミングと重なると、InvalidOperationException や内部バケット構造の破損によるクラッシュ・未定義動作が起こり得る。GPU実体の解放自体はキュー経由でメインスレッドに委譲されているため直接のGPUクラッシュは避けられているが、辞書破壊のリスクは残る。

**根拠**: NumAnim.cs:183-186 `~NumAnimation() { Dispose(); }`。Dispose()本体(190-209行)は `if (!string.IsNullOrEmpty(_keyPrefix) && Skin.Textures != null) { var keysToRemove = Skin.Textures.Keys.Where(...).ToList(); foreach (string? k in keysToRemove) { Skin.RemoveTexture(k); } }`。Skin.cs:11 `public static Dictionary<string, Texture> Textures { get; set; } = [];` は通常の非同時実行対応 Dictionary で、ConcurrentDictionaryではない。Skin.cs:523-532 の RemoveTexture は `value?.Dispose(); Textures.Remove(name); _textureCache.TryRemove(name, out _);` で、`Textures.Remove` はロック無しの直接書き換え。一方、GPU側の実破棄はCore/Texture.cs:107-111 `public void Dispose() { AstrumCore.RequestDispose(_texture!); GC.SuppressFinalize(this); }` に変わっており、Core/AstrumCore.cs:254-280 の `ConcurrentQueue<IDisposable> _disposeQueue` と `ProcessPendingDisposals()`(`if (Environment.CurrentManagedThreadId != MainThreadId) return;`)によって、実際のGPUリソース解放はメインスレッドでしか行われないよう既に修正されている。

**直し方の方針**: Dispose()内でSkin.Textures を直接触らず、Texture破棄と同様に「このキー群を削除してほしい」という要求だけをメインスレッド専用キューに積み、実際のDictionary操作はProcessPendingDisposals相当のメインスレッド処理でまとめて行う。あるいはSkin.TexturesをConcurrentDictionaryに変更するか、専用ロックで保護する。

### 今回の作業で解消されたもの（5 件）

当時は成立していましたが、その後の修正で消えています。

#### ✅ [high] フォント生成時に65504グリフを一括ラスタライズしている

`RayLib/RayLibFont.cs:39`

**根拠**: 現在のコード(36〜39行): 「// 焼くグリフは ASCII + 日本語の常用範囲（CommonJpCodePoints）に絞る。// 0x20〜0xFFFF を丸ごと（65504個、サロゲート単体まで含む）焼こうとすると、// サイズ24でもアトラスが8192四方級になり、確保に失敗して豆腐落ちする。int[] cps = BuildCodePoints(spec.ExtraGlyphs);」。BuildCodePoints→CommonJpCodePoints(346〜377行)はASCII(95)+ひらがな(96)+カタカナ(96)+CJK記号(64)+全角記号(240)+JIS第一水準漢字相当(約2965)の合成で、合計は数千個規模(65504個ではない)。git diff HEAD -- RayLib/RayLibFont.cs で確認すると、HEADコミット時点(=当時の監査対象)では実際に `int[] cps = EnumRange(0x20, 0xFFFF);` (0x20〜0xFFFFの範囲=65504個)だったことが差分として残っており、それが今回のコミット前修正でCommonJpCodePointsベースの絞り込みに置き換わっている。指摘は当時は正しかったが、その後の修正で解消済み。

#### ✅ [high] .ttc/.otc を LoadFontEx に渡しており日本語システムフォントが読めない

`RayLib/RayLibFont.cs:69`

**根拠**: 現在のコードでは .ttf/.otf/.ttc/.otc すべて41〜79行のブロックでバイト列を読み、54行 `if (IsTtcHeader(bytes))` でマジック'ttcf'を検出した場合は56行 `ExtractSubFontFromCollection(bytes, 0, out int chosen, out int total)` で中の1本を単体sfntに組み直し、66行で `hint = ".ttf"` に切り替えたうえで69行 `_font = Raylib.LoadFontFromMemory(hint, bytes, spec.Size, cps, cps.Length);` に渡している。ファイル全体を検索しても `LoadFontEx` の呼び出しは存在しない。git diff HEAD -- RayLib/RayLibFont.cs では、HEADコミット時点で `.ttf/.otf/.ttc/.otc` に対して `_font = Raylib.LoadFontEx(path, spec.Size, cps, cps.Length);` と、パスをそのまま渡していたことが削除行として確認できる(指摘どおりの実装だった)。raylib(stb_truetype)はTTC/OTCのオフセットテーブルを解釈せず常にoffset 0から読むため、当時は日本語Windowsシステムフォント(msgothic.ttc等)が実質読めなかったが、現行コードはこの問題を明示的に解決するための分解ロジックを新設している。

#### ✅ GetSound が自分自身を無限再帰する(StackOverflow)

`Extend/SkinExtend.cs:102`

**根拠**: 現在のコード SkinExtend.cs:94-103: 「呼ぶ先は 1 つ上の <see cref="SoundExtend(string, string?)"/>。ここを GetSound と書くと自分自身を呼んで無限再帰になり、StackOverflowException（.NET では捕捉できない）でプロセスごと落ちる。」というdocコメントの直後に `public static SoundExtend GetSound(string key) => SoundExtend(key.ToLowerInvariant()) ?? new("");`。呼び出し先は同名オーバーロードの`SoundExtend(string, string?)`メソッド(SkinExtend.cs:75)であり、GetSound自身ではない。作業ツリーの未コミットdiff(`git diff -- Extend/SkinExtend.cs`)でも `- GetSound(key.ToLowerInvariant()) ?? new("");` から `+ SoundExtend(key.ToLowerInvariant()) ?? new("");` への変更が確認でき、まさにこの自己再帰バグを直した修正であることが裏付けられる。

#### ✅ SetVSync が更新スレッドから GLFW/GL を叩く

`RayLib/RayLibPlatform.cs:105`

**根拠**: docs/KNOWN-ISSUES.md 17-21行目に『✅ Sleep.Update が更新スレッドから毎フレーム Platform.SetVSync を呼ぶ』として修正済みと明記。実装を追跡すると: Core/AstrumCore.cs:305-329 の Sleep.Update() は直接 Platform.SetVSync を呼ばず、:336-341 の RequestSetVSync 経由で 'AstrumCore.RequestToMainThread(() => AstrumCore.Platform.SetVSync(enabled));' を呼ぶ。Core/Game.cs:277-285 の RequestToMainThread は 'if (Environment.CurrentManagedThreadId == AstrumCore.MainThreadId) { action(); return; } _mainThreadActions.Enqueue(action);' で、メインスレッド以外からの呼び出しはキューに積むだけ。Core/Game.cs:71-88 を見るとこのキューをTryDequeueして実行しているのはメインスレッドのwhileループのみで、更新スレッド本体のUpdateLoop()(:109-123、Sleep.Update()を含むUpdate(game)を呼ぶ側)にはTryDequeueの呼び出しが存在しない。リポジトリ全体でPlatform.SetVSync / AstrumCore.VSync= の呼び出し元をgrepしてもこの経路以外は存在しない。

#### ✅ 唯一の実行可能プロジェクトが起動引数を捨て、例外表示も届かない

`Sandbox/Program.cs:51`

**壊れ方**: 該当なし（現行コードでは引数転送・例外表示とも機能している）。

**根拠**: Program.cs:52-73 の Main は `return GameApp.Run(args, config, () => new SimpleTestGame());` と args をそのまま GameApp.Run に転送している。Game/GameApp.cs:47-98 の Run 内で 55行目 `var options = Startup.Parse(args);`、75行目 `config.Apply(options);` により引数が反映され、78-91行目で `AstrumCore.Boot(...)` を try/catch し、例外を `Console.Error.WriteLine` と `Log.Error(ex, ...)` の両方に出力してから ExitFailure を返す。53行目の `ConsoleBridge.Attach();`（実装は Core/Startup.cs:296-336）が Sandbox.csproj:4 の `<OutputType>WinExe</OutputType>` でも親コンソールに出力が見えるようにしている。

**直し方の方針**: 対応不要。ユーザー提示の『Sandbox の起動口の書き換え（GameApp.Run 経由に変更）』という直近修正がこの指摘を解消済み。

### 成立しないもの（1 件）

読み違いでした。対応不要です。

#### — Dispose が共有の内蔵デフォルトフォントを UnloadFont してしまう

`RayLib/RayLibFont.cs:322`

**根拠**: 現在のコード320〜332行の Dispose() は `UnloadFont(_font);` を無条件に呼んでおり、_font はパス未指定/ロード失敗時に `Raylib.GetFontDefault()`(28,60,77,83,89行)に設定されうるため、「共有デフォルトフォントに対してUnloadFontを呼ぶ経路がある」こと自体はコードとして正しい。git diff HEAD -- RayLib/RayLibFont.cs でもこの Dispose 部分は無変更(HEAD側 git show HEAD:RayLib/RayLibFont.cs の179〜191行でも同一の `UnloadFont(_font);`)であり、直近の一連の修正の対象にもなっていない。ただし raylib本体側にガードがある。本プロジェクトが参照する `RayLib/AstrumLoom.RayLib.csproj:18` の `PackageReference Include="Raylib-cs" Version="7.0.2"` は raylib 5.5 を同梱しており(同梱ネイティブDLL `Debug/runtimes/win-x64/native/raylib.dll` の文字列にも "5.5" を確認)、raylib公式リポジトリの `5.5` タグ `src/rtext.c` の `UnloadFont(Font font)` は次の実装: 「void UnloadFont(Font font) { // NOTE: Make sure font is not default font (fallback) if (font.texture.id != GetFontDefault().texture.id) { UnloadFontData(font.glyphs, font.glyphCount); if (isGpuReady) UnloadTexture(font.texture); RL_FREE(font.recs); TRACELOGD(...); } }」。つまりネイティブ側で「渡されたフォントのテクスチャIDがデフォルトフォントのテクスチャIDと一致する場合は何もしない」ガードが入っており、_font がデフォルトフォントである限り UnloadFont(_font) はテクスチャもglyphsもrecsも解放しないno-opになる。したがって『共有デフォルトフォントが実際に破棄されて他のRayLibFontインスタンスやその後のデフォルトフォント利用が壊れる』という指摘の結論は raylib自身の既存ガードにより成立しない。C#側にガードがない点は事実だが、実害はネイティブ側で防がれている。

---

## 検証して「実際には壊れない」と判断されたもの

理由つきで棄却されています。参考まで。

- `Core/Game.cs:126` GameLock 既定 false のままマルチスレッド更新すると Update と Draw が同じシーンに同時アクセスする
  - The framework-level premise is real, but the "壊れる条件" that makes it a bug is not reachable, and every Core line number cited is wrong. (1) Line numbers: Game.cs の lock は 126/162 ではなく 207-215 (LogicStep) と 231-239 (Draw)、AstrumCore.cs の `public static bool GameLock = false;` は 203 ではなく 250、UpdateLoop は 101-115 ではなく 109-123、描画メインループは 64-80 ではなく 72-88、BaseProgram は 12-26 ではなく 5-27、HandleFatal は 180 ではなく 307 (Draw の catch は 251-253)。Core/Game.cs と Core/AstrumCore.cs は各1つしか存在せず、別バージョンではない。(2) 決定的な反証: 報告の壊れる経路である Sandbox/GameTemp.cs の GameTemplateScene は、プロジェクト全体で一度も new されていない (grep で参照は 7 行目の宣言のみ、Activator.CreateInstance や GetTypes() によるリフレクション登録も無し)。Sandbox/Program.cs:71 が起動するのは SimpleTestGame で、その Enable() (Program.cs:16) が生成するのは TextureDemoScene (Sandbox/Resourses.cs:8)。GameTemp.cs は完全な死にコードなので、_enemies に対する `InvalidOperationException: Collection was modified` は発生し得ない。(3) 実際に走る TextureDemoScene は Update 側で _time と _flash のプロパティ (スカラー) しか触らず (Resourses.cs:48-98)、Draw 側でコレクションを foreach する箇所は無い (_clones を回すループは 108-137 でコメントアウト済み)。よって報告どおりの致命エラー画面には到達しない。
- `Core/Scene.cs:38` Scene.Disable() が Enabled を false にしないので、無効化したシーンが動き続ける
  - コード観察としての「Enabled が false にならない」は事実だが、報告が主張する破綻経路が成立しない。(1) Disable() を呼ぶ engine 側の唯一の箇所は Scene.Change(Scene.cs:129) だが、Change は全ソース(Core/Extend/Game/Sandbox/DXLib/RayLib)を `\bChange\s*\(` で検索しても定義(Scene.cs:125)しかヒットせず、どこからも呼ばれていない。Sandbox/Program.cs も _scene.Disable() を一切呼ばない。つまり Disable() は現状ランタイムで到達しない。(2) 決定的な反証として、仮に Disable() が Enabled=false にしても報告のクラッシュは防げない。`base\.(Update|Draw|Debug|KeyUpdate)\(` の全文検索が repo 全体で0件で、派生シーン(TextureDemoScene.Update:Resourses.cs:48 / Draw:100、SoundDemoScene、GameTemplateScene、SimpleTestGame)はどれも base を呼ばず Enabled も参照しない。基底の `if (!Enabled) return;` は空の基底実装を打ち切るだけで派生の override を止める力が無く、Enabled を直しても _scene?.Update()/_scene?.Draw() は同じように走る。報告の因果(「Enabled が true のままだから Update/Draw が走って落ちる」)は誤り。(3) 加えて Change 後は engine が回すのは Scene.NowScene のみ(AstrumCore.cs:7,12,24-25)なので旧シーンは engine からは叩かれない。設計上の臭い(死んだフラグ、シーンを無効化する手段が無い、~Scene での代入、Enable() に Enabled=true が無い)ではあるが、コードで追える実際の破綻は存在しない。
- `Core/Scene.cs:125` Scene.Change に渡した子シーンが一度も Enable されない（Set + Start とは順序が逆）
  - 行番号と文面上の順序は主張どおり（Change は Core/Scene.cs:125-138、:128 で scene.Enable()、:132-136 で AddChildScene、Enable() は :26-33 で呼び出し時点の ChildScene しか列挙しない）。しかし「実際に壊れる経路」がコード上に存在しない。(1) ChildScene を参照しているのは Scene.cs だけで（AddChildScene/Enable/Disable のみ）、フレームワークは子シーンを Update/Draw/KeyUpdate/Debug/Drag のいずれにも伝播していない。基底 Scene.Draw(:51-54)/Update(:69-72) は空、BaseProgram(AstrumCore.cs:10-26) も NowScene の Update/Draw/Debug しか呼ばない。つまり Enable されない子は Draw もされないので、主張する「直後の Draw で未初期化ハンドル参照」は成立しない。(2) Scene.Change はリポジトリ内のどこからも呼ばれていない（Sandbox は SimpleTestGame のように子シーンを自前フィールドで持ち、手動で Enable/Update/Draw している）。「ボスシーン→リザルトのような child 付き遷移が全部これに当たる」という実例はゼロ。(3) 比較対象の説明も誤り。Set(:112-123) は Enable を一切呼ばず、Start(:124) は別の静的メソッドで、AstrumCore.Boot(AstrumCore.cs:61) の Set の後に GameRunner.Run(Game.cs:41) が起動時に一度だけ呼ぶだけ。ゲーム途中で Scene.Set(scene, child) を呼んでも親も子も Enable されない。よって「Set 経路だけ動き Change 経路だけ壊れている」という非対称は事実でなく、Change 側だけの欠陥とは言えない。
- `Core/Game.cs:123` 入力のバッファ取得（メインスレッド）とエッジ確定（更新スレッド）が分離していて、押下を取りこぼす／二重検出する
  - 構造（Buffer=メインスレッド／Update=更新スレッド）は事実だが、そこから主張される破壊経路がコード上成立しない。(1) 二重検出は不可能：RayLibInput.cs:47 / DxLibInput.cs:26 の `_state[i] = _now[i] ? (_state[i] < 1 ? 1 : 2) : (_state[i] > 0 ? -1 : 0)` はラッチする状態機械で、_now が true のまま Update() が2回走れば 1→2 となり GetKeyDown(==1) は2回目 false。「同じ _state==1 を2回読む」経路は存在しない。(2) 取りこぼしは分離が原因ではない：Buffer() は IsKeyDown / GetHitKeyStateAll というレベル取得で _now を上書きするだけなので、Update() 直前の1サンプルしか効かず、実効サンプリング＝更新レート。単スレッド経路（Game.cs:50-57 の 1:1）と挙動が同一で、更新周期内の押下離鍵が消えるのは通常のポーリング入力共通の性質。(3) 壊れる条件が成立しない：RayLibPlatform.cs:41-42 で Time と UTime の両方に config.TargetFps を入れ、SimpleTime.EndFrame(:161) は TargetFps<=0 で即 return するため、TargetFps=0 では描画も更新も両方無制限になり、更新を60fpsに固定する要素がない（FixedUpdate は既定 false、Sandbox も未設定なので RunLogicSteps は1ループ1ステップ）。加えて行番号もずれており（Game.cs:123 は UpdateLoop の閉じ括弧、KeyInput.Update は :145、MainUpdate は :269）、生入力のエッジ前進を呼ぶのは KeyInput.Update ではなく InputBridge.PreUpdate（Game.cs:134 → InputCapture.cs:91）。
- `Core/AstrumCore.cs:35` AstrumCore.WindowConfig は Boot でしか設定されず、GameHost を直接使うと全域が NullReferenceException
  - 機構としての指摘（WindowConfig が Boot 内の 1 箇所でしか代入されず、GameHost/GameRunner はそこに流さない）はコード上正しいが、「実際に壊れる経路」がリポジトリ内に一本も存在しないため、バグ報告としては成立しない。

【確認できた事実】
- Core/AstrumCore.cs:35 `public static GameConfig WindowConfig { get; private set; } = null!;` は主張どおり。
- 代入は Core/AstrumCore.cs:56 `WindowConfig = config;` の 1 箇所のみ（Boot 内）。`private set` なので外部からの代入も不可能。
- Core/Host.cs:11-22 の GameHost ctor は Config/Platform/Game と Platform.Time.TargetFps しか設定せず、AstrumCore.WindowConfig には流していない。
- Core/Game.cs:30-31 の GameRunner.Run も Platform と MainThreadId だけ再設定し、WindowConfig は触らない。
- したがって仮に WindowConfig が null のまま Loop() に入れば、Core/Game.cs:48 `if (!AstrumCore.MultiThreading)` → Core/AstrumCore.cs:249 `WindowConfig.UseMultiThreadUpdate` で NRE になる、という因果自体は C# の言語仕様上も成立する。

【反証できた点（real=false の根拠）】
1. 壊れる条件の前提が事実と違う。「README や Host.cs が提示する `new GameHost(config, platform, game).Run()` の経路」とあるが、README.md には GameHost / Boot / AstrumCore / Run() のいずれの文字列も存在しない（grep で 0 件、UTF-8 で読めることも確認済み）。Host.cs も型定義があるだけで使用例を提示していない。つまり「そう案内されているのに落ちる」という筋書きは成り立たない。
2. GameHost を構築している箇所はリポジトリ全体で Core/AstrumCore.cs:60 の 1 箇所だけで、それは同 :56 で WindowConfig を代入した直後の Boot 内。唯一の実行エントリである Sandbox/Program.cs:71 も `AstrumCore.Boot(...)` を通る。GameHost/GameRunner を直接使うコードは Core/DXLib/RayLib/Extend/Game/Sandbox のどこにも無い。
3. 報告自身が「実質 Boot 以外の起動経路が存在しない」と認めており、これは「壊れる経路がある」ではなく「public API の設計上の粗さ（GameHost が単体では完結しない）」の指摘に過ぎない。実際に落とすには今存在しないコードを新規に書く必要がある。
4. 行番号が全体的にずれており、報告者が実物を追い切れていない疑いがある（下記 correctedDetail 参照）。
- `Core/Game.cs:42` シングルスレッドモードでは _mainThreadActions が一度も排出されない
  - 構造的な指摘（_mainThreadActions の TryDequeue がマルチスレッド分岐にしか無い）はコード上は正しいが、報告が挙げる「壊れる条件」は実在しない。リポジトリ全体を ToMainThread で大文字小文字無視 grep しても、ヒットは定義（Game.cs:272）と公開ラッパ（AstrumCore.cs:283-284）の3件だけで、呼び出し元がゼロ。特に根拠として挙げられた Core/Host.cs:123 の AsyncLoadableBase の Task.Run 経路は RequestToMainThread を一切呼んでおらず、_bgloadfuncs?.Load() と WriteState(State_Loading) を呼ぶだけ。別スレッドからの破棄は AstrumCore.RequestDispose → _disposeQueue で、こちらはシングルスレッド分岐でも Game.cs:52 の ProcessPendingDisposals で確実に排出される。TimeoutMs で Failed に落ちる話も PumpAsync（Host.cs:182-188）が _asyncState を見ているだけで、このキューとは無関係。行番号もすべてずれている。実際に壊れる経路をコードで追えないため false。
- `Core/Game.cs:69` マルチスレッドモードでもメインスレッド要求は 1 フレームに 1 件しか処理されない
  - コードの形そのものは主張どおりだが、指摘された行番号は両方ずれており、何より「壊れる経路」がこのリポジトリに存在しない。(1) Game.cs:69 は `_updateThread.Start();` であり、`if (_mainThreadActions.TryDequeue(out var action))` は :77。ExtendAction の while は :294（メソッド定義は :291）で :221 ではない（:221 は Draw 内の `platform.Time.BeginFrame();`）。(2) `GameRunner.RequestToMainThread`（Game.cs:272）とその公開ラッパ `AstrumCore.RequestToMainThread`（AstrumCore.cs:283）は、Core/DXLib/RayLib/Extend/Game/Sandbox の全 74 ファイルを grep しても呼び出し元がゼロ。つまり `_mainThreadActions` に要素が入る経路が現状ひとつも無く、「ロード画面で 300 件積む」という前提が成立しない。(3) 実際のメインスレッド委譲は別系統で行われており、そちらはどちらも完全に吸い出している：破棄は `AstrumCore.RequestDispose` → `ProcessPendingDisposals`（AstrumCore.cs:269-280、`while` で全消化、Game.cs:75 から毎フレーム呼ばれる）、ロードは AsyncLoadableBase の `_deferred` フラグ＋`PumpAsync`（Host.cs:118-139, 168-190）でリソース単位に解決される。バックグラウンド処理も UpdateThread と Host.cs:123 の Task.Run 一箇所だけで、後者はアクションを積まず状態フラグを書くだけ。よって実害に到達する経路をコードで追えない。
- `Core/Profiler.cs:45` Profiler の計測状態が全部 static 単一で、複数スレッドから使うと結果が壊れる
  - コードの構造説明は正確だが、「実際に壊れる経路」がリポジトリ内に存在しない。ソリューション全体を grep しても Profiler の呼び出し元は Sandbox/Load.cs の LoadCheckScene.Draw() ただ一箇所（:38-93）だけで、更新スレッド側からの利用はゼロ。Core/Game.cs:64 の UpdateThread が実行するのは AstrumCore.InitDrop() と Update(game)→game.Update() のみで、game.Draw() は Game.cs:234/238 経由でしか呼ばれず、その Draw() を呼ぶのは Loop() のメインスレッド（単スレッド時 :57 / マルチスレッド時 :87）だけ。つまり Profiler に触れるのは常に描画スレッド1本のみで、逐次実行される。報告自身の壊れる条件も「同時に更新スレッド側でも Profiler を使うと」という仮定形であり、その前提を満たすコードが無い。将来マルチスレッド計測をしたら破綻する設計上の弱点（コメント :20 の誇張含む）ではあるが、現状のバグではない。
- `Core/Game.cs:258` 致命エラー画面の描画に catch が無く、二次例外でエラー画面ごと消える
  - The structural observation (no catch in RenderFatalAndClose's try) is correct, but every consequential part of the report fails verification. (1) The cited line numbers match git HEAD, not the working file: in the actual Core/Game.cs the try is at 330-345, DrawFatalMessage at 351-385, platform.Close() at 348, while line 258 is a different try inside Draw()'s finally that DOES have a catch. (2) The claimed "unhandled exception / raw crash" does not exist: Game/GameApp.cs:78-91 wraps AstrumCore.Boot in catch (Exception), logs to stderr, disposes the platform and returns ExitFailure, and both entry points (Sandbox/Program.cs:72) reach Boot only via GameApp.Run. (3) The stated trigger is explicitly guarded: DxLibFont.Measure (DXLib/DxLibFont.cs:46-55) and Draw (:57-63) both check Enable (_handle > 0) and fall back to Drawing.DefaultTextSize / Drawing.DefaultText, and a stale DxLib handle is only an int passed to a P/Invoke that returns an error code, never a managed exception; Drawing.DefaultFont cannot be null either since DxLibGraphics' ctor always assigns CreateFont() => new DxLibFont(spec). No actual throw source inside DrawFatalMessage could be traced. (4) platform.Close() is merely ShouldClose = true (DxLibPlatform.cs:75); the real teardown DxLib_End() still runs through `using var host` in AstrumCore.Boot (Core/AstrumCore.cs:60) during unwinding.
- `Core/Game.cs:172` オーバーレイとログ表示が同じ座標に重なって描画される
  - 主張の核心（両者が同じ (10,10) に重なる）はコードと矛盾します。Overlay の基底 Draw は画面「右上」に描きます。Core/Overlay.cs:42-52 で right = AstrumCore.Width - 10, top = 10 を取り、Drawing.Text(right - pad, top + pad + i * size, text, color, point: ReferencePoint.TopRight) と右寄せ描画しています。Sandbox の 1280x720 なら 1 行目の右端は x≒1262 で、x=10 ではありません。背景帯も Overlay.cs:46 で Drawing.Box(right - width - pad*2, ...) と右側です。ReferencePoint は Drawing.cs:171-176 でフォント描画にそのまま渡されており有効です。さらに Overlay.cs:5-6 のクラスコメントに「画面右上に出ます（左上は Log が使うため）」と明記されており、「レイアウトの調整が無い」どころか意図的な棲み分けです。行番号も外れています：Game.cs:172-174 は RunLogicSteps の可変 dt 分岐（LogicStep(game, wallDelta); return;）で、実際の Overlay.Current.Draw() / Log.Draw() は Game.cs:241-243。Overlay.cs:27 は var platform = AstrumCore.Platform; です。Log 側の記述（Log.cs:65 の x=10,y=10、Log.cs:85 の (0,0) 不透明度 0.5 の帯、Log.cs:90）は正しいものの、相手が右上にいるため重なりません。
- `Core/Text.cs:430` ReadJson が壊れた JSON で例外を投げる（SaveJson は catch しているのに非対称）
  - Line numbers and the code description are accurate (Text.cs:423 File.Exists / :430 unguarded DeserializeObject / :445-454 SaveJson try-catch), but no breaking path exists. ReadJson and SaveJson have zero call sites in the entire solution -- Core/Text.cs is the only .cs file referencing Newtonsoft, and no .cs file contains a .json path. GameConfig is constructed in code (Sandbox/Program.cs:55-67) and overridden by CLI args (GameApp.cs:75 config.Apply), never loaded from or saved to disk, so the "truncated file from a crashed SaveJson kills the next launch" chain has no first link. The claimed fatal path is also wrong: HandleFatal is only reached from UpdateLoop/Update/Draw/MainThreadAction (Game.cs:82,121,153,252); initialization (game.Initialize at Game.cs:39, Scene.Start at :41) is outside any HandleFatal-covered try, so an init-time exception would surface at GameApp.cs:83 as a console error and ExitFailure, not the 6-second fatal screen.
- `Core/Text.cs:30` Read の既定が空行を全部消し、Save は末尾の空行を落とすので往復で内容が変わる
  - コードの記述（11行の removeempty=true、30行の RemoveEmptyEntries、53-55行の「最後の非空要素まで」書き込み）はすべて実物どおりで行番号のズレもない。しかしリポジトリ内に「実際に壊れる経路」が存在しない。Text.Read の呼び出しは3か所だけで、いずれも行番号ではなくキー/接頭辞で意味が決まるパーサ: (1) C:\Users\lavvit\OneDrive\AstrumLoom\Extend\TextConf.cs:105（Load は 113行で `string.IsNullOrWhiteSpace(line)` を自分で continue しており、空行が残っていても同じ結果）、(2) C:\Users\lavvit\OneDrive\AstrumLoom\Extend\Skin.cs:187（"Texture:" 等の接頭辞判定、残りは Configs.Load へ渡され同じく空行スキップ）、(3) C:\Users\lavvit\OneDrive\AstrumLoom\Extend\ExoAnimation.cs:41（exo の `key=value` / `[0]` セクション判定）。報告が名指しした「リプレイ」「譜面/チューニング」は Text.Read を通っておらず、C:\Users\lavvit\OneDrive\AstrumLoom\Core\Debug\InputCapture.cs:309 と C:\Users\lavvit\OneDrive\AstrumLoom\Core\Debug\Tune.cs:135 が File.ReadAllLines を直接使い、しかも自前で空行と # 行を飛ばしている（行番号ではなく hz/seed/end 等のトークンで解釈）。Text.Save の呼び出しも Core\Log.cs:45 / Extend\Skin.cs:295（LoadedSkin.txt）/ Extend\TextConf.cs:140 の3か所で、いずれも Text.Read で読んだリストをそのまま書き戻す往復にはなっていない（TextConf.Save は Items から行を組み立て直す）。よって「行番号の繰り上がり」も「read→save の非可逆な往復」も、現状のコードでは到達できない仮定の話にとどまる。
- `Core/ShapeText.cs:196` グリフ表の遅延初期化が非スレッドセーフで、public な SetGlyph が実体ごと差し替える
  - コードの静的な記述（196-199行の遅延初期化、11行の public SetGlyph、13-14行の Clear→実体差し替え、9-10行の非volatile static、216行の TryGetValue）はすべて行番号どおりで正確。しかし「壊れる条件」が成立しない。(1) Core/Game.cs:46-102 のマルチスレッド分割は厳格で、更新スレッド（UpdateLoop, 109-123行）は Update(game) しか呼ばず、Draw(game) は 87 行のメインスレッドループからのみ呼ばれる。ShapeText.Draw の呼び出し元は FPS.cs:122 ←Sandbox/Overlay.cs:12 ←GameRunner.Draw 内の Overlay.Current.Draw() と Sandbox/Load.cs の描画関数だけで、いずれもメインスレッド。更新スレッドから ShapeText.Draw に到達する経路はエンジン内に存在しない。(2) 仮に更新スレッドから呼んだとしても、ShapeText.Draw の出力は全て Drawing.LineZ → IGraphics.Line → RayLibGraphic.Line の DrawLineEx（RayLib/RayLibGraphic.cs:49-58）という即時描画で、メインスレッドの BeginFrame/EndFrame の外から発行した時点で raylib の描画バッチが壊れる。グリフ表の競合以前に成立しない使い方であり、辞書が原因ではない。(3) SetGlyph() は引数を持たず毎回同一の表を構築するだけで、字形を差し替える手段が存在しない。リポジトリ全体で 198 行以外から呼ばれていない。(4) 言語仕様上も機構の説明が誤り。
- `Core/Texture.cs:197` Expand / DrawSize がロード未完了テクスチャで 0 除算し Scale が Infinity になる
  - 行番号と引用は正確で、無防備な除算が存在すること自体は事実だが、報告が主張する「壊れる経路」は二つの中心的な点で成立しない。(1) トリガーとして挙げる GameConfig.AsyncResourceLoad は Core/Config.cs:43 の定義が repo 内唯一の出現で、どこからも読まれていない完全な死に設定。非同期ロードを分岐させているのは AsyncLoadableBase.IsMainThread (Host.cs:118) であり、これが false になるのは UseMultiThreadUpdate=true (既定 false) のときだけ。既定構成ではメインスレッド上で LoadAsync が _loadfunc() をコンストラクタ内で同期実行する (Host.cs:141-159) ため、new Texture("x.png") から戻った時点で Width は既に正しく、報告の再現手順にある「読み込み完了前のフレーム」自体が存在しない。(2) 報告が冒頭に据える _texture==null のケースは描画に到達できない。全描画が _texture?.Draw(...) の null 条件呼び出しで、_texture はコンストラクタ以外で代入されることが一度もないため、null のまま永久に何も描かれず、Infinity が DrawTexturePro/DrawRotaGraph3F に渡ることはない。加えて Expand は repo 全体で呼び出し元ゼロ、DrawSize の唯一の呼び出し元 (Sandbox/Load.cs:51) では算出したスケールが使われてすらいない。C# の double 除算が例外を投げず Infinity になる点はスクラッチ実行で確認済みだが、それは無害な算術の確認であって描画破綻の証明にはなっていない。
- `Core/Graphic.cs:7` IGraphics.Size が初回の値を永久にキャッシュし、ウィンドウリサイズに追随しない
  - キャッシュの記述自体はコード通りだが、「壊れる条件」に到達する経路が存在しない。(1) GameConfig.Resizable は全ソース中 RayLibPlatform.cs:27-30 の 1 箇所でしか読まれず、しかも !Resizable の側だけで、そこで設定するのは ConfigFlags.UndecoratedWindow（装飾なし）であってサイズ変更可否とは別物。既定の true 側では何も設定されないため、raylib は FLAG_WINDOW_RESIZABLE 無しの固定サイズウィンドウになり、ドラッグ拡大自体ができない。(2) 既定バックエンドである DxLib（Config.cs:58、Sandbox/Program.cs:66）は Resizable を一切読まず、DxLibPlatform.cs:24-58 で SetWindowSizeChangeEnableFlag を呼んでいない（束縛は DxDLL.cs:19846-19851 に定義があるだけの未使用）。実行時に SetWindowSize / SetGraphMode / ToggleFullscreen / SetWindowState(Resizable) を呼ぶ箇所も皆無で、config.Fullscreen も Startup.cs:155-156,219 で解析されるだけでプラットフォームに反映されない。(3) IGraphics.Size の呼び出し元はソリューション全体で AstrumCore.WindowWidth/WindowHeight（AstrumCore.cs:38-39）だけで、その 2 つはどこからも参照されていない（Core/Extend/Game/Sandbox すべて）。よって「右端・下端基準の UI が旧サイズの位置に描かれ続ける」被害も現時点のコードでは発生しえない。潜在的な設計上の弱点ではあるが、実際に壊れる経路は追えない。
- `DXLib/DxLibGraphic.cs:11` Graphics.Size が初回取得値を永久にキャッシュする
  - コードの記述自体は正確だが、現時点で壊れる経路が存在しない。(1) ウィンドウサイズを変える呼び出し（ChangeWindowMode / SetGraphMode / SetWindowSizeExtendRate）は全て DxLibPlatform.cs:27-32、つまり DxLib_Init()（48行目）より前・DxLibGraphics 生成（53行目）より前にしか無く、実行中に変わる経路が無い。SetWindowSizeChangeEnableFlag も DxDLL.cs の宣言以外では呼ばれていないので、ユーザーのドラッグでも変わらない。(2) 被害者として名指しされた AstrumCore.WindowWidth/WindowHeight（AstrumCore.cs:38-39）はリポジトリ全体で呼び出し元がゼロで、IGraphics.Size の消費者も他に無い。「中央揃えの UI がずれる」対象のコードが存在しない。(3) GameConfig.Fullscreen は Startup.cs で解析されるだけで DxLibPlatform は読んでおらず（27行目は無条件の ChangeWindowMode(TRUE)）、フルスクリーン切替は起動時にも実装されていない。(4) RayLibGraphic.cs:19-30 も同じキャッシュをしており、片方の書き忘れではなく意図した実装。報告自身が「将来…実装した時点で」と書いている通り、これは将来のリスクメモであって追跡可能なバグではない。
- `RayLib/RayLibGraphics.cs:136` IGraphics.Text の基準点オフセットの符号が IFont 実装と逆
- `RayLib/RayLibGraphics.cs:79` 枠線の Circle / Oval が thickness を無視する
