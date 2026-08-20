using Raylib_cs;

using static AstrumLoom.LayoutUtil;
using static AstrumLoom.RayLib.RayLibGraphics;
using static Raylib_cs.Raylib;

namespace AstrumLoom.RayLib;

internal sealed class RayLibTexture : AsyncLoadableBase, ITexture
{
    public string Path { get; private set; } = "";
    public Texture2D Native { get; private set; }
    public int Width { get; private set; } = 0;
    public int Height { get; private set; } = 0;

    // RenderTexture の所有を持つ場合に保持する
    private (Size size, Action callback)? _renderInfo;
    private RenderTexture2D _renderTex;
    public RayLibTexture(int width, int height, Action callback)
    {
        _renderInfo = (new Size(width, height), callback);
        Load();
    }
    public RayLibTexture(string path)
    {
        Path = path;
        Load();
    }
    ~RayLibTexture() { Dispose(); }

    public void Dispose()
    {
        DisposeAsync(DisposeTx);
        GC.SuppressFinalize(this);
    }
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
    internal static RenderTexture2D RenderTexture2D { get; private set; }
    public void Load() => LoadAsync(this, LoadTx, LoadBackGround);
    private bool LoadTx()
    {
        bool file = FileCheck(Path);
        if (_renderInfo == null && !file)
            return false;

        bool pathLoad = file && _renderInfo == null;
        if (_renderInfo != null)
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

    public void Pump()
    {
        PumpAsync();
        if (!IsMainThread) return; // メインスレッドでのみ触る

        // 非同期ロードの完了待ち
        if (_pendingBytes != null)
        {
            try
            {
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
        (int tx, int ty) = (use.Flip.X ? -1 : 1, use.Flip.Y ? -1 : 1);

        // ★ 宛先座標系での origin（拡大後の量に変換）
        var origin = new System.Numerics.Vector2(
            x: (float)(point.X * Math.Abs(w)),
            y: (float)(point.Y * Math.Abs(h))
        );

        var rect = use.Rectangle ?? new(0, 0, Width, Height);
        // src は TurnX/TurnY で反転（幅/高さを負にする）
        var srcRect = new Rectangle(
            (float)rect.X, (float)rect.Y,
            (float)rect.Width * tx,
            (float)rect.Height * ty
        );

        // Render経由の場合上下反転するので補正
        if (_renderTex.Id != 0)
        {
            srcRect.Y += srcRect.Height;
            srcRect.Height *= -1;
        }

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
