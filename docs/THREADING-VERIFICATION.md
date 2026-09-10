# マルチスレッド検証（2026-09-10）

本体の動作は変更せず、tests/ThreadProbeに再実行用の検証を追加した。
現状は条件付きで並行動作するが、5要件すべての保証には至らない。

## 実測

Windows / .NET SDK 10.0.400 / Intel Arc / raylib 5.5 GLFW。
各ケース約4秒、描画60fps、更新のみUTime.TargetFps=0、FixedUpdate=false。
1秒経過後に更新スレッドで状態を公開しThread.Sleep(1500)。
描画は実時間で四角を動かす。各ケースは別プロセス。

|バックエンド|GameLock|更新回数|描画回数|Sleep中の描画|最大描画間隔|
|---|---:|---:|---:|---:|---:|
|DxLib|false|184791|239|89|19.074ms|
|RayLib|false|194734|241|90|17.052ms|
|DxLib|true|183840|150|0|1505.904ms|
|RayLib|true|189036|152|0|1510.866ms|

全ケースで更新スレッドID=4、描画スレッドID=2。致命エラーなし。
ロックなしではSleep中に公開状態を観測した。immutable recordをVolatile.Write/Readで
渡す試験で、検査した2フィールドの不整合は0件。
任意の共有オブジェクトを直接変更してよいという結果ではない。
Sleepを除く約2.5秒で約7.4〜7.8万更新/秒。短時間・軽量処理の測定であり、
長時間の安定性や負荷・メモリ上限は保証しない。表示遅延自体も未測定。

## 要件ごとの判定

1. **描画・処理の分離：確認済み。** UseMultiThreadUpdate=trueで動作する。
   既定はfalse。Hostは更新にもTargetFpsを適用するため、高速更新には
   UTime.TargetFpsの別途設定が必要。VSync切替でも上書きされる。
2. **即時反映：条件付き。** 安全に公開した最新状態を次の描画で読める。
   Wait終了前にも読めるが、60fpsなら次の描画まで待つ。
   ゼロ遅延・すべての途中状態の表示ではない。汎用の状態公開機構は未実装。
3. **超高速処理：今回の試験は成功、無条件の保証は不可。**
   メイン依頼キューは無制限で、Count件分を処理するため、大量投入は
   メモリ増加・描画遅延につながる。ProcessPendingDisposalsとExtendActionは
   空になるまで処理するため、継続投入・再登録で描画へ戻れない可能性がある。
   Scene.ChildSceneはListで、変更と列挙の同時実行も安全ではない。
4. **Wait中の描画：GameLock=falseで確認。** trueでは共通ロックにより停止を再現。
   同期Waitの全種類を試したわけではない。描画と共通ロックを持つ処理や、
   メインに依頼したAction自体のWaitは描画を止める。
   終了時は無期限Joinなので、解除されないWaitは終了も妨げる。
5. **ウィンドウ移動中：実ドラッグは未実測。** DxLibはメインでProcessMessage、
   RayLibはEndDrawing経由でイベント処理する。移動中に通常ループが戻らない場合の
   継続描画フックは本体に見当たらない。GLFWも移動・リサイズでイベント処理が
   ブロックし得ると明記している。更新スレッドの分離だけでは保証できない。
   [GLFW一次資料](https://www.glfw.org/docs/3.3/group__window.html)

## 実装方針

- 更新所有の可変状態と描画用の変更不可状態を分け、完成した状態を
  Volatile/Interlockedで公開する。描画開始時に1回取得する。
  すべての途中更新をキューに積まず最新状態を読む。
- GameLockでUpdate全体を囲まない。Wait前に見せたい状態はWait前に公開する。
- メイン依頼は状態更新なら最新値に集約し、必須命令ならキュー上限・受付制御・
  1描画あたりの処理予算を設ける。Action自体のブロックは予算では防げない。
- 終了には協調キャンセルを用意する。Joinをタイムアウトさせて破棄するだけでは
  更新が利用中のリソースを破棄する問題が残る。
- 移動中描画はバックエンドごとにイベント処理と再入防止を設計する。
  RayLibのEndDrawing中に単純にDrawを再呼び出す方式は避け、
  フレーム開始・終了・イベント処理の分離を含めて検証する。

## 再実行

リポジトリルートで、約4秒後にJSONが出力される。

```powershell
dotnet run --project tests/ThreadProbe -- dxlib
dotnet run --project tests/ThreadProbe -- raylib
dotnet run --project tests/ThreadProbe -- dxlib lock
dotnet run --project tests/ThreadProbe -- raylib lock
```

lockケースの終了コード0は要件達成ではなく比較用の実行完了を意味する。
ビルド成功。既存MovieExtend.cs由来のCS0420警告7件あり。
