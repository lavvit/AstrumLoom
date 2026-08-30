using Raylib_cs;

using static AstrumLoom.LayoutUtil;
using static AstrumLoom.RayLib.RayLibGraphics;
using static Raylib_cs.Raylib;

namespace AstrumLoom.RayLib;

/// <summary>
/// ITexture の raylib 実装。AsyncLoadableBase の非同期ロード基盤に乗り、通常のファイルテクスチャに加えて
/// Action(drawAction) を焼き込むRenderTexture（オフスクリーン描画結果を保持するテクスチャ）の両方を扱う。
/// </summary>
internal sealed class RayLibTexture : AsyncLoadableBase, ITexture
{
    public string Path { get; private set; } = "";
    public Texture2D Native { get; private set; }
    public int Width { get; private set; } = 0;
    public int Height { get; private set; } = 0;

    // RenderTexture の所有を持つ場合に保持する
    private (Size size, Action callback)? _renderInfo;
    private RenderTexture2D _renderTex;
    /// <summary>指定サイズのRenderTextureを作成し、callbackで即座に描画してテクスチャとして保持する。</summary>
    public RayLibTexture(int width, int height, Action callback)
    {
        _renderInfo = (new Size(width, height), callback);
        Load();
    }
    /// <summary>指定パスの画像ファイルをテクスチャとして読み込む。</summary>
    public RayLibTexture(string path)
    {
        Path = path;
        Load();
    }
    // メモリ上のエンコード済み画像バイト列（SkiaSharp等で焼いたPNG等）から読み込む場合に使う。
    // 既にメモリにあるのでバックグラウンドでのファイル読み込みは不要。デコード自体は
    // メインスレッド専用（LoadTx）なので、他スレッドから来た場合は Host.cs の _deferred に乗って
    // 次にメインスレッドから来たときへ回る（RenderTexture生成と同じ扱い）。
    private byte[]? _memoryBytes;
    private string? _memoryExt;
    /// <summary>メモリ上のエンコード済み画像バイト列（PNG等）からテクスチャを作る。</summary>
    public RayLibTexture(byte[] data, string ext)
    {
        _memoryBytes = data;
        _memoryExt = ext;
        Load();
    }

    // 生ピクセル経路。PNGのエンコード/デコードを両方すっ飛ばすので、_memoryBytes 経路より速い。
    private byte[]? _rawPixels;
    private int _rawWidth, _rawHeight;
    /// <summary>生のRGBA32ピクセル列（width*height*4バイト）からテクスチャを作る。</summary>
    public RayLibTexture(int width, int height, byte[] rgbaPixels)
    {
        _rawPixels = rgbaPixels;
        _rawWidth = width;
        _rawHeight = height;
        Load();
    }
    ~RayLibTexture() { Dispose(); }

