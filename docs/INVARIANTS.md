# 壊してはいけない決まりごと

ゲームループ・入力・スレッドまわりには、順番を入れ替えると静かに壊れる箇所があります。
実際に踏んで直したものだけを書いてあります。触る前に読んでください。

---

## 1. 1 ループの中の順番

`GameRunner.Update`（`Core/Game.cs`）の並びには理由があります。

```
1. platform.UTime.BeginFrame()
2. Sleep.Update()
3. _inputBridge.PreUpdate()          生入力を 1 フレーム進める
4. DebugControl.PollHotkeys()        F1〜F6 の判定
5. DebugControl.ShouldRunUpdate()    一時停止・スローの判定（1 ループ 1 回）
6. InputCapture.BeginFrame(frame)    再生の状態をこのフレームに合わせる
7. KeyInput.Update() / Mouse.Update() / Pad.Update()   入力の確定
8. InputCapture.EndFrame(frame)      記録に確定させる
9. RunLogicSteps()                   game.Update() を 1 回以上
```

守るべき点:

- **3 は 4 より前**。生入力を進めてからでないとホットキーのエッジが取れません。
  逆に言うと、ホットキーが一時停止中でも入力再生中でも効くのはこの順番のおかげです。
- **6 は 7 より前**。再生はフレーム番号で状態を引くので、入力を確定させる前にシークが要ります。
- **8 は 7 より後**。ここを 6 と同じ場所に置くと、記録に「1 フレーム前の入力」が
  現在のフレーム番号で書き込まれ、再生が 1 フレームずれます。実際にこれを踏みました。
- **5 は 1 ループにつき 1 回だけ**。ここを論理ステップごとに呼ぶと、キャッチアップで
  複数ステップ走ったときにスローの間引きが二重にかかります。
- **7 も 1 ループにつき 1 回だけ**。`IInput.Update` / `IMouse.Update` はエッジ（押した瞬間・
  離した瞬間）を進めるので、1 ループで 2 回呼ぶと押下を取りこぼします。
  キャッチアップで複数ステップ走る場合、それらは**同じ入力を共有します**。

`InputBridge.Update()` が `_inner.Update()` を呼ばないのも同じ理由です。
生入力を進めるのは `PreUpdate()` だけ、という役割分担になっています。

---

## 2. スレッドの持ち分

`GameConfig.UseMultiThreadUpdate` が true のとき、2 本のスレッドが走ります。

| | メインスレッド | 更新スレッド |
| --- | --- | --- |
| 担当 | `PollEvents` / `Draw` | `Update` |
| 触ってよいもの | 描画 API、ウィンドウ API | ゲームの状態 |

決まりごと:

- **ウィンドウ API と GL/DirectX の呼び出しはメインスレッドから**。
  更新スレッドから触りたくなったら `AstrumCore.RequestToMainThread(action)` に積みます。
- **リソースの破棄もメインスレッド**。別スレッドから `Dispose` したい場合は
  `AstrumCore.RequestDispose(disposable)` に積むと、次のループの頭で処理されます。
  `AsyncLoadableBase.DisposeAsync` はこれを自動でやります。
- **スクリーンショットの保存は描画フレームの中**。`Snapshot.Request` はどのスレッドからでも
  出せますが、実際の保存は `DebugSession.OnDrawFrame` から行われます。
- **複数スレッドから触る静的コレクションはロックする**。`Log.LogMessages` と
  `FpsCounter._times` はこれを忘れていて、列挙中の変更で例外 → 致命エラー画面に落ちていました。

`--selftest` / `--record` / `--replay` は、この面倒を避けるために単一スレッドへ強制的に倒します。

---

## 3. 再現性の前提

入力の再生が 1 ドットも狂わないのは、次の 3 つが同時に成り立っているからです。

1. **ロックステップ**（`GameConfig.LockStep`）。1 ループにつき必ず 1 論理フレーム。実時間を見ない。
2. **単一スレッド更新**。更新と描画の相対的な回数が固定される。
3. **dt が定数**。`AstrumCore.DeltaTime` は常に `1 / FixedUpdateHz`。

どれか 1 つでも崩すと再生はずれます。`Startup.Apply` がこの 3 つをまとめて設定しているので、
再現性が要るモードを増やすときは `LaunchOptions.DeterministicMode` に足してください。

ゲーム側にも条件があります。

- **実時間を直接読まない**。`DateTime.Now` や `Stopwatch` でゲームロジックを進めると再生がずれます。
  時間は `AstrumCore.DeltaTime` と `AstrumCore.FrameCount` から取ってください。
- **乱数は `Randomize` を通す**。`--seed` を指定したときに揃うのはこれだけです。
- 描画だけで使う `DateTime.Now`（時計表示など）は構いませんが、スクリーンショットの
  比較をするなら画面から外してください。

---

## 4. 合成入力のタイミング

`VirtualInput.Press(key)` の効果は、**次のフレームの入力確定から**反映されます。
上の「1 ループの中の順番」で言うと、セルフテストが動くのは 9 のさらに後だからです。

```csharp
SelfTest.Do("押す", () => VirtualInput.Press(Key.Right));
SelfTest.Wait(2);                      // ← これが要る
SelfTest.Check("届いた", () => Key.Right.Hold());
```

`Wait` を省くと、押した直後の `Check` はまだ false を見ます。

---

## 5. 文字コード

- **`.cs` と `.ps1` は UTF-8 BOM 付きで保存すること。** BOM が無いと、Windows PowerShell 5.1 が
  ANSI として読んで日本語が壊れ、`.ps1` は構文エラーになります。`.cs` はコメントが化けます。
- `.bat` は ASCII のみ。
- ゲームからコンソールへ出す文字列の符号化は `ConsoleBridge` が決めています。
  リダイレクトされていれば UTF-8、本物のコンソールならそのコードページ。
  ここで `Console.OutputEncoding` を書き換えると**親シェルのコードページまで変わる**ので触らないこと。

---

## 6. スクリーンショットが撮れる条件

- `IGraphics.SaveScreenshot` は `BeginFrame` と `EndFrame` の間から呼ぶこと。
  DxLib は裏画面を保存対象にする必要があり、Raylib はフレームバッファを読みます。
- Raylib 側で `Raylib.TakeScreenshot` を使ってはいけません。保存先が作業ディレクトリ基準に
  なってしまうので、`LoadImageFromScreen` + `ExportImage` で自分でパスを指定しています。
- 保存はオーバーレイとログを描いたあとに行われます。画面に見えているものがそのまま撮れます。

---

## 関連

- 新規ゲームの作り方は [WORKFLOW.md](WORKFLOW.md)
- 引数とホットキーの一覧は [DEBUG.md](DEBUG.md)
