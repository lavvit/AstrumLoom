using AstrumLoom;

using SkiaSharp;

namespace Sandbox;

/// <summary>
/// 「内容が変わった時だけSkiaで焼き直し、変わっていなければ既存テクスチャを使い回す」だけのキャッシュ。
///
/// AstrumLoomの RayLibFont.DrawGrad は毎フレーム呼ばれるたびに白文字をオフスクリーンへ焼き直す
/// （RenderTextureのプールはあるが、中身の描画自体は毎回やり直す）。文字列がフレームごとに
/// 変わるわけではない場所（曲タイトル・段位名・メニューラベル等）なら、内容が同じ間は
/// 「テクスチャを1枚貼るだけ」に落とせる。そのための最小限のキャッシュ。
/// </summary>
internal sealed class SkiaTextCache : IDisposable
{
    private string? _lastKey;
    private Texture? _texture;

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>直近の焼き直し1回分の所要時間(ms)。キャッシュを再利用しただけのフレームでは更新しない。</summary>
    public double LastBakeMs { get; private set; } = -1;
    /// <summary>起動してからの焼き直し回数。</summary>
    public int BakeCount { get; private set; }
    /// <summary>直近の焼き直しから何フレーム、キャッシュを再利用し続けているか。</summary>
    public int HitStreak { get; private set; }

    /// <summary>
    /// keyが前回と同じならキャッシュ済みテクスチャをそのまま返す。違えばrenderを呼んで焼き直す。
    /// render は (幅, 高さ, RGBA32ピクセル列) を返す関数。
    /// </summary>
    public Texture? Get(string key, Func<(int width, int height, byte[] rgba)> render)
    {
        if (key == _lastKey && _texture != null)
        {
            HitStreak++;
            return _texture;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (w, h, rgba) = render();
        _texture?.Dispose();
        _texture = new Texture(w, h, rgba);
        Width = w;
        Height = h;
        sw.Stop();

        _lastKey = key;
        LastBakeMs = sw.Elapsed.TotalMilliseconds;
        BakeCount++;
        HitStreak = 0;
        return _texture;
    }

    /// <summary>SKImageから (幅, 高さ, RGBA32) を取り出すヘルパー。render関数の実装側で使う想定。</summary>
    public static (int width, int height, byte[] rgba) Extract(SKImage image)
    {
        using var bitmap = SKBitmap.FromImage(image);
        return (bitmap.Width, bitmap.Height, bitmap.Bytes);
    }

    public void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
    }
}