    public void Dispose()
    {
        DisposeAsync(DisposeTx);
        GC.SuppressFinalize(this);
    }
    /// <summary>RenderTexture/通常テクスチャのネイティブリソースを解放する。メインスレッド以外から呼ばれた場合はAstrumCoreにメインスレッドでの破棄を依頼する。</summary>
    private bool DisposeTx()
    {
        if (!Raylib.IsWindowReady())
        {
            Log.Debug($"Texture dispose skipped: window not ready : {Path}");
            // ウィンドウ未準備でもマネージ側は終了扱いにしてファイナライザ再入を避ける
            Native = default;
            _renderTex = default;
            return true;
        }

        // ネイティブ解放はメインスレッドかつウィンドウが有効な時のみ
        if (_renderTex.Id != 0)
        {
            if (IsMainThread)
            {
                try
                {
                    // RenderTexture の解放は内部の Texture も同時に解放される
                    Raylib.UnloadRenderTexture(_renderTex);
                    Native = default;
                    _renderTex = default;
                }
                catch { Log.Error($"Failed to unload render texture: {Path}"); }
            }
            else
                AstrumCore.RequestDispose(this);
            return true;
        }

        if (Native.Id != 0)
        {
            if (IsMainThread)
            {
                try
                {
                    Raylib.UnloadTexture(Native);
                    Native = default;
                    return true;
                }
                catch { Log.Error($"Failed to unload texture: {Path}"); }
            }
            else
            {
                //Log.Debug($"Texture dispose skipped: not main thread : {Path}");
                AstrumCore.RequestDispose(this);
                return true;
            }
        }
        else
        {
            Native = default;
            return true;
        }
        return false;
    }
    #region 読み込み
    /// <summary>RenderTextureへの描画callback実行中、他のコード（RayLibFontのグラデーション描画等）がそこへ描き込めるよう公開する現在の描画先。</summary>
    internal static RenderTexture2D RenderTexture2D { get; private set; }
    /// <summary>非同期ロードを開始する。メインスレッドならLoadTxを即実行、そうでなければLoadBackGroundでバイト列だけ先に読んでおく。</summary>
    public void Load() => LoadAsync(this, LoadTx, LoadBackGround);
    /// <summary>メインスレッドから直接パスまたはRenderTexture生成を行う経路。</summary>
    private bool LoadTx()
    {
        bool file = FileCheck(Path);
        if (_renderInfo == null && !file && _memoryBytes == null && _rawPixels == null)
            return false;

        if (_rawPixels != null)
        {
            // GenImageColorで箱だけ作ってGPUに上げ、その直後に中身を丸ごと差し替える。
            // エンコード/デコードが無いぶん、_memoryBytes（PNG経由）より速い。
            var blank = Raylib.GenImageColor(_rawWidth, _rawHeight, new Raylib_cs.Color(0, 0, 0, 0));
            Native = Raylib.LoadTextureFromImage(blank);
            Raylib.UnloadImage(blank);
            if (Native.Id != 0)
                Raylib.UpdateTexture(Native, _rawPixels);
            _rawPixels = null;
        }
        else if (_renderInfo != null)
        {
            int width = (int)_renderInfo.Value.size.Width;
            int height = (int)_renderInfo.Value.size.Height;
            var callback = _renderInfo.Value.callback;
            if (width <= 0 || height <= 0)
                return false;
            var renderTex = Raylib.LoadRenderTexture(width, height);
            Raylib.BeginTextureMode(renderTex);
            RenderTexture2D = renderTex;
            callback?.Invoke();
            RenderTexture2D = default;
            Raylib.EndTextureMode();
            // RenderTexture を所有する RayLibTexture として返す
            _renderTex = renderTex;
            Native = renderTex.Texture;
        }
        else if (_memoryBytes != null)
        {
            // 既にメモリにあるエンコード済みバイト列（PNG等）をそのままデコードする。
            // ファイル読み込み経路（Pump の _pendingBytes）と同じ raylib API を使うだけで、
            // ディスクI/Oが無い分こちらの方が単純。
            var img = Raylib.LoadImageFromMemory(_memoryExt ?? ".png", _memoryBytes);
            Native = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            _memoryBytes = null; // GPUへ上げ終わったら保持不要
        }
        else
        {
            // PNG/JPG/BMP等そのままOK
            Native = Raylib.LoadTexture(Path);
        }

        // 初期状態をセット
        if (Native.Id == 0)
            return false;

        // サイズ取得
        int w = Native.Width, h = Native.Height;
        Width = w;
        Height = h;

        return true;
    }

    /// <summary>バックグラウンドスレッドから呼ばれる経路。raylibのネイティブAPIはメインスレッド専用なので、ここではファイルをバイト列として読むだけに留める（RenderTexture生成時は対象外）。</summary>
    public bool LoadBackGround()
    {
        bool file = FileCheck(Path);
        bool pathLoad = file && _renderInfo == null;
        if (pathLoad)
        {
            try
            {
                _pendingBytes = File.ReadAllBytes(Path);
                _pendingExt = System.IO.Path.GetExtension(Path).ToLowerInvariant();
            }
            catch
            {
                _pendingBytes = null;
            }
            return true;
        }
        return false;
    }

