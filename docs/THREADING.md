# 描画と更新を分離する

描画・ウィンドウ操作はメインスレッド、ゲーム処理は更新スレッドで行います。
描画へ渡す状態は `RenderState<T>` で公開してください。

```csharp
var config = new GameConfig
{
    UseMultiThreadUpdate = true,
    TargetFps = 60,             // 描画
    UpdateTargetFps = 0,        // 更新ループは無制限（VSyncから独立）
    FixedUpdate = false,       // trueなら論理更新はFixedUpdateHzで進む
};
```

`UpdateTargetFps` の既定は0です。以前の「描画と同じ上限」にする場合は、
明示的に60などを設定します。固定更新なら、更新ループが速くても論理ステップが
来るまでゲームのUpdateは実行されません。

## 状態の公開

```csharp
sealed record ViewState(double X, double Y, string Message);

readonly RenderState<ViewState> view = new(new(0, 0, "開始"));

// 更新スレッド
void UpdateView(double x, double y)
{
    view.Publish(new(x, y, "処理中"));
    Task.Delay(1000, AstrumCore.UpdateCancellation).GetAwaiter().GetResult();
}

// メインスレッドのDrawから
void DrawView()
{
    var state = view.Read(); // 1フレームにつき1回取得
    Drawing.Box(state.X, state.Y, 30, 30, Color.Cyan);
    Drawing.Text(10, 10, state.Message, Color.White);
}
```

公開はロック待ちをせず、保留状態も積みません。描画は次のフレームで最新状態を読みます。
Wait前に公開すれば、Waitが終わる前から反映できます。途中の全更新を表示する方式ではありません。
公開後のオブジェクトとその配下（配列・リスト等）は変更しないでください。
描画が参照するGPUリソースの寿命も、ゲーム側で保つ必要があります。

`Scene` の任意のフィールドや `ChildScene` を直接並行変更するコードを、自動で
安全にするものではありません。`GameLock=true` は互換用の共通ロックなので、
Update内で待つとDrawも待ちます。独立描画ではfalseにして状態公開方式を使用します。

## メインスレッドへ依頼する

- `TryRequestToMainThread(action)`：FIFO。満杯ならfalse。
- `TryRequestLatestToMainThread(key, action)`：同じキーの未実行依頼を最新値へ置換。
  新しいキーを追加できなければfalse。必要な更新は次のUpdate等で再試行してください。
- `TryRequestDispose(resource)`：同じインスタンスの保留依頼を集約。満杯ならfalse。
  falseの場合、破棄の責任と所有権は呼び出し側に残ります。

既存の `RequestToMainThread` は満杯時に例外を投げ、依頼を黙って破棄しません。
`RequestDispose` は既存のファイナライザーとの互換性のため上限を超えても受け付けます。
大量にリソースを生成するコードは `TryRequestDispose` で受付を制御してください。
既にメインスレッド上の通常・最新値依頼は、その場で実行します。

`MainThreadQueueCapacity`（既定4096）は通常依頼、開始フック、終了フック、
Try経由の破棄依頼の**各キュー**の上限です。フックは同じキーの最初の登録を保持します。
自己再登録したフックは同じフレームで無限に実行されません。

処理量は各キューにつき `MainThreadActionsPerFrame`（既定128件）と
`MainThreadActionBudgetMs`（既定2ms）で制限します。
予算はActionの合間で判定するため、1件の長いActionを中断することはできません。
メインスレッドに重い処理やWaitを依頼しないでください。

## 終了・ウィンドウ移動

長い待機には `AstrumCore.UpdateCancellation` を渡します。終了要求でキャンセルし、
更新スレッドを終了させてからシーン・GPU等を破棄します。終了待ちの間もメイン依頼を処理します。
キャンセルコールバック自身は短時間で戻る必要があります。
トークンを受け取らない無期限Waitや無限ループを、安全に強制中断する機能ではありません。

Windowsでは移動・サイズ変更モードのタイマーから、メインスレッド上で描画を続けます。
描画中・描画用の依頼処理中の再入を防いでいます。OSタイマーの精度に依存するため、
移動中も厳密に指定FPSで動く保証はありません。単一スレッド構成では移動中はDrawのみ進みます。

RayLibではバッチの送出・バッファ交換とイベント処理を分けました。
フレーム制御はAstrumLoomが担当するため、ネイティブの `EndDrawing` / `GetFrameTime` /
`GetFPS` やraylib独自の自動録画機能を混在させないでください。
時間・FPS・スクリーンショットにはAstrumLoomのAPIを使用します。

## 検証

```powershell
dotnet run --project tests/ThreadingChecks
dotnet build tests/ThreadProbe
./tests/ThreadProbe/run.ps1
```

独立テストは上限・FIFO・最新値集約・複数producer・状態の一貫性を検査します。
実機テストは両バックエンドのキュー飽和、Waitとキャンセル、破棄、移動モード、
30秒の連続負荷を検査し、各プロセス45秒のタイムアウトとJSONログを残します。
移動試験はSC_MOVEによるWindowsの実際のモーダルループを使用します。
手によるタイトルバーのドラッグ全操作を自動化したものではありません。
