# ピクセルの色と透明部分の判定

```csharp
var image = new Texture("Assets/button.png");

// 画像左上を (0, 0) とする元画像のRGBA値。
Color color = image.GetPixel(10, 20);
if (image.TryGetPixel(10, 20, out var pixel))
    Log.Write($"R={pixel.R}, G={pixel.G}, B={pixel.B}, A={pixel.A}");

// Draw と HitTest には同じ描画位置・描画オプションを渡す。
image.Draw(100, 200);
bool hover = image.HitTest(Mouse.X, Mouse.Y, 100, 200);
if (hover && Mouse.Push(MouseButton.Left))
    Log.Write("画像の透明でない部分をクリックしました");

// 薄い影などを無視する場合。元画像のAが32より大きい部分だけ反応。
bool solid = image.HitTest(Mouse.X, Mouse.Y, 100, 200, alphaThreshold: 32);
```

`HitTest` は `Point`、`Position`、正の `Scale` / `XYScale`、`Angle`、`Flip`、`Rectangle` を反映します。`x/y` はマウスと同じ画面座標、`drawX/drawY` は `Draw` と同じ描画座標で、`Drawing.DefaultScale` も考慮します。反転には `Flip` を使ってください。負のScaleはバックエンド間で描画仕様が異なるため判定対象外です。

一時オプションを使う場合は、描画と判定の両方に同じ `DrawOption` を指定します。`DrawRect` は対応する `Rectangle`、`DrawSize` は対応する `Scale` を判定側にも渡してください。描画呼び出し履歴は記録しません。

`TryGetPixel` は範囲外、未ロード、破棄済み、未対応バックエンド、読み出し待ちの場合に `false` を返します。`GetPixel` はその場合に透明色を返すため、透明なピクセルと読み出し待ちを区別するには `TryGetPixel` を使います。透明なピクセルを正常取得した場合は `TryGetPixel` が `true`、`color.A` が `0` です。

初回取得時だけ全画像をCPUへ読み出し、画像1枚につき幅×高さ×4バイトのキャッシュを保持します。更新スレッドからの初回取得はメインスレッドに予約され、完了まで取得できません。判定を初フレームから使う場合は、ロード完了後にメインスレッドで `TryGetPixel(0, 0, out _)` を呼んで準備できます。キャッシュは `Texture.Dispose()` で解放します。

色はテクスチャ自体の値で、描画時の色づけ・不透明度・補間・背景との合成は含みません。DxLibのファイル画像では乗算済みアルファからRGBを戻すため、半透明部分のRGBに丸め誤差が生じる場合があります。焼き込み画像の色はバックエンドに保存された描画結果です。外部ネイティブAPIで画像を書き換えた後のキャッシュ更新には対応しません。

しきい値は0〜254で、元画像のAがしきい値を超えれば反応します。`Opacity` または `Color.A` が0の場合は反応しません。他の画像による遮蔽やブレンド後の見た目は判定しません。

Sandboxの「Texture の見本帳」の5番カードでは、回転する環の不透明部分にマウスを重ねると緑の枠とHIT表示が出ます。環の中央の穴と外側の透明部分には反応しません。