    /// <summary>毎フレーム呼び出す。バックグラウンドで読み込んだバイト列が届いていれば、ここでTexture2Dへ変換して読み込みを完了させる。</summary>
    public void Pump()
    {
        PumpAsync();
        if (!IsMainThread) return; // メインスレッドでのみ触る

        // 非同期ロードの完了待ち
        if (_pendingBytes != null)
        {
            try
            {
                // PumpAsync() 内の _deferred フォールバックで LoadTx が先に走り、
                // 既に Native が生成されている場合がある。ここで二重にテクスチャを
                // 作ってしまうと古いハンドルが二度と Unload されずリークするので、
                // 上書きする前に解放しておく。
                if (Native.Id != 0)
                    Raylib.UnloadTexture(Native);

                // すべてメインで：バイト列 → Image → Texture2D
                var img = Raylib.LoadImageFromMemory(_pendingExt ?? ".png", _pendingBytes);
                Native = Raylib.LoadTextureFromImage(img);
                Raylib.UnloadImage(img);

                WriteState(State_Success);
            }
            catch { WriteState(State_Failed); }
            finally { _pendingBytes = null; _pendingExt = null; }
            return;
        }

        if (Native.Id > 0 && Width + Height == 0)
        {
            int w = Native.Width, h = Native.Height;
            Width = w;
            Height = h;
        }
    }
    private byte[]? _pendingBytes;
    private string? _pendingExt; // ".png" ".ogg" など

    public bool Enable => LoadFinished && Native.Id > 0;
    public bool IsReady => LoadReady;
    public bool IsFailed => LoadFailed;
    public bool Loaded => LoadFinished;

    #endregion
    /// <summary>
    /// DrawOptions（切り出し矩形/基準点/スケール/回転/反転/色/ブレンド）を反映してテクスチャを描画する。
    /// RenderTexture由来のテクスチャはOpenGLのUV原点がファイルテクスチャと上下逆になるため、その場合だけ src矩形を反転して補正する。
    /// </summary>
    public void Draw(double x, double y, DrawOptions option)
    {
        if (!Enable) return;
        var use = option;
        SetOptions(use);
        (double width, double height) = use.Rectangle.HasValue
            ? (use.Rectangle.Value.Width, use.Rectangle.Value.Height)
            : (Width, Height);

        var point = use.Position ?? (GetAnchorOffset(use.Point, width, height) * -1);
        double opacity = Math.Clamp(use.Opacity, 0.0, 1.0);
        var color = use.Color ?? Color.White;
        float defscale = (float)Drawing.DefaultScale;
        float fx = (float)(x * defscale);
        float fy = (float)(y * defscale);
        (double w, double h) = use.Scale;
        double angle = use.Angle;
        int tx = use.Flip.X ? -1 : 1;

        // ★ 宛先座標系での origin（拡大後の量に変換）
        var origin = new System.Numerics.Vector2(
            x: (float)(point.X * Math.Abs(w)),
            y: (float)(point.Y * Math.Abs(h))
        );

        var rect = use.Rectangle ?? new(0, 0, Width, Height);
        // RenderTexture経由はUV原点が上下逆なので、その分の反転とFlip.Yの反転は
        // 独立に2回適用せず、実効的に反転させるかどうかをXORでまとめて1回だけ決める。
        bool netFlipY = use.Flip.Y ^ (_renderTex.Id != 0);
        int netTy = netFlipY ? -1 : 1;
        // src は TurnX/TurnY で反転（幅/高さを負にする）
        var srcRect = new Rectangle(
            (float)rect.X, (float)rect.Y,
            (float)rect.Width * tx,
            (float)rect.Height * netTy
        );

        // 宛先サイズ（常に正）※拡大後の大きさ
        float destW = (float)(rect.Width * Math.Abs(w));
        float destH = (float)(rect.Height * Math.Abs(h));
        // dst は (x,y) を「アンカー位置」として渡す
        var dstRect = new Rectangle(fx, fy, destW, destH);

        DrawTexturePro(Native, srcRect, dstRect, origin,
            360 * (float)angle, ToRayColor(color, opacity));

        ResetOptions(use);
    }
}
